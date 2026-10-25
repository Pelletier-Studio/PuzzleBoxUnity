using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Kind = PuzzleBox.Tests.ContactRecorder.Kind;

namespace PuzzleBox.Tests
{
    /// <summary>
    /// OnContactEnter / OnContactStay / OnContactExit, the events a KinematicMotion2D sends to its own
    /// GameObject about what it touches.
    ///
    /// The rules these tests encode, per collider touched:
    /// - One Enter on the step the body first comes within its margin of the collider.
    /// - A Stay on every following step while it remains there (and no Stay on the Enter step), however
    ///   it moves along the collider.
    /// - One Exit on the step it leaves, and nothing more after that.
    /// - Colliders beyond the margin, triggers, and colliders on layers outside collisionMask raise nothing.
    /// - The Contact describes the other side: self is the body, collider and rigidbody are the other
    ///   collider and its Rigidbody2D (null for static geometry), normal is the touched surface's normal,
    ///   and relativeVelocity is the body's velocity minus the other side's.
    /// </summary>
    public class ContactEventTests : CollideAndSlideFixture
    {
        const float Speed = 5f;
        const float ApproachGap = 0.15f;
        const int Steps = 6;
        const float NormalTolerance = 1e-3f;

        static readonly float[] AllAngles = { 0f, 30f, -30f, 90f, -90f, 70f, -70f, 110f, -110f, 180f, 150f, -150f };

        static TestCaseData Case(string name, params object[] args)
        {
            return new TestCaseData(args).SetName(name).Returns(null);
        }

        static bool IsGround(float angle) => SlideRules.Classify(SlideRules.Normal(angle)) == SurfaceKind.Ground;

        ContactRecorder Record(SteppedKinematicMotion2D target)
        {
            return target.gameObject.AddComponent<ContactRecorder>();
        }

        // ---- E1: approaching a surface ----

        static IEnumerable<TestCaseData> ApproachCases()
        {
            foreach (float angle in AllAngles)
                yield return Case($"Enter_Approaching_{SurfaceName(angle)}", angle);
        }

        [UnityTest, TestCaseSource(nameof(ApproachCases))]
        public IEnumerator EnterWhenReachingSurface(float angle)
        {
            yield return SpawnBody();
            Surface surface = AddSurface(angle, margin + ApproachGap);
            ContactRecorder recorder = Record(body);
            AssertGroundedPrecondition(false);

            Launch(-surface.normal * Speed);
            int enterStep = -1;
            Vector2 velocityAtContact = Vector2.zero;
            for (int i = 0; i < Steps; i++)
            {
                yield return Step();
                if (enterStep < 0 && Gap(surface) <= margin + ContactBand)
                {
                    enterStep = body.stepsTaken;
                    velocityAtContact = body.velocity;
                }
            }
            Assert.Greater(enterStep, 0, "Precondition: the body should have reached the surface");

            AssertTouching(recorder, surface.collider, enterStep, body.stepsTaken);
            KinematicMotion2D.Contact contact = recorder.Find(Kind.Enter, surface.collider)[0].contact;
            Assert.AreSame(body.gameObject, contact.self, "contact.self");
            Assert.IsNull(contact.rigidbody, "contact.rigidbody should be null for static geometry");
            Assert.That(Vector2.Distance(contact.normal, surface.normal), Is.LessThan(NormalTolerance),
                $"contact.normal should be the surface normal {Format(surface.normal)}, got {Format(contact.normal)}");
            AssertVelocity(velocityAtContact, contact.relativeVelocity, "contact.relativeVelocity");
        }

        // ---- E2: resting against a surface ----

        static IEnumerable<TestCaseData> RestingCases()
        {
            foreach (float angle in new[] { 0f, -90f, 180f })
                yield return Case($"Stay_Resting_{SurfaceName(angle)}", angle);
        }

