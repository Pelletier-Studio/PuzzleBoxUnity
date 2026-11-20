/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using UnityEngine;

namespace PuzzleBox
{
    public class FaceMotionDirection2D : MonoBehaviour
    {
        public PlatformerPlayer2D player2D;

        // The rotation speed in degrees per second.
        public float rotationSpeed = 360f;

        public float rotationOffset = 1f;

        void Start()
        {
            if (!player2D)
            {
                player2D = GetComponentInParent<PlatformerPlayer2D>();
            }

            if (!player2D)
            {
                Debug.LogWarning("PlatformerPlayer2D component not found in parent hierarchy.", this);
            }
        }

        void LateUpdate()
        {
            if (player2D)
            {
                Vector2 motionDirection = player2D.facingDirection;

                // Rotate towards the facing direction
                if (motionDirection != Vector2.zero)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(new Vector3(motionDirection.x, 0f, motionDirection.y));
                    if (rotationOffset != 0f)
                    {
                        if (motionDirection.x < 0)
                        {
                            targetRotation *= Quaternion.Euler(0, -rotationOffset, 0);
                        } else
                        {
                            targetRotation *= Quaternion.Euler(0, rotationOffset, 0);
                        }
                    }
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                }
            }
        }

    }
}

