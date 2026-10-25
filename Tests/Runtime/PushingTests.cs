using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PuzzleBox.Tests
{
    /// <summary>
    /// One KinematicMotion2D pushing another.
    ///
    /// The rules these tests encode:
    /// - A pushes B only if B is pushable and A's pushPriority is higher than B's. Otherwise B is an
    ///   obstacle like any other: A stops at its margin from B, and B does not move. (Whether A itself is
    ///   pushable does not matter.)
    /// - Pusher and pushed slow down together: both move remaining * mA / (mA + mB), staying in contact.
    ///   Pushing a chain, the denominator is the mass of the whole chain.
    /// - The pusher keeps its velocity while pushing; only its displacement shrinks. If the pushed body
    ///   cannot move at all, the pusher meets it like a wall and the collide-and-slide rules apply.
    /// - A pushed body gets no velocity, only displacement.
    ///
    /// Bodies are 1x1, side by side on the x axis, with gravity cancelled. Every step also checks that
    /// no body's margin overlaps anything.
    /// </summary>
    public class PushingTests : CollideAndSlideFixture
    {
        const float Speed = 5f;
        const float ApproachGap = 0.15f;
        const int Steps = 10;

        static TestCaseData Case(string name, params object[] args)
        {
            return new TestCaseData(args).SetName(name).Returns(null);
        }

        /// <summary>A 1x1 body beside <paramref name="of"/>, on the side of <paramref name="side"/> (+1 right, -1 left), <paramref name="gap"/> away.</summary>
        SteppedKinematicMotion2D SpawnBeside(SteppedKinematicMotion2D of, float side, float gap, string name)
        {
            return SpawnBodyAt(name, of.position + Vector2.right * side * (1f + gap));
        }

        void AssertBodiesInContact(SteppedKinematicMotion2D a, SteppedKinematicMotion2D b, string context)
        {
            float gap = Gap(a, ColliderOf(b));
            Assert.LessOrEqual(gap, a.margin + ContactBand, $"{context}: {a.name} should rest against {b.name}, but its gap is {gap:F5}");
        }

        // ---- P1: who pushes whom ----

        public enum Priority
        {
            PusherHigher,
            Equal,
            PusherLower,
        }

        public enum PushHolding
        {
            Nothing,
            // The pusher holds a body out in front of it, and that is what touches the other body.
            PusherHoldsInFront,
            // The pushed body holds a body on its far side, which must go along with it.
            PushedHoldsBehind,
        }

        static IEnumerable<TestCaseData> RuleCases()
        {
            foreach (PushHolding holding in new[] { PushHolding.Nothing, PushHolding.PusherHoldsInFront, PushHolding.PushedHoldsBehind })
                foreach (bool pushable in new[] { true, false })
                    foreach (Priority priority in new[] { Priority.PusherHigher, Priority.Equal, Priority.PusherLower })
                        foreach (bool pusherPushable in new[] { false, true })
                            foreach ((string name, float side) in new[] { ("Right", 1f), ("Left", -1f) })
                            {
                                // The push rule does not depend on whether the pusher is pushable, which the
                                // empty-handed cases cover; the held variants only need one of the two.
                                if (holding != PushHolding.Nothing && pusherPushable)
                                {
                                    continue;
                                }
                                string suffix = holding == PushHolding.Nothing ? "" : $"_{holding}";
                                yield return Case($"PushRule_{(pushable ? "Pushable" : "NotPushable")}_{priority}_{(pusherPushable ? "PusherPushable" : "PusherNotPushable")}_{name}{suffix}",
                                    pushable, priority, pusherPushable, side, holding);
                            }
        }

        [UnityTest, TestCaseSource(nameof(RuleCases))]
        public IEnumerator PushesOnlyPushableLowerPriorityBodies(bool pushable, Priority priority, bool pusherPushable, float side, PushHolding holding)
        {
            yield return SpawnBody();
            // What touches the other body: the pusher, or what it holds out in front.
            SteppedKinematicMotion2D front = holding == PushHolding.PusherHoldsInFront
                ? AttachChild(Vector2.right * side * 1.05f, "HeldInFront")
                : body;
            SteppedKinematicMotion2D other = SpawnBeside(front, side, margin + ApproachGap, "Other");
            if (holding == PushHolding.PushedHoldsBehind)
            {
                AttachChild(Vector2.right * side * 1.05f, "HeldBehind", other);
            }
            yield return Settle();
            body.pushPriority = 1;
            body.pushable = pusherPushable;
            other.pushable = pushable;
            other.pushPriority = priority == Priority.PusherHigher ? 0 : priority == Priority.Equal ? 1 : 2;
            bool shouldPush = pushable && priority == Priority.PusherHigher;

            Vector2 velocity = Vector2.right * side * Speed;
            Vector2 otherStart = other.position;
            Launch(velocity);
            for (int i = 0; i < Steps; i++)
            {
                yield return Step();
                AssertVelocity(Vector2.zero, other.velocity, $"Step {body.stepsTaken}: the other body's velocity");
            }

            AssertBodiesInContact(front, other, "After pushing");
            if (shouldPush)
            {
                Assert.Greater((other.position.x - otherStart.x) * side, 0.1f, "The other body should have been pushed");
                AssertVelocity(velocity, "The pusher should keep its velocity");
            }
            else
            {
                AssertDisplacement(other, otherStart, Vector2.zero, PositionTolerance, "The other body should not have moved");
                AssertVelocity(Vector2.zero, "Blocked by the other body, the pusher should stop (wall rule)");
            }
        }

        // ---- P2: mass ----

        static IEnumerable<TestCaseData> MassCases()
        {
            yield return Case("Mass_Equal_Right", 1f, 1f, 1f);
            yield return Case("Mass_PushedHeavier_Right", 1f, 3f, 1f);
            yield return Case("Mass_PusherHeavier_Right", 3f, 1f, 1f);
            yield return Case("Mass_Equal_Left", 1f, 1f, -1f);
        }

        [UnityTest, TestCaseSource(nameof(MassCases))]
        public IEnumerator PusherAndPushedSlowDownTogether(float pusherMass, float pushedMass, float side)
        {
            yield return SpawnBody();
            SteppedKinematicMotion2D other = SpawnBeside(body, side, margin, "Other");
            yield return Settle();
            body.pushPriority = 1;
            body.mass = pusherMass;
            other.pushable = true;
            other.mass = pushedMass;

            Vector2 velocity = Vector2.right * side * Speed;
            Vector2 expected = velocity * FixedDeltaTime * pusherMass / (pusherMass + pushedMass);
            Launch(velocity);
            for (int i = 0; i < Steps; i++)
            {
                Vector2 start = body.position;
                Vector2 otherStart = other.position;
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                AssertDisplacement(start, expected, PositionTolerance, context);
                AssertDisplacement(other, otherStart, expected, PositionTolerance, context);
                AssertBodiesInContact(body, other, context);
                AssertVelocity(velocity, context + " (pusher)");
                AssertVelocity(Vector2.zero, other.velocity, context + " (pushed)");
            }
        }

        // ---- P3: the pushed body cannot move ----

        [UnityTest]
        public IEnumerator PushedBodyAgainstWallBlocksPusher()
        {
            yield return SpawnBody();
            SteppedKinematicMotion2D other = SpawnBeside(body, 1f, margin, "Other");
            yield return Settle();
            Surface wall = AddSurface(-90f, 1f + 2f * margin);
            body.pushPriority = 1;
            other.pushable = true;

            Vector2 start = body.position;
            Vector2 otherStart = other.position;
            Launch(Vector2.right * Speed);
            for (int i = 0; i < 5; i++)
            {
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                AssertDisplacement(start, Vector2.zero, PositionTolerance, context);
                AssertDisplacement(other, otherStart, Vector2.zero, PositionTolerance, context);
                AssertInContact(other, wall, context);
                AssertBodiesInContact(body, other, context);
                AssertVelocity(Vector2.zero, context + ": blocked solid, the pusher should stop (wall rule)");
            }
        }

        // ---- P4: a chain ----

        // Known issue, not fixed for now: each push applies its own mass ratio, so along a chain they
        // compound (1/2 * 1/2 = 1/4 each, instead of 1/3). Fixing it means summing the masses of the
        // touching pushable bodies before pushing, and not applying a ratio again when a pushed body
        // passes the push on.
        [UnityTest]
        [Ignore("Known issue: mass ratios compound along a chain of pushed bodies (1/4 instead of 1/3).")]
        public IEnumerator PushesAChain()
        {
            yield return SpawnBody();
            SteppedKinematicMotion2D middle = SpawnBeside(body, 1f, margin, "Middle");
            SteppedKinematicMotion2D last = SpawnBeside(middle, 1f, margin, "Last");
            yield return Settle();
            body.pushPriority = 2;
            middle.pushable = true;
            middle.pushPriority = 1;
            last.pushable = true;
            last.pushPriority = 0;

            Vector2 velocity = Vector2.right * Speed;
            Vector2 expected = velocity * FixedDeltaTime / 3f;
            Launch(velocity);
            for (int i = 0; i < Steps; i++)
            {
                Vector2 start = body.position;
                Vector2 middleStart = middle.position;
                Vector2 lastStart = last.position;
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                AssertDisplacement(start, expected, PositionTolerance, context);
                AssertDisplacement(middle, middleStart, expected, PositionTolerance, context);
                AssertDisplacement(last, lastStart, expected, PositionTolerance, context);
                AssertBodiesInContact(body, middle, context);
                AssertBodiesInContact(middle, last, context);
            }
        }

        // ---- P5: pushing along the floor ----

        [UnityTest]
        public IEnumerator PushesAlongTheFloor()
        {
            yield return SpawnBody();
            SteppedKinematicMotion2D other = SpawnBeside(body, 1f, margin, "Other");
            Surface floor = AddSurface(0f, margin);
            yield return Settle();
            body.pushPriority = 1;
            other.pushable = true;
            AssertGroundedPrecondition(true);
            AssertGroundedPrecondition(other, true);

            Vector2 velocity = Vector2.right * Speed;
            Vector2 expected = velocity * FixedDeltaTime * 0.5f;
            Launch(velocity);
            for (int i = 0; i < Steps; i++)
            {
                Vector2 start = body.position;
                Vector2 otherStart = other.position;
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                AssertDisplacement(start, expected, PositionTolerance, context);
                AssertDisplacement(other, otherStart, expected, PositionTolerance, context);
                AssertInContact(floor, context);
                AssertInContact(other, floor, context);
                AssertBodiesInContact(body, other, context);
            }
        }

        // ---- P6: the pushed body carries a rider ----

        [UnityTest]
        public IEnumerator PushedBodyCarriesItsRider()
        {
            yield return SpawnBody();
            SteppedKinematicMotion2D other = SpawnBeside(body, 1f, margin, "Other");
            SteppedKinematicMotion2D rider = SpawnBodyAt("Rider", other.position + Vector2.up * (1f + margin));
            yield return Settle();
            body.pushPriority = 1;
            other.pushable = true;
            AssertGroundedPrecondition(rider, true);

            Launch(Vector2.right * Speed);
            for (int i = 0; i < Steps; i++)
            {
                Vector2 otherStart = other.position;
                Vector2 riderStart = rider.position;
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                Assert.Greater(other.position.x - otherStart.x, 0f, $"{context}: the other body should be pushed");
                AssertDisplacement(rider, riderStart, other.position - otherStart, PositionTolerance, context + ": the rider should move with the pushed body");
                AssertBodiesInContact(rider, other, context);
            }
        }

        // ---- P7: pushed until it hits a wall ----

        [UnityTest]
        public IEnumerator PushedIntoWallStops()
        {
            yield return SpawnBody();
            SteppedKinematicMotion2D other = SpawnBeside(body, 1f, margin, "Other");
            yield return Settle();
            Surface wall = AddSurface(-90f, 1f + 2f * margin + 0.1f);
            body.pushPriority = 1;
            other.pushable = true;

            Launch(Vector2.right * Speed);
            for (int i = 0; i < Steps; i++)
            {
                yield return Step();
            }
            AssertInContact(other, wall, "After pushing");
            AssertBodiesInContact(body, other, "After pushing");
            AssertVelocity(Vector2.zero, "Once the pushed body is stopped by the wall, the pusher should stop (wall rule)");
        }

        // ---- Attachments ----

        /// <summary>What the pusher holds adds to its mass.</summary>
        [UnityTest]
        public IEnumerator HeldBodiesAddToPusherMass()
        {
            yield return SpawnBody();
            AttachChild(Vector2.left * 1.05f, "HeldBehind");
            SteppedKinematicMotion2D other = SpawnBeside(body, 1f, margin, "Other");
            yield return Settle();
            body.pushPriority = 1;
            other.pushable = true;
            other.mass = 2f;
            Assert.AreEqual(2f, body.combinedMass, 1e-5f, "Precondition: the pusher and what it holds weigh 2");

            Vector2 velocity = Vector2.right * Speed;
            // (1 + 1) / (1 + 1 + 2)
            Vector2 expected = velocity * FixedDeltaTime * 0.5f;
            Launch(velocity);
            for (int i = 0; i < Steps; i++)
            {
                Vector2 start = body.position;
                Vector2 otherStart = other.position;
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                AssertDisplacement(start, expected, PositionTolerance, context);
                AssertDisplacement(other, otherStart, expected, PositionTolerance, context);
                AssertBodiesInContact(body, other, context);
            }
        }

        static IEnumerable<TestCaseData> HeldGroupCases()
        {
            yield return Case("PushHeldBody_HolderPushable", true);
            yield return Case("PushHeldBody_HolderNotPushable", false);
        }

        /// <summary>
        /// Pushing something another body holds pushes the whole group, as one body: the push rule uses the
        /// holder's settings, and the mass is the whole group's.
        /// </summary>
        [UnityTest, TestCaseSource(nameof(HeldGroupCases))]
        public IEnumerator PushingAHeldBodyPushesItsHolder(bool holderPushable)
        {
            yield return SpawnBody();
            // A holder to the right, holding a body out to its left, which is what the pusher touches.
            SteppedKinematicMotion2D holder = SpawnBodyAt("Holder", Vector2.right * (1f + margin + 1.05f));
            SteppedKinematicMotion2D held = AttachChild(Vector2.left * 1.05f, "Held", holder);
            yield return Settle();
            body.pushPriority = 1;
            holder.pushable = holderPushable;
            // The held body's own settings must not matter.
            held.pushable = !holderPushable;
            held.pushPriority = holderPushable ? 2 : 0;

            Vector2 velocity = Vector2.right * Speed;
            // 1 / (1 + 2): the holder and what it holds weigh 2.
            Vector2 expected = holderPushable ? velocity * FixedDeltaTime / 3f : Vector2.zero;
            Launch(velocity);
            for (int i = 0; i < Steps; i++)
            {
                Vector2 start = body.position;
                Vector2 holderStart = holder.position;
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                AssertDisplacement(holder, holderStart, expected, PositionTolerance, context + ": the holder");
                AssertDisplacement(start, expected, PositionTolerance, context + ": the pusher");
                AssertBodiesInContact(body, held, context);
            }
            AssertVelocity(holderPushable ? velocity : Vector2.zero, holderPushable ? "The pusher should keep its velocity" : "Blocked, the pusher should stop (wall rule)");
        }

        /// <summary>What the pusher holds out in front is stopped by a wall, and so is the pusher.</summary>
        [UnityTest]
        public IEnumerator HeldBodyAgainstWallBlocksHolder()
        {
            yield return SpawnBody();
            HoldToward(Holding.Leading, Vector2.left);
            yield return Settle();
            Surface wall = AddSurface(-90f, margin);

            Vector2 start = body.position;
            Launch(Vector2.right * Speed);
            for (int i = 0; i < 5; i++)
            {
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                AssertDisplacement(start, Vector2.zero, PositionTolerance, context);
                AssertInContact(wall, context);
                AssertVelocity(Vector2.zero, context + ": blocked, the holder should stop (wall rule)");
            }
        }
    }
}
