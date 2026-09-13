using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PuzzleBox;

/// <summary>
/// Tests for KinematicMotion2D's attachment API (AttachTo / Detach).
///
/// THE SPECIFICATION these tests encode. While attached, a child's colliders are treated as part of
/// the parent: the parent and its attached subtree behave as one rigid body for collision purposes.
///
///   1. ONE MERGED FOOTPRINT, FOR BLOCKING. Any collision that would stop a child stops the PARENT.
///      This is a side effect of collision detection itself - the parent's movement queries cast the
///      whole subtree's colliders, not just its own.
///   2. MUTUALLY EXCLUDED WITHIN THE SUBTREE. A parent and its attached children never collide with
///      each other. Without this, a held item overlapping its holder would block the holder.
///   3. RIGID FOLLOW. The child tracks the parent exactly and its own simulation is suspended: no
///      gravity, no ground check, no overlap resolution, no self-driven movement, and `velocity` is
///      left untouched.
///   4. GROUND DETECTION IS THE ONE EXCEPTION - BLOCKING ONLY. CheckForGround keeps using the
///      parent's own colliders. A held item resting on the floor stops its holder's descent but does
///      NOT make the holder grounded, and cannot redefine groundNormal or groundMotion.
///   5. SYMMETRIC OUTWARD EFFECTS. The merged footprint acts outward as well as inward: the subtree
///      pushes pushable bodies the parent never touches, and a body standing on an attached child is
///      carried.
///   6. EVENTS GO TO BOTH BODIES, each for its own colliders, distinguished by Contact.self.
///
/// Note how (3) and (6) interact: because the child's FixedUpdate is suspended, its events cannot
/// come from its own pass. The parent's contact and crush passes must fan out to the body owning
/// each collider.
///
/// STATUS: this is a TDD red phase. The implementation does not yet satisfy most of the above, so
/// many tests here are EXPECTED TO FAIL. Each one says so in its own comment, naming the defect it
/// covers. Tests without such a note are regression guards that should pass today.
///
/// Do not make a failing test here pass by weakening its assertion - the failures are the point.
///
/// THE RED LIST, as measured on a full PlayMode run against the unmodified component. This is the
/// specification for the follow-up fix: the job is done when this list is empty and no assertion in
/// this file has been weakened to get there.
///
///     AttachedChild_HittingWall_StopsTheParent
///     AttachedChild_HittingCeiling_StopsRisingParent
///     AttachedChild_HittingFloor_StopsFallingParent
///     AttachedChild_HittingFloor_DoesNotGroundTheParent
///     AttachedSubtree_DoesNotCollideWithItself
///     AttachedChain_FootprintIncludesGrandchild
///     AttachedSubtree_ManyColliders_DoesNotSilentlyDropHits
///     AttachedBody_IgnoresItsOwnGravity
///     AttachedBody_DoesNotDriveItself_AndKeepsItsVelocity
///     AttachedBody_DoesNotDoubleMove_WhenAlsoGroundedOnItsParent
///     AttachedChild_PushesPushableBody
///     AttachedChild_BlockedByNonPushableBody_StopsTheParent
///     RiderOnAttachedChild_IsCarried
///     AttachedSubtree_PushDistance_UsesCombinedMass
///     AttachedChild_Collision_ReportsContactOnBothBodies
///     AttachedChild_UnresolvableOverlap_CrushesBothBodies
///     AttachmentChain_PropagatesToGrandchild
///     SelfAttachment_DoesNotDoubleMove
///     AttachingNotYetStartedBody_DoesNotThrow_AndParentStillMoves
///     TeleportingParentViaPosition_CarriesAttachedSubtree
///     SeparatingParent_CarriesAttachedSubtree
///     DestroyingParent_DetachesItsChildren
///
/// Everything else in this file passes today and must keep passing.
///
/// Two of those passing tests are worth knowing about, because they pass for reasons that will
/// change once the fix lands, and that is exactly why they are here:
///
///   - AttachedChild_SelfContacts_AreNotReported passes only because the parent and child are
///     currently SEPARATED apart before contacts are sampled - which is itself the violation that
///     AttachedSubtree_DoesNotCollideWithItself catches. Once they are allowed to stay overlapped,
///     this test becomes a real check that self-contacts are excluded from reporting.
///   - AttachmentCycle_DoesNotHangOrThrow passes because the current carry writes positions directly
///     instead of recursing. The fix is what makes a cycle dangerous.
/// </summary>
public class TestKinematicAttachment : KinematicTestFixture
{
    // The prefab's box is 1x1 and is never scaled in this file, so every body has a half-extent of
    // 0.5. Deriving faces from rigidbody positions rather than Collider2D.bounds keeps these checks
    // independent of when Unity last synced transforms.
    const float HalfExtent = 0.5f;

    private static float LeftFaceOf(KinematicMotion2D motion) => motion.position.x - HalfExtent;
    private static float RightFaceOf(KinematicMotion2D motion) => motion.position.x + HalfExtent;
    private static float TopFaceOf(KinematicMotion2D motion) => motion.position.y + HalfExtent;
    private static float BottomFaceOf(KinematicMotion2D motion) => motion.position.y - HalfExtent;

    // Records KinematicMotion2D's own contact events. Unlike the recorder in TestKinematicCollisions
    // this one also captures Contact.self, which is what distinguishes the parent's copy of a
    // subtree contact from the child's under specification point (6).
    private class AttachmentContactRecorder : MonoBehaviour
    {
        public int enterCount;
        public readonly List<GameObject> selves = new List<GameObject>();
        public readonly List<GameObject> others = new List<GameObject>();

        void OnContactEnter(KinematicMotion2D.Contact contact)
        {
            enterCount++;
            selves.Add(contact.self);
            others.Add(contact.collider != null ? contact.collider.gameObject : null);
        }

        public bool SawContactWith(GameObject other)
        {
            foreach (GameObject go in others)
            {
                if (go == other)
                {
                    return true;
                }
            }
            return false;
        }
    }

    private class CrushRecorder : MonoBehaviour
    {
        public int count;
        public GameObject lastCrusher;

        void OnCrushedBy(KinematicMotion2D.Contact contact)
        {
            count++;
            lastCrusher = contact.collider != null ? contact.collider.gameObject : null;
        }
    }

    // Re-applies a velocity every FixedUpdate, the way gameplay code driven by input does.
    //
    // Required for any test where the body might PUSH something. FixedUpdate ends with
    // `velocity = actualMotion / deltaSeconds`, and a push deliberately keeps only a mass-weighted
    // fraction of the intended travel - so a body whose velocity is set once decays geometrically to
    // a standstill within a few frames of touching anything pushable. See the longer note on the
    // helper of the same name in TestKinematicCollisions.
    private class ConstantVelocityDriver : MonoBehaviour
    {
        public Vector2 velocity;
        private KinematicMotion2D motion;

