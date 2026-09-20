using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PuzzleBox;

/// <summary>
/// Shared staging for the PlatformerPlayer2D test suites: character construction, bounded waiting,
/// state drivers, measurement and geometry. It extends KinematicTestFixture, so scene loading,
/// the `spawned` list, teardown, CreateStaticFloor and DocumentedDefaultMargin all come for free.
///
/// Nothing in here asserts anything about PlatformerPlayer2D's behaviour. It is staging only -
/// its own assertions are all "Staging error: ..." or "Precondition: ...", which report a broken
/// setup rather than a failed specification.
///
/// Three decisions in here are load-bearing and are explained at their definitions:
///
///   1. The character is BUILT IN CODE, not instantiated from a prefab. See BuildPlayer.
///   2. Configuration happens between AddComponent and the first yield, because
///      PlatformerPlayer2D.Start() latches several fields. See BuildPlayer / MakePlayer.
///   3. EVERY wait is bounded. See WaitForState. Several PlatformerPlayer2D states are currently
///      unreachable or un-exitable; those tests must FAIL, never hang the run.
/// </summary>
public abstract class PlatformerTestFixture : KinematicTestFixture
{
    // ------------------------------------------------------------------
    // Geometry constants
    // ------------------------------------------------------------------

    // The test character is a 1x1 box, matching TestKinematicBody2D so that distances carry over
    // from the existing suites (wall reach, ground clearance, the 0.5 half-extents in their helpers).
    protected const float CharacterHalfWidth = 0.5f;
    protected const float CharacterHalfHeight = 0.5f;

    // Wide enough for a full accelerate-to-runSpeed-and-hold measurement (runSpeed 10, so ~15 units
    // of travel) without the character reaching an edge. At 40 the floor spans +/-20 around its lane,
    // which still fits comfortably inside the 100-unit sub-lane spacing the test files use.
    protected const float FloorWidth = 40f;
    protected const float FloorThickness = 1f;

    // A floor short enough to run off the end of, for the tests about leaving the ground.
    protected const float ShortFloorWidth = 8f;

    // Tall enough that a character launched up a wall at the speeds the drivers use stays beside it.
    protected const float WallHeight = 8f;
    protected const float WallThickness = 1f;

    // ------------------------------------------------------------------
    // Tolerances
    //
    // There is no single epsilon. Each constant below has a stated physical meaning, because a
    // tolerance without a reason is just a number that gets loosened whenever a test goes red.
    // ------------------------------------------------------------------

    // One frame of gravity: 9.81 * 0.02. This is the natural unit of one-frame velocity error,
    // because KinematicMotion2D integrates gravity before motion and then rewrites `velocity`
    // from the motion that actually happened.
    protected const float GravityStep = 0.1962f;

    protected const float PositionTolerance = 0.05f;

    // Roughly one GravityStep plus slack, for speed claims measured as realized displacement.
    protected const float SpeedTolerance = 0.25f;

    // Unit-vector components: dash direction snapping, facing, jump-vector direction.
    protected const float DirectionTolerance = 0.01f;

    // Jump apex is asserted as a ONE-SIDED band [h, h + JumpHeightTolerance], never an equality.
    // Two effects push the measured apex above the requested height and both push the same way:
    // semi-implicit Euler overshoots the analytic apex by about v*dt/2, and Jump() deliberately
    // adds a further -g * jumpGravityMultiplier * dt to the launch velocity to cancel the first
    // frame's gravity (PlatformerPlayer2D.cs, in Jump()).
    protected const float JumpHeightTolerance = 0.35f;

    // ------------------------------------------------------------------
    // Timeouts
    // ------------------------------------------------------------------

    protected const float StateTimeout = 2.0f;
    protected const float DashTimeout = 1.0f;

    // The bulk "let it fall and come to rest" wait, matching the existing suites' idiom.
    protected const float SettleSeconds = 1.5f;

    // ------------------------------------------------------------------
    // Character construction
    // ------------------------------------------------------------------

    protected delegate void PlayerConfig(PlatformerPlayer2D player);

    /// <summary>
    /// Iterator methods cannot have out/ref parameters, so the staging helpers below write their
    /// results onto one of these instead. Same idiom as TestKinematicAttachment's Pair.
    /// </summary>
    protected class PlayerResult
    {
        public PlatformerPlayer2D player;
        public GameObject floor;
        public GameObject wall;

        // World y of the top surface of `floor`.
        public float groundTopY;

        // The character's position after it has settled on the floor. EVERY height and distance
        // claim is a delta against this, never against an absolute world coordinate - see the
        // note about float resolution in the lane comments of each test file.
        public Vector2 restPosition;

        // Set by the wall drivers: which side the wall is on, +1 right / -1 left.
        public float wallSide;
    }