        [UnityTest, TestCaseSource(nameof(RestingCases))]
        public IEnumerator StayWhileResting(float angle)
        {
            yield return SpawnBody();
            Surface surface = AddSurface(angle, margin);
            ContactRecorder recorder = Record(body);
            AssertGroundedPrecondition(IsGround(angle));

            for (int i = 0; i < Steps; i++)
            {
                yield return Step();
            }
            AssertTouching(recorder, surface.collider, 1, body.stepsTaken);
        }

        // ---- E3: sliding along a surface ----

        static IEnumerable<TestCaseData> SlidingCases()
        {
            foreach (float angle in AllAngles)
                yield return Case($"Stay_SlidingAlong_{SurfaceName(angle)}", angle);
        }

        [UnityTest, TestCaseSource(nameof(SlidingCases))]
        public IEnumerator StayWhileSliding(float angle)
        {
            yield return SpawnBody();
            Surface surface = AddSurface(angle, margin);
            ContactRecorder recorder = Record(body);
            AssertGroundedPrecondition(IsGround(angle));

            Launch(SlideRules.Tangent(surface.normal) * Speed);
            for (int i = 0; i < Steps; i++)
            {
                yield return Step();
            }
            AssertTouching(recorder, surface.collider, 1, body.stepsTaken);
        }

        // ---- E4: leaving a surface ----

        static IEnumerable<TestCaseData> LeavingCases()
        {
            foreach (float angle in new[] { 0f, -90f, 180f })
                yield return Case($"Exit_Leaving_{SurfaceName(angle)}", angle);
        }

        [UnityTest, TestCaseSource(nameof(LeavingCases))]
        public IEnumerator ExitWhenLeaving(float angle)
        {
            yield return SpawnBody();
            Surface surface = AddSurface(angle, margin);
            ContactRecorder recorder = Record(body);
            AssertGroundedPrecondition(IsGround(angle));

            yield return Step();
            yield return Step();
            Launch(surface.normal * Speed);
            yield return Step();
            int exitStep = body.stepsTaken;
            for (int i = 0; i < 3; i++)
            {
                yield return Step();
            }

            Assert.AreEqual(1, recorder.Count(Kind.Enter, surface.collider), "Enter events");
            Assert.AreEqual(1, recorder.Count(Kind.Exit, surface.collider), "Exit events");
            Assert.AreEqual(exitStep, recorder.Find(Kind.Exit, surface.collider)[0].step, "The Exit should come on the step the body left");
            Assert.AreEqual(1, recorder.Count(Kind.Stay, surface.collider), "Stay events (only on the step between Enter and Exit)");
        }

        // ---- E5: out of range ----

        [UnityTest]
        public IEnumerator NoEventsBeyondMargin()
        {
            yield return SpawnBody();
            Surface surface = AddSurface(0f, margin + 0.05f);
            ContactRecorder recorder = Record(body);
            AssertGroundedPrecondition(false);

            for (int i = 0; i < Steps; i++)
            {
                yield return Step();
            }
            Assert.IsEmpty(recorder.records, "No contact events for a surface beyond the margin");
            Assert.That(Gap(surface), Is.EqualTo(margin + 0.05f).Within(PositionTolerance), "Precondition: the body should not have moved");
        }

        // ---- E6: corners ----

        static IEnumerable<TestCaseData> CornerCases()
        {
            yield return Case("Corner_FloorAndWall", 0f, -90f);
            yield return Case("Corner_CeilingAndWall", 180f, -90f);
        }

        [UnityTest, TestCaseSource(nameof(CornerCases))]
        public IEnumerator EventsForEachColliderInCorner(float firstAngle, float secondAngle)
        {
            yield return SpawnBody();
            Surface first = AddSurface(firstAngle, margin);
            Surface second = AddSurface(secondAngle, margin);
            ContactRecorder recorder = Record(body);

            for (int i = 0; i < Steps; i++)
            {
                yield return Step();
            }
            AssertTouching(recorder, first.collider, 1, body.stepsTaken);
            AssertTouching(recorder, second.collider, 1, body.stepsTaken);
        }

