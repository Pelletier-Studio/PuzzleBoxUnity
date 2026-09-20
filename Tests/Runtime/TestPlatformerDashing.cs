using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PuzzleBox;

/// <summary>
/// SPECIFICATION - PlatformerPlayer2D dashing.
///
///  1. canDash gates the mechanic entirely.
///  2. A dash goes in the direction of the movement input, or - if the stick is centred - the way
///     the character is facing. movementInputScaling applies first, like everywhere else.
///  3. limitDashAngle snaps that direction to the eight compass points; with it off, the raw analog
///     angle is used.
///  4. Dash speed is |x| * dashSpeedSide + |y| * (dashSpeedUp or dashSpeedDown).
///  5. A dash LASTS dashTime, with its speed shaped by dashSpeedCurve across that span, and
///     dashTimer.phase running 0 to 1 over it.
///  6. dashCoolDownTime must elapse before another dash is allowed.
///  7. dashInputFreezeTime takes input away for that long, AND GIVES IT BACK.
///  8. dashGravityRatio decides how much gravity acts during the dash: 0 is a perfectly straight
///     line for the whole of dashTime, 1 arcs like a thrown object.
///  9. A dash is aborted early by running head-on into a wall, or by the speed dropping to
///     minDashSpeed. A glancing blow is not a head-on collision.
/// 10. Starting a dash sends DidDash, and the dash always ends in a state the character can act from.
///
/// THIS FILE IS QUARANTINE. Utils.Timer.Start(time) on an idle timer - and every fresh timer is
/// idle, because timeLeft starts at 0 - takes the `else if (timeLeft &lt;= 0)` branch, fires OnStart
/// and returns WITHOUT EVER ASSIGNING timeLeft OR totalTime. Only Reset(time) loads a duration, and
/// nothing in the dash path calls Reset. Every dash defect below traces back to that one line:
///
///   - dashTimer reports isFinished the instant PerformDash starts it, so State.Dashing survives a
///     single FixedUpdate. dashTime, dashSpeedCurve and dashTimer.phase are all dead.
///   - dashCoolDownTimer never loads, so dashCoolDownTime has no effect whatsoever.
///   - inputFreezeTimer fires OnStart (which clears acceptInput) but never OnEnd, so ACCEPTINPUT IS
///     PERMANENTLY FALSE AFTER THE FIRST DASH. The character is deaf to input for the rest of its
///     life, and isDashing stays true forever while `state` leaves Dashing immediately.
///
/// That last one is contagious: any test step after a dash silently runs with input disabled and
/// could go green for entirely the wrong reason. Two containment rules, both followed below:
/// dash tests live only in this file, and a test that dashes either ends there or calls
/// ForceRestoreInput with a reason. Where a dash's direction or speed is the subject, the
/// assertion is made SYNCHRONOUSLY after Dash() returns, which sidesteps the timer completely.
///
/// Per the suite's scope, the root cause in Utils.Timer is not unit-tested here - only its
/// player-visible symptoms. It is named above so that a fixer is not chasing eight mysteries.
///
/// This file is also the one exception to the suite's "only the input file sends input messages"
/// rule: the dash cancel lives in OnMove rather than in Move, so the three tests about it have to
/// go through the message layer to reach it.
///
/// RED LIST (expected to fail; transcribe the observed set after the first full run):
///   Dash_LastsForDashTime
///   Dash_SpeedFollowsDashSpeedCurveOverItsDuration
///   DashTimerPhase_AdvancesFromZeroToOneAcrossTheDash
///   DashCoolDown_SecondDashWithinCoolDown_IsRefused
///   DashInputFreeze_AfterDashInputFreezeTime_RestoresInput
///   Dash_DoesNotPermanentlyDisableInput
///   Dash_IsDashingClearsWhenTheDashEnds
///   DashGravityRatio_Zero_KeepsTheDashStraightForItsWholeDuration
///   Move_WhileNotDashing_DoesNotFireDashTimerEvents                        PP-12
///   Dash_StickReleaseDuringTheDash_DoesNotCancelIt                         PP-12
/// </summary>
public class TestPlatformerDashing : PlatformerTestFixture
{
    private const float Lane = 22000f;