    /// <summary>
    /// Builds the test character from scratch. This is deliberately NOT a prefab.
    ///
    /// Packages/PuzzleBox/Prefabs/Player.prefab is authored content whose serialized values differ
    /// from the class defaults in ways that would silently invert results - hangSpeed 0 (every hang
    /// test passes vacuously), maxAirJumps 0 (every air jump refused), canDash and canGrabWall both
    /// on (gated-feature tests "pass" without exercising the gate), collisionMask 55 (geometry on
    /// most layers is not solid), and no serialized `margin` or `sticky` at all.
    ///
    /// A new test prefab would reproduce the same failure mode across ~70 fields: anything added to
    /// the class later deserializes to 0 in a prefab nobody re-saves, and minJumpHeight = 0 alone
    /// makes CanJump return false and silently empties the entire jumping file. Building in code
    /// means AddComponent gives us the C# field initializers, and ApplyDocumentedDefaults names
    /// every value the suite depends on in the same assembly as the tests that read it.
    ///
    /// SYNCHRONOUS ON PURPOSE: Start() has NOT run when this returns. AddComponent runs Awake
    /// immediately and Start at the top of the next frame, and PlatformerPlayer2D.Start() latches
    /// normalGravityMultiplier from gravityMultiplier, jumpInput.duration from jumpBufferTime,
    /// facingDirection, wallDirection and state. Do not yield between AddComponent and `configure`,
    /// or those latched values are already wrong.
    /// </summary>
    protected PlatformerPlayer2D BuildPlayer(Vector2 position, PlayerConfig configure)
    {
        GameObject go = new GameObject("TestPlatformerPlayer");
        go.transform.position = position;

        Rigidbody2D body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.useFullKinematicContacts = true;

        BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(CharacterHalfWidth * 2f, CharacterHalfHeight * 2f);

        // Awake() runs here.
        PlatformerPlayer2D player = go.AddComponent<PlatformerPlayer2D>();

        ApplyDocumentedDefaults(player);
        if (configure != null)
        {
            configure(player);
        }

        spawned.Add(go);
        return player;
    }

    /// <summary>
    /// The suite's baseline character. Every value the tests depend on is named here, once.
    ///
    /// Four fields deliberately DEVIATE from the class defaults, because each one would otherwise
    /// widen the result of a test that is not about it:
    ///
    ///   jumpHeightSpeedBoost    0 (class 2)    - adds up to 2 units to any jump made while moving
    ///   jumpGroundCheckDistance 0 (class 0)    - already 0, pinned so coyote tests stay clean
    ///   maxAirJumps             0 (class 1)    - a stray second jump would rescue a failed first one
    ///   fallJumpTimeLimit       0 (class 0.05) - coyote time would grant jumps the test did not ask for
    ///
    /// Tests that own one of those fields turn it on in their own `configure` callback and say so.
    /// Jump_ClassDefaults_MatchTheDocumentedValues (in TestPlatformerJumping) asserts the component's
    /// REAL defaults, so this method cannot drift away from the class unnoticed.
    ///
    /// deathAnimationTimeoutSeconds is also shortened: the class default of 2 s, times the number of
    /// lifecycle tests, is minutes of wall-clock time for no added coverage.
    /// </summary>
    protected static void ApplyDocumentedDefaults(PlatformerPlayer2D p)
    {
        // --- KinematicMotion2D ---
        p.simulatePhysics = true;
        p.mass = 1f;
        p.useGravity = true;
        p.gravityModifier = 1f;
        p.gravityMultiplier = 1f;
        p.maxGroundAngleDegrees = 45f;
        p.maxCeilingAngleDegrees = 45f;
        p.collisionMask = ~0;

        // The class default is 0.005, but the existing suite documents why that is too small to move
        // horizontally against resting ground: at a margin below Box2D's combined polygon radius a
        // body comes to rest exactly touching its floor, and every horizontal Cast then registers a
        // distance-0 hit against it. KinematicTestFixture.DocumentedDefaultMargin is 0.02.
        p.margin = DocumentedDefaultMargin;

        p.pushable = false;
        p.pushPriority = 0;
        p.minSlideDistance = 0f;
        p.maxSpeedUp = 100f;
        p.maxSpeedDown = 100f;
        p.maxSpeedSide = 100f;
        p.useGroundMotion = true;
        p.sticky = true;
        p.velocity = Vector2.zero;
        p.lastGroundVelocity = Vector2.zero;

        // --- Movement ---
        p.faceMotionDirection = true;
        p.walkSpeed = 3f;
        p.walkAcceleration = 10f;
        p.runSpeed = 10f;
        p.runAcceleration = 10f;
        p.breakingForce = 10f;
        p.airSpeed = 2f;
        p.airAcceleration = 10f;
        p.airBreakingForce = 100f;
        p.hangSpeed = 1f;
        p.hangGravityRatio = 1f;          // 1 = no hang effect; the hang tests lower it
        p.movementInputScaling = Vector2.one;

        // --- Jumping ---
        p.minJumpHeight = 1f;
        p.maxJumpHeight = 3f;
        p.jumpAngle = 0f;
        p.jumpHeightSpeedBoost = 0f;      // DEVIATION - see the summary above
        p.jumpGroundCheckDistance = 0f;   // DEVIATION - see the summary above
        p.jumpBufferTime = 0.04f;
        p.maxAirJumps = 0;                // DEVIATION - see the summary above
        p.airJumpHeightRatio = 0.5f;
        p.fallJumpTimeLimit = 0f;         // DEVIATION - see the summary above

        // --- Wall / grab / climb jumps ---
        p.canWallJump = false;
        p.wallJumpHeightRatio = 1f;
        p.wallJumpHorizontalVelocity = 1f;
        p.canJumpWhenGrabbing = true;
        p.grabJumpHeightRatio = 1f;
        p.grabJumpHorizontalVelocity = 1f;
        p.canJumpWhenClimbing = true;
        p.climbJumpHeightRatio = 1f;

        // --- Walls ---
        p.wallCheckDistance = 0.3f;
        p.wallCheckVerticalOffset = 0f;
        p.wallSlideGravityRatio = 0.5f;
        p.wallSlideMaxSpeed = 5f;
        p.canGrabWall = false;
        p.maxWallGrabTime = 1f;
        p.wallClimbUpSpeed = 2f;
        p.wallClimbDownSpeed = 4f;
        p.climbOverEdgeJumpHeight = 1f;
        p.climbOverHorizontalVelocity = 2f;

        // --- Climbing ---
        p.canClimb = false;
        p.climbSpeedUp = 2f;
        p.climbSpeedDown = 3f;
        p.climbSpeedSide = 2f;
        p.climbAcceleration = 20f;
        p.climbBreakingForce = 50f;
        p.climbCollisionMask = ~0;

        // --- Dashing ---
        p.canDash = false;
        p.dashSpeedSide = 20f;
        p.dashSpeedUp = 20f;
        p.dashSpeedDown = 0f;
        p.dashTime = 0.2f;
        p.dashSpeedCurve = AnimationCurve.Linear(0, 1, 1, 1);
        p.minDashSpeed = 0f;
        p.dashInputFreezeTime = 0.2f;
        p.dashCoolDownTime = 2f;
        p.limitDashAngle = true;
        p.dashGravityRatio = 0f;

        // --- Other ---
        p.deathAnimationTimeoutSeconds = 0.2f;  // DEVIATION - wall-clock only, see the summary above
        p.acceptInput = true;
    }

