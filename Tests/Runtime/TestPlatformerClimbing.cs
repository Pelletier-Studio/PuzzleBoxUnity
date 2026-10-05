using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PuzzleBox;

/// <summary>
/// SPECIFICATION - PlatformerPlayer2D climbing (ladders, vines, and the like).
///
/// Climbing is deliberately kept in its own file rather than folded in with the wall states. The
/// source says why, in the comment above the Climbing header: "Climbing is a mechanic that is
/// completely distinct from wall grabbing. As a matter of fact, it has nothing to do with walls at
/// all." There is no concept of a climbable surface - a game marks out a ladder with a trigger that
/// toggles canClimb - so every test here toggles that flag rather than building geometry.
///
///  1. canClimb gates the whole mechanic. With it off, vertical input does nothing special.
///  2. With it on, upward input starts a climb, whether the character is standing or airborne.
///  3. While climbing, gravity is switched off entirely - a character that stops moving stays put.
///  4. Input moves the character at climbSpeedUp / climbSpeedDown / climbSpeedSide, reached at
///     climbAcceleration; releasing it brakes to a dead stop at climbBreakingForce without
///     oscillating around zero.
///  5. Input below SMALL_INPUT_THRESHOLD is not input, in the air as much as on the ground.
///  6. climbCollisionMask replaces the usual collision mask for the duration of the climb, so a
///     game can let the character pass through geometry that is solid at every other time - and
///     that geometry becomes solid again the moment the climb ends.
///  7. StopClimbing() drops the character out of a climb, and does nothing if it was not climbing.
///  8. A jump taken while climbing is permitted by canJumpWhenClimbing, scaled by
///     climbJumpHeightRatio, and hands the character back to normal gravity.
///  9. climbingJumpCoolDown is a grace period after a climb jump during which the ladder cannot
///     recapture the character. It exists because the airborne entry into Climbing re-tests the
///     vertical input every frame, so a player still holding up at the moment they press jump
///     would otherwise have the jump cancelled on the very next frame - ApplyClimbingMotion
///     clamps velocity.y down to climbSpeedUp before the character has travelled anywhere.
///     It is a grace period, NOT a permanent cancel: once it expires, a still-held up input
///     legitimately catches the ladder again. Setting it to 0 opts out.
/// 10. Nothing else arms that cooldown. An ordinary ground jump does not, and neither does
///     StopClimbing() - which a ladder trigger calls on exit, and which must not leave a
///     character unable to climb the next ladder it reaches.
/// 11. Touching the ground clears it. The cooldown governs whether the LADDER may recapture a
///     character in mid-air; it must never stop a player deliberately starting a climb from the
///     floor, so a grounded character never has one running.
///
/// RED LIST: empty. Every test in this file passes against the current implementation.
/// Climbing_TinyAnalogInputInTheAir_IsInsideTheDeadzone was red until the three airborne entries
/// into Climbing were changed from `> 0` to `> SMALL_INPUT_THRESHOLD`; keep it as the guard for
/// that. If anything here goes red, it is a regression, not a known defect.
/// </summary>
public class TestPlatformerClimbing : PlatformerTestFixture
{
    private const float Lane = 20500f;

    private static float Slot(int n)
    {
        return Lane + n * 50f;
    }

    // A built-in Unity layer that no test geometry uses by default, so it can stand for "geometry
    // the game wants the character to pass through while climbing".
    private const int PassThroughLayer = 4;   // "Water"

