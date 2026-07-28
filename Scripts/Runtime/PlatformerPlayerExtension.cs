/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using UnityEngine;

namespace PuzzleBox
{
    /// <summary>
    /// Base class for all player extensions. Attach subclasses as additional components
    /// on the same GameObject as PlatformerPlayer2D.
    /// <para>
    /// プレーヤー拡張の基底クラス。PlatformerPlayer2Dと同じGameObjectにサブクラスを追加コンポーネントとしてアタッチしてください。
    /// </para>
    /// <example>
    /// <code>
    /// public class DustOnLanding : PlatformerPlayerExtension
    /// {
    ///     public GameObject dustPrefab;
    ///     protected override void OnPlayerLanded()
    ///     {
    ///         Instantiate(dustPrefab, player.position, Quaternion.identity);
    ///     }
    /// }
    /// </code>
    /// </example>
    /// </summary>
    [RequireComponent(typeof(PlatformerPlayer2D))]
    public class PlatformerPlayerExtension : MonoBehaviour
    {
        /// <summary>
        /// Reference to the PlatformerPlayer2D on this GameObject.
        /// Cached in Awake. Available from Start onwards.
        /// <para>このGameObject上のPlatformerPlayer2Dへの参照。Awakeでキャッシュされ、Start以降で利用可能。</para>
        /// </summary>
        protected PlatformerPlayer2D player { get; private set; }

        // ------------------------------------------------------------------
        // Lifecycle / ライフサイクル
        // ------------------------------------------------------------------

        protected virtual void Awake()
        {
            player = GetComponent<PlatformerPlayer2D>();
        }

        protected virtual void OnEnable()
        {
            if (player != null)
            {
                player.OnJumped += HandleJumped;
                player.OnLanded += HandleLanded;
                player.OnDied += HandleDied;
                player.OnDestroyed += HandleDestroyed;
                player.OnInputEnabledChanged += HandleInputEnabledChanged;
            }
        }

        protected virtual void OnDisable()
        {
            if (player != null)
            {
                player.OnJumped -= HandleJumped;
                player.OnLanded -= HandleLanded;
                player.OnDied -= HandleDied;
                player.OnDestroyed -= HandleDestroyed;
                player.OnInputEnabledChanged -= HandleInputEnabledChanged;
            }
        }

        // ------------------------------------------------------------------
        // Event wrappers (private → virtual hooks)
        // イベントラッパー（private → 仮想フック）
        // ------------------------------------------------------------------

        private void HandleJumped() => OnPlayerJumped();
        private void HandleLanded() => OnPlayerLanded();
        private void HandleDied() => OnPlayerDied();
        private void HandleDestroyed() => OnPlayerDestroyed();
        private void HandleInputEnabledChanged(bool enabled) => OnPlayerInputEnabledChanged(enabled);

        // ------------------------------------------------------------------
        // Virtual hooks — override these in subclasses
        // 仮想フック — サブクラスでオーバーライドしてください
        // ------------------------------------------------------------------

        /// <summary>Called when the player executes a jump. / ジャンプ実行時に呼ばれる。</summary>
        protected virtual void OnPlayerJumped() { }

        /// <summary>Called when the player lands on the ground. / 着地時に呼ばれる。</summary>
        protected virtual void OnPlayerLanded() { }

        /// <summary>Called when Kill() is invoked on the player. / Kill()呼び出し時に呼ばれる。</summary>
        protected virtual void OnPlayerDied() { }

        /// <summary>Called just before the player GameObject is destroyed. / プレーヤーGameObject破棄直前に呼ばれる。</summary>
        protected virtual void OnPlayerDestroyed() { }

        /// <summary>
        /// Called when player input is enabled or disabled (e.g., during death sequence).
        /// <para>プレーヤー入力が有効・無効に切り替わった時に呼ばれる（例：死亡シーケンス中）。</para>
        /// </summary>
        protected virtual void OnPlayerInputEnabledChanged(bool enabled) { }

        // ------------------------------------------------------------------
        // SendMessage receivers — override these in subclasses
        // SendMessage受信 — サブクラスでオーバーライドしてください
        // ------------------------------------------------------------------

        /// <summary>Received via SendMessage when the player jumps. / ジャンプ時にSendMessageで受信。</summary>
        protected virtual void DidJump() { }

        /// <summary>Received via SendMessage when the player dashes. / ダッシュ時にSendMessageで受信。</summary>
        protected virtual void DidDash() { }

        /// <summary>Received via SendMessage when the player lands. / 着地時にSendMessageで受信。</summary>
        protected virtual void DidLand() { }

        /// <summary>Received via SendMessage when Kill() is called. / Kill()呼び出し時にSendMessageで受信。</summary>
        protected virtual void WasKilled() { }

        /// <summary>Received via SendMessage when the player is about to be destroyed. / 破棄直前にSendMessageで受信。</summary>
        protected virtual void WasDestroyed() { }

        // ------------------------------------------------------------------
        // Contact receivers (from KinematicMotion2D SendMessage)
        // 接触受信（KinematicMotion2DのSendMessageから）
        // ------------------------------------------------------------------

        /// <summary>
        /// Called when a new contact is detected.
        /// <para>新しい接触が検出された時に呼ばれる。</para>
        /// </summary>
        protected virtual void OnContactEnter(KinematicMotion2D.Contact contact) { }

        /// <summary>
        /// Called when an existing contact is lost.
        /// <para>既存の接触が失われた時に呼ばれる。</para>
        /// </summary>
        protected virtual void OnContactExit(KinematicMotion2D.Contact contact) { }

        /// <summary>
        /// Called every physics frame while a contact persists.
        /// <para>接触が継続している間、毎物理フレームで呼ばれる。</para>
        /// </summary>
        protected virtual void OnContactStay(KinematicMotion2D.Contact contact) { }
    }
}
