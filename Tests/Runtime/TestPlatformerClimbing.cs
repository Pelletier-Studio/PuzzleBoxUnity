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
///
/// THIS IS A TDD RED PHASE. The test marked below is expected to fail against the current
/// implementation. Do not make it pass by weakening its assertion - the failure is the point.
///
/// RED LIST (expected to fail; transcribe the observed set after the first full run):
///   Climbing_TinyAnalogInputInTheAir_IsInsideTheDeadzone                   PP-17
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
        yield return MakeClimber(Slot(19), r, p => p.canJumpWhenClimbing = true);

        Assert.AreEqual(0f, r.player.gravityMultiplier, 0.01f,
            "Precondition: gravity should be switched off while climbing.");

        r.player.Jump(true);
        yield return StepSeconds(0.2f);

        Assert.Greater(r.player.gravityMultiplier, 0.1f,
            $"Jumping off a ladder hands the character back to gravity, but gravityMultiplier is " +
            $"still {r.player.gravityMultiplier:F3} - a character that jumps and never comes down " +
            "is the failure this guards against.");

        // And it really does come down.
        ApexResult apex = new ApexResult();
        yield return MeasureApex(r.player, 3f, apex);

        Assert.IsTrue(apex.confirmedDescent,
            $"After jumping off a ladder the character should fall again, but it never started " +
            $"descending; it reached {apex.apexHeight:F3} units above the launch and stayed there.");
    }
}
