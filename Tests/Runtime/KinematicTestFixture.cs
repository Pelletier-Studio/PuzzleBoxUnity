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

/// <summary>
/// Shared staging for the KinematicMotion2D test suites: prefab lookup, scene loading, spawning and
/// teardown. These members were previously duplicated verbatim across TestKinematicCollisions and
/// TestKinematicVisualInspection; they live here so the newer suites do not make a third and fourth
/// copy of them.
///
/// Nothing in here asserts anything about KinematicMotion2D. It is staging only.
/// </summary>
public abstract class KinematicTestFixture
{
    // Looked up by name via AssetDatabase.FindAssets rather than a hardcoded path, so these
    // keep working if the package (or this Tests folder) ever gets moved or renamed.
    protected const string KinematicBodyPrefabName = "TestKinematicBody2D";
    protected const string RigidbodyPrefabName = "TestRigidbody2D";
    protected const string TriggerPrefabName = "TestTrigger2D";

    // The TestKinematicBody2D prefab predates KinematicMotion2D.margin, so the field is absent
    // from the prefab's serialized data and deserializes to 0 - below the [Min(0.005f)] the
    // component declares for it, and below Box2D's polygon radius (0.005 per shape, 0.01
    // combined). At margin 0 a body comes to rest exactly touching its ground, so every
    // subsequent horizontal Cast registers a distance-0 hit against that same ground and
    // horizontal motion is blocked. Tests that rely on movement therefore set the documented
    // default explicitly, so they exercise a supported configuration.
    protected const float DocumentedDefaultMargin = 0.02f;

    protected List<GameObject> spawned;

    [SetUp]
    public void SetUp()
    {
        spawned = new List<GameObject>();
        Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
    }

    // The Unity Test Framework does not automatically load this assembly's scene when running
    // PlayMode tests (it runs in whatever scene happens to be open, typically a blank one), so
    // the tests below - which rely on the "Ground" object authored in
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

    protected static GameObject LoadPrefabByName(string prefabName)
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

    protected GameObject Spawn(string prefabName, Vector2 position)
    {
        GameObject prefab = LoadPrefabByName(prefabName);
        GameObject instance = Object.Instantiate(prefab, position, Quaternion.identity);
        spawned.Add(instance);
        return instance;
    }

    protected KinematicMotion2D SpawnBody(Vector2 position)
    {
        KinematicMotion2D motion = Spawn(KinematicBodyPrefabName, position).GetComponent<KinematicMotion2D>();
        motion.margin = DocumentedDefaultMargin;
        return motion;
    }

    // Plain collider, no Rigidbody2D at all: represents static level geometry.
    // rotationDegrees lets callers build sloped ground for grounded/wall-classification tests.
    protected GameObject CreateStaticFloor(Vector2 position, Vector2 size, float rotationDegrees = 0f)
    {
        GameObject floor = new GameObject("StaticFloor");
        floor.transform.position = position;
        floor.transform.rotation = Quaternion.Euler(0, 0, rotationDegrees);
        BoxCollider2D collider = floor.AddComponent<BoxCollider2D>();
        collider.size = size;
        spawned.Add(floor);
        return floor;
    }

    // Looks up the "Ground" object already present in the test scene.
    protected static GameObject FindGround()
    {
        GameObject ground = GameObject.Find("Ground");
        Assert.IsNotNull(ground, "Expected a 'Ground' object in the test scene.");
        return ground;
    }

    protected static float GetTopSurfaceY(GameObject obj)
    {
        Collider2D collider = obj.GetComponent<Collider2D>();
        Assert.IsNotNull(collider, $"Expected '{obj.name}' to have a Collider2D.");
        return collider.bounds.max.y;
    }
}
