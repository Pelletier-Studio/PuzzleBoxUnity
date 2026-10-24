using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PuzzleBox.Tests
{
    /// <summary>
    /// Staging and assertions shared by the collide-and-slide suites.
    ///
    /// Every scene is built around a 1x1 box body at the origin with no gravity. Surfaces are long,
    /// static BoxCollider2Ds placed at an exact gap from the body (see <see cref="AddSurface"/>), so
    /// every test starts from a valid, non-overlapping state. The body only advances when a test calls
    /// <see cref="Step"/>, and no step may leave its margin overlapping any surface.
    ///
    /// Gaps are measured geometrically, from the body's box to each surface's face plane. That is exact
    /// here because surfaces are long and only ever meet in concave corners, so the body never nears a
    /// surface's end. Physics2D.Distance is not used: it reports the gap minus Box2D's polygon skin
    /// (0.02 for two boxes), whereas KinematicMotion2D's margin is a geometric gap.
    ///
    /// Assertion messages starting with "Precondition:" mean the scene was not built as intended.
    /// They point at the harness, not at KinematicMotion2D.
    /// </summary>
    public abstract class CollideAndSlideFixture
    {
        protected const float FixedDeltaTime = 0.02f;
        protected const int SteadyStateSteps = 5;

        // How far a gap may dip below margin before it counts as an overlap.
        //
        // This is looser than float rounding alone would need, because KinematicMotion2D finds
        // contacts with Box2D shape casts, and Box2D stops a shape cast within a tolerance of the
        // contact rather than exactly at it. Measured: a box corner approaching a wall tilted 20
        // degrees from vertical, from about 0.03 away, was reported 0.0008 beyond the true contact
        // along the normal, so the body came to rest at 0.0192 instead of 0.02. Head-on contacts with
        // axis-aligned faces come back exact. The margin exists to keep colliders apart, and an error
        // of this size still leaves 95% of it, so we accept it as engine noise.
        protected const float OverlapTolerance = 1e-3f;
        // A body is resting against a surface when its gap is within this much above margin.
        protected const float ContactBand = 0.01f;
        // Velocity tolerance, as a fraction of the launch speed.
        protected const float RelativeVelocityTolerance = 1e-3f;
        protected const float PositionTolerance = 1e-3f;

        const float SurfaceLength = 40f;
        const float SurfaceThickness = 1f;
        const int MaxFramesPerStep = 100;

        /// <summary>A static surface: its collider plus the plane of the face the body sees.</summary>
        protected sealed class Surface
        {
            public BoxCollider2D collider;
            public Vector2 normal;
            public Vector2 facePoint;
            public string name => collider.name;
        }

        protected SteppedKinematicMotion2D body;
        protected BoxCollider2D bodyCollider;
        protected readonly List<Surface> surfaces = new List<Surface>();

        readonly List<GameObject> spawned = new List<GameObject>();
        float launchSpeed;
        float savedFixedDeltaTime;
        SimulationMode2D savedSimulationMode;

        protected float margin => body.margin;

        [SetUp]
        public void SetUpPhysics()
        {
            savedFixedDeltaTime = Time.fixedDeltaTime;
            savedSimulationMode = Physics2D.simulationMode;
            Time.fixedDeltaTime = FixedDeltaTime;
            Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
        }

        [UnityTearDown]
        public IEnumerator TearDownScene()
        {
            foreach (GameObject go in spawned)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }
            spawned.Clear();
            surfaces.Clear();
            body = null;
            bodyCollider = null;

            Time.fixedDeltaTime = savedFixedDeltaTime;
            Physics2D.simulationMode = savedSimulationMode;
            yield return null;
        }

        protected static string Format(Vector2 v)
        {
            return $"({v.x:F4}, {v.y:F4})";
        }

        protected static string SurfaceName(float angleDegrees)
        {
            return $"{SlideRules.Classify(SlideRules.Normal(angleDegrees))}{angleDegrees:+0;-0;0}";
        }

        // ---- Staging ----

        /// <summary>
        /// Creates the body at the origin and lets Awake/Start run. It does not step yet.
        ///
        /// Gravity is cancelled with gravityMultiplier = 0 rather than useGravity = false, so the rest of
        /// the suite does not hinge on the useGravity = false path, which
        /// CollideAndSlideSingleSurfaceTests.WithoutGravity covers on its own.
        /// </summary>
        protected IEnumerator SpawnBody(bool useGravity = true)
        {
            GameObject go = new GameObject("Body");
            go.SetActive(false);
            go.AddComponent<Rigidbody2D>();
            bodyCollider = go.AddComponent<BoxCollider2D>();
            bodyCollider.size = Vector2.one;
            body = go.AddComponent<SteppedKinematicMotion2D>();
            body.useGravity = useGravity;
            body.gravityMultiplier = 0f;
            spawned.Add(go);

            go.SetActive(true);
            yield return null;
            Physics2D.SyncTransforms();
        }

        /// <summary>
        /// Adds a static surface with the given normal angle (see <see cref="SlideRules"/>) whose face
        /// lies exactly <paramref name="gap"/> from the body.
        /// </summary>
        protected Surface AddSurface(float angleDegrees, float gap)
        {
            Vector2 normal = SlideRules.Normal(angleDegrees);
            SurfaceKind kind = SlideRules.Classify(normal);
            Assert.AreEqual(kind == SurfaceKind.Ground, body.IsGroundNormal(normal),
                $"Precondition: IsGroundNormal disagrees with the {kind} classification of {SurfaceName(angleDegrees)}");
            Assert.AreEqual(kind == SurfaceKind.Wall, body.IsWallNormal(normal),
                $"Precondition: IsWallNormal disagrees with the {kind} classification of {SurfaceName(angleDegrees)}");
            Assert.AreEqual(kind == SurfaceKind.Ceiling, body.IsCeilingNormal(normal),
                $"Precondition: IsCeilingNormal disagrees with the {kind} classification of {SurfaceName(angleDegrees)}");
            Assert.GreaterOrEqual(gap, margin - OverlapTolerance,
                $"Precondition: {SurfaceName(angleDegrees)} must not start inside the body's margin");

            Surface surface = new Surface
            {
                normal = normal,
                facePoint = body.position - normal * (Support(normal) + gap),
            };

            GameObject go = new GameObject(SurfaceName(angleDegrees));
            go.transform.SetPositionAndRotation(surface.facePoint - normal * (SurfaceThickness * 0.5f),
                Quaternion.Euler(0f, 0f, -angleDegrees));
            surface.collider = go.AddComponent<BoxCollider2D>();
            surface.collider.size = new Vector2(SurfaceLength, SurfaceThickness);
            spawned.Add(go);
            surfaces.Add(surface);
            Physics2D.SyncTransforms();
            return surface;
        }

        protected void AssertGroundedPrecondition(bool expected)
        {
            body.UpdateGroundedState();
            Assert.AreEqual(expected, body.isGrounded,
                $"Precondition: the body should start {(expected ? "grounded" : "airborne")}");
        }

        /// <summary>Checks that, moving along <paramref name="velocity"/>, <paramref name="expected"/> is the first surface the margin reaches.</summary>
        protected void AssertFirstHitPrecondition(Surface expected, Vector2 velocity)
        {
            Surface first = null;
            float firstTime = float.PositiveInfinity;
            foreach (Surface surface in surfaces)
            {
                float closingSpeed = -Vector2.Dot(velocity, surface.normal);
                // Not merely <= 0: Normal(180) is only flat to within float precision.
                if (closingSpeed <= 1e-4f * velocity.magnitude)
                {
                    continue;
                }
                float time = (Gap(surface) - margin) / closingSpeed;
                if (time < firstTime)
                {
                    first = surface;
                    firstTime = time;
                }
            }
            Assert.AreSame(expected, first,
                $"Precondition: moving {Format(velocity)}, {expected.name} should be reached first, not {(first != null ? first.name : "nothing")}");
        }

        // ---- Stepping ----

        protected void Launch(Vector2 velocity)
        {
            body.velocity = velocity;
            launchSpeed = velocity.magnitude;
        }

        /// <summary>
        /// Runs exactly one FixedUpdate of the body, waits for the physics step that follows it, and
        /// checks that the margin overlaps nothing.
        /// </summary>
        protected IEnumerator Step()
        {
            int target = body.stepsTaken + 1;
            body.pendingSteps = 1;
            for (int frames = 0; body.stepsTaken < target; frames++)
            {
                if (frames > MaxFramesPerStep)
                {
                    Assert.Fail("Precondition: the body never ran its FixedUpdate");
                }
                yield return new WaitForFixedUpdate();
            }
            AssertNoOverlap($"After step {body.stepsTaken}");
        }

        /// <summary>
        /// Steps the body <see cref="SteadyStateSteps"/> more times and checks that it keeps
        /// <paramref name="expected"/> velocity, moves by exactly that much per step and stays against
        /// <paramref name="contacts"/>.
        /// </summary>
        protected IEnumerator SteadyState(Vector2 expected, IEnumerable<Surface> contacts)
        {
            for (int i = 0; i < SteadyStateSteps; i++)
            {
                Vector2 previous = body.position;
                yield return Step();
                string context = $"Steady state, step {body.stepsTaken}";
                AssertVelocity(expected, context);
                AssertDisplacement(previous, expected * FixedDeltaTime, PositionTolerance, context);
                foreach (Surface contact in contacts)
                {
                    AssertInContact(contact, context);
                }
            }
        }

        // ---- Assertions ----

        /// <summary>Half the box's extent along <paramref name="normal"/>: how far its support point sits from its centre.</summary>
        float Support(Vector2 normal)
        {
            Vector2 size = bodyCollider.size;
            return 0.5f * (Mathf.Abs(normal.x) * size.x + Mathf.Abs(normal.y) * size.y);
        }

        protected float Gap(Surface surface)
        {
            return Vector2.Dot(body.position - surface.facePoint, surface.normal) - Support(surface.normal);
        }

        protected void AssertNoOverlap(string context)
        {
            foreach (Surface surface in surfaces)
            {
                float gap = Gap(surface);
                if (gap < margin - OverlapTolerance)
                {
                    Assert.Fail($"{context}: the margin overlaps {surface.name} (gap {gap:F5}, margin {margin:F5}, body at {Format(body.position)})");
                }
            }
        }

        protected void AssertInContact(Surface surface, string context)
        {
            float gap = Gap(surface);
            if (gap > margin + ContactBand)
            {
                Assert.Fail($"{context}: the body should rest against {surface.name}, but its gap is {gap:F5} (margin {margin:F5}, body at {Format(body.position)})");
            }
        }

        protected void AssertVelocity(Vector2 expected, string context)
        {
            AssertVelocity(expected, body.velocity, context);
        }

        protected void AssertVelocity(Vector2 expected, Vector2 actual, string context)
        {
            if ((actual - expected).magnitude > RelativeVelocityTolerance * launchSpeed)
            {
                Assert.Fail($"{context}: expected velocity {Format(expected)}, got {Format(actual)}");
            }
        }

        protected void AssertDisplacement(Vector2 from, Vector2 expected, float tolerance, string context)
        {
            Vector2 actual = body.position - from;
            if ((actual - expected).magnitude > tolerance)
            {
                Assert.Fail($"{context}: expected the body to move by {Format(expected)}, but it moved by {Format(actual)}");
            }
        }
    }
}
