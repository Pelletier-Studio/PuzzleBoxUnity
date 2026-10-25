using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PuzzleBox.Tests
{
    /// <summary>
    /// Staging and assertions shared by the KinematicMotion2D suites.
    ///
    /// Every scene is built around a 1x1 box body at the origin with no gravity. Surfaces are boxes
    /// placed at an exact gap from the body (see <see cref="AddSurface"/>), so every test starts from a
    /// valid, non-overlapping state. Surfaces are usually static, but may also be moving platforms
    /// (<see cref="AddPlatform"/>) or the top of another body (<see cref="SpawnRiderOn"/>). Bodies only
    /// advance when a test calls <see cref="Step"/>, and no step may leave any body's margin
    /// overlapping any surface.
    ///
    /// Gaps are measured geometrically, from a body's box to each surface's face plane, within the
    /// length of the face. That is exact here because surfaces only ever meet in concave corners, so a
    /// body never nears the end of a face it is resting on. Physics2D.Distance is not used: it reports
    /// the gap minus Box2D's polygon skin (0.02 for two boxes), whereas KinematicMotion2D's margin is a
    /// geometric gap.
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
        // Velocity tolerance, as a fraction of the launch speed (or of 1, for slower launches).
        protected const float RelativeVelocityTolerance = 1e-3f;
        protected const float PositionTolerance = 1e-3f;

        const float SurfaceLength = 40f;
        const float SurfaceThickness = 1f;
        const int MaxFramesPerStep = 100;

        /// <summary>
        /// A surface: its collider plus the face the bodies see. The face moves with the collider, so
        /// this works for static geometry and moving platforms alike.
        /// </summary>
        protected sealed class Surface
        {
            public BoxCollider2D collider;
            public Vector2 normal;
            // Centre of the face, relative to the collider's body (or transform, when static).
            public Vector2 faceOffset;
            // Half the face's length. Bodies beyond its ends are not constrained by it.
            public float halfLength;
            // The body this surface belongs to, if any. A body is never measured against its own surface.
            public SteppedKinematicMotion2D owner;

            public Vector2 facePoint
            {
                get
                {
                    Rigidbody2D rb = collider.attachedRigidbody;
                    return (rb != null ? rb.position : (Vector2)collider.transform.position) + faceOffset;
                }
            }

            public string name => collider.name;
        }

        public enum PlatformOrder
        {
            // The platform's FixedUpdate runs before its riders'.
            PlatformFirst,
            // The platform's FixedUpdate runs after its riders'.
            PlatformLast,
        }

        /// <summary>What the body holds: an attached 1x1 child, placed by <see cref="HoldToward"/>.</summary>
        public enum Holding
        {
            Nothing,
            // Between the body and the surface: the child is what touches the surface.
            Leading,
            // On the far side of the body from the surface.
            Trailing,
            // To the body's right, level with it.
            Beside,
        }

        const float HeldGap = 0.05f;

        protected SteppedKinematicMotion2D body;
        protected BoxCollider2D bodyCollider;
        // The box that surfaces are placed against and measured from: the body, or a child it holds out
        // in front of it. Velocity and displacement are still the body's: it is what simulates.
        protected SteppedKinematicMotion2D contactBody;
        protected readonly List<Surface> surfaces = new List<Surface>();

        // Children attached through this fixture, with their offset from their root, which must never change.
        readonly List<(SteppedKinematicMotion2D child, Vector2 offset)> heldOffsets = new List<(SteppedKinematicMotion2D, Vector2)>();

        // Everything that takes steps: the body, platforms and extra riders.
        readonly List<SteppedKinematicMotion2D> steppedBodies = new List<SteppedKinematicMotion2D>();
        // The bodies whose margins are checked against every surface, every other body and every
        // dynamic box, with their colliders.
        readonly Dictionary<SteppedKinematicMotion2D, BoxCollider2D> riders = new Dictionary<SteppedKinematicMotion2D, BoxCollider2D>();
        // Dynamic boxes, which the bodies must keep their margin from.
        readonly List<BoxCollider2D> dynamicBoxes = new List<BoxCollider2D>();
        readonly List<GameObject> spawned = new List<GameObject>();
        float launchSpeed;
        float savedFixedDeltaTime;
        SimulationMode2D savedSimulationMode;

        protected float margin => body.margin;

        // The overlap tolerance the per-step checks use. A test may relax it where the physics engine,
        // not KinematicMotion2D, decides how close things get. Reset before every test.
        protected float overlapTolerance;

        [SetUp]
        public void SetUpPhysics()
        {
            overlapTolerance = OverlapTolerance;
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
            steppedBodies.Clear();
            riders.Clear();
            dynamicBoxes.Clear();
            heldOffsets.Clear();
            body = null;
            bodyCollider = null;
            contactBody = null;

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
        /// CollideAndSlideSingleSurfaceTests.WithoutGravity covers on its own. Tests that need real
        /// gravity pass a <paramref name="gravityMultiplier"/>.
        /// </summary>
        protected IEnumerator SpawnBody(bool useGravity = true, float gravityMultiplier = 0f)
        {
            body = CreateRider("Body", Vector2.zero, gravityMultiplier);
            body.useGravity = useGravity;
            bodyCollider = riders[body];
            contactBody = body;
            yield return Settle();
        }

        /// <summary>
        /// Attaches a new 1x1 body to <paramref name="parent"/> (the body by default) at
        /// <paramref name="offset"/> from it. From then on every step checks that it keeps its offset from
        /// the root of its attachment chain. Call <see cref="Settle"/> before stepping.
        /// </summary>
        protected SteppedKinematicMotion2D AttachChild(Vector2 offset, string name = "Held", SteppedKinematicMotion2D parent = null)
        {
            parent = parent != null ? parent : body;
            SteppedKinematicMotion2D child = CreateRider(name, parent.position + offset, 0f);
            Attach(child, parent);
            return child;
        }

        /// <summary>
        /// Attaches <paramref name="child"/> to <paramref name="parent"/>. From then on every step checks
        /// that it keeps its current offset from the root of its attachment chain.
        /// </summary>
        protected void Attach(SteppedKinematicMotion2D child, KinematicMotion2D parent)
        {
            child.AttachTo(parent);
            heldOffsets.Add((child, child.position - child.attachedTo.position));
        }

        /// <summary>
        /// Makes the body hold a child as <paramref name="holding"/> describes, relative to a surface with
        /// the given normal, and returns it (null for <see cref="Holding.Nothing"/>). The child goes one box
        /// plus a small gap away along the axis closest to the surface direction, so the two boxes never
        /// overlap, even next to tilted surfaces. A leading child becomes the <see cref="contactBody"/>.
        /// Call this before adding surfaces, then <see cref="Settle"/>.
        /// </summary>
        protected SteppedKinematicMotion2D HoldToward(Holding holding, Vector2 normal)
        {
            Vector2 axis;
            switch (holding)
            {
                case Holding.Leading:
                    axis = ClosestAxis(-normal);
                    break;
                case Holding.Trailing:
                    axis = ClosestAxis(normal);
                    break;
                case Holding.Beside:
                    axis = Vector2.right;
                    break;
                default:
                    return null;
            }

            SteppedKinematicMotion2D child = AttachChild(axis * (1f + HeldGap), $"Held{holding}");
            if (holding == Holding.Leading)
            {
                contactBody = child;
            }
            return child;
        }

        static Vector2 ClosestAxis(Vector2 direction)
        {
            return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
                ? new Vector2(Mathf.Sign(direction.x), 0f)
                : new Vector2(0f, Mathf.Sign(direction.y));
        }

        /// <summary>The root of a body's attachment chain: the body itself when unattached.</summary>
        protected static KinematicMotion2D RootOf(KinematicMotion2D motion)
        {
            return motion.attachedTo != null ? motion.attachedTo : motion;
        }

        static bool SameGroup(KinematicMotion2D a, KinematicMotion2D b)
        {
            return RootOf(a) == RootOf(b);
        }

        /// <summary>
        /// Creates a second 1x1 body directly beneath <paramref name="above"/>, <paramref name="gap"/>
        /// below it, and registers its top face as a surface <paramref name="above"/> can stand on.
        /// Call <see cref="Settle"/> before stepping.
        /// </summary>
        protected SteppedKinematicMotion2D SpawnRiderOn(SteppedKinematicMotion2D above, float gap)
        {
            Vector2 position = above.position - Vector2.up * (Support(Vector2.up, riders[above].size) + gap + 0.5f);
            SteppedKinematicMotion2D rider = CreateRider($"Rider{riders.Count}", position, above.gravityMultiplier);
            surfaces.Add(new Surface
            {
                collider = riders[rider],
                normal = Vector2.up,
                faceOffset = Vector2.up * 0.5f,
                halfLength = 0.5f,
                owner = rider,
            });
            return rider;
        }

        /// <summary>
        /// Creates another 1x1 body at <paramref name="position"/>: it steps with the others and must
        /// keep its margin from everything, like the body. Call <see cref="Settle"/> before stepping.
        /// </summary>
        protected SteppedKinematicMotion2D SpawnBodyAt(string name, Vector2 position, float gravityMultiplier = 0f)
        {
            return CreateRider(name, position, gravityMultiplier);
        }

        /// <summary>
        /// Adds a dynamic box with frozen rotation. Bodies must keep their margin from it, unless
        /// <paramref name="checkMargin"/> is false: a dynamic body that moves into a body comes to rest at
        /// the physics engine's own contact distance, which the body cannot help.
        /// Call <see cref="Settle"/> (or <see cref="Simulate"/>) before stepping.
        /// </summary>
        protected Rigidbody2D AddDynamicBox(string name, Vector2 position, float mass = 1f, float gravityScale = 1f, bool checkMargin = true)
        {
            GameObject go = new GameObject(name);
            go.transform.position = position;
            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.mass = mass;
            rb.gravityScale = gravityScale;
            rb.freezeRotation = true;
            BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
            collider.size = Vector2.one;
            spawned.Add(go);
            if (checkMargin)
            {
                dynamicBoxes.Add(collider);
            }
            return rb;
        }

        protected BoxCollider2D ColliderOf(SteppedKinematicMotion2D rider)
        {
            return riders[rider];
        }

        /// <summary>Removes a surface from the overlap checks, for surfaces bodies are meant to pass through.</summary>
        protected void IgnoreForOverlap(Surface surface)
        {
            surfaces.Remove(surface);
        }

        /// <summary>Lets the physics engine run <paramref name="fixedSteps"/> steps without stepping any body.</summary>
        protected IEnumerator Simulate(int fixedSteps)
        {
            for (int i = 0; i < fixedSteps; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        SteppedKinematicMotion2D CreateRider(string name, Vector2 position, float gravityMultiplier)
        {
            GameObject go = new GameObject(name);
            go.SetActive(false);
            go.transform.position = position;
            go.AddComponent<Rigidbody2D>();
            BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
            collider.size = Vector2.one;
            SteppedKinematicMotion2D rider = go.AddComponent<SteppedKinematicMotion2D>();
            rider.gravityMultiplier = gravityMultiplier;
            spawned.Add(go);
            steppedBodies.Add(rider);
            riders[rider] = collider;
            go.SetActive(true);
            return rider;
        }

        /// <summary>Lets Awake/Start run for everything created so far.</summary>
        protected IEnumerator Settle()
        {
            yield return null;
            Physics2D.SyncTransforms();
        }

        /// <summary>
        /// Adds a static surface with the given normal angle (see <see cref="SlideRules"/>) whose face
        /// lies exactly <paramref name="gap"/> from the body. By default it is long and centred on the
        /// body; <paramref name="length"/> and <paramref name="tangentOffset"/> make a shorter face,
        /// shifted along itself (towards the face's right, seen from the body), and
        /// <paramref name="thickness"/> a thinner box behind it. The gap is measured from
        /// <see cref="contactBody"/>, or from <paramref name="from"/> when given.
        /// </summary>
        protected Surface AddSurface(float angleDegrees, float gap, float length = SurfaceLength, float tangentOffset = 0f,
            float thickness = SurfaceThickness, SteppedKinematicMotion2D from = null)
        {
            from = from != null ? from : contactBody;
            Vector2 normal = SlideRules.Normal(angleDegrees);
            AssertClassificationPrecondition(normal, SurfaceName(angleDegrees));
            Assert.GreaterOrEqual(gap, margin - OverlapTolerance,
                $"Precondition: {SurfaceName(angleDegrees)} must not start inside the body's margin");

            Vector2 tangent = SlideRules.Tangent(normal);
            Vector2 face = from.position - normal * (Support(normal, riders[from].size) + gap) + tangent * tangentOffset;

            GameObject go = new GameObject(SurfaceName(angleDegrees));
            go.transform.SetPositionAndRotation(face - normal * (thickness * 0.5f),
                Quaternion.Euler(0f, 0f, -angleDegrees));
            Surface surface = new Surface
            {
                collider = go.AddComponent<BoxCollider2D>(),
                normal = normal,
                faceOffset = normal * (thickness * 0.5f),
                halfLength = length * 0.5f,
            };
            surface.collider.size = new Vector2(length, thickness);
            spawned.Add(go);
            surfaces.Add(surface);
            Physics2D.SyncTransforms();
            return surface;
        }

        /// <summary>
        /// Adds a moving platform: a KinematicMotion2D without gravity whose top face, with the given
        /// normal angle, lies exactly <paramref name="gap"/> below <paramref name="on"/> (the body by
        /// default). Give it a velocity to move it. Call <see cref="Settle"/> before stepping.
        /// </summary>
        protected Surface AddPlatform(float angleDegrees, float gap, PlatformOrder order, float width = 8f,
            float thickness = 0.5f, SteppedKinematicMotion2D on = null)
        {
            on = on != null ? on : body;
            Vector2 normal = SlideRules.Normal(angleDegrees);
            AssertClassificationPrecondition(normal, SurfaceName(angleDegrees));
            Vector2 face = on.position - normal * (Support(normal, riders[on].size) + gap);

            GameObject go = new GameObject($"Platform{angleDegrees:+0;-0;0}");
            go.SetActive(false);
            go.transform.SetPositionAndRotation(face - normal * (thickness * 0.5f), Quaternion.Euler(0f, 0f, -angleDegrees));
            // Kinematic straight away: a dynamic body would start falling before Start() runs.
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(width, thickness);
            SteppedKinematicMotion2D platform = order == PlatformOrder.PlatformFirst
                ? (SteppedKinematicMotion2D)go.AddComponent<EarlyPlatform>()
                : go.AddComponent<LatePlatform>();
            platform.useGravity = false;
            platform.gravityMultiplier = 0f;
            spawned.Add(go);
            steppedBodies.Add(platform);
            go.SetActive(true);

            Surface surface = new Surface
            {
                collider = collider,
                normal = normal,
                faceOffset = normal * (thickness * 0.5f),
                halfLength = width * 0.5f,
                owner = platform,
            };
            surfaces.Add(surface);
            return surface;
        }

        void AssertClassificationPrecondition(Vector2 normal, string name)
        {
            SurfaceKind kind = SlideRules.Classify(normal);
            Assert.AreEqual(kind == SurfaceKind.Ground, body.IsGroundNormal(normal),
                $"Precondition: IsGroundNormal disagrees with the {kind} classification of {name}");
            Assert.AreEqual(kind == SurfaceKind.Wall, body.IsWallNormal(normal),
                $"Precondition: IsWallNormal disagrees with the {kind} classification of {name}");
            Assert.AreEqual(kind == SurfaceKind.Ceiling, body.IsCeilingNormal(normal),
                $"Precondition: IsCeilingNormal disagrees with the {kind} classification of {name}");
        }

        protected void AssertGroundedPrecondition(bool expected)
        {
            AssertGroundedPrecondition(body, expected);
        }

        /// <summary>
        /// Checks <paramref name="rider"/>'s grounded state. As a side effect this registers it on the
        /// platform it stands on, so it is carried from the very first step whatever the update order.
        /// </summary>
        protected void AssertGroundedPrecondition(SteppedKinematicMotion2D rider, bool expected)
        {
            rider.UpdateGroundedState();
            Assert.AreEqual(expected, rider.isGrounded,
                $"Precondition: {rider.name} should start {(expected ? "grounded" : "airborne")}");
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
        /// Runs exactly one FixedUpdate of every stepped body, waits for the physics step that follows,
        /// and checks that no body's margin overlaps anything.
        /// </summary>
        protected IEnumerator Step()
        {
            var targets = new Dictionary<SteppedKinematicMotion2D, int>();
            foreach (SteppedKinematicMotion2D stepped in steppedBodies)
            {
                targets[stepped] = stepped.stepsTaken + 1;
                stepped.pendingSteps = 1;
            }
            for (int frames = 0; !AllStepped(targets); frames++)
            {
                if (frames > MaxFramesPerStep)
                {
                    Assert.Fail("Precondition: a body never ran its FixedUpdate");
                }
                yield return new WaitForFixedUpdate();
            }
            AssertNoOverlap($"After step {body.stepsTaken}");
            AssertHeldRigid($"After step {body.stepsTaken}");
        }

        /// <summary>Checks that every attached child is still at its offset from the root of its chain.</summary>
        protected void AssertHeldRigid(string context)
        {
            foreach ((SteppedKinematicMotion2D child, Vector2 offset) in heldOffsets)
            {
                KinematicMotion2D root = child.attachedTo;
                if (root == null)
                {
                    continue;
                }
                Vector2 actual = child.position - root.position;
                if ((actual - offset).magnitude > PositionTolerance)
                {
                    Assert.Fail($"{context}: {child.name} should stay at {Format(offset)} from {root.name}, but is at {Format(actual)}");
                }
            }
        }

        static bool AllStepped(Dictionary<SteppedKinematicMotion2D, int> targets)
        {
            foreach (KeyValuePair<SteppedKinematicMotion2D, int> target in targets)
            {
                if (target.Key.stepsTaken < target.Value)
                {
                    return false;
                }
            }
            return true;
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

        /// <summary>Half a box's extent along <paramref name="direction"/>: how far its support point sits from its centre.</summary>
        static float Support(Vector2 direction, Vector2 size)
        {
            return 0.5f * (Mathf.Abs(direction.x) * size.x + Mathf.Abs(direction.y) * size.y);
        }

        protected float Gap(Surface surface)
        {
            return Gap(contactBody, surface);
        }

        /// <summary>
        /// The gap between <paramref name="rider"/> and <paramref name="surface"/>'s face, or infinity
        /// when the rider is beyond the ends of the face.
        /// </summary>
        protected float Gap(SteppedKinematicMotion2D rider, Surface surface)
        {
            Vector2 size = riders[rider].size;
            Vector2 offset = rider.position - surface.facePoint;
            Vector2 tangent = SlideRules.Tangent(surface.normal);
            if (Mathf.Abs(Vector2.Dot(offset, tangent)) > surface.halfLength + Support(tangent, size))
            {
                return float.PositiveInfinity;
            }
            return Vector2.Dot(offset, surface.normal) - Support(surface.normal, size);
        }

        /// <summary>
        /// The gap between <paramref name="rider"/> and the box <paramref name="other"/>: the largest of
        /// the gaps to the planes of the box's four faces. That is exact whenever the two boxes face each
        /// other across one of those faces, which is the case for every contact in these tests.
        /// </summary>
        protected float Gap(SteppedKinematicMotion2D rider, BoxCollider2D other)
        {
            Vector2 size = riders[rider].size;
            Rigidbody2D rb = other.attachedRigidbody;
            Vector2 center = rb != null ? rb.position : (Vector2)other.transform.position;
            float angle = (rb != null ? rb.rotation : other.transform.eulerAngles.z) * Mathf.Deg2Rad;
            Vector2 right = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 up = new Vector2(-right.y, right.x);
            Vector2 half = other.size * 0.5f;
            Vector2 offset = rider.position - center;

            float gap = float.NegativeInfinity;
            foreach ((Vector2 normal, float extent) in new[] { (right, half.x), (-right, half.x), (up, half.y), (-up, half.y) })
            {
                gap = Mathf.Max(gap, Vector2.Dot(offset, normal) - extent - Support(normal, size));
            }
            return gap;
        }

        protected void AssertNoOverlap(string context)
        {
            foreach (KeyValuePair<SteppedKinematicMotion2D, BoxCollider2D> rider in riders)
            {
                foreach (Surface surface in surfaces)
                {
                    // A body is never measured against its own surface, or one of its own attachment group.
                    if (surface.owner != null && SameGroup(surface.owner, rider.Key))
                    {
                        continue;
                    }
                    float gap = Gap(rider.Key, surface);
                    if (gap < rider.Key.margin - overlapTolerance)
                    {
                        Assert.Fail($"{context}: the margin of {rider.Key.name} overlaps {surface.name} (gap {gap:F5}, margin {rider.Key.margin:F5}, {rider.Key.name} at {Format(rider.Key.position)})");
                    }
                }

                foreach (KeyValuePair<SteppedKinematicMotion2D, BoxCollider2D> other in riders)
                {
                    // Members of one attachment group may touch each other.
                    if (!SameGroup(other.Key, rider.Key))
                    {
                        AssertClear(rider.Key, other.Value, context);
                    }
                }

                foreach (BoxCollider2D box in dynamicBoxes)
                {
                    AssertClear(rider.Key, box, context);
                }
            }
        }

        void AssertClear(SteppedKinematicMotion2D rider, BoxCollider2D other, string context)
        {
            float gap = Gap(rider, other);
            if (gap < rider.margin - overlapTolerance)
            {
                Assert.Fail($"{context}: the margin of {rider.name} overlaps {other.name} (gap {gap:F5}, margin {rider.margin:F5}, {rider.name} at {Format(rider.position)})");
            }
        }

        protected void AssertInContact(Surface surface, string context)
        {
            AssertInContact(contactBody, surface, context);
        }

        protected void AssertInContact(SteppedKinematicMotion2D rider, Surface surface, string context)
        {
            float gap = Gap(rider, surface);
            if (gap > rider.margin + ContactBand)
            {
                Assert.Fail($"{context}: {rider.name} should rest against {surface.name}, but its gap is {gap:F5} (margin {rider.margin:F5}, {rider.name} at {Format(rider.position)})");
            }
        }

        protected void AssertVelocity(Vector2 expected, string context)
        {
            AssertVelocity(expected, body.velocity, context);
        }

        protected void AssertVelocity(Vector2 expected, Vector2 actual, string context)
        {
            if ((actual - expected).magnitude > RelativeVelocityTolerance * Mathf.Max(launchSpeed, 1f))
            {
                Assert.Fail($"{context}: expected velocity {Format(expected)}, got {Format(actual)}");
            }
        }

        protected void AssertDisplacement(Vector2 from, Vector2 expected, float tolerance, string context)
        {
            AssertDisplacement(body, from, expected, tolerance, context);
        }

        protected void AssertDisplacement(KinematicMotion2D mover, Vector2 from, Vector2 expected, float tolerance, string context)
        {
            Vector2 actual = mover.position - from;
            if ((actual - expected).magnitude > tolerance)
            {
                Assert.Fail($"{context}: expected {mover.name} to move by {Format(expected)}, but it moved by {Format(actual)}");
            }
        }
    }
}
