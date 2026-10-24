using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PuzzleBox.Tests
{
    /// <summary>
    /// Collide-and-slide against one surface, for every ground, wall and ceiling orientation and for
    /// axis-aligned, diagonal and head-on velocities. Expected results come from <see cref="SlideRules"/>.
    /// </summary>
    public class CollideAndSlideSingleSurfaceTests : CollideAndSlideFixture
    {
        public enum Start
        {
            // Starts clear of the surface and hits it partway through the first step.
            Approach,
            // Starts resting against the surface, at exactly margin.
            Touching,
        }

        const float Speed = 25f;
        // Free space left before impact, so the body is plainly airborne when the impact step begins.
        const float MinApproachGap = 0.1f;
        const float MaxImpactFraction = 0.8f;
        const float TunnelCeilingGap = 0.1f;
        // Directions this close to parallel with the surface are treated as grazing, not impacts.
        const float MinImpactDot = 0.1f;

        // Normal angles, clockwise from up. ± covers both mirror images.
        static readonly float[] GroundAngles = { 0f, 30f, -30f };
        static readonly float[] WallAngles = { 90f, -90f, 70f, -70f, 110f, -110f };
        static readonly float[] CeilingAngles = { 180f, 150f, -150f };

        static readonly (string name, Vector2 direction)[] CompassDirections =
        {
            ("Right", new Vector2(1f, 0f)),
            ("UpRight", new Vector2(1f, 1f)),
            ("Up", new Vector2(0f, 1f)),
            ("UpLeft", new Vector2(-1f, 1f)),
            ("Left", new Vector2(-1f, 0f)),
            ("DownLeft", new Vector2(-1f, -1f)),
            ("Down", new Vector2(0f, -1f)),
            ("DownRight", new Vector2(1f, -1f)),
        };

        static IEnumerable<float> AllAngles()
        {
            foreach (float angle in GroundAngles) yield return angle;
            foreach (float angle in WallAngles) yield return angle;
            foreach (float angle in CeilingAngles) yield return angle;
        }

        /// <summary>Compass directions plus head-on (-normal) for tilted surfaces, keeping those that move into the surface.</summary>
        static IEnumerable<(string name, Vector2 direction)> DirectionsInto(float angle)
        {
            Vector2 normal = SlideRules.Normal(angle);
            foreach ((string name, Vector2 direction) in CompassDirections)
            {
                if (Vector2.Dot(direction.normalized, normal) < -MinImpactDot)
                {
                    yield return (name, direction.normalized);
                }
            }
            if (Mathf.Repeat(angle, 90f) != 0f)
            {
                yield return ("HeadOn", -normal);
            }
        }

        static TestCaseData Case(string name, params object[] args)
        {
            return new TestCaseData(args).SetName(name).Returns(null);
        }

        // ---- Airborne ----

        static IEnumerable<TestCaseData> AirborneCases()
        {
            foreach (float angle in AllAngles())
            {
                bool isGround = SlideRules.Classify(SlideRules.Normal(angle)) == SurfaceKind.Ground;
                foreach ((string name, Vector2 direction) in DirectionsInto(angle))
                {
                    yield return Case($"Airborne_{SurfaceName(angle)}_{name}_{Start.Approach}", angle, direction, Start.Approach);
                    // Resting against ground at margin is being grounded, which GroundedCases covers.
                    if (!isGround)
                    {
                        yield return Case($"Airborne_{SurfaceName(angle)}_{name}_{Start.Touching}", angle, direction, Start.Touching);
                    }
                }
            }
        }

        [UnityTest, TestCaseSource(nameof(AirborneCases))]
        public IEnumerator Airborne(float surfaceAngle, Vector2 direction, Start start)
        {
            yield return SpawnBody();
            Vector2 normal = SlideRules.Normal(surfaceAngle);
            Vector2 velocity = direction * Speed;
            float impactFraction = start == Start.Touching ? 0f : ImpactFraction(velocity, normal, MinApproachGap);
            Surface surface = AddSurface(surfaceAngle, margin + impactFraction * NormalTravelPerStep(velocity, normal));
            AssertGroundedPrecondition(false);

            Vector2 expected = SlideRules.AfterCollision(velocity, normal, false);
            yield return CollideThenSlide(surface, velocity, expected);
        }

        // ---- Airborne with useGravity off: collisions must not depend on gravity ----

        static IEnumerable<TestCaseData> WithoutGravityCases()
        {
            yield return Case("WithoutGravity_Ground0_Down", 0f, new Vector2(0f, -1f));
            yield return Case("WithoutGravity_Wall-90_Right", -90f, new Vector2(1f, 0f));
            yield return Case("WithoutGravity_Wall-90_UpRight", -90f, new Vector2(1f, 1f).normalized);
            yield return Case("WithoutGravity_Ceiling180_UpRight", 180f, new Vector2(1f, 1f).normalized);
        }

        [UnityTest, TestCaseSource(nameof(WithoutGravityCases))]
        public IEnumerator WithoutGravity(float surfaceAngle, Vector2 direction)
        {
            yield return SpawnBody(useGravity: false);
            Vector2 normal = SlideRules.Normal(surfaceAngle);
            Vector2 velocity = direction * Speed;
            float impactFraction = ImpactFraction(velocity, normal, MinApproachGap);
            Surface surface = AddSurface(surfaceAngle, margin + impactFraction * NormalTravelPerStep(velocity, normal));
            AssertGroundedPrecondition(false);

            Vector2 expected = SlideRules.AfterCollision(velocity, normal, false);
            yield return CollideThenSlide(surface, velocity, expected);
        }

        // ---- Grounded ----

        static IEnumerable<TestCaseData> GroundedCases()
        {
            foreach (float angle in GroundAngles)
            {
                foreach ((string name, Vector2 direction) in DirectionsInto(angle))
                {
                    yield return Case($"Grounded_{SurfaceName(angle)}_{name}", angle, direction);
                }
            }
        }

        [UnityTest, TestCaseSource(nameof(GroundedCases))]
        public IEnumerator Grounded(float groundAngle, Vector2 direction)
        {
            yield return SpawnBody();
            Vector2 normal = SlideRules.Normal(groundAngle);
            Vector2 velocity = direction * Speed;
            Surface ground = AddSurface(groundAngle, margin);
            AssertGroundedPrecondition(true);

            Vector2 expected = SlideRules.AfterCollision(velocity, normal, true);
            yield return CollideThenSlide(ground, velocity, expected);
        }

        // ---- Grounded, hitting a ceiling above ----

        static IEnumerable<TestCaseData> TunnelCases()
        {
            foreach (float angle in CeilingAngles)
            {
                Vector2 normal = SlideRules.Normal(angle);
                foreach ((string name, Vector2 direction) in DirectionsInto(angle))
                {
                    // Slides that lead back down into the floor are ceiling -> ground, covered by the
                    // multi-surface suite.
                    if (direction.y < 0f || SlideRules.AfterCollision(direction, normal, true).y < 0f)
                    {
                        continue;
                    }
                    yield return Case($"Grounded_Tunnel_{SurfaceName(angle)}_{name}", angle, direction);
                }
            }
        }

        [UnityTest, TestCaseSource(nameof(TunnelCases))]
        public IEnumerator GroundedUnderCeiling(float ceilingAngle, Vector2 direction)
        {
            yield return SpawnBody();
            Vector2 normal = SlideRules.Normal(ceilingAngle);
            Vector2 velocity = direction * Speed;
            AddSurface(0f, margin);
            Surface ceiling = AddSurface(ceilingAngle, margin + TunnelCeilingGap);
            AssertGroundedPrecondition(true);

            float impactFraction = TunnelCeilingGap / NormalTravelPerStep(velocity, normal);
            Assert.Less(impactFraction, MaxImpactFraction, "Precondition: the ceiling must be reached early in the first step");

            Vector2 expected = SlideRules.AfterCollision(velocity, normal, true);
            yield return CollideThenSlide(ceiling, velocity, expected);
        }

        // ---- Moving along or away from a surface is not a collision ----

        static IEnumerable<TestCaseData> UnobstructedCases()
        {
            foreach (float angle in AllAngles())
            {
                Vector2 normal = SlideRules.Normal(angle);
                Vector2 tangent = SlideRules.Tangent(normal);
                string state = SlideRules.Classify(normal) == SurfaceKind.Ground ? "Grounded" : "Airborne";
                yield return Case($"{state}_{SurfaceName(angle)}_AlongSurface", angle, tangent);
                yield return Case($"{state}_{SurfaceName(angle)}_AlongSurfaceReversed", angle, -tangent);
                yield return Case($"{state}_{SurfaceName(angle)}_Away", angle, normal);
            }
        }

        [UnityTest, TestCaseSource(nameof(UnobstructedCases))]
        public IEnumerator MovingAlongOrAway(float surfaceAngle, Vector2 direction)
        {
            yield return SpawnBody();
            Vector2 normal = SlideRules.Normal(surfaceAngle);
            Vector2 velocity = direction * Speed;
            Surface surface = AddSurface(surfaceAngle, margin);
            AssertGroundedPrecondition(SlideRules.Classify(normal) == SurfaceKind.Ground);

            bool alongSurface = Mathf.Abs(Vector2.Dot(direction, normal)) < 1e-4f;
            Launch(velocity);
            yield return SteadyState(velocity, alongSurface ? new[] { surface } : new Surface[0]);
        }

        // ---- Helpers ----

        /// <summary>How far the body closes in on the surface in one unobstructed step.</summary>
        static float NormalTravelPerStep(Vector2 velocity, Vector2 normal)
        {
            return -Vector2.Dot(velocity, normal) * FixedDeltaTime;
        }

        /// <summary>
        /// The fraction of the first step after which the body meets the surface: halfway, or later if
        /// needed to leave at least <paramref name="minGap"/> of free space before the impact.
        /// </summary>
        static float ImpactFraction(Vector2 velocity, Vector2 normal, float minGap)
        {
            float fraction = Mathf.Max(0.5f, minGap / NormalTravelPerStep(velocity, normal));
            Assert.LessOrEqual(fraction, MaxImpactFraction, "Precondition: the impact must happen inside the first step");
            return fraction;
        }

        /// <summary>
        /// The impact happens on the first step. After it the velocity must already follow the rule and
        /// the body must rest against the surface, having moved freely until contact and then along the
        /// new velocity for the rest of the step. After that it keeps sliding (or stays still).
        ///
        /// Where exactly inside the contact band the body stops is left to AssertInContact. The impact
        /// point is taken from the gap the body actually stopped at, because a small difference in that
        /// gap becomes a difference of gap / |n.d| along the path, which is large at grazing incidence.
        /// </summary>
        IEnumerator CollideThenSlide(Surface surface, Vector2 velocity, Vector2 expected)
        {
            Launch(velocity);
            Vector2 start = body.position;
            float startGap = Gap(surface);
            yield return Step();

            AssertVelocity(expected, "Impact step");
            AssertInContact(surface, "Impact step");
            float endGap = Gap(surface);
            float normalTravel = NormalTravelPerStep(velocity, surface.normal);
            float impactFraction = Mathf.Clamp01((startGap - endGap) / normalTravel);
            // Any change in gap the motion does not explain (settling within the band when starting
            // in contact) is movement along the normal, which the gap checks already bound.
            float settle = endGap - (startGap - impactFraction * normalTravel);
            Vector2 displacement = (velocity * impactFraction + expected * (1f - impactFraction)) * FixedDeltaTime
                + surface.normal * settle;
            AssertDisplacement(start, displacement, PositionTolerance, "Impact step");

            yield return SteadyState(expected, new[] { surface });
        }
    }
}