    private static float Slot(int n)
    {
        return Lane + n * 50f;
    }

    private class DashMessageRecorder : MonoBehaviour
    {
        public int didDash;

        private void DidDash()
        {
            didDash++;
        }
    }

    /// <summary>
    /// Subscribes to a Utils.Timer's callbacks. Plain C#, not a MonoBehaviour - the timers are
    /// public fields on the component, so no SendMessage is involved.
    /// </summary>
    private class TimerEventRecorder
    {
        public int started;
        public int completed;
        public int cancelled;
        public int ended;

        public TimerEventRecorder(PuzzleBox.Utils.Timer timer)
        {
            timer.OnStart += () => started++;
            timer.OnComplete += () => completed++;
            timer.OnCancel += () => cancelled++;
            timer.OnEnd += () => ended++;
        }
    }

    // ------------------------------------------------------------------
    // Dash direction
    //
    // Everything in this section is asserted synchronously, on the line after Dash() returns.
    // That is the only point at which the launch vector is observable - base.FixedUpdate rewrites
    // velocity from realized motion - and it makes these tests immune to the timer defect.
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Dash_WithCanDashFalse_IsIgnored()
    {
        PlayerResult r = new PlayerResult();
        DashMessageRecorder recorder = null;

        yield return MakePlayerOnGround(Slot(0), r, p =>
        {
            p.canDash = false;
            recorder = p.gameObject.AddComponent<DashMessageRecorder>();
        });

        r.player.Move(Vector2.right);
        yield return Step(2);

        r.player.Dash();

        Assert.AreNotEqual(PlatformerPlayer2D.State.Dashing, r.player.state,
            $"With canDash off the dash command should be ignored, but the state is {r.player.state}.");
        Assert.AreEqual(0, recorder.didDash,
            $"An ignored dash must not send DidDash, but it arrived {recorder.didDash} time(s).");
    }