        void Awake() { motion = GetComponent<KinematicMotion2D>(); }
        void FixedUpdate() { motion.velocity = velocity; }
    }

    private static void DriveAt(KinematicMotion2D motion, Vector2 velocity)
    {
        motion.velocity = velocity;
        motion.gameObject.AddComponent<ConstantVelocityDriver>().velocity = velocity;
    }

    // Drives a plain kinematic Rigidbody2D with MovePosition - nothing stops it, which is what makes
    // it usable as an unavoidable crusher. Mirrors the helper of the same shape in
    // TestKinematicCollisions.
    private class ConstantVelocityKinematicMover : MonoBehaviour
    {
        public Vector2 velocity;
        private Rigidbody2D rb;

        void Awake() { rb = GetComponent<Rigidbody2D>(); }
        void FixedUpdate() { rb.MovePosition(rb.position + velocity * Time.fixedDeltaTime); }
    }

    private GameObject CreateGenericKinematicMover(Vector2 position, Vector2 velocity)
    {
        GameObject go = new GameObject("GenericKinematicMover");
        go.transform.position = position;
        BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
        collider.size = Vector2.one;
        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        go.AddComponent<ConstantVelocityKinematicMover>().velocity = velocity;
        spawned.Add(go);
        return go;
    }

    // Iterator methods cannot have out/ref parameters, so results are written onto a passed-in
    // object - the same pattern the slope and pinned-rider scenarios in TestKinematicCollisions use.
    private class Pair
    {
        public KinematicMotion2D parent;
        public KinematicMotion2D child;
        public Vector2 offset; // child.position - parent.position, measured once attached
    }

    /// <summary>
    /// Spawns a parent with a child attached at the given offset, and settles a frame so Start() has
    /// run on both before anything moves.
    ///
    /// The child's gravity is switched OFF here on purpose. An attached body keeping its own gravity
    /// is a defect in its own right (covered by AttachedBody_IgnoresItsOwnGravity below), and if the
    /// child were left falling it would drift out of position and break the staging of every
    /// footprint test in this file - which would then fail for the wrong reason. One defect per test.
    /// </summary>
    private IEnumerator MakeAttachedPair(float baseX, Vector2 childOffset, Pair pair, bool parentUsesGravity = false)
    {
        KinematicMotion2D parent = SpawnBody(new Vector2(baseX, 0));
        parent.useGravity = parentUsesGravity;
        parent.velocity = Vector2.zero;

        KinematicMotion2D child = SpawnBody(new Vector2(baseX, 0) + childOffset);
        child.useGravity = false;
        child.velocity = Vector2.zero;

        // Let Start() run on both, so rb and the collider cache are initialised.
        yield return null;

        child.AttachTo(parent);
        yield return new WaitForFixedUpdate();

        pair.parent = parent;
        pair.child = child;
        pair.offset = child.position - parent.position;
    }

    // Guards the staging of the horizontal footprint tests: if the parent's own box overlaps the
    // obstacle's vertical band, the obstacle stops the parent directly and the test proves nothing
    // about the merged footprint.
    private static void AssertOnlyChildCanReach(KinematicMotion2D parent, KinematicMotion2D child, float bandBottom, float bandTop)
    {
        Assert.IsTrue(TopFaceOf(parent) <= bandBottom || BottomFaceOf(parent) >= bandTop,
            $"Staging error: the parent's own box (y {BottomFaceOf(parent):F2}..{TopFaceOf(parent):F2}) reaches the obstacle band " +
            $"(y {bandBottom:F2}..{bandTop:F2}), so the obstacle would stop it directly and the test would pass for the wrong reason.");
        Assert.IsTrue(TopFaceOf(child) > bandBottom && BottomFaceOf(child) < bandTop,
            $"Staging error: the child's box (y {BottomFaceOf(child):F2}..{TopFaceOf(child):F2}) does not reach the obstacle band " +
            $"(y {bandBottom:F2}..{bandTop:F2}), so nothing in the subtree would ever touch it.");
    }

    // ------------------------------------------------------------------
    // 2a. The merged footprint - the core of the specification.
    //
    // Every test in this section is EXPECTED TO FAIL until RigidbodyCast/RigidbodyOverlap gather the
    // attached subtree's colliders (specification point 1) and Cast/overlap resolution exclude the
    // subtree from itself (point 2).
    //
    // HOW THESE ARE ASSERTED, and why the obvious way does not work.
    //
    // The tempting assertion is "the child never enters the wall". It is useless: it passes whether
    // or not the footprint is merged. An attached child still runs its own overlap resolution, which
    // independently shoves it back out of any wall it is dragged into - so the child stays clear of
    // the obstacle either way, while the parent sails straight past and the attach offset silently
    // stretches. An earlier draft of these tests asserted exactly that and passed green against a
    // component that does not implement the specification at all.
    //
    // What actually distinguishes a stopped parent from a running one is where the PARENT is allowed
    // to get to: at most the point at which the child, held rigidly at its attach offset, touches the
    // obstacle. So each test below asserts the parent's position against that limit, and then checks
    // that the subtree stayed rigid.
    // ------------------------------------------------------------------

    // The subtree must move as one body: if the parent ran on while the child was held back by its
    // own overlap resolution, the offset is what gives it away.
    private static void AssertAttachOffsetHeld(Pair pair, float tolerance = 0.1f)
    {
        Vector2 offset = pair.child.position - pair.parent.position;
        Assert.AreEqual(pair.offset.x, offset.x, tolerance,
            $"The attached child should have held its x offset from the parent (was {pair.offset.x:F3}, now {offset.x:F3}).");
        Assert.AreEqual(pair.offset.y, offset.y, tolerance,
            $"The attached child should have held its y offset from the parent (was {pair.offset.y:F3}, now {offset.y:F3}).");
    }

    // EXPECTED RED. The parent drags the child straight through the wall today.
    [UnityTest]
    public IEnumerator AttachedChild_HittingWall_StopsTheParent()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(6000, new Vector2(0, 1.5f), pair);

        // A wall standing in the child's band only: its bottom face is above the parent's top.
        float bandBottom = TopFaceOf(pair.parent) + 0.2f;
        float bandTop = bandBottom + 3f;
        AssertOnlyChildCanReach(pair.parent, pair.child, bandBottom, bandTop);

        float wallX = pair.parent.position.x + 3f;
        CreateStaticFloor(new Vector2(wallX, (bandBottom + bandTop) * 0.5f), new Vector2(1f, bandTop - bandBottom));
        float wallLeftFace = wallX - 0.5f;

