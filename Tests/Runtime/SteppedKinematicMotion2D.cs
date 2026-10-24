using UnityEngine;

namespace PuzzleBox.Tests
{
    /// <summary>
    /// KinematicMotion2D whose FixedUpdate only runs when a test allows it, so a test controls
    /// exactly how many physics steps the body takes and can inspect the state after each one.
    /// </summary>
    public class SteppedKinematicMotion2D : KinematicMotion2D
    {
        public int pendingSteps;

        public int stepsTaken { get; private set; }

        protected override void FixedUpdate()
        {
            if (pendingSteps <= 0)
            {
                return;
            }

            pendingSteps--;
            base.FixedUpdate();
            stepsTaken++;
        }
    }
}
