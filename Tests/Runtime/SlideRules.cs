using UnityEngine;

namespace PuzzleBox.Tests
{
    public enum SurfaceKind
    {
        Ground,
        Wall,
        Ceiling,
    }

    /// <summary>
    /// The collide-and-slide rules from Documentation/Collide-and-Slide.png, written as an oracle the
    /// tests compare KinematicMotion2D against. Nothing here touches the class under test.
    ///
    /// Surfaces are described by the angle of their outward normal, in degrees, measured clockwise
    /// from up: 0 is flat ground, 90 a vertical wall facing right, 180 a flat ceiling. The normal is
    /// therefore (sin a, cos a).
    /// </summary>
    public static class SlideRules
    {
        // KinematicMotion2D's documented defaults for maxGroundAngleDegrees / maxCeilingAngleDegrees.
        public const float MaxGroundAngleDegrees = 45f;
        public const float MaxCeilingAngleDegrees = 45f;

        const float Epsilon = 1e-5f;

        public static Vector2 Normal(float angleDegrees)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        public static Vector2 Tangent(Vector2 normal)
        {
            return new Vector2(normal.y, -normal.x);
        }

        public static SurfaceKind Classify(Vector2 normal)
        {
            if (Vector2.Angle(normal, Vector2.up) <= MaxGroundAngleDegrees)
            {
                return SurfaceKind.Ground;
            }
            if (Vector2.Angle(normal, Vector2.down) <= MaxCeilingAngleDegrees)
            {
                return SurfaceKind.Ceiling;
            }
            return SurfaceKind.Wall;
        }

        /// <summary>
        /// Velocity after <paramref name="velocity"/> collides with a surface of the given normal.
        ///
        /// - Ground or ceiling, either state: slide along the surface, preserving horizontal speed.
        /// - Wall, grounded: stop.
        /// - Wall, airborne: slide along the wall, preserving vertical speed, even when that reverses
        ///   the horizontal direction of travel.
        ///
        /// For several surfaces hit in one move, the last surface decides, always computed from the
        /// velocity the move started with. A body left wedged, so that the last surface's slide would
        /// drive it into another surface it is touching, has zero velocity.
        /// </summary>
        public static Vector2 AfterCollision(Vector2 velocity, Vector2 normal, bool grounded)
        {
            Vector2 tangent = Tangent(normal);
            switch (Classify(normal))
            {
                case SurfaceKind.Ground:
                case SurfaceKind.Ceiling:
                    return PreserveHorizontal(velocity, tangent);
                default:
                    return grounded ? Vector2.zero : PreserveVertical(velocity, tangent);
            }
        }

        static Vector2 PreserveHorizontal(Vector2 velocity, Vector2 tangent)
        {
            if (Mathf.Abs(velocity.x) < Epsilon || Mathf.Abs(tangent.x) < Epsilon)
            {
                return Vector2.zero;
            }
            return tangent * (velocity.x / tangent.x);
        }

        static Vector2 PreserveVertical(Vector2 velocity, Vector2 tangent)
        {
            if (Mathf.Abs(velocity.y) < Epsilon || Mathf.Abs(tangent.y) < Epsilon)
            {
                return Vector2.zero;
            }
            return tangent * (velocity.y / tangent.y);
        }
    }
}
