/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
 
using System;

namespace PuzzleBox
{
    namespace Utils
    {
        /**
         * 汎用のカウントダウンタイマークラスです。
         * ダッシュの持続時間、クールダウン、壁つかみの待機時間など、
         * 「一定時間後に何かをしたい」という処理に使います。
         *
         * 【基本的な使い方】
         *   1. Timer を生成する：  Utils.Timer timer = new Utils.Timer();
         *   2. 毎フレーム Tick を呼ぶ：  timer.Tick(Time.deltaTime);
         *   3. タイマーを開始する：  timer.Start(2.0f);  // 2秒間
         *   4. 完了時の処理を登録する：  timer.OnComplete += () => {  処理  };
         *
         * 【主なプロパティ】
         *   - timeLeft   : 残り時間（秒）
         *   - isFinished : タイマーが終了したか
         *   - phase      : 経過割合（0.0 = 開始直後、1.0 = 終了）。アニメーションの補間などに使えます。
         *   - active     : falseにするとTick中でも時間が進みません（一時停止）。
         *
         * 【コールバック】
         *   - OnStart    : Start() が呼ばれた時
         *   - OnComplete : 時間が0になった時（自然に終了）
         *   - OnCancel   : Cancel() が呼ばれた時
         *   - OnEnd      : OnComplete または OnCancel の直後、必ず呼ばれます。
         *
         * 【Start() の挙動に注意】
         *   Start(time) は「現在の残り時間より長い場合のみ延長する」という動作をします。
         *   例えばタイマーが残り3秒の時に Start(1.0f) を呼んでも、残り時間は3秒のままです。
         *   タイマーを確実に新しい時間で再スタートしたい場合は、Reset(time) を使ってください。
         *   Reset(time) は現在の残り時間に関わらず、無条件で指定した時間にセットします。
         */
        public class Timer
        {
            public bool active = true;
            public float timeLeft { private set; get; }
            public float totalTime { private set; get; }

            public float timeElapsed { get { return totalTime - timeLeft; } }

            public Action OnComplete;
            public Action OnEnd;
            public Action OnStart;
            public Action OnCancel;

            public bool isFinished
            {
                get
                {
                    return timeLeft <= 0;
                }
            }

            public float phase
            {
                get
                {
                    if (totalTime > 0)
                    {
                        return 1f - timeLeft / totalTime;
                    }
                    else
                    {
                        return 0;
                    }
                }
            }


            public void Tick(float dt)
            {
                if (timeLeft > 0 && active)
                {
                    timeLeft -= dt;
                    if (timeLeft <= 0)
                    {
                        timeLeft = 0;
                        OnComplete?.Invoke();
                        OnEnd?.Invoke();
                    }
                }
            }

            public void Start(float time)
            {
                if (time > 0)
                {
                    if (time > timeLeft)
                    {
                        timeLeft = time;
                    }

                    if (time > totalTime)
                    {
                        totalTime = time;
                    }

                    OnStart?.Invoke();
                }
            }

            public void Reset()
            {
                timeLeft = totalTime;
            }

            public void Reset(float time)
            {
                timeLeft = totalTime = time;
            }

            public void Cancel()
            {
                timeLeft = 0;
                OnCancel?.Invoke();
                OnEnd?.Invoke();
            }
        }
    }
}