        // ---- E7: triggers and excluded layers ----

        [UnityTest]
        public IEnumerator NoEventsForTriggers()
        {
            yield return SpawnBody();
            Surface trigger = AddSurface(-90f, margin + ApproachGap);
            trigger.collider.isTrigger = true;
            IgnoreForOverlap(trigger);
            yield return PassThrough(trigger);
        }

        [UnityTest]
        public IEnumerator NoEventsForExcludedLayers()
        {
            const int excludedLayer = 8;
            yield return SpawnBody();
            body.collisionMask = ~(1 << excludedLayer);
            Surface excluded = AddSurface(-90f, margin + ApproachGap);
            excluded.collider.gameObject.layer = excludedLayer;
            IgnoreForOverlap(excluded);
            yield return PassThrough(excluded);
        }

        /// <summary>Moves the body into and through <paramref name="surface"/>, which must neither stop it nor raise events.</summary>
        IEnumerator PassThrough(Surface surface)
        {
            ContactRecorder recorder = Record(body);
            Physics2D.SyncTransforms();
            Vector2 velocity = -surface.normal * Speed;
            Launch(velocity);
            for (int i = 0; i < Steps; i++)
            {
                Vector2 start = body.position;
                yield return Step();
                AssertDisplacement(start, velocity * FixedDeltaTime, PositionTolerance, $"Step {body.stepsTaken}: nothing should block the body");
            }
            Assert.IsEmpty(recorder.records, $"No contact events for {surface.name}");
        }

        // ---- E8: another KinematicMotion2D ----

        [UnityTest]
        public IEnumerator BothBodiesReportTheirContact()
        {
            yield return SpawnBody();
            SteppedKinematicMotion2D other = SpawnBodyAt("Other", Vector2.right * (1f + margin + ApproachGap));
            yield return Settle();
            ContactRecorder recorder = Record(body);
            ContactRecorder otherRecorder = Record(other);
            Assert.IsFalse(other.pushable, "Precondition: the other body should not be pushable");

            Launch(Vector2.right * Speed);
            int enterStep = -1;
            Vector2 relativeAtContact = Vector2.zero;
            for (int i = 0; i < Steps; i++)
            {
                yield return Step();
                if (enterStep < 0 && Gap(body, ColliderOf(other)) <= margin + ContactBand)
                {
                    enterStep = body.stepsTaken;
                    relativeAtContact = body.velocity - other.velocity;
                }
            }
            Assert.Greater(enterStep, 0, "Precondition: the body should have reached the other body");

            AssertTouching(recorder, ColliderOf(other), enterStep, body.stepsTaken);
            KinematicMotion2D.Contact contact = recorder.Find(Kind.Enter, ColliderOf(other))[0].contact;
            Assert.AreSame(other.rigidbody, contact.rigidbody, "contact.rigidbody should be the other body's");
            Assert.That(Vector2.Distance(contact.normal, Vector2.left), Is.LessThan(NormalTolerance), $"contact.normal, got {Format(contact.normal)}");
            AssertVelocity(relativeAtContact, contact.relativeVelocity, "contact.relativeVelocity");

            // The other body sees the contact too, in the same step or, if it updated first, the next.
            List<ContactRecorder.Record> otherEnters = otherRecorder.Find(Kind.Enter, bodyCollider);
            Assert.AreEqual(1, otherEnters.Count, "The other body should get one Enter about the body");
            Assert.That(otherEnters[0].step, Is.InRange(enterStep, enterStep + 1), "The other body's Enter step");
            Assert.AreSame(body.rigidbody, otherEnters[0].contact.rigidbody, "The other body's contact.rigidbody");
            Assert.That(Vector2.Distance(otherEnters[0].contact.normal, Vector2.right), Is.LessThan(NormalTolerance),
                $"The other body's contact.normal, got {Format(otherEnters[0].contact.normal)}");
        }

        // ---- E9: a dynamic body ----

