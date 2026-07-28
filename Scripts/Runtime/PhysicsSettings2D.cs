/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
 
using UnityEngine;

namespace PuzzleBox
{
    public class PhysicsSettings2D : MonoBehaviour
    {
        public float gravity = -9.8f;

        public void SetGravity(float g) 
        {
            gravity = g;
            Physics2D.gravity = Vector2.up * gravity;
        }

        private void Awake()
        {
            SetGravity(gravity);
        }

        void FixedUpdate()
        {
            SetGravity(gravity);
        }
    }
} // namespace

