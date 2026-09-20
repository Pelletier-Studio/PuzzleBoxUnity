using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PuzzleBox;

/// <summary>
/// SPECIFICATION - PlatformerPlayer2D jumping.
///
///  1. Jump height is controlled by how long the button is held: a tap reaches minJumpHeight, a
///     hold reaches maxJumpHeight, and anything in between lands in between. Height is monotonic
///     in hold duration.
///  2. The height fields are honoured even when they are set nonsensically. minJumpHeight above
///     maxJumpHeight clamps rather than inverting, and a zero height refuses the jump instead of
///     dividing by it.
///  3. Releasing the jump button increases gravity to cut the jump short. That increase must never
///     reverse the character's direction of travel within a single frame, at any gravityModifier.
///  4. Releasing the jump button when no jump is in progress does nothing.
///  5. jumpAngle leans the launch away from vertical, toward the way the character is facing, and
///     is ADDED to whatever horizontal velocity the character already had.
///  6. jumpHeightSpeedBoost adds up to its full value in world units to maxJumpHeight, scaled by
///     how close the character is to its top ground speed.
///  7. Coyote time (fallJumpTimeLimit) grants a full GROUND jump - not an air jump - for a grace
///     period after leaving the ground, and grants nothing once the period has passed.
///  8. Jump buffering (jumpBufferTime) remembers a press made shortly before landing and performs
///     it on touchdown, exactly once, and never while input is disabled.
///  9. maxAirJumps air jumps are available per airborne period, each at airJumpHeightRatio of a
///     normal jump, and the allowance is refilled by touching the ground.
/// 10. Landing and jumping raise OnLanded / OnJumped and send DidLand / DidJump, once each.
///
/// THIS IS A TDD RED PHASE. Several tests below are expected to fail against the current
/// implementation; each says so above its declaration, with the register ID where one exists.
/// Do not make a failing test here pass by weakening its assertion - the failures are the point.
///
/// RED LIST (expected to fail; transcribe the observed set after the first full run):
///   JumpRelease_UnderAGravityModifier_NeverReversesDirectionInOneFrame     PP-09
///   JumpRelease_WhileNotJumping_DoesNotTouchGravityMultiplier              PP-11
///   JumpAngle_WithPureUpInput_UsesTheLastHorizontalFacing                  PP-20
///   JumpGroundCheckDistance_FallingDiagonally_ProbesTowardTheGround
///   Landed_Event_FiresWithAGroundState
///
/// NOT TESTED HERE, deliberately:
///   PP-41 (Jump(false, true) passes a meaningless bufferInput argument). `bufferInput` is only
///   read inside the `if (jumpState)` branch, so no input can distinguish Jump(false, true) from
///   Jump(false, false). A test would assert nothing. It is a readability defect, not a behavioural
///   one - fix it on code-reading grounds or leave it.
/// </summary>
public class TestPlatformerJumping : PlatformerTestFixture
{
    private const float Lane = 15000f;

    private static float Slot(int n)
    {
        return Lane + n * 50f;
    }

    /// <summary>
    /// Counts the component's C# events and its SendMessage notifications side by side, and records
    /// what `state` said at the moment each event fired. Lives on the character's own GameObject so
    /// that SendMessage reaches it.
    /// </summary>
    private class PlayerEventRecorder : MonoBehaviour
    {
        public int jumped;
        public int landed;
        public int didJump;
        public int didLand;

        public readonly List<PlatformerPlayer2D.State> stateAtLanded = new List<PlatformerPlayer2D.State>();
        public readonly List<PlatformerPlayer2D.State> stateAtJumped = new List<PlatformerPlayer2D.State>();

        private PlatformerPlayer2D player;

        public void Hook(PlatformerPlayer2D p)
        {
            player = p;
            p.OnJumped += HandleJumped;
            p.OnLanded += HandleLanded;
        }

        private void HandleJumped()
        {
            jumped++;
            stateAtJumped.Add(player.state);
        }

        private void HandleLanded()
        {
            landed++;
            stateAtLanded.Add(player.state);
        }

        // Received via SendMessage from the component.
        private void DidJump()
        {
            didJump++;
        }

        private void DidLand()
        {
            didLand++;
        }
    }

    private PlayerEventRecorder AttachRecorder(PlatformerPlayer2D p)
    {
        PlayerEventRecorder recorder = p.gameObject.AddComponent<PlayerEventRecorder>();
        recorder.Hook(p);
        return recorder;
    }

