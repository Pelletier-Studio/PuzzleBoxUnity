using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PuzzleBox.Tests
{
    /// <summary>
    /// Attaching one KinematicMotion2D to another, like a held item: the attachment API itself, and the
    /// rules that only make sense for an attached body. (How attachments affect collisions, ground
    /// motion, pushing and contact events is covered in those suites.)
    ///
    /// The rules these tests encode:
    /// - AttachTo links a body to a parent; attachedTo is the root of the chain, attachments the direct
    ///   children. Attaching to itself, or to its own descendant, is refused with a warning. AttachTo(null)
    ///   and Detach unlink it, and destroying a parent unlinks its children.
    /// - An attached body does not simulate: it only moves with its root, keeping its offset.
    /// - Its velocity is its root's. When detached it keeps that velocity and simulates again.
    /// - combinedMass is the mass of a body and everything attached below it.
    /// - A body never stands on its own attachments, and is never pushed apart from them.
    /// </summary>
    public class AttachmentTests : CollideAndSlideFixture
    {
        const float Speed = 4f;
        const int Steps = 5;

        // ---- The attachment graph ----

        [UnityTest]
        public IEnumerator AttachDetachAndReparent()
        {
            yield return SpawnBody();
            SteppedKinematicMotion2D middle = SpawnBodyAt("Middle", Vector2.right * 2f);
            SteppedKinematicMotion2D leaf = SpawnBodyAt("Leaf", Vector2.right * 4f);
            SteppedKinematicMotion2D other = SpawnBodyAt("Other", Vector2.up * 2f);
            yield return Settle();

            middle.AttachTo(body);
            leaf.AttachTo(middle);
            Assert.AreSame(body, middle.attachedTo, "middle.attachedTo");
            Assert.AreSame(body, leaf.attachedTo, "attachedTo is the root of the chain");
            Assert.IsNull(body.attachedTo, "The root is not attached");
            CollectionAssert.AreEqual(new[] { middle }, body.attachments, "body.attachments");
            CollectionAssert.AreEqual(new[] { leaf }, middle.attachments, "middle.attachments");

            middle.AttachTo(other);
            Assert.AreSame(other, middle.attachedTo, "Re-attached to another parent");
            Assert.AreSame(other, leaf.attachedTo, "Its own attachments follow it");
            CollectionAssert.IsEmpty(body.attachments, "The old parent lets go");
            CollectionAssert.AreEqual(new[] { middle }, other.attachments, "The new parent holds it");

            leaf.Detach();
            Assert.IsNull(leaf.attachedTo, "Detach");
            CollectionAssert.IsEmpty(middle.attachments, "Detach removes it from its parent");

            middle.AttachTo(null);
            Assert.IsNull(middle.attachedTo, "AttachTo(null) detaches");
            CollectionAssert.IsEmpty(other.attachments, "AttachTo(null) removes it from its parent");
        }

        [UnityTest]
        public IEnumerator RefusesAttachingToItselfOrInACycle()
        {
            yield return SpawnBody();
            SteppedKinematicMotion2D child = SpawnBodyAt("Child", Vector2.right * 2f);
            yield return Settle();

            LogAssert.Expect(LogType.Warning, new Regex("itself"));
            body.AttachTo(body);
            Assert.IsNull(body.attachedTo, "A body cannot be attached to itself");

            child.AttachTo(body);
            LogAssert.Expect(LogType.Warning, new Regex("already attached"));
            body.AttachTo(child);
            Assert.IsNull(body.attachedTo, "A body cannot be attached to its own attachment");
            Assert.AreSame(body, child.attachedTo, "The existing attachment is untouched");
        }

        [UnityTest]
        public IEnumerator DestroyingParentDetachesChildren()
        {
            yield return SpawnBody();
            SteppedKinematicMotion2D parent = SpawnBodyAt("Parent", Vector2.right * 2f);
            yield return Settle();
            body.AttachTo(parent);

            Object.Destroy(parent.gameObject);
            yield return null;
            Assert.IsNull(body.attachedTo, "Destroying the parent should detach its children");
        }

        [UnityTest]
        public IEnumerator CombinedMassIncludesWholeSubtree()
        {
            yield return SpawnBody();
            SteppedKinematicMotion2D middle = AttachChild(Vector2.right * 1.05f, "Middle");
            SteppedKinematicMotion2D leaf = AttachChild(Vector2.right * 1.05f, "Leaf", middle);
            yield return Settle();
            body.mass = 1f;
            middle.mass = 2f;
            leaf.mass = 3f;

            Assert.AreEqual(6f, body.combinedMass, 1e-5f, "The root weighs everything");
            Assert.AreEqual(5f, middle.combinedMass, 1e-5f, "A middle body weighs itself and what it holds");
            Assert.AreEqual(3f, leaf.combinedMass, 1e-5f, "A leaf weighs itself");
        }

        // ---- Attached bodies do not simulate ----

        [UnityTest]
        public IEnumerator AttachedBodyOnlyMovesWithItsRoot()
        {
            yield return SpawnBody();
            SteppedKinematicMotion2D held = AttachChild(Vector2.right * 1.05f);
            yield return Settle();
            held.velocity = new Vector2(3f, 2f);
            held.gravityMultiplier = 1f;

            Vector2 start = held.position;
            for (int i = 0; i < Steps; i++)
            {
                yield return Step();
                AssertDisplacement(held, start, Vector2.zero, PositionTolerance, $"Step {body.stepsTaken}: with its root still, the held body should not move");
            }
        }

        // ---- Velocity ----

        /// <summary>A held body's velocity is its root's, down a chain, whether the root moves freely or is stopped.</summary>
        [UnityTest]
        public IEnumerator AttachedVelocityIsRootVelocity()
        {
            yield return SpawnBody();
            SteppedKinematicMotion2D middle = AttachChild(Vector2.left * 1.05f, "Middle");
            SteppedKinematicMotion2D leaf = AttachChild(Vector2.left * 1.05f, "Leaf", middle);
            AddSurface(-90f, margin + 0.15f);
            yield return Settle();

            Launch(new Vector2(Speed, 1f));
            for (int i = 0; i < 2 * Steps; i++)
            {
                yield return Step();
                string context = $"Step {body.stepsTaken}";
                AssertVelocity(body.velocity, middle.velocity, context + ": the middle body's velocity");
                AssertVelocity(body.velocity, leaf.velocity, context + ": the leaf's velocity");
            }
        }

        /// <summary>A released body keeps the velocity it had while held, and simulates again.</summary>
        [UnityTest]
        public IEnumerator DetachedBodyKeepsVelocityAndSimulates()
        {
            yield return SpawnBody();
            SteppedKinematicMotion2D held = AttachChild(Vector2.up * 1.05f);
            yield return Settle();

            Vector2 velocity = new Vector2(Speed, 0f);
            Launch(velocity);
            for (int i = 0; i < Steps; i++)
            {
                yield return Step();
            }
            held.Detach();
            AssertVelocity(velocity, held.velocity, "On release, the held body should keep its holder's velocity");

            for (int i = 0; i < Steps; i++)
            {
                Vector2 start = held.position;
                yield return Step();
                AssertDisplacement(held, start, velocity * FixedDeltaTime, PositionTolerance, $"Step {body.stepsTaken}: released, it should move on its own");
            }
        }

        // ---- A body and its attachments never interact ----

        /// <summary>A body with something held directly below it, and nothing else, is not standing on anything.</summary>
        [UnityTest]
        public IEnumerator NotGroundedOnOwnAttachment()
        {
            yield return SpawnBody(gravityMultiplier: 1f);
            AttachChild(Vector2.down * (1f + margin));
            yield return Settle();

            body.UpdateGroundedState();
            Assert.IsFalse(body.isGrounded, "A body must not count as grounded on its own attachment");

            Vector2 start = body.position;
            for (int i = 0; i < Steps; i++)
            {
                yield return Step();
            }
            Assert.Less(body.position.y, start.y, "With nothing beneath it but what it holds, the body should fall");
        }

        static IEnumerable HeldCloseCases()
        {
            yield return new TestCaseData(1f + 0.005f).SetName("HeldInsideMargin").Returns(null);
            yield return new TestCaseData(0.8f).SetName("HeldOverlapping").Returns(null);
        }

        /// <summary>Something held closer than the margin, or even overlapping, is not pushed apart from its holder.</summary>
        [UnityTest, TestCaseSource(nameof(HeldCloseCases))]
        public IEnumerator HeldBodyIsNotSeparatedFromHolder(float distance)
        {
            yield return SpawnBody();
            AttachChild(Vector2.right * distance);
            yield return Settle();

            Vector2 start = body.position;
            for (int i = 0; i < Steps; i++)
            {
                yield return Step();
                AssertDisplacement(start, Vector2.zero, PositionTolerance, $"Step {body.stepsTaken}: the body should not move away from what it holds");
            }
        }
    }
}
