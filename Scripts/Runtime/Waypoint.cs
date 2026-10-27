/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
 
using UnityEngine;

namespace PuzzleBox
{
    public class Waypoint : MonoBehaviour
    {
        [Min(0)]
        public float speed = 2f;

        [Min(1)]
        public float acceleration = 100f;

        [Min(1)]
        public float breaking = 100f;

        public Vector3 position
        {
            get { return transform.position; }
        }

        public float EstimateBreakingDistance(float currentSpeed)
        {
            if (breaking == float.PositiveInfinity)
            {
                return 0;
            }
            else if (breaking > 0)
            {
                return (currentSpeed * currentSpeed) / (2f * breaking);
            }
            else
            {
                return float.PositiveInfinity;
            }
        }
    }
}