    // ------------------------------------------------------------------
    // Entering and leaving a climb
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Climbing_WithCanClimbFalse_UpInputDoesNothing()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(0), r, p => p.canClimb = false);

        r.player.Move(Vector2.up);

        Trace trace = new Trace();
        yield return Record(r.player, 0.8f, trace);

        Assert.IsFalse(trace.Saw(PlatformerPlayer2D.State.Climbing),
            $"With canClimb off there is nothing to climb, so up input should be ignored. " +
            $"Observed: {trace.Describe()}.");
        Assert.Less(Mathf.Abs(r.player.position.y - r.restPosition.y), PositionTolerance,
            "A character that cannot climb should not rise when up is held.");
    }

    [UnityTest]
    public IEnumerator Climbing_UpInputWhileGrounded_EntersClimbing()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToClimbing(Slot(1), r);

        Assert.AreEqual(PlatformerPlayer2D.State.Climbing, r.player.state,
            $"Pushing up inside a climbable area should start a climb, but the state is " +
            $"{r.player.state}.");
    }

    [UnityTest]
    public IEnumerator Climbing_UpInputWhileAirborne_EntersClimbing()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(2), 12f, r, p => p.canClimb = true);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released above the floor");

        r.player.Move(Vector2.up);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Climbing, StateTimeout,
                                  "a falling character that grabbed a ladder");

        Assert.AreEqual(PlatformerPlayer2D.State.Climbing, r.player.state,
            $"Catching a ladder in mid-air should start a climb, but the state is {r.player.state}.");
    }

    [UnityTest]
    public IEnumerator Climbing_SuppressesGravityEntirely()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(3), 12f, r, p => p.canClimb = true);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released above the floor");

        r.player.Move(Vector2.up);
        yield return WaitForState(r.player, PlatformerPlayer2D.State.Climbing, StateTimeout,
                                  "a falling character that grabbed a ladder");

        // Let go of the stick. The braking should bring it to a halt, and nothing should then pull
        // it down: hanging motionless on a ladder is the whole point of switching gravity off.
        r.player.Move(Vector2.zero);
        yield return StepSeconds(0.3f);

        float restingY = r.player.position.y;
        yield return StepSeconds(0.8f);

        Assert.AreEqual(restingY, r.player.position.y, PositionTolerance,
            $"Gravity is switched off while climbing, so a character that stops moving should hang " +
            $"where it is: it was at y {restingY:F3} and is now at {r.player.position.y:F3}.");
        Assert.AreEqual(0f, r.player.gravityMultiplier, 0.01f,
            $"gravityMultiplier should be 0 throughout a climb, but it is " +
            $"{r.player.gravityMultiplier:F3}.");
    }

    [UnityTest]
    public IEnumerator StopClimbing_WhileClimbing_EntersFalling()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(4), 12f, r, p => p.canClimb = true);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released above the floor");
        r.player.Move(Vector2.up);
        yield return WaitForState(r.player, PlatformerPlayer2D.State.Climbing, StateTimeout,
                                  "a falling character that grabbed a ladder");

        r.player.StopClimbing();

        Assert.AreEqual(PlatformerPlayer2D.State.Falling, r.player.state,
            $"StopClimbing() should drop the character off the ladder immediately, but the state " +
            $"is {r.player.state}.");
    }

    [UnityTest]
    public IEnumerator StopClimbing_WhileNotClimbing_LeavesTheStateUntouched()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(5), r);

        Assert.AreEqual(PlatformerPlayer2D.State.Walking, r.player.state,
            "Precondition: the character should be walking.");

        r.player.StopClimbing();

        Assert.AreEqual(PlatformerPlayer2D.State.Walking, r.player.state,
            $"StopClimbing() on a character that is not climbing should do nothing, but the state " +
            $"became {r.player.state}. A trigger that fires this on exit must be safe to call " +
            "whether or not the character was on the ladder.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator Climbing_CanClimbTurnedOffMidClimb_FallsImmediately()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToClimbing(Slot(6), r);

        // The documented way to build a ladder: a trigger clears the flag as the character leaves.
        r.player.canClimb = false;

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a climbing character that left the climbable area");

        Assert.AreEqual(PlatformerPlayer2D.State.Falling, r.player.state,
            $"Leaving a climbable area should drop the character, but the state is {r.player.state}.");
    }

    // ------------------------------------------------------------------
    // Climbing speeds
    // ------------------------------------------------------------------

    // Puts the character on an imaginary ladder in mid-air with room above and below to measure in.
    private IEnumerator MakeClimber(float slotX, PlayerResult r, PlayerConfig cfg = null)
    {
        yield return MakePlayerInAir(slotX, 14f, r, p =>
        {
            p.canClimb = true;
            if (cfg != null) cfg(p);
        });

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released above the floor");

        r.player.Move(Vector2.up);
        yield return WaitForState(r.player, PlatformerPlayer2D.State.Climbing, StateTimeout,
                                  "a falling character that grabbed a ladder");
    }

    [UnityTest]
    public IEnumerator Climbing_UpInput_RisesAtClimbSpeedUp()
    {
        PlayerResult r = new PlayerResult();
        yield return MakeClimber(Slot(7), r, p => p.climbSpeedUp = 2f);

        // climbAcceleration is 20, so the cap is reached in a tenth of a second.
        yield return StepSeconds(0.3f);

        SpeedResult speed = new SpeedResult();
        yield return MeasureAverageSpeed(r.player, 0.4f, speed);

        Assert.AreEqual(r.player.climbSpeedUp, speed.verticalSpeed, SpeedTolerance,
            $"Climbing up should settle at climbSpeedUp ({r.player.climbSpeedUp:F3}), but the " +
            $"character rose at {speed.verticalSpeed:F3} units/s.");
    }

    [UnityTest]
    public IEnumerator Climbing_DownInput_DescendsAtClimbSpeedDown()
    {
        PlayerResult r = new PlayerResult();
        yield return MakeClimber(Slot(8), r, p => p.climbSpeedDown = 3f);

        r.player.Move(Vector2.down);
        yield return StepSeconds(0.3f);

        SpeedResult speed = new SpeedResult();
        yield return MeasureAverageSpeed(r.player, 0.4f, speed);

        Assert.AreEqual(-r.player.climbSpeedDown, speed.verticalSpeed, SpeedTolerance,
            $"Climbing down should settle at climbSpeedDown ({r.player.climbSpeedDown:F3}), but the " +
            $"character moved at {speed.verticalSpeed:F3} units/s.");
    }

    [UnityTest]
    public IEnumerator Climbing_SideInput_MovesAtClimbSpeedSide()
    {
        PlayerResult r = new PlayerResult();
        yield return MakeClimber(Slot(9), r, p => p.climbSpeedSide = 2f);

        r.player.Move(Vector2.right);
        yield return StepSeconds(0.3f);

        SpeedResult speed = new SpeedResult();
        yield return MeasureAverageSpeed(r.player, 0.4f, speed);

        Assert.AreEqual(r.player.climbSpeedSide, speed.displacement.x / speed.seconds, SpeedTolerance,
            $"Moving sideways on a ladder should settle at climbSpeedSide " +
            $"({r.player.climbSpeedSide:F3}), but the character moved at " +
            $"{speed.displacement.x / speed.seconds:F3} units/s.");
    }

    [UnityTest]
    public IEnumerator Climbing_NeutralInput_BrakesToADeadStop()
    {
        PlayerResult r = new PlayerResult();
        yield return MakeClimber(Slot(10), r);

        r.player.Move(new Vector2(1f, 1f));
        yield return StepSeconds(0.4f);

        Assert.Greater(r.player.velocity.magnitude, 1f,
            $"Precondition: the character should be moving before the brake, but its speed is " +
            $"{r.player.velocity.magnitude:F3}.");

        r.player.Move(Vector2.zero);

        // climbBreakingForce is 50, so a couple of tenths is plenty.
        yield return StepSeconds(0.4f);

        Assert.AreEqual(0f, r.player.velocity.magnitude, SpeedTolerance,
            $"Releasing the stick on a ladder should bring the character to a dead stop, but it is " +
            $"still moving at {r.player.velocity.magnitude:F3} units/s.");
    }

    // The braking in ApplyClimbingMotion works by adding a step against the direction of travel and
    // then snapping to zero if the sign flipped. If that snap were missing, the character would
    // judder back and forth around zero forever instead of stopping.
    [UnityTest]
    public IEnumerator Climbing_BrakingSnapsToZeroWithoutOscillating()
    {
        PlayerResult r = new PlayerResult();
        yield return MakeClimber(Slot(11), r);

        r.player.Move(new Vector2(1f, 1f));
        yield return StepSeconds(0.4f);

        r.player.Move(Vector2.zero);
        yield return StepSeconds(0.4f);

        Trace trace = new Trace();
        yield return Record(r.player, 0.5f, trace);

        for (int i = 0; i < trace.velocities.Count; i++)
        {
            Assert.Less(trace.velocities[i].magnitude, SpeedTolerance,
                $"On step {i}, well after the stick was released, the character was still moving at " +
                $"{trace.velocities[i].magnitude:F4} units/s. Climb braking must settle at zero, " +
                "not oscillate across it.");
        }
    }

    // EXPECTED RED (PP-17). The grounded entry into Climbing correctly uses
    // `motionInput.y > SMALL_INPUT_THRESHOLD`, but all three airborne entries in UpdateStateInAir
    // use `motionInput.y > 0`. A stick resting a hair off centre therefore catches an airborne
    // character on any ladder it happens to fall past - and because Climbing switches gravity off,
    // the character stops dead in mid-air for no reason the player can see.
    [UnityTest]
    public IEnumerator Climbing_TinyAnalogInputInTheAir_IsInsideTheDeadzone()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(12), 14f, r, p => p.canClimb = true);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released above the floor");

        // Well below SMALL_INPUT_THRESHOLD (0.1): stick drift, not a grab for the ladder.
        r.player.Move(new Vector2(0f, 0.02f));

        Trace trace = new Trace();
        yield return Record(r.player, 0.8f, trace);

        Assert.IsFalse(trace.Saw(PlatformerPlayer2D.State.Climbing),
            $"Vertical input of 0.02 is below SMALL_INPUT_THRESHOLD and should not catch a ladder, " +
            $"but the airborne entry into Climbing tests for `> 0`. Observed: {trace.Describe()}.");
    }

    // ------------------------------------------------------------------
    // climbCollisionMask
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Climbing_UsesClimbCollisionMask_PassingThroughExcludedGeometry()
    {
        float laneX = Slot(13);

        PlayerResult r = new PlayerResult();
        yield return MakeClimber(Slot(13), r, p =>
        {
            p.collisionMask = ~0;                          // solid to everything normally
            p.climbCollisionMask = ~(1 << PassThroughLayer);  // except while climbing
            p.climbSpeedUp = 4f;
        });

        // A slab directly overhead, on the layer the climb mask excludes: the ladder's
        // "pass through the floor above" case.
        float slabY = r.player.position.y + 2f;
        GameObject slab = CreateCeiling(new Vector2(laneX, slabY), new Vector2(8f, 0.5f));
        slab.layer = PassThroughLayer;

        float startY = r.player.position.y;
        yield return StepSeconds(1.5f);

        Assert.Greater(r.player.position.y, slabY + 0.5f,
            $"climbCollisionMask excludes layer {PassThroughLayer}, so a climbing character should " +
            $"pass straight through the slab at y {slabY:F3} - but it stopped at " +
            $"{r.player.position.y:F3}, having started at {startY:F3}.");
    }

    [UnityTest]
    public IEnumerator Climbing_LeavingClimbing_RestoresTheNormalCollisionMask()
    {
        float laneX = Slot(14);

        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(laneX, 14f, r, p =>
        {
            p.canClimb = true;
            p.collisionMask = ~0;
            p.climbCollisionMask = ~(1 << PassThroughLayer);
        });

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released above the floor");

        // The same slab, but this time the character is falling, not climbing. Outside a climb the
        // normal mask applies and the slab is solid.
        float slabY = r.player.position.y - 3f;
        GameObject slab = CreateCeiling(new Vector2(laneX, slabY), new Vector2(8f, 0.5f));
        slab.name = "SolidSlab";
        slab.layer = PassThroughLayer;

        yield return StepSeconds(2f);

        Assert.Greater(r.player.position.y, slabY,
            $"Outside a climb, collisionMask applies and layer {PassThroughLayer} is solid, so the " +
            $"falling character should have landed on the slab at y {slabY:F3} - but it is at " +
            $"{r.player.position.y:F3}, below it.");
        Assert.IsTrue(r.player.isGrounded,
            "The character should be standing on the slab it could not pass through.");
    }

    // ------------------------------------------------------------------
    // Jumping off a ladder
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator ClimbJump_WithCanJumpWhenClimbingTrue_EntersJumping()
    {
        PlayerResult r = new PlayerResult();
        yield return MakeClimber(Slot(15), r, p => p.canJumpWhenClimbing = true);

        r.player.Jump(true);

        Assert.AreEqual(PlatformerPlayer2D.State.Jumping, r.player.state,
            $"A jump taken on a ladder should leave the ladder and become an ordinary jump, but " +
            $"the state is {r.player.state}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator ClimbJump_WithCanJumpWhenClimbingFalse_IsRefused()
    {
        PlayerResult r = new PlayerResult();
        yield return MakeClimber(Slot(16), r, p =>
        {
            p.canJumpWhenClimbing = false;
            p.maxAirJumps = 0;
        });

        r.player.Jump(true, false);

        Assert.AreEqual(PlatformerPlayer2D.State.Climbing, r.player.state,
            $"With canJumpWhenClimbing off the character should stay on the ladder, but the state " +
            $"is {r.player.state}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator ClimbJump_HeightScalesWithClimbJumpHeightRatio()
    {
        PlayerResult full = new PlayerResult();
        yield return MakeClimber(Slot(17), full, p =>
        {
            p.canJumpWhenClimbing = true;
            p.climbJumpHeightRatio = 1f;
        });

        full.player.Move(Vector2.zero);
        yield return StepSeconds(0.4f);      // stop climbing first, so the launch is all jump
        full.player.Jump(true);
        float fullLaunch = full.player.velocity.y;

        PlayerResult half = new PlayerResult();
        yield return MakeClimber(Slot(18), half, p =>
        {
            p.canJumpWhenClimbing = true;
            p.climbJumpHeightRatio = 0.5f;
        });

        half.player.Move(Vector2.zero);
        yield return StepSeconds(0.4f);
        half.player.Jump(true);
        float halfLaunch = half.player.velocity.y;

        Assert.AreEqual(fullLaunch * 0.5f, halfLaunch, SpeedTolerance,
            $"climbJumpHeightRatio scales the launch speed, so 0.5 should launch at half the speed " +
            $"of 1.0: {halfLaunch:F3} against {fullLaunch:F3}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator ClimbJump_RestoresNormalGravity()
    {
        PlayerResult r = new PlayerResult();
        yield return MakeClimber(Slot(19), r, p =>
        {
            p.canJumpWhenClimbing = true;
            p.climbingJumpCoolDown = 0.5f;
        });

        Assert.AreEqual(0f, r.player.gravityMultiplier, 0.01f,
            "Precondition: gravity should be switched off while climbing.");

        // The stick stays held, as it would in play: a player jumping off a ladder is still
        // pushing up at the moment they press jump. That is the case climbingJumpCoolDown exists
        // for, so the window measured below is deliberately inside it.
        float startY = r.player.position.y;
        r.player.Jump(true);
        float launch = r.player.velocity.y;

        Assert.AreEqual(PlatformerPlayer2D.State.Jumping, r.player.state,
            "Precondition: the jump itself should be granted.");

        float window = r.player.climbingJumpCoolDown * 0.8f;
        Trace trace = new Trace();
        yield return Record(r.player, window, trace);

        Assert.IsFalse(trace.Saw(PlatformerPlayer2D.State.Climbing),
            $"Inside climbingJumpCoolDown ({r.player.climbingJumpCoolDown:F3} s) the ladder must " +
            $"not recapture the character, or the jump is cancelled before it travels. " +
            $"Observed: {trace.Describe()}.");

        Assert.Greater(r.player.gravityMultiplier, 0.1f,
            $"Jumping off a ladder hands the character back to gravity, but gravityMultiplier is " +
            $"still {r.player.gravityMultiplier:F3}.");

        // The decisive measurement: the character has to actually travel like something that
        // jumped. If the ladder had recaptured it, ApplyClimbingMotion would have clamped
        // velocity.y to climbSpeedUp and it would be creeping up at that speed instead.
        float rise = r.player.position.y - startY;
        float climbWouldHaveRisen = r.player.climbSpeedUp * window;

        Assert.Greater(rise, climbWouldHaveRisen * 1.5f,
            $"A climb jump launched at {launch:F3} units/s should out-climb the ladder it jumped " +
            $"from: over {window:F3} s it rose {rise:F3} units, against the {climbWouldHaveRisen:F3} " +
            $"a plain climb at climbSpeedUp ({r.player.climbSpeedUp:F3}) would have managed.");
    }

    // The two ways a climb jump gets clear of the ladder WITHOUT needing climbingJumpCoolDown at
    // all: the player releases the stick, or the ladder volume ends and a trigger clears canClimb.
    // These were written to isolate which condition the cooldown was actually needed for - both of
    // these paths worked before it existed - and they stay as regression guards, because a cooldown
    // that is only correct for the held-stick case must not break the two cases that were fine.
    [UnityTest]
    public IEnumerator ClimbJump_WithUpReleased_LeavesTheLadder()
    {
        PlayerResult r = new PlayerResult();
        yield return MakeClimber(Slot(20), r, p => p.canJumpWhenClimbing = true);

        // Stick centred before the press, which is what a player does when they mean to get off.
        r.player.Move(Vector2.zero);
        yield return Step(2);

        r.player.Jump(true);
        float launch = r.player.velocity.y;

        // The window has to outlast the climb of the jump itself, or the character is still
        // ascending when recording stops and Falling can never appear. Time to apex is
        // launch / |g|, so twice that returns it to launch height, plus a little slack.
        float flight = 2f * launch / Mathf.Abs(Physics2D.gravity.y) + 0.2f;

        Trace trace = new Trace();
        yield return Record(r.player, flight, trace);

        Assert.IsFalse(trace.Saw(PlatformerPlayer2D.State.Climbing),
            $"With the stick centred the jump should carry the character off the ladder. " +
            $"Launched at {launch:F3} units/s. Observed: {trace.Describe()}.");
        Assert.IsTrue(trace.Saw(PlatformerPlayer2D.State.Falling),
            $"The jump should arc over into a fall. Observed: {trace.Describe()}.");
    }

    [UnityTest]
    public IEnumerator ClimbJump_WithUpHeldButTheAreaLeftBehind_LeavesTheLadder()
    {
        PlayerResult r = new PlayerResult();
        yield return MakeClimber(Slot(21), r, p => p.canJumpWhenClimbing = true);

        r.player.Jump(true);
        float launch = r.player.velocity.y;

        // A trigger-bounded ladder clears canClimb as the character rises out of the volume.
        yield return Step(2);
        r.player.canClimb = false;

        // The window has to outlast the climb of the jump itself, or the character is still
        // ascending when recording stops and Falling can never appear. Time to apex is
        // launch / |g|, so twice that returns it to launch height, plus a little slack.
        float flight = 2f * launch / Mathf.Abs(Physics2D.gravity.y) + 0.2f;

        Trace trace = new Trace();
        yield return Record(r.player, flight, trace);

        Assert.IsTrue(trace.Saw(PlatformerPlayer2D.State.Falling),
            $"Once the climbable area is behind it the character should fall normally. " +
            $"Launched at {launch:F3} units/s. Observed: {trace.Describe()}.");
    }

    // ------------------------------------------------------------------
    // climbingJumpCoolDown
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator ClimbJumpCoolDown_AfterTheCoolDown_ResumesClimbingWithUpStillHeld()
    {
        PlayerResult r = new PlayerResult();
        yield return MakeClimber(Slot(22), r, p =>
        {
            p.canJumpWhenClimbing = true;
            p.climbingJumpCoolDown = 0.15f;
        });

        // Up stays held for the whole test.
        r.player.Jump(true);

        Trace trace = new Trace();
        yield return Record(r.player, 0.6f, trace);

        Assert.IsTrue(trace.Saw(PlatformerPlayer2D.State.Climbing),
            $"climbingJumpCoolDown is a grace period, not a permanent cancel: once " +
            $"{r.player.climbingJumpCoolDown:F3} s has passed, a player still holding up is asking " +
            $"to be back on the ladder and should be. Observed: {trace.Describe()}.");

        Assert.AreNotEqual(PlatformerPlayer2D.State.Climbing, trace.states[0],
            $"The recapture should happen AFTER the cooldown, not on the first frame. " +
            $"Observed: {trace.Describe()}.");
    }

    // The opt-out. Setting the cooldown to 0 restores the behaviour the field was added to change,
    // which is what a game wants if it would rather handle this with its own trigger logic.
    [UnityTest]
    public IEnumerator ClimbJumpCoolDown_Zero_LetsTheLadderRecaptureImmediately()
    {
        PlayerResult r = new PlayerResult();
        yield return MakeClimber(Slot(23), r, p =>
        {
            p.canJumpWhenClimbing = true;
            p.climbingJumpCoolDown = 0f;
        });

        r.player.Jump(true);

        Trace trace = new Trace();
        yield return Record(r.player, 0.2f, trace);

        Assert.AreEqual(PlatformerPlayer2D.State.Climbing, trace.states[0],
            $"With climbingJumpCoolDown 0 there is no grace period, so a still-held up input should " +
            $"catch the ladder on the very next frame. Observed: {trace.Describe()}.");
    }

    [UnityTest]
    public IEnumerator ClimbJumpCoolDown_IsNotArmedByAnOrdinaryGroundJump()
    {
        PlayerResult r = new PlayerResult();
        // canClimb on, but the stick is centred, so this is a plain ground jump and not a climb jump.
        yield return MakePlayerOnGround(Slot(24), r, p =>
        {
            p.canClimb = true;
            p.canJumpWhenClimbing = true;
            p.climbingJumpCoolDown = 0.5f;
        });

        Assert.AreEqual(PlatformerPlayer2D.State.Walking, r.player.state,
            "Precondition: with the stick centred the character should be walking, not climbing.");

        r.player.Jump(true);

        Assert.LessOrEqual(r.player.climbingJumpCoolDownTimer.timeLeft, 0f,
            $"The cooldown belongs to climb jumps. An ordinary ground jump should not arm it, but " +
            $"timeLeft is {r.player.climbingJumpCoolDownTimer.timeLeft:F3} - which would stop the " +
            "character catching a ladder on the way up.");

        yield return null;
    }

    // EXPECTED RED. StopClimbing() ends with climbingJumpCoolDownTimer.Reset(), and the no-argument
    // Utils.Timer.Reset() does NOT clear a timer - it reloads it, setting timeLeft back to
    // totalTime. So once a climb jump has given the timer a totalTime, every later StopClimbing()
    // ARMS a full cooldown instead of clearing one. StopClimbing() is the documented way for a
    // ladder trigger to release the character on exit, so this makes stepping off one ladder block
    // the next one for climbingJumpCoolDown seconds.
    //
    // Cancel(false) is the call that clears a timer; Reset(0f, false) also works.
    [UnityTest]
    public IEnumerator StopClimbing_AfterAClimbJump_DoesNotArmTheCoolDown()
    {
        PlayerResult r = new PlayerResult();
        yield return MakeClimber(Slot(25), r, p =>
        {
            p.canJumpWhenClimbing = true;
            p.climbingJumpCoolDown = 0.2f;
        });

        // The climb jump is what gives the timer a totalTime to be reloaded from later.
        r.player.Jump(true);
        yield return StepSeconds(0.5f);

        Assert.LessOrEqual(r.player.climbingJumpCoolDownTimer.timeLeft, 0f,
            $"Precondition: the cooldown should have expired after 0.5 s, but timeLeft is " +
            $"{r.player.climbingJumpCoolDownTimer.timeLeft:F3}.");

        // A ladder trigger releasing the character as it leaves the volume.
        r.player.StopClimbing();

        Assert.LessOrEqual(r.player.climbingJumpCoolDownTimer.timeLeft, 0f,
            $"Leaving a climbable area should not arm the climb-jump cooldown, but StopClimbing() " +
            $"left timeLeft at {r.player.climbingJumpCoolDownTimer.timeLeft:F3} of " +
            $"{r.player.climbingJumpCoolDown:F3} s - so the next ladder the character reaches " +
            "refuses it for that long.");
    }

    // Touching the ground clears the cooldown outright - ApplyGroundMotion cancels the timer on
    // every grounded frame. That is what makes the comment on the grounded entry in
    // UpdateStateOnGround true: that branch does not consult the timer, and it does not need to,
    // because a grounded character never has one running.
    [UnityTest]
    public IEnumerator ClimbJumpCoolDown_TouchingTheGround_ClearsIt()
    {
        PlayerResult r = new PlayerResult();
        yield return MakeClimber(Slot(26), r, p =>
        {
            p.canJumpWhenClimbing = true;
            // Far longer than the flight, so only the landing can account for the timer clearing.
            p.climbingJumpCoolDown = 10f;
        });

        r.player.Jump(true);

        Assert.Greater(r.player.climbingJumpCoolDownTimer.timeLeft, 0f,
            "Precondition: the climb jump should have armed the cooldown.");

        // Stick centred so the character simply falls back to the floor.
        r.player.Move(Vector2.zero);
        yield return WaitUntilGrounded(r.player, 6f, "the character after its climb jump");
        yield return Step(2);

        Assert.LessOrEqual(r.player.climbingJumpCoolDownTimer.timeLeft, 0f,
            $"Touching the ground should clear the climb-jump cooldown, but timeLeft is still " +
            $"{r.player.climbingJumpCoolDownTimer.timeLeft:F3} of " +
            $"{r.player.climbingJumpCoolDown:F3} s after landing.");
    }

    // The user-visible half of the same thing, asserted without reaching for the timer: a character
    // that has landed can start a climb straight away, however long the cooldown nominally is. The
    // cooldown stops the LADDER recapturing a character in mid-air; it must never stop a player
    // deliberately starting a climb from the floor.
    [UnityTest]
    public IEnumerator ClimbJumpCoolDown_DoesNotBlockClimbingFromTheGround()
    {
        PlayerResult r = new PlayerResult();
        yield return MakeClimber(Slot(27), r, p =>
        {
            p.canJumpWhenClimbing = true;
            p.climbingJumpCoolDown = 10f;
        });

        r.player.Jump(true);

        r.player.Move(Vector2.zero);
        yield return WaitUntilGrounded(r.player, 6f, "the character after its climb jump");
        yield return Step(2);

        // Up again, from a standing start, well inside the nominal cooldown.
        r.player.Move(Vector2.up);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Climbing, StateTimeout,
                                  "a grounded character pushing up after a climb jump");

        Assert.AreEqual(PlatformerPlayer2D.State.Climbing, r.player.state,
            $"A character standing on the floor should be able to start a climb immediately, even " +
            $"though it climb-jumped less than climbingJumpCoolDown " +
            $"({r.player.climbingJumpCoolDown:F3} s) ago.");
    }
}
