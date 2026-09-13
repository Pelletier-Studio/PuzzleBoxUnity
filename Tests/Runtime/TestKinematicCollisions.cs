using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PuzzleBox;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public class TestKinematicCollisions : KinematicTestFixture
{
    // Prefab names, scene loading, spawning and teardown live in KinematicTestFixture.

    // Records the standard Unity collision/trigger messages so tests can assert on them.
    private class CollisionRecorder : MonoBehaviour
    {
        public int enterCount;
        public int exitCount;
        public int stayCount;
        public int triggerEnterCount;
        public GameObject lastOther;

        void OnCollisionEnter2D(Collision2D collision)
        {
            enterCount++;
            lastOther = collision.gameObject;
        }

        void OnCollisionExit2D(Collision2D collision)
        {
            exitCount++;
            lastOther = collision.gameObject;
        }

        void OnCollisionStay2D(Collision2D collision)
        {
            stayCount++;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            triggerEnterCount++;
        }
    }

    // Records KinematicMotion2D's own custom contact events (OnContactEnter/Exit/Stay).
    // Kinematic-vs-Kinematic pairs do not reliably produce Unity's native
    // OnCollisionEnter2D/Stay2D/Exit2D messages (this depends on
    // Rigidbody2D.useFullKinematicContacts, which has a long history of being
    // unreliable across Unity versions for Kinematic-vs-Kinematic pairs specifically).
    // KinematicMotion2D's own Contact/OnContactEnter/OnContactStay/OnContactExit API is
    // the supported way to detect this particular pairing.
    private class ContactRecorder : MonoBehaviour
    {
        public int enterCount;
        public int exitCount;
        public int stayCount;
        public GameObject lastOther;

        void OnContactEnter(KinematicMotion2D.Contact contact)
        {
            enterCount++;
            lastOther = OtherGameObject(contact);
        }

        void OnContactExit(KinematicMotion2D.Contact contact)
        {
            exitCount++;
            lastOther = OtherGameObject(contact);
        }

        void OnContactStay(KinematicMotion2D.Contact contact)
        {
            stayCount++;
        }

        private static GameObject OtherGameObject(KinematicMotion2D.Contact contact)
        {
            return contact.rigidbody != null ? contact.rigidbody.gameObject : contact.collider.gameObject;
        }
    }

    [UnityTest]
    public IEnumerator KinematicBody_CollidesWithStaticGeometry()
    {
        CreateStaticFloor(new Vector2(0, 0), new Vector2(5, 1));

        GameObject body = Spawn(KinematicBodyPrefabName, new Vector2(0, 3));
        CollisionRecorder recorder = body.AddComponent<CollisionRecorder>();
        KinematicMotion2D motion = body.GetComponent<KinematicMotion2D>();

        // Make sure that margin is less than maximumContactOffset
        motion.margin = 0f;

        // Let the body fall under gravity and land on the floor.
        yield return new WaitForSeconds(1.5f);

        Assert.Greater(recorder.enterCount, 0, "OnCollisionEnter2D should fire when the kinematic body lands on static geometry.");

        recorder.stayCount = 0;
        yield return new WaitForSeconds(0.5f);
        Assert.Greater(recorder.stayCount, 0, "OnCollisionStay2D should fire while the kinematic body rests on static geometry.");

        // Move the body straight up, away from the floor.
        motion.velocity = new Vector2(0, 20);
        yield return new WaitForSeconds(0.5f);

        Assert.Greater(recorder.exitCount, 0, "OnCollisionExit2D should fire when the kinematic body leaves static geometry.");
    
        // Now add a margin
        motion.margin = KinematicMotion2D.maximumContactOffset;
        body.transform.position = new Vector2(0, 3);
        
        
        recorder.enterCount = 0;
        // Let the body fall under gravity and land on the floor.
        yield return new WaitForSeconds(1.5f);

        Assert.AreEqual(0, recorder.enterCount, "OnCollisionEnter2D should not fire when the kinematic body with a large margin lands on static geometry.");

        recorder.stayCount = 0;
        yield return new WaitForSeconds(0.5f);
        Assert.AreEqual(0, recorder.stayCount, "OnCollisionStay2D should not fire while the kinematic body with a large margin rests on static geometry.");

        // Move the body straight up, away from the floor.
        motion.velocity = new Vector2(0, 20);
        recorder.exitCount = 0;
        yield return new WaitForSeconds(0.5f);

        Assert.AreEqual(0, recorder.exitCount, "OnCollisionExit2D should not fire when the kinematic body with a large margin leaves static geometry.");
    }

    [UnityTest]
    public IEnumerator KinematicBody_CollidesWithDynamicRigidbody()
    {
        GameObject dynamicBody = Spawn(RigidbodyPrefabName, new Vector2(0, 0));
        Rigidbody2D dynamicRb = dynamicBody.GetComponent<Rigidbody2D>();
        dynamicRb.gravityScale = 0f;
        dynamicRb.constraints = RigidbodyConstraints2D.FreezeAll;

        GameObject body = Spawn(KinematicBodyPrefabName, new Vector2(0, 3));
        CollisionRecorder recorder = body.AddComponent<CollisionRecorder>();
        KinematicMotion2D motion = body.GetComponent<KinematicMotion2D>();

        // Make sure that margin is less than maximumContactOffset
        motion.margin = 0f;

        yield return new WaitForSeconds(1.5f);

        Assert.Greater(recorder.enterCount, 0, "OnCollisionEnter2D should fire when the kinematic body lands on a dynamic Rigidbody2D.");
        Assert.AreEqual(dynamicBody, recorder.lastOther, "The recorded collision should be with the dynamic Rigidbody2D.");

        recorder.stayCount = 0;
        yield return new WaitForSeconds(0.5f);
        Assert.Greater(recorder.stayCount, 0, "OnCollisionStay2D should fire while the kinematic body rests on a dynamic Rigidbody2D.");

        motion.velocity = new Vector2(0, 20);
        yield return new WaitForSeconds(0.5f);

        Assert.Greater(recorder.exitCount, 0, "OnCollisionExit2D should fire when the kinematic body leaves a dynamic Rigidbody2D.");

        // Now add a margin.
        motion.margin = KinematicMotion2D.maximumContactOffset;
        body.transform.position = new Vector2(0, 3);

        recorder.enterCount = 0;

        // Let the body fall under gravity and land on the floor.
        yield return new WaitForSeconds(1.5f);

        Assert.AreEqual(0, recorder.enterCount, "OnCollisionEnter2D should not fire when the kinematic body with a large margin lands on a dynamic Rigidbody2D.");

        recorder.stayCount = 0;
        yield return new WaitForSeconds(0.5f);
        Assert.AreEqual(0, recorder.stayCount, "OnCollisionStay2D should not fire while the kinematic body with a large margin rests on a dynamic Rigidbody2D.");

        // Move the body straight up, away from the floor.
        motion.velocity = new Vector2(0, 20);
        recorder.exitCount = 0;
        yield return new WaitForSeconds(0.5f);

        Assert.AreEqual(0, recorder.exitCount, "OnCollisionExit2D should not fire when the kinematic body with a large margin leaves a dynamic Rigidbody2D.");
    }

    // Kinematic-vs-Kinematic pairs are a rare enough scenario, and Unity's native
    // OnCollisionEnter2D/Stay2D/Exit2D for this pairing (via Rigidbody2D.useFullKinematicContacts)
    // is unreliable enough across engine versions, that KinematicMotion2D does not attempt to
    // force native messages to fire here the way it does for static geometry and dynamic
    // Rigidbody2D targets. For this pairing, use KinematicMotion2D's own
    // OnContactEnter/OnContactStay/OnContactExit messages instead.
    [UnityTest]
    public IEnumerator KinematicBody_OtherKinematicBody_FiresCustomContactEvents()
    {
        GameObject ground = Spawn(KinematicBodyPrefabName, new Vector2(0, 0));
        ground.GetComponent<KinematicMotion2D>().simulatePhysics = false;

        GameObject body = Spawn(KinematicBodyPrefabName, new Vector2(0, 3));
        ContactRecorder recorder = body.AddComponent<ContactRecorder>();
        KinematicMotion2D motion = body.GetComponent<KinematicMotion2D>();

        yield return new WaitForSeconds(1.5f);

        Assert.Greater(recorder.enterCount, 0, "OnContactEnter should fire when two kinematic bodies touch.");
        Assert.AreEqual(ground, recorder.lastOther, "The recorded contact should be with the other kinematic body.");

        recorder.stayCount = 0;
        yield return new WaitForSeconds(0.5f);
        Assert.Greater(recorder.stayCount, 0, "OnContactStay should fire while resting on another kinematic body.");

        motion.velocity = new Vector2(0, 20);
        yield return new WaitForSeconds(0.5f);

        Assert.Greater(recorder.exitCount, 0, "OnContactExit should fire when leaving another kinematic body.");
    }

    // Staged in its own lane, clear of the scene's Ground (which spans x -7.5..7.5 with its top
    // surface at y=-3). At x=0 the body fell through the trigger and then landed on that Ground
    // about a second later, so the enterCount/stayCount assertions below were counting a perfectly
    // legitimate collision with the floor and had nothing to do with the trigger.
    //
    // margin is pinned to 0 rather than inherited from the prefab (whose serialized data predates
    // the field, so its deserialized value is not something to depend on). That matters: 0 is the
    // configuration in which KinematicMotion2D *does* produce native collision messages against
    // solid geometry - see KinematicBody_CollidesWithStaticGeometry - so "no messages fired" below
    // is a real claim about the trigger instead of a vacuous pass at a margin that suppresses them.
    [UnityTest]
    public IEnumerator KinematicBody_DoesNotCollideWithTrigger()
    {
        GameObject trigger = Spawn(TriggerPrefabName, new Vector2(5000, 0));

        GameObject body = Spawn(KinematicBodyPrefabName, new Vector2(5000, 3));
        CollisionRecorder recorder = body.AddComponent<CollisionRecorder>();
        KinematicMotion2D motion = body.GetComponent<KinematicMotion2D>();

        motion.margin = 0f;

        // Let the body fall through the trigger's position entirely. Nothing is below it in this
        // lane, so it is still in free fall when the window closes.
        yield return new WaitForSeconds(1.5f);

        Assert.Greater(recorder.triggerEnterCount, 0, "OnTriggerEnter2D should still fire, confirming the body actually reached the trigger.");
        Assert.AreEqual(0, recorder.enterCount, "OnCollisionEnter2D should never fire for a trigger collider.");
        Assert.AreEqual(0, recorder.stayCount, "OnCollisionStay2D should never fire for a trigger collider.");
        Assert.AreEqual(0, recorder.exitCount, "OnCollisionExit2D should never fire for a trigger collider.");
        Assert.Less(body.transform.position.y, trigger.transform.position.y, "The kinematic body should fall straight through the trigger instead of being stopped by it.");
    }

    // ------------------------------------------------------------------
    // Grounded state
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator KinematicBody_FallingInAir_IsNotGroundedAndTimeInAirIncreases()
    {
        float groundTop = GetTopSurfaceY(FindGround());

        KinematicMotion2D motion = SpawnBody(new Vector2(0, groundTop + 10));

        // Sample while still well clear of the ground below.
        yield return new WaitForSeconds(0.3f);

        Assert.IsFalse(motion.isGrounded, "A body falling freely through open air should not be grounded.");
        Assert.Greater(motion.timeInAir, 0f, "timeInAir should accumulate while airborne.");
    }

    [UnityTest]
    public IEnumerator KinematicBody_LandsOnGround_BecomesGroundedAndTimeInAirResets()
    {
        float groundTop = GetTopSurfaceY(FindGround());

        KinematicMotion2D motion = SpawnBody(new Vector2(0, groundTop + 3));

        yield return new WaitForSeconds(1.5f);

        Assert.IsTrue(motion.isGrounded, "The body should be grounded after falling onto the Ground collider.");
        Assert.AreEqual(0f, motion.timeInAir, "timeInAir should reset to 0 once grounded.");
    }

    [UnityTest]
    public IEnumerator KinematicBody_JustLanded_TrueOnlyOnLandingFrame()
    {
        float groundTop = GetTopSurfaceY(FindGround());

        KinematicMotion2D motion = SpawnBody(new Vector2(0, groundTop + 3));

        int justLandedCount = 0;
        int steps = Mathf.CeilToInt(1.5f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            if (motion.justLanded)
            {
                justLandedCount++;
            }
        }

        Assert.AreEqual(1, justLandedCount, "justLanded should be true for exactly one frame: the frame the body lands.");
        Assert.IsTrue(motion.isGrounded, "The body should still be grounded after landing.");
    }

    [UnityTest]
    public IEnumerator KinematicBody_JustFell_TrueOnlyOnFrameLeavingGround()
    {
        float groundTop = GetTopSurfaceY(FindGround());

        KinematicMotion2D motion = SpawnBody(new Vector2(0, groundTop + 3));

        // Let the body land first.
        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(motion.isGrounded, "Precondition: the body should be grounded before we make it leave the ground.");

        // Launch it straight up, away from the ground.
        motion.velocity = new Vector2(0, 20);

        int justFellCount = 0;
        int steps = Mathf.CeilToInt(0.5f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            if (motion.justFell)
            {
                justFellCount++;
            }
        }

        Assert.AreEqual(1, justFellCount, "justFell should be true for exactly one frame: the frame the body leaves the ground.");
        Assert.IsFalse(motion.isGrounded, "The body should no longer be grounded after launching away from the ground.");
    }

    [UnityTest]
    public IEnumerator KinematicBody_GroundNormalAndGroundRight_MatchFlatGround()
    {
        float groundTop = GetTopSurfaceY(FindGround());

        KinematicMotion2D motion = SpawnBody(new Vector2(0, groundTop + 3));

        yield return new WaitForSeconds(1.5f);

        Assert.IsTrue(motion.isGrounded);
        Assert.Less(Vector2.Distance(Vector2.up, motion.groundNormal), 0.01f, "groundNormal should point straight up on flat ground.");
        Assert.Less(Vector2.Distance(Vector2.right, motion.groundRight), 0.01f, "groundRight should point right when the ground normal points up.");
    }

    [UnityTest]
    public IEnumerator KinematicBody_RemainsGrounded_WhileRestingAcrossMultipleFrames()
    {
        float groundTop = GetTopSurfaceY(FindGround());

        KinematicMotion2D motion = SpawnBody(new Vector2(0, groundTop + 3));

        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(motion.isGrounded, "Precondition: the body should be grounded before checking that it stays grounded.");

        for (int i = 0; i < 10; i++)
        {
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(motion.isGrounded, "The body should remain grounded while resting motionless on flat ground.");
        }
    }

    [UnityTest]
    public IEnumerator KinematicBody_DoesNotSinkIntoGround_WhileResting()
    {
        float groundTop = GetTopSurfaceY(FindGround());

        KinematicMotion2D motion = SpawnBody(new Vector2(0, groundTop + 3));
        float halfHeight = motion.GetBounds(true).extents.y;

        yield return new WaitForSeconds(2f);

        float bottom = motion.position.y - halfHeight;
        Assert.GreaterOrEqual(bottom, groundTop - 0.05f, "The body should rest on top of the ground, not sink through it.");
    }

    [Test]
    public void IsGroundNormal_WallNormal_CeilingNormal_ClassifyAnglesCorrectly()
    {
        KinematicMotion2D motion = SpawnBody(new Vector2(1000, 1000));

        // Straight up: ground, not wall, not ceiling.
        Assert.IsTrue(motion.IsGroundNormal(Vector2.up));
        Assert.IsFalse(motion.IsWallNormal(Vector2.up));
        Assert.IsFalse(motion.IsCeilingNormal(Vector2.up));

        // Straight down: ceiling, not wall, not ground.
        Assert.IsFalse(motion.IsGroundNormal(Vector2.down));
        Assert.IsFalse(motion.IsWallNormal(Vector2.down));
        Assert.IsTrue(motion.IsCeilingNormal(Vector2.down));

        // Sideways: wall, neither ground nor ceiling.
        Assert.IsFalse(motion.IsGroundNormal(Vector2.right));
        Assert.IsTrue(motion.IsWallNormal(Vector2.right));
        Assert.IsFalse(motion.IsCeilingNormal(Vector2.right));

        Assert.IsFalse(motion.IsGroundNormal(Vector2.left));
        Assert.IsTrue(motion.IsWallNormal(Vector2.left));
        Assert.IsFalse(motion.IsCeilingNormal(Vector2.left));

        // Just inside the default 45 degree ground threshold.
        Vector2 justInsideGround = Quaternion.Euler(0, 0, 44f) * Vector2.up;
        Assert.IsTrue(motion.IsGroundNormal(justInsideGround), "A 44 degree slope should still count as ground with the default 45 degree threshold.");

        // Just outside the default 45 degree ground threshold: should be classified as a wall.
        Vector2 justOutsideGround = Quaternion.Euler(0, 0, 46f) * Vector2.up;
        Assert.IsFalse(motion.IsGroundNormal(justOutsideGround), "A 46 degree slope should exceed the default 45 degree ground threshold.");
        Assert.IsTrue(motion.IsWallNormal(justOutsideGround), "A slope beyond the ground threshold (but well short of the ceiling threshold) should be classified as a wall.");
    }

    [UnityTest]
    public IEnumerator KinematicBody_GroundedOnSlope_WithinMaxAngle()
    {
        // Placed far from the other tests' geometry to avoid any cross-contact.
        const float rampAngle = 30f; // Within the default 45 degree maxGroundAngleDegrees.
        Vector2 rampCenter = new Vector2(500, 0);
        CreateStaticFloor(rampCenter, new Vector2(10, 4), rampAngle);

        KinematicMotion2D motion = SpawnBody(new Vector2(rampCenter.x, rampCenter.y + 5));

        yield return new WaitForSeconds(1.5f);

        Assert.IsTrue(motion.isGrounded, "A body landing on a 30 degree slope should be grounded (below the default 45 degree threshold).");
        Assert.IsTrue(motion.IsGroundNormal(motion.groundNormal), "The reported groundNormal should itself classify as ground.");

        Vector2 expectedNormal = Quaternion.Euler(0, 0, rampAngle) * Vector2.up;
        Assert.Less(Vector2.Angle(expectedNormal, motion.groundNormal), 1f, "groundNormal should match the slope's actual surface normal.");
    }

    [UnityTest]
    public IEnumerator KinematicBody_NotGrounded_WhenUseGravityDisabled_EvenWhileTouchingGround()
    {
        float groundTop = GetTopSurfaceY(FindGround());

        KinematicMotion2D motion = SpawnBody(new Vector2(0, groundTop + 3));

        // Let it land normally first.
        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(motion.isGrounded, "Precondition: the body should be grounded before disabling gravity.");

        // CheckForGround() early-outs when useGravity is false, so a body resting in
        // contact with the ground should stop being reported as grounded.
        motion.useGravity = false;

        yield return new WaitForSeconds(0.3f);

        Assert.IsFalse(motion.isGrounded, "isGrounded should be false while useGravity is disabled, even while physically touching the ground.");
    }

    [UnityTest]
    public IEnumerator KinematicBody_UpdateGroundedState_CanBeCalledManually()
    {
        float groundTop = GetTopSurfaceY(FindGround());

        KinematicMotion2D motion = SpawnBody(new Vector2(0, groundTop + 3));

        // Disable automatic physics so FixedUpdate never runs UpdateGroundedState for us.
        motion.simulatePhysics = false;

        // Let Start() run so the component's collider cache is initialized.
        yield return null;

        float halfHeight = motion.GetBounds(true).extents.y;
        motion.position = new Vector2(0, groundTop + halfHeight);

        Assert.IsFalse(motion.isGrounded, "Precondition: isGrounded should still be false since physics simulation is disabled and no grounded check has run yet.");

        motion.UpdateGroundedState();

        Assert.IsTrue(motion.isGrounded, "UpdateGroundedState() should synchronously detect the ground directly beneath the body.");
    }

    [UnityTest]
    public IEnumerator KinematicBody_LastGroundVelocity_FreezesAtMomentOfLeavingGround()
    {
        float groundTop = GetTopSurfaceY(FindGround());

        KinematicMotion2D motion = SpawnBody(new Vector2(0, groundTop + 3));

        // Let it land, then give it a steady horizontal velocity while grounded.
        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(motion.isGrounded, "Precondition: the body should be grounded before running along the ground.");

        float xBeforeRunning = motion.position.x;
        motion.velocity = new Vector2(5, 0);
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        Vector2 lastGroundVelocityWhileGrounded = motion.lastGroundVelocity;

        // Deliberately strict thresholds rather than "> 0". A body that is blocked from moving
        // along its own ground still ends up with a tiny positive residue here (it advances by
        // the sub-millimetre contact distance each frame), so a "> 0" assertion would pass even
        // when horizontal movement is completely broken.
        Assert.Greater(motion.position.x, xBeforeRunning + 0.05f, "The body should actually travel along the ground when given a horizontal velocity.");
        Assert.Greater(lastGroundVelocityWhileGrounded.x, 1f, "lastGroundVelocity should track the body's real horizontal velocity while grounded.");

        // Now launch the body up and away from the ground.
        motion.velocity = new Vector2(lastGroundVelocityWhileGrounded.x, 20);
        yield return new WaitForFixedUpdate();

        Assert.IsFalse(motion.isGrounded, "The body should have left the ground after being launched upward.");
        Assert.AreEqual(lastGroundVelocityWhileGrounded.x, motion.lastGroundVelocity.x, 0.5f,
            "lastGroundVelocity should freeze at the value from the last grounded frame instead of tracking the in-air velocity.");

        // Keep falling; lastGroundVelocity should remain frozen even as velocity changes under gravity.
        yield return new WaitForSeconds(0.3f);
        Assert.AreEqual(lastGroundVelocityWhileGrounded.x, motion.lastGroundVelocity.x, 0.5f,
            "lastGroundVelocity should stay frozen while airborne.");
    }

    // Isolates the ground-carry mechanism (WillMove -> GroundWillMove) from the separate question
    // of what landing velocity a body inherits. The platform is stationary while the rider lands,
    // so the `velocity -= groundVelocity` adjustment on the landing frame subtracts zero and
    // cannot mask the carry. Only then does the platform start moving.
    [UnityTest]
    public IEnumerator KinematicBody_IsCarriedByPlatform_ThatStartsMovingAfterLanding()
    {
        // Placed far from the other tests' geometry to avoid any cross-contact.
        Vector2 platformStart = new Vector2(1000, 0);
        KinematicMotion2D platformMotion = SpawnBody(platformStart);
        platformMotion.transform.localScale = new Vector3(10, 1, 1);
        platformMotion.useGravity = false;
        platformMotion.velocity = Vector2.zero;

        KinematicMotion2D riderMotion = SpawnBody(new Vector2(platformStart.x, platformStart.y + 3));

        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(riderMotion.isGrounded, "Precondition: the rider should have landed on the stationary platform.");

        float riderXBefore = riderMotion.position.x;
        float platformXBefore = platformMotion.position.x;
        platformMotion.velocity = new Vector2(1, 0);

        yield return new WaitForSeconds(1f);

        float platformTravel = platformMotion.position.x - platformXBefore;
        float riderTravel = riderMotion.position.x - riderXBefore;

        Assert.Greater(platformTravel, 0.5f, "Precondition: the platform itself should have moved.");
        Assert.IsTrue(riderMotion.isGrounded, "The rider should still be grounded on the platform after riding it.");
        Assert.AreEqual(platformTravel, riderTravel, 0.1f, $"The rider should be carried the same distance the platform travelled (platform {platformTravel:F3}, rider {riderTravel:F3}).");
        Assert.AreEqual(1f, riderMotion.groundVelocity.x, 0.1f, "groundVelocity should report the platform's velocity while standing on it.");
    }

    // Documents deliberate, frictionless behaviour - this is NOT a bug, do not "fix" it.
    //
    // On the landing frame KinematicMotion2D applies `velocity -= groundVelocity`, converting the
    // body's velocity into ground-relative terms. A body that drops straight down onto a platform
    // already moving at (1,0) therefore lands with velocity (-1, y): relative to the platform it
    // genuinely is drifting backwards at 1 unit/s.
    //
    // There is no friction model, so that relative velocity never decays. Each frame the platform
    // carries the body +1*dt via GroundWillMove while the body drives itself -1*dt, and the two
    // cancel exactly - the body holds station in world space while the platform slides underneath
    // it, still reporting isGrounded. The cancellation is self-sustaining because the carry happens
    // during the platform's FixedUpdate, outside the body's own startPosition -> actualMotion
    // window, so `velocity = actualMotion / deltaSeconds` re-derives -1 every frame.
    //
    // The case this adjustment exists for works correctly: jump from a moving platform and land
    // back on it, and the subtraction cancels the platform speed the body had inherited. Gameplay
    // code (player input, PlatformerPlayerExtension) is expected to overwrite velocity.x each
    // frame, so a controlled character never exhibits the drift.
    [UnityTest]
    public IEnumerator KinematicBody_LandingOnMovingPlatform_KeepsRelativeVelocity_WithoutFriction()
    {
        // Placed far from the other tests' geometry to avoid any cross-contact.
        Vector2 platformStart = new Vector2(2000, 0);
        KinematicMotion2D platformMotion = SpawnBody(platformStart);
        platformMotion.transform.localScale = new Vector3(10, 1, 1);
        platformMotion.useGravity = false;
        platformMotion.velocity = new Vector2(1, 0);

        // Dropped straight down: the rider never matches the platform's horizontal speed.
        KinematicMotion2D riderMotion = SpawnBody(new Vector2(platformStart.x, platformStart.y + 3));

        yield return new WaitForSeconds(1.5f);

        Assert.IsTrue(riderMotion.isGrounded, "The rider should be grounded after landing on the moving platform.");
        Assert.AreEqual(1f, riderMotion.groundVelocity.x, 0.1f, "groundVelocity should report the platform's velocity while standing on it.");

        float riderXBefore = riderMotion.position.x;
        float platformXBefore = platformMotion.position.x;

        yield return new WaitForSeconds(1f);

        Assert.Greater(platformMotion.position.x - platformXBefore, 0.5f, "Precondition: the platform itself should have moved.");
        Assert.IsTrue(riderMotion.isGrounded, "The rider should remain grounded even while sliding backwards relative to the platform.");

        // The two cancel exactly, so the rider holds station in world space.
        Assert.AreEqual(riderXBefore, riderMotion.position.x, 0.1f, "Without friction the rider should hold station in world space while the platform slides underneath it.");
        Assert.AreEqual(-1f, riderMotion.velocity.x, 0.1f, "The rider's velocity should stay at the negative of the platform's velocity: its unchanged ground-relative drift.");
    }

    // ------------------------------------------------------------------
    // Moving platform edge cases: descending faster than free fall, and rising very fast
    // ------------------------------------------------------------------

    // Far faster than a body reaches under gravity over these short test windows: in 0.3s a body
    // starting from rest free-falls only about 0.44 units, while these platforms travel 9.
    const float ExtremePlatformSpeed = 30f;
    const float EdgeCaseWindow = 0.3f;

    // Both edge-case families need a platform with nothing underneath it, since the platform (and
    // possibly the rider) travels several units vertically. Staged far from the scene's Ground.
    private KinematicMotion2D SpawnFreeStandingPlatform(Vector2 position, bool sticky, float width = 6f)
    {
        KinematicMotion2D platform = SpawnBody(position);
        platform.transform.localScale = new Vector3(width, 1, 1);
        platform.useGravity = false;
        platform.sticky = sticky;
        platform.velocity = Vector2.zero;
        return platform;
    }

    // The prefab's box is 1x1 and the platform is only scaled horizontally, so both bodies have a
    // half-height of 0.5. Deriving the surfaces from rigidbody positions rather than Collider2D
    // bounds keeps these checks independent of when Unity last synced transforms.
    private static float PlatformTopY(KinematicMotion2D platform) => platform.position.y + 0.5f;
    private static float RiderBottomY(KinematicMotion2D rider) => rider.position.y - 0.5f;

    [UnityTest]
    public IEnumerator StickyPlatform_DescendingFasterThanFreeFall_DragsRiderDownWithIt()
    {
        KinematicMotion2D platform = SpawnFreeStandingPlatform(new Vector2(3000, 0), sticky: true);
        KinematicMotion2D rider = SpawnBody(new Vector2(3000, 2));

        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(rider.isGrounded, "Precondition: the rider should have landed on the platform.");

        float platformYBefore = platform.position.y;
        float riderYBefore = rider.position.y;
        platform.velocity = new Vector2(0, -ExtremePlatformSpeed);

        yield return new WaitForSeconds(EdgeCaseWindow);

        float platformDrop = platformYBefore - platform.position.y;
        float riderDrop = riderYBefore - rider.position.y;

        Assert.Greater(platformDrop, 5f, "Precondition: the platform should have descended a long way.");
        Assert.AreEqual(platformDrop, riderDrop, 0.2f, $"A sticky platform should drag its rider down even far faster than free fall (platform {platformDrop:F2}, rider {riderDrop:F2}).");
        Assert.IsTrue(rider.isGrounded, "The rider should stay grounded while stuck to a sticky platform.");
    }

    [UnityTest]
    public IEnumerator NonStickyPlatform_DescendingFasterThanFreeFall_LeavesRiderBehind()
    {
        KinematicMotion2D platform = SpawnFreeStandingPlatform(new Vector2(3100, 0), sticky: false);
        KinematicMotion2D rider = SpawnBody(new Vector2(3100, 2));

        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(rider.isGrounded, "Precondition: the rider should have landed on the platform.");

        float platformYBefore = platform.position.y;
        float riderYBefore = rider.position.y;
        platform.velocity = new Vector2(0, -ExtremePlatformSpeed);

        yield return new WaitForSeconds(EdgeCaseWindow);

        float platformDrop = platformYBefore - platform.position.y;
        float riderDrop = riderYBefore - rider.position.y;

        Assert.Greater(platformDrop, 5f, "Precondition: the platform should have descended a long way.");
        Assert.Less(riderDrop, 1.5f, $"A non-sticky platform should drop away from its rider, leaving it to fall under gravity alone (it fell {riderDrop:F2} in {EdgeCaseWindow}s; free fall is about 0.44).");
        Assert.IsFalse(rider.isGrounded, "The rider should no longer be grounded once the platform has dropped out from under it.");
    }

    // The rider is pushed up ahead of the platform by GroundWillMove -> MoveBy -> Slide, which is
    // collision-checked. This verifies the rider is never left inside or below the deck, sampled
    // every physics step rather than only at the end, so a single-frame clip cannot slip through.
    [UnityTest]
    public IEnumerator Platform_RisingVeryFast_NeverClipsThroughRider()
    {
        KinematicMotion2D platform = SpawnFreeStandingPlatform(new Vector2(3200, 0), sticky: true);
        KinematicMotion2D rider = SpawnBody(new Vector2(3200, 2));

        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(rider.isGrounded, "Precondition: the rider should have landed on the platform.");

        float platformYBefore = platform.position.y;
        float riderYBefore = rider.position.y;
        platform.velocity = new Vector2(0, ExtremePlatformSpeed);

        int steps = Mathf.CeilToInt(EdgeCaseWindow / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            Assert.GreaterOrEqual(RiderBottomY(rider), PlatformTopY(platform) - 0.05f,
                $"On step {i} the rider sank into or fell through the rising platform (rider bottom {RiderBottomY(rider):F3}, platform top {PlatformTopY(platform):F3}).");
        }

        float platformRise = platform.position.y - platformYBefore;
        float riderRise = rider.position.y - riderYBefore;

        Assert.Greater(platformRise, 5f, "Precondition: the platform should have risen a long way.");
        Assert.AreEqual(platformRise, riderRise, 0.2f, $"The rider should be carried up with the platform rather than being passed by it (platform {platformRise:F2}, rider {riderRise:F2}).");
    }

    // Same check at a speed high enough to cross the rider's whole collider in a single physics
    // step (200 units/s is 4 units per 0.02s step against a 1-unit-tall body), which is where naive
    // position updates tunnel straight through.
    [UnityTest]
    public IEnumerator Platform_RisingFastEnoughToTunnel_StillCarriesRiderOnTop()
    {
        KinematicMotion2D platform = SpawnFreeStandingPlatform(new Vector2(3300, 0), sticky: true);
        KinematicMotion2D rider = SpawnBody(new Vector2(3300, 2));

        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(rider.isGrounded, "Precondition: the rider should have landed on the platform.");

        platform.velocity = new Vector2(0, 200f);

        for (int i = 0; i < 10; i++)
        {
            yield return new WaitForFixedUpdate();
            Assert.GreaterOrEqual(RiderBottomY(rider), PlatformTopY(platform) - 0.05f,
                $"On step {i} the platform tunnelled past the rider (rider bottom {RiderBottomY(rider):F3}, platform top {PlatformTopY(platform):F3}).");
        }
    }

    // Isolates the question the follow-test above cannot answer on its own: does the platform
    // itself actually move? groundVelocity reads back groundMotion.velocity, and velocity is only
    // recomputed from real displacement inside FixedUpdate - so a platform that never moves keeps
    // the exact value the test assigned it, and asserting groundVelocity.x == 1 proves nothing.
    [UnityTest]
    public IEnumerator MovingKinematicPlatform_ActuallyChangesItsOwnPosition()
    {
        Vector2 platformStart = new Vector2(1000, 0);
        KinematicMotion2D platformMotion = SpawnBody(platformStart);
        platformMotion.transform.localScale = new Vector3(10, 1, 1);
        platformMotion.useGravity = false;
        platformMotion.velocity = new Vector2(1, 0);

        // No rider at all: nothing that could block, push, or otherwise perturb the platform.
        yield return new WaitForSeconds(1f);

        Assert.Greater(platformMotion.position.x, platformStart.x + 0.5f,
            $"A gravity-free kinematic body with velocity (1,0) should travel ~1 unit in 1 second, but it moved from {platformStart.x} to {platformMotion.position.x} (velocity is now {platformMotion.velocity}).");
    }

    // ------------------------------------------------------------------
    // A body riding a moving platform collides with static geometry. These are all staged far
    // apart on the x axis (see the base offsets on each test) so none of the geometry crosses
    // over into another test.
    // ------------------------------------------------------------------

    // The wall's bottom face sits comfortably above the platform's own box, so the platform never
    // touches it and keeps travelling normally underneath - only the taller rider standing on top
    // can reach it. This isolates "the body atop the platform collides with static geometry" from
    // "the platform itself collides with static geometry" (already covered elsewhere).
    private GameObject CreateElevatedWall(float platformTopY, float x, float height = 4f)
    {
        float wallBottom = platformTopY + 0.5f;
        Vector2 size = new Vector2(1f, height);
        Vector2 center = new Vector2(x, wallBottom + height * 0.5f);
        return CreateStaticFloor(center, size);
    }

    [UnityTest]
    public IEnumerator KinematicBody_OnHorizontalMovingPlatform_HitsStaticWall_DoesNotClipOrTunnel()
    {
        Vector2 platformStart = new Vector2(4000, 0);
        KinematicMotion2D platform = SpawnFreeStandingPlatform(platformStart, sticky: true, width: 20f);
        KinematicMotion2D rider = SpawnBody(new Vector2(platformStart.x, platformStart.y + 3));

        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(rider.isGrounded, "Precondition: the rider should have landed on the platform.");

        float platformTopY = platform.position.y + 0.5f;
        float wallX = rider.position.x + 3f;
        CreateElevatedWall(platformTopY, wallX);
        float wallLeftFace = wallX - 0.5f;

        platform.velocity = new Vector2(2f, 0);

        int steps = Mathf.CeilToInt(3f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            float riderRight = rider.position.x + 0.5f;
            Assert.LessOrEqual(riderRight, wallLeftFace + 0.05f,
                $"On step {i} the rider clipped or tunnelled through the wall (rider right {riderRight:F3}, wall left {wallLeftFace:F3}).");
        }

        Assert.Greater(platform.position.x, platformStart.x + 3f,
            "Precondition: the platform itself should have kept moving, unimpeded, underneath the rider.");
        Assert.Less(rider.position.x, wallLeftFace,
            "The rider should have come to rest against the wall rather than passing through it.");
    }

    // ------------------------------------------------------------------
    // The rider gets carried by an unimpeded platform into a wall, then a second, independently
    // moving object closes in from the other side and crushes it against that wall while it is
    // still riding the platform. Three kinds of "other object" are tested, and they deliberately
    // assert DIFFERENT things, because who is capable of stopping the crusher differs in each:
    //
    //   KinematicMotion2D      - stops itself (its own Slide casts ahead)      => no clipping
    //   dynamic Rigidbody2D    - stopped by Unity's solver against a kinematic => no clipping
    //   generic kinematic RB2D - stopped by nothing at all                     => reports a crush
    //
    // The third is not a weaker test, it is a different one: an overlap that provably cannot be
    // prevented is the exact condition CrushedBy exists to report.
    // ------------------------------------------------------------------

    private class ConstantVelocityKinematicMover : MonoBehaviour
    {
        public Vector2 velocity;
        private Rigidbody2D rb;

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
        }

        void FixedUpdate()
        {
            rb.MovePosition(rb.position + velocity * Time.fixedDeltaTime);
        }
    }

    // A plain kinematic Rigidbody2D with no KinematicMotion2D component at all - nothing on this
    // object performs collision-aware movement, and Unity's physics engine does not resolve
    // kinematic-vs-kinematic overlaps on its own the way it does for dynamic-vs-kinematic pairs.
    private GameObject CreateGenericKinematicMover(Vector2 position, Vector2 velocity)
    {
        GameObject go = new GameObject("GenericKinematicMover");
        go.transform.position = position;
        BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
        collider.size = Vector2.one;
        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        ConstantVelocityKinematicMover mover = go.AddComponent<ConstantVelocityKinematicMover>();
        mover.velocity = velocity;
        spawned.Add(go);
        return go;
    }

    private class PinnedRiderScenario
    {
        public KinematicMotion2D platform;
        public KinematicMotion2D rider;
        public float wallLeftFace;
    }

    // Shared setup for all three "crushed against a wall by another object" tests: drops a rider
    // onto an unimpeded platform, sets it moving towards an elevated wall, and waits for the rider
    // to be pinned there before the caller introduces the crushing object. Iterator methods cannot
    // have out/ref parameters, so results are written onto the passed-in scenario instead.
    private IEnumerator SetUpRiderPinnedAgainstWall(float baseX, PinnedRiderScenario scenario)
    {
        Vector2 platformStart = new Vector2(baseX, 0);
        KinematicMotion2D platform = SpawnFreeStandingPlatform(platformStart, sticky: true, width: 20f);
        KinematicMotion2D rider = SpawnBody(new Vector2(platformStart.x, platformStart.y + 3));
        scenario.platform = platform;
        scenario.rider = rider;

        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(rider.isGrounded, "Precondition: the rider should have landed on the platform.");

        float platformTopY = platform.position.y + 0.5f;
        float wallX = rider.position.x + 2f;
        CreateElevatedWall(platformTopY, wallX);
        scenario.wallLeftFace = wallX - 0.5f;

        platform.velocity = new Vector2(2f, 0);

        yield return new WaitForSeconds(2f);
        Assert.Less(rider.position.x, scenario.wallLeftFace, "Precondition: the rider should be pinned against the wall before the crushing object arrives.");
        Assert.Greater(platform.position.x, platformStart.x + 3f, "Precondition: the platform should still be moving, unimpeded, underneath the rider.");
    }

    [UnityTest]
    public IEnumerator KinematicBody_OnUnimpededPlatform_CrushedAgainstWallByAnotherKinematicMotion2D_DoesNotClipOrTunnel()
    {
        PinnedRiderScenario scenario = new PinnedRiderScenario();
        yield return SetUpRiderPinnedAgainstWall(4100, scenario);
        KinematicMotion2D rider = scenario.rider;
        float wallLeftFace = scenario.wallLeftFace;

        // Needs to be pushable so the incoming mover (default pushable=false) is actually allowed
        // to push it - see CanPush's pushable-mismatch branch, which ignores pushPriority entirely
        // once the two pushable flags differ.
        rider.pushable = true;

        KinematicMotion2D mover = SpawnBody(new Vector2(rider.position.x - 4f, rider.position.y));
        mover.useGravity = false;
        mover.velocity = new Vector2(2f, 0);

        int steps = Mathf.CeilToInt(2f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            float riderRight = rider.position.x + 0.5f;
            float riderLeft = rider.position.x - 0.5f;
            float moverRight = mover.position.x + 0.5f;

            Assert.LessOrEqual(riderRight, wallLeftFace + 0.05f,
                $"On step {i} the crushed rider was pushed through the wall (rider right {riderRight:F3}, wall left {wallLeftFace:F3}).");
            Assert.LessOrEqual(moverRight, riderLeft + 0.05f,
                $"On step {i} the incoming KinematicMotion2D mover clipped through the rider (mover right {moverRight:F3}, rider left {riderLeft:F3}).");
        }
    }

    [UnityTest]
    public IEnumerator KinematicBody_OnUnimpededPlatform_CrushedAgainstWallByDynamicRigidbody_DoesNotClipOrTunnel()
    {
        PinnedRiderScenario scenario = new PinnedRiderScenario();
        yield return SetUpRiderPinnedAgainstWall(4200, scenario);
        KinematicMotion2D rider = scenario.rider;
        float wallLeftFace = scenario.wallLeftFace;

        GameObject moverObject = Spawn(RigidbodyPrefabName, new Vector2(rider.position.x - 4f, rider.position.y));
        Rigidbody2D moverRb = moverObject.GetComponent<Rigidbody2D>();
        moverRb.gravityScale = 0f;
        moverRb.constraints = RigidbodyConstraints2D.FreezeRotation;
        moverRb.linearVelocity = new Vector2(2f, 0);

        int steps = Mathf.CeilToInt(2f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            float riderRight = rider.position.x + 0.5f;
            float riderLeft = rider.position.x - 0.5f;
            float moverRight = moverRb.position.x + 0.5f;

            Assert.LessOrEqual(riderRight, wallLeftFace + 0.05f,
                $"On step {i} the crushed rider was pushed through the wall (rider right {riderRight:F3}, wall left {wallLeftFace:F3}).");
            Assert.LessOrEqual(moverRight, riderLeft + 0.05f,
                $"On step {i} the incoming dynamic Rigidbody2D clipped through the rider (mover right {moverRight:F3}, rider left {riderLeft:F3}).");
        }
    }

    // The third crusher type behaves fundamentally differently from the other two, and this test
    // asserts something different as a result.
    //
    // A plain kinematic Rigidbody2D driven by MovePosition is stopped by NOTHING: Unity's solver
    // does not stop kinematic bodies for collisions, and it carries no PuzzleBox code that could
    // stop it either. The rider is a KinematicMotion2D, which can only move ITSELF - it has no
    // authority over a foreign body that never consults it. So unlike the KinematicMotion2D case
    // (whose Slide casts and stops) and the dynamic Rigidbody2D case (which Unity's solver stops),
    // the clip here is unavoidable by construction.
    //
    // That is exactly the definition of a crush: a collision with a moving object that cannot be
    // resolved without clipping. The correct behaviour is therefore not to prevent the overlap -
    // it is to REPORT it. Do not "fix" this test back into a non-clipping assertion.
    [UnityTest]
    public IEnumerator KinematicBody_OnUnimpededPlatform_CrushedAgainstWallByGenericKinematicRigidbody_ReportsCrush()
    {
        PinnedRiderScenario scenario = new PinnedRiderScenario();
        yield return SetUpRiderPinnedAgainstWall(4300, scenario);
        KinematicMotion2D rider = scenario.rider;
        float wallLeftFace = scenario.wallLeftFace;

        CrushedByRecorder recorder = rider.gameObject.AddComponent<CrushedByRecorder>();
        GameObject mover = CreateGenericKinematicMover(new Vector2(rider.position.x - 4f, rider.position.y), new Vector2(2f, 0));

        int steps = Mathf.CeilToInt(2f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();

            // The rider is still under KinematicMotion2D's control, so this half of the guarantee
            // does hold: it must never be shoved through the wall behind it.
            float riderRight = rider.position.x + 0.5f;
            Assert.LessOrEqual(riderRight, wallLeftFace + 0.05f,
                $"On step {i} the crushed rider was pushed through the wall (rider right {riderRight:F3}, wall left {wallLeftFace:F3}).");
        }

        Assert.Greater(recorder.count, 0,
            "The rider is overlapped by a moving body with a wall behind it and no escape direction, so the crush must be reported even though the overlap itself cannot be prevented.");
        Assert.AreEqual(mover, recorder.lastCrusher,
            "The reported crusher should be the generic kinematic Rigidbody2D that drove into the rider.");
    }

    // ------------------------------------------------------------------
    // A platform rising against gravity crushes its rider against a static obstacle above.
    // ------------------------------------------------------------------

    // Records KinematicMotion2D's OnCrushedBy message. The payload is a single Contact, matching
    // OnContactEnter/Stay/Exit - the crusher is identified by contact.collider/contact.rigidbody
    // rather than by a KinematicMotion2D reference, since a crusher may be a plain kinematic or
    // dynamic Rigidbody2D with no KinematicMotion2D on it at all.
    //
    // CrushedBy is edge-triggered: it fires once when the crush begins, not every frame it lasts.
    private class CrushedByRecorder : MonoBehaviour
    {
        public int count;
        public GameObject lastCrusher;

        void OnCrushedBy(KinematicMotion2D.Contact contact)
        {
            count++;
            lastCrusher = contact.collider != null ? contact.collider.gameObject : null;
        }
    }

    [UnityTest]
    public IEnumerator KinematicBody_OnRisingPlatform_CrushedAgainstCeiling_FiresCrushedBy()
    {
        Vector2 platformStart = new Vector2(4400, 0);
        KinematicMotion2D platform = SpawnFreeStandingPlatform(platformStart, sticky: true, width: 6f);
        KinematicMotion2D rider = SpawnBody(new Vector2(platformStart.x, platformStart.y + 3));
        CrushedByRecorder recorder = rider.gameObject.AddComponent<CrushedByRecorder>();

        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(rider.isGrounded, "Precondition: the rider should have landed on the platform.");

        // A ceiling positioned so the rider's head reaches it after rising a couple of units.
        float platformTopY = platform.position.y + 0.5f;
        float ceilingBottomY = platformTopY + 3f;
        CreateStaticFloor(new Vector2(platformStart.x, ceilingBottomY + 2f), new Vector2(4f, 4f));

        platform.velocity = new Vector2(0, 2f);

        int steps = Mathf.CeilToInt(4f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            float riderTop = rider.position.y + 0.5f;
            Assert.LessOrEqual(riderTop, ceilingBottomY + 0.05f,
                $"On step {i} the rider was pushed through the ceiling instead of being stopped by it (rider top {riderTop:F3}, ceiling bottom {ceilingBottomY:F3}).");
        }

        Assert.Greater(recorder.count, 0,
            "CrushedBy should fire on the rider once it is pinned between the platform and the ceiling.");

        // The platform is what cannot be escaped from: the rider rests against the ceiling with a
        // margin gap, while the platform drives further into it every step, so the platform is the
        // deepest unresolved overlap.
        Assert.AreEqual(platform.gameObject, recorder.lastCrusher,
            "The reported crusher should be the platform driving the rider into the ceiling.");

        // Edge-triggered: one crush, one notification, however long it lasts.
        int countAfterFirstReport = recorder.count;
        yield return new WaitForSeconds(0.5f);
        Assert.AreEqual(countAfterFirstReport, recorder.count,
            "CrushedBy should fire once when the crush begins, not repeatedly while it persists.");
    }

    // The main risk with a confinement-based crush detector is false positives: ordinary resting
    // contact is, geometrically, "touching something and not moving". It must never be reported.
    [UnityTest]
    public IEnumerator KinematicBody_RestingOnGround_NeverReportsCrush()
    {
        float groundTop = GetTopSurfaceY(FindGround());

        KinematicMotion2D motion = SpawnBody(new Vector2(0, groundTop + 3));
        CrushedByRecorder recorder = motion.gameObject.AddComponent<CrushedByRecorder>();

        yield return new WaitForSeconds(2f);

        Assert.IsTrue(motion.isGrounded, "Precondition: the body should have landed and be resting.");
        Assert.AreEqual(0, recorder.count, "Ordinary resting contact must never be reported as a crush.");
    }

    // A body placed inside geometry it cannot escape is geometrically indistinguishable from a
    // crushed one: both are overlapped with no free direction. They are told apart by history -
    // a crush is a transition out of a resolved state, a bad placement never had one. This must
    // be reported to the developer as a placement error, not to gameplay as a crush.
    [UnityTest]
    public IEnumerator KinematicBody_SpawnedWithNoEscape_WarnsAndDoesNotReportCrush()
    {
        // A pocket 0.9 tall for a 1x1 body: separating up overlaps the ceiling, separating down
        // overlaps the floor, so no escape direction exists and Separate cannot resolve it.
        Vector2 pocketCenter = new Vector2(4800, 0);
        CreateStaticFloor(pocketCenter + new Vector2(0, -0.95f), new Vector2(4f, 1f));
        CreateStaticFloor(pocketCenter + new Vector2(0, 0.95f), new Vector2(4f, 1f));

        // Coupled to the message built in KinematicMotion2D.HandleUnresolvedOverlaps. Match only
        // the stable core of the sentence: this assertion was originally written against the
        // Japanese text and was silently invalidated by the English translation pass, so the
        // surrounding detail (object name, penetration depth, position) is deliberately excluded
        // from the pattern.
        LogAssert.Expect(LogType.Warning, new Regex("is placed where overlap cannot be resolved"));

        KinematicMotion2D motion = SpawnBody(pocketCenter);
        CrushedByRecorder recorder = motion.gameObject.AddComponent<CrushedByRecorder>();

        yield return new WaitForSeconds(1f);

        Assert.AreEqual(0, recorder.count,
            "A body that was never in a resolved state was not crushed by anything - it was placed badly, and should be reported as a placement error instead.");
    }

    // Deferred by agreement: this change reports the crush but does not act on it. Making the
    // platform stop requires Cast to stop unconditionally skipping its own riders, which touches
    // the whole carry path, so it is deliberately a separate piece of work.
    [UnityTest]
    [Ignore("Not implemented yet: a crush is currently reported but does not stop the crusher.")]
    public IEnumerator KinematicBody_OnRisingPlatform_CrushedAgainstCeiling_PlatformStops()
    {
        Vector2 platformStart = new Vector2(4450, 0);
        KinematicMotion2D platform = SpawnFreeStandingPlatform(platformStart, sticky: true, width: 6f);
        KinematicMotion2D rider = SpawnBody(new Vector2(platformStart.x, platformStart.y + 3));

        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(rider.isGrounded, "Precondition: the rider should have landed on the platform.");

        float platformTopY = platform.position.y + 0.5f;
        float ceilingBottomY = platformTopY + 3f;
        CreateStaticFloor(new Vector2(platformStart.x, ceilingBottomY + 2f), new Vector2(4f, 4f));

        platform.velocity = new Vector2(0, 2f);
        yield return new WaitForSeconds(4f);

        float platformTopAfterCrush = platform.position.y + 0.5f;
        float riderBottomAfterCrush = rider.position.y - 0.5f;
        Assert.LessOrEqual(platformTopAfterCrush, riderBottomAfterCrush + 0.05f,
            $"The platform should not rise up into the rider it is crushing against the ceiling (platform top {platformTopAfterCrush:F3}, rider bottom {riderBottomAfterCrush:F3}).");

        float platformYCheckpoint1 = platform.position.y;
        yield return new WaitForSeconds(1f);
        float platformYCheckpoint2 = platform.position.y;
        Assert.Less(platformYCheckpoint2 - platformYCheckpoint1, 0.5f,
            "The platform should stop rising once the object it is carrying is crushed against an obstacle above, rather than continuing to climb steadily.");
    }

    // ------------------------------------------------------------------
    // A tilted moving platform pushes the object it carries into an obstacle standing up from the
    // incline. Terminology, as used throughout this section:
    //   object   - the KinematicMotion2D under test
    //   platform - the KinematicMotion2D the object rests on
    //   obstacle - static geometry that impedes the object (but not the platform)
    //
    // The specification: while the obstacle impedes the object, the object must be pushed in a
    // direction ORTHOGONAL TO THE PLATFORM NORMAL. Read in the platform's frame - the object
    // slides along the platform surface and stays on it. (The world-frame reading is incoherent:
    // a horizontally translating incline recedes from any world-static point at |v . n|, so an
    // object whose world motion were tangential would immediately leave the surface.)
    //
    // That gives one crisp invariant, asserted every physics step below: the object's
    // perpendicular offset from the platform surface never changes.
    // ------------------------------------------------------------------

    const float SlopeAngleDegrees = 30f;

    // Deliberately slow. Engagement with a *static* obstacle on a translating incline is
    // inherently transient (see the staging note in BuildSlopeScenario), and both the time to
    // reach the obstacle and the time until it floats clear scale as 1/speed - so a slower
    // platform buys a longer absolute window to assert in, without changing the geometry.
    const float SlopePlatformSpeed = 1f;

    private class SlopeScenario
    {
        public KinematicMotion2D platform;
        public KinematicMotion2D rider;
        public Vector2 normal;
        public Vector2 upSlope;

        // Along-slope travel available before the object's leading CORNER meets the obstacle's
        // face. Only a rough "was it carried at all" yardstick, deliberately not a clipping
        // bound: the object also sinks relative to the static obstacle as the deck translates, so
        // contact migrates from (object corner vs obstacle face) to (obstacle corner vs the
        // object's top face), and the object legitimately advances past this figure while doing
        // so. Whether it actually clipped is measured from the colliders instead.
        public float freeGapAlongSlope;

        // Dot(rider.position - platform.position, normal) while resting: the invariant above.
        public float restingSurfaceOffset;

        // Largest along-slope travel observed during the run.
        public float maxTravelled;

        // Worst (most negative) collider separation observed. Negative means overlap.
        public float worstSeparation;

        public Collider2D riderCollider;
        public Collider2D obstacleCollider;
    }

    // Builds a slanted moving platform with the object resting directly on its incline (dropped
    // and settled, the same way the existing static-slope test does, rather than guessing an exact
    // resting position for an unrotated box balanced on a rotated one), plus a static obstacle
    // further up-slope - rotated to match the incline, like a curb standing up from the surface.
    //
    // STAGING NOTE - do not widen obstacleDistance without redoing this arithmetic. With a static
    // obstacle and a translating incline, the obstacle's clearance above the surface grows at
    // |v . n| while the object closes on it at |v . upSlope|. At 30 degrees that is 0.5v versus
    // 0.866v against an available overlap of (object height above surface 1.366 - obstacle
    // clearance 0.5) = 0.866, so contact-before-disengage requires an along-slope FREE GAP of
    // under 1.5 units - a speed-independent, purely geometric limit. The previous staging used a
    // 2.0 gap, where the object legitimately passes underneath the obstacle after ~0.58s and the
    // "blocked" assertion is unsatisfiable no matter how correct the carry is.
    private IEnumerator BuildSlopeScenario(float baseX, SlopeScenario scenario, float obstacleDistance = 1.5f)
    {
        Vector2 normal = Quaternion.Euler(0, 0, SlopeAngleDegrees) * Vector2.up;
        Vector2 upSlope = new Vector2(normal.y, -normal.x); // matches KinematicMotion2D.groundRight for this normal
        scenario.normal = normal;
        scenario.upSlope = upSlope;

        Vector2 platformCenter = new Vector2(baseX, 0);
        KinematicMotion2D platform = SpawnBody(platformCenter);
        platform.transform.localScale = new Vector3(30f, 1f, 1f);
        platform.transform.rotation = Quaternion.Euler(0, 0, SlopeAngleDegrees);
        platform.useGravity = false;
        platform.velocity = Vector2.zero;
        scenario.platform = platform;

        Vector2 dropPoint = platformCenter + normal * 4f;
        KinematicMotion2D rider = SpawnBody(dropPoint);
        scenario.rider = rider;

        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(rider.isGrounded, "Precondition: the object should have landed on the slanted platform.");

        // Stop whatever small frictionless drift it picked up while settling (see the documented
        // frictionless-landing behaviour above), so the obstacle can be placed a known distance
        // up-slope from a stable starting point.
        rider.velocity = Vector2.zero;
        yield return new WaitForFixedUpdate();

        Vector2 platformSurfacePoint = platform.position + normal * 0.5f;
        float riderAlongSlope = Vector2.Dot(rider.position - platformSurfacePoint, upSlope);

        // Bottom clear of the platform's own box, so the obstacle impedes the object but not the
        // platform; tall enough to catch the object standing on the surface.
        const float obstacleHalfThickness = 0.3f;
        Vector2 obstacleCenter = platformSurfacePoint + upSlope * (riderAlongSlope + obstacleDistance) + normal * 2.5f;
        GameObject obstacle = CreateStaticFloor(obstacleCenter, new Vector2(obstacleHalfThickness * 2f, 4f), SlopeAngleDegrees);

        scenario.riderCollider = rider.GetComponent<Collider2D>();
        scenario.obstacleCollider = obstacle.GetComponent<Collider2D>();

        // Support of the object's unrotated 1x1 box along the slope direction, so the free gap is
        // derived rather than hardcoded and stays correct if the angle or sizes change.
        float riderHalfExtentAlongSlope = 0.5f * (Mathf.Abs(upSlope.x) + Mathf.Abs(upSlope.y));
        scenario.freeGapAlongSlope = obstacleDistance - riderHalfExtentAlongSlope - obstacleHalfThickness;
        scenario.restingSurfaceOffset = Vector2.Dot(rider.position - platform.position, normal);

        Assert.Greater(scenario.freeGapAlongSlope, 0f, "Precondition: the obstacle should start clear of the object.");
    }

    // Drives the platform and asserts the specification every physics step.
    private IEnumerator RunSlopeScenario(SlopeScenario scenario, Vector2 platformVelocity, float seconds)
    {
        Vector2 riderStart = scenario.rider.position;
        scenario.platform.velocity = platformVelocity;
        scenario.maxTravelled = 0f;
        scenario.worstSeparation = float.MaxValue;

        int steps = Mathf.CeilToInt(seconds / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();

            float travelled = Vector2.Dot(scenario.rider.position - riderStart, scenario.upSlope);
            scenario.maxTravelled = Mathf.Max(scenario.maxTravelled, travelled);

            float surfaceOffset = Vector2.Dot(scenario.rider.position - scenario.platform.position, scenario.normal);

            // The specification itself: measured in the platform's frame the object's motion is
            // purely tangential, so its perpendicular offset from the surface is conserved.
            Assert.AreEqual(scenario.restingSurfaceOffset, surfaceOffset, 0.05f,
                $"On step {i} the object left the platform surface instead of being pushed orthogonal to the platform normal (offset {surfaceOffset:F3}, expected {scenario.restingSurfaceOffset:F3}).");

            Assert.IsTrue(scenario.rider.isGrounded,
                $"On step {i} the object stopped being grounded: it lost contact with the platform carrying it.");

            // Whether the object clipped the obstacle is measured straight from the colliders
            // rather than derived from an along-slope budget. Deriving it means modelling box
            // supports, contact corners and the margin gap by hand, which is exactly the sort of
            // arithmetic that produces a test failure when nothing is actually overlapping.
            // Collider2D.Distance is the ground truth (and is what Separate() itself uses):
            // negative means genuine penetration.
            ColliderDistance2D separation = scenario.riderCollider.Distance(scenario.obstacleCollider);
            scenario.worstSeparation = Mathf.Min(scenario.worstSeparation, separation.distance);

            // Tolerance is one margin: a body is allowed to rest right up against a surface, and
            // sub-margin jitter at the contact point is not a clip.
            Assert.Greater(separation.distance, -DocumentedDefaultMargin,
                $"On step {i} the object penetrated the obstacle by {-separation.distance:F4} (travelled {travelled:F3} along the slope).");
        }
    }

    [UnityTest]
    public IEnumerator KinematicBody_OnSlantedMovingPlatform_MotionAlongSlope_DoesNotClipObstacle()
    {
        SlopeScenario scenario = new SlopeScenario();
        yield return BuildSlopeScenario(4500, scenario);

        // Motion perpendicular to the ground normal: purely along the slope. This leaves the
        // platform's own plane invariant, so the obstacle stays engaged for the whole run.
        yield return RunSlopeScenario(scenario, scenario.upSlope * SlopePlatformSpeed, 2.5f);

        Assert.Greater(scenario.maxTravelled, scenario.freeGapAlongSlope - 0.1f,
            $"The object should have been carried up to the obstacle and stopped there, but it only travelled {scenario.maxTravelled:F3} of the {scenario.freeGapAlongSlope:F3} available.");
    }

    [UnityTest]
    public IEnumerator KinematicBody_OnSlantedMovingPlatform_HorizontalMotion_PushesRiderParallelToGround()
    {
        SlopeScenario scenario = new SlopeScenario();
        yield return BuildSlopeScenario(4600, scenario);

        // Pure world-horizontal motion, not aligned with the slope: the carry has to be split into
        // a tangential part (blocked by the obstacle) and a normal part (which must still apply,
        // otherwise the object cannot stay on a surface that is receding beneath it).
        // Window kept under the ~1.73s at which the obstacle floats clear of the object.
        yield return RunSlopeScenario(scenario, new Vector2(SlopePlatformSpeed, 0), 1.5f);

        Assert.Greater(scenario.maxTravelled, scenario.freeGapAlongSlope - 0.1f,
            $"The object should have been carried up to the obstacle and stopped there, but it only travelled {scenario.maxTravelled:F3} of the {scenario.freeGapAlongSlope:F3} available.");
    }

    [UnityTest]
    public IEnumerator KinematicBody_OnSlantedMovingPlatform_UpwardMotion_PushesRiderParallelToGround()
    {
        SlopeScenario scenario = new SlopeScenario();
        yield return BuildSlopeScenario(4700, scenario);

        float platformYBefore = scenario.platform.position.y;

        // Pure world-vertical motion. No travel lower bound here: the platform closes on the
        // obstacle's underside at 0.866v while the object closes on its face at only 0.5v, so the
        // platform is itself stopped by the obstacle (a ceiling normal, with no horizontal
        // component to redirect into) at ~0.58s - before the object necessarily reaches it. What
        // must hold regardless is that the object stays glued to the deck throughout.
        yield return RunSlopeScenario(scenario, new Vector2(0, SlopePlatformSpeed), 1.2f);

        Assert.Greater(scenario.platform.position.y, platformYBefore + 0.2f,
            "Precondition: the platform should have risen before the obstacle stopped it.");
    }

    // ------------------------------------------------------------------
    // Pushing: CanPush, pushable, pushPriority and mass.
    //
    // Pushing is a headline feature of this component and until now was only ever exercised
    // incidentally, by the crush tests. Nothing asserted that a push actually happens.
    //
    // Staged in the 10000 lane. (The 6000-7999 lanes belong to TestKinematicAttachment.)
    // ------------------------------------------------------------------

    // Re-applies a velocity every FixedUpdate, the way gameplay code driven by player input or an AI
    // does. This is REQUIRED to test pushing, and the reason is worth understanding before touching
    // any test in this section.
    //
    // FixedUpdate ends with `velocity = actualMotion / deltaSeconds`. When a body pushes something,
    // Slide deliberately keeps only `pushDelta * massRatio` of its remaining travel - so the pusher's
    // actual motion that frame is a fraction of what it intended, and that fraction is then written
    // back into `velocity` as if it were the body's new speed. Set a pusher's velocity once and it
    // therefore halves every frame against an equal-mass target: 0.03, 0.015, 0.0075... a geometric
    // series summing to about 0.06 units of push in total, after which it is stationary.
    //
    // That is not a bug being worked around here - a real character controller rewrites velocity.x
    // from input every frame, which is exactly what this driver models. It is, however, a sharp edge
    // worth knowing about, and PushedBody_WithoutContinuousDrive_StallsAlmostImmediately below pins
    // it explicitly.
    private class ConstantVelocityDriver : MonoBehaviour
    {
        public Vector2 velocity;
        private KinematicMotion2D motion;

        void Awake()
        {
            motion = GetComponent<KinematicMotion2D>();
        }

        void FixedUpdate()
        {
            motion.velocity = velocity;
        }
    }

    // A gravity-free body driven at a constant velocity, used as a pusher throughout this section.
    private KinematicMotion2D SpawnPusher(Vector2 position, Vector2 velocity)
    {
        KinematicMotion2D pusher = SpawnBody(position);
        pusher.useGravity = false;
        pusher.velocity = velocity;
        pusher.gameObject.AddComponent<ConstantVelocityDriver>().velocity = velocity;
        return pusher;
    }

    private KinematicMotion2D SpawnTarget(Vector2 position, bool pushable, int pushPriority = 0)
    {
        KinematicMotion2D target = SpawnBody(position);
        target.useGravity = false;
        target.velocity = Vector2.zero;
        target.pushable = pushable;
        target.pushPriority = pushPriority;
        return target;
    }

    // CanPush's pushable-mismatch branch: when the two pushable flags differ, the other body's
    // own flag decides, and pushPriority is not consulted at all.
    [UnityTest]
    public IEnumerator PushableBody_IsPushedByMovingKinematicBody()
    {
        KinematicMotion2D target = SpawnTarget(new Vector2(10002, 0), pushable: true);
        KinematicMotion2D pusher = SpawnPusher(new Vector2(10000, 0), new Vector2(1.5f, 0));

        float targetXBefore = target.position.x;
        yield return new WaitForSeconds(2.5f);

        Assert.Greater(target.position.x, targetXBefore + 0.5f,
            $"A pushable body should be pushed along by a moving kinematic body, but it only moved {target.position.x - targetXBefore:F3}.");
        Assert.Greater(pusher.position.x, 10000f + 0.5f, "The pusher should have made progress while pushing.");
        Assert.LessOrEqual(pusher.position.x + 0.5f, target.position.x - 0.5f + 0.05f,
            "The pusher should stay behind the body it is pushing, not overlap it.");
    }

    [UnityTest]
    public IEnumerator NonPushableBody_IsNotPushed_PusherStops()
    {
        KinematicMotion2D target = SpawnTarget(new Vector2(10102, 0), pushable: false);
        KinematicMotion2D pusher = SpawnPusher(new Vector2(10100, 0), new Vector2(1.5f, 0));

        float targetXBefore = target.position.x;
        yield return new WaitForSeconds(2.5f);

        Assert.AreEqual(targetXBefore, target.position.x, 0.05f, "A body that is not pushable must not be pushed.");
        Assert.LessOrEqual(pusher.position.x + 0.5f, target.position.x - 0.5f + 0.05f,
            "The pusher should have stopped at contact rather than passing through.");
    }

    // Two bodies with the same pushable flag fall through to the pushPriority comparison, which is
    // strictly greater-than - so a tie means neither one may push the other.
    [UnityTest]
    public IEnumerator EqualPushable_EqualPriority_BothStopAtContact()
    {
        KinematicMotion2D target = SpawnTarget(new Vector2(10202, 0), pushable: true, pushPriority: 0);
        KinematicMotion2D pusher = SpawnPusher(new Vector2(10200, 0), new Vector2(1.5f, 0));
        pusher.pushable = true;
        pusher.pushPriority = 0;

        float targetXBefore = target.position.x;
        yield return new WaitForSeconds(2.5f);

        Assert.AreEqual(targetXBefore, target.position.x, 0.05f,
            "With equal pushable flags and equal pushPriority, neither body may push the other.");
        Assert.LessOrEqual(pusher.position.x + 0.5f, target.position.x - 0.5f + 0.05f,
            "The pusher should stop at contact.");
    }

    [UnityTest]
    public IEnumerator HigherPushPriority_PushesEqualPushableBody()
    {
        KinematicMotion2D target = SpawnTarget(new Vector2(10302, 0), pushable: true, pushPriority: 0);
        KinematicMotion2D pusher = SpawnPusher(new Vector2(10300, 0), new Vector2(1.5f, 0));
        pusher.pushable = true;
        pusher.pushPriority = 1;

        float targetXBefore = target.position.x;
        yield return new WaitForSeconds(2.5f);

        Assert.Greater(target.position.x, targetXBefore + 0.5f,
            $"The higher-pushPriority body should push the lower one, but the target only moved {target.position.x - targetXBefore:F3}.");
    }

    // The mismatch branch ignores pushPriority entirely, even a wildly higher one on the target.
    [UnityTest]
    public IEnumerator MismatchedPushable_IgnoresPushPriority()
    {
        KinematicMotion2D target = SpawnTarget(new Vector2(10402, 0), pushable: true, pushPriority: 99);
        KinematicMotion2D pusher = SpawnPusher(new Vector2(10400, 0), new Vector2(1.5f, 0));
        pusher.pushable = false;
        pusher.pushPriority = 0;

        float targetXBefore = target.position.x;
        yield return new WaitForSeconds(2.5f);

        Assert.Greater(target.position.x, targetXBefore + 0.5f,
            "When the pushable flags differ, the other body's pushable flag decides and pushPriority is not consulted - " +
            $"so this target should be pushed despite its priority of 99 (it moved {target.position.x - targetXBefore:F3}).");
    }

    // CanPush early-outs when the other body is standing on this one: a platform carries its rider
    // through GroundWillMove and must never also push it. The observable consequence of getting this
    // wrong is on the PLATFORM - a push runs the mass-ratio arithmetic and eats into the distance the
    // platform still has to travel, so it would fall behind its own velocity.
    [UnityTest]
    public IEnumerator Ground_DoesNotPushItsOwnRider()
    {
        KinematicMotion2D platform = SpawnFreeStandingPlatform(new Vector2(10500, 0), sticky: true, width: 20f);
        KinematicMotion2D rider = SpawnBody(new Vector2(10500, 3));

        // Deliberately made maximally pushable, so only the ground early-out can prevent a push.
        rider.pushable = true;
        rider.pushPriority = 0;

        yield return new WaitForSeconds(1.5f);
        Assert.IsTrue(rider.isGrounded, "Precondition: the rider should have landed on the platform.");

        float platformXBefore = platform.position.x;
        platform.velocity = new Vector2(2f, 0);

        yield return new WaitForSeconds(1f);

        // groundVelocity reads back the platform's own velocity, which confirms the rider really is
        // registered as standing on THIS platform and not merely airborne next to it.
        Assert.AreEqual(2f, rider.groundVelocity.x, 0.1f,
            "Precondition: the rider should be standing on the moving platform.");
        Assert.AreEqual(2f, platform.position.x - platformXBefore, 0.15f,
            $"A platform must not push the rider standing on it - it should travel its full velocity unimpeded, " +
            $"but it moved {platform.position.x - platformXBefore:F3} of the expected 2.0.");
    }

    // The sharp edge described on ConstantVelocityDriver, pinned as behaviour in its own right.
    //
    // A pusher whose velocity is set once and never refreshed stalls almost immediately, because
    // each frame's reduced actual motion is written back into velocity as the body's new speed. This
    // matters to anyone driving a KinematicMotion2D by assigning velocity from outside rather than
    // every frame: pushing will appear to "not work" for reasons that have nothing to do with
    // pushable or pushPriority.
    //
    // If this test ever starts failing because the body keeps pushing, the velocity write-back has
    // been changed - check whether that was intended, and update ConstantVelocityDriver's note.
    [UnityTest]
    public IEnumerator PushedBody_WithoutContinuousDrive_StallsAlmostImmediately()
    {
        KinematicMotion2D target = SpawnTarget(new Vector2(11002, 0), pushable: true);

        // Deliberately NOT using SpawnPusher: no ConstantVelocityDriver, velocity set exactly once.
        KinematicMotion2D pusher = SpawnBody(new Vector2(11000, 0));
        pusher.useGravity = false;
        pusher.velocity = new Vector2(1.5f, 0);

        float targetXBefore = target.position.x;
        yield return new WaitForSeconds(2.5f);

        float travel = target.position.x - targetXBefore;
        Assert.Greater(travel, 0f, "Precondition: the pusher should have made contact and pushed at least once.");
        Assert.Less(travel, 0.25f,
            $"A pusher whose velocity is never refreshed should stall within a few frames (the reduced push motion is " +
            $"written back into velocity, halving it each frame), but the target travelled {travel:F3}.");
    }

    // Slide scales the pusher's remaining travel by mass / (mass + otherMass), so a heavier pusher
    // keeps more of its motion after a push. Asserts the direction of the effect rather than an exact
    // figure, which would over-fit the arithmetic.
    [UnityTest]
    public IEnumerator HeavierPusher_RetainsMoreMotionAfterPushing()
    {
        float lightTravel = 0f;
        float heavyTravel = 0f;

        foreach (bool heavy in new[] { false, true })
        {
            float baseX = heavy ? 10700 : 10600;

            KinematicMotion2D target = SpawnTarget(new Vector2(baseX + 2f, 0), pushable: true);
            target.mass = 5f;

            KinematicMotion2D pusher = SpawnPusher(new Vector2(baseX, 0), new Vector2(1.5f, 0));
            pusher.mass = heavy ? 20f : 1f;

            yield return new WaitForSeconds(2f);

            float travel = pusher.position.x - baseX;
            if (heavy) { heavyTravel = travel; } else { lightTravel = travel; }
        }

        Assert.Greater(heavyTravel, lightTravel + 0.05f,
            $"A heavier pusher should retain more of its motion after pushing (heavy {heavyTravel:F3}, light {lightTravel:F3}).");
    }

    // Dynamic bodies take a separate branch in Slide: the collider is cast to find a safe distance,
    // the position is written directly (MovePosition would be deferred to the next frame), and
    // transforms are synced immediately.
    [UnityTest]
    public IEnumerator PushingDynamicRigidbody_MovesItWithoutOverlap()
    {
        GameObject targetObject = Spawn(RigidbodyPrefabName, new Vector2(10802, 0));
        Rigidbody2D targetRb = targetObject.GetComponent<Rigidbody2D>();
        targetRb.gravityScale = 0f;
        targetRb.constraints = RigidbodyConstraints2D.FreezeRotation;
        targetRb.linearVelocity = Vector2.zero;

        KinematicMotion2D pusher = SpawnPusher(new Vector2(10800, 0), new Vector2(1.5f, 0));

        float targetXBefore = targetRb.position.x;

        int steps = Mathf.CeilToInt(2.5f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            Assert.LessOrEqual(pusher.position.x + 0.5f, targetRb.position.x - 0.5f + 0.05f,
                $"On step {i} the pusher overlapped the dynamic body it was pushing " +
                $"(pusher right {pusher.position.x + 0.5f:F3}, target left {targetRb.position.x - 0.5f:F3}).");
        }

        Assert.Greater(targetRb.position.x, targetXBefore + 0.5f,
            $"A dynamic Rigidbody2D should be pushed along by a kinematic body, but it only moved {targetRb.position.x - targetXBefore:F3}.");
    }

    // ProcessOverlaps takes a different branch from Slide: when the overlapping body has a LOWER
    // pushPriority, it is the one that gets separated out, leaving this body where it is.
    [UnityTest]
    public IEnumerator ProcessOverlaps_HigherPriorityBody_PushesTheOtherOut()
    {
        KinematicMotion2D low = SpawnBody(new Vector2(10900.4f, 0));
        low.useGravity = false;
        low.velocity = Vector2.zero;
        low.pushPriority = 0;

        KinematicMotion2D high = SpawnBody(new Vector2(10900, 0));
        high.useGravity = false;
        high.velocity = Vector2.zero;
        high.pushPriority = 5;

        float lowXBefore = low.position.x;
        float highXBefore = high.position.x;

        yield return new WaitForSeconds(1f);

        Assert.AreEqual(highXBefore, high.position.x, 0.05f,
            $"The higher-priority body should hold its position and separate the other one instead (it moved {high.position.x - highXBefore:F3}).");
        Assert.Greater(Mathf.Abs(low.position.x - lowXBefore), 0.1f,
            "The lower-priority body should have been pushed out of the overlap.");
    }

    // ------------------------------------------------------------------
    // Speed limits, collision mask and the gravity modifiers. Staged in the 11000 lane, high above
    // the scene's Ground so the falling tests have clear air.
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator MaxSpeedDown_ClampsFreeFall()
    {
        KinematicMotion2D motion = SpawnBody(new Vector2(11000, 50));
        motion.maxSpeedDown = 3f;

        // Unclamped, 2 seconds of free fall reaches about 19.6 units/s.
        yield return new WaitForSeconds(2f);

        Assert.GreaterOrEqual(motion.velocity.y, -3f - 0.2f,
            $"Downward speed should be clamped to maxSpeedDown, but velocity.y reached {motion.velocity.y:F3}.");
        Assert.Less(motion.velocity.y, -1f, "Precondition: the body should actually be falling.");
    }

    [UnityTest]
    public IEnumerator MaxSpeedUp_ClampsUpwardVelocity()
    {
        KinematicMotion2D motion = SpawnBody(new Vector2(11100, 50));
        motion.maxSpeedUp = 2f;
        motion.velocity = new Vector2(0, 50f);

        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        Assert.LessOrEqual(motion.velocity.y, 2f + 0.2f,
            $"Upward speed should be clamped to maxSpeedUp, but velocity.y reached {motion.velocity.y:F3}.");
    }

    [UnityTest]
    public IEnumerator MaxSpeedSide_ClampsHorizontalVelocity()
    {
        KinematicMotion2D motion = SpawnBody(new Vector2(11200, 50));
        motion.useGravity = false;
        motion.maxSpeedSide = 2f;
        motion.velocity = new Vector2(50f, 0);

        float xBefore = motion.position.x;
        yield return new WaitForSeconds(1f);

        Assert.LessOrEqual(Mathf.Abs(motion.velocity.x), 2f + 0.2f,
            $"Horizontal speed should be clamped to maxSpeedSide, but velocity.x reached {motion.velocity.x:F3}.");
        Assert.AreEqual(2f, motion.position.x - xBefore, 0.3f,
            $"A body clamped to 2 units/s should travel about 2 units in a second, but it travelled {motion.position.x - xBefore:F3}.");
    }

    // Layers 6-31 are unnamed and unused in this project, so a test can claim one without touching
    // ProjectSettings.
    const int UnusedTestLayer = 31;

    [UnityTest]
    public IEnumerator CollisionMask_ExcludedLayer_IsPassedThrough()
    {
        GameObject floor = CreateStaticFloor(new Vector2(11300, 0), new Vector2(6f, 1f));
        floor.layer = UnusedTestLayer;

        KinematicMotion2D motion = SpawnBody(new Vector2(11300, 3));
        motion.collisionMask = ~(1 << UnusedTestLayer);

        yield return new WaitForSeconds(1.5f);

        Assert.Less(motion.position.y, -1f,
            $"A body whose collisionMask excludes the floor's layer should fall straight through it, but it stopped at y={motion.position.y:F3}.");
        Assert.IsFalse(motion.isGrounded, "A body cannot be grounded on geometry its collisionMask excludes.");
    }

    // The control for the test above: same geometry, same layer, mask left alone. Without this, a
    // body that failed to fall for some unrelated reason would make the exclusion test pass vacuously.
    [UnityTest]
    public IEnumerator CollisionMask_IncludedLayer_StillCollides()
    {
        GameObject floor = CreateStaticFloor(new Vector2(11400, 0), new Vector2(6f, 1f));
        floor.layer = UnusedTestLayer;

        KinematicMotion2D motion = SpawnBody(new Vector2(11400, 3));
        motion.collisionMask = ~0;

        yield return new WaitForSeconds(1.5f);

        Assert.IsTrue(motion.isGrounded, "With the layer included in collisionMask, the body should land on the floor.");
        Assert.Greater(motion.position.y, 0f, "The body should be resting on top of the floor.");
    }

    [UnityTest]
    public IEnumerator GravityMultiplier_ScalesFallRate()
    {
        KinematicMotion2D normal = SpawnBody(new Vector2(11500, 50));
        KinematicMotion2D doubled = SpawnBody(new Vector2(11520, 50));
        doubled.gravityMultiplier = 2f;

        yield return new WaitForSeconds(0.5f);

        float normalDrop = 50f - normal.position.y;
        float doubledDrop = 50f - doubled.position.y;

        Assert.Greater(normalDrop, 0.1f, "Precondition: the reference body should be falling.");
        Assert.Greater(doubledDrop, normalDrop * 1.5f,
            $"gravityMultiplier=2 should make the body fall markedly faster (normal {normalDrop:F3}, doubled {doubledDrop:F3}).");
    }

    [UnityTest]
    public IEnumerator GravityModifier_ScalesFallRate()
    {
        KinematicMotion2D normal = SpawnBody(new Vector2(11600, 50));
        KinematicMotion2D doubled = SpawnBody(new Vector2(11620, 50));
        doubled.gravityModifier = 2f;

        yield return new WaitForSeconds(0.5f);

        float normalDrop = 50f - normal.position.y;
        float doubledDrop = 50f - doubled.position.y;

        Assert.Greater(normalDrop, 0.1f, "Precondition: the reference body should be falling.");
        Assert.Greater(doubledDrop, normalDrop * 1.5f,
            $"gravityModifier=2 should make the body fall markedly faster (normal {normalDrop:F3}, doubled {doubledDrop:F3}).");
    }

    // DOCUMENTATION DISCREPANCY. KinematicMotion2D.API.md lists "Reset gravityModifier = 1f" as step
    // 3 of the FixedUpdate order, and describes the field as a "per-frame multiplier, reset to 1 each
    // FixedUpdate". The implementation never resets it. This test pins what the code actually does,
    // so the doc can be corrected against a known-true statement rather than a guess.
    [UnityTest]
    public IEnumerator GravityModifier_IsNotResetEachFixedUpdate()
    {
        KinematicMotion2D motion = SpawnBody(new Vector2(11700, 50));
        motion.gravityModifier = 0f; // set ONCE

        yield return new WaitForSeconds(1f);

        Assert.AreEqual(0f, motion.gravityModifier, 0.0001f,
            "gravityModifier is not reset each FixedUpdate, despite what the API reference says.");
        Assert.AreEqual(50f, motion.position.y, 0.05f,
            $"With gravityModifier pinned at 0 the body should not fall at all, but it moved to y={motion.position.y:F3}. " +
            "If this now fails, the per-frame reset described in the API reference has been implemented and the doc is no longer wrong.");
    }

    // A negative gravityModifier flips GravityDirection, which inverts ground detection: the body
    // falls upward and treats the underside of geometry above it as ground. Fully implemented,
    // previously untested.
    [UnityTest]
    public IEnumerator NegativeGravityModifier_BodyFallsUpwardAndLandsOnCeiling()
    {
        // The 'ceiling' is ordinary geometry above the body; with inverted gravity it acts as ground.
        CreateStaticFloor(new Vector2(11800, 5f), new Vector2(6f, 1f));

        KinematicMotion2D motion = SpawnBody(new Vector2(11800, 0));
        motion.gravityModifier = -1f;

        yield return new WaitForSeconds(2f);

        Assert.Greater(motion.position.y, 0.5f, "With inverted gravity the body should fall upward.");
        Assert.IsTrue(motion.isGrounded, "With inverted gravity the underside of the geometry above should count as ground.");
        Assert.Less(motion.position.y, 5f, "The body should come to rest below the geometry, not pass through it.");
    }

    // IsGroundNormal consults GravityDirection but IsCeilingNormal hard-codes Vector2.down, so under
    // inverted gravity a single normal classifies as BOTH ground and ceiling - and IsWallNormal,
    // which is defined as "neither", becomes unreachable for it.
    //
    // This test pins the asymmetry as it stands today. It is a flag, not an endorsement: if the
    // classification is ever made symmetric, this test should be updated along with it.
    [Test]
    public void NegativeGravityModifier_CeilingClassification_IsAsymmetric()
    {
        KinematicMotion2D motion = SpawnBody(new Vector2(11900, 0));
        motion.gravityModifier = -1f;

        Assert.IsTrue(motion.IsGroundNormal(Vector2.down),
            "With inverted gravity, a downward-facing normal is ground.");
        Assert.IsTrue(motion.IsCeilingNormal(Vector2.down),
            "IsCeilingNormal ignores GravityDirection, so the same normal is also still classified as a ceiling.");
        Assert.IsFalse(motion.IsWallNormal(Vector2.down),
            "IsWallNormal is defined as 'neither ground nor ceiling', so it stays false for the doubly-classified normal.");
    }

    // ------------------------------------------------------------------
    // One-way platforms and the airborne ceiling-slide branch. Staged in the 12000 lane.
    //
    // RED LIST for this section, measured on a full PlayMode run against the unmodified component:
    //
    //     OneWayPlatform_BodyMovingUp_PassesThrough
    //     OneWayPlatform_RotationalOffset_IsRespected
    //     OneWayPlatform_BodyPassingThrough_IsNotPushedOutByOverlapResolution
    //
    // All three are the same defect. One-way awareness lives in Cast and nowhere else: Cast
    // correctly declines to block a body moving the permitted way, but ProcessOverlaps then sees an
    // ordinary unresolved overlap and ejects it, and CheckForGround - which has no one-way check at
    // all - is free to report the body as standing on the deck it is passing through. The visible
    // result is that a body launched up through a one-way platform is pinned underneath it, and a
    // body falling through an inverted one comes to rest on top of it.
    //
    // The remaining tests in this section pass today.
    // ------------------------------------------------------------------

    // Static geometry carrying a one-way PlatformEffector2D. Cast reads the effector component
    // directly rather than relying on Unity's own effector processing, but usedByEffector is set too
    // so the collider is configured the way a real one-way platform would be.
    private GameObject CreateOneWayPlatform(Vector2 position, Vector2 size, float rotationalOffset = 0f)
    {
        GameObject platform = CreateStaticFloor(position, size);
        platform.name = "OneWayPlatform";

        PlatformEffector2D effector = platform.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true;
        effector.rotationalOffset = rotationalOffset;

        platform.GetComponent<Collider2D>().usedByEffector = true;
        return platform;
    }

    [UnityTest]
    public IEnumerator OneWayPlatform_FallingBody_LandsOnIt()
    {
        CreateOneWayPlatform(new Vector2(12000, 0), new Vector2(6f, 0.5f));

        KinematicMotion2D motion = SpawnBody(new Vector2(12000, 3));

        yield return new WaitForSeconds(1.5f);

        Assert.IsTrue(motion.isGrounded, "A body falling onto a one-way platform should land on it.");
        Assert.Greater(motion.position.y, 0f, "The body should be resting on top of the platform, not below it.");
    }

    // EXPECTED RED. Cast honours the one-way effector correctly, so the body is never *blocked* -
    // but only Cast knows about one-way platforms. ProcessOverlaps does not, so the moment the body
    // overlaps the deck, Separate treats it as an ordinary unresolved overlap and ejects it back the
    // way it came. The body ends up pinned just under the platform it was allowed to enter.
    //
    // Shares a root cause with OneWayPlatform_BodyPassingThrough_IsNotPushedOutByOverlapResolution
    // and OneWayPlatform_RotationalOffset_IsRespected: one-way awareness exists in Cast only, and
    // needs to reach CheckForGround and ProcessOverlaps too.
    [UnityTest]
    public IEnumerator OneWayPlatform_BodyMovingUp_PassesThrough()
    {
        CreateOneWayPlatform(new Vector2(12100, 0), new Vector2(6f, 0.5f));

        KinematicMotion2D motion = SpawnBody(new Vector2(12100, -3));
        motion.velocity = new Vector2(0, 12f);

        yield return new WaitForSeconds(0.5f);

        Assert.Greater(motion.position.y, 0.5f,
            $"A body moving upward should pass through a one-way platform, but it reached only y={motion.position.y:F3}.");
    }

    // rotationalOffset rotates the effector's "up" and so flips which way the platform is solid.
    // At 180 degrees a falling body should pass through instead of landing.
    //
    // EXPECTED RED, for the same reason as OneWayPlatform_BodyMovingUp_PassesThrough: Cast lets the
    // body in, then overlap resolution pushes it back out and CheckForGround - which has no one-way
    // check at all - reports it as grounded. It comes to rest on a surface it should have fallen
    // straight through.
    [UnityTest]
    public IEnumerator OneWayPlatform_RotationalOffset_IsRespected()
    {
        CreateOneWayPlatform(new Vector2(12200, 0), new Vector2(6f, 0.5f), rotationalOffset: 180f);

        KinematicMotion2D motion = SpawnBody(new Vector2(12200, 3));

        yield return new WaitForSeconds(1.5f);

        Assert.Less(motion.position.y, -1f,
            $"With a 180 degree rotationalOffset the platform is solid from below, so a falling body should pass " +
            $"through it, but it stopped at y={motion.position.y:F3}.");
        Assert.IsFalse(motion.isGrounded, "The body should not be grounded on a platform it is passing through.");
    }

    // EXPECTED RED. Cast honours one-way platforms, but CheckForGround does not: it casts along the
    // gravity direction and accepts any ground-facing normal it finds. A body rising through a
    // one-way platform overlaps it, and that overlap answers the downward ground cast at distance 0,
    // so the body is briefly reported as standing on the very platform it is passing through.
    [UnityTest]
    public IEnumerator OneWayPlatform_BodyPassingThrough_IsNotReportedGrounded()
    {
        CreateOneWayPlatform(new Vector2(12300, 0), new Vector2(6f, 0.5f));

        KinematicMotion2D motion = SpawnBody(new Vector2(12300, -3));
        motion.velocity = new Vector2(0, 12f);

        int steps = Mathf.CeilToInt(0.5f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();

            // Only assert while the body is actually moving upward through the platform's band.
            if (motion.velocity.y > 0f && motion.position.y > -1.5f && motion.position.y < 1.5f)
            {
                Assert.IsFalse(motion.isGrounded,
                    $"On step {i} (y={motion.position.y:F3}) the body was reported as grounded on the one-way platform it is passing through.");
            }
        }
    }

    // EXPECTED RED. ProcessOverlaps has no one-way check either, so while a body is mid-pass the
    // separation logic sees an ordinary unresolved overlap and ejects it - fighting the very motion
    // Cast deliberately allowed.
    [UnityTest]
    public IEnumerator OneWayPlatform_BodyPassingThrough_IsNotPushedOutByOverlapResolution()
    {
        CreateOneWayPlatform(new Vector2(12400, 0), new Vector2(6f, 0.5f));

        KinematicMotion2D motion = SpawnBody(new Vector2(12400, -3));
        motion.velocity = new Vector2(0, 12f);

        float previousY = motion.position.y;

        int steps = Mathf.CeilToInt(0.5f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();

            if (motion.position.y > -1.5f && motion.position.y < 1.5f)
            {
                Assert.GreaterOrEqual(motion.position.y, previousY - 0.01f,
                    $"On step {i} the body was pushed back down while passing through the one-way platform " +
                    $"(y went from {previousY:F3} to {motion.position.y:F3}).");
            }

            previousY = motion.position.y;
        }

        Assert.Greater(motion.position.y, 1.5f, "The body should have made it all the way through the platform.");
    }

    // The ceiling-slide branch in Slide only engages when the body is NOT grounded. Gravity is off
    // here so the body is never grounded and the horizontal drive is the only thing moving it, which
    // isolates the branch: any continued progress past the contact point comes from the redirection.
    //
    // NOTE: the geometry here is the fiddliest in the file. If this fails, check the staging (does
    // the body actually reach the slanted underside?) before concluding the branch is broken.
    [UnityTest]
    public IEnumerator AirborneBody_SlidesAlongSlantedCeiling()
    {
        const float ceilingAngle = 20f; // within the default 45 degree maxCeilingAngleDegrees

        // A slab above the body, tilted so its underside is a shallow ceiling rather than a wall.
        CreateStaticFloor(new Vector2(12500 + 3f, 1.2f), new Vector2(8f, 1f), ceilingAngle);

        KinematicMotion2D motion = SpawnBody(new Vector2(12500, 0));
        motion.useGravity = false; // never grounded, so the !isGrounded half of the branch holds
        motion.velocity = new Vector2(2f, 0);

        float xBefore = motion.position.x;
        yield return new WaitForSeconds(2f);

        Assert.Greater(motion.position.x - xBefore, 1f,
            $"An airborne body meeting a shallow ceiling should be redirected along it rather than stopped dead, " +
            $"but it advanced only {motion.position.x - xBefore:F3} units.");
    }

    // The other half of the same condition: a grounded body does not get the ceiling redirection, so
    // the same shallow ceiling stops it instead of steering it.
    [UnityTest]
    public IEnumerator GroundedBody_IsStoppedBySlantedCeiling()
    {
        const float ceilingAngle = 20f;

        CreateStaticFloor(new Vector2(12600, -1f), new Vector2(20f, 1f));

        // Tilted slab whose underside dips into the body's path further along the floor.
        CreateStaticFloor(new Vector2(12600 + 4f, 1.35f), new Vector2(8f, 1f), ceilingAngle);

        KinematicMotion2D motion = SpawnBody(new Vector2(12600, 0));

        yield return new WaitForSeconds(1f);
        Assert.IsTrue(motion.isGrounded, "Precondition: the body should be resting on the floor.");

        motion.velocity = new Vector2(2f, 0);
        yield return new WaitForSeconds(2.5f);

        Assert.Less(motion.position.x, 12600f + 8f,
            $"A grounded body does not get the ceiling-slide redirection, so the slab should stop it, " +
            $"but it reached x={motion.position.x:F3}.");
    }
}
