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
         * This is a general purpose countdown timer class.
         * It is used to implement countdown-based logic, such as dash duration, cooldowns, 
         * and wall grab wait times. We can use it to trigger any action that needs to take
         * place after a given duration.
         *
         * Basic usage:
         *   1. Create a Timer:  Utils.Timer timer = new Utils.Timer();
         *   2. Call Tick() every frame:  timer.Tick(Time.deltaTime);
         *   3. Start the timer:  timer.Start(2.0f);  // 2 seconds
         *   4. Register completion callback:  timer.OnComplete += () => {  // your code  };
         *   5. Optionally, cancel the timer:  timer.Cancel();
         *   6. Optionally, register a start callback:  timer.OnStart += () => {  // your code  };
         *
         * Properties:
         *   - timeLeft   : Remaining time (seconds)
         *   - totalTime  : Total duration set for the timer (seconds)
         *   - timeElapsed: Time elapsed since the timer started (seconds)
         *   - isFinished : Whether the timer has finished
         *   - phase      : Progress ratio (0.0 = just started, 1.0 = finished)
         *   - active     : If set to false, time does not progress even when Tick is called (pause)
         * Callbacks:
         *   - OnStart    : Called when Start() is invoked
         *   - OnComplete : Called when the timer reaches 0 (natural completion)
         *   - OnCancel   : Called when Cancel() is invoked
         *   - OnEnd      : Called immediately after OnComplete or OnCancel, always invoked
         *
         * Important note:
         *   - Start(time) will only extend the timer if the new time is longer than the current remaining time.
         *     For example, if the timer has 3 seconds left and you call Start(1.0f), the remaining time will still be 3 seconds.
         *     OnStart callback will only be invoked if the timer is actually extended.
         *   - To restart the timer with a new time unconditionally, use Reset(time). This will set the timer to the specified time regardless of the current remaining time.
         *     OnStart callback will always be invoked when using Reset(time) with a time greater than 0.
         */
        public class Timer
        {
            // When this is true, the timer will progress when Tick() is called. If false, the timer is paused.
            public bool active = true;

            // The remaining time, in seconds, for the timer. This value decreases as Tick() is called.
            public float timeLeft { private set; get; } = 0f;

            // The total duration set for the timer, in seconds.
            public float totalTime { private set; get; } = 0f;

            // How much time has elapsed since the timer started, in seconds.
            public float timeElapsed { get { return totalTime - timeLeft; } }

            // A callback that is invoked only when the timer reaches 0 (natural completion).
            public Action OnComplete;

            // A callback that is invoked whenever the timer ends, regardless of whether it completed naturally or was canceled.
            public Action OnEnd;

            // A callback that is invoked when the timer is started. Note that starting an already running timer
            // will only invoke the callback if the timer is extended.
            public Action OnStart;

            // A callback that is invoked when the timer is canceled.
            public Action OnCancel;

            // Did the timer finish naturally (reached 0).
            public bool isFinished
            {
                get
                {
                    return timeLeft <= 0;
                }
            }

            // The current phase of the timer, represented as a value between 0 and 1.
            // 0 means the timer has just started, and 1 means the timer has finished.
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


            // Advances the timer by the specified delta time (dt).
            public void Tick(float dt, bool invokeCallback = true)
            {
                if (timeLeft > 0 && active)
                {
                    timeLeft -= dt;
                    if (timeLeft <= 0)
                    {
                        timeLeft = 0;
                        if (invokeCallback)
                        {
                            OnComplete?.Invoke();
                            OnEnd?.Invoke();
                        }
                    }
                }
            }

            // Start the timer.
            public void Start(float time, bool invokeCallback = true)
            {
                if (time < 0) time = 0; // Treat negative time as 0

                // If the timer is already running, we check to see if it needs to be canceled.
                if (timeLeft > 0 && time > timeLeft)
                {
                    Cancel(invokeCallback);
                    if (time > totalTime)
                    {
                        totalTime = time;
                    }

                    timeLeft = time;

                    if (time > 0 && invokeCallback)
                    {
                        OnStart?.Invoke();
                    }
                } else if (timeLeft <= 0)
                {
                    // The timer is idle (or has expired), initialize the time and start
                    totalTime = time;
                    timeLeft = time;

                    if (time > 0 && invokeCallback)
                    {
                        OnStart?.Invoke();
                    }
                }
            }

            // Reset the timer to its initial state.
            public void Reset(bool invokeCallback = true)
            {
                // If the timer is already running, we check to see if it needs to be canceled.
                if (timeLeft > 0)
                {
                    Cancel(invokeCallback);
                }

                timeLeft = totalTime;
                if (invokeCallback && totalTime > 0)
                {
                    OnStart?.Invoke();
                }
            }

            // Reset the timer to a specific time.
            public void Reset(float time, bool invokeCallback = true)
            {
                // If the timer is already running, we check to see if it needs to be canceled.
                if (timeLeft > 0)
                {
                    Cancel(invokeCallback);
                }

                timeLeft = totalTime = time;
                if (invokeCallback && time > 0)
                {
                    OnStart?.Invoke();
                }
            }

            // Cancel the timer, setting its time left to 0.
            public void Cancel(bool invokeCallback = true)
            {
                if (timeLeft > 0)
                {
                    timeLeft = 0;
                }
                if (invokeCallback)
                {
                    OnCancel?.Invoke();
                    OnEnd?.Invoke();
                }
            }
        }
    }
}
