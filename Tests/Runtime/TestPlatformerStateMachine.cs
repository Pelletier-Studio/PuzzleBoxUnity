using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PuzzleBox;

/// <summary>
/// SPECIFICATION - PlatformerPlayer2D state machine invariants.
///
/// This file is about the twelve states as a SYSTEM: that each one can be entered, that each one
/// can be left, and that no combination of inputs strands the character in a state it cannot
/// escape. The per-mechanic behaviour of each state belongs to the other files.
///
///  1. Every state in the enum is reachable through ordinary play.
///  2. Every state can be left. A character is never stranded: releasing the input that produced a
///     state, or losing the condition that justified it, always leads somewhere else.
///  3. Touching the ground wins. Whatever the character was doing in the air, landing puts it in a
///     ground state - a character standing on the floor is not climbing a wall.
///  4. The motion applied on a frame is chosen from that frame's grounded state, not the previous
///     frame's.
///  5. While the component is not simulating - simulatePhysics off, or carried by a parent - the
///     state machine is suspended too. Timers do not drain and velocity does not accumulate, so the
///     character does not spring to life when simulation resumes.
///
/// THIS IS A TDD RED PHASE. Several tests below are expected to fail against the current
/// implementation; each says so above its declaration, with the register ID where one exists.
/// Do not make a failing test here pass by weakening its assertion - the failures are the point.
///
/// RED LIST (expected to fail; transcribe the observed set after the first full run):
///   StateMachine_SpawnedInMidAir_DoesNotClaimToBeWalking
///   GroundedPrecedence_MotionIsChosenFromTheCurrentFrameGroundedState
///   Grabbing_LandingWhileStillGrabbing_ReturnsToAGroundState               PP-39
///   SimulatePhysicsFalse_DoesNotConsumeWallGrabTime                        PP-04
///   AttachedToAParent_DoesNotAccumulateVelocity                            PP-04
/// </summary>
public class TestPlatformerStateMachine : PlatformerTestFixture
{
    private const float Lane = 24000f;

    private static float Slot(int n)
    {
        return Lane + n * 50f;
    }

    private static bool IsGroundState(PlatformerPlayer2D.State s)
    {
        return s == PlatformerPlayer2D.State.Walking || s == PlatformerPlayer2D.State.Running;
    }

