/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PuzzleBox
{
    /**
     * Unityのインプットシステムは、入力の種類によって異なるオブジェクト型で値を渡してきます。
     * このクラスはそれらの異なる型（UnityEngine.InputSystem.InputValue、
     * InputAction.CallbackContext、および汎用的なobject型）を一つにまとめるアダプターです。
     *
     * これにより、入力を受け取る側のコードは、値の由来を気にせず
     * 統一された方法（IsPressed、Get<T>など）で入力値を読み取ることができます。
     * SendMessageで渡されたobject型の入力値も、このクラスを通じて安全に扱えます。
     */
    public class InputValue
    {
        private UnityEngine.InputSystem.InputValue _value;
        private InputAction.CallbackContext _context;
        private object _object;

        public static implicit operator InputValue(UnityEngine.InputSystem.InputValue value)
        {
            return new InputValue(value);
        }

        public static bool IsPressed(object obj)
        {
            if (obj != null)
            {
                if (obj is InputValue)
                {
                    return ((InputValue)obj).isPressed;
                }
                else if (obj is UnityEngine.InputSystem.InputValue)
                {
                    return ((UnityEngine.InputSystem.InputValue)obj).isPressed;
                }
            }
            return false;
        }

        public static object GetObject(object obj)
        {
            if (obj != null)
            {
                if (obj is InputValue)
                {
                    return ((InputValue)obj).Get();
                }
                else if (obj is UnityEngine.InputSystem.InputValue)
                {
                    return ((UnityEngine.InputSystem.InputValue)obj).Get();
                }
            }
            return null;
        }

        public static TValue GetValue<TValue>(object obj) where TValue : struct
        {
            if (obj != null)
            {
                if (obj is InputValue)
                {
                    return ((InputValue)obj).Get<TValue>();
                }
                else if (obj is UnityEngine.InputSystem.InputValue)
                {
                    return ((UnityEngine.InputSystem.InputValue)obj).Get<TValue>();
                }
            }
            return default(TValue);
        }

        public InputValue(UnityEngine.InputSystem.InputValue value)
        {
            _value = value;
        }

        public InputValue(InputAction.CallbackContext context)
        {
            _context = context;
        }

        public InputValue(object obj)
        {
            _object = obj;
        }

        public object Get()
        {
            return _value != null ? _value.Get() :
                (_object == null ? _context.ReadValueAsObject() : _object);
        }

        public TValue Get<TValue>() where TValue : struct
        {
            if (_value != null)
            {
                return _value.Get<TValue>();
            }
            else if (_object != null && _object is TValue)
            {
                return (TValue)_object;
            }
            else
            {
                return _context.ReadValue<TValue>();
            }
        }

        public bool isPressed
        {
            get
            {
                if (_value != null)
                {
                    return _value.isPressed;
                }
                else if (_object != null && _object is bool)
                {
                    return (bool)_object;
                }
                else
                {
                    return _context.ReadValueAsButton();
                }
            }
        }
    }

    /**
     * UnityのPlayerInputコンポーネントは、標準では同じGameObjectにある
     * コンポーネントにしか入力を届けることができません。
     *
     * このクラスはその制限を解消するためのヘルパーです。
     * PlayerInputが受け取った入力アクション（例：「Jump」「Move」など）を、
     * インスペクターで指定した任意のGameObjectへSendMessageまたはBroadcastMessageで
     * 転送します。これにより、入力コンポーネントとプレーヤーのロジックを
     * 別々のGameObjectに分けて設計することができます。
     *
     * 例えば「Jump」アクションが発火すると、受信先の「OnJump(object)」
     * メソッドを自動的に呼び出します。
     */
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerInputHelper : MonoBehaviour
    {
        public enum NotificationMode
        {
            SendMessages,
            BroadcastMessages
        }

        private static Dictionary<Guid, string> messageNames = new Dictionary<Guid, string>();

        public GameObject[] targets;
        public NotificationMode behavior = NotificationMode.SendMessages;

        PlayerInput playerInput;

        void Awake()
        {
            playerInput = GetComponent<PlayerInput>();
            playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
        }

        private void OnEnable()
        {
            playerInput.onActionTriggered += HandleAction;
        }

        private void OnDisable()
        {
            playerInput.onActionTriggered -= HandleAction;
        }

        private string MakeMethodName(string actionName)
        {
            if (!string.IsNullOrEmpty(actionName))
            {
                if (actionName.Length > 1)
                {
                    return "On" + char.ToUpper(actionName[0]) + actionName.Substring(1);
                }
                else
                {
                    return actionName.ToUpper();
                }
            }
            return string.Empty;
        }


        void HandleAction(InputAction.CallbackContext context)
        {
            if (context.phase == InputActionPhase.Performed || (context.canceled && context.action.type == InputActionType.Value))
            {
                InputValue inputValue = new InputValue(context);


                if (!messageNames.ContainsKey(context.action.id))
                {
                    // Capitalize
                    messageNames[context.action.id] = MakeMethodName(context.action.name);
                }

                string messageName = messageNames[context.action.id];

                foreach(GameObject target in targets)
                {
                    if (target != null)
                    {
                        if (behavior == NotificationMode.SendMessages)
                        {
                            target.SendMessage(messageName, inputValue, SendMessageOptions.DontRequireReceiver);
                        }
                        else
                        {
                            target.BroadcastMessage(messageName, inputValue, SendMessageOptions.DontRequireReceiver);
                        }
                    }
                }
            }
        }
    }
}

