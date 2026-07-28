/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

 
using UnityEngine;

namespace PuzzleBox
{
    namespace Utils
    {
        /**
         * このクラスは値を一定時間だけ記憶しておくための汎用バッファです。
         * プラットフォームゲームのジャンプ入力バッファリングなど、
         * 「少し前に押したボタンを有効にしたい」という処理に使えます。
         * 
         * 使い方の例：
         * 
         * // 0.05秒間バッファリングする。
         * Utils.BufferedInput<bool> jumpInput = new Utils.BufferedInput<bool>(0.05f);
         * 
         * // 値をセットする
         * bool val = true;
         * jumpInput.Set(val);
         * 
         * // 値を読み取る
         * if (jumpInput.HasValue()) {
         *     val = jumpInput.Get();
         * }
         * 
         */
        public class BufferedInput<T>
        {
            public float duration;

            T _value;
            float _time;


            public BufferedInput(float duration)
            {
                this.duration = duration;
                this._time = float.NegativeInfinity;
            }

            public bool HasValue()
            {
                return Time.time - _time <= duration;
            }


            public void Set(T value) 
            { 
                _value = value;
                _time = Time.time;
            }

            public void Set(T value, float duration)
            {
                Set(value);
                this.duration = duration;
            }

            public T Get()
            {
                return _value;
            }

            public void Reset()
            {
                _time = float.NegativeInfinity;
            }

        }

    }
}