    /// <summary>
    /// BuildPlayer plus one frame, so Start() has run by the time this returns.
    /// </summary>
    protected IEnumerator MakePlayer(Vector2 position, PlayerResult result, PlayerConfig configure = null)
    {
        result.player = BuildPlayer(position, configure);

        yield return null;   // Start() runs here.

        Assert.IsTrue(result.player != null,
            "Staging error: the character was destroyed before Start() could run.");
        Assert.AreEqual(PlatformerPlayer2D.State.Walking, result.player.state,
            $"Staging error: Start() should leave a fresh character in Walking, but it is {result.player.state}. " +
            "Either Start() did not run, or the character was already stepped.");
    }

    /// <summary>
    /// A floor in the given lane with the character settled on top of it.
    /// result.restPosition is the baseline for every subsequent height or distance claim.
    /// </summary>
    protected IEnumerator MakePlayerOnGround(float laneX, PlayerResult result, PlayerConfig configure = null,
                                             float floorWidth = FloorWidth)
    {
        result.floor = CreateStaticFloor(new Vector2(laneX, 0f), new Vector2(floorWidth, FloorThickness));
        result.floor.name = "LaneFloor";
        result.groundTopY = FloorThickness * 0.5f;

        // Dropped from just above the floor rather than placed exactly on it, so the character
        // arrives at its own resting height instead of one the test picked.
        yield return MakePlayer(new Vector2(laneX, result.groundTopY + CharacterHalfHeight + 0.1f),
                                result, configure);

        yield return Settle();

        Assert.IsTrue(result.player.isGrounded,
            $"Precondition: the character should have landed on the lane floor, but isGrounded is false " +
            $"at y {result.player.position.y:F3} (floor top {result.groundTopY:F3}).");

        result.restPosition = result.player.position;
    }

    /// <summary>
    /// The character in free air, `height` above the lane floor. The floor is still built, so a test
    /// can choose whether the character ever touches ground - which matters, because wallGrabTimer is
    /// only ever loaded while grounded.
    /// </summary>
    protected IEnumerator MakePlayerInAir(float laneX, float height, PlayerResult result,
                                          PlayerConfig configure = null, float floorWidth = FloorWidth)
    {
        result.floor = CreateStaticFloor(new Vector2(laneX, 0f), new Vector2(floorWidth, FloorThickness));
        result.floor.name = "LaneFloor";
        result.groundTopY = FloorThickness * 0.5f;

        yield return MakePlayer(new Vector2(laneX, result.groundTopY + height), result, configure);

        result.restPosition = new Vector2(laneX, result.groundTopY + CharacterHalfHeight);

        Assert.IsFalse(result.player.isGrounded,
            $"Staging error: the character was meant to start airborne {height:F3} above the floor, " +
            "but it reports grounded.");
    }