    // ------------------------------------------------------------------
    // Entry conditions
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator StateMachine_OnStart_IsWalking()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(0), r);

        Assert.AreEqual(PlatformerPlayer2D.State.Walking, r.player.state,
            $"A character standing still on the ground should be Walking, but it is {r.player.state}.");
    }

    // EXPECTED RED. Start() assigns `state = State.Walking` unconditionally, without looking at
    // where the character actually is. Anything that reads `state` during the same frame - another
    // component's Start, an animator being initialised, a save-game restore that drops the
    // character in mid-air - is told the character is walking on ground it is nowhere near. The
    // first FixedUpdate corrects it, but by then the wrong animation has been selected.
    [UnityTest]
    public IEnumerator StateMachine_SpawnedInMidAir_DoesNotClaimToBeWalking()
    {
        // Deliberately not MakePlayer: that helper asserts the Walking start as a staging check,
        // which is the very thing under test here.
        PlatformerPlayer2D player = BuildPlayer(new Vector2(Slot(1), 40f), null);

        yield return null;   // Start() runs

        Assert.AreNotEqual(PlatformerPlayer2D.State.Walking, player.state,
            "A character created 40 units above the nearest floor is not walking, but Start() " +
            "assigns Walking without checking. Anything reading `state` in the same frame is " +
            "given a state the character has never been in.");
    }

    // Exercises every driver in the fixture, which is the point: if this passes, the other files
    // can rely on the drivers, and a driver regression shows up here rather than as a confusing
    // failure somewhere else.
    [UnityTest]
    public IEnumerator StateMachine_EveryStateIsReachable()
    {
        PlayerResult walking = new PlayerResult();
        yield return DriveToWalking(Slot(10), walking);

        PlayerResult running = new PlayerResult();
        yield return DriveToRunning(Slot(11), running);

        PlayerResult jumping = new PlayerResult();
        yield return DriveToJumping(Slot(12), jumping);

        PlayerResult falling = new PlayerResult();
        yield return DriveToFalling(Slot(13), falling);

        PlayerResult wallSliding = new PlayerResult();
        yield return DriveToWallSliding(Slot(14), wallSliding);

        PlayerResult grabbing = new PlayerResult();
        yield return DriveToGrabbing(Slot(15), grabbing);

        PlayerResult climbingUp = new PlayerResult();
        yield return DriveToClimbingWallUp(Slot(16), climbingUp);

        PlayerResult climbingDown = new PlayerResult();
        yield return DriveToClimbingWallDown(Slot(17), climbingDown);

        PlayerResult climbingOver = new PlayerResult();
        yield return DriveToClimbingWallOver(Slot(18), climbingOver);

        PlayerResult wallJumping = new PlayerResult();
        yield return DriveToWallJumping(Slot(19), wallJumping);

        PlayerResult climbing = new PlayerResult();
        yield return DriveToClimbing(Slot(20), climbing);

        PlayerResult dashing = new PlayerResult();
        yield return MakePlayerOnGround(Slot(21), dashing, p => p.canDash = true);
        dashing.player.Move(Vector2.right);
        yield return Step(2);
        EnterDashing(dashing.player);

        // Each driver already asserted the state it delivers, so reaching this line is the result.
        // Assert once more against the enum itself so that a state added to the enum later fails
        // here instead of silently going untested.
        int stateCount = System.Enum.GetValues(typeof(PlatformerPlayer2D.State)).Length;
        Assert.AreEqual(12, stateCount,
            $"The State enum has {stateCount} members, but this test drives 12. A new state needs " +
            "a driver in PlatformerTestFixture and a line here.");
    }

    // ------------------------------------------------------------------
    // Ground and air transitions
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Walking_LeavingTheGround_BecomesFallingWithinTwoFrames()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(2), r, null, ShortFloorWidth);

        r.player.Move(Vector2.right);

        int steps = Mathf.CeilToInt(StateTimeout / Time.fixedDeltaTime);
        int framesSinceLeavingGround = -1;

        for (int i = 0; i < steps; i++)
        {
            yield return new WaitForFixedUpdate();

            if (!r.player.isGrounded && framesSinceLeavingGround < 0)
            {
                framesSinceLeavingGround = 0;
            }
            else if (framesSinceLeavingGround >= 0)
            {
                framesSinceLeavingGround++;
            }

            if (framesSinceLeavingGround >= 0 && r.player.state == PlatformerPlayer2D.State.Falling)
            {
                Assert.LessOrEqual(framesSinceLeavingGround, 2,
                    $"A character that walks off a ledge should report Falling within two frames, " +
                    $"but it took {framesSinceLeavingGround}.");
                yield break;
            }
        }

        Assert.Fail($"The character never reported Falling after walking off the floor; its state " +
                    $"is {r.player.state} and isGrounded is {r.player.isGrounded}.");
    }

    [UnityTest]
    public IEnumerator Falling_Landing_BecomesWalkingWithinTwoFrames()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(3), 5f, r);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released above the floor");
        yield return WaitUntilGrounded(r.player, 3f, "the falling character");

        yield return Step(2);

        Assert.AreEqual(PlatformerPlayer2D.State.Walking, r.player.state,
            $"Two frames after touching down with no input held, the character should be Walking, " +
            $"but it is {r.player.state}.");
    }

    [UnityTest]
    public IEnumerator Falling_LandingWhileRunHeld_BecomesRunning()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(4), 5f, r);

        r.player.Run(true);
        r.player.Move(Vector2.right);

        yield return WaitUntilGrounded(r.player, 3f, "a character released above the floor");
        yield return Step(3);

        Assert.AreEqual(PlatformerPlayer2D.State.Running, r.player.state,
            $"Landing with the run button held should go straight to Running, not Walking, but the " +
            $"state is {r.player.state}.");
    }

    // EXPECTED RED. PlatformerPlayer2D.FixedUpdate runs UpdateState() and the
    // `isGrounded ? ApplyGroundMotion() : ApplyAirMotion()` branch BEFORE base.FixedUpdate(), which
    // is where UpdateGroundedState() actually runs. Every frame therefore decides what kind of
    // motion to apply using the PREVIOUS frame's grounded flag: the frame the character walks off a
    // ledge still runs ground motion, and the frame it touches down still runs air motion.
    //
    // The consequences are small but real - the air-jump allowance is refilled a frame late, ground
    // braking is applied for one frame of free fall - and the lag is pervasive enough that every
    // other test in this suite has to say "within two frames" instead of naming a frame. Fixing it
    // means moving the grounded check ahead of the motion choice.
    [UnityTest]
    public IEnumerator GroundedPrecedence_MotionIsChosenFromTheCurrentFrameGroundedState()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(5), r, null, ShortFloorWidth);

        r.player.Move(Vector2.right);

        Trace trace = new Trace();
        yield return Record(r.player, 2f, trace);

        for (int i = 0; i < trace.states.Count; i++)
        {
            bool airborneButOnAGroundState = !trace.grounded[i] && IsGroundState(trace.states[i]);

            Assert.IsFalse(airborneButOnAGroundState,
                $"On step {i} the character was airborne (isGrounded false) while `state` still " +
                $"said {trace.states[i]}. The state machine reads the previous frame's grounded " +
                "flag, so for one frame after leaving the ground it applies ground motion to a " +
                "character that is in the air.");
        }
    }

    // ------------------------------------------------------------------
    // Wall states: no strandings
    // ------------------------------------------------------------------

    // PP-01 regression guard. Grabbing used to have no exit at all while grounded: every escape in
    // UpdateStateOnWall was gated on !isGrounded, so a character that tapped grab while standing
    // beside a wall was frozen permanently, with ApplyWallMotion pinning velocity to zero.
    [UnityTest]
    public IEnumerator Grabbing_WhileGrounded_DoesNotSoftlock()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(6), r, p => p.canGrabWall = true);

        CreateWallBeside(r, 1f);
        yield return Step(2);

        Assert.IsTrue(r.player.isTouchingWall,
            "Precondition: the character should be standing beside the wall.");

        r.player.GrabWall(true);
        yield return WaitForState(r.player, PlatformerPlayer2D.State.Grabbing, StateTimeout,
                                  "a grounded character that pressed grab beside a wall");

        // Let go. This is the whole test: releasing the button must give the character back.
        r.player.GrabWall(false);

        yield return WaitWhileState(r.player, PlatformerPlayer2D.State.Grabbing, StateTimeout,
                                    "a grounded character that released grab");

        Assert.IsTrue(IsGroundState(r.player.state),
            $"After releasing grab while standing on the ground the character should be in a ground " +
            $"state, but it is {r.player.state}.");

        // And it can actually move again - ApplyWallMotion pins velocity to zero, so a state change
        // alone is not proof the character is free.
        float startX = r.player.position.x;
        r.player.Move(Vector2.left);
        yield return StepSeconds(0.5f);

        Assert.Less(r.player.position.x, startX - 0.5f,
            $"After releasing grab the character should be able to walk away, but it moved only " +
            $"{r.player.position.x - startX:F3} units in 0.5 s.");
    }

    // PP-38 regression guard. Running out of grab time used to leave the character hanging: the
    // timer-expiry branch delegated to UpdateStateInAir, which saw the grab still held and called
    // TryGrabbingWall again, which declined to act because the state was already Grabbing - and
    // nothing else was evaluated, so maxWallGrabTime had no effect at all while the button was down.
    [UnityTest]
    public IEnumerator Grabbing_GrabTimeExpiringInAir_FallsInsteadOfSticking()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToGrabbing(Slot(7), r, p => p.maxWallGrabTime = 0.4f);

        Assert.Greater(r.player.wallGrabTimer.timeLeft, 0f,
            $"Precondition: the grab timer should be running, but timeLeft is " +
            $"{r.player.wallGrabTimer.timeLeft:F3}. A fresh Utils.Timer reports finished from " +
            "birth, and only the grounded branch of UpdateState ever loads it.");

        // The grab button stays held for the whole test - that is the case the regression was in.
        yield return WaitWhileState(r.player, PlatformerPlayer2D.State.Grabbing,
                                    r.player.maxWallGrabTime + 1f,
                                    "a character holding grab past maxWallGrabTime");

        Assert.AreNotEqual(PlatformerPlayer2D.State.Grabbing, r.player.state,
            "maxWallGrabTime should release the wall even while the button is held.");
    }

    // PP-07 regression guard. TryGrabbingWall refuses while the state is WallJumping, and
    // UpdateStateInAir used to commit to the wall branch without evaluating anything else - so a
    // character that wall-jumped straight back into the same wall with grab still held stayed in
    // WallJumping for the entire fall: no wall slide, and no air jump either, because CanJump did
    // not list the state.
    [UnityTest]
    public IEnumerator WallJumping_IntoTheSameWallWithGrabHeld_DoesNotStick()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToWallJumping(Slot(8), r, p =>
        {
            // Straight up, so the character stays against the wall it just pushed off.
            p.grabJumpHorizontalVelocity = 0f;
            p.maxWallGrabTime = 5f;
        });

        Assert.IsTrue(r.player.isGrabbing,
            "Precondition: the grab button should still be held after the wall jump.");

        yield return WaitWhileState(r.player, PlatformerPlayer2D.State.WallJumping, 3f,
                                    "a character that wall-jumped back into the same wall");

        Assert.AreNotEqual(PlatformerPlayer2D.State.WallJumping, r.player.state,
            "A wall jump that leaves the character hugging the same wall must not strand it in " +
            "WallJumping for the rest of the fall.");
    }

    // EXPECTED RED (PP-39). Climb down a wall onto the floor with the grab button still held:
    // isGrounded is true, but isGrabbing, canGrabWall and isTouchingWall all still hold, and
    // UpdateState refills wallGrabTimer on every grounded frame - so UpdateStateOnWall's release
    // branch is never entered and the character stays in a wall state while standing on the ground.
    // With down held, ApplyWallMotion drives velocity.y into the floor with gravity switched off.
    // Not a softlock since PP-01 (releasing grab still works), but landing should win over grab
    // input, not lose to it.
    [UnityTest]
    public IEnumerator Grabbing_LandingWhileStillGrabbing_ReturnsToAGroundState()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToClimbingWallDown(Slot(9), r);

        yield return WaitUntilGrounded(r.player, StateTimeout,
                                       "a character climbing down a wall to the floor");
        yield return Step(3);

        Assert.IsTrue(IsGroundState(r.player.state),
            $"A character that has climbed down onto the floor is standing on the ground, not " +
            $"hanging on a wall, but its state is {r.player.state} with the grab button still held.");
    }

    [UnityTest]
    public IEnumerator ClimbingWallOver_LosingUpwardSpeed_LeavesTheVaultState()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToClimbingWallOver(Slot(22), r);

        yield return WaitWhileState(r.player, PlatformerPlayer2D.State.ClimbingWallOver, 3f,
                                    "a character vaulting over the top of a wall");

        Assert.AreNotEqual(PlatformerPlayer2D.State.ClimbingWallOver, r.player.state,
            "The vault over a wall edge is a momentary boost, not a state to live in.");
    }

    [UnityTest]
    public IEnumerator Climbing_CanClimbRevoked_Exits()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToClimbing(Slot(23), r);

        // The documented way to build a ladder is a trigger that toggles canClimb, so this is the
        // ordinary case of stepping out of a climbable area, not an edge case.
        r.player.canClimb = false;

        yield return WaitWhileState(r.player, PlatformerPlayer2D.State.Climbing, StateTimeout,
                                    "a character whose climbable area was taken away");

        Assert.AreNotEqual(PlatformerPlayer2D.State.Climbing, r.player.state,
            "Clearing canClimb should end the climb; gravity is switched off while Climbing, so a " +
            "character left in that state would hang in the air.");
    }

    [UnityTest]
    public IEnumerator Dashing_AlwaysExits()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(24), r, p => p.canDash = true);

        r.player.Move(Vector2.right);
        yield return Step(2);

        EnterDashing(r.player);

        yield return WaitWhileState(r.player, PlatformerPlayer2D.State.Dashing, DashTimeout,
                                    "a character that started a dash");

        Assert.AreNotEqual(PlatformerPlayer2D.State.Dashing, r.player.state,
            "A dash must always end, whatever happens during it.");
    }

    // ------------------------------------------------------------------
    // Suspended simulation
    //
    // KinematicMotion2D.FixedUpdate returns early when simulatePhysics is false, and again when the
    // body is attached to a parent. PlatformerPlayer2D's override does its own work BEFORE and
    // AFTER that call without checking either condition, so the state machine keeps running on a
    // character that is not being simulated.
    // ------------------------------------------------------------------

    // EXPECTED RED (PP-04). maxWallGrabTime is a budget for hanging on a wall. A paused game is not
    // hanging on a wall.
    [UnityTest]
    public IEnumerator SimulatePhysicsFalse_DoesNotConsumeWallGrabTime()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToGrabbing(Slot(25), r, p => p.maxWallGrabTime = 5f);

        float before = r.player.wallGrabTimer.timeLeft;
        Assert.Greater(before, 1f,
            $"Precondition: the grab timer should have plenty left, but timeLeft is {before:F3}.");

        r.player.simulatePhysics = false;
        yield return StepSeconds(0.5f);

        Assert.AreEqual(before, r.player.wallGrabTimer.timeLeft, 0.02f,
            $"With simulatePhysics off the character is not being simulated, so its grab time " +
            $"should not drain - but timeLeft went from {before:F3} to " +
            $"{r.player.wallGrabTimer.timeLeft:F3} over half a second of suspended simulation.");
    }

    // EXPECTED RED (PP-04). While the character is attached to a parent, the base class hands all
    // movement to that parent and returns before applying gravity or consuming velocity. The
    // override still runs ApplyAirMotion, which accelerates velocity.x from held input - velocity
    // that nothing spends, because the body is not moving itself. It is all still there on detach,
    // so the character is flung sideways the instant it is put down.
    [UnityTest]
    public IEnumerator AttachedToAParent_DoesNotAccumulateVelocity()
    {
        float laneX = Slot(26);

        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(laneX, 12f, r);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released high above the floor");

        KinematicMotion2D carrier = CreateMovingPlatform(
            new Vector2(laneX + 5f, 20f), new Vector2(2f, 1f), Vector2.zero);
        yield return null;

        r.player.velocity = Vector2.zero;
        r.player.AttachTo(carrier);

        Assert.IsNotNull(r.player.attachedTo,
            "Staging error: the character should be attached to the carrier.");

        // Hold a direction the whole time. A carried character is cargo; the input should not be
        // charging up a velocity that nothing is spending.
        r.player.Move(Vector2.right);
        yield return StepSeconds(0.5f);

        Assert.AreEqual(0f, r.player.velocity.x, 0.5f,
            $"A character carried by a parent is not moving under its own power, but held input " +
            $"built its velocity.x up to {r.player.velocity.x:F3} units/s while attached. That " +
            "velocity is still there when it is put down.");
    }
}
