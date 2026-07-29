using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PuzzleBox;
#if UNITY_EDITOR
using UnityEditor;
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
    private GameObject CreateStaticFloor(Vector2 position, Vector2 size)
    {
        GameObject floor = new GameObject("StaticFloor");
        floor.transform.position = position;
        BoxCollider2D collider = floor.AddComponent<BoxCollider2D>();
        collider.size = size;
        spawned.Add(floor);
        return floor;
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
}