    // ------------------------------------------------------------------
    // Stepping
    // ------------------------------------------------------------------

    protected static IEnumerator Step(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            yield return new WaitForFixedUpdate();
        }
    }

    protected static IEnumerator StepSeconds(float seconds)
    {
        int steps = Mathf.CeilToInt(seconds / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
        }
    }

    protected static IEnumerator Settle()
    {
        yield return new WaitForSeconds(SettleSeconds);
    }

    // ------------------------------------------------------------------
    // Bounded waiting
    //
    // There is no unbounded `while (p.state != X) yield` ANYWHERE in this suite, and there must
    // never be one. Several PlatformerPlayer2D states are currently unreachable or un-exitable -
    // a fresh Utils.Timer reports isFinished immediately, which leaves State.Dashing surviving a
    // single FixedUpdate and leaves a never-grounded character unable to grab a wall at all. Those
    // tests have to fail with a legible message; a hung editor is not a test result.
    // ------------------------------------------------------------------

    protected static IEnumerator WaitForState(PlatformerPlayer2D p, PlatformerPlayer2D.State target,
                                              float timeoutSeconds, string why)
    {
        int steps = Mathf.CeilToInt(timeoutSeconds / Time.fixedDeltaTime);

        for (int i = 0; i < steps; i++)
        {
            if (p == null)
            {
                Assert.Fail($"Staging error: {why} - the character was destroyed after {i} frames " +
                            $"while waiting for {target}.");
                yield break;
            }

            if (p.state == target)
            {
                yield break;
            }

            yield return new WaitForFixedUpdate();
        }

        Assert.Fail($"Staging error: {why} never reached {target}; last state was " +
                    $"{(p == null ? "<destroyed>" : p.state.ToString())} after {steps} frames " +
                    $"({timeoutSeconds:F3} s).");
    }

    protected static IEnumerator WaitWhileState(PlatformerPlayer2D p, PlatformerPlayer2D.State current,
                                                float timeoutSeconds, string why)
    {
        int steps = Mathf.CeilToInt(timeoutSeconds / Time.fixedDeltaTime);

        for (int i = 0; i < steps; i++)
        {
            if (p == null || p.state != current)
            {
                yield break;
            }

            yield return new WaitForFixedUpdate();
        }

        Assert.Fail($"Staging error: {why} never left {current} after {steps} frames ({timeoutSeconds:F3} s).");
    }

    protected static IEnumerator WaitUntilGrounded(PlatformerPlayer2D p, float timeoutSeconds, string why)
    {
        int steps = Mathf.CeilToInt(timeoutSeconds / Time.fixedDeltaTime);

        for (int i = 0; i < steps; i++)
        {
            if (p != null && p.isGrounded)
            {
                yield break;
            }

            yield return new WaitForFixedUpdate();
        }

        Assert.Fail($"Staging error: {why} never became grounded after {steps} frames " +
                    $"({timeoutSeconds:F3} s); last y was {(p == null ? float.NaN : p.position.y):F3}.");
    }

    // ------------------------------------------------------------------
    // Geometry
    // ------------------------------------------------------------------

    protected GameObject CreateWall(Vector2 position, Vector2 size)
    {
        GameObject wall = CreateStaticFloor(position, size);
        wall.name = "Wall";
        return wall;
    }

    protected GameObject CreateCeiling(Vector2 position, Vector2 size)
    {
        GameObject ceiling = CreateStaticFloor(position, size);
        ceiling.name = "Ceiling";
        return ceiling;
    }

    protected GameObject CreateTriggerWall(Vector2 position, Vector2 size)
    {
        GameObject trigger = CreateStaticFloor(position, size);
        trigger.name = "TriggerWall";
        trigger.GetComponent<Collider2D>().isTrigger = true;
        return trigger;
    }

    protected GameObject CreateOneWayPlatform(Vector2 position, Vector2 size, float rotationalOffset = 0f)
    {
        GameObject platform = CreateStaticFloor(position, size);
        platform.name = "OneWayPlatform";

        PlatformEffector2D effector = platform.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true;
        effector.rotationalOffset = rotationalOffset;

        platform.GetComponent<Collider2D>().usedByEffector = true;
        return platform;
    }

    /// <summary>
    /// A wall standing beside the character, just inside wall-check reach.
    /// The wall check casts from the collider centre out to bounds.extents.x + wallCheckDistance,
    /// so the wall's near face is placed at a fraction of wallCheckDistance beyond the character's
    /// own edge rather than at an absolute gap - that way it stays in range if a test changes
    /// wallCheckDistance.
    /// </summary>
    protected GameObject CreateWallBeside(PlayerResult result, float side, float height = WallHeight)
    {
        PlatformerPlayer2D p = result.player;

        float nearFaceX = p.position.x + side * (CharacterHalfWidth + p.wallCheckDistance * 0.5f);
        float centreX = nearFaceX + side * WallThickness * 0.5f;

        result.wall = CreateWall(new Vector2(centreX, result.groundTopY + height * 0.5f),
                                 new Vector2(WallThickness, height));
        result.wallSide = side;
        return result.wall;
    }

    /// <summary>
    /// A kinematic platform that re-applies its velocity every FixedUpdate.
    /// A KinematicMotion2D rewrites `velocity` from realized motion at the end of every step, so a
    /// platform told to move once stops after a frame. The existing suites use the same driver.
    /// </summary>
    protected KinematicMotion2D CreateMovingPlatform(Vector2 position, Vector2 size, Vector2 velocity)
    {
        GameObject go = new GameObject("MovingPlatform");
        go.transform.position = position;

        Rigidbody2D body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.useFullKinematicContacts = true;

        BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
        collider.size = size;

        KinematicMotion2D motion = go.AddComponent<KinematicMotion2D>();
        motion.margin = DocumentedDefaultMargin;
        motion.useGravity = false;
        motion.sticky = true;
        motion.velocity = velocity;

        ConstantVelocityDriver driver = go.AddComponent<ConstantVelocityDriver>();
        driver.velocity = velocity;

        spawned.Add(go);
        return motion;
    }

    protected class ConstantVelocityDriver : MonoBehaviour
    {
        public Vector2 velocity;
        private KinematicMotion2D motion;

        void Awake()
        {
            motion = GetComponent<KinematicMotion2D>();
        }

        void FixedUpdate()
        {
            if (motion != null)
            {
                motion.velocity = velocity;
            }
        }
    }

    /// <summary>
    /// Adds a child collider to the character. Used by the wall-detection tests: the wall raycast
    /// excludes only the root GameObject, while the base class gathers colliders with
    /// GetComponentsInChildren, so a child collider is both part of the character's own footprint
    /// and a wall as far as UpdateWallTouchingState is concerned.
    /// </summary>
    protected static GameObject AddChildCollider(PlatformerPlayer2D p, Vector2 localOffset, Vector2 size)
    {
        GameObject child = new GameObject("ChildCollider");
        child.transform.SetParent(p.transform, false);
        child.transform.localPosition = localOffset;

        BoxCollider2D collider = child.AddComponent<BoxCollider2D>();
        collider.size = size;

        return child;
    }

    /// <summary>
    /// Must be called from inside a PlayerConfig callback: PlatformerPlayer2D caches the renderer
    /// in Start(), so one added later is never seen.
    /// </summary>
    protected static SpriteRenderer AddSpriteRenderer(PlatformerPlayer2D p)
    {
        return p.gameObject.AddComponent<SpriteRenderer>();
    }

    // ------------------------------------------------------------------
    // Input helpers
    //
    // PuzzleBox.InputValue.IsPressed and GetValue<T> understand exactly two runtime types:
    // PuzzleBox.InputValue and UnityEngine.InputSystem.InputValue. A raw bool or Vector2 yields
    // false / default(T) - the message arrives and quietly does nothing - so every send below
    // wraps its payload. Never pass null: Get<T> then falls through to a default CallbackContext.
    //
    // ORGANIZING RULE: only TestPlatformerInputAndLifecycle uses these. Every other file calls the
    // public API directly (player.Move, .Run, .GrabWall, .Jump, .Dash), which keeps the acceptInput
    // gate and the InputValue wrapper out of the other ~150 tests - so an input regression fails
    // the input file rather than the whole suite.
    // ------------------------------------------------------------------

    protected static void SendMove(PlatformerPlayer2D p, Vector2 value)
    {
        p.SendMessage("OnMove", new PuzzleBox.InputValue((object)value));
    }

    protected static void SendRun(PlatformerPlayer2D p, bool pressed)
    {
        p.SendMessage("OnRun", new PuzzleBox.InputValue((object)pressed));
    }

    protected static void SendGrabWall(PlatformerPlayer2D p, bool pressed)
    {
        p.SendMessage("OnGrabWall", new PuzzleBox.InputValue((object)pressed));
    }

    protected static void SendJump(PlatformerPlayer2D p, bool pressed)
    {
        p.SendMessage("OnJump", new PuzzleBox.InputValue((object)pressed));
    }

    // OnDash ignores its payload, but SendMessage still needs one.
    protected static void SendDash(PlatformerPlayer2D p)
    {
        p.SendMessage("OnDash", new PuzzleBox.InputValue((object)true));
    }

    // SetUserInputEnabled is private; SendMessage reaches it anyway, which is how the death path
    // exercises it without the test reaching into internals.
    protected static void SendSetUserInputEnabled(PlatformerPlayer2D p, bool enabled)
    {
        p.SendMessage("SetUserInputEnabled", enabled);
    }

    // A SendMessage issued straight after WaitForFixedUpdate lands inside the fixed-step window,
    // which is timing no real PlayerInput dispatch ever has. These land in the Update phase instead.
    protected static IEnumerator SendMoveAtUpdate(PlatformerPlayer2D p, Vector2 value)
    {
        yield return null;
        SendMove(p, value);
    }

    protected static IEnumerator SendJumpAtUpdate(PlatformerPlayer2D p, bool pressed)
    {
        yield return null;
        SendJump(p, pressed);
    }

    /// <summary>
    /// Restores acceptInput after a dash has frozen it.
    ///
    /// PerformDash starts inputFreezeTimer, whose OnStart clears acceptInput - but a freshly started
    /// Utils.Timer reports isFinished immediately, so OnEnd never fires and acceptInput is never
    /// restored. Any test step after a dash would otherwise run with input silently dead, and could
    /// go green for entirely the wrong reason.
    ///
    /// Every call site is a workaround for a defect and must name it in `reason`. Grep for this
    /// method to find them all.
    /// </summary>
    protected static void ForceRestoreInput(PlatformerPlayer2D p, string reason)
    {
        Assert.IsNotNull(reason, "ForceRestoreInput requires a reason naming the defect it works around.");
        p.acceptInput = true;
    }

    // ------------------------------------------------------------------
    // Measurement
    //
    // KinematicMotion2D ends every FixedUpdate with
    //     velocity = (rb.position - startPosition - positionAdjustment) / deltaSeconds
    // so `velocity` read after a yield is REALIZED motion, not what the character intended. Two
    // consequences the helpers below exist to handle:
    //   - to test what velocity an API set, call the API and read velocity BEFORE yielding;
    //   - to test a steady-state speed, measure displacement over time (MeasureAverageSpeed),
    //     never an instantaneous read. A character pressed against a wall reads velocity.x == 0.
    // ------------------------------------------------------------------

    protected class ApexResult
    {
        public float startY;
        public float apexY;
        public int apexFrame;

        // Frames spent with |velocity.y| below hangSpeed, for the hang-gravity tests.
        public int framesNearApex;

        // False means the measurement timed out before a descent was confirmed - report that as a
        // staging error, not as a height failure.
        public bool confirmedDescent;

        public float apexHeight { get { return apexY - startY; } }
    }

    /// <summary>
    /// Tracks the highest y reached from now on. Descent is confirmed only after THREE consecutive
    /// frames below the running maximum: a single blocked or clipped frame is not a descent.
    /// </summary>
    protected static IEnumerator MeasureApex(PlatformerPlayer2D p, float timeoutSeconds, ApexResult result)
    {
        result.startY = p.position.y;
        result.apexY = result.startY;
        result.apexFrame = 0;
        result.framesNearApex = 0;
        result.confirmedDescent = false;

        int steps = Mathf.CeilToInt(timeoutSeconds / Time.fixedDeltaTime);
        int framesBelowMax = 0;

        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();

            if (p == null)
            {
                yield break;
            }

            if (Mathf.Abs(p.velocity.y) < p.hangSpeed)
            {
                result.framesNearApex++;
            }

            float y = p.position.y;
            if (y > result.apexY)
            {
                result.apexY = y;
                result.apexFrame = i;
                framesBelowMax = 0;
            }
            else
            {
                framesBelowMax++;
                if (framesBelowMax >= 3)
                {
                    result.confirmedDescent = true;
                    yield break;
                }
            }
        }
    }

    /// <summary>
    /// A per-frame trace. The only reliable way to observe a state that lives for a single
    /// FixedUpdate, and the only honest way to assert "the dash lasted N frames".
    /// </summary>
    protected class Trace
    {
        public readonly List<PlatformerPlayer2D.State> states = new List<PlatformerPlayer2D.State>();
        public readonly List<Vector2> positions = new List<Vector2>();
        public readonly List<Vector2> velocities = new List<Vector2>();
        public readonly List<bool> grounded = new List<bool>();

        public bool Saw(PlatformerPlayer2D.State s)
        {
            return states.Contains(s);
        }

        public int Count(PlatformerPlayer2D.State s)
        {
            int n = 0;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] == s) n++;
            }
            return n;
        }

        public int ConsecutiveFrames(PlatformerPlayer2D.State s)
        {
            int best = 0;
            int run = 0;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] == s)
                {
                    run++;
                    if (run > best) best = run;
                }
                else
                {
                    run = 0;
                }
            }
            return best;
        }

        public string Describe()
        {
            List<string> parts = new List<string>();
            int i = 0;
            while (i < states.Count)
            {
                int j = i;
                while (j < states.Count && states[j] == states[i]) j++;
                parts.Add($"{states[i]} x{j - i}");
                i = j;
            }
            return string.Join(" -> ", parts);
        }
    }

    protected static IEnumerator Record(PlatformerPlayer2D p, float seconds, Trace trace)
    {
        int steps = Mathf.CeilToInt(seconds / Time.fixedDeltaTime);

        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();

            if (p == null)
            {
                yield break;
            }

            trace.states.Add(p.state);
            trace.positions.Add(p.position);
            trace.velocities.Add(p.velocity);
            trace.grounded.Add(p.isGrounded);
        }
    }

    protected class SpeedResult
    {
        public Vector2 displacement;
        public float seconds;

        public float speed { get { return seconds > 0 ? displacement.magnitude / seconds : 0f; } }
        public float horizontalSpeed { get { return seconds > 0 ? Mathf.Abs(displacement.x) / seconds : 0f; } }
        public float verticalSpeed { get { return seconds > 0 ? displacement.y / seconds : 0f; } }
    }

    /// <summary>
    /// Steady-state speed from realized displacement. The correct idiom for every "moves at
    /// walkSpeed" claim; see the region comment above for why an instantaneous velocity read is not.
    /// </summary>
    protected static IEnumerator MeasureAverageSpeed(PlatformerPlayer2D p, float seconds, SpeedResult result)
    {
        int steps = Mathf.CeilToInt(seconds / Time.fixedDeltaTime);
        Vector2 start = p.position;

        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
        }

        result.displacement = p.position - start;
        result.seconds = steps * Time.fixedDeltaTime;
    }

    // ------------------------------------------------------------------
    // State drivers
    //
    // `state` is { get; protected set; }, so a test cannot simply assign one - every state has to
    // be reached through real physics. Each driver ends in a bounded WaitForState, so a driver
    // failure reports as "Staging error: ..." and never masquerades as the test's own assertion.
    // ------------------------------------------------------------------

    protected IEnumerator DriveToWalking(float laneX, PlayerResult r, PlayerConfig cfg = null)
    {
        yield return MakePlayerOnGround(laneX, r, cfg);
        yield return WaitForState(r.player, PlatformerPlayer2D.State.Walking, StateTimeout,
                                  "a character settled on level ground");
    }

    protected IEnumerator DriveToRunning(float laneX, PlayerResult r, PlayerConfig cfg = null)
    {
        yield return MakePlayerOnGround(laneX, r, cfg);

        r.player.Run(true);
        r.player.Move(Vector2.right);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Running, StateTimeout,
                                  "a grounded character with run held");
    }

    protected IEnumerator DriveToJumping(float laneX, PlayerResult r, PlayerConfig cfg = null)
    {
        yield return MakePlayerOnGround(laneX, r, cfg);

        r.player.Jump(true);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Jumping, StateTimeout,
                                  "a grounded character that pressed jump");
    }

    protected IEnumerator DriveToFalling(float laneX, PlayerResult r, PlayerConfig cfg = null)
    {
        // High enough that the character is still descending well after Falling is confirmed.
        yield return MakePlayerInAir(laneX, 6f, r, cfg);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released in mid-air");
    }

    protected IEnumerator DriveToWallSliding(float laneX, PlayerResult r, PlayerConfig cfg = null)
    {
        yield return MakePlayerInAir(laneX, 5f, r, cfg);
        CreateWallBeside(r, 1f);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.WallSliding, StateTimeout,
                                  "a character falling beside a wall");
    }

    /// <summary>
    /// Airborne wall grab.
    ///
    /// The ground touch at the start is NOT incidental. A fresh Utils.Timer has timeLeft 0, so
    /// wallGrabTimer.isFinished is true from birth and TryGrabbingWall refuses forever; the only
    /// thing that ever loads it is the wallGrabTimer.Reset(maxWallGrabTime, false) that UpdateState
    /// runs on every grounded frame. So the character must stand on the floor once before it can
    /// hold a wall at all. Exactly one test (WallGrab_WithoutEverTouchingGround_StillGrabs) skips
    /// this deliberately, and it is expected to be red.
    ///
    /// The launch up the wall is done by writing velocity directly rather than by jumping, so that
    /// a regression in the jump machinery cannot take the wall tests down with it.
    /// </summary>
    protected IEnumerator DriveToGrabbing(float laneX, PlayerResult r, PlayerConfig cfg = null)
    {
        yield return MakePlayerOnGround(laneX, r, p =>
        {
            p.canGrabWall = true;
            if (cfg != null) cfg(p);
        });

        CreateWallBeside(r, 1f);

        // One settled frame beside the wall, so UpdateWallTouchingState has seen it and
        // wallGrabTimer has been refilled by the grounded branch of UpdateState.
        yield return Step(2);

        Assert.IsTrue(r.player.isTouchingWall,
            $"Staging error: the wall was placed {r.player.wallCheckDistance * 0.5f:F3} beyond the " +
            "character's edge but is not detected; check CreateWallBeside against wallCheckDistance.");

        // Rise clear of the floor BEFORE grabbing. A character that grabs while still within
        // groundCheckDistance of the ground is re-grounded every frame, and UpdateState refills
        // wallGrabTimer on every grounded frame - so a grab taken too low can never time out, and
        // the tests about maxWallGrabTime would pass for the wrong reason.
        r.player.velocity = new Vector2(0f, 8f);

        int steps = Mathf.CeilToInt(StateTimeout / Time.fixedDeltaTime);
        bool clear = false;
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            if (!r.player.isGrounded && r.player.position.y - r.restPosition.y > 1f)
            {
                clear = true;
                break;
            }
        }

        Assert.IsTrue(clear,
            $"Staging error: the character never rose clear of the floor beside the wall; it is at " +
            $"y {r.player.position.y:F3} against a resting {r.restPosition.y:F3}.");

        r.player.GrabWall(true);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Grabbing, StateTimeout,
                                  "a character holding grab against a wall in mid-air");
    }

    protected IEnumerator DriveToClimbingWallUp(float laneX, PlayerResult r, PlayerConfig cfg = null)
    {
        yield return DriveToGrabbing(laneX, r, cfg);

        r.player.Move(Vector2.up);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.ClimbingWallUp, StateTimeout,
                                  "a grabbing character pushing up");
    }

    protected IEnumerator DriveToClimbingWallDown(float laneX, PlayerResult r, PlayerConfig cfg = null)
    {
        yield return DriveToGrabbing(laneX, r, cfg);

        r.player.Move(Vector2.down);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.ClimbingWallDown, StateTimeout,
                                  "a grabbing character pushing down");
    }

    /// <summary>
    /// Climb up a wall and over its top edge.
    ///
    /// The wall has to be short enough that the character tops out before maxWallGrabTime expires,
    /// so its height is computed from maxWallGrabTime and wallClimbUpSpeed rather than hardcoded -
    /// otherwise changing either field in a test silently turns this driver into a grab-timeout test.
    /// </summary>
    protected IEnumerator DriveToClimbingWallOver(float laneX, PlayerResult r, PlayerConfig cfg = null)
    {
        yield return MakePlayerOnGround(laneX, r, p =>
        {
            p.canGrabWall = true;
            if (cfg != null) cfg(p);
        });

        PlatformerPlayer2D player = r.player;

        // Half the grab budget to reach the top, half as slack for the launch and the wall check.
        float climbBudget = player.maxWallGrabTime * player.wallClimbUpSpeed * 0.5f;
        float wallTopY = r.restPosition.y + CharacterHalfHeight + climbBudget;
        float wallHeight = wallTopY - r.groundTopY;

        Assert.Greater(climbBudget, 0.5f,
            $"Staging error: maxWallGrabTime ({player.maxWallGrabTime:F3}) times wallClimbUpSpeed " +
            $"({player.wallClimbUpSpeed:F3}) leaves only {climbBudget:F3} units of climb, which is not " +
            "enough to clear a wall the character can stand beside.");

        CreateWallBeside(r, 1f, wallHeight);

        yield return Step(2);

        player.GrabWall(true);
        player.Move(Vector2.up);
        player.velocity = new Vector2(0f, 2f);

        yield return WaitForState(player, PlatformerPlayer2D.State.ClimbingWallOver, StateTimeout,
                                  "a character climbing up past the top of a short wall");
    }

    protected IEnumerator DriveToWallJumping(float laneX, PlayerResult r, PlayerConfig cfg = null)
    {
        yield return DriveToGrabbing(laneX, r, p =>
        {
            p.canJumpWhenGrabbing = true;
            if (cfg != null) cfg(p);
        });

        r.player.Jump(true);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.WallJumping, StateTimeout,
                                  "a grabbing character that pressed jump");
    }

    protected IEnumerator DriveToClimbing(float laneX, PlayerResult r, PlayerConfig cfg = null)
    {
        yield return MakePlayerOnGround(laneX, r, p =>
        {
            p.canClimb = true;
            if (cfg != null) cfg(p);
        });

        r.player.Move(Vector2.up);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Climbing, StateTimeout,
                                  "a character pushing up inside a climbable area");
    }

    /// <summary>
    /// Dashing is NOT an iterator, and must not become one.
    ///
    /// dashTimer reports isFinished the instant PerformDash starts it, so UpdateState leaves
    /// State.Dashing on the very next FixedUpdate. Any driver that yields has already missed the
    /// state it was meant to deliver. Callers assert synchronously on the next line, or observe the
    /// dash through Record/Trace instead.
    /// </summary>
    protected static void EnterDashing(PlatformerPlayer2D p)
    {
        Assert.IsTrue(p.canDash,
            "Staging error: EnterDashing needs canDash enabled in the test's configure callback.");

        p.Dash();

        Assert.AreEqual(PlatformerPlayer2D.State.Dashing, p.state,
            $"Staging error: Dash() did not enter Dashing; state is {p.state}. " +
            "Check canDash and the dash cooldown.");
    }
}