        [UnityTest]
        public IEnumerator ContactWithDynamicBody()
        {
            yield return SpawnBody();
            Rigidbody2D box = AddDynamicBox("Box", Vector2.right * (1f + margin), gravityScale: 0f);
            yield return Settle();
            ContactRecorder recorder = Record(body);
            BoxCollider2D boxCollider = box.GetComponent<BoxCollider2D>();

            for (int i = 0; i < Steps; i++)
            {
                yield return Step();
            }
            AssertTouching(recorder, boxCollider, 1, body.stepsTaken);
            KinematicMotion2D.Contact contact = recorder.Find(Kind.Enter, boxCollider)[0].contact;
            Assert.AreSame(box, contact.rigidbody, "contact.rigidbody should be the dynamic body");
            AssertVelocity(body.velocity - box.linearVelocity, contact.relativeVelocity, "contact.relativeVelocity");
        }

        // ---- E10: riding a moving platform ----

        [UnityTest]
        public IEnumerator StayWhileRidingThenExitWhenSteppingOff()
        {
            yield return SpawnBody();
            Surface top = AddPlatform(0f, margin, PlatformOrder.PlatformFirst, width: 1.6f);
            yield return Settle();
            ContactRecorder recorder = Record(body);
            AssertGroundedPrecondition(true);

            top.owner.velocity = Vector2.right * 2f;
            Launch(Vector2.right * 10f);
            int exitStep = -1;
            for (int i = 0; i < 20 && exitStep < 0; i++)
            {
                yield return Step();
                if (Gap(top) > margin + ContactBand)
                {
                    exitStep = body.stepsTaken;
                }
            }
            Assert.Greater(exitStep, 0, "Precondition: the body should have walked off the platform");
            for (int i = 0; i < 3; i++)
            {
                yield return Step();
            }

            Collider2D platform = top.collider;
            Assert.AreEqual(1, recorder.Count(Kind.Enter, platform), "Enter events");
            Assert.AreEqual(1, recorder.Find(Kind.Enter, platform)[0].step, "The Enter should come on the first step");
            Assert.AreSame(top.owner.rigidbody, recorder.Find(Kind.Enter, platform)[0].contact.rigidbody, "contact.rigidbody");
            for (int step = 2; step < exitStep; step++)
            {
                Assert.AreEqual(1, recorder.Count(Kind.Stay, platform, step), $"Step {step}: one Stay while riding");
            }
            Assert.AreEqual(1, recorder.Count(Kind.Exit, platform), "Exit events");
            Assert.AreEqual(exitStep, recorder.Find(Kind.Exit, platform)[0].step, "The Exit should come on the step the body stepped off");
            Assert.AreEqual(0, recorder.Find(Kind.Stay, platform).FindAll(r => r.step >= exitStep).Count, "No Stay after stepping off");
        }

        // ---- Shared ----

        /// <summary>
        /// Checks the events about <paramref name="collider"/>: one Enter at <paramref name="enterStep"/>,
        /// then exactly one Stay on every step up to <paramref name="lastStep"/>, and no Exit.
        /// </summary>
        static void AssertTouching(ContactRecorder recorder, Collider2D collider, int enterStep, int lastStep)
        {
            List<ContactRecorder.Record> enters = recorder.Find(Kind.Enter, collider);
            Assert.AreEqual(1, enters.Count, $"Enter events about {collider.name} (steps: {string.Join(", ", enters.ConvertAll(r => r.step))})");
            Assert.AreEqual(enterStep, enters[0].step, $"The Enter about {collider.name} should come on the step contact was made");
            Assert.AreEqual(0, recorder.Count(Kind.Exit, collider), $"Exit events about {collider.name}");
            for (int step = 1; step <= lastStep; step++)
            {
                int expected = step > enterStep ? 1 : 0;
                Assert.AreEqual(expected, recorder.Count(Kind.Stay, collider, step), $"Stay events about {collider.name} in step {step}");
            }
        }
    }
}
