using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using PuzzleBox;

/// <summary>
/// SPECIFICATION - PlatformerPlayer2D input plumbing and lifecycle.
///
///  1. The OnMove / OnRun / OnGrabWall / OnJump / OnDash callbacks accept the payloads Unity's
///     Input System actually delivers, via the PuzzleBox.InputValue adapter.
///  2. acceptInput gates PRESSES only. A release always gets through, in every direction, so that
///     taking control away from the player can never leave the character stuck running, stuck
///     gripping a wall, or stuck in a jump that rises to full height on its own.
///  3. A press that is blocked stays blocked. It is never delivered as a release instead.
///  4. Movement input is not latched across a control handover: disabling input zeroes the
///     character's motion, and re-enabling it restores whatever the stick is holding NOW.
///  5. SetUserInputEnabled toggles acceptInput, raises OnInputEnabledChanged, and follows through
///     to the PlayerInput component when there is one.
///  6. Kill() runs the death sequence once, however many times it is called, and the object is
///     destroyed after deathAnimationTimeoutSeconds whatever the game's time scale is doing.
///  7. DestroySelf() cancels any pending death timeout and announces the destruction once.
///
/// THIS IS A TDD RED PHASE. The tests marked below are expected to fail against the current
/// implementation. Do not make one pass by weakening its assertion - the failures are the point.
///
/// RED LIST (expected to fail; transcribe the observed set after the first full run):
///   DestroySelf_CalledTwice_RaisesOnDestroyedOnce
///   DeathTimeout_WithTimeScaleZero_StillDestroys                           PP-29
///
/// NOT TESTED HERE, deliberately:
///   PP-14 (inputFreezeTimer.OnEnd hardcodes acceptInput = true, clobbering a death that happened
///   during the freeze) and PP-40 (that same OnEnd restores a motionInput the Update transition
///   handler has already zeroed) are both downstream of a callback that never fires: a freshly
///   started Utils.Timer reports finished immediately, so inputFreezeTimer's OnEnd is unreachable
///   in the current build. Neither defect can be observed until the timer defect named in
///   TestPlatformerDashing is fixed. BOTH NEED TESTS ADDING HERE AT THAT POINT - they are real,
///   they are just currently masked by a larger one.
///
///   PP-02 (Kill not being idempotent) is NOT in the red list: the guard is live in the source, so
///   the two Kill_CalledTwice_* tests below are green regression guards. The register entry is
///   stale and should be closed.
/// </summary>
public class TestPlatformerInputAndLifecycle : PlatformerTestFixture
{
    private const float Lane = 26000f;

    private static float Slot(int n)
    {
        return Lane + n * 50f;
    }

    // Runs before KinematicTestFixture.TearDown, which is what actually destroys the spawned
    // objects. Time.timeScale is global and survives the test that changed it, so a test that
    // leaves it at zero hangs every test after it - this is the highest-consequence piece of
    // cleanup in the suite.
    [TearDown]
    public void RestoreTimeScale()
    {
        Time.timeScale = 1f;
    }

    private class LifecycleRecorder : MonoBehaviour
    {
        public int died;
        public int destroyed;
        public readonly List<bool> inputEnabledChanges = new List<bool>();
        public readonly List<string> messages = new List<string>();

        public void Hook(PlatformerPlayer2D p)
        {
            p.OnDied += HandleDied;
            p.OnDestroyed += HandleDestroyed;
            p.OnInputEnabledChanged += HandleInputEnabledChanged;
        }

        private void HandleDied()
        {
            died++;
        }

        private void HandleDestroyed()
        {
            destroyed++;
        }

        private void HandleInputEnabledChanged(bool enabled)
        {
            inputEnabledChanges.Add(enabled);
        }

        private void DidJump() { messages.Add("DidJump"); }
        private void DidDash() { messages.Add("DidDash"); }
        private void DidLand() { messages.Add("DidLand"); }
        private void WasKilled() { messages.Add("WasKilled"); }
        private void WasDestroyed() { messages.Add("WasDestroyed"); }
    }

    private LifecycleRecorder AttachRecorder(PlatformerPlayer2D p)
    {
        LifecycleRecorder recorder = p.gameObject.AddComponent<LifecycleRecorder>();
        recorder.Hook(p);
        return recorder;
    }

