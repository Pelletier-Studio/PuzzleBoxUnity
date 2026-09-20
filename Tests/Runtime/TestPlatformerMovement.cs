using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PuzzleBox;

/// <summary>
/// SPECIFICATION - PlatformerPlayer2D horizontal motion and facing.
///
///  1. On the ground, movement input accelerates the character at walkAcceleration (or
///     runAcceleration while run is held) up to walkSpeed (or runSpeed), and no further.
///  2. Releasing movement input brakes the character to a stop at breakingForce. Braking never
///     carries the character past zero into the opposite direction.
///  3. Lowering the speed cap - by releasing run while above walkSpeed - is a deceleration, not a
///     teleport: it is bounded by breakingForce like any other slow-down.
///  4. The speed cap limits speed ALONG THE GROUND. A character walking up a slope reaches the same
///     walking speed it reaches on the flat.
///  5. Movement input below SMALL_INPUT_THRESHOLD is not movement. An analog stick resting off
///     centre must not creep the character across the floor.
///  6. movementInputScaling scales the raw input on each axis before anything else consumes it.
///  7. In the air, input accelerates at airAcceleration up to airSpeed - except that horizontal
///     momentum carried off the ground is preserved while the character keeps moving that way.
///  8. facingDirection follows horizontal input, holds its last horizontal value when input is
///     released or purely vertical, and points at the wall while the character is on one.
///
/// THIS IS A TDD RED PHASE. Several tests below are expected to fail against the current
/// implementation; each says so above its declaration, with the register ID where one exists.
/// Do not make a failing test here pass by weakening its assertion - the failures are the point.
///
/// RED LIST (expected to fail; transcribe the observed set after the first full run):
///   Running_ReleasedAboveWalkSpeed_DecelerationRespectsBreakingForce
///   GroundMotion_TinyAnalogInput_IsInsideTheDeadzoneAndDoesNotMove          PP-17
///   AirMotion_TinyAnalogInput_IsInsideTheDeadzone                           PP-17
///   Facing_PureUpInput_KeepsTheLastHorizontalFacing                         PP-20
///   GroundSpeedClamp_WalkingUpASlope_HoldsTheSameSpeedAsOnFlatGround        PP-15 (suspected)
///
/// NOT TESTED HERE, deliberately:
///   PP-18 (MoveInAir compares a pre-acceleration direction sign against a post-acceleration
///   speed) has no observable consequence at this level. The stale sign can only differ from the
///   fresh one on the single frame where velocity.x changes sign, and on that frame the character
///   is moving at most airAcceleration * fixedDeltaTime = 0.2 units/s in the new direction - far
///   below either candidate speed limit, so both limits permit the same motion. The
///   Mathf.Sign(0) == 1 half is likewise unobservable: it makes a stationary character "match" a
///   stationary lastGroundVelocity, and max(0, airSpeed) is airSpeed, which is what the
///   non-matching branch would have used anyway. A test here would assert nothing. Fix it on
///   code-reading grounds or not at all.
/// </summary>
public class TestPlatformerMovement : PlatformerTestFixture
{
    // Every test in this file is staged in its own sub-lane so that geometry from one scenario can
    // never be seen by another - Object.Destroy in TearDown only takes effect at the end of the
    // frame, and the wall raycasts do not filter by test. Floors are FloorWidth (40) wide, so
    // 50-unit spacing leaves a 10-unit gap between neighbours.
    private const float Lane = 13000f;

    private static float Slot(int n)
    {
        return Lane + n * 50f;
    }

