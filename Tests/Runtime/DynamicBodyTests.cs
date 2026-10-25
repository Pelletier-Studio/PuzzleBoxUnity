using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PuzzleBox.Tests
{
    /// <summary>
    /// A KinematicMotion2D together with dynamic Rigidbody2D boxes.
    ///
    /// The rules these tests encode:
    /// - Pushing a dynamic body works like pushing another KinematicMotion2D: both move
    ///   remaining * m / (m + M) and stay in contact, the pusher keeps its velocity, and a dynamic body
    ///   that cannot move blocks the pusher like a wall.
    /// - A dynamic body pushed against a wall stops at the body's margin from it.
    /// - To dynamic bodies, a KinematicMotion2D is immovable: they land on it, or bump into it, and stop.
    /// - A KinematicMotion2D can stand on a dynamic body like on any ground. It does not press the body
    ///   into what is beneath it, and walking on it does not drag it along.
    ///
    /// Boxes are 1x1 with frozen rotation and real gravity, unless stated otherwise. The physics engine
    /// moves them a little on its own (settling onto the floor, contact offsets), hence the looser
    /// tolerance on their positions.
    /// </summary>
    public class DynamicBodyTests : CollideAndSlideFixture
    {
        const float Speed = 5f;
        const int Steps = 10;
        const int SettleSteps = 25;
        const float DynamicTolerance = 2e-3f;

        static TestCaseData Case(string name, params object[] args)
        {
            return new TestCaseData(args).SetName(name).Returns(null);
        }

        static void AssertMoved(Rigidbody2D box, Vector2 from, Vector2 expected, string context)
        {
            Vector2 actual = box.position - from;
            Assert.That((actual - expected).magnitude, Is.LessThan(DynamicTolerance),
                $"{context}: expected {box.name} to move by {Format(expected)}, but it moved by {Format(actual)}");
        }

        /// <summary>The gap between a 1x1 box and a static surface's face.</summary>
        static float BoxGap(Rigidbody2D box, Surface surface)
        {
            Vector2 n = surface.normal;
            return Vector2.Dot(box.position - surface.facePoint, n) - 0.5f * (Mathf.Abs(n.x) + Mathf.Abs(n.y));
        }

        void AssertTouchingBox(Rigidbody2D box, string context)
        {
            float gap = Gap(body, box.GetComponent<BoxCollider2D>());
            Assert.LessOrEqual(gap, margin + ContactBand, $"{context}: the body should rest against {box.name}, but its gap is {gap:F5}");
        }

        // The box staged by StageBoxBeside.
        Rigidbody2D stagedBox;

        /// <summary>The body grounded on a floor, with a box of mass <paramref name="mass"/> resting on the floor right beside it.</summary>
        IEnumerator StageBoxBeside(float mass)
        {
            yield return SpawnBody();
            AddSurface(0f, margin);
            stagedBox = AddDynamicBox("Box", Vector2.right * (1f + margin), mass);
            yield return Settle();
            yield return Simulate(SettleSteps);
            AssertGroundedPrecondition(true);
        }

        // ---- D1: pushing ----

        static IEnumerable<TestCaseData> PushCases()
        {
            yield return Case("PushDynamic_EqualMass", 1f);
            yield return Case("PushDynamic_HeavierBox", 3f);
        }

        [UnityTest, TestCaseSource(nameof(PushCases))]
        public IEnumerator PushesDynamicBody(float mass)
        {
            yield return StageBoxBeside(mass);
            Rigidbody2D box = stagedBox;
            float restingHeight = box.position.y;

            Vector2 velocity = Vector2.right * Speed;
            Vector2 expected = velocity * FixedDeltaTime * body.mass / (body.mass + mass);
            Launch(velocity);
            for (int i = 0; i < Steps; i++)
            {
                Vector2 start = body.position;
                Vector2 boxStart = box.position;
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                AssertDisplacement(start, expected, PositionTolerance, context);
                AssertMoved(box, boxStart, expected, context);
                AssertTouchingBox(box, context);
                AssertVelocity(velocity, context + " (pusher)");
            }
            Assert.That(box.position.y, Is.EqualTo(restingHeight).Within(DynamicTolerance), "The box should stay on the floor");
        }

        // ---- D2: the box cannot move ----

        [UnityTest]
        public IEnumerator DynamicBodyAgainstWallBlocksPusher()
        {
            yield return StageBoxBeside(1f);
            Rigidbody2D box = stagedBox;
            Surface wall = AddSurface(-90f, 1f + 2f * margin);

            Vector2 start = body.position;
            Vector2 boxStart = box.position;
            Launch(Vector2.right * Speed);
            for (int i = 0; i < 5; i++)
            {
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                AssertMoved(box, boxStart, Vector2.zero, context);
                Assert.GreaterOrEqual(BoxGap(box, wall), margin - OverlapTolerance, $"{context}: the box should not be pushed into the wall's margin");
                AssertDisplacement(start, Vector2.zero, PositionTolerance, context);
                AssertTouchingBox(box, context);
                AssertVelocity(Vector2.zero, context + ": blocked solid, the pusher should stop (wall rule)");
            }
        }

        // ---- D3, D4: dynamic bodies moving into the body ----
        //
        // Box2D brings a dynamic body to rest at its own contact distance, which is closer than the
        // KinematicMotion2D's margin. The KinematicMotion2D then steps back out of what it sees as an
        // overlap, by up to its margin, and that step can take it into the margin of what is behind it.
        // These tests accept that much: the body may yield by up to its margin, but no more.

        [UnityTest]
        public IEnumerator DynamicBodyLandsOnBody()
        {
            yield return SpawnBody();
            AddSurface(0f, margin);
            Rigidbody2D box = AddDynamicBox("Box", Vector2.up * 1.3f, checkMargin: false);
            yield return Settle();
            AssertGroundedPrecondition(true);
            overlapTolerance = margin;

            Vector2 start = body.position;
            for (int i = 0; i < 40; i++)
            {
                yield return Step();
                AssertDisplacement(start, Vector2.zero, margin, $"Step {body.stepsTaken}: the body should barely be moved by the box");
            }
            Assert.That(box.linearVelocity.magnitude, Is.LessThan(0.05f), "The box should have come to rest");
            float boxBottom = box.position.y - 0.5f;
            float bodyTop = body.position.y + 0.5f;
            Assert.That(boxBottom - bodyTop, Is.InRange(-OverlapTolerance, 0.03f), "The box should rest on top of the body");
        }

        [UnityTest]
        public IEnumerator DynamicBodyBumpsIntoBody()
        {
            yield return SpawnBody();
            Rigidbody2D box = AddDynamicBox("Box", Vector2.right * 3f, gravityScale: 0f, checkMargin: false);
            yield return Settle();
            box.linearVelocity = Vector2.left * Speed;

            Vector2 start = body.position;
            for (int i = 0; i < 40; i++)
            {
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                AssertDisplacement(start, Vector2.zero, margin, context + ": the body should barely be moved by the box");
                Assert.GreaterOrEqual(box.position.x - 0.5f, body.position.x + 0.5f - OverlapTolerance, $"{context}: the box should not pass into the body");
            }
        }

        // ---- D5: standing on a dynamic body ----

        [UnityTest]
        public IEnumerator StandsOnDynamicBody()
        {
            yield return SpawnBody(gravityMultiplier: 1f);
            // A box resting on the floor, its top margin + 0.1 below the body.
            Rigidbody2D box = AddDynamicBox("Box", Vector2.down * (1f + margin + 0.1f));
            AddSurface(0f, 1f + margin + 0.1f);
            yield return Settle();
            yield return Simulate(SettleSteps);
            AssertGroundedPrecondition(false);

            for (int i = 0; i < 30 && !body.isGrounded; i++)
            {
                yield return Step();
            }
            Assert.IsTrue(body.isGrounded, "The body should have landed on the box");
            Vector2 boxRest = box.position;
            for (int i = 0; i < 5; i++)
            {
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                AssertTouchingBox(box, context);
                AssertVelocity(Vector2.zero, body.groundVelocity, context + " (groundVelocity)");
                AssertMoved(box, boxRest, Vector2.zero, context + ": the body should not press the box into the floor");
            }

            Launch(Vector2.right * 2f);
            for (int i = 0; i < 5; i++)
            {
                yield return Step();
                string context = $"Step {body.stepsTaken}, walking";
                Assert.IsTrue(body.isGrounded, $"{context}: the body should stay on the box");
                AssertMoved(box, boxRest, Vector2.zero, context + ": walking on the box should not drag it");
            }
        }
    }
}