    // ------------------------------------------------------------------
    // The InputValue adapter
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator OnMove_WithAVector2InputValue_SetsMotionInput()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(0), r);

        SendMove(r.player, new Vector2(1f, 0f));

        Assert.AreEqual(1f, r.player.motionInput.x, DirectionTolerance,
            $"An OnMove carrying a Vector2 should reach motionInput, but it is {r.player.motionInput}.");

        yield return StepSeconds(0.5f);

        Assert.Greater(r.player.position.x - r.restPosition.x, 0.3f,
            "A character sent a right input through the message layer should actually walk right.");
    }

    // Documents the adapter's contract rather than demanding a change. PuzzleBox.InputValue
    // understands only itself and UnityEngine.InputSystem.InputValue; a raw Vector2 yields
    // default(Vector2), so the message arrives and quietly does nothing. Anything forwarding input
    // to this component by hand has to wrap its payload.
    [UnityTest]
    public IEnumerator OnMove_WithARawVector2_IsIgnored()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(1), r);

        r.player.SendMessage("OnMove", (object)new Vector2(1f, 0f));

        Assert.AreEqual(0f, r.player.motionInput.x, DirectionTolerance,
            $"An unwrapped Vector2 is not a payload PuzzleBox.InputValue can read, so it should " +
            $"leave motionInput alone - but motionInput is {r.player.motionInput}.");
    }

    [UnityTest]
    public IEnumerator OnRun_Pressed_SetsIsRunning()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(2), r);

        SendRun(r.player, true);

        Assert.IsTrue(r.player.isRunning,
            "A pressed Run action should put the character into running mode.");
    }

    [UnityTest]
    public IEnumerator OnGrabWall_Pressed_SetsIsGrabbing()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(3), r, p => p.canGrabWall = true);

        SendGrabWall(r.player, true);

        Assert.IsTrue(r.player.isGrabbing,
            "A pressed GrabWall action should set the grab flag.");
    }

    [UnityTest]
    public IEnumerator OnJump_Pressed_StartsAJump()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(4), r);

        SendJump(r.player, true);

        Assert.AreEqual(PlatformerPlayer2D.State.Jumping, r.player.state,
            $"A pressed Jump action should start a jump, but the state is {r.player.state}.");
        Assert.IsTrue(r.player.isJumping,
            "isJumping should be set while the button is held.");
    }

    // ------------------------------------------------------------------
    // The acceptInput gate
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator OnDash_WhileInputDisabled_IsIgnored()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(5), r, p => p.canDash = true);

        r.player.acceptInput = false;

        SendDash(r.player);

        Assert.AreNotEqual(PlatformerPlayer2D.State.Dashing, r.player.state,
            $"A dash pressed while input is disabled should be ignored, but the state is " +
            $"{r.player.state}.");

        yield return null;
    }

    // PP-05 regression guard. Jump() used to early-return on !acceptInput, which covered BOTH
    // directions of the button - so a release that landed during an input freeze was dropped, no
    // further callback ever arrived for that press, isJumping stayed true, and the jump rose to
    // full height as though the player were still holding it.
    [UnityTest]
    public IEnumerator OnJump_ReleasedWhileInputDisabled_StillEndsTheJump()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(6), r);

        SendJump(r.player, true);
        Assert.IsTrue(r.player.isJumping, "Precondition: the jump should be in progress.");

        // A cutscene, a dash freeze, a death - anything that takes control away mid-jump.
        r.player.acceptInput = false;

        SendJump(r.player, false);

        Assert.IsFalse(r.player.isJumping,
            "A button RELEASE must always get through, even while input is disabled. Otherwise no " +
            "further callback ever arrives for this press and the character is stuck in a jump " +
            "that behaves as if the button were held down forever.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator OnRun_ReleasedWhileInputDisabled_StillClearsIsRunning()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(7), r);

        SendRun(r.player, true);
        Assert.IsTrue(r.player.isRunning, "Precondition: the character should be running.");

        r.player.acceptInput = false;
        SendRun(r.player, false);

        Assert.IsFalse(r.player.isRunning,
            "A run release must get through even while input is disabled, or the character is " +
            "stuck in running mode for as long as control is taken away.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator OnGrabWall_ReleasedWhileInputDisabled_StillClearsIsGrabbing()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(8), r, p => p.canGrabWall = true);

        SendGrabWall(r.player, true);
        Assert.IsTrue(r.player.isGrabbing, "Precondition: the character should be gripping.");

        r.player.acceptInput = false;
        SendGrabWall(r.player, false);

        Assert.IsFalse(r.player.isGrabbing,
            "A grab release must get through even while input is disabled, or the character keeps " +
            "hold of a wall it was told to let go of.");

        yield return null;
    }

    // PP-32 regression guard. The first version of the release-passthrough logic read
    //     if (IsPressed(val) && acceptInput) { X(true); } else { X(false); }
    // which folded two cases into one: a PRESS made while input was disabled fell into the else and
    // was delivered as a RELEASE. Pressing jump during an input freeze therefore cut an in-flight
    // jump short, and a blocked run or grab press cleared state a script had deliberately set.
    [UnityTest]
    public IEnumerator OnJump_PressedWhileInputDisabled_IsNotDeliveredAsARelease()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(9), r);

        SendJump(r.player, true);
        Assert.IsTrue(r.player.isJumping, "Precondition: the jump should be in progress.");

        r.player.acceptInput = false;

        // The player mashes jump during the freeze. This press is blocked - it must not arrive as
        // a release and cut the jump the player is already in the middle of.
        SendJump(r.player, true);

        Assert.IsTrue(r.player.isJumping,
            "A press blocked by the input gate must be dropped, not turned into a release: " +
            "mashing jump during a freeze cut the in-flight jump short.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator OnMove_WhileInputDisabled_DoesNotMoveTheCharacter()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(10), r);

        r.player.acceptInput = false;
        yield return Step(2);

        float startX = r.player.position.x;
        SendMove(r.player, Vector2.right);

        yield return StepSeconds(0.8f);

        Assert.Less(Mathf.Abs(r.player.position.x - startX), PositionTolerance,
            $"Movement pressed while input is disabled should not move the character, but it " +
            $"travelled {r.player.position.x - startX:F3} units.");
    }

    // PP-34 regression guard. OnMove used to early-return before recording the stick's value, so a
    // stick held at the moment input was disabled left motionInput at its last non-zero reading and
    // the character kept accelerating - the same "stuck in a state" failure the button passthrough
    // exists to prevent.
    [UnityTest]
    public IEnumerator AcceptInput_TurnedOff_ZeroesMotionInput()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(11), r);

        SendMove(r.player, Vector2.right);
        yield return StepSeconds(0.4f);

        Assert.Greater(r.player.motionInput.x, 0.5f,
            "Precondition: the character should be holding a right input.");

        r.player.acceptInput = false;
        yield return null;   // the transition is detected in Update

        Assert.AreEqual(0f, r.player.motionInput.x, DirectionTolerance,
            $"Disabling input should clear the character's motion input, but it is still " +
            $"{r.player.motionInput}. A latched stick keeps the character walking through a " +
            "cutscene.");

        float speedAtHandover = Mathf.Abs(r.player.velocity.x);
        float stoppedX = r.player.position.x;
        yield return StepSeconds(0.6f);

        // Clearing the input does not stop the character dead - it stops ACCELERATING it, and
        // ApplyGroundMotion then brakes at breakingForce. The coast is therefore v^2 / 2a, which at
        // walkSpeed 3 and breakingForce 10 is 0.45 units. Asserting anything shorter than that is
        // asserting that the brake is instant, which is a different (and wrong) specification.
        float expectedCoast = (speedAtHandover * speedAtHandover) / (2f * r.player.breakingForce);

        Assert.Less(Mathf.Abs(r.player.position.x - stoppedX), expectedCoast + PositionTolerance,
            $"With input disabled the character should coast to a stop within " +
            $"{expectedCoast:F3} units (braking at breakingForce {r.player.breakingForce:F1} from " +
            $"{speedAtHandover:F3} units/s), but it travelled a further " +
            $"{r.player.position.x - stoppedX:F3}.");

        Assert.AreEqual(0f, r.player.velocity.x, SpeedTolerance,
            $"Once the coast is over the character should be stationary, but it is still moving at " +
            $"{r.player.velocity.x:F3} units/s - which would mean the stale input is still driving it.");
    }

    [UnityTest]
    public IEnumerator AcceptInput_TurnedBackOn_RestoresTheCurrentStickPositionNotAStaleOne()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(12), r);

        // Holding right when control is taken away.
        SendMove(r.player, Vector2.right);
        yield return StepSeconds(0.3f);

        r.player.acceptInput = false;
        yield return null;

        // During the blackout the player moves the stick the other way. The device keeps sending
        // events; the character is just not allowed to act on them yet.
        SendMove(r.player, Vector2.left);
        yield return Step(2);

        r.player.acceptInput = true;
        yield return null;

        Assert.Less(r.player.motionInput.x, -0.5f,
            $"On regaining control the character should act on where the stick is NOW (left), not " +
            $"on where it was when control was taken away (right), and not on nothing at all - but " +
            $"motionInput is {r.player.motionInput}.");
    }

    // ------------------------------------------------------------------
    // SetUserInputEnabled
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator SetUserInputEnabled_False_ClearsAcceptInputAndRaisesTheEvent()
    {
        PlayerResult r = new PlayerResult();
        LifecycleRecorder recorder = null;

        yield return MakePlayerOnGround(Slot(13), r, p => { recorder = AttachRecorder(p); });

        SendSetUserInputEnabled(r.player, false);

        Assert.IsFalse(r.player.acceptInput,
            "SetUserInputEnabled(false) should clear acceptInput - it is the single authoritative " +
            "gate the input callbacks read.");
        Assert.AreEqual(1, recorder.inputEnabledChanges.Count,
            $"SetUserInputEnabled should raise OnInputEnabledChanged once, but it fired " +
            $"{recorder.inputEnabledChanges.Count} time(s).");
        Assert.IsFalse(recorder.inputEnabledChanges[0],
            "OnInputEnabledChanged should report the new value, which is false.");
    }

    [UnityTest]
    public IEnumerator SetUserInputEnabled_True_ReEnablesInput()
    {
        PlayerResult r = new PlayerResult();
        LifecycleRecorder recorder = null;

        yield return MakePlayerOnGround(Slot(14), r, p => { recorder = AttachRecorder(p); });

        SendSetUserInputEnabled(r.player, false);
        SendSetUserInputEnabled(r.player, true);

        Assert.IsTrue(r.player.acceptInput,
            "SetUserInputEnabled(true) should give control back.");
        Assert.AreEqual(2, recorder.inputEnabledChanges.Count,
            $"Both calls should have raised OnInputEnabledChanged, but it fired " +
            $"{recorder.inputEnabledChanges.Count} time(s).");
        Assert.IsTrue(recorder.inputEnabledChanges[1],
            "The second OnInputEnabledChanged should report true.");
    }

    [UnityTest]
    public IEnumerator SetUserInputEnabled_False_DisablesThePlayerInputComponent()
    {
        // A PlayerInput with no actions asset logs on enable; that is Unity complaining about the
        // test's staging, not the component under test.
        LogAssert.ignoreFailingMessages = true;

        PlayerInput playerInput = null;

        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(15), r, p =>
        {
            playerInput = p.gameObject.AddComponent<PlayerInput>();
        });

        Assert.IsNotNull(playerInput, "Staging error: the PlayerInput component was not added.");

        SendSetUserInputEnabled(r.player, false);

        Assert.IsFalse(playerInput.enabled,
            "Disabling user input should follow through to the PlayerInput component, so the " +
            "device stops dispatching to the character at all.");

        SendSetUserInputEnabled(r.player, true);

        Assert.IsTrue(playerInput.enabled,
            "Re-enabling user input should switch the PlayerInput component back on.");

        LogAssert.ignoreFailingMessages = false;
    }

    // ------------------------------------------------------------------
    // Kill
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Kill_DisablesInputAndRaisesOnDied()
    {
        PlayerResult r = new PlayerResult();
        LifecycleRecorder recorder = null;

        yield return MakePlayerOnGround(Slot(16), r, p =>
        {
            p.deathAnimationTimeoutSeconds = 5f;   // long, so the object survives the assertions
            recorder = AttachRecorder(p);
        });

        r.player.Kill();

        Assert.AreEqual(1, recorder.died,
            $"Kill() should raise OnDied once, but it fired {recorder.died} time(s).");
        Assert.IsFalse(r.player.acceptInput,
            "A dead character should not be taking input.");
        Assert.Contains("WasKilled", recorder.messages,
            $"Kill() should send WasKilled; the messages seen were [{string.Join(", ", recorder.messages)}].");

        yield return null;
    }

    // PP-02 regression guard - and the register entry claiming this is broken is stale: the
    // `isKilled = true` line is live in the source, so this passes. Two hazards hitting the player
    // in the same frame must not produce two death sequences.
    [UnityTest]
    public IEnumerator Kill_CalledTwice_RaisesOnDiedOnce()
    {
        PlayerResult r = new PlayerResult();
        LifecycleRecorder recorder = null;

        yield return MakePlayerOnGround(Slot(17), r, p =>
        {
            p.deathAnimationTimeoutSeconds = 5f;
            recorder = AttachRecorder(p);
        });

        r.player.Kill();
        r.player.Kill();
        r.player.Kill();

        Assert.AreEqual(1, recorder.died,
            $"Kill() must be idempotent - three hazards in one frame is one death - but OnDied " +
            $"fired {recorder.died} time(s).");

        int killMessages = 0;
        foreach (string m in recorder.messages)
        {
            if (m == "WasKilled") killMessages++;
        }

        Assert.AreEqual(1, killMessages,
            $"WasKilled should be sent once however many times Kill() is called, but it arrived " +
            $"{killMessages} time(s).");

        yield return null;
    }

    [UnityTest]
    public IEnumerator Kill_AfterDeathAnimationTimeoutSeconds_DestroysTheGameObject()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(18), r, p => p.deathAnimationTimeoutSeconds = 0.2f);

        GameObject go = r.player.gameObject;
        r.player.Kill();

        yield return new WaitForSeconds(0.6f);

        // NUnit's IsNull compares the managed reference, which is still non-null for a destroyed
        // UnityEngine.Object. Unity's overloaded == is the one that knows.
        Assert.IsTrue(go == null,
            "deathAnimationTimeoutSeconds exists so that the object is destroyed without relying " +
            "on an animation event, but the character is still alive well past the timeout.");
    }

    [UnityTest]
    public IEnumerator Kill_BeforeTheTimeout_LeavesTheGameObjectAlive()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(19), r, p => p.deathAnimationTimeoutSeconds = 1.5f);

        GameObject go = r.player.gameObject;
        r.player.Kill();

        yield return new WaitForSeconds(0.3f);

        Assert.IsFalse(go == null,
            "The timeout is a grace period for the death animation to play, so the object must " +
            "survive until it expires.");
    }

    // EXPECTED RED (PP-29). WaitForDeath uses WaitForSeconds, which is scaled by Time.timeScale.
    // The field's own comment says it exists to stop a dead character stalling subsequent
    // processing and causing "a nasty freeze bug" - but at timeScale 0, which is exactly how a
    // game pauses or runs a hit-stop, the timeout never elapses and the object is never destroyed.
    // WaitForSecondsRealtime is the fix.
    [UnityTest]
    public IEnumerator DeathTimeout_WithTimeScaleZero_StillDestroys()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(20), r, p => p.deathAnimationTimeoutSeconds = 0.2f);

        GameObject go = r.player.gameObject;

        r.player.Kill();
        Time.timeScale = 0f;

        // Neither WaitForSeconds nor WaitForFixedUpdate advances at timeScale 0, so waiting on
        // either here would hang the run rather than fail it. Waiting on a frame COUNT is no good
        // either: in batch mode the render loop is not paced to a display, so 180 frames can pass
        // in a few milliseconds of real time - far less than deathAnimationTimeoutSeconds, which
        // would fail this test without the defect it is looking for being present. So: real
        // elapsed time, with a frame cap as the backstop.
        float waitStart = Time.realtimeSinceStartup;
        int frames = 0;

        while (go != null && Time.realtimeSinceStartup - waitStart < 2f && frames < 20000)
        {
            frames++;
            yield return null;
        }

        Assert.IsTrue(go == null,
            "The death timeout is meant to guarantee the object goes away without depending on " +
            $"animation events, but it is measured in scaled time - so a paused or hit-stopped game " +
            $"is exactly the case where the guarantee fails. {frames} frames and " +
            $"{Time.realtimeSinceStartup - waitStart:F2} s of real time at timeScale 0, against a " +
            $"timeout of {r.player.deathAnimationTimeoutSeconds:F2} s, and the character is " +
            "still here.");
    }

    // ------------------------------------------------------------------
    // DestroySelf
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator DestroySelf_SendsWasDestroyedAndRaisesOnDestroyed()
    {
        PlayerResult r = new PlayerResult();
        LifecycleRecorder recorder = null;

        yield return MakePlayerOnGround(Slot(21), r, p => { recorder = AttachRecorder(p); });

        GameObject go = r.player.gameObject;
        r.player.DestroySelf();

        Assert.AreEqual(1, recorder.destroyed,
            $"DestroySelf() should raise OnDestroyed once, but it fired {recorder.destroyed} time(s).");
        Assert.Contains("WasDestroyed", recorder.messages,
            $"DestroySelf() should send WasDestroyed; the messages seen were " +
            $"[{string.Join(", ", recorder.messages)}].");

        yield return null;

        Assert.IsTrue(go == null, "DestroySelf() should actually destroy the GameObject.");
    }

    [UnityTest]
    public IEnumerator DestroySelf_CancelsThePendingDeathTimeout()
    {
        PlayerResult r = new PlayerResult();
        LifecycleRecorder recorder = null;

        yield return MakePlayerOnGround(Slot(22), r, p =>
        {
            p.deathAnimationTimeoutSeconds = 0.3f;
            recorder = AttachRecorder(p);
        });

        r.player.Kill();
        r.player.DestroySelf();

        yield return new WaitForSeconds(0.8f);

        Assert.AreEqual(1, recorder.destroyed,
            $"Destroying early should cancel the pending death timeout, so the destruction is " +
            $"announced once - but OnDestroyed fired {recorder.destroyed} time(s), which means the " +
            "timeout coroutine ran as well.");
    }

    // EXPECTED RED. DestroySelf has no guard of its own: Destroy(gameObject) is deferred to the end
    // of the frame, so a second call in the same frame runs the whole body again and announces a
    // destruction that has already been announced. Kill() got its idempotence guard; this path did
    // not. Two systems reacting to the same death - a respawn manager and a score counter, say -
    // is enough to hit it.
    [UnityTest]
    public IEnumerator DestroySelf_CalledTwice_RaisesOnDestroyedOnce()
    {
        PlayerResult r = new PlayerResult();
        LifecycleRecorder recorder = null;

        yield return MakePlayerOnGround(Slot(23), r, p => { recorder = AttachRecorder(p); });

        r.player.DestroySelf();
        r.player.DestroySelf();

        Assert.AreEqual(1, recorder.destroyed,
            $"An object can only be destroyed once, so OnDestroyed should fire once however many " +
            $"times DestroySelf() is called - but it fired {recorder.destroyed} time(s).");

        yield return null;
    }

    // ------------------------------------------------------------------
    // Message names
    //
    // These five strings are the component's published contract with sibling components and with
    // PlatformerPlayerExtension. Renaming one silently breaks every extension in every project
    // using the package, with no compiler error anywhere - so they are pinned here by name.
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator Messages_AreSentWithTheDocumentedNames()
    {
        PlayerResult r = new PlayerResult();
        LifecycleRecorder recorder = null;

        yield return MakePlayerInAir(Slot(24), 4f, r, p =>
        {
            p.canDash = true;
            p.deathAnimationTimeoutSeconds = 5f;
            recorder = AttachRecorder(p);
        });

        yield return WaitUntilGrounded(r.player, 3f, "a character released above the floor");
        yield return Step(2);

        r.player.Jump(true);
        yield return Step(2);

        r.player.Move(Vector2.right);
        r.player.Dash();

        ForceRestoreInput(r.player, "inputFreezeTimer never fires OnEnd, so acceptInput stays false");

        r.player.Kill();

        Assert.Contains("DidLand", recorder.messages,
            $"Landing should send DidLand; seen: [{string.Join(", ", recorder.messages)}].");
        Assert.Contains("DidJump", recorder.messages,
            $"Jumping should send DidJump; seen: [{string.Join(", ", recorder.messages)}].");
        Assert.Contains("DidDash", recorder.messages,
            $"Dashing should send DidDash; seen: [{string.Join(", ", recorder.messages)}].");
        Assert.Contains("WasKilled", recorder.messages,
            $"Kill() should send WasKilled; seen: [{string.Join(", ", recorder.messages)}].");

        r.player.DestroySelf();

        Assert.Contains("WasDestroyed", recorder.messages,
            $"DestroySelf() should send WasDestroyed; seen: [{string.Join(", ", recorder.messages)}].");

        yield return null;
    }
}
