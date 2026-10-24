using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PuzzleBox.Tests
{
    /// <summary>
    /// Collide-and-slide into corners formed by two surfaces, following the "Multiple collisions" and
    /// "Ground -> ..." panels of Documentation/Collide-and-Slide.png.
    ///
    /// Scenarios are authored moving rightward and also run mirrored. In <see cref="InOneStep"/> the
    /// body is close enough to both surfaces that the whole chain of collisions happens in one move,
    /// which is what the rules are stated for: the last surface decides, from the velocity the move
    /// started with. <see cref="OverSeveralSteps"/> re-runs, at low speed, the scenarios whose result
    /// should not depend on how the chain is split across steps.
    /// </summary>
    public class CollideAndSlideMultiSurfaceTests : CollideAndSlideFixture
    {
        const float FastSpeed = 25f;
        const float SlowSpeed = 2f;
        const int MaxSlowSteps = 60;

        public struct SurfaceSpec
        {
            public float angle;
            // Gap from the body beyond margin.
            public float extraGap;

            public SurfaceSpec(float angle, float extraGap)
            {
                this.angle = angle;
                this.extraGap = extraGap;
            }
        }

        public class Scenario
        {
            public string name;
            public bool grounded;
            public SurfaceSpec[] surfaces;
            public Vector2 direction;
            // Index of the surface the body meets first (checked as a precondition).
            public int firstHit;
            // Index of the surface the body ends up sliding along, or -1 if it must stop.
            public int slideOn = -1;
            // Indices of the surfaces a stopped body must rest against.
            public int[] restsAgainst = new int[0];
            // Whether the result is the same when the collisions happen over several steps.
            public bool speedIndependent;

            public Scenario Mirrored()
            {
                Scenario mirrored = (Scenario)MemberwiseClone();
                mirrored.name = name.Replace("_Rightward", "_Leftward");
                mirrored.surfaces = surfaces.Select(s => new SurfaceSpec(-s.angle, s.extraGap)).ToArray();
                mirrored.direction = new Vector2(-direction.x, direction.y);
                return mirrored;
            }

            public int[] FinalContacts => slideOn >= 0 ? new[] { slideOn } : restsAgainst;

            public Vector2 ExpectedVelocity(Vector2 velocity)
            {
                return slideOn >= 0
                    ? SlideRules.AfterCollision(velocity, SlideRules.Normal(surfaces[slideOn].angle), grounded)
                    : Vector2.zero;
            }

            public override string ToString() => name;
        }

        // Directions as degrees counter-clockwise from +x.
        static Vector2 Direction(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        static readonly (string name, Vector2 direction) Right = ("Right", Direction(0f));
        static readonly (string name, Vector2 direction) DownRight = ("DownRight", Direction(-45f));
        static readonly (string name, Vector2 direction) Down = ("Down", Direction(-90f));
        static readonly (string name, Vector2 direction) UpRight = ("UpRight", Direction(45f));
        static readonly (string name, Vector2 direction) Up = ("Up", Direction(90f));
        static readonly (string name, Vector2 direction) ShallowDownRight = ("ShallowDownRight", Direction(-15f));
        static readonly (string name, Vector2 direction) ShallowUpRight = ("ShallowUpRight", Direction(15f));
        static readonly (string name, Vector2 direction) SteepUpRight = ("SteepUpRight", Direction(75f));

        // Walls on the body's right, by normal angle (clockwise from up).
        static readonly (string name, float angle)[] RightWalls =
        {
            ("VerticalWall", -90f),
            ("LeaningWall", -70f),
            ("OverhangingWall", -110f),
        };

        static Scenario Make(string id, string variant, (string name, Vector2 direction) move, bool grounded,
            SurfaceSpec[] surfaces, int firstHit, int slideOn = -1, int[] restsAgainst = null, bool speedIndependent = false)
        {
            return new Scenario
            {
                name = $"{id}_{variant}_{move.name}_Rightward",
                grounded = grounded,
                surfaces = surfaces,
                direction = move.direction,
                firstHit = firstHit,
                slideOn = slideOn,
                restsAgainst = restsAgainst ?? new int[0],
                speedIndependent = speedIndependent,
            };
        }

        static SurfaceSpec S(float angle, float extraGap) => new SurfaceSpec(angle, extraGap);

        static readonly int[] Both = { 0, 1 };

        static IEnumerable<Scenario> AuthoredScenarios()
        {
            // G1: grounded on flat ground, sliding into a wall stops dead.
            foreach ((string wall, float angle) in RightWalls)
            {
                SurfaceSpec[] scene = { S(0f, 0f), S(angle, 0.05f) };
                yield return Make("G1_GroundToWall", $"FlatGround_{wall}", Right, true, scene, 1, restsAgainst: Both, speedIndependent: true);
                yield return Make("G1_GroundToWall", $"FlatGround_{wall}", DownRight, true, scene, 0, restsAgainst: Both, speedIndependent: true);
            }

            // G2: grounded on a slope, sliding into a wall stops dead.
            foreach ((string wall, float angle) in RightWalls)
            {
                SurfaceSpec[] rising = { S(-30f, 0f), S(angle, 0.05f) };
                yield return Make("G2_SlopeToWall", $"RisingSlope_{wall}", Right, true, rising, 0, restsAgainst: Both, speedIndependent: true);
                yield return Make("G2_SlopeToWall", $"RisingSlope_{wall}", DownRight, true, rising, 0, restsAgainst: Both, speedIndependent: true);

                SurfaceSpec[] falling = { S(30f, 0f), S(angle, 0.1f) };
                yield return Make("G2_SlopeToWall", $"FallingSlope_{wall}", DownRight, true, falling, 0, restsAgainst: Both, speedIndependent: true);
            }

            // G3: grounded, sliding from one ground onto another follows the last one, keeping vx.
            SurfaceSpec[] flatToRising = { S(0f, 0f), S(-30f, 0.05f) };
            yield return Make("G3_GroundToGround", "FlatToRisingSlope", Right, true, flatToRising, 1, slideOn: 1);
            yield return Make("G3_GroundToGround", "FlatToRisingSlope", DownRight, true, flatToRising, 0, slideOn: 1);
            SurfaceSpec[] fallingToFlat = { S(30f, 0f), S(0f, 0.05f) };
            yield return Make("G3_GroundToGround", "FallingSlopeToFlat", DownRight, true, fallingToFlat, 0, slideOn: 1);
            SurfaceSpec[] gentleToSteep = { S(-15f, 0f), S(-30f, 0.05f) };
            yield return Make("G3_GroundToGround", "GentleToSteepSlope", Right, true, gentleToSteep, 0, slideOn: 1);
            yield return Make("G3_GroundToGround", "GentleToSteepSlope", DownRight, true, gentleToSteep, 0, slideOn: 1);

            // G4: grounded, sliding from ground into a ceiling stops dead.
            SurfaceSpec[] slopeUnderCeiling = { S(-30f, 0f), S(-150f, 0.05f) };
            yield return Make("G4_GroundToCeiling", "RisingSlopeIntoCeiling", Right, true, slopeUnderCeiling, 0, restsAgainst: Both, speedIndependent: true);
            yield return Make("G4_GroundToCeiling", "RisingSlopeIntoCeiling", DownRight, true, slopeUnderCeiling, 0, restsAgainst: Both, speedIndependent: true);
            SurfaceSpec[] flatUnderCeiling = { S(0f, 0f), S(-150f, 0.05f) };
            yield return Make("G4_GroundToCeiling", "FlatGroundIntoCeiling", Right, true, flatUnderCeiling, 1, restsAgainst: Both, speedIndependent: true);
            yield return Make("G4_GroundToCeiling", "FlatGroundIntoCeiling", DownRight, true, flatUnderCeiling, 0, restsAgainst: Both, speedIndependent: true);

            // A1: airborne, ceiling then wall. The wall decides: original vy kept.
            foreach ((string wall, float angle) in new[] { ("VerticalWall", -90f), ("OverhangingWall", -110f) })
            {
                SurfaceSpec[] scene = { S(-150f, 0.01f), S(angle, 0.08f) };
                yield return Make("A1_CeilingToWall", wall, ShallowDownRight, false, scene, 0, slideOn: 1);
                yield return Make("A1_CeilingToWall", wall, Right, false, scene, 0, restsAgainst: Both);
                // Keeping the upward vy slides the body up the wall, back into the ceiling it just left,
                // so it ends wedged in the corner.
                yield return Make("A1_CeilingToWall", wall, ShallowUpRight, false, scene, 0, restsAgainst: Both);
            }

            // A2: airborne, wall then ceiling. The ceiling decides: original vx kept.
            SurfaceSpec[] wallUnderCeiling = { S(120f, 0.01f), S(150f, 0.12f) };
            yield return Make("A2_WallToCeiling", "OverhangThenCeiling", SteepUpRight, false, wallUnderCeiling, 0, slideOn: 1);
            yield return Make("A2_WallToCeiling", "OverhangThenCeiling", Up, false, wallUnderCeiling, 0, restsAgainst: Both);

            // A3: airborne, sliding down a wall onto the ground. The ground keeps vx, which carries the
            // body into the corner, so it ends wedged.
            // Not speed independent: a slow body sliding down the wall counts as grounded up to
            // groundCheckDistance before it touches the ground, and stops (or turns along the ground)
            // without ever colliding with it. With gravity, as in a game, it would settle onto the
            // margin; these tests cancel gravity.
            foreach ((string wall, float angle) in new[] { ("VerticalWall", -90f), ("OverhangingWall", -110f) })
            {
                SurfaceSpec[] scene = { S(angle, 0.01f), S(0f, 0.15f) };
                yield return Make("A3_WallToGround", wall, DownRight, false, scene, 0, restsAgainst: Both);
            }

            // A4: sliding from a ceiling onto the ground. The ground keeps vx, which carries the body into
            // the wedge between the two, so it ends wedged.
            SurfaceSpec[] wedge = { S(-150f, 0.02f), S(-30f, 0.15f) };
            yield return Make("A4_CeilingToGround", "Wedge", UpRight, false, wedge, 0, restsAgainst: Both, speedIndependent: true);
            yield return Make("A4_CeilingToGround", "Wedge", Right, false, wedge, 0, restsAgainst: Both, speedIndependent: true);
            SurfaceSpec[] lowCeiling = { S(0f, 0f), S(-150f, 0.1f) };
            yield return Make("A4_CeilingToGround", "FromGroundUnderCeiling", UpRight, true, lowCeiling, 1, restsAgainst: Both, speedIndependent: true);

            // A5: airborne between two walls that narrow in the direction of travel. Each wall keeps vy
            // and sends the body across to the other, so it ends wedged between them.
            SurfaceSpec[] funnel = { S(70f, 0.02f), S(-70f, 0.06f) };
            yield return Make("A5_WallToWall", "Funnel", Down, false, funnel, 0, restsAgainst: Both, speedIndependent: true);
            yield return Make("A5_WallToWall", "Funnel", DownRight, false, funnel, 1, restsAgainst: Both, speedIndependent: true);
            SurfaceSpec[] invertedFunnel = { S(110f, 0.02f), S(-110f, 0.06f) };
            yield return Make("A5_WallToWall", "InvertedFunnel", Up, false, invertedFunnel, 0, restsAgainst: Both, speedIndependent: true);
            yield return Make("A5_WallToWall", "InvertedFunnel", UpRight, false, invertedFunnel, 1, restsAgainst: Both, speedIndependent: true);

            // A6: airborne into a valley. The first ground hit keeps vx: falling straight down, that
            // stops the body; falling diagonally, it slides up the slope it lands on, away from the valley.
            // Not speed independent: a slow body counts as grounded up to groundCheckDistance before it
            // touches the slope, and then stops where it is, short of the contact band.
            SurfaceSpec[] valley = { S(30f, 0.15f), S(-30f, 0.3f) };
            yield return Make("A6_GroundToGround", "Valley", Down, false, valley, 0, restsAgainst: new[] { 0 });
            yield return Make("A6_GroundToGround", "Valley", DownRight, false, valley, 1, slideOn: 1);

            // A7: airborne under a roof apex. Carries on along the second ceiling, original vx kept.
            SurfaceSpec[] apex = { S(150f, 0.01f), S(-150f, 0.12f) };
            yield return Make("A7_CeilingToCeiling", "Apex", UpRight, false, apex, 0, slideOn: 1);

            // A8: axis-aligned corners between a flat ceiling and a vertical wall.
            yield return Make("A8_CeilingWallCorner", "CeilingFirst", UpRight, false, new[] { S(180f, 0.01f), S(-90f, 0.08f) }, 0, restsAgainst: Both, speedIndependent: true);
            yield return Make("A8_CeilingWallCorner", "WallFirst", UpRight, false, new[] { S(-90f, 0.01f), S(180f, 0.08f) }, 0, restsAgainst: Both, speedIndependent: true);
            yield return Make("A8_CeilingWallCorner", "AlongCeiling", Right, false, new[] { S(180f, 0f), S(-90f, 0.05f) }, 1, restsAgainst: Both, speedIndependent: true);
        }

        static IEnumerable<Scenario> AllScenarios()
        {
            foreach (Scenario scenario in AuthoredScenarios())
            {
                yield return scenario;
                yield return scenario.Mirrored();
            }
        }

        static IEnumerable<TestCaseData> FastCases()
        {
            return AllScenarios().Select(s => new TestCaseData(s).SetName(s.name).Returns(null));
        }

        static IEnumerable<TestCaseData> SlowCases()
        {
            return AllScenarios()
                .Where(s => s.speedIndependent)
                .Select(s => new TestCaseData(s).SetName(s.name + "_Slow").Returns(null));
        }

        IEnumerator Stage(Scenario scenario)
        {
            yield return SpawnBody();
            foreach (SurfaceSpec spec in scenario.surfaces)
            {
                AddSurface(spec.angle, margin + spec.extraGap);
            }
            AssertGroundedPrecondition(scenario.grounded);
        }

        [UnityTest, TestCaseSource(nameof(FastCases))]
        public IEnumerator InOneStep(Scenario scenario)
        {
            yield return Stage(scenario);
            Vector2 velocity = scenario.direction.normalized * FastSpeed;
            AssertFirstHitPrecondition(surfaces[scenario.firstHit], velocity);
            Vector2 expected = scenario.ExpectedVelocity(velocity);
            Surface[] contacts = scenario.FinalContacts.Select(i => surfaces[i]).ToArray();

            Launch(velocity);
            yield return Step();
            AssertVelocity(expected, "After the step containing every collision");
            foreach (Surface contact in contacts)
            {
                AssertInContact(contact, "After the step containing every collision");
            }

            yield return SteadyState(expected, contacts);

            if (scenario.grounded && scenario.slideOn >= 0)
            {
                body.UpdateGroundedState();
                Vector2 lastGround = SlideRules.Normal(scenario.surfaces[scenario.slideOn].angle);
                Assert.IsTrue(body.isGrounded, "The body should end grounded on the last ground");
                Assert.That(Vector2.Distance(body.groundNormal, lastGround), Is.LessThan(1e-3f),
                    $"groundNormal should be the last ground's {Format(lastGround)}, got {Format(body.groundNormal)}");
            }
        }

        [UnityTest, TestCaseSource(nameof(SlowCases))]
        public IEnumerator OverSeveralSteps(Scenario scenario)
        {
            yield return Stage(scenario);
            Surface[] contacts = scenario.FinalContacts.Select(i => surfaces[i]).ToArray();

            // Step until the body stops moving. The last step that moved it is the one that reached the
            // corner, and its velocity must already be zero.
            Launch(scenario.direction.normalized * SlowSpeed);
            int lastMovingStep = 0;
            Vector2 lastMovingVelocity = body.velocity;
            for (int i = 0; ; i++)
            {
                if (i == MaxSlowSteps)
                {
                    Assert.Fail($"The body never came to rest (body at {Format(body.position)}, velocity {Format(body.velocity)})");
                }
                Vector2 previous = body.position;
                yield return Step();
                if (Vector2.Distance(previous, body.position) <= PositionTolerance * 1e-2f)
                {
                    break;
                }
                lastMovingStep = body.stepsTaken;
                lastMovingVelocity = body.velocity;
            }

            foreach (Surface contact in contacts)
            {
                AssertInContact(contact, $"Step {lastMovingStep}, on reaching the corner");
            }
            AssertVelocity(Vector2.zero, lastMovingVelocity, $"Step {lastMovingStep}, on reaching the corner");
            AssertVelocity(Vector2.zero, $"Step {body.stepsTaken}, at rest in the corner");
            yield return SteadyState(Vector2.zero, contacts);
        }
    }
}
