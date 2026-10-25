using UnityEngine;

namespace PuzzleBox.Tests
{
    /// <summary>A stepped platform whose FixedUpdate runs before that of the bodies riding it.</summary>
    [DefaultExecutionOrder(-100)]
    public class EarlyPlatform : SteppedKinematicMotion2D
    {
    }
}
