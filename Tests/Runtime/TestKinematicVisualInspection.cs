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
/// Watchable scenarios for eyeballing KinematicMotion2D's ground-following behaviour in the Game
/// view. These are companions to the assertion-driven tests in TestKinematicCollisions, not a
/// replacement: they run on a slow, human-readable timeline with an on-screen HUD so the motion can
/// actually be observed.
///
/// Every scenario is marked [Explicit], so a normal "Run All" skips them - select one in the Test
/// Runner and run it on its own, with the Game view visible. Each takes roughly 10-15 seconds.
///
/// Everything is staged inside the scene camera's view (orthographic size 5 at the origin), unlike
/// the automated tests which park bodies far off-screen to keep them from interacting.
/// </summary>
public class TestKinematicVisualInspection : KinematicTestFixture
{
    // Prefab names, scene loading, spawning and teardown live in KinematicTestFixture.

    static readonly Color BodyColor = new Color(1f, 1f, 1f);
    static readonly Color PlatformColor = new Color(0.95f, 0.55f, 0.2f);
    static readonly Color ObstacleColor = new Color(0.75f, 0.25f, 0.25f);

    // Draws the current phase and the live motion state over the Game view.
    private class ScenarioHud : MonoBehaviour
    {
        public string scenario = "";
        public string phase = "";
        public KinematicMotion2D body;
        public KinematicMotion2D platform;

        // Optional third body, shown when a scenario involves an attached item as well as a
        // body and a platform.
        public KinematicMotion2D attached;
        public string attachedLabel = "attached";

        private static string AttachmentOf(KinematicMotion2D motion)
        {
            string parent = motion.attachedTo != null ? motion.attachedTo.name : "-";
            return $"attachedTo={parent}  attachedCount={motion.attachedCount}";
        }

        void OnGUI()
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 32;
            style.normal.textColor = Color.white;

            string text = $"{scenario}\n\n{phase}\n";

            if (body != null)
            {
                text += $"\nbody      grounded={body.isGrounded}  velocity={body.velocity}" +
                        $"  groundVelocity={body.groundVelocity}  timeInAir={body.timeInAir:F2}" +
                        $"\n          position={body.position}  {AttachmentOf(body)}";
            }

            if (platform != null)
            {
                text += $"\nplatform  velocity={platform.velocity}  position={platform.position}" +
                        $"  {AttachmentOf(platform)}";
            }

            if (attached != null)
            {
                text += $"\n{attachedLabel,-9} position={attached.position}  velocity={attached.velocity}" +
                        $"  {AttachmentOf(attached)}";
            }