    // ------------------------------------------------------------------
    // Variable jump height
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Jump_HeldToApex_ReachesMaxJumpHeight()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(0), r);

        r.player.Jump(true);   // held: never released

        ApexResult apex = new ApexResult();
        yield return MeasureApex(r.player, 3f, apex);

        Assert.IsTrue(apex.confirmedDescent,
            $"Staging error: the character never came back down within 3 s; apex was " +
            $"{apex.apexHeight:F3} above its start.");

        Assert.GreaterOrEqual(apex.apexHeight, r.player.maxJumpHeight - PositionTolerance,
            $"A held jump should reach maxJumpHeight ({r.player.maxJumpHeight:F3}), but it topped " +
            $"out {apex.apexHeight:F3} above the ground.");
        Assert.LessOrEqual(apex.apexHeight, r.player.maxJumpHeight + JumpHeightTolerance,
            $"A held jump should reach maxJumpHeight ({r.player.maxJumpHeight:F3}) and no more, but " +
            $"it reached {apex.apexHeight:F3}.");
    }

    [UnityTest]
    public IEnumerator Jump_ReleasedImmediately_ReachesMinJumpHeightAndNoMore()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(1), r);

        r.player.Jump(true);
        r.player.Jump(false);   // released in the same frame: the shortest possible press

        ApexResult apex = new ApexResult();
        yield return MeasureApex(r.player, 3f, apex);

        Assert.IsTrue(apex.confirmedDescent,
            "Staging error: the character never came back down within 3 s.");

        Assert.GreaterOrEqual(apex.apexHeight, r.player.minJumpHeight - PositionTolerance,
            $"A tapped jump should still reach minJumpHeight ({r.player.minJumpHeight:F3}), but it " +
            $"only reached {apex.apexHeight:F3}.");
        Assert.LessOrEqual(apex.apexHeight, r.player.minJumpHeight + JumpHeightTolerance,
            $"A jump released immediately should reach minJumpHeight ({r.player.minJumpHeight:F3}) " +
            $"and no more, but it reached {apex.apexHeight:F3}.");
    }

    [UnityTest]
    public IEnumerator Jump_ReleasedMidRise_ApexIsBetweenMinAndMaxHeight()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(2), r);

        r.player.Jump(true);
        yield return StepSeconds(0.15f);
        r.player.Jump(false);

        ApexResult apex = new ApexResult();
        yield return MeasureApex(r.player, 3f, apex);

        Assert.Greater(apex.apexHeight, r.player.minJumpHeight + PositionTolerance,
            $"A jump held for 0.15 s should clear minJumpHeight ({r.player.minJumpHeight:F3}), but " +
            $"it reached only {apex.apexHeight:F3}.");
        Assert.Less(apex.apexHeight, r.player.maxJumpHeight,
            $"A jump released part way up should fall short of maxJumpHeight " +
            $"({r.player.maxJumpHeight:F3}), but it reached {apex.apexHeight:F3}.");
    }

    [UnityTest]
    public IEnumerator Jump_HeightIsMonotonicInHoldDuration()
    {
        float[] holdTimes = new float[] { 0f, 0.08f, 0.16f, 0.3f };
        float[] heights = new float[holdTimes.Length];

        for (int i = 0; i < holdTimes.Length; i++)
        {
            PlayerResult r = new PlayerResult();
            yield return MakePlayerOnGround(Slot(3 + i), r);

            r.player.Jump(true);
            if (holdTimes[i] > 0f)
            {
                yield return StepSeconds(holdTimes[i]);
            }
            r.player.Jump(false);

            ApexResult apex = new ApexResult();
            yield return MeasureApex(r.player, 3f, apex);

            Assert.IsTrue(apex.confirmedDescent,
                $"Staging error: the {holdTimes[i]:F2} s hold never came back down.");

            heights[i] = apex.apexHeight;
        }

        for (int i = 1; i < heights.Length; i++)
        {
            Assert.Greater(heights[i], heights[i - 1] - PositionTolerance,
                $"Jump height must not decrease as the button is held longer, but holding for " +
                $"{holdTimes[i]:F2} s reached {heights[i]:F3} while {holdTimes[i - 1]:F2} s reached " +
                $"{heights[i - 1]:F3}.");
        }

        Assert.Greater(heights[heights.Length - 1], heights[0] + 0.5f,
            $"Holding the button should make a clearly higher jump than tapping it, but the longest " +
            $"hold reached {heights[heights.Length - 1]:F3} against the tap's {heights[0]:F3}.");
    }

    // ------------------------------------------------------------------
    // Degenerate height settings
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Jump_WithMinJumpHeightAboveMax_ClampsInsteadOfInverting()
    {
        // minJumpHeight above maxJumpHeight is nonsense, but nothing stops a designer entering it.
        // The dangerous outcome is an inverted jump - where RELEASING the button makes the character
        // rise higher than holding it - which is what an unclamped breakGravityMultiplier below 1
        // would produce.
        PlayerResult tapped = new PlayerResult();
        yield return MakePlayerOnGround(Slot(7), tapped, p =>
        {
            p.minJumpHeight = 5f;
            p.maxJumpHeight = 2f;
        });

        tapped.player.Jump(true);
        tapped.player.Jump(false);

        ApexResult tappedApex = new ApexResult();
        yield return MeasureApex(tapped.player, 3f, tappedApex);

        PlayerResult held = new PlayerResult();
        yield return MakePlayerOnGround(Slot(8), held, p =>
        {
            p.minJumpHeight = 5f;
            p.maxJumpHeight = 2f;
        });

        held.player.Jump(true);

        ApexResult heldApex = new ApexResult();
        yield return MeasureApex(held.player, 3f, heldApex);

        Assert.LessOrEqual(tappedApex.apexHeight, heldApex.apexHeight + PositionTolerance,
            $"Releasing the jump button must never produce a HIGHER jump than holding it. With " +
            $"minJumpHeight 5 above maxJumpHeight 2, the tap reached {tappedApex.apexHeight:F3} and " +
            $"the hold reached {heldApex.apexHeight:F3}.");

        Assert.LessOrEqual(heldApex.apexHeight, 2f + JumpHeightTolerance,
            $"With minJumpHeight clamped down to maxJumpHeight, the jump should reach the smaller " +
            $"of the two (2), but it reached {heldApex.apexHeight:F3}.");
    }

    [UnityTest]
    public IEnumerator Jump_WithZeroMaxJumpHeight_IsRefused()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerOnGround(Slot(9), r, p =>
        {
            p.maxJumpHeight = 0f;
            recorder = AttachRecorder(p);
        });

        r.player.Jump(true);

        yield return StepSeconds(0.5f);

        Assert.AreEqual(0, recorder.jumped,
            $"A character with maxJumpHeight 0 cannot jump, but OnJumped fired {recorder.jumped} " +
            "time(s). The zero must be refused, not divided by.");
        Assert.IsTrue(r.player.isGrounded,
            "A refused jump should leave the character on the ground.");
        Assert.Less(Mathf.Abs(r.player.position.y - r.restPosition.y), PositionTolerance,
            $"A refused jump should not move the character: it is {r.player.position.y:F3} against " +
            $"a resting {r.restPosition.y:F3}.");
    }

    // ------------------------------------------------------------------
    // The jump-break gravity
    // ------------------------------------------------------------------

    // EXPECTED RED (PP-09). ApplyAirMotion guards the jump break with
    //     float gravityStep = Physics2D.gravity.y * breakGravityMultiplier * Time.fixedDeltaTime;
    // which omits gravityModifier, while the base class applies
    //     velocity += Physics2D.gravity * dt * gravityMultiplier * gravityModifier.
    // With gravityModifier 2 the real step is twice the predicted one, so the guard lets through a
    // frame it was written to intercept and the character's vertical velocity jumps straight from
    // clearly rising to clearly falling.
    //
    // The release timing is swept because the guard only misfires when velocity.y happens to land
    // in the window between the predicted and the actual step. One release time might miss it;
    // eight will not.
    [UnityTest]
    public IEnumerator JumpRelease_UnderAGravityModifier_NeverReversesDirectionInOneFrame()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(10), r, p => p.gravityModifier = 2f);

        for (int attempt = 0; attempt < 8; attempt++)
        {
            yield return WaitUntilGrounded(r.player, StateTimeout,
                                           $"the character before release sweep attempt {attempt}");

            r.player.Jump(true);
            yield return StepSeconds(attempt * Time.fixedDeltaTime);
            r.player.Jump(false);

            Trace trace = new Trace();
            yield return Record(r.player, 0.6f, trace);

            for (int i = 0; i < trace.velocities.Count - 1; i++)
            {
                float before = trace.velocities[i].y;
                float after = trace.velocities[i + 1].y;

                bool reversedInOneFrame = before > 0.1f && after < -0.1f;

                Assert.IsFalse(reversedInOneFrame,
                    $"On attempt {attempt}, step {i}: releasing the jump button flipped the " +
                    $"character from rising at {before:F3} units/s to falling at {after:F3} units/s " +
                    "in a single frame. The break-gravity guard is meant to clamp that frame to " +
                    "zero; it mispredicts the step because it omits gravityModifier " +
                    $"({r.player.gravityModifier:F1}).");
            }

            // Let it land before the next attempt.
            yield return StepSeconds(1.5f);
        }
    }

    // EXPECTED RED (PP-11). Jump(false) writes breakGravityMultiplier into gravityMultiplier
    // unconditionally, whether or not a jump is in progress. Every motion path overwrites
    // gravityMultiplier on the next FixedUpdate, so today this is latent rather than visible in
    // motion - but the write happens, it is observable on the public field, and it is the loaded
    // half of the wallSlideWaitTimer hole: the WallSliding branch has no `else`, so the moment that
    // timer is ever started this stale value becomes the character's actual gravity.
    [UnityTest]
    public IEnumerator JumpRelease_WhileNotJumping_DoesNotTouchGravityMultiplier()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(11), r);

        // One real jump first, so breakGravityMultiplier holds a value that differs from normal
        // gravity (it becomes maxJumpHeight / minJumpHeight = 3).
        r.player.Jump(true);
        yield return StepSeconds(0.1f);
        r.player.Jump(false);

        yield return WaitUntilGrounded(r.player, 3f, "the character after its first jump");
        yield return Step(2);

        float gravityWhileWalking = r.player.gravityMultiplier;
        Assert.AreEqual(1f, gravityWhileWalking, 0.01f,
            $"Precondition: a walking character should be under normal gravity, but " +
            $"gravityMultiplier is {gravityWhileWalking:F3}.");

        // A stray button release with no jump in flight. Read the field synchronously, before the
        // next FixedUpdate overwrites it.
        r.player.Jump(false);

        Assert.AreEqual(gravityWhileWalking, r.player.gravityMultiplier, 0.01f,
            $"Releasing the jump button when no jump is in progress should change nothing, but " +
            $"gravityMultiplier went from {gravityWhileWalking:F3} to " +
            $"{r.player.gravityMultiplier:F3} - the break gravity from the previous jump.");
    }

    // ------------------------------------------------------------------
    // jumpAngle
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator JumpAngle_Zero_ProducesAPurelyVerticalLaunch()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(12), r, p => p.jumpAngle = 0f);

        r.player.Jump(true);

        // Read velocity synchronously: base.FixedUpdate rewrites it from realized motion, so this
        // is the only point at which the launch vector itself is observable.
        Vector2 launch = r.player.velocity;

        Assert.Greater(launch.y, 1f,
            $"Precondition: the jump should have launched the character upward, but velocity is {launch}.");
        Assert.AreEqual(0f, launch.x, SpeedTolerance,
            $"With jumpAngle 0 the launch should be straight up, but velocity.x is {launch.x:F3}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator JumpAngle_PositiveWhileFacingRight_LeansRight()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(13), r, p => p.jumpAngle = 30f);

        // Face right without building up speed, so the lean is the only horizontal contribution.
        r.player.Move(Vector2.right);
        yield return Step(2);
        r.player.Move(Vector2.zero);
        yield return StepSeconds(0.5f);

        Assert.AreEqual(1f, r.player.facingDirection.x, DirectionTolerance,
            "Precondition: the character should be facing right.");

        float horizontalBefore = r.player.velocity.x;
        r.player.Jump(true);
        Vector2 launch = r.player.velocity;

        float leaned = launch.x - horizontalBefore;
        float expectedRatio = Mathf.Tan(30f * Mathf.Deg2Rad);

        Assert.Greater(leaned, 0f,
            $"A jumpAngle of 30 degrees while facing right should lean the launch right, but the " +
            $"horizontal component changed by {leaned:F3}.");
        Assert.AreEqual(expectedRatio, leaned / launch.y, 0.05f,
            $"The lean should be tan(jumpAngle) of the vertical launch speed " +
            $"({expectedRatio:F3}), but it is {leaned / launch.y:F3} " +
            $"(horizontal {leaned:F3}, vertical {launch.y:F3}).");

        yield return null;
    }

    [UnityTest]
    public IEnumerator JumpAngle_PositiveWhileFacingLeft_LeansLeft()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(14), r, p => p.jumpAngle = 30f);

        r.player.Move(Vector2.left);
        yield return Step(2);
        r.player.Move(Vector2.zero);
        yield return StepSeconds(0.5f);

        Assert.AreEqual(-1f, r.player.facingDirection.x, DirectionTolerance,
            "Precondition: the character should be facing left.");

        float horizontalBefore = r.player.velocity.x;
        r.player.Jump(true);

        float leaned = r.player.velocity.x - horizontalBefore;

        Assert.Less(leaned, 0f,
            $"A jumpAngle of 30 degrees while facing left should lean the launch left, but the " +
            $"horizontal component changed by {leaned:F3}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator JumpAngle_IsAddedToExistingHorizontalVelocityNotAssigned()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(15), r, p => p.jumpAngle = 30f);

        r.player.Move(Vector2.right);
        yield return StepSeconds(1f);

        float runningSpeed = r.player.velocity.x;
        Assert.Greater(runningSpeed, 1f,
            $"Precondition: the character should be walking before it jumps, but velocity.x is " +
            $"{runningSpeed:F3}.");

        r.player.Jump(true);
        float launched = r.player.velocity.x;

        Assert.Greater(launched, runningSpeed + 0.5f,
            $"The jump lean should ADD to the character's existing horizontal velocity, not replace " +
            $"it: it was walking at {runningSpeed:F3} and launched at {launched:F3}.");

        yield return null;
    }

    // EXPECTED RED (PP-20). The launch angle is taken from Mathf.Sign(facingDirection.x), and
    // facingDirection becomes (0, 1) on a pure up input. Mathf.Sign(0) is +1 in Unity, so a
    // character that was facing LEFT launches as though it were facing right.
    [UnityTest]
    public IEnumerator JumpAngle_WithPureUpInput_UsesTheLastHorizontalFacing()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(16), r, p => p.jumpAngle = 30f);

        r.player.Move(Vector2.left);
        yield return StepSeconds(0.5f);
        r.player.Move(Vector2.zero);
        yield return StepSeconds(0.5f);

        Assert.AreEqual(-1f, r.player.facingDirection.x, DirectionTolerance,
            "Precondition: the character should be facing left before the up input.");

        // Now hold straight up and jump. The character has not turned around.
        r.player.Move(Vector2.up);
        yield return Step(2);

        float horizontalBefore = r.player.velocity.x;
        r.player.Jump(true);
        float leaned = r.player.velocity.x - horizontalBefore;

        Assert.Less(leaned, 0f,
            $"Pushing straight up is not turning around: a character that was facing left should " +
            $"still lean left when it jumps, but the launch leaned {leaned:F3} (positive is right). " +
            "facingDirection is (0, 1) after a pure up input, and Mathf.Sign(0) is +1.");

        yield return null;
    }

    // ------------------------------------------------------------------
    // jumpHeightSpeedBoost
    // ------------------------------------------------------------------

    // The three boost tests set velocity.x directly rather than running the character up to speed:
    // Jump() reads velocity.x at the moment of the call, so this gives an exact speed ratio without
    // needing a floor long enough to accelerate along.
    private IEnumerator MeasureBoostedJump(float slotX, float horizontalSpeed, ApexResult apex)
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(slotX, r, p => p.jumpHeightSpeedBoost = 2f);

        r.player.velocity = new Vector2(horizontalSpeed, 0f);
        r.player.Jump(true);

        yield return MeasureApex(r.player, 3f, apex);

        Assert.IsTrue(apex.confirmedDescent,
            $"Staging error: the boosted jump from {horizontalSpeed:F3} units/s never came back down.");
    }

    [UnityTest]
    public IEnumerator JumpHeightSpeedBoost_AtFullGroundSpeed_AddsTheFullBoost()
    {
        ApexResult apex = new ApexResult();
        // runSpeed is the larger of the two ground speeds, so this is a speed ratio of exactly 1.
        yield return MeasureBoostedJump(Slot(17), 10f, apex);

        // maxJumpHeight 3 + jumpHeightSpeedBoost 2, as world units, not as a multiplier.
        Assert.AreEqual(5f, apex.apexHeight, JumpHeightTolerance,
            $"At full ground speed the boost adds its whole value to maxJumpHeight, so the jump " +
            $"should reach 3 + 2 = 5 units, but it reached {apex.apexHeight:F3}.");
    }

    [UnityTest]
    public IEnumerator JumpHeightSpeedBoost_AtHalfGroundSpeed_AddsHalfTheBoost()
    {
        ApexResult apex = new ApexResult();
        yield return MeasureBoostedJump(Slot(18), 5f, apex);

        Assert.AreEqual(4f, apex.apexHeight, JumpHeightTolerance,
            $"At half of the top ground speed the boost should contribute half its value, so the " +
            $"jump should reach 3 + 1 = 4 units, but it reached {apex.apexHeight:F3}.");
    }

    [UnityTest]
    public IEnumerator JumpHeightSpeedBoost_FromRest_AddsNothing()
    {
        ApexResult apex = new ApexResult();
        yield return MeasureBoostedJump(Slot(19), 0f, apex);

        Assert.AreEqual(3f, apex.apexHeight, JumpHeightTolerance,
            $"A stationary character earns no speed boost, so its jump should reach maxJumpHeight " +
            $"(3 units), but it reached {apex.apexHeight:F3}.");
    }

    // The boost is unbounded in principle - a fast moving platform or a dash can hand the character
    // a speed far above its own top speed. speedRatio is clamped at 1, so the boost must be too.
    [UnityTest]
    public IEnumerator JumpHeightSpeedBoost_AboveTopGroundSpeed_IsCappedAtTheFullBoost()
    {
        ApexResult apex = new ApexResult();
        yield return MeasureBoostedJump(Slot(20), 40f, apex);

        Assert.LessOrEqual(apex.apexHeight, 5f + JumpHeightTolerance,
            $"The speed boost is capped at its full value, so even at four times the character's " +
            $"top ground speed the jump should not exceed 3 + 2 = 5 units, but it reached " +
            $"{apex.apexHeight:F3}.");
    }

    // ------------------------------------------------------------------
    // Coyote time
    //
    // The grace period has to outlast the state machine's own lag. `state` is updated from the
    // PREVIOUS frame's isGrounded, so a character takes a frame or two to report Falling after it
    // actually leaves the ground - and while `state` still says Walking, CanJump returns true
    // unconditionally, which is a different mechanism entirely. These tests therefore wait for
    // Falling before pressing jump, and use a grace period comfortably longer than that lag.
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator CoyoteTime_JumpWithinFallJumpTimeLimit_IsAFullGroundJump()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(21), r, p =>
        {
            p.fallJumpTimeLimit = 0.2f;
            p.maxAirJumps = 0;     // so that anything granted here is coyote time, not an air jump
        }, ShortFloorWidth);

        r.player.Move(Vector2.right);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character walking off the end of a short floor");

        Assert.Less(r.player.timeInAir, r.player.fallJumpTimeLimit,
            $"Staging error: the character took {r.player.timeInAir:F3} s to report Falling, which " +
            $"is already past fallJumpTimeLimit ({r.player.fallJumpTimeLimit:F3}). Lengthen the " +
            "grace period for this test.");

        float launchY = r.player.position.y;
        r.player.Jump(true);

        Assert.AreEqual(PlatformerPlayer2D.State.Jumping, r.player.state,
            $"A jump inside the coyote window should be granted, but the state is {r.player.state}.");

        ApexResult apex = new ApexResult();
        yield return MeasureApex(r.player, 3f, apex);

        float rise = apex.apexY - launchY;
        Assert.GreaterOrEqual(rise, r.player.maxJumpHeight - PositionTolerance,
            $"Coyote time grants a full GROUND jump, not a reduced air jump, so a held press should " +
            $"lift the character maxJumpHeight ({r.player.maxJumpHeight:F3}) above where it was " +
            $"pressed - but it rose only {rise:F3}.");
    }

    [UnityTest]
    public IEnumerator CoyoteTime_JumpAfterTheLimit_IsRefused()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerOnGround(Slot(22), r, p =>
        {
            p.fallJumpTimeLimit = 0.1f;
            p.maxAirJumps = 0;
            recorder = AttachRecorder(p);
        }, ShortFloorWidth);

        r.player.Move(Vector2.right);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character walking off the end of a short floor");

        // Well past the grace period.
        yield return StepSeconds(0.5f);

        Assert.Greater(r.player.timeInAir, r.player.fallJumpTimeLimit,
            "Precondition: the coyote window should have expired by now.");

        r.player.Jump(true, false);   // no buffering: this press is about CanJump alone

        Assert.AreEqual(0, recorder.jumped,
            $"Once fallJumpTimeLimit ({r.player.fallJumpTimeLimit:F3}) has passed and no air jumps " +
            $"are available, a press should be refused, but OnJumped fired {recorder.jumped} time(s) " +
            $"at timeInAir {r.player.timeInAir:F3}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator CoyoteTime_ZeroLimit_DisablesTheGracePeriod()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerOnGround(Slot(23), r, p =>
        {
            p.fallJumpTimeLimit = 0f;
            p.maxAirJumps = 0;
            recorder = AttachRecorder(p);
        }, ShortFloorWidth);

        r.player.Move(Vector2.right);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character walking off the end of a short floor");

        r.player.Jump(true, false);

        Assert.AreEqual(0, recorder.jumped,
            $"With fallJumpTimeLimit 0 there is no grace period, so a press made after the character " +
            $"is already falling should be refused; OnJumped fired {recorder.jumped} time(s).");

        yield return null;
    }

    // ------------------------------------------------------------------
    // Jump buffering
    //
    // jumpBufferTime is latched into the buffer's duration by Start(), so every test here sets it
    // in the configure callback rather than afterwards.
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator JumpBuffer_PressedJustBeforeLanding_FiresOnLanding()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerInAir(Slot(24), 3f, r, p =>
        {
            p.jumpBufferTime = 0.4f;
            p.maxAirJumps = 0;
            p.fallJumpTimeLimit = 0f;
            recorder = AttachRecorder(p);
        });

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released above the floor");

        // Close enough to the floor that the press is still inside the buffer window on touchdown,
        // but far enough that it is unambiguously refused when it is made.
        yield return StepSeconds(0.15f);

        r.player.Jump(true);
        Assert.AreEqual(0, recorder.jumped,
            "Precondition: the press should be refused in mid-air and buffered, not performed.");

        yield return WaitUntilGrounded(r.player, StateTimeout, "the falling character");
        yield return Step(3);

        Assert.AreEqual(1, recorder.jumped,
            $"A press made inside jumpBufferTime ({r.player.jumpBufferTime:F3}) of landing should be " +
            $"performed on touchdown, but OnJumped fired {recorder.jumped} time(s).");
    }

    [UnityTest]
    public IEnumerator JumpBuffer_PressedBeyondJumpBufferTime_DoesNotFire()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerInAir(Slot(25), 8f, r, p =>
        {
            p.jumpBufferTime = 0.05f;
            p.maxAirJumps = 0;
            p.fallJumpTimeLimit = 0f;
            recorder = AttachRecorder(p);
        });

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released well above the floor");

        r.player.Jump(true);
        Assert.AreEqual(0, recorder.jumped,
            "Precondition: the press should be refused in mid-air.");

        yield return WaitUntilGrounded(r.player, 3f, "the falling character");
        yield return Step(3);

        Assert.AreEqual(0, recorder.jumped,
            $"A press made far more than jumpBufferTime ({r.player.jumpBufferTime:F3}) before " +
            $"landing should have expired, but OnJumped fired {recorder.jumped} time(s) on touchdown.");
    }

    [UnityTest]
    public IEnumerator JumpBuffer_FiresExactlyOnce()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerInAir(Slot(26), 3f, r, p =>
        {
            p.jumpBufferTime = 0.4f;
            p.maxAirJumps = 0;
            p.fallJumpTimeLimit = 0f;
            recorder = AttachRecorder(p);
        });

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released above the floor");
        yield return StepSeconds(0.15f);

        r.player.Jump(true);

        yield return WaitUntilGrounded(r.player, StateTimeout, "the falling character");
        yield return StepSeconds(0.5f);

        Assert.AreEqual(1, recorder.jumped,
            $"The buffer holds one press, so landing should produce exactly one jump, but OnJumped " +
            $"fired {recorder.jumped} time(s). The buffer is re-read every FixedUpdate while it " +
            "still has a value.");
    }

    [UnityTest]
    public IEnumerator JumpBuffer_WhileInputDisabled_DoesNotFire()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerInAir(Slot(27), 3f, r, p =>
        {
            p.jumpBufferTime = 0.4f;
            p.maxAirJumps = 0;
            p.fallJumpTimeLimit = 0f;
            recorder = AttachRecorder(p);
        });

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released above the floor");
        yield return StepSeconds(0.15f);

        r.player.Jump(true);

        // A cutscene, a death, a pause - anything that takes control away between the press and
        // the landing.
        r.player.acceptInput = false;

        yield return WaitUntilGrounded(r.player, StateTimeout, "the falling character");
        yield return Step(3);

        Assert.AreEqual(0, recorder.jumped,
            $"A buffered press must not fire while input is disabled, but OnJumped fired " +
            $"{recorder.jumped} time(s) after acceptInput was cleared.");
    }

    // ------------------------------------------------------------------
    // Air jumps
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator AirJump_WhenAllowed_LaunchesAtAirJumpHeightRatio()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(28), 10f, r, p =>
        {
            p.maxAirJumps = 1;
            p.airJumpHeightRatio = 0.5f;
            p.fallJumpTimeLimit = 0f;
        });

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released high above the floor");
        yield return StepSeconds(0.2f);

        float launchY = r.player.position.y;
        r.player.Jump(true);

        Assert.AreEqual(PlatformerPlayer2D.State.Jumping, r.player.state,
            $"An air jump should be granted while maxAirJumps remain, but the state is {r.player.state}.");

        ApexResult apex = new ApexResult();
        yield return MeasureApex(r.player, 3f, apex);

        // Launch speed is scaled by airJumpHeightRatio, and height goes as the square of speed.
        float expectedRise = r.player.maxJumpHeight * r.player.airJumpHeightRatio * r.player.airJumpHeightRatio;

        Assert.AreEqual(expectedRise, apex.apexY - launchY, JumpHeightTolerance,
            $"An air jump launches at airJumpHeightRatio ({r.player.airJumpHeightRatio:F2}) of the " +
            $"normal jump speed, so it should rise {expectedRise:F3} units, but it rose " +
            $"{apex.apexY - launchY:F3}.");
    }

    [UnityTest]
    public IEnumerator AirJump_CountIsLimitedByMaxAirJumps()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerInAir(Slot(29), 14f, r, p =>
        {
            p.maxAirJumps = 1;
            p.fallJumpTimeLimit = 0f;
            recorder = AttachRecorder(p);
        });

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released high above the floor");

        // First air jump: granted.
        r.player.Jump(true, false);
        Assert.AreEqual(1, recorder.jumped,
            "Precondition: the first air jump should be granted.");

        // Let it come back down to Falling, then try again without ever touching the ground.
        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "the character after its air jump");
        yield return StepSeconds(0.2f);

        r.player.Jump(true, false);

        Assert.AreEqual(1, recorder.jumped,
            $"With maxAirJumps 1 the second air jump should be refused, but OnJumped has fired " +
            $"{recorder.jumped} time(s).");
    }

    [UnityTest]
    public IEnumerator AirJump_WithMaxAirJumpsZero_IsRefused()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerInAir(Slot(30), 10f, r, p =>
        {
            p.maxAirJumps = 0;
            p.fallJumpTimeLimit = 0f;
            recorder = AttachRecorder(p);
        });

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released above the floor");
        yield return StepSeconds(0.2f);

        r.player.Jump(true, false);

        Assert.AreEqual(0, recorder.jumped,
            $"With maxAirJumps 0 there are no air jumps at all, but OnJumped fired " +
            $"{recorder.jumped} time(s).");

        yield return null;
    }

    [UnityTest]
    public IEnumerator AirJump_AllowanceIsRefilledByTouchingTheGround()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerOnGround(Slot(31), r, p =>
        {
            p.maxAirJumps = 1;
            p.fallJumpTimeLimit = 0f;
            recorder = AttachRecorder(p);
        });

        // Ground jump, then spend the air jump.
        r.player.Jump(true);
        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, 3f,
                                  "the character after its ground jump");
        r.player.Jump(true, false);

        int afterFirstFlight = recorder.jumped;
        Assert.AreEqual(2, afterFirstFlight,
            $"Precondition: a ground jump plus one air jump should be two jumps, but OnJumped fired " +
            $"{afterFirstFlight} time(s).");

        yield return WaitUntilGrounded(r.player, 4f, "the character after spending its air jump");
        yield return Step(3);

        // Same again. If the allowance were not refilled, the second air jump would be refused.
        r.player.Jump(true);
        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, 3f,
                                  "the character after its second ground jump");
        r.player.Jump(true, false);

        Assert.AreEqual(4, recorder.jumped,
            $"Touching the ground refills the air jump allowance, so the second flight should also " +
            $"get a ground jump and an air jump - four in total - but OnJumped has fired " +
            $"{recorder.jumped} time(s).");
    }

    [UnityTest]
    public IEnumerator AirJump_WhileStillAscending_IsAllowed()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerOnGround(Slot(32), r, p =>
        {
            p.maxAirJumps = 1;
            recorder = AttachRecorder(p);
        });

        r.player.Jump(true);
        yield return Step(3);

        Assert.AreEqual(PlatformerPlayer2D.State.Jumping, r.player.state,
            $"Precondition: the character should still be ascending, but the state is {r.player.state}.");
        Assert.Greater(r.player.velocity.y, 0f,
            $"Precondition: the character should still be rising, but velocity.y is " +
            $"{r.player.velocity.y:F3}.");

        r.player.Jump(true, false);

        Assert.AreEqual(2, recorder.jumped,
            $"A double jump should be available on the way up, not only after the apex, but " +
            $"OnJumped has fired {recorder.jumped} time(s) while the character is still ascending.");
    }

    // ------------------------------------------------------------------
    // jumpGroundCheckDistance
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator JumpGroundCheckDistance_FallingWithinRange_GrantsAFullGroundJump()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerInAir(Slot(33), 4f, r, p =>
        {
            p.jumpGroundCheckDistance = 1.5f;
            p.maxAirJumps = 0;
            p.fallJumpTimeLimit = 0f;
            recorder = AttachRecorder(p);
        });

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released above the floor");

        // Wait until the character is inside the probe's reach but still airborne.
        int steps = Mathf.CeilToInt(StateTimeout / Time.fixedDeltaTime);
        bool inRange = false;
        for (int i = 0; i < steps && !inRange; i++)
        {
            float clearance = (r.player.position.y - CharacterHalfHeight) - r.groundTopY;
            if (!r.player.isGrounded && clearance < 1f && clearance > 0.2f)
            {
                inRange = true;
                break;
            }
            yield return new WaitForFixedUpdate();
        }

        Assert.IsTrue(inRange,
            "Staging error: the character never passed through the band just above the floor.");

        r.player.Jump(true, false);

        Assert.AreEqual(1, recorder.jumped,
            $"A character falling within jumpGroundCheckDistance " +
            $"({r.player.jumpGroundCheckDistance:F3}) of the ground should be allowed to jump, but " +
            $"OnJumped fired {recorder.jumped} time(s).");
    }

    [UnityTest]
    public IEnumerator JumpGroundCheckDistance_Zero_GrantsNothing()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerInAir(Slot(34), 4f, r, p =>
        {
            p.jumpGroundCheckDistance = 0f;
            p.maxAirJumps = 0;
            p.fallJumpTimeLimit = 0f;
            recorder = AttachRecorder(p);
        });

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released above the floor");

        int steps = Mathf.CeilToInt(StateTimeout / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            float clearance = (r.player.position.y - CharacterHalfHeight) - r.groundTopY;
            if (!r.player.isGrounded && clearance < 1f && clearance > 0.2f)
            {
                break;
            }
            yield return new WaitForFixedUpdate();
        }

        r.player.Jump(true, false);

        Assert.AreEqual(0, recorder.jumped,
            $"With jumpGroundCheckDistance 0 the near-ground probe is off, so a press just above " +
            $"the floor should be refused, but OnJumped fired {recorder.jumped} time(s).");
    }

    // EXPECTED RED. CanJump probes for nearby ground with
    //     CheckForGround(velocity.normalized, jumpGroundCheckDistance, out groundHit)
    // - along the direction of travel, not toward the ground. A character falling diagonally (after
    // a wall jump, a dash, or simply running off a ledge at speed) aims the probe sideways, so the
    // floor directly beneath it is missed and the mechanic quietly stops working exactly when a
    // player is moving fast enough to need it.
    [UnityTest]
    public IEnumerator JumpGroundCheckDistance_FallingDiagonally_ProbesTowardTheGround()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerInAir(Slot(35), 4f, r, p =>
        {
            p.jumpGroundCheckDistance = 1.5f;
            p.maxAirJumps = 0;
            p.fallJumpTimeLimit = 0f;
            p.airSpeed = 12f;          // so the sideways component is not braked away
            recorder = AttachRecorder(p);
        });

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released above the floor");

        int steps = Mathf.CeilToInt(StateTimeout / Time.fixedDeltaTime);
        bool inRange = false;
        for (int i = 0; i < steps; i++)
        {
            // Hold a strongly diagonal velocity: mostly sideways, still descending.
            r.player.velocity = new Vector2(10f, -3f);

            float clearance = (r.player.position.y - CharacterHalfHeight) - r.groundTopY;
            if (!r.player.isGrounded && clearance < 1f && clearance > 0.2f)
            {
                inRange = true;
                break;
            }
            yield return new WaitForFixedUpdate();
        }

        Assert.IsTrue(inRange,
            "Staging error: the character never passed through the band just above the floor.");

        r.player.Jump(true, false);

        Assert.AreEqual(1, recorder.jumped,
            $"The near-ground jump should find the floor beneath the character regardless of which " +
            $"way it happens to be travelling, but a diagonal fall aimed the probe sideways and " +
            $"OnJumped fired {recorder.jumped} time(s).");
    }

    // ------------------------------------------------------------------
    // Hang gravity
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator HangGravity_RatioBelowOne_LengthensTimeSpentNearTheApex()
    {
        // Same hangSpeed in both runs, so ApexResult.framesNearApex counts the same band.
        PlayerResult normal = new PlayerResult();
        yield return MakePlayerOnGround(Slot(36), normal, p =>
        {
            p.hangSpeed = 2f;
            p.hangGravityRatio = 1f;    // no reduction
        });

        normal.player.Jump(true);
        ApexResult normalApex = new ApexResult();
        yield return MeasureApex(normal.player, 4f, normalApex);

        PlayerResult floaty = new PlayerResult();
        yield return MakePlayerOnGround(Slot(37), floaty, p =>
        {
            p.hangSpeed = 2f;
            p.hangGravityRatio = 0.2f;  // much weaker gravity near the apex
        });

        floaty.player.Jump(true);
        ApexResult floatyApex = new ApexResult();
        yield return MeasureApex(floaty.player, 4f, floatyApex);

        Assert.Greater(floatyApex.framesNearApex, normalApex.framesNearApex,
            $"Lowering hangGravityRatio should make the character linger near the top of its jump, " +
            $"but it spent {floatyApex.framesNearApex} frames below hangSpeed against " +
            $"{normalApex.framesNearApex} at full gravity.");
    }

    [UnityTest]
    public IEnumerator HangGravity_OnlyAppliesWhileSpeedIsBelowHangSpeed()
    {
        // hangSpeed 0 switches the mechanic off entirely: there is no speed below zero, so the
        // reduced gravity should never be reached however small hangGravityRatio is.
        PlayerResult disabled = new PlayerResult();
        yield return MakePlayerOnGround(Slot(38), disabled, p =>
        {
            p.hangSpeed = 0f;
            p.hangGravityRatio = 0.1f;
        });

        disabled.player.Jump(true);
        ApexResult disabledApex = new ApexResult();
        yield return MeasureApex(disabled.player, 4f, disabledApex);

        PlayerResult plain = new PlayerResult();
        yield return MakePlayerOnGround(Slot(39), plain, p =>
        {
            p.hangSpeed = 0f;
            p.hangGravityRatio = 1f;
        });

        plain.player.Jump(true);
        ApexResult plainApex = new ApexResult();
        yield return MeasureApex(plain.player, 4f, plainApex);

        Assert.AreEqual(plainApex.apexHeight, disabledApex.apexHeight, PositionTolerance,
            $"With hangSpeed 0 the hang effect is off, so hangGravityRatio should make no " +
            $"difference at all: 0.1 reached {disabledApex.apexHeight:F3} and 1.0 reached " +
            $"{plainApex.apexHeight:F3}.");
    }

    // ------------------------------------------------------------------
    // Events
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Landed_Event_FiresOnceOnTouchdownAndSendsDidLand()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerInAir(Slot(40), 4f, r, p => { recorder = AttachRecorder(p); });

        yield return WaitUntilGrounded(r.player, 3f, "a character released above the floor");
        yield return StepSeconds(0.5f);

        Assert.AreEqual(1, recorder.landed,
            $"Landing should raise OnLanded exactly once, but it fired {recorder.landed} time(s).");
        Assert.AreEqual(1, recorder.didLand,
            $"Landing should send DidLand exactly once, but it arrived {recorder.didLand} time(s).");
    }

    // EXPECTED RED. Landed() is invoked from inside KinematicMotion2D.FixedUpdate, during
    // UpdateGroundedState - which runs AFTER PlatformerPlayer2D has already chosen and applied this
    // frame's motion from the PREVIOUS frame's isGrounded. So a handler subscribed to OnLanded sees
    // `state` still holding an airborne value on the very frame the character touches down. Anything
    // a game does in that handler - switching an animation, spawning dust, reading the state to
    // decide which - is reading a state that has already been superseded by the landing it is
    // reacting to.
    [UnityTest]
    public IEnumerator Landed_Event_FiresWithAGroundState()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerInAir(Slot(41), 4f, r, p => { recorder = AttachRecorder(p); });

        yield return WaitUntilGrounded(r.player, 3f, "a character released above the floor");
        yield return Step(2);

        Assert.AreEqual(1, recorder.stateAtLanded.Count,
            $"Precondition: OnLanded should have fired exactly once, but it fired " +
            $"{recorder.stateAtLanded.Count} time(s).");

        PlatformerPlayer2D.State observed = recorder.stateAtLanded[0];
        bool isGroundState = observed == PlatformerPlayer2D.State.Walking
                          || observed == PlatformerPlayer2D.State.Running;

        Assert.IsTrue(isGroundState,
            $"A handler reacting to OnLanded should see a ground state, but `state` still said " +
            $"{observed} at the moment the event fired.");
    }

    [UnityTest]
    public IEnumerator Jumped_Event_FiresOnceAndSendsDidJump()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerOnGround(Slot(42), r, p => { recorder = AttachRecorder(p); });

        r.player.Jump(true);
        yield return StepSeconds(0.3f);

        Assert.AreEqual(1, recorder.jumped,
            $"One press should raise OnJumped once, but it fired {recorder.jumped} time(s).");
        Assert.AreEqual(1, recorder.didJump,
            $"One press should send DidJump once, but it arrived {recorder.didJump} time(s).");
    }

    [UnityTest]
    public IEnumerator Jumped_Event_DoesNotFireWhenTheJumpIsRefused()
    {
        PlayerResult r = new PlayerResult();
        PlayerEventRecorder recorder = null;

        yield return MakePlayerInAir(Slot(43), 10f, r, p =>
        {
            p.maxAirJumps = 0;
            p.fallJumpTimeLimit = 0f;
            recorder = AttachRecorder(p);
        });

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released high above the floor");
        yield return StepSeconds(0.3f);

        r.player.Jump(true, false);

        Assert.AreEqual(0, recorder.jumped,
            $"A refused jump must not raise OnJumped, but it fired {recorder.jumped} time(s).");
        Assert.AreEqual(0, recorder.didJump,
            $"A refused jump must not send DidJump, but it arrived {recorder.didJump} time(s).");

        yield return null;
    }

    // ------------------------------------------------------------------
    // Ground velocity inheritance and grounded bookkeeping
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator GroundVelocityInheritance_JumpingFromAMovingPlatform_CarriesPlatformSpeed()
    {
        float laneX = Slot(44);

        KinematicMotion2D platform = CreateMovingPlatform(new Vector2(laneX, 0f),
                                                          new Vector2(12f, 1f), Vector2.zero);

        PlayerResult r = new PlayerResult();
        yield return MakePlayer(new Vector2(laneX, 1.1f), r);
        yield return Settle();

        Assert.IsTrue(r.player.isGrounded,
            "Precondition: the character should have landed on the platform.");

        platform.GetComponent<ConstantVelocityDriver>().velocity = new Vector2(4f, 0f);
        yield return StepSeconds(0.5f);

        r.player.Jump(true);
        float launchedHorizontal = r.player.velocity.x;

        Assert.AreEqual(4f, launchedHorizontal, SpeedTolerance + 0.5f,
            $"Jumping from a platform moving at 4 units/s should carry that speed into the jump, " +
            $"but the launch velocity.x is {launchedHorizontal:F3}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator Grounded_OnTheFrameAfterJumping_IsFalse()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(45), r);

        Assert.IsTrue(r.player.isGrounded, "Precondition: the character should start grounded.");

        r.player.Jump(true);

        // Jump() re-runs the grounded check before it returns, precisely so that a jump taken on a
        // slope does not carry the slope's tangent into the launch.
        Assert.IsFalse(r.player.isGrounded,
            "Jump() rechecks the grounded state before returning, so a character that has just " +
            "launched should no longer report grounded.");

        yield return null;
    }
}