    [UnityTest]
    public IEnumerator Dash_WithMovementInput_UsesTheInputDirection()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(1), r, p =>
        {
            p.canDash = true;
            p.limitDashAngle = false;
        });

        r.player.Move(Vector2.left);
        r.player.Dash();

        Assert.AreEqual(-1f, r.player.dashDirection.x, DirectionTolerance,
            $"A dash with left input should go left, but dashDirection is {r.player.dashDirection}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator Dash_WithoutMovementInput_UsesTheFacingDirection()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(2), r, p =>
        {
            p.canDash = true;
            p.limitDashAngle = false;
        });

        // Face left, then centre the stick. facingDirection holds its last horizontal value.
        r.player.Move(Vector2.left);
        yield return StepSeconds(0.3f);
        r.player.Move(Vector2.zero);
        yield return StepSeconds(0.3f);

        Assert.AreEqual(-1f, r.player.facingDirection.x, DirectionTolerance,
            "Precondition: the character should be facing left with the stick centred.");

        r.player.Dash();

        Assert.AreEqual(-1f, r.player.dashDirection.x, DirectionTolerance,
            $"With no movement input the dash follows the way the character is facing (left), but " +
            $"dashDirection is {r.player.dashDirection}.");
    }

    [UnityTest]
    public IEnumerator Dash_WithMovementInputScaling_UsesTheScaledDirection()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(3), r, p =>
        {
            p.canDash = true;
            p.limitDashAngle = false;
            p.movementInputScaling = new Vector2(-1f, 1f);
        });

        r.player.Move(Vector2.right);
        r.player.Dash();

        Assert.AreEqual(-1f, r.player.dashDirection.x, DirectionTolerance,
            $"movementInputScaling is applied before anything reads the input, so a right press " +
            $"with x scaling -1 should dash left, but dashDirection is {r.player.dashDirection}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator LimitDashAngle_True_SnapsToTheEightCompassPoints()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(4), r, p =>
        {
            p.canDash = true;
            p.limitDashAngle = true;
            // So that acceptInput never flips during the sweep and the synthetic Move() in Update
            // cannot clear motionInput between probes.
            p.dashInputFreezeTime = 0f;
            // The sweep dashes sixteen times in a single frame to probe the snapping, which is not
            // something a player could do - so the cooldown is switched off rather than waited out.
            p.dashCoolDownTime = 0f;
        });

        // The whole sweep runs inside one frame, so nothing else can intervene between the Move and
        // the Dash that reads it.
        for (int i = 0; i < 16; i++)
        {
            float probeDegrees = i * 23f;   // deliberately not a multiple of 45
            float probeRadians = probeDegrees * Mathf.Deg2Rad;

            r.player.Move(new Vector2(Mathf.Cos(probeRadians), Mathf.Sin(probeRadians)));
            r.player.Dash();

            Vector2 dashed = r.player.dashDirection;
            float resultDegrees = Mathf.Atan2(dashed.y, dashed.x) * Mathf.Rad2Deg;

            // Distance to the nearest multiple of 45 degrees.
            float snapped = Mathf.Round(resultDegrees / 45f) * 45f;
            float error = Mathf.Abs(Mathf.DeltaAngle(resultDegrees, snapped));

            Assert.Less(error, 1f,
                $"With limitDashAngle on, an input at {probeDegrees:F1} degrees should snap to one " +
                $"of the eight compass points, but the dash went at {resultDegrees:F2} degrees - " +
                $"{error:F2} degrees off the nearest of them.");
            Assert.AreEqual(1f, dashed.magnitude, DirectionTolerance,
                $"The snapped dash direction should be a unit vector, but {dashed} has magnitude " +
                $"{dashed.magnitude:F4}.");
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator LimitDashAngle_False_PreservesTheRawAnalogAngle()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(5), r, p =>
        {
            p.canDash = true;
            p.limitDashAngle = false;
            p.dashInputFreezeTime = 0f;
        });

        float probeDegrees = 23f;
        float probeRadians = probeDegrees * Mathf.Deg2Rad;

        r.player.Move(new Vector2(Mathf.Cos(probeRadians), Mathf.Sin(probeRadians)));
        r.player.Dash();

        float resultDegrees = Mathf.Atan2(r.player.dashDirection.y, r.player.dashDirection.x) * Mathf.Rad2Deg;

        Assert.AreEqual(probeDegrees, resultDegrees, 1f,
            $"With limitDashAngle off the stick's own angle is used, so an input at " +
            $"{probeDegrees:F1} degrees should dash at {probeDegrees:F1} degrees, but it went at " +
            $"{resultDegrees:F2}.");

        yield return null;
    }

    // ------------------------------------------------------------------
    // Dash speed
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator DashSpeed_Sideways_IsDashSpeedSide()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(6), r, p =>
        {
            p.canDash = true;
            p.dashSpeedSide = 20f;
        });

        r.player.Move(Vector2.right);
        r.player.Dash();

        Assert.AreEqual(20f, r.player.velocity.magnitude, SpeedTolerance,
            $"A purely sideways dash launches at dashSpeedSide (20), but the launch speed is " +
            $"{r.player.velocity.magnitude:F3}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator DashSpeed_StraightUp_IsDashSpeedUp()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(7), r, p =>
        {
            p.canDash = true;
            p.dashSpeedUp = 14f;
        });

        r.player.Move(Vector2.up);
        r.player.Dash();

        Assert.AreEqual(14f, r.player.velocity.magnitude, SpeedTolerance,
            $"A dash straight up launches at dashSpeedUp (14), but the launch speed is " +
            $"{r.player.velocity.magnitude:F3}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator DashSpeed_StraightDown_IsDashSpeedDown()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(8), 14f, r, p =>
        {
            p.canDash = true;
            p.dashSpeedDown = 8f;
        });

        r.player.Move(Vector2.down);
        r.player.Dash();

        Assert.AreEqual(8f, r.player.velocity.magnitude, SpeedTolerance,
            $"A dash straight down launches at dashSpeedDown (8) - a separate field precisely so " +
            $"that a downward dash can be slower - but the launch speed is " +
            $"{r.player.velocity.magnitude:F3}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator DashSpeed_Diagonal_IsTheWeightedSumOfTheTwoAxisSpeeds()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(9), r, p =>
        {
            p.canDash = true;
            p.dashSpeedSide = 20f;
            p.dashSpeedUp = 10f;
            p.limitDashAngle = true;
        });

        r.player.Move(new Vector2(1f, 1f).normalized);
        r.player.Dash();

        float component = Mathf.Sqrt(0.5f);   // the 45-degree unit vector's x and y
        float expected = component * 20f + component * 10f;

        Assert.AreEqual(expected, r.player.velocity.magnitude, SpeedTolerance,
            $"A diagonal dash blends the two axis speeds by the direction's components, so at 45 " +
            $"degrees it should launch at {expected:F3}, but the launch speed is " +
            $"{r.player.velocity.magnitude:F3}.");

        yield return null;
    }

    // ------------------------------------------------------------------
    // Dash duration, curve and cooldown
    // ------------------------------------------------------------------

    // EXPECTED RED. dashTimer reports isFinished the instant it is started, so UpdateState's
    // Dashing case exits on the very next FixedUpdate and dashTime is never honoured. The dash
    // degrades to a single-frame velocity impulse.
    [UnityTest]
    public IEnumerator Dash_LastsForDashTime()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(10), 20f, r, p =>
        {
            p.canDash = true;
            p.dashTime = 0.2f;
        });

        r.player.Move(Vector2.right);
        EnterDashing(r.player);

        Trace trace = new Trace();
        yield return Record(r.player, 0.6f, trace);

        // The frame the dash begins is observed synchronously by EnterDashing, before Record's
        // first sample, so the trace can legitimately be one frame short.
        int expected = Mathf.FloorToInt(r.player.dashTime / Time.fixedDeltaTime) - 1;
        int observed = trace.ConsecutiveFrames(PlatformerPlayer2D.State.Dashing);

        Assert.GreaterOrEqual(observed, expected,
            $"A dash of dashTime {r.player.dashTime:F3} s should last about " +
            $"{Mathf.RoundToInt(r.player.dashTime / Time.fixedDeltaTime)} physics frames, but the " +
            $"character was in Dashing for {observed} consecutive frames. Observed: " +
            $"{trace.Describe()}.");

        ForceRestoreInput(r.player, "inputFreezeTimer never fires OnEnd, so acceptInput stays false");
    }

    // EXPECTED RED, same root cause. With a decaying curve the dash should start fast and end slow.
    [UnityTest]
    public IEnumerator Dash_SpeedFollowsDashSpeedCurveOverItsDuration()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(11), 20f, r, p =>
        {
            p.canDash = true;
            p.dashTime = 0.3f;
            p.dashGravityRatio = 0f;
            p.dashSpeedCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.2f);
        });

        r.player.Move(Vector2.right);
        EnterDashing(r.player);

        Trace trace = new Trace();
        yield return Record(r.player, 0.4f, trace);

        int first = -1;
        int last = -1;
        for (int i = 0; i < trace.states.Count; i++)
        {
            if (trace.states[i] == PlatformerPlayer2D.State.Dashing)
            {
                if (first < 0) first = i;
                last = i;
            }
        }

        Assert.GreaterOrEqual(last - first, 3,
            $"dashSpeedCurve shapes the speed across the dash, so the dash has to last long enough " +
            $"to shape: only {last - first + 1} frames of Dashing were observed. " +
            $"Observed: {trace.Describe()}.");

        float startSpeed = Mathf.Abs(trace.velocities[first].x);
        float endSpeed = Mathf.Abs(trace.velocities[last].x);

        Assert.Less(endSpeed, startSpeed * 0.7f,
            $"A curve falling from 1.0 to 0.2 should leave the dash clearly slower at its end than " +
            $"at its start, but it went from {startSpeed:F3} to {endSpeed:F3} units/s.");

        ForceRestoreInput(r.player, "inputFreezeTimer never fires OnEnd, so acceptInput stays false");
    }

    // EXPECTED RED, same root cause. phase is what drives dashSpeedCurve; it is computed as
    // 1 - timeLeft / totalTime, and totalTime is never assigned, so it is pinned at 0.
    [UnityTest]
    public IEnumerator DashTimerPhase_AdvancesFromZeroToOneAcrossTheDash()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(12), 20f, r, p =>
        {
            p.canDash = true;
            p.dashTime = 0.3f;
        });

        r.player.Move(Vector2.right);
        EnterDashing(r.player);

        float maxPhase = r.player.dashTimer.phase;

        int steps = Mathf.CeilToInt(0.4f / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();
            maxPhase = Mathf.Max(maxPhase, r.player.dashTimer.phase);
        }

        Assert.Greater(maxPhase, 0.5f,
            $"dashTimer.phase should run from 0 to 1 across the dash - it is the input to " +
            $"dashSpeedCurve - but the highest value observed was {maxPhase:F3}.");

        ForceRestoreInput(r.player, "inputFreezeTimer never fires OnEnd, so acceptInput stays false");
    }

    // EXPECTED RED. dashCoolDownTimer is started the same way and therefore never loads, so
    // dashCoolDownTime has no effect at all and the dash can be spammed every frame.
    [UnityTest]
    public IEnumerator DashCoolDown_SecondDashWithinCoolDown_IsRefused()
    {
        PlayerResult r = new PlayerResult();
        DashMessageRecorder recorder = null;

        yield return MakePlayerInAir(Slot(13), 20f, r, p =>
        {
            p.canDash = true;
            p.dashCoolDownTime = 2f;
            recorder = p.gameObject.AddComponent<DashMessageRecorder>();
        });

        r.player.Move(Vector2.right);

        // Dash() is public and ungated, so the frozen input cannot rescue this test: what is being
        // measured is the cooldown alone.
        r.player.Dash();
        Assert.AreEqual(1, recorder.didDash, "Precondition: the first dash should be performed.");

        yield return StepSeconds(0.4f);   // well inside the 2 s cooldown

        r.player.Dash();

        Assert.AreEqual(1, recorder.didDash,
            $"dashCoolDownTime is {r.player.dashCoolDownTime:F3} s, so a second dash 0.4 s later " +
            $"should be refused - but DidDash has been sent {recorder.didDash} time(s).");

        ForceRestoreInput(r.player, "inputFreezeTimer never fires OnEnd, so acceptInput stays false");
    }

    [UnityTest]
    public IEnumerator DashCoolDown_SecondDashAfterCoolDown_IsAllowed()
    {
        PlayerResult r = new PlayerResult();
        DashMessageRecorder recorder = null;

        yield return MakePlayerOnGround(Slot(14), r, p =>
        {
            p.canDash = true;
            p.dashCoolDownTime = 0.3f;
            recorder = p.gameObject.AddComponent<DashMessageRecorder>();
        });

        r.player.Move(Vector2.right);
        r.player.Dash();

        yield return StepSeconds(0.6f);   // past the cooldown

        r.player.Move(Vector2.right);
        r.player.Dash();

        Assert.AreEqual(2, recorder.didDash,
            $"Once dashCoolDownTime ({r.player.dashCoolDownTime:F3} s) has passed, dashing again " +
            $"should be allowed, but DidDash has been sent {recorder.didDash} time(s).");

        ForceRestoreInput(r.player, "inputFreezeTimer never fires OnEnd, so acceptInput stays false");
    }

    // ------------------------------------------------------------------
    // The input freeze
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator DashInputFreeze_DuringTheFreeze_TakesInputAway()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(15), r, p =>
        {
            p.canDash = true;
            p.dashInputFreezeTime = 0.2f;
        });

        r.player.Move(Vector2.right);
        r.player.Dash();

        Assert.IsFalse(r.player.acceptInput,
            "Starting a dash with dashInputFreezeTime set should take input away for its duration.");

        ForceRestoreInput(r.player, "inputFreezeTimer never fires OnEnd, so acceptInput stays false");
        yield return null;
    }

    // EXPECTED RED. inputFreezeTimer is started the same way as the others, so it never loads a
    // duration, never ticks down and never fires OnEnd - and OnEnd is what sets acceptInput back to
    // true. The freeze has no end.
    [UnityTest]
    public IEnumerator DashInputFreeze_AfterDashInputFreezeTime_RestoresInput()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(16), r, p =>
        {
            p.canDash = true;
            p.dashInputFreezeTime = 0.2f;
        });

        r.player.Move(Vector2.right);
        r.player.Dash();

        yield return StepSeconds(0.5f);   // more than twice the freeze

        Assert.IsTrue(r.player.acceptInput,
            $"dashInputFreezeTime is {r.player.dashInputFreezeTime:F3} s, so input should be back " +
            "half a second later - but acceptInput is still false.");
    }

    // EXPECTED RED, and this is the one that matters to a player: the same defect, stated as what
    // it does to the game. One dash and the character never responds to the controller again.
    [UnityTest]
    public IEnumerator Dash_DoesNotPermanentlyDisableInput()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(17), r, p =>
        {
            p.canDash = true;
            p.dashInputFreezeTime = 0.2f;
        });

        r.player.Move(Vector2.right);
        r.player.Dash();

        // Five times the freeze duration, and long past the dash itself.
        yield return StepSeconds(1f);

        Assert.IsTrue(r.player.acceptInput,
            "A single dash must not cost the player control of the character for the rest of the " +
            "game, but a second after dashing acceptInput is still false and every press-side " +
            "input callback is gated on it.");
    }

    // EXPECTED RED. isDashing is driven by dashTimer's OnStart/OnEnd; OnEnd never fires, so the
    // flag latches true while `state` leaves Dashing on the next frame. Anything reading isDashing
    // to pick an animation or suppress damage is stuck in the dash forever.
    [UnityTest]
    public IEnumerator Dash_IsDashingClearsWhenTheDashEnds()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(18), 20f, r, p => p.canDash = true);

        r.player.Move(Vector2.right);
        EnterDashing(r.player);

        Assert.IsTrue(r.player.isDashing, "Precondition: isDashing should be true during the dash.");

        yield return WaitWhileState(r.player, PlatformerPlayer2D.State.Dashing, DashTimeout,
                                    "a character that started a dash");
        yield return StepSeconds(0.5f);

        Assert.IsFalse(r.player.isDashing,
            $"The dash is over - the state is {r.player.state} - but isDashing is still true.");

        ForceRestoreInput(r.player, "inputFreezeTimer never fires OnEnd, so acceptInput stays false");
    }

    // ------------------------------------------------------------------
    // Cancelling a dash
    // ------------------------------------------------------------------

    // EXPECTED RED (PP-12). OnMove calls dashTimer.Cancel() unconditionally, and Utils.Timer.Cancel
    // fires OnCancel and OnEnd even when the timer is already at zero. So every movement message -
    // which, with a stick, is every frame the player is moving - re-runs the dash-end callbacks.
    // Any game code subscribed to dashTimer.OnEnd sees a storm of false dash-ended events.
    [UnityTest]
    public IEnumerator Move_WhileNotDashing_DoesNotFireDashTimerEvents()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(19), r, p => p.canDash = true);

        TimerEventRecorder dashEvents = new TimerEventRecorder(r.player.dashTimer);

        // Ordinary walking input. No dash has been started and none is in progress.
        for (int i = 0; i < 5; i++)
        {
            SendMove(r.player, Vector2.right);
            yield return new WaitForFixedUpdate();
        }

        Assert.AreEqual(0, dashEvents.ended,
            $"Moving the stick while not dashing should not fire the dash timer's callbacks, but " +
            $"OnEnd fired {dashEvents.ended} time(s) and OnCancel {dashEvents.cancelled} time(s) " +
            "across five movement messages.");
    }

    // EXPECTED RED (PP-12). The comment above the cancel says "Cancel dashes only if there is
    // actual playing movement input", but the call is unconditional inside OnMove - so a stick
    // being RELEASED, which arrives as a Move of (0, 0), kills the dash just as surely as a
    // deliberate change of direction.
    [UnityTest]
    public IEnumerator Dash_StickReleaseDuringTheDash_DoesNotCancelIt()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(20), 20f, r, p =>
        {
            p.canDash = true;
            p.dashTime = 0.3f;
            // Zero, so that input stays live and OnMove actually reaches the cancel - which is the
            // code under test. Utils.Timer.Start(0) never fires OnStart, so acceptInput is
            // untouched.
            p.dashInputFreezeTime = 0f;
        });

        r.player.Move(Vector2.right);

        TimerEventRecorder dashEvents = new TimerEventRecorder(r.player.dashTimer);
        EnterDashing(r.player);

        // The player lets go of the stick mid-dash. That is not a command to stop dashing.
        SendMove(r.player, Vector2.zero);

        Assert.AreEqual(0, dashEvents.cancelled,
            $"Releasing the stick is not a movement command, so it should not cancel an in-flight " +
            $"dash, but the dash timer's OnCancel fired {dashEvents.cancelled} time(s).");

        yield return null;
    }

    [UnityTest]
    public IEnumerator Dash_IntoAWallHeadOn_IsCancelled()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(21), r, p =>
        {
            p.canDash = true;
            p.dashTime = 0.5f;
            p.dashSpeedSide = 20f;
        });

        CreateWallBeside(r, 1f);
        yield return Step(2);

        TimerEventRecorder dashEvents = new TimerEventRecorder(r.player.dashTimer);

        r.player.Move(Vector2.right);
        EnterDashing(r.player);

        yield return StepSeconds(0.2f);

        Assert.Greater(dashEvents.cancelled, 0,
            "Dashing straight into a wall should abort the dash rather than grinding against it " +
            "for the rest of dashTime, but the dash timer was never cancelled.");

        ForceRestoreInput(r.player, "inputFreezeTimer never fires OnEnd, so acceptInput stays false");
    }

    [UnityTest]
    public IEnumerator Dash_SpeedFallingBelowMinDashSpeed_AbortsTheDash()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(22), 20f, r, p =>
        {
            p.canDash = true;
            p.dashTime = 0.5f;
            p.dashSpeedSide = 10f;
            // Above the dash's own speed, so the abort triggers on the first check.
            p.minDashSpeed = 15f;
        });

        TimerEventRecorder dashEvents = new TimerEventRecorder(r.player.dashTimer);

        r.player.Move(Vector2.right);
        EnterDashing(r.player);

        yield return StepSeconds(0.2f);

        Assert.Greater(dashEvents.cancelled, 0,
            $"A dash whose speed is at or below minDashSpeed ({r.player.minDashSpeed:F3}) should be " +
            "aborted, but the dash timer was never cancelled.");

        ForceRestoreInput(r.player, "inputFreezeTimer never fires OnEnd, so acceptInput stays false");
    }

    // ------------------------------------------------------------------
    // Gravity during a dash
    // ------------------------------------------------------------------

    // EXPECTED RED. dashGravityRatio 0 means "no gravity during the dash", and the dash is supposed
    // to last dashTime - so a horizontal dash should travel a straight line for that whole span.
    // Because the dash ends after a single frame, the character is back in free fall almost
    // immediately and the line bends.
    [UnityTest]
    public IEnumerator DashGravityRatio_Zero_KeepsTheDashStraightForItsWholeDuration()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(23), 20f, r, p =>
        {
            p.canDash = true;
            p.dashTime = 0.3f;
            p.dashGravityRatio = 0f;
        });

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released high above the floor");

        r.player.Move(Vector2.right);
        EnterDashing(r.player);

        float startY = r.player.position.y;
        yield return StepSeconds(r.player.dashTime);

        float drop = startY - r.player.position.y;

        Assert.Less(drop, 0.15f,
            $"With dashGravityRatio 0 the character should hold its height for the whole of " +
            $"dashTime ({r.player.dashTime:F3} s), but it fell {drop:F3} units during it.");

        ForceRestoreInput(r.player, "inputFreezeTimer never fires OnEnd, so acceptInput stays false");
    }

    // EXPECTED RED (PP-16), the other half of the same field. ApplyAirMotion reassigns
    // `velocity = CalculateDashVelocity()` from scratch every frame, which discards whatever
    // gravity the base class integrated on the previous one - so gravity can only ever contribute a
    // single frame of displacement and the documented "1 means full gravity" is unreachable.
    [UnityTest]
    public IEnumerator DashGravityRatio_One_ArcsMoreThanRatioZero()
    {
        PlayerResult heavy = new PlayerResult();
        yield return MakePlayerInAir(Slot(24), 20f, heavy, p =>
        {
            p.canDash = true;
            p.dashTime = 0.3f;
            p.dashGravityRatio = 1f;
        });
        yield return WaitForState(heavy.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released high above the floor");

        heavy.player.Move(Vector2.right);
        EnterDashing(heavy.player);
        float heavyStartY = heavy.player.position.y;
        yield return StepSeconds(heavy.player.dashTime);
        float heavyDrop = heavyStartY - heavy.player.position.y;

        PlayerResult light = new PlayerResult();
        yield return MakePlayerInAir(Slot(25), 20f, light, p =>
        {
            p.canDash = true;
            p.dashTime = 0.3f;
            p.dashGravityRatio = 0f;
        });
        yield return WaitForState(light.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released high above the floor");

        light.player.Move(Vector2.right);
        EnterDashing(light.player);
        float lightStartY = light.player.position.y;
        yield return StepSeconds(light.player.dashTime);
        float lightDrop = lightStartY - light.player.position.y;

        Assert.Greater(heavyDrop, lightDrop + 0.2f,
            $"dashGravityRatio decides how much gravity acts during the dash, so a ratio of 1 " +
            $"should arc visibly more than a ratio of 0 - but over the same dashTime they dropped " +
            $"{heavyDrop:F3} and {lightDrop:F3} units.");

        ForceRestoreInput(heavy.player, "inputFreezeTimer never fires OnEnd, so acceptInput stays false");
        ForceRestoreInput(light.player, "inputFreezeTimer never fires OnEnd, so acceptInput stays false");
    }

    // ------------------------------------------------------------------
    // Ending a dash
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Dash_EndingGrounded_ReturnsToAGroundState()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(26), r, p =>
        {
            p.canDash = true;
            p.dashTime = 0.1f;
        });

        r.player.Move(Vector2.right);
        EnterDashing(r.player);

        yield return WaitWhileState(r.player, PlatformerPlayer2D.State.Dashing, DashTimeout,
                                    "a grounded character that dashed");
        yield return Step(3);

        bool groundState = r.player.state == PlatformerPlayer2D.State.Walking
                        || r.player.state == PlatformerPlayer2D.State.Running;

        Assert.IsTrue(groundState,
            $"A dash that ends with the character on the ground should hand it back to a ground " +
            $"state, but it is {r.player.state}.");

        ForceRestoreInput(r.player, "inputFreezeTimer never fires OnEnd, so acceptInput stays false");
    }

    [UnityTest]
    public IEnumerator Dash_EndingAirborne_ReturnsToFalling()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(27), 20f, r, p =>
        {
            p.canDash = true;
            p.dashTime = 0.1f;
        });

        r.player.Move(Vector2.right);
        EnterDashing(r.player);

        yield return WaitWhileState(r.player, PlatformerPlayer2D.State.Dashing, DashTimeout,
                                    "an airborne character that dashed");
        yield return Step(3);

        Assert.AreEqual(PlatformerPlayer2D.State.Falling, r.player.state,
            $"A dash that ends in mid-air should leave the character falling, but it is " +
            $"{r.player.state}.");

        ForceRestoreInput(r.player, "inputFreezeTimer never fires OnEnd, so acceptInput stays false");
    }

    [UnityTest]
    public IEnumerator Dash_SendsDidDash()
    {
        PlayerResult r = new PlayerResult();
        DashMessageRecorder recorder = null;

        yield return MakePlayerOnGround(Slot(28), r, p =>
        {
            p.canDash = true;
            recorder = p.gameObject.AddComponent<DashMessageRecorder>();
        });

        r.player.Move(Vector2.right);
        r.player.Dash();

        Assert.AreEqual(1, recorder.didDash,
            $"Starting a dash should send DidDash exactly once, but it arrived {recorder.didDash} " +
            "time(s).");

        ForceRestoreInput(r.player, "inputFreezeTimer never fires OnEnd, so acceptInput stays false");
        yield return null;
    }
}
