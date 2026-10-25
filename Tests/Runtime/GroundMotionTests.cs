using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PuzzleBox.Tests
{
    /// <summary>
    /// Ground motion: a body standing on a moving KinematicMotion2D (a platform) is carried by it.
    ///
    /// The rules these tests encode:
    /// - While grounded on a platform, a body moves by its own velocity plus the platform's motion, and
    ///   its velocity is relative to the platform. Landing keeps the body's velocity, now measured
    ///   relative to the platform, so a body that lands with no sideways speed rides along with it.
    /// - The carry is collision-checked against everything except the platform itself, and is split
    ///   along and across the platform's surface, so an obstacle that blocks one part leaves the other.
    /// - A sticky platform (the default) drags its riders down however fast it descends. A non-sticky
    ///   one never carries them down: they fall under gravity instead.
    /// - Leaving a platform (walking off it or launching from it) adds the platform's velocity to the
    ///   body's, so world momentum is kept, except that a non-sticky platform dropping away hands over
    ///   only its horizontal velocity. Once off the ground, a body moves only by its own velocity: the
    ///   platform's motion is never counted twice.
    /// - lastGroundVelocity is the world velocity (own plus platform) at the last grounded step.
    ///
    /// Gravity is off unless a test says otherwise. Every step also checks that no body's margin
    /// overlaps anything, including the platform.
    /// </summary>
    public class GroundMotionTests : CollideAndSlideFixture
    {
        const float PlatformSpeed = 5f;
        const float FastDescent = 10f;
        const int CarrySteps = 10;

        static readonly (string name, Vector2 direction)[] Compass =
        {
            ("Right", new Vector2(1f, 0f)),
            ("UpRight", new Vector2(1f, 1f).normalized),
            ("Up", new Vector2(0f, 1f)),
            ("UpLeft", new Vector2(-1f, 1f).normalized),
            ("Left", new Vector2(-1f, 0f)),
            ("DownLeft", new Vector2(-1f, -1f).normalized),
            ("Down", new Vector2(0f, -1f)),
            ("DownRight", new Vector2(1f, -1f).normalized),
        };

        static readonly (string name, Vector2 direction)[] Axes =
        {
            ("Right", Vector2.right),
            ("Left", Vector2.left),
            ("Up", Vector2.up),
            ("Down", Vector2.down),
        };

        static readonly PlatformOrder[] Orders = { PlatformOrder.PlatformFirst, PlatformOrder.PlatformLast };

        static TestCaseData Case(string name, params object[] args)
        {
            return new TestCaseData(args).SetName(name).Returns(null);
        }

        static Vector2 Gravity => Physics2D.gravity;

        // ---- C1: a resting rider is carried exactly ----

        static IEnumerable<TestCaseData> RestingCases()
        {
            foreach ((string name, Vector2 direction) in Compass)
                foreach (PlatformOrder order in Orders)
                    yield return Case($"Carry_FlatPlatform_{name}_{order}", direction, order);
        }

        [UnityTest, TestCaseSource(nameof(RestingCases))]
        public IEnumerator CarriesRestingRider(Vector2 direction, PlatformOrder order)
        {
            yield return SpawnBody();
            Surface top = AddPlatform(0f, margin, order);
            yield return Settle();
            AssertGroundedPrecondition(true);

            yield return Ride(top, direction * PlatformSpeed, Vector2.zero, order);
        }

        // ---- C2: a walking rider moves by its own velocity plus the carry ----

        static IEnumerable<TestCaseData> WalkingCases()
        {
            foreach (float walk in new[] { 3f, -3f })
                foreach ((string name, Vector2 direction) in Axes)
                    foreach (PlatformOrder order in Orders)
                        yield return Case($"Carry_Walking{(walk > 0 ? "Right" : "Left")}_Platform{name}_{order}", new Vector2(walk, 0f), direction, order);
        }

        [UnityTest, TestCaseSource(nameof(WalkingCases))]
        public IEnumerator CarriesWalkingRider(Vector2 walk, Vector2 direction, PlatformOrder order)
        {
            yield return SpawnBody();
            Surface top = AddPlatform(0f, margin, order);
            yield return Settle();
            AssertGroundedPrecondition(true);

            Launch(walk);
            yield return Ride(top, direction * PlatformSpeed, walk, order);
        }

        // ---- C3: tilted platforms ----

        static IEnumerable<TestCaseData> TiltedCases()
        {
            foreach (float angle in new[] { 20f, -20f })
                foreach ((string name, Vector2 direction) in Axes)
                    foreach (PlatformOrder order in Orders)
                        yield return Case($"Carry_TiltedPlatform{angle:+0;-0}_{name}_{order}", angle, direction, order);
        }

        [UnityTest, TestCaseSource(nameof(TiltedCases))]
        public IEnumerator CarriesRiderOnTiltedPlatform(float angle, Vector2 direction, PlatformOrder order)
        {
            yield return SpawnBody();
            Surface top = AddPlatform(angle, margin, order);
            yield return Settle();
            AssertGroundedPrecondition(true);

            yield return Ride(top, direction * PlatformSpeed, Vector2.zero, order);
        }

        // ---- C4: useGroundMotion = false ----

        static IEnumerable<TestCaseData> DisabledCases()
        {
            yield return Case("NoCarry_UseGroundMotionOff_Right", Vector2.right);
            yield return Case("NoCarry_UseGroundMotionOff_Left", Vector2.left);
        }

        [UnityTest, TestCaseSource(nameof(DisabledCases))]
        public IEnumerator DoesNotCarryWhenGroundMotionIsOff(Vector2 direction)
        {
            yield return SpawnBody();
            body.useGroundMotion = false;
            Surface top = AddPlatform(0f, margin, PlatformOrder.PlatformFirst);
            yield return Settle();
            AssertGroundedPrecondition(true);

            Vector2 platformVelocity = direction * PlatformSpeed;
            top.owner.velocity = platformVelocity;
            for (int i = 0; i < CarrySteps; i++)
            {
                Vector2 start = body.position;
                Vector2 platformStart = top.owner.position;
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                AssertDisplacement(top.owner, platformStart, platformVelocity * FixedDeltaTime, PositionTolerance, context);
                AssertDisplacement(start, Vector2.zero, PositionTolerance, context);
                AssertInContact(top, context);
                AssertVelocity(Vector2.zero, body.groundVelocity, context + " (groundVelocity)");
            }
        }

        // ---- S1-S6: sticky ----

        [UnityTest]
        public IEnumerator StickyPlatformDragsRiderDownFasterThanFreeFall()
        {
            yield return SpawnBody(gravityMultiplier: 1f);
            Surface top = AddPlatform(0f, margin, PlatformOrder.PlatformFirst);
            yield return Settle();
            AssertGroundedPrecondition(true);
            Assert.IsTrue(top.owner.sticky, "Precondition: platforms are sticky by default");

            yield return Ride(top, Vector2.down * FastDescent, Vector2.zero, PlatformOrder.PlatformFirst);
        }

        [UnityTest]
        public IEnumerator NonStickyPlatformDropsAwayFromRider()
        {
            yield return SpawnBody(gravityMultiplier: 1f);
            Surface top = AddPlatform(0f, margin, PlatformOrder.PlatformFirst);
            top.owner.sticky = false;
            yield return Settle();
            AssertGroundedPrecondition(true);

            top.owner.velocity = Vector2.down * FastDescent;
            for (int i = 0; i < CarrySteps; i++)
            {
                Vector2 start = body.position;
                Vector2 velocityBefore = body.velocity;
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                // Never carried down: the body only moves by its own velocity.
                AssertDisplacement(start, body.velocity * FixedDeltaTime, PositionTolerance, context);
                if (!body.isGrounded)
                {
                    // Falls freely. The platform's downward velocity is not handed over.
                    AssertVelocity(velocityBefore + Gravity * FixedDeltaTime, context);
                }
            }
            Assert.IsFalse(body.isGrounded, "The body should be falling, left behind by the platform");
            AssertVelocity(Vector2.zero, body.groundVelocity, "groundVelocity once airborne");
            Assert.Greater(Gap(top), margin + 0.5f, "The platform should have dropped away from the body");
        }

        static IEnumerable<TestCaseData> DiagonalDropCases()
        {
            foreach (PlatformOrder order in Orders)
                yield return Case($"NonSticky_DropsAwayDiagonally_{order}", order);
        }

        [UnityTest, TestCaseSource(nameof(DiagonalDropCases))]
        public IEnumerator NonStickyPlatformDroppingDiagonallyKeepsHorizontalMotion(PlatformOrder order)
        {
            yield return SpawnBody(gravityMultiplier: 1f);
            Surface top = AddPlatform(0f, margin, order);
            top.owner.sticky = false;
            yield return Settle();
            AssertGroundedPrecondition(true);

            Vector2 platformVelocity = new Vector2(1f, -1f).normalized * FastDescent;
            top.owner.velocity = platformVelocity;
            for (int i = 0; i < CarrySteps; i++)
            {
                Vector2 start = body.position;
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                // Horizontally, the body keeps the platform's speed throughout: carried while on it,
                // then by the inherited velocity, and never both in one step.
                Vector2 moved = body.position - start;
                Assert.That(moved.x, Is.EqualTo(platformVelocity.x * FixedDeltaTime).Within(PositionTolerance),
                    $"{context}: horizontal displacement");
                // Vertically, it is never carried down: only its own velocity moves it.
                Assert.That(moved.y, Is.EqualTo(body.velocity.y * FixedDeltaTime).Within(PositionTolerance),
                    $"{context}: vertical displacement");
            }
            Assert.IsFalse(body.isGrounded, "The body should be falling, left behind by the platform");
            Assert.That(body.velocity.x, Is.EqualTo(platformVelocity.x).Within(RelativeVelocityTolerance * FastDescent),
                "The platform's horizontal velocity should have been handed over");
        }

        [UnityTest]
        public IEnumerator NonStickyPlatformStillCarriesRiderUp()
        {
            yield return SpawnBody();
            Surface top = AddPlatform(0f, margin, PlatformOrder.PlatformFirst);
            top.owner.sticky = false;
            yield return Settle();
            AssertGroundedPrecondition(true);

            yield return Ride(top, Vector2.up * PlatformSpeed, Vector2.zero, PlatformOrder.PlatformFirst);
        }

        [UnityTest]
        public IEnumerator RiderKeepsUpWithSlowNonStickyDescent()
        {
            const float descent = 1f;
            const int steps = 60;
            const float keepUp = 0.05f;

            yield return SpawnBody(gravityMultiplier: 1f);
            Surface top = AddPlatform(0f, margin, PlatformOrder.PlatformFirst);
            top.owner.sticky = false;
            yield return Settle();
            AssertGroundedPrecondition(true);

            Vector2 start = body.position;
            top.owner.velocity = Vector2.down * descent;
            for (int i = 0; i < steps; i++)
            {
                yield return Step();
                Assert.LessOrEqual(Gap(top), margin + keepUp,
                    $"Step {body.stepsTaken}: the body should fall after the platform, not drift away from it");
            }
            float platformDrop = descent * FixedDeltaTime * steps;
            Assert.That(start.y - body.position.y, Is.EqualTo(platformDrop).Within(keepUp),
                "The body should descend as far as the platform did");
        }

        [UnityTest]
        public IEnumerator StickyPlatformLeavesRiderOnLedges()
        {
            yield return SpawnBody();
            // A narrow platform drops through a slot between two ledges that the wider body overhangs.
            Surface top = AddPlatform(0f, margin, PlatformOrder.PlatformFirst, width: 0.4f);
            Surface leftLedge = AddSurface(0f, margin + 0.15f, length: 3f, tangentOffset: -1.8f);
            Surface rightLedge = AddSurface(0f, margin + 0.15f, length: 3f, tangentOffset: 1.8f);
            yield return Settle();
            AssertGroundedPrecondition(true);
            AssertPlatformClearPrecondition(top);

            Vector2 platformVelocity = Vector2.down * PlatformSpeed;
            top.owner.velocity = platformVelocity;
            for (int i = 0; i < CarrySteps; i++)
            {
                Vector2 platformStart = top.owner.position;
                yield return Step();
                AssertDisplacement(top.owner, platformStart, platformVelocity * FixedDeltaTime, PositionTolerance,
                    $"Step {body.stepsTaken}: the platform should carry on through the slot");
            }
            AssertInContact(leftLedge, "After the platform has gone");
            AssertInContact(rightLedge, "After the platform has gone");
            AssertVelocity(Vector2.zero, body.groundVelocity, "groundVelocity on the ledges");
        }

        // ---- O1-O2: obstacles block the carry ----

        static IEnumerable<TestCaseData> WallCases()
        {
            foreach (float angle in new[] { 0f, 20f, -20f })
                foreach ((string name, Vector2 direction) in new[] { ("Right", Vector2.right), ("Left", Vector2.left) })
                    foreach (PlatformOrder order in Orders)
                        yield return Case($"WallBlocksCarry_Platform{angle:+0;-0;0}_{name}_{order}", angle, direction, order);
        }

        /// <summary>
        /// The platform passes under a wall that only the rider reaches. The rider stops at the wall and
        /// the platform slides on beneath it. On a tilted platform the surface under the rider rises or
        /// falls as the platform moves, and the rider must follow it: measured in the platform's frame,
        /// it slides along the surface and never leaves it.
        /// </summary>
        [UnityTest, TestCaseSource(nameof(WallCases))]
        public IEnumerator WallStopsCarryButRiderStaysOnPlatform(float angle, Vector2 direction, PlatformOrder order)
        {
            yield return SpawnBody();
            Surface top = AddPlatform(angle, margin, order);
            Surface wall = AddWallAtRiderHeight(direction, margin + 0.15f);
            yield return Settle();
            AssertGroundedPrecondition(true);
            AssertPlatformClearPrecondition(top);

            Vector2 platformVelocity = direction * PlatformSpeed;
            top.owner.velocity = platformVelocity;
            for (int i = 0; i < CarrySteps; i++)
            {
                Vector2 platformStart = top.owner.position;
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                if (i == 0)
                {
                    AssertOrderPrecondition(top.owner, order);
                }
                AssertDisplacement(top.owner, platformStart, platformVelocity * FixedDeltaTime, PositionTolerance, context);
                AssertInContact(top, context);
            }
            AssertInContact(wall, "After the platform has moved on");
            AssertVelocity(Vector2.zero, "After the platform has moved on");
        }

        /// <summary>
        /// A vertical wall on the side of <paramref name="direction"/>, whose lower end stays well above
        /// any platform the body stands on, so only the body can reach it.
        /// </summary>
        Surface AddWallAtRiderHeight(Vector2 direction, float gap)
        {
            float angle = direction.x > 0f ? -90f : 90f;
            Vector2 tangent = SlideRules.Tangent(SlideRules.Normal(angle));
            // Centre the 2-unit wall at y = 0.85, so it spans y in [-0.15, 1.85] next to a body at the
            // origin. Two constraints meet here. The lower end must clear the platform across the wall's
            // whole thickness, and a 20 degree platform rising towards the wall climbs quickly under it,
            // hence the thin wall (the platform is near y = -0.42 under its far face). And the body must
            // not slip under the wall: on a platform falling away from the wall it follows the surface
            // down by about 0.27 over the test, leaving its top edge near y = 0.23.
            return AddSurface(angle, gap, length: 2f, tangentOffset: 0.85f / tangent.y, thickness: 0.1f);
        }

        /// <summary>
        /// Checks that <paramref name="platform"/> starts at least a margin away from every static surface.
        /// (Physics2D.Distance reports the gap minus Box2D's skin, which is 0.02 for two boxes, so a
        /// positive distance means at least that much clearance.)
        /// </summary>
        void AssertPlatformClearPrecondition(Surface platform)
        {
            foreach (Surface surface in surfaces)
            {
                if (surface.owner != null)
                {
                    continue;
                }
                float distance = Physics2D.Distance(platform.collider, surface.collider).distance;
                Assert.Greater(distance, 0f, $"Precondition: {platform.name} must start clear of {surface.name}");
            }
        }

        // ---- L1-L5: landing, leaving, lastGroundVelocity ----

        static IEnumerable<TestCaseData> LandingCases()
        {
            foreach (PlatformOrder order in Orders)
                yield return Case($"LandOnMovingPlatform_{order}", order);
        }

        /// <summary>
        /// A body falling straight down onto a platform moving sideways lands at rest relative to the
        /// platform, and from then on rides along with it.
        /// </summary>
        [UnityTest, TestCaseSource(nameof(LandingCases))]
        public IEnumerator LandingOnMovingPlatformRidesAlong(PlatformOrder order)
        {
            const float platformSpeed = 3f;
            yield return SpawnBody(gravityMultiplier: 1f);
            Surface top = AddPlatform(0f, margin + 0.05f, order);
            yield return Settle();
            AssertGroundedPrecondition(false);

            top.owner.velocity = Vector2.right * platformSpeed;
            int landingStep = -1;
            for (int i = 0; i < 20 && landingStep < 0; i++)
            {
                Vector2 start = body.position;
                yield return Step();
                if (body.isGrounded)
                {
                    landingStep = body.stepsTaken;
                }
                else
                {
                    Assert.That(body.position.x - start.x, Is.EqualTo(0f).Within(PositionTolerance),
                        $"Step {body.stepsTaken}: falling straight down, the body should not move sideways");
                }
            }
            Assert.Greater(landingStep, 0, "The body never landed on the platform");
            Assert.That(body.velocity.x, Is.EqualTo(0f).Within(RelativeVelocityTolerance),
                $"Step {landingStep}: the body should land at rest relative to the platform");
            AssertVelocity(Vector2.right * platformSpeed, body.groundVelocity, $"Step {landingStep} (groundVelocity)");

            for (int i = 0; i < SteadyStateSteps; i++)
            {
                Vector2 start = body.position;
                Vector2 platformStart = top.owner.position;
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                Assert.That(body.position.x - start.x, Is.EqualTo(top.owner.position.x - platformStart.x).Within(PositionTolerance),
                    $"{context}: the body should ride along with the platform");
                AssertInContact(top, context);
            }
        }

        static readonly (string name, Vector2 velocity)[] PlatformVelocities =
        {
            ("MovingRight", new Vector2(3f, 0f)),
            ("MovingLeft", new Vector2(-3f, 0f)),
            ("Rising", new Vector2(0f, 2f)),
        };

        static IEnumerable<TestCaseData> LeavingCases(string prefix)
        {
            foreach ((string name, Vector2 velocity) in PlatformVelocities)
                foreach (PlatformOrder order in Orders)
                    yield return Case($"{prefix}_Platform{name}_{order}", velocity, order);
        }

        static IEnumerable<TestCaseData> WalkOffCases() => LeavingCases("WalkOffEdge");

        [UnityTest, TestCaseSource(nameof(WalkOffCases))]
        public IEnumerator WalkingOffEdgeInheritsPlatformVelocity(Vector2 platformVelocity, PlatformOrder order)
        {
            Vector2 walk = new Vector2(10f, 0f);
            yield return SpawnBody(gravityMultiplier: 1f);
            Surface top = AddPlatform(0f, margin, order, width: 1.6f);
            yield return Settle();
            AssertGroundedPrecondition(true);

            top.owner.velocity = platformVelocity;
            Launch(walk);
            yield return LeaveAndCheck(walk, platformVelocity, "walked off the edge");
        }

        static IEnumerable<TestCaseData> LaunchCases() => LeavingCases("Launch");

        [UnityTest, TestCaseSource(nameof(LaunchCases))]
        public IEnumerator LaunchingInheritsPlatformVelocity(Vector2 platformVelocity, PlatformOrder order)
        {
            Vector2 launch = new Vector2(0f, 8f);
            yield return SpawnBody(gravityMultiplier: 1f);
            Surface top = AddPlatform(0f, margin, order);
            yield return Settle();
            AssertGroundedPrecondition(true);

            top.owner.velocity = platformVelocity;
            yield return Step();
            yield return Step();
            Launch(launch);
            yield return LeaveAndCheck(launch, platformVelocity, "launched");
        }

        /// <summary>
        /// Steps until the body is airborne, then checks it kept the platform's velocity, and that from
        /// the step it left onwards it moves only by its own velocity.
        /// </summary>
        IEnumerator LeaveAndCheck(Vector2 ownVelocity, Vector2 platformVelocity, string how)
        {
            bool left = false;
            for (int i = 0; i < 20 && !left; i++)
            {
                Vector2 start = body.position;
                yield return Step();
                if (!body.isGrounded)
                {
                    left = true;
                    string context = $"Step {body.stepsTaken}, having {how}";
                    AssertVelocity(ownVelocity + platformVelocity + Gravity * FixedDeltaTime, context);
                    AssertDisplacement(start, body.velocity * FixedDeltaTime, PositionTolerance, context);
                }
            }
            Assert.IsTrue(left, $"The body never {how}");

            for (int i = 0; i < 3; i++)
            {
                Vector2 start = body.position;
                Vector2 velocityBefore = body.velocity;
                yield return Step();
                string context = $"Step {body.stepsTaken}, in the air";
                AssertVelocity(velocityBefore + Gravity * FixedDeltaTime, context);
                AssertDisplacement(start, body.velocity * FixedDeltaTime, PositionTolerance, context);
            }
        }

        [UnityTest]
        public IEnumerator LastGroundVelocityIsWorldVelocity()
        {
            Vector2 walk = new Vector2(2f, 0f);
            Vector2 platformVelocity = new Vector2(3f, 0f);
            yield return SpawnBody();
            Surface top = AddPlatform(0f, margin, PlatformOrder.PlatformFirst);
            yield return Settle();
            AssertGroundedPrecondition(true);

            top.owner.velocity = platformVelocity;
            Launch(walk);
            for (int i = 0; i < 3; i++)
            {
                yield return Step();
                AssertVelocity(walk + platformVelocity, body.lastGroundVelocity, $"Step {body.stepsTaken} (lastGroundVelocity, riding)");
            }

            Launch(new Vector2(walk.x, 8f));
            for (int i = 0; i < 3; i++)
            {
                yield return Step();
            }
            Assert.IsFalse(body.isGrounded, "Precondition: the body should have launched");
            AssertVelocity(walk + platformVelocity, body.lastGroundVelocity, "In the air (lastGroundVelocity)");
        }

        // ---- K1: stacked riders ----

        static IEnumerable<TestCaseData> StackedCases()
        {
            foreach ((string name, Vector2 direction) in new[] { ("Right", Vector2.right), ("Up", Vector2.up), ("Down", Vector2.down) })
                foreach (PlatformOrder order in Orders)
                    yield return Case($"Stacked_{name}_{order}", direction, order);
        }

        [UnityTest, TestCaseSource(nameof(StackedCases))]
        public IEnumerator CarriesStackedRiders(Vector2 direction, PlatformOrder order)
        {
            yield return SpawnBody();
            SteppedKinematicMotion2D lower = SpawnRiderOn(body, margin);
            Surface top = AddPlatform(0f, margin, order, on: lower);
            Surface lowerTop = surfaces.Find(s => s.owner == lower);
            yield return Settle();
            AssertGroundedPrecondition(lower, true);
            AssertGroundedPrecondition(true);

            Vector2 platformVelocity = direction * PlatformSpeed;
            top.owner.velocity = platformVelocity;
            for (int i = 0; i < CarrySteps; i++)
            {
                Vector2 start = body.position;
                Vector2 lowerStart = lower.position;
                Vector2 platformStart = top.owner.position;
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                if (i == 0)
                {
                    AssertOrderPrecondition(top.owner, order);
                }
                Vector2 carried = top.owner.position - platformStart;
                AssertDisplacement(top.owner, platformStart, platformVelocity * FixedDeltaTime, PositionTolerance, context);
                AssertDisplacement(lower, lowerStart, carried, PositionTolerance, context);
                AssertDisplacement(start, carried, PositionTolerance, context);
                AssertInContact(lower, top, context);
                AssertInContact(lowerTop, context);
            }
        }

        // ---- Shared ----

        /// <summary>
        /// Moves the platform at <paramref name="platformVelocity"/> for <see cref="CarrySteps"/> steps and
        /// checks after each that the platform moved freely, that the rider moved by its own velocity plus
        /// the platform's motion while staying on it, and that its velocity stayed relative.
        /// </summary>
        IEnumerator Ride(Surface top, Vector2 platformVelocity, Vector2 ownVelocity, PlatformOrder order)
        {
            SteppedKinematicMotion2D platform = top.owner;
            platform.velocity = platformVelocity;
            for (int i = 0; i < CarrySteps; i++)
            {
                Vector2 start = body.position;
                Vector2 platformStart = platform.position;
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                if (i == 0)
                {
                    AssertOrderPrecondition(platform, order);
                }
                // The platform is not held back by its own rider.
                AssertDisplacement(platform, platformStart, platformVelocity * FixedDeltaTime, PositionTolerance, context);
                Vector2 carried = platform.position - platformStart;
                AssertDisplacement(start, carried + ownVelocity * FixedDeltaTime, PositionTolerance, context);
                AssertInContact(top, context);
                AssertVelocity(ownVelocity, context);
                AssertVelocity(platformVelocity, body.groundVelocity, context + " (groundVelocity)");
            }
        }

        void AssertOrderPrecondition(SteppedKinematicMotion2D platform, PlatformOrder order)
        {
            bool platformFirst = platform.lastSequence < body.lastSequence;
            Assert.AreEqual(order == PlatformOrder.PlatformFirst, platformFirst,
                $"Precondition: the platform's FixedUpdate should run {(order == PlatformOrder.PlatformFirst ? "before" : "after")} the body's");
        }
    }
}