            GUI.Box(new Rect(8, 8, 950, 460), GUIContent.none);
            GUI.Label(new Rect(20, 16, 930, 440), text, style);
        }
    }

    // Echoes OnCrushedBy to the Console so a crush can be correlated with what is on screen.
    private class CrushLogger : MonoBehaviour
    {
        void OnCrushedBy(KinematicMotion2D.Contact contact)
        {
            string crusher = contact.collider != null ? contact.collider.name : "<unknown>";
            Debug.Log($"[visual] CRUSHED BY {crusher} at {contact.point}");
        }
    }

    private ScenarioHud hud;

    // Runs in addition to KinematicTestFixture.TearDown (NUnit calls derived teardowns first,
    // then base ones), which is what actually destroys the spawned objects.
    [TearDown]
    public void TearDownHud()
    {
        hud = null;
    }

    private KinematicMotion2D SpawnColoredBody(string name, Vector2 position, Color color)
    {
        GameObject instance = Object.Instantiate(LoadPrefabByName(KinematicBodyPrefabName), position, Quaternion.identity);
        instance.name = name;
        spawned.Add(instance);

        SpriteRenderer renderer = instance.GetComponent<SpriteRenderer>();
        if (renderer != null)
        {
            renderer.color = color;
        }

        KinematicMotion2D motion = instance.GetComponent<KinematicMotion2D>();
        motion.margin = DocumentedDefaultMargin;
        return motion;
    }

    private KinematicMotion2D SpawnPlatform(Vector2 position, float width)
    {
        KinematicMotion2D platform = SpawnColoredBody("Platform", position, PlatformColor);
        platform.transform.localScale = new Vector3(width, 1f, 1f);
        platform.useGravity = false;
        platform.velocity = Vector2.zero;
        return platform;
    }

    private KinematicMotion2D SpawnRider(Vector2 position)
    {
        return SpawnColoredBody("Rider", position, BodyColor);
    }

    private void CreateHud(string scenario, KinematicMotion2D body, KinematicMotion2D platform)
    {
        GameObject go = new GameObject("ScenarioHud");
        spawned.Add(go);
        hud = go.AddComponent<ScenarioHud>();
        hud.scenario = scenario;
        hud.body = body;
        hud.platform = platform;
    }

    // Advances the scenario to a labelled phase and holds there long enough to watch it.
    private IEnumerator Phase(string description, float seconds)
    {
        if (hud != null)
        {
            hud.phase = description;
        }
        Debug.Log($"[visual] {description}");
        yield return new WaitForSeconds(seconds);
    }

    // Deliberately loose: these scenarios exist to be watched, not to assert fine detail. This only
    // catches a body falling out of the world entirely, so a broken scenario still fails loudly
    // instead of quietly showing nothing.
    //
    // Every scenario is staged (deck length, speeds, phase durations) so that nothing is *supposed*
    // to run off the end of a surface. If this fires, re-stage the scenario rather than loosening
    // the check: a body that slides off the deck, off the scene Ground, and into the void may be
    // behaving perfectly correctly, but it has stopped demonstrating whatever it was meant to show.
    private static void AssertStillInPlay(KinematicMotion2D body)
    {
        Assert.Greater(body.position.y, -10f, "The body fell out of the world - the scenario did not play out as intended.");
    }

    // ------------------------------------------------------------------
    // Riding a platform that is already carrying the body
    // ------------------------------------------------------------------

    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_RiderIsCarried_HorizontalPlatformMotion()
    {
        KinematicMotion2D platform = SpawnPlatform(new Vector2(0, -2), 4f);
        KinematicMotion2D rider = SpawnRider(new Vector2(0, -0.9f));
        CreateHud("Horizontal platform motion - rider should track the platform exactly", rider, platform);

        yield return Phase("Settling onto the stationary platform", 1.5f);
        yield return Phase("Resting - platform stationary (3s)", 3f);

        platform.velocity = new Vector2(1f, 0);
        yield return Phase("Platform moving RIGHT at 1.0 (3s) - rider should move with it", 3f);

        platform.velocity = new Vector2(-1f, 0);
        yield return Phase("Platform moving LEFT at 1.0 (3s) - rider should reverse with it", 3f);

        platform.velocity = Vector2.zero;
        yield return Phase("Platform stopped - rider should stop dead, no sliding", 2f);

        AssertStillInPlay(rider);
    }

    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_RiderIsCarried_VerticalPlatformMotion()
    {
        // Started high enough that the downward leg never reaches the scene's Ground object.
        KinematicMotion2D platform = SpawnPlatform(new Vector2(0, -1), 4f);
        KinematicMotion2D rider = SpawnRider(new Vector2(0, 0.1f));
        CreateHud("Vertical platform motion - rider should stay glued to the platform", rider, platform);

        yield return Phase("Settling onto the stationary platform", 1.5f);
        yield return Phase("Resting - platform stationary (3s)", 3f);

        // Rising ground: the rider must not be flagged as 'taking off' by the grounded check.
        platform.velocity = new Vector2(0, 0.8f);
        yield return Phase("Platform moving UP at 0.8 (3s) - rider should stay grounded, no bouncing", 3f);

        // Descending ground exercises the delta.y < 0 branch of GroundWillMove, which moves
        // vertically outside of Slide to avoid colliding with ground that has not moved yet.
        platform.velocity = new Vector2(0, -0.8f);
        yield return Phase("Platform moving DOWN at 0.8 (3s) - rider should follow, not float or sink", 3f);

        platform.velocity = Vector2.zero;
        yield return Phase("Platform stopped - rider should settle immediately", 2f);

        AssertStillInPlay(rider);
    }

    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_RiderIsCarried_DiagonalPlatformMotion()
    {
        KinematicMotion2D platform = SpawnPlatform(new Vector2(-2, -2), 4f);
        KinematicMotion2D rider = SpawnRider(new Vector2(-2, -0.9f));
        CreateHud("Diagonal platform motion - horizontal and vertical carry combined", rider, platform);

        yield return Phase("Settling onto the stationary platform", 1.5f);
        yield return Phase("Resting - platform stationary (3s)", 3f);

        platform.velocity = new Vector2(1f, 0.6f);
        yield return Phase("Platform moving UP-RIGHT (3s) - rider should track both axes", 3f);

        platform.velocity = new Vector2(-1f, -0.6f);
        yield return Phase("Platform moving DOWN-LEFT (3s) - rider should track back", 3f);

        platform.velocity = Vector2.zero;
        yield return Phase("Platform stopped", 2f);

        AssertStillInPlay(rider);
    }

    // ------------------------------------------------------------------
    // Landing on a platform that is already moving
    // ------------------------------------------------------------------

    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_DroppedRider_LandsOnMovingPlatform_DriftsWithoutFriction()
    {
        // Wide and slow so the rider stays on the deck long enough to watch the drift develop.
        // The rider holds station in world space while the deck travels right underneath it, so the
        // deck's LEFT edge is what eventually catches up - hence starting the platform well to the
        // left of the drop point.
        KinematicMotion2D platform = SpawnPlatform(new Vector2(-1, -2), 10f);
        platform.velocity = new Vector2(0.5f, 0);

        KinematicMotion2D rider = SpawnRider(new Vector2(1, 2));
        CreateHud("Dropped onto a moving platform - EXPECTED: rider holds station while the platform slides underneath (frictionless)", rider, platform);

        yield return Phase("Platform moving RIGHT at 0.5, rider falling straight down", 2f);
        yield return Phase("Landed - rider velocity should now read about (-0.5, 0): its ground-relative drift", 3f);
        yield return Phase("Rider should stay put in world space while the platform slides right beneath it", 4f);

        AssertStillInPlay(rider);
    }

    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_MovingRider_LandsOnMovingPlatform_MatchedSpeed()
    {
        // Both moving right at the same speed: the landing adjustment cancels cleanly and the
        // rider should settle into riding the platform with no visible slide at all.
        //
        // Both start well to the left, because nothing here ever slows down - the whole scenario
        // travels steadily right and would otherwise leave the camera's view before it finishes.
        KinematicMotion2D platform = SpawnPlatform(new Vector2(-3, -2), 8f);
        platform.velocity = new Vector2(0.5f, 0);

        KinematicMotion2D rider = SpawnRider(new Vector2(-5, 2));
        rider.velocity = new Vector2(0.5f, 0);
        CreateHud("Moving rider lands on a platform moving at the SAME speed - EXPECTED: clean pickup, no slide", rider, platform);

        yield return Phase("Both moving RIGHT at 0.5, rider falling", 2f);
        yield return Phase("Landed - rider velocity should collapse to about zero relative to the platform", 3f);
        yield return Phase("Rider should ride along with no drift across the deck", 4f);

        AssertStillInPlay(rider);
    }

    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_MovingRider_LandsOnMovingPlatform_OpposingDirections()
    {
        // The hardest case: closing head-on. The rider keeps its full relative velocity on landing,
        // so it should visibly continue sliding across the deck after touchdown.
        //
        // Speeds are kept low and the deck long on purpose. The rider lands with velocity
        // 0.6 - (-0.5) = 1.1 and so crosses the deck at 1.1 units/s relative to it; anything faster
        // runs off the end, lands on the scene Ground, and then runs off that too before the
        // scenario finishes - which is correct behaviour but shows nothing useful.
        KinematicMotion2D platform = SpawnPlatform(new Vector2(0, -2), 12f);
        platform.velocity = new Vector2(-0.5f, 0);

        KinematicMotion2D rider = SpawnRider(new Vector2(-3, 1.5f));
        rider.velocity = new Vector2(0.6f, 0);
        CreateHud("Moving rider lands on a platform moving the OTHER way - EXPECTED: rider keeps sliding across the deck", rider, platform);

        yield return Phase("Rider moving RIGHT at 0.6, platform moving LEFT at 0.5, closing", 2f);
        yield return Phase("Landed - rider velocity should read about 1.1, and it should slide right across the deck", 4f);

        platform.velocity = Vector2.zero;
        yield return Phase("Platform stopped - rider should continue right at its own velocity", 2f);

        AssertStillInPlay(rider);
    }

    // ------------------------------------------------------------------
    // Edge cases: outrunning free fall, and rising fast enough to tunnel
    // ------------------------------------------------------------------

    // 12 units/s for 0.6s drops the platform 7.2 units, while a body starting from rest free-falls
    // only about 1.8 units in the same window - a wide enough gap to see plainly. Longer, gentler
    // descents do not work here: free fall accelerates, so over ~1.2s a 6 units/s platform and a
    // falling body cover almost exactly the same distance and the difference becomes invisible.
    const float OutrunsFreeFallSpeed = 12f;
    const float OutrunsFreeFallWindow = 0.6f;

    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_StickyPlatform_DescendsFasterThanFreeFall_RiderStaysAttached()
    {
        KinematicMotion2D platform = SpawnPlatform(new Vector2(0, 3), 5f);
        platform.sticky = true;

        KinematicMotion2D rider = SpawnRider(new Vector2(0, 4.1f));
        CreateHud("STICKY platform dropping faster than free fall - EXPECTED: rider stays glued to the deck", rider, platform);

        yield return Phase("Settling onto the stationary platform", 1.5f);
        yield return Phase("Resting near the top of the view", 2f);

        platform.velocity = new Vector2(0, -OutrunsFreeFallSpeed);
        yield return Phase($"Platform DROPPING at {OutrunsFreeFallSpeed} - rider should ride it down, staying grounded", OutrunsFreeFallWindow);

        platform.velocity = Vector2.zero;
        yield return Phase("Stopped - rider should still be sitting on the deck", 3f);

        AssertStillInPlay(rider);
    }

    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_NonStickyPlatform_DescendsFasterThanFreeFall_RiderIsLeftBehind()
    {
        KinematicMotion2D platform = SpawnPlatform(new Vector2(0, 3), 5f);
        platform.sticky = false;

        KinematicMotion2D rider = SpawnRider(new Vector2(0, 4.1f));
        CreateHud("NON-STICKY platform dropping faster than free fall - EXPECTED: deck drops away, rider falls behind it", rider, platform);

        yield return Phase("Settling onto the stationary platform", 1.5f);
        yield return Phase("Resting near the top of the view", 2f);

        platform.velocity = new Vector2(0, -OutrunsFreeFallSpeed);
        yield return Phase($"Platform DROPPING at {OutrunsFreeFallSpeed} - deck should pull away below, rider left falling under gravity", OutrunsFreeFallWindow);

        platform.velocity = Vector2.zero;
        yield return Phase("Stopped - watch the rider fall the remaining distance and land back on the deck", 3f);

        AssertStillInPlay(rider);
    }

    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_PlatformRisingVeryFast_RiderStaysOnTop()
    {
        // Rises 5 units in 0.5s. The rider is pushed up ahead of the deck by GroundWillMove, so it
        // should stay planted on the surface rather than being passed through or punted upward.
        KinematicMotion2D platform = SpawnPlatform(new Vector2(0, -2.5f), 5f);
        KinematicMotion2D rider = SpawnRider(new Vector2(0, -1.4f));
        CreateHud("Platform RISING very fast - EXPECTED: rider stays planted on top, never clipped or launched", rider, platform);

        yield return Phase("Settling onto the stationary platform", 1.5f);
        yield return Phase("Resting near the bottom of the view", 2f);

        platform.velocity = new Vector2(0, 10f);
        yield return Phase("Platform RISING at 10 - rider should stay glued to the surface", 0.5f);

        platform.velocity = Vector2.zero;
        yield return Phase("Stopped - rider should settle immediately, with no bounce or overshoot", 3f);

        AssertStillInPlay(rider);
    }

    // ------------------------------------------------------------------
    // Static ground, for comparison
    // ------------------------------------------------------------------

    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_RiderRunsAndLandsOnStaticGround()
    {
        // Uses the scene's own Ground object, so this doubles as a check that the authored scene
        // behaves the way the automated tests assume.
        GameObject ground = GameObject.Find("Ground");
        Assert.IsNotNull(ground, "Expected a 'Ground' object in the test scene.");

        KinematicMotion2D rider = SpawnRider(new Vector2(-5, 3));
        CreateHud("Static ground - falling, landing, running, and launching", rider, null);

        yield return Phase("Falling towards the static ground", 2f);
        yield return Phase("Landed - should rest still, no jitter or sinking", 2f);

        rider.velocity = new Vector2(2.5f, 0);
        yield return Phase("Running RIGHT at 2.5 (3s) - should travel smoothly along the surface", 3f);

        rider.velocity = new Vector2(0, 8f);
        yield return Phase("Launched UP - should leave the ground, then fall back", 3f);

        yield return Phase("Settled again", 2f);

        AssertStillInPlay(rider);
    }

    // ------------------------------------------------------------------
    // A rider on a moving platform collides with static geometry. Companions to the
    // assertion-driven tests of the same shape in TestKinematicCollisions.
    // ------------------------------------------------------------------

    private static GameObject CreateStaticObstacle(string name, Vector2 position, Vector2 size, float rotationDegrees = 0f)
    {
        GameObject obstacle = new GameObject(name);
        obstacle.transform.position = position;
        obstacle.transform.rotation = Quaternion.Euler(0, 0, rotationDegrees);
        // Scale a 1x1 collider up to the requested size (rather than setting collider.size
        // directly) so a Simple-drawMode SpriteRenderer - which follows transform scale, not the
        // collider - renders at the same size as the actual collider.
        obstacle.transform.localScale = new Vector3(size.x, size.y, 1f);

        BoxCollider2D collider = obstacle.AddComponent<BoxCollider2D>();
        collider.size = Vector2.one;

        // Borrow the kinematic body prefab's sprite/material so the obstacle is actually visible
        // in the Game view instead of being an invisible collider.
        SpriteRenderer reference = LoadPrefabByName(KinematicBodyPrefabName).GetComponent<SpriteRenderer>();
        SpriteRenderer renderer = obstacle.AddComponent<SpriteRenderer>();
        renderer.sprite = reference.sprite;
        renderer.sharedMaterial = reference.sharedMaterial;
        renderer.color = ObstacleColor;

        return obstacle;
    }

    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_RiderOnHorizontalPlatform_HitsStaticWall()
    {
        KinematicMotion2D platform = SpawnPlatform(new Vector2(-3, -2), 6f);
        KinematicMotion2D rider = SpawnRider(new Vector2(-3, -0.9f));
        CreateHud("Rider on a moving platform hits a static wall - EXPECTED: rider stops at the wall, platform keeps going underneath", rider, platform);

        yield return Phase("Settling onto the stationary platform", 1.5f);

        // The wall's bottom face sits above the platform's own box, so only the rider (not the
        // platform) can ever reach it.
        float platformTopY = platform.position.y + 0.5f;
        GameObject wall = CreateStaticObstacle("Wall", new Vector2(1.5f, platformTopY + 2f), new Vector2(0.6f, 3f));
        spawned.Add(wall);

        // At speed 0.5 the rider (starting ~3.7 units short of the wall) needs about 7.4s to reach
        // it - this phase has to be at least that long or the scenario ends before contact happens.
        platform.velocity = new Vector2(0.5f, 0);
        yield return Phase("Platform moving RIGHT at 0.5 - rider should stop at the wall, no clipping", 8f);

        yield return Phase("Rider pinned at the wall - platform should still be visibly sliding underneath it", 2f);

        AssertStillInPlay(rider);
    }

    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_RiderOnHorizontalPlatform_CrushedAgainstWallByAnotherKinematicBody()
    {
        KinematicMotion2D platform = SpawnPlatform(new Vector2(-3, -2), 6f);
        KinematicMotion2D rider = SpawnRider(new Vector2(-3, -0.9f));
        CreateHud("Rider pinned at a wall gets crushed by a second moving body - EXPECTED: neither clips through the other", rider, platform);

        yield return Phase("Settling onto the stationary platform", 1.5f);

        float platformTopY = platform.position.y + 0.5f;
        GameObject wall = CreateStaticObstacle("Wall", new Vector2(1.5f, platformTopY + 2f), new Vector2(0.6f, 3f));
        spawned.Add(wall);

        // Same 3.7-unit gap to the wall as the simple-collision scenario above, so wait it out in
        // full before bringing in the second body - otherwise the mover chases a rider that is
        // still moving at the same speed it is and never catches up.
        platform.velocity = new Vector2(0.5f, 0);
        yield return Phase("Platform moving RIGHT at 0.5 - rider travelling towards the wall", 8f);

        // Needs to be pushable so the incoming mover is actually allowed to push it.
        rider.pushable = true;

        // Starts close enough, and clearly faster than the rider it is chasing (which is now
        // pinned and stationary), to visibly close the gap and make contact within the phase below.
        KinematicMotion2D mover = SpawnColoredBody("Mover", new Vector2(rider.position.x - 4f, rider.position.y), PlatformColor);
        mover.useGravity = false;
        mover.velocity = new Vector2(1.5f, 0);

        yield return Phase("A second body closes in from the left, squeezing the rider against the wall", 3.5f);
        yield return Phase("Rider should stay pinned at the wall - the mover should stop at the rider, not overlap it", 2f);

        AssertStillInPlay(rider);
    }

    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_RiderOnRisingPlatform_CrushedAgainstCeiling()
    {
        // Both spawned above -2.5 so neither overlaps the scene's Ground object, whose top surface
        // sits at y=-3 (position -3.5, scale 1 -> top = -3.5 + 0.5).
        KinematicMotion2D platform = SpawnPlatform(new Vector2(0, -2f), 4f);
        KinematicMotion2D rider = SpawnRider(new Vector2(0, -0.9f));
        CreateHud("Rider on a rising platform is crushed against a ceiling - EXPECTED: OnCrushedBy fires once (see Console). The platform NOT stopping is a known, deliberately deferred gap.", rider, platform);

        // Logs the crush to the Console so it can be correlated with what is on screen.
        rider.gameObject.AddComponent<CrushLogger>();

        yield return Phase("Settling onto the stationary platform", 1.5f);

        float platformTopY = platform.position.y + 0.5f;
        GameObject ceiling = CreateStaticObstacle("Ceiling", new Vector2(0, platformTopY + 3.5f), new Vector2(4f, 1f));
        spawned.Add(ceiling);

        platform.velocity = new Vector2(0, 0.6f);
        yield return Phase("Platform RISING at 0.6 - rider's head should stop at the ceiling", 4f);

        yield return Phase("A single '[visual] CRUSHED BY ...' line should have appeared in the Console. The platform still climbing into the rider is the deferred half of this behaviour.", 4f);

        AssertStillInPlay(rider);
    }

    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_RiderOnSlantedPlatform_PushedAlongSlopeIntoObstacle()
    {
        const float slopeAngle = 30f;
        Vector2 normal = Quaternion.Euler(0, 0, slopeAngle) * Vector2.up;
        Vector2 upSlope = new Vector2(normal.y, -normal.x);

        KinematicMotion2D platform = SpawnPlatform(new Vector2(-2, -3), 8f);
        platform.transform.rotation = Quaternion.Euler(0, 0, slopeAngle);

        KinematicMotion2D rider = SpawnRider(new Vector2(-2, -3) + normal * 4f);
        CreateHud("Rider on a slanted moving platform pushed into an obstacle - EXPECTED: pushed along the slope, not clipped or knocked off", rider, platform);

        yield return Phase("Settling onto the stationary slanted platform", 1.5f);
        rider.velocity = Vector2.zero;

        Vector2 platformSurfacePoint = platform.position + normal * 0.5f;
        Vector2 obstacleCenter = platformSurfacePoint + upSlope * (Vector2.Dot(rider.position - platformSurfacePoint, upSlope) + 3f) + normal * 2.5f;
        GameObject obstacle = CreateStaticObstacle("SlopeObstacle", obstacleCenter, new Vector2(0.6f, 4f), slopeAngle);
        spawned.Add(obstacle);

        platform.velocity = upSlope * 0.6f;
        yield return Phase("Platform moving UP-SLOPE - rider should be stopped by the obstacle, staying on the incline", 6f);

        AssertStillInPlay(rider);
    }

    // The scenario that exposed the carry bug. Unlike the up-slope case above, world-horizontal
    // motion of a tilted deck is NOT within the deck's own plane, so the carry has to be split
    // into a tangential part (which the obstacle blocks) and a normal part (which must still
    // apply). Get that wrong and the rider slides backwards down the deck and starts chattering
    // in and out of contact within a single physics step of the platform starting to move.
    //
    // Note the obstacle is closer here (1.5 rather than 3.0) and the platform slower. A static
    // obstacle on a horizontally translating incline only stays engaged for a limited window -
    // the deck sinks away from world-static geometry at 0.5x the platform speed - so the
    // scenario is staged to land inside that window. See BuildSlopeScenario in
    // TestKinematicCollisions for the arithmetic.
    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_RiderOnSlantedPlatform_HorizontalMotion_StaysOnSurface()
    {
        const float slopeAngle = 30f;
        Vector2 normal = Quaternion.Euler(0, 0, slopeAngle) * Vector2.up;
        Vector2 upSlope = new Vector2(normal.y, -normal.x);

        KinematicMotion2D platform = SpawnPlatform(new Vector2(-2, -3), 8f);
        platform.transform.rotation = Quaternion.Euler(0, 0, slopeAngle);

        KinematicMotion2D rider = SpawnRider(new Vector2(-2, -3) + normal * 4f);
        CreateHud("Slanted platform moving HORIZONTALLY into an obstacle - EXPECTED: rider stays planted on the incline, no sliding back, no grounded flicker", rider, platform);

        yield return Phase("Settling onto the stationary slanted platform", 1.5f);
        rider.velocity = Vector2.zero;

        Vector2 platformSurfacePoint = platform.position + normal * 0.5f;
        Vector2 obstacleCenter = platformSurfacePoint + upSlope * (Vector2.Dot(rider.position - platformSurfacePoint, upSlope) + 1.5f) + normal * 2.5f;
        GameObject obstacle = CreateStaticObstacle("SlopeObstacle", obstacleCenter, new Vector2(0.6f, 4f), slopeAngle);
        spawned.Add(obstacle);

        platform.velocity = new Vector2(0.6f, 0);
        yield return Phase("Platform moving RIGHT (not along the slope) - watch 'grounded' in the HUD: it must stay True the whole time", 2.5f);

        yield return Phase("Rider should be resting against the obstacle, still flat on the deck - not sunk into it, not floating above it", 2f);

        AssertStillInPlay(rider);
    }

    // ------------------------------------------------------------------
    // Attachment (AttachTo / Detach). Companions to the assertion-driven tests in
    // TestKinematicAttachment, which states the full specification these scenarios illustrate.
    //
    // Most of these currently show the WRONG behaviour on purpose - they are how you watch the
    // defects that the red tests in TestKinematicAttachment describe. Each HUD line says what
    // SHOULD happen, so the gap is visible rather than having to be remembered.
    // ------------------------------------------------------------------

    private KinematicMotion2D SpawnItem(string name, Vector2 position)
    {
        KinematicMotion2D item = SpawnColoredBody(name, position, ObstacleColor);
        item.useGravity = false;
        item.velocity = Vector2.zero;
        return item;
    }

    private void CreateHud(string scenario, KinematicMotion2D body, KinematicMotion2D platform, KinematicMotion2D attached, string attachedLabel = "item")
    {
        CreateHud(scenario, body, platform);
        hud.attached = attached;
        hud.attachedLabel = attachedLabel;
    }

    // The headline of the corrected specification: an attached item's colliders are part of its
    // carrier, so a wall only the item can reach must stop the carrier.
    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_AttachedChild_StopsCarrierAtWall()
    {
        KinematicMotion2D carrier = SpawnColoredBody("Carrier", new Vector2(-4, -2), PlatformColor);
        carrier.useGravity = false;
        carrier.velocity = Vector2.zero;

        KinematicMotion2D item = SpawnItem("Item", new Vector2(-4, -0.5f));

        CreateHud("Carrier holding an item walks into a wall only the ITEM can reach - EXPECTED: the carrier stops. " +
                  "Watch for the item ploughing straight through the wall instead: that is the open defect.",
                  carrier, null, item);

        yield return null;
        item.AttachTo(carrier);

        yield return Phase("Attached - the item should be locked to the carrier", 2f);

        // The wall's bottom face sits above the carrier's own box, so only the item can reach it.
        GameObject wall = CreateStaticObstacle("Wall", new Vector2(1.5f, 1.5f), new Vector2(0.6f, 3.6f));
        spawned.Add(wall);

        carrier.velocity = new Vector2(1f, 0);
        yield return Phase("Carrier moving RIGHT at 1.0 - the item should meet the wall and stop the carrier with it", 6f);

        yield return Phase("Carrier should be at rest with the item against the wall, neither overlapping it", 2f);

        AssertStillInPlay(carrier);
    }

    // The one deliberate exception to the merged footprint: an item resting on the floor stops its
    // holder's descent, but must not make the holder GROUNDED. Watch the grounded flag on the HUD.
    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_AttachedChild_DanglingOntoFloor()
    {
        KinematicMotion2D carrier = SpawnColoredBody("Carrier", new Vector2(0, 2f), PlatformColor);
        KinematicMotion2D item = SpawnItem("Item", new Vector2(0, 0.5f));

        CreateHud("Carrier falling with an item dangling below it - EXPECTED: the item lands on the ground and stops " +
                  "the carrier, but 'grounded' on the HUD stays False and timeInAir keeps climbing.",
                  carrier, null, item);

        yield return null;
        item.AttachTo(carrier);

        yield return Phase("Falling - the item leads the way down", 2.5f);
        yield return Phase("The item should be resting on the ground with the carrier suspended above it, NOT grounded", 4f);

        AssertStillInPlay(carrier);
    }

    // The double-movement defect, on screen: a rider that is both standing on a platform and
    // attached to it is carried twice per frame, so it visibly outruns the deck it is standing on.
    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_AttachedRider_AlsoGroundedOnItsCarrier()
    {
        KinematicMotion2D platform = SpawnPlatform(new Vector2(-3, -2), 8f);
        KinematicMotion2D rider = SpawnRider(new Vector2(-5, -0.9f));

        CreateHud("Rider standing on a platform AND attached to it - EXPECTED: it rides along normally. " +
                  "Watch it slide forward across the deck instead: that is the double-carry defect.",
                  rider, platform, null);

        yield return Phase("Settling onto the stationary platform", 1.5f);

        rider.AttachTo(platform);
        yield return Phase("Attached while grounded - still stationary", 1.5f);

        platform.velocity = new Vector2(0.8f, 0);
        yield return Phase("Platform moving RIGHT at 0.8 - the rider should keep its place on the deck, not run ahead of it", 5f);

        platform.velocity = Vector2.zero;
        yield return Phase("Stopped - compare the rider's position on the deck with where it started", 2f);

        AssertStillInPlay(rider);
    }

    // Chains: moving the root should move the whole chain. Currently the grandchild sits still.
    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_AttachmentChain_ThreeBodies()
    {
        KinematicMotion2D root = SpawnColoredBody("Root", new Vector2(-4, -2), PlatformColor);
        root.useGravity = false;
        root.velocity = Vector2.zero;

        KinematicMotion2D child = SpawnItem("Child", new Vector2(-4, -0.5f));
        KinematicMotion2D grandchild = SpawnItem("Grandchild", new Vector2(-4, 1f));

        CreateHud("Chain of three: Root <- Child <- Grandchild - EXPECTED: all three move together. " +
                  "Watch the grandchild stay behind: attachment does not currently propagate past the first level.",
                  root, child, grandchild, "grandchd");

        yield return null;
        child.AttachTo(root);
        grandchild.AttachTo(child);

        yield return Phase("Chain assembled, stationary", 2f);

        root.velocity = new Vector2(1f, 0);
        yield return Phase("Root moving RIGHT at 1.0 - the whole stack should travel as one", 6f);

        root.velocity = Vector2.zero;
        yield return Phase("Stopped - all three should still be in a vertical line", 2f);

        AssertStillInPlay(root);
    }

    // The GrabKinematicMotion2D use case end to end: pick something up, carry it, put it down again.
    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_GrabAndRelease_MidMotion()
    {
        KinematicMotion2D carrier = SpawnColoredBody("Carrier", new Vector2(-5, -2), PlatformColor);
        carrier.useGravity = false;
        carrier.velocity = Vector2.zero;

        // An ordinary falling body until it is grabbed - this one keeps its gravity on purpose.
        KinematicMotion2D item = SpawnColoredBody("Item", new Vector2(-5, 2f), ObstacleColor);

        CreateHud("Grab, carry, release - EXPECTED: the item holds its offset while held (no sinking), then resumes " +
                  "falling from wherever it was let go.",
                  carrier, null, item);

        yield return Phase("Item falling towards the carrier", 1.5f);

        item.AttachTo(carrier);
        yield return Phase("GRABBED - the item should freeze relative to the carrier, not keep sinking", 2.5f);

        carrier.velocity = new Vector2(1.2f, 0);
        yield return Phase("Carrying RIGHT at 1.2 - the item should travel locked to the carrier", 4f);

        item.Detach();
        yield return Phase("RELEASED mid-travel - the item should drop away and land on the ground", 3f);

        carrier.velocity = Vector2.zero;
        yield return Phase("Carrier stopped, item at rest on the ground", 2f);

        AssertStillInPlay(carrier);
    }

    // One-way platforms: the classic platformer move. Jump up through the deck, then land on it.
    [UnityTest, Explicit, Category("VisualInspection")]
    public IEnumerator Visual_OneWayPlatform_JumpUpThroughAndLandOn()
    {
        GameObject platform = CreateStaticObstacle("OneWayPlatform", new Vector2(0, 0), new Vector2(6f, 0.4f));
        PlatformEffector2D effector = platform.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true;
        platform.GetComponent<Collider2D>().usedByEffector = true;
        spawned.Add(platform);

        KinematicMotion2D body = SpawnRider(new Vector2(0, -2f));

        CreateHud("One-way platform - EXPECTED: the body rises straight through the deck, then falls back and lands " +
                  "ON it. Watch the 'grounded' flag during the upward pass: it should stay False.",
                  body, null, null);

        yield return Phase("Falling onto the scene ground below the platform", 2f);

        body.velocity = new Vector2(0, 9f);
        yield return Phase("Launched UP - should pass straight through the one-way deck without being stopped or shoved back", 1.5f);

        yield return Phase("Falling back down - should now LAND on the deck it just passed through", 3f);

        AssertStillInPlay(body);
    }
}
