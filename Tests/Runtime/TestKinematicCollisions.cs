using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PuzzleBox;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public class TestKinematicCollisions
{
    // Looked up by name via AssetDatabase.FindAssets rather than a hardcoded path, so these
    // keep working if the package (or this Tests folder) ever gets moved or renamed.
    const string KinematicBodyPrefabName = "TestKinematicBody2D";
    const string RigidbodyPrefabName = "TestRigidbody2D";
    const string TriggerPrefabName = "TestTrigger2D";

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

    private List<GameObject> spawned;

    [SetUp]
    public void SetUp()
    {
        spawned = new List<GameObject>();
        Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
    }

    // The Unity Test Framework does not automatically load this assembly's scene when running
    // PlayMode tests (it runs in whatever scene happens to be open, typically a blank one), so
    // the grounded-state tests below - which rely on the "Ground" object authored in
    // TestKinematicCollisions.unity - explicitly load it here. LoadSceneInPlayMode works even
    // though the scene isn't listed in Build Settings.
    [UnitySetUp]
    public IEnumerator UnitySetUp()
    {
        string[] guids = AssetDatabase.FindAssets("t:Scene TestKinematicCollisions");
        string scenePath = null;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == "TestKinematicCollisions")
            {
                scenePath = path;
                break;
            }
        }

        Assert.IsNotNull(scenePath, "Could not find the TestKinematicCollisions test scene.");

        yield return EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in spawned)
        {
            if (go != null)
            {
                Object.Destroy(go);
            }
        }
        spawned.Clear();
    }

    private static GameObject LoadPrefabByName(string prefabName)
    {
        string[] guids = AssetDatabase.FindAssets($"t:Prefab {prefabName}");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == prefabName)
            {
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
        }

        Assert.Fail($"Could not find a prefab named '{prefabName}'.");
        return null;
    }

    private GameObject Spawn(string prefabName, Vector2 position)
    {
        GameObject prefab = LoadPrefabByName(prefabName);
        GameObject instance = Object.Instantiate(prefab, position, Quaternion.identity);
        spawned.Add(instance);
        return instance;
    }

    // Plain collider, no Rigidbody2D at all: represents static level geometry.
    // rotationDegrees lets callers build sloped ground for grounded/wall-classification tests.
    private GameObject CreateStaticFloor(Vector2 position, Vector2 size, float rotationDegrees = 0f)
    {
        GameObject floor = new GameObject("StaticFloor");
        floor.transform.position = position;
        floor.transform.rotation = Quaternion.Euler(0, 0, rotationDegrees);
        BoxCollider2D collider = floor.AddComponent<BoxCollider2D>();
        collider.size = size;
        spawned.Add(floor);
        return floor;
    }

    // The TestKinematicBody2D prefab predates KinematicMotion2D.margin, so the field is absent
    // from the prefab's serialized data and deserializes to 0 - below the [Min(0.005f)] the
    // component declares for it, and below Box2D's polygon radius (0.005 per shape, 0.01
    // combined). At margin 0 a body comes to rest exactly touching its ground, so every
    // subsequent horizontal Cast registers a distance-0 hit against that same ground and
    // horizontal motion is blocked. The grounded-state tests below therefore set the documented
    // default explicitly, so they exercise a supported configuration.
    const float DocumentedDefaultMargin = 0.02f;

    private KinematicMotion2D SpawnBody(Vector2 position)
    {
        KinematicMotion2D motion = Spawn(KinematicBodyPrefabName, position).GetComponent<KinematicMotion2D>();
        motion.margin = DocumentedDefaultMargin;
        return motion;
    }

    // Looks up the "Ground" object already present in the test scene.
    private static GameObject FindGround()
    {
        GameObject ground = GameObject.Find("Ground");
        Assert.IsNotNull(ground, "Expected a 'Ground' object in the test scene.");
        return ground;
    }

    private static float GetTopSurfaceY(GameObject obj)
    {
        Collider2D collider = obj.GetComponent<Collider2D>();
        Assert.IsNotNull(collider, $"Expected '{obj.name}' to have a Collider2D.");
        return collider.bounds.max.y;
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

    [UnityTest]
    public IEnumerator KinematicBody_DoesNotCollideWithTrigger()
    {
        GameObject trigger = Spawn(TriggerPrefabName, new Vector2(0, 0));

        GameObject body = Spawn(KinematicBodyPrefabName, new Vector2(0, 3));
        CollisionRecorder recorder = body.AddComponent<CollisionRecorder>();

        // Let the body fall through the trigger's position entirely.
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
    private KinematicMotion2D SpawnFreeStandingPlatform(Vector2 position, bool sticky)
    {
        KinematicMotion2D platform = SpawnBody(position);
        platform.transform.localScale = new Vector3(6, 1, 1);
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
}