        // Where the parent may travel to before its child, held at the attach offset, meets the wall.
        float parentLimit = wallLeftFace - pair.offset.x - HalfExtent;

        pair.parent.velocity = new Vector2(2f, 0);

        int steps = Mathf.CeilToInt(3f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            Assert.LessOrEqual(pair.parent.position.x, parentLimit + 0.05f,
                $"On step {i} the parent drove past the point where its attached child meets the wall " +
                $"(parent x {pair.parent.position.x:F3}, limit {parentLimit:F3}). " +
                "A collision that stops a child must stop its parent.");
        }

        AssertAttachOffsetHeld(pair);
    }

    // EXPECTED RED. Same defect, vertically, with the child leading a rising parent.
    [UnityTest]
    public IEnumerator AttachedChild_HittingCeiling_StopsRisingParent()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(6050, new Vector2(0, 1.5f), pair);

        float ceilingBottomY = TopFaceOf(pair.child) + 2f;
        CreateStaticFloor(new Vector2(pair.parent.position.x, ceilingBottomY + 2f), new Vector2(6f, 4f));

        float parentLimit = ceilingBottomY - pair.offset.y - HalfExtent;

        pair.parent.velocity = new Vector2(0, 2f);

        int steps = Mathf.CeilToInt(3f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            Assert.LessOrEqual(pair.parent.position.y, parentLimit + 0.05f,
                $"On step {i} the parent kept rising past the point where its attached child meets the ceiling " +
                $"(parent y {pair.parent.position.y:F3}, limit {parentLimit:F3}).");
        }

        AssertAttachOffsetHeld(pair);
    }

    // EXPECTED RED. A child dangling below a falling parent must arrest the parent's descent.
    [UnityTest]
    public IEnumerator AttachedChild_HittingFloor_StopsFallingParent()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(6100, new Vector2(0, -1.5f), pair, parentUsesGravity: true);

        float floorTopY = BottomFaceOf(pair.child) - 2f;
        CreateStaticFloor(new Vector2(pair.parent.position.x, floorTopY - 2f), new Vector2(6f, 4f));

        float parentLimit = floorTopY - pair.offset.y + HalfExtent;

        int steps = Mathf.CeilToInt(2f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            Assert.GreaterOrEqual(pair.parent.position.y, parentLimit - 0.05f,
                $"On step {i} the parent kept falling past the point where its attached child lands on the floor " +
                $"(parent y {pair.parent.position.y:F3}, limit {parentLimit:F3}).");
        }

        AssertAttachOffsetHeld(pair);
    }

    // EXPECTED RED (the blocking half). This is specification point (4), and the sharpest pair of
    // assertions in the file: the parent is STOPPED by what its child is resting on, but is NOT
    // GROUNDED by it. The grounded half passes today only because the blocking half does not happen
    // at all, so both must be checked together for the test to mean anything.
    [UnityTest]
    public IEnumerator AttachedChild_HittingFloor_DoesNotGroundTheParent()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(6150, new Vector2(0, -1.5f), pair, parentUsesGravity: true);

        float floorTopY = BottomFaceOf(pair.child) - 1f;
        CreateStaticFloor(new Vector2(pair.parent.position.x, floorTopY - 2f), new Vector2(6f, 4f));

        float parentLimit = floorTopY - pair.offset.y + HalfExtent;

        yield return new WaitForSeconds(1.5f);

        // Blocking: the child is resting on the floor, so the parent has stopped.
        Assert.GreaterOrEqual(pair.parent.position.y, parentLimit - 0.05f,
            $"The parent should have been stopped by its child resting on the floor " +
            $"(parent y {pair.parent.position.y:F3}, limit {parentLimit:F3}).");

        // ...but ground detection still only sees the parent's own colliders, and the parent's own
        // box is a clear 2 units above the floor.
        Assert.IsFalse(pair.parent.isGrounded,
            "A held item resting on the floor must not make its holder grounded: CheckForGround uses the parent's own colliders only.");
        Assert.Less(Vector2.Distance(Vector2.up, pair.parent.groundNormal), 0.01f,
            "groundNormal should still be straight up - the child's contact must not redefine the parent's ground.");
        Assert.Greater(pair.parent.timeInAir, 0f,
            "The parent is not grounded, so timeInAir should keep accumulating even though its descent is blocked.");
    }

    // EXPECTED RED. The mutual-exclusion half of the specification (point 2), on the overlap path:
    // overlap resolution currently sees the attached child as a foreign body and separates from it.
    //
    // This one matters most as a guard on the fix: merging the footprint (point 1) without excluding
    // the subtree from itself would freeze every parent that overlaps what it carries.
    [UnityTest]
    public IEnumerator AttachedSubtree_DoesNotCollideWithItself()
    {
        // Deliberately overlapping - a held item generally does overlap its holder.
        Pair pair = new Pair();
        yield return MakeAttachedPair(6200, new Vector2(0.3f, 0), pair);

        float startX = pair.parent.position.x;
        pair.parent.velocity = new Vector2(2f, 0);

        yield return new WaitForSeconds(1f);

        Assert.AreEqual(startX + 2f, pair.parent.position.x, 0.2f,
            $"The parent should travel freely through open space while carrying an overlapping child, but it moved " +
            $"{pair.parent.position.x - startX:F3} of the expected 2.0 - it is colliding with its own subtree.");
        Assert.AreEqual(pair.offset.x, (pair.child.position - pair.parent.position).x, 0.05f,
            "The attach offset should be unchanged: overlap resolution must not push a parent and its attached child apart.");
    }

    // EXPECTED RED. Depends on the footprint covering the whole subtree, not just direct children.
    [UnityTest]
    public IEnumerator AttachedChain_FootprintIncludesGrandchild()
    {
        KinematicMotion2D a = SpawnBody(new Vector2(6250, 0));
        a.useGravity = false;
        a.velocity = Vector2.zero;

        KinematicMotion2D b = SpawnBody(new Vector2(6250, 1.5f));
        b.useGravity = false;
        b.velocity = Vector2.zero;

        KinematicMotion2D c = SpawnBody(new Vector2(6250, 3f));
        c.useGravity = false;
        c.velocity = Vector2.zero;

        yield return null;

        b.AttachTo(a);
        c.AttachTo(b);
        yield return new WaitForFixedUpdate();

        // A band only the grandchild reaches.
        float bandBottom = TopFaceOf(b) + 0.2f;
        float bandTop = bandBottom + 3f;
        AssertOnlyChildCanReach(a, c, bandBottom, bandTop);

        float wallX = a.position.x + 3f;
        CreateStaticFloor(new Vector2(wallX, (bandBottom + bandTop) * 0.5f), new Vector2(1f, bandTop - bandBottom));
        float wallLeftFace = wallX - 0.5f;

        // As in the other footprint tests, the root is the witness: the grandchild's own overlap
        // resolution would keep it out of the wall regardless.
        float grandchildOffsetX = c.position.x - a.position.x;
        float rootLimit = wallLeftFace - grandchildOffsetX - HalfExtent;

        a.velocity = new Vector2(2f, 0);

        int steps = Mathf.CeilToInt(3f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            Assert.LessOrEqual(a.position.x, rootLimit + 0.05f,
                $"On step {i} the root drove past the point where its GRANDCHILD meets the wall " +
                $"(root x {a.position.x:F3}, limit {rootLimit:F3}). " +
                "The merged footprint must cover the whole attached subtree, not just direct children.");
        }
    }

    // EXPECTED RED. Guards the fixed-size query buffers: `hits` and `overlaps` hold 8 entries, and
    // RigidbodyCast returns as soon as it fills them - remaining colliders are never cast at all.
    // A merged subtree footprint reaches that ceiling far sooner than a single body does, and the
    // failure mode is silent, because a dropped hit is indistinguishable from "no collision".
    [UnityTest]
    public IEnumerator AttachedSubtree_ManyColliders_DoesNotSilentlyDropHits()
    {
        const int childCount = 10;

        KinematicMotion2D parent = SpawnBody(new Vector2(6300, 0));
        parent.useGravity = false;
        parent.velocity = Vector2.zero;

        // Spaced 1.5 apart so the 1x1 boxes never overlap each other.
        List<KinematicMotion2D> children = new List<KinematicMotion2D>();
        for (int i = 0; i < childCount; i++)
        {
            KinematicMotion2D child = SpawnBody(new Vector2(6300, 1.5f * (i + 1)));
            child.useGravity = false;
            child.velocity = Vector2.zero;
            children.Add(child);
        }

        yield return null;

        foreach (KinematicMotion2D child in children)
        {
            child.AttachTo(parent);
        }
        yield return new WaitForFixedUpdate();

        // The wall is in the band of the LAST attached child, so it can only be found by a query
        // that got all the way through the subtree.
        KinematicMotion2D last = children[childCount - 1];
        float bandBottom = BottomFaceOf(last) + 0.1f;
        float bandTop = TopFaceOf(last) - 0.1f;

        float wallX = parent.position.x + 3f;
        CreateStaticFloor(new Vector2(wallX, (bandBottom + bandTop) * 0.5f), new Vector2(1f, bandTop - bandBottom));
        float wallLeftFace = wallX - 0.5f;

        float lastOffsetX = last.position.x - parent.position.x;
        float parentLimit = wallLeftFace - lastOffsetX - HalfExtent;

        parent.velocity = new Vector2(2f, 0);

        int steps = Mathf.CeilToInt(3f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            Assert.LessOrEqual(parent.position.x, parentLimit + 0.05f,
                $"On step {i} the parent drove past the point where its {childCount}th attached child meets the wall " +
                $"(parent x {parent.position.x:F3}, limit {parentLimit:F3}). " +
                "That child's collider was most likely dropped by a full query buffer.");
        }
    }

    // ------------------------------------------------------------------
    // 2b. Rigid follow and suspended simulation.
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator AttachedBody_TracksParentExactly_HorizontalMotion()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(6350, new Vector2(0, 1.5f), pair);

        pair.parent.velocity = new Vector2(2f, 0);

        int steps = Mathf.CeilToInt(1.5f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            Vector2 offset = pair.child.position - pair.parent.position;
            Assert.AreEqual(pair.offset.x, offset.x, 0.02f, $"On step {i} the child drifted horizontally out of its attach offset.");
            Assert.AreEqual(pair.offset.y, offset.y, 0.02f, $"On step {i} the child drifted vertically out of its attach offset.");
        }
    }

    [UnityTest]
    public IEnumerator AttachedBody_TracksParentExactly_DiagonalMotion()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(6400, new Vector2(0, 1.5f), pair);

        pair.parent.velocity = new Vector2(1.5f, 1f);

        int steps = Mathf.CeilToInt(1.5f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            Vector2 offset = pair.child.position - pair.parent.position;
            Assert.AreEqual(pair.offset.x, offset.x, 0.02f, $"On step {i} the child drifted horizontally out of its attach offset.");
            Assert.AreEqual(pair.offset.y, offset.y, 0.02f, $"On step {i} the child drifted vertically out of its attach offset.");
        }
    }

    // EXPECTED RED. Specification point (3): an attached body's simulation is suspended, so a held
    // item does not sink out of the hand holding it. Today the child's FixedUpdate keeps running and
    // gravity keeps pulling it down.
    //
    // Note this test does NOT use MakeAttachedPair, which switches the child's gravity off - the
    // whole point here is a child that would fall if it were being simulated.
    [UnityTest]
    public IEnumerator AttachedBody_IgnoresItsOwnGravity()
    {
        KinematicMotion2D parent = SpawnBody(new Vector2(6450, 0));
        parent.useGravity = false;
        parent.velocity = Vector2.zero;

        KinematicMotion2D child = SpawnBody(new Vector2(6450, 2f));
        child.useGravity = true; // explicit: this is the condition under test

        yield return null;

        child.AttachTo(parent);
        yield return new WaitForFixedUpdate();

        float offsetBefore = (child.position - parent.position).y;

        // Nothing moves for a second. A suspended child holds station; a simulated one falls ~4.9m.
        yield return new WaitForSeconds(1f);

        float offsetAfter = (child.position - parent.position).y;
        Assert.AreEqual(offsetBefore, offsetAfter, 0.05f,
            $"An attached body must not apply its own gravity: it fell {offsetBefore - offsetAfter:F3} units out of its holder.");
    }

    // EXPECTED RED. The other half of point (3). Two things are checked, and it is the SECOND that
    // fails today: an attached body with a velocity set on it currently drives itself with that
    // velocity and flies out of its parent. Once suspended, the velocity should simply sit there
    // unused - neither acted on nor overwritten by a displacement the body did not drive.
    [UnityTest]
    public IEnumerator AttachedBody_DoesNotDriveItself_AndKeepsItsVelocity()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(6500, new Vector2(0, 1.5f), pair);

        // A sentinel value the child never acts on: while attached it should neither move itself by
        // this velocity nor have it overwritten.
        Vector2 sentinel = new Vector2(7f, 3f);
        pair.child.velocity = sentinel;

        Vector2 offsetBefore = pair.child.position - pair.parent.position;

        yield return new WaitForSeconds(0.5f);

        Assert.AreEqual(sentinel.x, pair.child.velocity.x, 0.01f, "An attached body's velocity.x should be left untouched while its simulation is suspended.");
        Assert.AreEqual(sentinel.y, pair.child.velocity.y, 0.01f, "An attached body's velocity.y should be left untouched while its simulation is suspended.");

        Vector2 offsetAfter = pair.child.position - pair.parent.position;
        Assert.AreEqual(offsetBefore.x, offsetAfter.x, 0.05f, "An attached body must not drive itself with its own velocity.");
        Assert.AreEqual(offsetBefore.y, offsetAfter.y, 0.05f, "An attached body must not drive itself with its own velocity.");
    }

    // EXPECTED RED, and the highest-value test in this file: standing on a platform and grabbing it
    // is exactly what GrabKinematicMotion2D invites. The body is carried twice per frame today -
    // once by the ground-carry (WillMove -> GroundWillMove) and once by the attach loop - so it
    // travels at double the platform's speed.
    [UnityTest]
    public IEnumerator AttachedBody_DoesNotDoubleMove_WhenAlsoGroundedOnItsParent()
    {
        KinematicMotion2D platform = SpawnBody(new Vector2(6550, 0));
        platform.transform.localScale = new Vector3(10, 1, 1);
        platform.useGravity = false;
        platform.velocity = Vector2.zero;

        KinematicMotion2D rider = SpawnBody(new Vector2(6550, 3));

        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(rider.isGrounded, "Precondition: the rider should have landed on the platform.");

        // Now it is BOTH grounded on the platform and attached to it.
        rider.AttachTo(platform);
        yield return new WaitForFixedUpdate();

        float riderXBefore = rider.position.x;
        float platformXBefore = platform.position.x;
        platform.velocity = new Vector2(1f, 0);

        yield return new WaitForSeconds(1f);

        float platformTravel = platform.position.x - platformXBefore;
        float riderTravel = rider.position.x - riderXBefore;

        Assert.Greater(platformTravel, 0.5f, "Precondition: the platform itself should have moved.");
        Assert.AreEqual(platformTravel, riderTravel, 0.1f,
            $"A body that is both grounded on and attached to the same parent must be carried ONCE " +
            $"(platform {platformTravel:F3}, rider {riderTravel:F3}).");
    }

    // Guards against the fix over-suspending: once detached, the body is an ordinary simulated body
    // again. Passes today.
    [UnityTest]
    public IEnumerator AttachedBody_ResumesOwnSimulation_AfterDetach()
    {
        KinematicMotion2D parent = SpawnBody(new Vector2(6600, 0));
        parent.useGravity = false;
        parent.velocity = Vector2.zero;

        KinematicMotion2D child = SpawnBody(new Vector2(6600, 4f));
        child.useGravity = true;

        yield return null;

        child.AttachTo(parent);
        yield return new WaitForFixedUpdate();

        child.Detach();
        float yAtDetach = child.position.y;

        yield return new WaitForSeconds(0.5f);

        Assert.Less(child.position.y, yAtDetach - 0.5f,
            $"A detached body should fall under gravity again, but it only moved {yAtDetach - child.position.y:F3} units in 0.5s.");
        Assert.Less(child.velocity.y, -1f, "A detached body's velocity should be driven by its own simulation again.");
    }

    // ------------------------------------------------------------------
    // 2c. Symmetric outward effects (specification point 5).
    // All EXPECTED RED: the subtree is invisible to the parent's queries, and the attach carry never
    // routes through the child's own MoveRigidbody, so the child's WillMove never fires.
    // ------------------------------------------------------------------

    // EXPECTED RED.
    [UnityTest]
    public IEnumerator AttachedChild_PushesPushableBody()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(6650, new Vector2(0, 1.5f), pair);

        // A pushable body sitting in the child's band, out of the parent's reach.
        KinematicMotion2D target = SpawnBody(new Vector2(pair.child.position.x + 2f, pair.child.position.y));
        target.useGravity = false;
        target.pushable = true;
        target.velocity = Vector2.zero;
        yield return null;

        AssertOnlyChildCanReach(pair.parent, pair.child, BottomFaceOf(target), TopFaceOf(target));

        float targetXBefore = target.position.x;

        // Driven continuously: a one-shot velocity decays to a halt as soon as it starts pushing.
        DriveAt(pair.parent, new Vector2(1.5f, 0));

        yield return new WaitForSeconds(2.5f);

        Assert.Greater(target.position.x, targetXBefore + 0.5f,
            $"A pushable body met only by an attached child should be pushed by the subtree, but it moved {target.position.x - targetXBefore:F3}.");
    }

    // EXPECTED RED. The converse: a body that refuses to be pushed stops the whole subtree.
    [UnityTest]
    public IEnumerator AttachedChild_BlockedByNonPushableBody_StopsTheParent()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(6700, new Vector2(0, 1.5f), pair);

        KinematicMotion2D blocker = SpawnBody(new Vector2(pair.child.position.x + 2f, pair.child.position.y));
        blocker.useGravity = false;
        blocker.pushable = false;
        blocker.velocity = Vector2.zero;
        yield return null;

        float blockerLeftFace = LeftFaceOf(blocker);
        float parentLimit = blockerLeftFace - pair.offset.x - HalfExtent;

        pair.parent.velocity = new Vector2(1.5f, 0);

        int steps = Mathf.CeilToInt(2.5f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            Assert.LessOrEqual(pair.parent.position.x, parentLimit + 0.05f,
                $"On step {i} the parent drove past the point where its attached child meets a non-pushable body " +
                $"(parent x {pair.parent.position.x:F3}, limit {parentLimit:F3}).");
        }

        Assert.AreEqual(blockerLeftFace, LeftFaceOf(blocker), 0.1f, "A non-pushable blocker should not have been moved.");
        AssertAttachOffsetHeld(pair);
    }

    // EXPECTED RED. Requires the attach carry to route through the child's own MoveRigidbody so its
    // WillMove fires and its riders are carried.
    [UnityTest]
    public IEnumerator RiderOnAttachedChild_IsCarried()
    {
        KinematicMotion2D parent = SpawnBody(new Vector2(6750, 0));
        parent.useGravity = false;
        parent.velocity = Vector2.zero;

        // The attached child acts as a platform for a third body.
        KinematicMotion2D carriedPlatform = SpawnBody(new Vector2(6750, 2f));
        carriedPlatform.transform.localScale = new Vector3(6, 1, 1);
        carriedPlatform.useGravity = false;
        carriedPlatform.velocity = Vector2.zero;

        KinematicMotion2D rider = SpawnBody(new Vector2(6750, 4f));

        yield return null;
        carriedPlatform.AttachTo(parent);

        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(rider.isGrounded, "Precondition: the rider should have landed on the attached platform.");

        float riderXBefore = rider.position.x;
        float parentXBefore = parent.position.x;
        parent.velocity = new Vector2(1f, 0);

        yield return new WaitForSeconds(1f);

        float parentTravel = parent.position.x - parentXBefore;
        Assert.Greater(parentTravel, 0.5f, "Precondition: the parent itself should have moved.");
        Assert.AreEqual(parentTravel, rider.position.x - riderXBefore, 0.15f,
            $"A body standing on an attached child should be carried with it (parent {parentTravel:F3}, rider {rider.position.x - riderXBefore:F3}).");
    }

    // EXPECTED RED. Pins the STATED ASSUMPTION that a subtree pushes as one body of combined mass.
    // This is an inference from "the child's colliders are part of the parent", not something that
    // was specified explicitly - if per-body mass was intended instead, THIS is the test to change.
    //
    // Asserts a direction, not a ratio: the exact figure depends on the mass-ratio arithmetic in
    // Slide, and pinning it exactly would over-fit.
    [UnityTest]
    public IEnumerator AttachedSubtree_PushDistance_UsesCombinedMass()
    {
        float lightTravel = 0f;
        float heavyTravel = 0f;

        // Same push, run twice, differing only in the attached child's mass.
        foreach (bool heavy in new[] { false, true })
        {
            float baseX = heavy ? 6850 : 6800;

            Pair pair = new Pair();
            yield return MakeAttachedPair(baseX, new Vector2(0, 1.5f), pair);
            pair.parent.mass = 1f;
            pair.child.mass = heavy ? 20f : 1f;

            KinematicMotion2D target = SpawnBody(new Vector2(pair.child.position.x + 1.5f, pair.child.position.y));
            target.useGravity = false;
            target.pushable = true;
            target.mass = 5f;
            target.velocity = Vector2.zero;
            yield return null;

            float startX = pair.parent.position.x;
            DriveAt(pair.parent, new Vector2(1.5f, 0));

            yield return new WaitForSeconds(2f);

            float travel = pair.parent.position.x - startX;
            if (heavy) { heavyTravel = travel; } else { lightTravel = travel; }
        }

        Assert.Greater(heavyTravel, lightTravel + 0.1f,
            $"A subtree carrying a heavy child should push through a body more effectively than one carrying a light child " +
            $"(heavy {heavyTravel:F3}, light {lightTravel:F3}). If push mass is meant to be per-body rather than per-subtree, " +
            "this is the assumption to revisit.");
    }

    // ------------------------------------------------------------------
    // 2d. Event fan-out (specification point 6).
    // ------------------------------------------------------------------

    // EXPECTED RED on the parent's half. The child reports its own contact today (its own pass is
    // still running); the parent never does, because its queries do not include the child's
    // colliders. Both halves are asserted so the test stays meaningful after the fix, when the
    // child's copy must come from the parent's fan-out instead of its own pass.
    [UnityTest]
    public IEnumerator AttachedChild_Collision_ReportsContactOnBothBodies()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(6900, new Vector2(0, 1.5f), pair);

        AttachmentContactRecorder parentRecorder = pair.parent.gameObject.AddComponent<AttachmentContactRecorder>();
        AttachmentContactRecorder childRecorder = pair.child.gameObject.AddComponent<AttachmentContactRecorder>();

        float bandBottom = TopFaceOf(pair.parent) + 0.2f;
        float bandTop = bandBottom + 3f;
        AssertOnlyChildCanReach(pair.parent, pair.child, bandBottom, bandTop);

        float wallX = pair.parent.position.x + 2f;
        GameObject wall = CreateStaticFloor(new Vector2(wallX, (bandBottom + bandTop) * 0.5f), new Vector2(1f, bandTop - bandBottom));

        pair.parent.velocity = new Vector2(1.5f, 0);
        yield return new WaitForSeconds(2f);

        Assert.IsTrue(childRecorder.SawContactWith(wall),
            "The child's own collider touched the wall, so the child should receive a contact event for it.");
        Assert.IsTrue(parentRecorder.SawContactWith(wall),
            "The parent should also receive the contact: while attached, the child's colliders are part of the parent's footprint.");

        foreach (GameObject self in parentRecorder.selves)
        {
            Assert.AreEqual(pair.parent.gameObject, self, "Contact.self on the parent's copy should be the parent's own GameObject.");
        }
        foreach (GameObject self in childRecorder.selves)
        {
            Assert.AreEqual(pair.child.gameObject, self, "Contact.self on the child's copy should be the child's own GameObject.");
        }
    }

    // EXPECTED RED. Squeezes the attached child between static geometry and a plain kinematic
    // Rigidbody2D that nothing can stop - the same unavoidable-overlap construction used by the
    // crush tests in TestKinematicCollisions. Both bodies in the subtree should hear about it.
    [UnityTest]
    public IEnumerator AttachedChild_UnresolvableOverlap_CrushesBothBodies()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(6950, new Vector2(0, 1.5f), pair);

        CrushRecorder parentRecorder = pair.parent.gameObject.AddComponent<CrushRecorder>();
        CrushRecorder childRecorder = pair.child.gameObject.AddComponent<CrushRecorder>();

        // A wall hard against the child's right, and an unstoppable mover closing from the left.
        float bandBottom = BottomFaceOf(pair.child);
        float bandTop = TopFaceOf(pair.child);
        float wallX = RightFaceOf(pair.child) + 0.5f + 0.1f;
        CreateStaticFloor(new Vector2(wallX, (bandBottom + bandTop) * 0.5f), new Vector2(1f, bandTop - bandBottom));

        CreateGenericKinematicMover(new Vector2(pair.child.position.x - 3f, pair.child.position.y), new Vector2(2f, 0));

        yield return new WaitForSeconds(3f);

        Assert.Greater(childRecorder.count, 0,
            "The attached child is overlapped by a moving body with a wall behind it and no escape, so it must report a crush.");
        Assert.Greater(parentRecorder.count, 0,
            "The parent should report the crush too: while attached, the child's colliders are part of the parent.");
    }

    // EXPECTED RED. The event-side half of specification point (2): a parent and its attached child
    // overlap all the time, and that overlap is not a collision.
    [UnityTest]
    public IEnumerator AttachedChild_SelfContacts_AreNotReported()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(7000, new Vector2(0.3f, 0), pair);

        AttachmentContactRecorder parentRecorder = pair.parent.gameObject.AddComponent<AttachmentContactRecorder>();
        AttachmentContactRecorder childRecorder = pair.child.gameObject.AddComponent<AttachmentContactRecorder>();

        pair.parent.velocity = new Vector2(1f, 0);
        yield return new WaitForSeconds(1f);

        Assert.IsFalse(parentRecorder.SawContactWith(pair.child.gameObject),
            "A parent must not report a contact with its own attached child.");
        Assert.IsFalse(childRecorder.SawContactWith(pair.parent.gameObject),
            "An attached child must not report a contact with its own parent.");
    }

    // ------------------------------------------------------------------
    // 2e. Lifecycle. These are regression guards - they should pass today.
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator AttachTo_SetsAttachedTo_AndRegistersWithParent()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(7050, new Vector2(0, 1.5f), pair);

        Assert.AreEqual(pair.parent, pair.child.attachedTo, "attachedTo should report the parent the body was attached to.");
        Assert.AreEqual(1, pair.parent.attachedCount, "The parent should have exactly one attached body.");
        Assert.AreEqual(0, pair.child.attachedCount, "The child has nothing attached to it.");
        Assert.IsNull(pair.parent.attachedTo, "The parent is not itself attached to anything.");
    }

    [UnityTest]
    public IEnumerator AttachTo_SamePlatformTwice_DoesNotDuplicate()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(7100, new Vector2(0, 1.5f), pair);

        pair.child.AttachTo(pair.parent); // second time
        Assert.AreEqual(1, pair.parent.attachedCount, "Attaching to the same parent twice must not register the body twice.");

        // Observable consequence of a duplicate registration would be a doubled carry.
        float parentXBefore = pair.parent.position.x;
        float childXBefore = pair.child.position.x;
        pair.parent.velocity = new Vector2(1f, 0);

        yield return new WaitForSeconds(1f);

        float parentTravel = pair.parent.position.x - parentXBefore;
        Assert.Greater(parentTravel, 0.5f, "Precondition: the parent should have moved.");
        Assert.AreEqual(parentTravel, pair.child.position.x - childXBefore, 0.05f,
            "A doubly-registered child would be carried twice per frame.");
    }

    [UnityTest]
    public IEnumerator AttachTo_DifferentParent_DetachesFromTheFirst()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(7150, new Vector2(0, 1.5f), pair);

        KinematicMotion2D secondParent = SpawnBody(new Vector2(7170, 0));
        secondParent.useGravity = false;
        secondParent.velocity = Vector2.zero;
        yield return null;

        pair.child.AttachTo(secondParent);

        Assert.AreEqual(secondParent, pair.child.attachedTo, "attachedTo should report the new parent.");
        Assert.AreEqual(0, pair.parent.attachedCount, "The original parent should no longer hold the body.");
        Assert.AreEqual(1, secondParent.attachedCount, "The new parent should hold it instead.");

        // The old parent must no longer carry it.
        float childXBefore = pair.child.position.x;
        pair.parent.velocity = new Vector2(2f, 0);
        yield return new WaitForSeconds(0.5f);

        Assert.AreEqual(childXBefore, pair.child.position.x, 0.05f, "The original parent must not carry a body that has been re-attached elsewhere.");
    }

    [UnityTest]
    public IEnumerator AttachTo_Null_Detaches()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(7200, new Vector2(0, 1.5f), pair);

        pair.child.AttachTo(null);

        Assert.IsNull(pair.child.attachedTo, "AttachTo(null) should detach.");
        Assert.AreEqual(0, pair.parent.attachedCount, "AttachTo(null) should deregister from the previous parent.");
    }

    [UnityTest]
    public IEnumerator Detach_WithNoParent_IsSafe()
    {
        KinematicMotion2D body = SpawnBody(new Vector2(7250, 0));
        body.useGravity = false;
        yield return null;

        Assert.IsNull(body.attachedTo, "Precondition: the body starts unattached.");
        Assert.DoesNotThrow(() => body.Detach(), "Detaching an unattached body should be a no-op, not a throw.");
        Assert.IsNull(body.attachedTo);
    }

    [UnityTest]
    public IEnumerator DestroyingAttachedChild_RemovesItFromParent()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(7300, new Vector2(0, 1.5f), pair);

        Object.DestroyImmediate(pair.child.gameObject);

        Assert.AreEqual(0, pair.parent.attachedCount, "A destroyed child should remove itself from its parent's attachment list.");

        // The parent must still be able to move without tripping over the stale entry.
        float xBefore = pair.parent.position.x;
        pair.parent.velocity = new Vector2(1f, 0);
        yield return new WaitForSeconds(0.5f);

        Assert.Greater(pair.parent.position.x, xBefore + 0.2f, "The parent should still move normally after an attached child is destroyed.");
    }

    // ------------------------------------------------------------------
    // 2f. Graph and teardown edges.
    // ------------------------------------------------------------------

    // EXPECTED RED. The attach carry writes the child's rigidbody position directly instead of
    // routing through the child's own MoveRigidbody, so the chain stops at the first level.
    [UnityTest]
    public IEnumerator AttachmentChain_PropagatesToGrandchild()
    {
        KinematicMotion2D a = SpawnBody(new Vector2(7350, 0));
        a.useGravity = false;
        a.velocity = Vector2.zero;

        KinematicMotion2D b = SpawnBody(new Vector2(7350, 1.5f));
        b.useGravity = false;
        b.velocity = Vector2.zero;

        KinematicMotion2D c = SpawnBody(new Vector2(7350, 3f));
        c.useGravity = false;
        c.velocity = Vector2.zero;

        yield return null;

        b.AttachTo(a);
        c.AttachTo(b);
        yield return new WaitForFixedUpdate();

        float aXBefore = a.position.x;
        float bXBefore = b.position.x;
        float cXBefore = c.position.x;

        a.velocity = new Vector2(1f, 0);
        yield return new WaitForSeconds(1f);

        float aTravel = a.position.x - aXBefore;
        Assert.Greater(aTravel, 0.5f, "Precondition: the root should have moved.");
        Assert.AreEqual(aTravel, b.position.x - bXBefore, 0.05f, "The direct child should follow the root.");
        Assert.AreEqual(aTravel, c.position.x - cXBefore, 0.05f,
            $"The grandchild should follow the root too (root {aTravel:F3}, grandchild {c.position.x - cXBefore:F3}): " +
            "attachment must propagate through the whole chain.");
    }

    // EXPECTED RED. Attaching a body to itself puts it in its own carry list, so every move is
    // applied twice and the recomputed velocity doubles each frame - a runaway. The run is kept to
    // a handful of steps so a regression fails on the assertion rather than diverging to infinity.
    [UnityTest]
    public IEnumerator SelfAttachment_DoesNotDoubleMove()
    {
        KinematicMotion2D body = SpawnBody(new Vector2(7400, 0));
        body.useGravity = false;
        yield return null;

        body.AttachTo(body);

        float xBefore = body.position.x;
        body.velocity = new Vector2(1f, 0);

        const int steps = 5;
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
        }

        float expected = 1f * steps * Time.fixedDeltaTime;
        Assert.IsFalse(float.IsNaN(body.position.x) || float.IsInfinity(body.position.x),
            "Self-attachment should not drive the body's position to a non-finite value.");
        Assert.AreEqual(expected, body.position.x - xBefore, expected * 0.5f,
            $"A self-attached body must not be carried by itself: it travelled {body.position.x - xBefore:F4} where {expected:F4} was expected.");
    }

    // NOT expected to fail today, and that is the point of having it: the current carry writes
    // positions directly instead of recursing, so a cycle happens to be harmless. The fix changes
    // exactly that - recursing through MoveRigidbody, and walking the subtree to gather colliders,
    // both traverse the attachment graph, which AttachTo does nothing to keep acyclic. This test is
    // here so that the day a visited-set or depth cap is missing, it is caught immediately.
    //
    // Bounded step count so a regression fails on an assertion rather than hanging the Test Runner.
    [UnityTest]
    public IEnumerator AttachmentCycle_DoesNotHangOrThrow()
    {
        KinematicMotion2D a = SpawnBody(new Vector2(7450, 0));
        a.useGravity = false;
        a.velocity = Vector2.zero;

        KinematicMotion2D b = SpawnBody(new Vector2(7450, 2f));
        b.useGravity = false;
        b.velocity = Vector2.zero;

        yield return null;

        a.AttachTo(b);
        b.AttachTo(a);

        a.velocity = new Vector2(1f, 0);

        const int steps = 10;
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
        }

        Assert.IsFalse(float.IsNaN(a.position.x) || float.IsInfinity(a.position.x), "A cycle must not drive positions to a non-finite value.");
        Assert.IsFalse(float.IsNaN(b.position.x) || float.IsInfinity(b.position.x), "A cycle must not drive positions to a non-finite value.");

        float expected = 1f * steps * Time.fixedDeltaTime;
        Assert.AreEqual(expected, a.position.x - 7450f, expected,
            $"An attachment cycle should not compound movement: the body travelled {a.position.x - 7450f:F4} where about {expected:F4} was expected.");
    }

    // EXPECTED RED. The attach carry uses the `rb` field, which is only assigned in Start(), rather
    // than the `rigidbody` property that lazily fetches it. Attaching a body whose Start has not run
    // therefore throws - and it throws from inside the PARENT's Slide, so the parent's own movement
    // for that frame is lost too. That second effect is what the displacement assertion pins.
    //
    // The child is spawned inactive to make "has not started" deterministic, rather than racing
    // Unity's Start-before-first-FixedUpdate ordering.
    [UnityTest]
    public IEnumerator AttachingNotYetStartedBody_DoesNotThrow_AndParentStillMoves()
    {
        KinematicMotion2D parent = SpawnBody(new Vector2(7500, 0));
        parent.useGravity = false;
        parent.velocity = Vector2.zero;
        yield return null;

        KinematicMotion2D child = SpawnBody(new Vector2(7500, 1.5f));
        child.useGravity = false;
        child.gameObject.SetActive(false); // Start() never runs, so its rb field stays null

        child.AttachTo(parent);

        float xBefore = parent.position.x;
        parent.velocity = new Vector2(1f, 0);

        yield return new WaitForSeconds(1f);

        Assert.AreEqual(xBefore + 1f, parent.position.x, 0.1f,
            $"Attaching a body that has not started must not disturb the parent's own movement, but the parent moved " +
            $"{parent.position.x - xBefore:F3} of the expected 1.0.");
    }

    // EXPECTED RED. MoveRigidbody is the only carry path, so a body repositioned through the
    // documented `position` property leaves its subtree behind.
    [UnityTest]
    public IEnumerator TeleportingParentViaPosition_CarriesAttachedSubtree()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(7550, new Vector2(0, 1.5f), pair);

        pair.parent.position = pair.parent.position + new Vector2(5f, 0);
        yield return new WaitForFixedUpdate();

        Vector2 offset = pair.child.position - pair.parent.position;
        Assert.AreEqual(pair.offset.x, offset.x, 0.05f,
            "Repositioning a parent through `position` should carry its attached subtree with it.");
        Assert.AreEqual(pair.offset.y, offset.y, 0.05f,
            "Repositioning a parent through `position` should carry its attached subtree with it.");
    }

    // EXPECTED RED. Separate() writes the rigidbody position directly, so an overlap correction moves
    // the parent out from under its own subtree.
    //
    // Staged as a shallow overlap that CAN be resolved, so this exercises separation rather than the
    // bad-placement warning path.
    [UnityTest]
    public IEnumerator SeparatingParent_CarriesAttachedSubtree()
    {
        KinematicMotion2D parent = SpawnBody(new Vector2(7600, 0));
        parent.useGravity = false;
        parent.velocity = Vector2.zero;

        // The child sits well clear of the geometry, so only the parent needs separating.
        KinematicMotion2D child = SpawnBody(new Vector2(7600, 3f));
        child.useGravity = false;
        child.velocity = Vector2.zero;

        yield return null;
        child.AttachTo(parent);
        yield return new WaitForFixedUpdate();

        Vector2 offsetBefore = child.position - parent.position;
        float parentYBefore = parent.position.y;

        // Drop the parent into a shallow overlap with a static floor. ProcessOverlaps should push it
        // back out on the next step.
        CreateStaticFloor(new Vector2(7600, parent.position.y - 0.8f), new Vector2(6f, 1f));
        yield return new WaitForSeconds(0.5f);

        Assert.AreNotEqual(parentYBefore, parent.position.y,
            "Precondition: the parent should have been separated out of the overlapping floor.");

        Vector2 offsetAfter = child.position - parent.position;
        Assert.AreEqual(offsetBefore.y, offsetAfter.y, 0.05f,
            $"Overlap separation should carry the attached subtree with the parent (offset was {offsetBefore.y:F3}, now {offsetAfter.y:F3}).");
    }

    // EXPECTED RED. OnDestroy detaches a body from its own parent but never detaches its children,
    // so they are left pointing at a destroyed component.
    [UnityTest]
    public IEnumerator DestroyingParent_DetachesItsChildren()
    {
        Pair pair = new Pair();
        yield return MakeAttachedPair(7650, new Vector2(0, 1.5f), pair);

        KinematicMotion2D child = pair.child;
        Object.DestroyImmediate(pair.parent.gameObject);

        Assert.IsNull(child.attachedTo, "Destroying a parent should detach the bodies attached to it, not leave them pointing at a destroyed component.");

        // And it should behave as an ordinary free body again.
        child.useGravity = true;
        float yBefore = child.position.y;
        yield return new WaitForSeconds(0.5f);

        Assert.Less(child.position.y, yBefore - 0.5f, "An orphaned body should resume falling under its own simulation.");
    }
}
