using UnityEngine;

namespace PuzzleBox.Tests
{
    /// <summary>
    /// KinematicMotion2D whose FixedUpdate only runs when a test allows it, so a test controls
    /// exactly how many physics steps the body takes and can inspect the state after each one.
    /// </summary>
    public class SteppedKinematicMotion2D : KinematicMotion2D
    {
        // Counts FixedUpdates across all stepped bodies, so tests can check the order they ran in.
        public static int fixedUpdateSequence;

        public int pendingSteps;

        public int stepsTaken { get; private set; }

        // fixedUpdateSequence at this body's most recent FixedUpdate.
        public int lastSequence { get; private set; }

        protected override void FixedUpdate()
        {
            if (pendingSteps <= 0)
            {
                return;
            }

            pendingSteps--;
            lastSequence = ++fixedUpdateSequence;
            base.FixedUpdate();
            stepsTaken++;
        }
    }
}
