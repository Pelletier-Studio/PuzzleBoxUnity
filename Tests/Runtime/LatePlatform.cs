using UnityEngine;

namespace PuzzleBox.Tests
{
    /// <summary>A stepped platform whose FixedUpdate runs after that of the bodies riding it.</summary>
    [DefaultExecutionOrder(100)]
    public class LatePlatform : SteppedKinematicMotion2D
    {
    }
}
