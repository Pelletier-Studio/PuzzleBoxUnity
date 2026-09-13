/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System;
using UnityEngine;

namespace PuzzleBox
{
    public class GrabKinematicMotion2D : MonoBehaviour
    {
        public KinematicMotion2D attachTo;

        private KinematicMotion2D objectToGrab;

        void OnTriggerEnter2D(Collider2D other)
        {
            // Try to get the KinematicMotion2D component from the other collider.
            KinematicMotion2D otherMotion = other.GetComponent<KinematicMotion2D>();
            if (otherMotion != null)
            {
                Debug.Log("Set grab target: " + otherMotion.name);
                objectToGrab = otherMotion;
            }
        }

        void OnTriggerExit2D(Collider2D other)
        {
            // If the object leaving the trigger is the one we were planning to grab, clear the reference.
            KinematicMotion2D otherMotion = other.GetComponent<KinematicMotion2D>();
            if (otherMotion != null && otherMotion == objectToGrab)
            {
                Debug.Log("Cleared grab target: " + otherMotion.name);
                objectToGrab = null;
            }
        }

        // Try to grab or release an object. Pass true to grab, false to release.
        public void Grab(bool shouldGrab)
        {
            if (attachTo == null)
            {
                Debug.LogWarning("attachTo is not set.");
                return;
            }

            if (objectToGrab != null)
            {
                if (shouldGrab)
                {
                    Debug.Log("Grabbing object: " + objectToGrab.name);
                    objectToGrab.AttachTo(attachTo);
                }
                else
                {
                    Debug.Log("Releasing object: " + objectToGrab.name);
                    objectToGrab.Detach();
                }   
            }
        }
        
        void OnGrab(object val)
        {
            Grab(PuzzleBox.InputValue.IsPressed(val));
        }
    }
}