    // ------------------------------------------------------------------
    // Staging self-checks
    //
    // These four assert that the fixture itself is sane. If they fail, nothing else in this file
    // means anything, and the bug is in PlatformerTestFixture rather than in the component.
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Character_AtRest_IsGroundedAndWalking()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(0), r);

        Assert.IsTrue(r.player.isGrounded, "A character settled on a floor should be grounded.");
        Assert.AreEqual(PlatformerPlayer2D.State.Walking, r.player.state,
            $"A grounded character with no input should be in Walking, but it is {r.player.state}.");
        Assert.AreEqual(r.groundTopY + CharacterHalfHeight, r.player.position.y, PositionTolerance,
            $"The character should rest with its bottom face on the floor top ({r.groundTopY:F3}), " +
            $"but its centre is at y {r.player.position.y:F3}.");
    }

    // The margin guard. The existing suite documents why a margin below Box2D's combined polygon
    // radius pins a resting body in place: every horizontal Cast registers a distance-0 hit against
    // the floor it is standing on. If this fails, ApplyDocumentedDefaults stopped setting margin.
    [UnityTest]
    public IEnumerator Character_AtRest_CanStartWalking()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(1), r);

        r.player.Move(Vector2.right);
        yield return StepSeconds(0.5f);

        Assert.Greater(r.player.position.x - r.restPosition.x, 0.5f,
            $"A grounded character told to walk right should have moved, but it advanced only " +
            $"{r.player.position.x - r.restPosition.x:F3} units in 0.5 s. A margin below the " +
            "documented default pins a resting body against its own floor.");
    }

    // Guards the fixture against drifting away from the component. ApplyDocumentedDefaults
    // deliberately neutralises four fields; this test reads the CLASS defaults from a character the
    // fixture has not touched, so that a change to the component's shipped values shows up here
    // rather than silently changing what the rest of the suite measures.
    [UnityTest]
    public IEnumerator Character_DefaultsMatchTheComponent()
    {
        GameObject go = new GameObject("RawPlatformerPlayer");
        go.transform.position = new Vector2(Slot(2), 50f);
        spawned.Add(go);

        Rigidbody2D body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        go.AddComponent<BoxCollider2D>();

        // No ApplyDocumentedDefaults: these are the field initialisers the class itself declares.
        PlatformerPlayer2D p = go.AddComponent<PlatformerPlayer2D>();

        Assert.AreEqual(3f, p.walkSpeed, 0.0001f, "The component's default walkSpeed changed.");
        Assert.AreEqual(10f, p.walkAcceleration, 0.0001f, "The component's default walkAcceleration changed.");
        Assert.AreEqual(10f, p.runSpeed, 0.0001f, "The component's default runSpeed changed.");
        Assert.AreEqual(10f, p.breakingForce, 0.0001f, "The component's default breakingForce changed.");
        Assert.AreEqual(2f, p.airSpeed, 0.0001f, "The component's default airSpeed changed.");
        Assert.AreEqual(10f, p.airAcceleration, 0.0001f, "The component's default airAcceleration changed.");
        Assert.AreEqual(100f, p.airBreakingForce, 0.0001f, "The component's default airBreakingForce changed.");
        Assert.AreEqual(1f, p.minJumpHeight, 0.0001f, "The component's default minJumpHeight changed.");
        Assert.AreEqual(3f, p.maxJumpHeight, 0.0001f, "The component's default maxJumpHeight changed.");
        Assert.AreEqual(0.3f, p.wallCheckDistance, 0.0001f, "The component's default wallCheckDistance changed.");

        // The four the fixture neutralises. If any of these change, revisit the DEVIATION comments
        // in ApplyDocumentedDefaults - the reason for neutralising them may no longer hold.
        Assert.AreEqual(2f, p.jumpHeightSpeedBoost, 0.0001f,
            "The component's default jumpHeightSpeedBoost changed; the fixture neutralises this field.");
        Assert.AreEqual(0f, p.jumpGroundCheckDistance, 0.0001f,
            "The component's default jumpGroundCheckDistance changed; the fixture neutralises this field.");
        Assert.AreEqual(1, p.maxAirJumps,
            "The component's default maxAirJumps changed; the fixture neutralises this field.");
        Assert.AreEqual(3f / 60f, p.fallJumpTimeLimit, 0.0001f,
            "The component's default fallJumpTimeLimit changed; the fixture neutralises this field.");

        yield return null;
    }

    // ------------------------------------------------------------------
    // Ground movement: speed caps
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Walking_WithFullRightInput_ReachesWalkSpeedAndHoldsIt()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(3), r);

        r.player.Move(Vector2.right);

        // walkSpeed / walkAcceleration = 0.3 s to reach the cap; 1 s leaves ample settling time.
        yield return StepSeconds(1f);

        SpeedResult speed = new SpeedResult();
        yield return MeasureAverageSpeed(r.player, 0.5f, speed);

        Assert.AreEqual(r.player.walkSpeed, speed.displacement.x / speed.seconds, SpeedTolerance,
            $"A walking character should settle at walkSpeed ({r.player.walkSpeed:F3}), but it " +
            $"covered {speed.displacement.x:F3} units in {speed.seconds:F3} s " +
            $"({speed.displacement.x / speed.seconds:F3} units/s).");
    }

    [UnityTest]
    public IEnumerator Walking_WithFullLeftInput_ReachesWalkSpeedInTheNegativeDirection()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(4), r);

        r.player.Move(Vector2.left);
        yield return StepSeconds(1f);

        SpeedResult speed = new SpeedResult();
        yield return MeasureAverageSpeed(r.player, 0.5f, speed);

        Assert.AreEqual(-r.player.walkSpeed, speed.displacement.x / speed.seconds, SpeedTolerance,
            $"A character walking left should settle at -walkSpeed ({-r.player.walkSpeed:F3}), but it " +
            $"moved at {speed.displacement.x / speed.seconds:F3} units/s.");
    }

    [UnityTest]
    public IEnumerator Running_WithRunHeld_ReachesRunSpeedNotWalkSpeed()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(5), r);

        r.player.Run(true);
        r.player.Move(Vector2.right);

        yield return StepSeconds(1.5f);

        SpeedResult speed = new SpeedResult();
        yield return MeasureAverageSpeed(r.player, 0.4f, speed);

        float observed = speed.displacement.x / speed.seconds;

        Assert.AreEqual(r.player.runSpeed, observed, SpeedTolerance,
            $"A running character should settle at runSpeed ({r.player.runSpeed:F3}), but it moved " +
            $"at {observed:F3} units/s.");
        Assert.Greater(observed, r.player.walkSpeed + SpeedTolerance,
            $"Running should be meaningfully faster than walking, but {observed:F3} units/s is not " +
            $"clear of walkSpeed ({r.player.walkSpeed:F3}).");
    }

    [UnityTest]
    public IEnumerator Walking_SpeedCap_IsNotExceededAfterLongAcceleration()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(6), r);

        r.player.Move(Vector2.right);

        Trace trace = new Trace();
        yield return Record(r.player, 2f, trace);

        for (int i = 0; i < trace.positions.Count - 1; i++)
        {
            float step = trace.positions[i + 1].x - trace.positions[i].x;
            float stepLimit = (r.player.walkSpeed * Time.fixedDeltaTime) + PositionTolerance;

            Assert.LessOrEqual(step, stepLimit,
                $"On step {i} the character advanced {step:F4} units in one frame, which is above " +
                $"walkSpeed * fixedDeltaTime ({r.player.walkSpeed * Time.fixedDeltaTime:F4}). " +
                "Holding input must not accumulate speed past the cap.");
        }
    }

    // ------------------------------------------------------------------
    // Ground movement: acceleration and braking
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Walking_FromRest_AccelerationMatchesWalkAcceleration()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(7), r);

        r.player.Move(Vector2.right);

        // 10 frames = 0.2 s, still below the 0.3 s it takes to reach walkSpeed, so the cap is not
        // yet involved and the measurement is about acceleration alone.
        const int frames = 10;
        yield return Step(frames);

        float elapsed = frames * Time.fixedDeltaTime;
        float expected = r.player.walkAcceleration * elapsed;

        Assert.Less(expected, r.player.walkSpeed,
            $"Staging error: this test assumes {elapsed:F3} s of acceleration stays below walkSpeed, " +
            $"but walkAcceleration * {elapsed:F3} = {expected:F3} and walkSpeed is {r.player.walkSpeed:F3}.");

        Assert.AreEqual(expected, r.player.velocity.x, SpeedTolerance,
            $"After {elapsed:F3} s of walk input the character should be moving at " +
            $"walkAcceleration * time ({expected:F3} units/s), but it is at {r.player.velocity.x:F3}.");
    }

    [UnityTest]
    public IEnumerator Walking_InputReleased_BrakesToRestAtBreakingForce()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(8), r);

        r.player.Move(Vector2.right);
        yield return StepSeconds(1f);

        float speedBeforeRelease = Mathf.Abs(r.player.velocity.x);
        Assert.Greater(speedBeforeRelease, 1f,
            $"Precondition: the character should be walking before the brake test, but it is only at " +
            $"{speedBeforeRelease:F3} units/s.");

        r.player.Move(Vector2.zero);

        // breakingForce is a deceleration in units/s^2, so stopping from `speedBeforeRelease`
        // should take speedBeforeRelease / breakingForce seconds.
        float expectedStopTime = speedBeforeRelease / r.player.breakingForce;

        // Half way through, it should still be moving.
        yield return StepSeconds(expectedStopTime * 0.5f);
        Assert.Greater(Mathf.Abs(r.player.velocity.x), 0.3f,
            $"Half way through the expected braking time ({expectedStopTime:F3} s) the character " +
            $"should still be moving, but it is already at {r.player.velocity.x:F3} units/s - " +
            "the brake is stronger than breakingForce allows.");

        // And by the end, plus a couple of frames of slack, it should have stopped.
        yield return StepSeconds(expectedStopTime * 0.5f + 4f * Time.fixedDeltaTime);
        Assert.AreEqual(0f, r.player.velocity.x, SpeedTolerance,
            $"After {expectedStopTime:F3} s of braking at breakingForce ({r.player.breakingForce:F3}) " +
            $"the character should have stopped, but it is still at {r.player.velocity.x:F3} units/s.");
    }

    [UnityTest]
    public IEnumerator Walking_BrakingNeverOvershootsPastZero()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(9), r);

        r.player.Move(Vector2.right);
        yield return StepSeconds(1f);

        r.player.Move(Vector2.zero);

        Trace trace = new Trace();
        yield return Record(r.player, 1f, trace);

        for (int i = 0; i < trace.velocities.Count; i++)
        {
            Assert.GreaterOrEqual(trace.velocities[i].x, -SpeedTolerance,
                $"On step {i} braking carried the character backwards to {trace.velocities[i].x:F4} " +
                "units/s. Releasing input should decelerate to rest, never reverse.");
        }
    }

    [UnityTest]
    public IEnumerator Walking_ReversingInput_PassesThroughZeroWithoutTeleport()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(10), r);

        r.player.Move(Vector2.right);
        yield return StepSeconds(1f);

        r.player.Move(Vector2.left);

        Trace trace = new Trace();
        yield return Record(r.player, 1.5f, trace);

        float stepLimit = (r.player.walkSpeed * Time.fixedDeltaTime) + PositionTolerance;
        for (int i = 0; i < trace.positions.Count - 1; i++)
        {
            float step = Mathf.Abs(trace.positions[i + 1].x - trace.positions[i].x);
            Assert.LessOrEqual(step, stepLimit,
                $"On step {i} the character moved {step:F4} units in one frame while reversing, " +
                $"above the walkSpeed budget of {stepLimit:F4}. Reversal should pass through zero, " +
                "not jump across it.");
        }

        Assert.Less(r.player.velocity.x, -1f,
            $"After 1.5 s of left input the character should be walking left, but it is at " +
            $"{r.player.velocity.x:F3} units/s.");
    }

    // EXPECTED RED. Releasing run while above walkSpeed lowers the cap from runSpeed to walkSpeed,
    // and ApplyGroundMotion enforces the new cap with a single `velocity += breakDirection *
    // (speed - maxSpeed)` - the whole excess is removed in one frame. Every other slow-down in the
    // class is rate-limited (the braking path uses min(breakingForce * dt, speed)), so this one is
    // inconsistent as well as physically abrupt: a character drops from runSpeed to walkSpeed
    // instantly the moment the run button comes up.
    [UnityTest]
    public IEnumerator Running_ReleasedAboveWalkSpeed_DecelerationRespectsBreakingForce()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(11), r);

        r.player.Run(true);
        r.player.Move(Vector2.right);
        yield return StepSeconds(1.5f);

        float runningSpeed = r.player.velocity.x;
        Assert.Greater(runningSpeed, r.player.walkSpeed + 1f,
            $"Precondition: the character should be running well above walkSpeed, but it is at " +
            $"{runningSpeed:F3} units/s.");

        r.player.Run(false);

        Trace trace = new Trace();
        yield return Record(r.player, 0.5f, trace);

        float maxDrop = (r.player.breakingForce * Time.fixedDeltaTime) + SpeedTolerance;
        float previous = runningSpeed;

        for (int i = 0; i < trace.velocities.Count; i++)
        {
            float drop = previous - trace.velocities[i].x;
            Assert.LessOrEqual(drop, maxDrop,
                $"On step {i} the character lost {drop:F4} units/s in a single frame while the speed " +
                $"cap dropped from runSpeed to walkSpeed. breakingForce ({r.player.breakingForce:F3}) " +
                $"allows at most {maxDrop:F4} per frame; lowering the cap should decelerate the " +
                "character, not teleport its velocity.");
            previous = trace.velocities[i].x;
        }
    }

    // ------------------------------------------------------------------
    // Ground movement: the speed clamp on sloped ground
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Walking_UpA30DegreeSlope_StaysGrounded()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayer(new Vector2(Slot(12), 3f), r);

        CreateStaticFloor(new Vector2(Slot(12), 0f), new Vector2(FloorWidth, FloorThickness), 30f);

        yield return Settle();
        Assert.IsTrue(r.player.isGrounded,
            "Precondition: the character should have landed on the 30-degree slope.");

        r.player.Move(Vector2.right);

        Trace trace = new Trace();
        yield return Record(r.player, 1.5f, trace);

        int airborneFrames = 0;
        for (int i = 0; i < trace.grounded.Count; i++)
        {
            if (!trace.grounded[i]) airborneFrames++;
        }

        Assert.Less(airborneFrames, trace.grounded.Count / 4,
            $"A character walking up a 30-degree slope should stay on it, but it was airborne for " +
            $"{airborneFrames} of {trace.grounded.Count} frames. A slope shallower than " +
            $"maxGroundAngleDegrees ({r.player.maxGroundAngleDegrees:F1}) is ground, not a ramp to " +
            "bounce off.");

        Assert.Greater(r.player.position.y, r.player.position.y - 1f,
            "Staging error: the slope scenario produced no vertical reference.");
    }

    // EXPECTED RED (suspected PP-15). The ground speed clamp compares velocity.MAGNITUDE against
    // maxSpeed but applies the correction along the ground-tangent projection. Any velocity normal
    // to the ground - which a slope produces on every frame where gravity is applied because the
    // ground hit is farther away than margin - inflates `speed` without inflating the along-ground
    // speed the cap is supposed to govern, so the correction over-subtracts and the character walks
    // up a slope slower than it walks on the flat.
    [UnityTest]
    public IEnumerator GroundSpeedClamp_WalkingUpASlope_HoldsTheSameSpeedAsOnFlatGround()
    {
        // Flat reference, measured first in its own sub-lane.
        PlayerResult flat = new PlayerResult();
        yield return MakePlayerOnGround(Slot(13), flat);

        flat.player.Move(Vector2.right);
        yield return StepSeconds(1f);

        SpeedResult flatSpeed = new SpeedResult();
        yield return MeasureAverageSpeed(flat.player, 0.5f, flatSpeed);

        // The same character on a 20-degree slope. Shallow enough that it is unambiguously ground.
        PlayerResult slope = new PlayerResult();
        yield return MakePlayer(new Vector2(Slot(14), 3f), slope);
        CreateStaticFloor(new Vector2(Slot(14), 0f), new Vector2(FloorWidth, FloorThickness), 20f);

        yield return Settle();
        Assert.IsTrue(slope.player.isGrounded,
            "Precondition: the character should have landed on the 20-degree slope.");

        slope.player.Move(Vector2.right);
        yield return StepSeconds(1f);

        SpeedResult slopeSpeed = new SpeedResult();
        yield return MeasureAverageSpeed(slope.player, 0.5f, slopeSpeed);

        Assert.AreEqual(flatSpeed.speed, slopeSpeed.speed, SpeedTolerance,
            $"The speed cap governs speed along the ground, so a character walking up a 20-degree " +
            $"slope should travel at the same speed it does on the flat. Flat: {flatSpeed.speed:F3} " +
            $"units/s; slope: {slopeSpeed.speed:F3} units/s.");
    }

    // The zero-vector half of the same clamp: when the character has no along-ground velocity,
    // `v.normalized` is the zero vector. This must not produce NaN in the position or velocity,
    // whatever else it does or does not clamp.
    [UnityTest]
    public IEnumerator GroundSpeedClamp_WithNoHorizontalVelocity_ProducesNoNaN()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(15), r);

        r.player.Move(Vector2.zero);
        r.player.velocity = new Vector2(0f, -20f);

        Trace trace = new Trace();
        yield return Record(r.player, 0.5f, trace);

        for (int i = 0; i < trace.positions.Count; i++)
        {
            Assert.IsFalse(float.IsNaN(trace.positions[i].x) || float.IsNaN(trace.positions[i].y),
                $"On step {i} the character's position became NaN ({trace.positions[i]}). " +
                "The ground speed clamp normalises a vector that is zero when there is no " +
                "along-ground velocity.");
            Assert.IsFalse(float.IsNaN(trace.velocities[i].x) || float.IsNaN(trace.velocities[i].y),
                $"On step {i} the character's velocity became NaN ({trace.velocities[i]}).");
        }
    }

    // ------------------------------------------------------------------
    // Ground movement: input deadzone and scaling
    // ------------------------------------------------------------------

    // EXPECTED RED (PP-17). The class declares SMALL_INPUT_THRESHOLD and uses it for the climb and
    // facing decisions, but ApplyGroundMotion tests `Mathf.Abs(motionInput.x) > 0`. An analog stick
    // resting a couple of percent off centre therefore accelerates the character forever and never
    // lets it brake, because the braking branch is the `else` of that same test.
    [UnityTest]
    public IEnumerator GroundMotion_TinyAnalogInput_IsInsideTheDeadzoneAndDoesNotMove()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(16), r);

        // Well below SMALL_INPUT_THRESHOLD (0.1): stick drift, not a movement request.
        r.player.Move(new Vector2(0.02f, 0f));

        yield return StepSeconds(2f);

        float drift = Mathf.Abs(r.player.position.x - r.restPosition.x);
        Assert.Less(drift, PositionTolerance,
            $"Movement input of 0.02 is below SMALL_INPUT_THRESHOLD and should be ignored, but the " +
            $"character drifted {drift:F3} units in 2 s. A stick resting off centre must not creep " +
            "the character across the floor.");
    }

    [UnityTest]
    public IEnumerator MovementInputScaling_NegativeX_InvertsHorizontalMovement()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(17), r, p => p.movementInputScaling = new Vector2(-1f, 1f));

        r.player.Move(Vector2.right);
        yield return StepSeconds(1f);

        Assert.Less(r.player.position.x - r.restPosition.x, -0.5f,
            $"With movementInputScaling.x = -1, right input should move the character left, but it " +
            $"moved {r.player.position.x - r.restPosition.x:F3} units.");
        Assert.AreEqual(-1f, r.player.motionInput.x, DirectionTolerance,
            $"movementInputScaling should be applied to motionInput itself, but motionInput.x is " +
            $"{r.player.motionInput.x:F3} for a right input of 1.");
    }

    // The vertical half of the same field. Vertical input is what selects Climbing, so inverting it
    // must invert that decision too, not just the horizontal motion.
    [UnityTest]
    public IEnumerator MovementInputScaling_NegativeY_InvertsVerticalInput()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(18), r, p =>
        {
            p.canClimb = true;
            p.movementInputScaling = new Vector2(1f, -1f);
        });

        r.player.Move(Vector2.up);
        yield return StepSeconds(0.5f);

        Assert.AreEqual(-1f, r.player.motionInput.y, DirectionTolerance,
            $"With movementInputScaling.y = -1, an up input should become a down input, but " +
            $"motionInput.y is {r.player.motionInput.y:F3}.");
        Assert.AreNotEqual(PlatformerPlayer2D.State.Climbing, r.player.state,
            "An up input inverted into a down input should not start a climb, but the character " +
            "entered Climbing.");
    }

    [UnityTest]
    public IEnumerator MovementInputScaling_Zero_SuppressesMovementEntirely()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(19), r, p => p.movementInputScaling = Vector2.zero);

        r.player.Move(Vector2.right);
        yield return StepSeconds(1.5f);

        float drift = Mathf.Abs(r.player.position.x - r.restPosition.x);
        Assert.Less(drift, PositionTolerance,
            $"With movementInputScaling set to zero the character should not move at all, but it " +
            $"travelled {drift:F3} units.");
    }

    // ------------------------------------------------------------------
    // Ground movement: moving platforms
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator GroundMotion_OnAMovingPlatform_IsCarriedAlong()
    {
        float laneX = Slot(20);

        KinematicMotion2D platform = CreateMovingPlatform(new Vector2(laneX, 0f),
                                                          new Vector2(10f, 1f), Vector2.zero);

        PlayerResult r = new PlayerResult();
        yield return MakePlayer(new Vector2(laneX, 1.1f), r);
        yield return Settle();

        Assert.IsTrue(r.player.isGrounded,
            "Precondition: the character should have landed on the platform.");

        float offsetAtRest = r.player.position.x - platform.position.x;

        platform.GetComponent<ConstantVelocityDriver>().velocity = new Vector2(2f, 0f);

        int steps = Mathf.CeilToInt(2f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();

            float offset = (r.player.position.x - platform.position.x) - offsetAtRest;
            Assert.Less(Mathf.Abs(offset), 0.5f,
                $"On step {i} the character had slipped {offset:F3} units relative to the platform " +
                "carrying it. A character standing still on a moving platform should be carried " +
                "with it, not left behind.");
        }

        Assert.Greater(platform.position.x - laneX, 3f,
            "Precondition: the platform itself should have travelled during the measurement.");
    }

    // ------------------------------------------------------------------
    // Air movement
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator AirMotion_WithInput_AcceleratesTowardAirSpeed()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(21), 12f, r);

        r.player.Move(Vector2.right);

        // airSpeed / airAcceleration = 0.2 s. Half a second leaves the cap comfortably reached.
        yield return StepSeconds(0.5f);

        Assert.AreEqual(r.player.airSpeed, r.player.velocity.x, SpeedTolerance,
            $"An airborne character holding right should accelerate to airSpeed " +
            $"({r.player.airSpeed:F3}), but it is moving at {r.player.velocity.x:F3} units/s.");
    }

    [UnityTest]
    public IEnumerator AirMotion_NoInput_HoldsHorizontalVelocity()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(22), 12f, r);

        // Below airSpeed, so nothing should be braking it.
        r.player.velocity = new Vector2(1.5f, 0f);
        r.player.Move(Vector2.zero);

        yield return StepSeconds(0.6f);

        Assert.AreEqual(1.5f, r.player.velocity.x, SpeedTolerance,
            $"There is no air braking without input below airSpeed, so a character drifting at 1.5 " +
            $"units/s should still be at 1.5, but it is at {r.player.velocity.x:F3}.");
    }

    [UnityTest]
    public IEnumerator AirMotion_AboveAirSpeed_IsBrakedBackToAirSpeed()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(23), 12f, r);

        // Well above airSpeed, with no ground momentum to justify it.
        r.player.lastGroundVelocity = Vector2.zero;
        r.player.velocity = new Vector2(10f, 0f);
        r.player.Move(Vector2.zero);

        // airBreakingForce is 100, so shedding 8 units/s takes 0.08 s. Give it 0.3 s.
        yield return StepSeconds(0.3f);

        Assert.AreEqual(r.player.airSpeed, r.player.velocity.x, SpeedTolerance,
            $"Horizontal speed above airSpeed with no ground momentum behind it should be braked " +
            $"back to airSpeed ({r.player.airSpeed:F3}), but the character is at " +
            $"{r.player.velocity.x:F3} units/s.");
    }

    [UnityTest]
    public IEnumerator AirMotion_LeavingGroundAboveAirSpeed_PreservesGroundMomentum()
    {
        // A short floor, so the character runs off the end of it rather than out of the lane.
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(24), r, null, ShortFloorWidth);

        r.player.Run(true);
        r.player.Move(Vector2.right);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character running off the end of a short floor");

        float speedOnLeaving = r.player.lastGroundVelocity.x;
        Assert.Greater(speedOnLeaving, r.player.airSpeed + 1f,
            $"Precondition: the character should leave the ground well above airSpeed, but " +
            $"lastGroundVelocity.x is {speedOnLeaving:F3}.");

        // Keep pushing the same way; momentum should survive.
        yield return StepSeconds(0.4f);

        Assert.Greater(r.player.velocity.x, r.player.airSpeed + 1f,
            $"Horizontal momentum carried off the ground should be preserved while the character " +
            $"keeps moving that way, but its speed collapsed to {r.player.velocity.x:F3} units/s - " +
            $"barely above airSpeed ({r.player.airSpeed:F3}) - after leaving the ground at " +
            $"{speedOnLeaving:F3}.");
    }

    [UnityTest]
    public IEnumerator AirMotion_ReversingAgainstGroundMomentum_DropsTheLimitToAirSpeed()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(25), 12f, r);

        // As if the character had just run off a ledge to the right at runSpeed.
        r.player.lastGroundVelocity = new Vector2(10f, 0f);
        r.player.velocity = new Vector2(10f, 0f);

        // Now push back the other way. The preserved momentum belongs to the direction it was
        // earned in; once the character is travelling left, only airSpeed should be available.
        r.player.Move(Vector2.left);

        yield return StepSeconds(1.5f);

        Assert.Less(r.player.velocity.x, 0f,
            $"Precondition: after 1.5 s of left input the character should be travelling left, but " +
            $"it is at {r.player.velocity.x:F3} units/s.");
        Assert.GreaterOrEqual(r.player.velocity.x, -(r.player.airSpeed + SpeedTolerance),
            $"Momentum preserved from a rightward run must not be spent going left: the character " +
            $"reached {r.player.velocity.x:F3} units/s, beyond the leftward airSpeed budget of " +
            $"{-r.player.airSpeed:F3}.");
    }

    // EXPECTED RED (PP-17), the airborne twin of the ground deadzone test above. MoveInAir gates on
    // `Mathf.Abs(motionInput.x) > 0`, so stick drift steers the character through the whole fall.
    [UnityTest]
    public IEnumerator AirMotion_TinyAnalogInput_IsInsideTheDeadzone()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(26), 14f, r);

        float startX = r.player.position.x;
        r.player.Move(new Vector2(0.02f, 0f));

        yield return StepSeconds(1.2f);

        float drift = Mathf.Abs(r.player.position.x - startX);
        Assert.Less(drift, PositionTolerance,
            $"Movement input of 0.02 is below SMALL_INPUT_THRESHOLD and should be ignored in the " +
            $"air as well as on the ground, but the character drifted {drift:F3} units sideways " +
            "during its fall.");
    }

    // ------------------------------------------------------------------
    // Facing direction
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Facing_OnSpawn_IsRight()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(27), r);

        Assert.AreEqual(1f, r.player.facingDirection.x, DirectionTolerance,
            $"A fresh character should face right by default, but facingDirection is " +
            $"{r.player.facingDirection}.");
    }

    [UnityTest]
    public IEnumerator Facing_MovingLeft_FacesLeft()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(28), r);

        r.player.Move(Vector2.left);
        yield return StepSeconds(0.2f);

        Assert.AreEqual(-1f, r.player.facingDirection.x, DirectionTolerance,
            $"A character moving left should face left, but facingDirection is " +
            $"{r.player.facingDirection}.");
    }

    [UnityTest]
    public IEnumerator Facing_InputReleased_HoldsTheLastHorizontalDirection()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(29), r);

        r.player.Move(Vector2.left);
        yield return StepSeconds(0.3f);

        r.player.Move(Vector2.zero);
        yield return StepSeconds(0.5f);

        Assert.AreEqual(-1f, r.player.facingDirection.x, DirectionTolerance,
            $"Releasing the stick should leave the character facing the way it was last moving " +
            $"(left), but facingDirection is {r.player.facingDirection}.");
    }

    // EXPECTED RED (PP-20). facingDirection carries two meanings at once: it is the normalized 2D
    // input vector, and it is also read as a left/right sign by the sprite flip, the wall tie-break
    // and Mathf.Sign(facingDirection.x) in Jump. Pressing straight up makes it (0, 1), and
    // Mathf.Sign(0) is +1 in Unity - so a character that was facing LEFT silently starts reporting
    // "right" for every consumer of the sign, without the player having turned around.
    [UnityTest]
    public IEnumerator Facing_PureUpInput_KeepsTheLastHorizontalFacing()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(30), r);

        r.player.Move(Vector2.left);
        yield return StepSeconds(0.3f);

        Assert.AreEqual(-1f, r.player.facingDirection.x, DirectionTolerance,
            "Precondition: the character should be facing left before the up input.");

        r.player.Move(Vector2.up);
        yield return StepSeconds(0.2f);

        Assert.Less(r.player.facingDirection.x, 0f,
            $"Pushing straight up is not turning around. The character was facing left, but after a " +
            $"pure up input facingDirection is {r.player.facingDirection} - and Mathf.Sign of its x " +
            "is +1, so every consumer that reads it as a left/right sign now says 'right'.");
    }

    [UnityTest]
    public IEnumerator Facing_SpriteRendererFlipX_TracksFacingWhenFaceMotionDirectionIsTrue()
    {
        SpriteRenderer renderer = null;

        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(31), r, p =>
        {
            p.faceMotionDirection = true;
            // Must be added before Start(), which is where the component caches it.
            renderer = AddSpriteRenderer(p);
        });

        r.player.Move(Vector2.left);
        yield return StepSeconds(0.3f);

        Assert.IsTrue(renderer.flipX,
            "With faceMotionDirection on, a character facing left should have its sprite flipped.");

        r.player.Move(Vector2.right);
        yield return StepSeconds(0.3f);

        Assert.IsFalse(renderer.flipX,
            "With faceMotionDirection on, a character facing right should have its sprite unflipped.");
    }

    [UnityTest]
    public IEnumerator Facing_SpriteRendererFlipX_IsLeftAloneWhenFaceMotionDirectionIsFalse()
    {
        SpriteRenderer renderer = null;

        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(32), r, p =>
        {
            p.faceMotionDirection = false;
            renderer = AddSpriteRenderer(p);
        });

        renderer.flipX = false;

        r.player.Move(Vector2.left);
        yield return StepSeconds(0.3f);

        Assert.AreEqual(-1f, r.player.facingDirection.x, DirectionTolerance,
            "Precondition: facingDirection should still track input even when the sprite does not.");
        Assert.IsFalse(renderer.flipX,
            "With faceMotionDirection off, the component must not touch the sprite's flipX - the " +
            "game may be driving it from an animator instead.");
    }

    // ------------------------------------------------------------------
    // ProcessCollision / AvoidPlatformEdge
    //
    // PlatformerPlayer2D overrides ProcessCollision so that a character whose head clips the corner
    // of a platform is nudged sideways past it rather than stopped dead. Only the override's own
    // contract is tested here; the base class's collision resolution is the existing suite's job.
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator HeadBump_NearAPlatformEdge_NudgesTheCharacterPastTheEdge()
    {
        float laneX = Slot(33);

        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(laneX, r);

        // A ceiling whose right edge overlaps only the left sliver of the character's head, so a
        // small sideways nudge clears it.
        float ceilingRightEdge = r.restPosition.x - CharacterHalfWidth * 0.6f;
        float ceilingWidth = 6f;
        CreateCeiling(new Vector2(ceilingRightEdge - ceilingWidth * 0.5f, r.restPosition.y + 2.5f),
                      new Vector2(ceilingWidth, 0.5f));

        yield return Step(2);

        r.player.velocity = new Vector2(0f, 9f);

        Trace trace = new Trace();
        yield return Record(r.player, 1f, trace);

        float sidewaysNudge = r.player.position.x - r.restPosition.x;

        Assert.Greater(sidewaysNudge, 0.05f,
            $"A character rising into the corner of a platform should be nudged clear of the edge " +
            $"rather than stopped under it, but it moved {sidewaysNudge:F3} units sideways.");
    }

    [UnityTest]
    public IEnumerator HeadBump_DeadCentreUnderAPlatform_IsNotNudged()
    {
        float laneX = Slot(34);

        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(laneX, r);

        // A wide ceiling centred on the character: there is no edge to escape past, so the
        // correction must not fire and shove it sideways.
        CreateCeiling(new Vector2(r.restPosition.x, r.restPosition.y + 2.5f), new Vector2(12f, 0.5f));

        yield return Step(2);

        r.player.velocity = new Vector2(0f, 9f);

        yield return StepSeconds(1f);

        float sidewaysNudge = Mathf.Abs(r.player.position.x - r.restPosition.x);

        Assert.Less(sidewaysNudge, 0.2f,
            $"A character that hits the middle of a wide platform has no edge to be nudged past, " +
            $"but it slid {sidewaysNudge:F3} units sideways.");
    }

    // PlatformerPlayer2D narrows the base class's push rule: `otherMotion.pushable && base.CanPush`.
    // A body that is not pushable must not be shoved even when the base rule alone would allow it.
    [UnityTest]
    public IEnumerator CanPush_NonPushableBody_IsNotPushed()
    {
        float laneX = Slot(35);

        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(laneX, r);

        KinematicMotion2D target = CreateMovingPlatform(
            new Vector2(r.restPosition.x + 2f, r.restPosition.y), new Vector2(1f, 1f), Vector2.zero);
        target.name = "NonPushableTarget";
        target.pushable = false;
        target.pushPriority = 0;

        yield return Step(2);
        float targetStartX = target.position.x;

        r.player.Run(true);
        r.player.Move(Vector2.right);

        yield return StepSeconds(2f);

        Assert.Less(Mathf.Abs(target.position.x - targetStartX), PositionTolerance,
            $"A body with pushable = false must not be pushed, but the character shoved it " +
            $"{target.position.x - targetStartX:F3} units.");
        Assert.Less(r.player.position.x, target.position.x,
            "The character should have come to rest against the body it could not push, not passed " +
            "through it.");
    }
}
