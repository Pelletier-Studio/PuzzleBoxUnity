using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PuzzleBox;

/// <summary>
/// SPECIFICATION - PlatformerPlayer2D wall interaction.
///
///  1. isTouchingWall is true when solid geometry stands within wallCheckDistance of the
///     character's side, and wallDirection says which side. Only solid geometry counts: a trigger
///     volume, a one-way platform being passed through, and the character's own colliders are not
///     walls. Carrying something must not extend the character's reach.
///  2. Falling beside a wall enters WallSliding, which weakens gravity by wallSlideGravityRatio and
///     caps descent at wallSlideMaxSpeed.
///  3. With canGrabWall, holding grab against a wall enters Grabbing: velocity is pinned to zero
///     and gravity is switched off. The hold is budgeted by maxWallGrabTime, which drains only
///     while airborne and is refilled by touching the ground.
///  4. From Grabbing, vertical input climbs the wall at wallClimbUpSpeed / wallClimbDownSpeed.
///     Reaching the top vaults the character over the edge, using climbOverEdgeJumpHeight and
///     climbOverHorizontalVelocity, in the direction of the wall.
///  5. Wall jumps and grab jumps push the character AWAY from the wall at their configured
///     horizontal velocity, at their configured fraction of a normal jump height, and only when
///     their respective permission flags allow it.
///  6. Every height in this file is a height in world units under the character's own gravity -
///     gravityModifier scales the world, not the specification.
///
/// THIS IS A TDD RED PHASE. Several tests below are expected to fail against the current
/// implementation; each says so above its declaration, with the register ID where one exists.
/// Do not make a failing test here pass by weakening its assertion - the failures are the point.
///
/// RED LIST (expected to fail; transcribe the observed set after the first full run):
///   WallDetection_ATrigger_IsNotAWall                                      PP-08
///   WallDetection_ItsOwnChildCollider_IsNotAWall                           PP-08
///   WallDetection_WhileCarryingAnAttachedBody_KeepsTheSameReach            PP-08
///   WallSlide_OnEntry_ArmsTheSlideDelayTimer                               PP-11
///   WallGrab_WithoutEverTouchingGround_CanStillGrab
///   ClimbOverEdge_UnderAGravityModifier_StillReachesTheConfiguredHeight    PP-10
/// </summary>
public class TestPlatformerWalls : PlatformerTestFixture
{
    private const float Lane = 18000f;

    private static float Slot(int n)
    {
        return Lane + n * 50f;
    }

    // ------------------------------------------------------------------
    // Wall detection
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator WallDetection_WallOnTheRight_SetsIsTouchingWallAndDirectionPlusOne()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(0), r);

        CreateWallBeside(r, 1f);
        yield return Step(3);

        Assert.IsTrue(r.player.isTouchingWall,
            "A wall placed within wallCheckDistance of the character's right side should be detected.");
        Assert.AreEqual(1f, r.player.wallDirection, DirectionTolerance,
            $"A wall on the right should give wallDirection +1, but it is {r.player.wallDirection:F3}.");
    }

    [UnityTest]
    public IEnumerator WallDetection_WallOnTheLeft_SetsDirectionMinusOne()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(1), r);

        CreateWallBeside(r, -1f);
        yield return Step(3);

        Assert.IsTrue(r.player.isTouchingWall,
            "A wall placed within wallCheckDistance of the character's left side should be detected.");
        Assert.AreEqual(-1f, r.player.wallDirection, DirectionTolerance,
            $"A wall on the left should give wallDirection -1, but it is {r.player.wallDirection:F3}.");
    }

    [UnityTest]
    public IEnumerator WallDetection_WallsOnBothSides_TakesDirectionFromFacing()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(2), r);

        CreateWallBeside(r, 1f);
        CreateWallBeside(r, -1f);

        // Face left. In a corridor the character should report the wall it is looking at.
        r.player.Move(Vector2.left);
        yield return Step(3);

        Assert.IsTrue(r.player.isTouchingWall,
            "Precondition: a character between two walls is touching a wall.");
        Assert.AreEqual(-1f, r.player.wallDirection, DirectionTolerance,
            $"With walls on both sides the tie is broken by the way the character faces (left), " +
            $"but wallDirection is {r.player.wallDirection:F3}.");
    }

    [UnityTest]
    public IEnumerator WallDetection_NoWall_ClearsIsTouchingWall()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(3), r);

        GameObject wall = CreateWallBeside(r, 1f);
        yield return Step(3);
        Assert.IsTrue(r.player.isTouchingWall, "Precondition: the wall should be detected first.");

        Object.DestroyImmediate(wall);
        yield return Step(3);

        Assert.IsFalse(r.player.isTouchingWall,
            "With the wall gone the character should no longer report touching one.");
    }

    // Documents current behaviour rather than demanding a change: wallDirection is only written
    // when a wall is actually found, so it keeps its last value once the character walks away.
    // Anything reading it must check isTouchingWall first.
    [UnityTest]
    public IEnumerator WallDetection_NoWall_LeavesWallDirectionAtItsLastValue()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(4), r);

        GameObject wall = CreateWallBeside(r, -1f);
        yield return Step(3);
        Assert.AreEqual(-1f, r.player.wallDirection, DirectionTolerance,
            "Precondition: the left wall should be detected first.");

        Object.DestroyImmediate(wall);
        yield return Step(3);

        Assert.AreEqual(-1f, r.player.wallDirection, DirectionTolerance,
            "wallDirection is only assigned when a wall is found, so it keeps its last value when " +
            "there is none. Callers must gate on isTouchingWall.");
    }

    [UnityTest]
    public IEnumerator WallDetection_BeyondWallCheckDistance_IsNotTouching()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(5), r);

        // Just outside the probe: half-extent + wallCheckDistance is the reach, so place the near
        // face a little past it.
        float nearFaceX = r.restPosition.x + CharacterHalfWidth + r.player.wallCheckDistance + 0.2f;
        CreateWall(new Vector2(nearFaceX + WallThickness * 0.5f, r.groundTopY + WallHeight * 0.5f),
                   new Vector2(WallThickness, WallHeight));

        yield return Step(3);

        Assert.IsFalse(r.player.isTouchingWall,
            $"A wall {r.player.wallCheckDistance + 0.2f:F3} units from the character's side is " +
            $"beyond wallCheckDistance ({r.player.wallCheckDistance:F3}) and should not be detected.");
    }

    [UnityTest]
    public IEnumerator WallDetection_WallCheckVerticalOffset_MovesTheProbeOrigin()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(6), r, p => p.wallCheckVerticalOffset = 2f);

        // A short pillar at the character's own height. With the probe lifted 2 units it should
        // pass clean over the top.
        float nearFaceX = r.restPosition.x + CharacterHalfWidth + r.player.wallCheckDistance * 0.5f;
        CreateWall(new Vector2(nearFaceX + 0.5f, r.restPosition.y), new Vector2(1f, 1f));

        yield return Step(3);

        Assert.IsFalse(r.player.isTouchingWall,
            "With wallCheckVerticalOffset raised 2 units, a pillar level with the character's " +
            "middle is below the probe and should not register as a wall.");
    }

    // EXPECTED RED (PP-08). UpdateWallTouchingState uses a bare Physics2D.RaycastNonAlloc, which
    // obeys the project-wide Physics2D.queriesHitTriggers (on by default). Every cast in
    // KinematicMotion2D sets contactFilter.useTriggers = false, so this is the one place in the
    // character that disagrees with the rest of it - and the consequence is that walking past a
    // checkpoint volume or a damage region makes the character think it is against a wall: it can
    // wall-slide down thin air, and grab onto nothing.
    [UnityTest]
    public IEnumerator WallDetection_ATrigger_IsNotAWall()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(7), r);

        float nearFaceX = r.restPosition.x + CharacterHalfWidth + r.player.wallCheckDistance * 0.5f;
        CreateTriggerWall(new Vector2(nearFaceX + WallThickness * 0.5f, r.groundTopY + WallHeight * 0.5f),
                          new Vector2(WallThickness, WallHeight));

        yield return Step(3);

        Assert.IsFalse(r.player.isTouchingWall,
            "A trigger volume is not solid: the character can walk straight through it, so it must " +
            "not count as a wall. The wall raycast does not filter triggers, unlike every other " +
            "cast in the character.");
    }

    // EXPECTED RED (PP-08). Self-exclusion is `hit.collider.gameObject != gameObject`, but the base
    // class gathers the character's own footprint with GetComponentsInChildren<Collider2D>(). Any
    // rig that puts a collider on a child - a hitbox, a foot sensor, a weapon - reports itself as
    // a wall on both sides, permanently.
    [UnityTest]
    public IEnumerator WallDetection_ItsOwnChildCollider_IsNotAWall()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(8), r);

        AddChildCollider(r.player, new Vector2(0.4f, 0f), new Vector2(0.4f, 0.8f));

        yield return Step(3);

        Assert.IsFalse(r.player.isTouchingWall,
            "A collider on the character's own hierarchy is part of the character, not a wall it " +
            "is standing next to, but the wall probe excludes only the root GameObject.");
    }

    // EXPECTED RED (PP-08). The probe originates at `bounds.center` and reaches
    // `bounds.extents.x + wallCheckDistance` - and `bounds` is the COMBINED footprint of the
    // character and everything attached to it. Pick up a crate and the character's wall reach
    // silently grows by the width of the crate, so it grabs walls it is not near.
    [UnityTest]
    public IEnumerator WallDetection_WhileCarryingAnAttachedBody_KeepsTheSameReach()
    {
        float laneX = Slot(9);

        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(laneX, r);

        // Outside the character's own reach, so an unencumbered character does not see it.
        float gap = r.player.wallCheckDistance + 0.6f;
        float nearFaceX = r.restPosition.x + CharacterHalfWidth + gap;
        CreateWall(new Vector2(nearFaceX + WallThickness * 0.5f, r.groundTopY + WallHeight * 0.5f),
                   new Vector2(WallThickness, WallHeight));

        yield return Step(3);
        Assert.IsFalse(r.player.isTouchingWall,
            "Precondition: the wall should be out of reach before the character picks anything up.");

        // Now carry a crate on that side.
        KinematicMotion2D crate = CreateMovingPlatform(
            new Vector2(r.restPosition.x + 1f, r.restPosition.y), new Vector2(1f, 1f), Vector2.zero);
        crate.name = "CarriedCrate";
        yield return null;
        crate.AttachTo(r.player);

        yield return Step(3);

        Assert.IsFalse(r.player.isTouchingWall,
            "Picking something up does not make the character wider for the purpose of finding " +
            "walls, but the probe measures from the combined footprint of the character and " +
            "everything attached to it, so the crate extended its reach onto a wall it is not near.");
    }

    // ------------------------------------------------------------------
    // Wall sliding
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator WallSlide_FallingBesideAWall_EntersWallSliding()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToWallSliding(Slot(10), r);

        Assert.AreEqual(PlatformerPlayer2D.State.WallSliding, r.player.state,
            $"A character falling beside a wall should be wall sliding, but it is {r.player.state}.");
    }

    [UnityTest]
    public IEnumerator WallSlide_AscendingBesideAWall_DoesNotEnterWallSliding()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(11), r);

        CreateWallBeside(r, 1f);
        yield return Step(2);

        r.player.velocity = new Vector2(0f, 9f);

        Trace trace = new Trace();
        yield return Record(r.player, 0.4f, trace);

        Assert.IsFalse(trace.Saw(PlatformerPlayer2D.State.WallSliding),
            $"Sliding DOWN a wall is what WallSliding means; a character travelling upward beside " +
            $"one is not sliding. Observed: {trace.Describe()}.");
    }

    [UnityTest]
    public IEnumerator WallSlide_ReducesFallAccelerationByWallSlideGravityRatio()
    {
        // Falling freely beside nothing, for reference.
        PlayerResult freeFall = new PlayerResult();
        yield return MakePlayerInAir(Slot(12), 16f, freeFall);
        yield return WaitForState(freeFall.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a character released high above the floor");

        float freeStartY = freeFall.player.position.y;
        yield return StepSeconds(0.3f);
        float freeDrop = freeStartY - freeFall.player.position.y;

        // The same fall, but hugging a wall.
        PlayerResult sliding = new PlayerResult();
        yield return MakePlayerInAir(Slot(13), 16f, sliding, p =>
        {
            p.wallSlideGravityRatio = 0.25f;
            p.wallSlideMaxSpeed = 100f;    // out of the way, so gravity is what is measured
        });
        CreateWallBeside(sliding, 1f, 20f);

        yield return WaitForState(sliding.player, PlatformerPlayer2D.State.WallSliding, StateTimeout,
                                  "a character falling beside a wall");

        float slideStartY = sliding.player.position.y;
        yield return StepSeconds(0.3f);
        float slideDrop = slideStartY - sliding.player.position.y;

        Assert.Less(slideDrop, freeDrop * 0.9f,
            $"With wallSlideGravityRatio 0.25 a character should fall noticeably more slowly beside " +
            $"a wall than in the open, but it dropped {slideDrop:F3} units against {freeDrop:F3} in " +
            "free fall over the same time.");
    }

    [UnityTest]
    public IEnumerator WallSlide_DescentSpeedIsCappedAtWallSlideMaxSpeed()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(14), 25f, r, p =>
        {
            p.wallSlideMaxSpeed = 2f;
            p.wallSlideGravityRatio = 1f;   // full gravity, so only the cap can hold it back
        });
        CreateWallBeside(r, 1f, 30f);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.WallSliding, StateTimeout,
                                  "a character falling beside a tall wall");

        // Long enough that unrestrained gravity would be far past the cap.
        yield return StepSeconds(1.5f);

        SpeedResult speed = new SpeedResult();
        yield return MeasureAverageSpeed(r.player, 0.5f, speed);

        float descent = -speed.displacement.y / speed.seconds;

        Assert.LessOrEqual(descent, r.player.wallSlideMaxSpeed + SpeedTolerance,
            $"Wall sliding descent is capped at wallSlideMaxSpeed " +
            $"({r.player.wallSlideMaxSpeed:F3}), but the character was descending at " +
            $"{descent:F3} units/s.");
    }

    [UnityTest]
    public IEnumerator WallSlide_LeavingTheWall_ReturnsToFalling()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(Slot(15), 20f, r, p => p.wallSlideMaxSpeed = 3f);

        GameObject wall = CreateWallBeside(r, 1f, 24f);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.WallSliding, StateTimeout,
                                  "a character falling beside a wall");

        Object.DestroyImmediate(wall);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Falling, StateTimeout,
                                  "a wall-sliding character whose wall was removed");

        Assert.AreEqual(PlatformerPlayer2D.State.Falling, r.player.state,
            $"With no wall left to slide on the character should simply be falling, but it is " +
            $"{r.player.state}.");
    }

    // EXPECTED RED (PP-11). wallSlideWaitTimer is declared, exposed in the inspector and consulted
    // at two call sites - and never Started, Reset or Ticked anywhere in the class. timeLeft is
    // permanently 0, so isFinished is permanently true and both call sites are unconditional. Either
    // the grace period before a wall slide engages was never implemented, or this is scaffolding
    // that should be deleted. It is not harmless either way: the WallSliding branch of
    // ApplyAirMotion has no `else`, so the moment the timer IS started, gravityMultiplier keeps
    // whatever the previous state left in it - including the 0 that ApplyWallMotion writes.
    [UnityTest]
    public IEnumerator WallSlide_OnEntry_ArmsTheSlideDelayTimer()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToWallSliding(Slot(16), r);

        Assert.Greater(r.player.wallSlideWaitTimer.totalTime, 0f,
            $"Entering WallSliding should arm wallSlideWaitTimer - the grace period the two call " +
            $"sites in ApplyAirMotion and FixedUpdate test for - but its totalTime is " +
            $"{r.player.wallSlideWaitTimer.totalTime:F3}, meaning it has never been given a " +
            "duration and both of those tests are dead code.");
    }

    // ------------------------------------------------------------------
    // Wall grabbing
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator WallGrab_WithCanGrabWallFalse_IsRefused()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(17), r, p => p.canGrabWall = false);

        CreateWallBeside(r, 1f);
        yield return Step(2);

        r.player.GrabWall(true);
        r.player.velocity = new Vector2(0f, 8f);

        Trace trace = new Trace();
        yield return Record(r.player, 1f, trace);

        Assert.IsFalse(trace.Saw(PlatformerPlayer2D.State.Grabbing),
            $"With canGrabWall off the grab button should do nothing, but the character entered " +
            $"Grabbing. Observed: {trace.Describe()}.");
    }

    [UnityTest]
    public IEnumerator WallGrab_WhileGrabbing_VelocityIsZeroAndGravityIsSuppressed()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToGrabbing(Slot(18), r, p => p.maxWallGrabTime = 5f);

        float heldY = r.player.position.y;
        yield return StepSeconds(0.5f);

        Assert.AreEqual(PlatformerPlayer2D.State.Grabbing, r.player.state,
            "Precondition: the character should still be holding the wall.");
        Assert.AreEqual(heldY, r.player.position.y, PositionTolerance,
            $"A character gripping a wall does not move: it should still be at y {heldY:F3}, but " +
            $"it is at {r.player.position.y:F3}.");
        Assert.AreEqual(0f, r.player.gravityMultiplier, 0.01f,
            $"Gravity is switched off while gripping a wall, but gravityMultiplier is " +
            $"{r.player.gravityMultiplier:F3}.");
    }

    [UnityTest]
    public IEnumerator WallGrab_Released_LeavesGrabbing()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToGrabbing(Slot(19), r, p => p.maxWallGrabTime = 5f);

        r.player.GrabWall(false);

        yield return WaitWhileState(r.player, PlatformerPlayer2D.State.Grabbing, StateTimeout,
                                    "a character that released the grab button");

        Assert.AreNotEqual(PlatformerPlayer2D.State.Grabbing, r.player.state,
            "Releasing the grab button should let go of the wall.");
    }

    // EXPECTED RED. maxWallGrabTime is meant to limit how long a character can hold a wall. But the
    // budget lives in a Utils.Timer, and a fresh Timer reports isFinished from birth - the only
    // thing that ever loads it is the wallGrabTimer.Reset(maxWallGrabTime, false) that UpdateState
    // runs on grounded frames. So a character that has not yet touched the ground in its life -
    // one that spawns in mid-air, or is dropped into the level onto a wall - has no grab budget at
    // all and TryGrabbingWall refuses every time.
    [UnityTest]
    public IEnumerator WallGrab_WithoutEverTouchingGround_CanStillGrab()
    {
        float laneX = Slot(20);

        PlayerResult r = new PlayerResult();
        yield return MakePlayerInAir(laneX, 14f, r, p =>
        {
            p.canGrabWall = true;
            p.maxWallGrabTime = 5f;
        });

        CreateWallBeside(r, 1f, 20f);

        Assert.IsFalse(r.player.isGrounded,
            "Staging error: this character must never have touched the ground.");

        r.player.GrabWall(true);

        Trace trace = new Trace();
        yield return Record(r.player, 1f, trace);

        Assert.IsTrue(trace.Saw(PlatformerPlayer2D.State.Grabbing),
            $"A character that has never stood on the ground should still be able to grab a wall - " +
            $"maxWallGrabTime is a budget of {r.player.maxWallGrabTime:F3} s, not a reward for " +
            $"having landed. Observed: {trace.Describe()}.");
    }

    [UnityTest]
    public IEnumerator MaxWallGrabTime_TicksOnlyWhileAirborne()
    {
        PlayerResult r = new PlayerResult();
        yield return MakePlayerOnGround(Slot(21), r, p =>
        {
            p.canGrabWall = true;
            p.maxWallGrabTime = 2f;
        });

        CreateWallBeside(r, 1f);
        yield return Step(2);

        r.player.GrabWall(true);
        yield return WaitForState(r.player, PlatformerPlayer2D.State.Grabbing, StateTimeout,
                                  "a grounded character gripping a wall");

        float before = r.player.wallGrabTimer.timeLeft;
        yield return StepSeconds(1f);

        Assert.AreEqual(before, r.player.wallGrabTimer.timeLeft, 0.05f,
            $"Grab time is a budget for HANGING on a wall. A character with its feet on the floor " +
            $"is not spending it, but timeLeft went from {before:F3} to " +
            $"{r.player.wallGrabTimer.timeLeft:F3} over a second of standing still.");
    }

    [UnityTest]
    public IEnumerator MaxWallGrabTime_IsRefilledByTouchingTheGround()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToGrabbing(Slot(22), r, p => p.maxWallGrabTime = 0.5f);

        // Burn the budget: hold on until the wall lets go.
        yield return WaitWhileState(r.player, PlatformerPlayer2D.State.Grabbing, 2f,
                                    "a character holding grab past maxWallGrabTime");

        r.player.GrabWall(false);
        yield return WaitUntilGrounded(r.player, 3f, "the character after losing its grip");
        yield return Step(3);

        Assert.AreEqual(r.player.maxWallGrabTime, r.player.wallGrabTimer.timeLeft, 0.05f,
            $"Touching the ground refills the grab budget, so timeLeft should be back to " +
            $"maxWallGrabTime ({r.player.maxWallGrabTime:F3}), but it is " +
            $"{r.player.wallGrabTimer.timeLeft:F3}.");
    }

    // ------------------------------------------------------------------
    // Climbing a wall, and vaulting over its top
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator ClimbWallUp_UpInput_RisesAtWallClimbUpSpeed()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToClimbingWallUp(Slot(23), r, p =>
        {
            p.maxWallGrabTime = 5f;
            p.wallClimbUpSpeed = 2f;
        });

        SpeedResult speed = new SpeedResult();
        yield return MeasureAverageSpeed(r.player, 0.4f, speed);

        Assert.AreEqual(r.player.wallClimbUpSpeed, speed.verticalSpeed, SpeedTolerance,
            $"Climbing up should rise at wallClimbUpSpeed ({r.player.wallClimbUpSpeed:F3}), but the " +
            $"character rose at {speed.verticalSpeed:F3} units/s.");
    }

    [UnityTest]
    public IEnumerator ClimbWallUp_WithZeroWallClimbUpSpeed_StaysGrabbing()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToGrabbing(Slot(24), r, p =>
        {
            p.maxWallGrabTime = 5f;
            p.wallClimbUpSpeed = 0f;
        });

        r.player.Move(Vector2.up);

        Trace trace = new Trace();
        yield return Record(r.player, 0.5f, trace);

        Assert.IsFalse(trace.Saw(PlatformerPlayer2D.State.ClimbingWallUp),
            $"wallClimbUpSpeed 0 means the character cannot climb up at all, so up input should " +
            $"leave it gripping the wall. Observed: {trace.Describe()}.");
    }

    [UnityTest]
    public IEnumerator ClimbWallDown_DownInput_DescendsAtWallClimbDownSpeed()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToGrabbing(Slot(25), r, p =>
        {
            p.maxWallGrabTime = 5f;
            p.wallClimbDownSpeed = 4f;
        });

        // Climb up first, so there is room below to measure a descent before reaching the floor.
        r.player.Move(Vector2.up);
        yield return StepSeconds(0.8f);

        r.player.Move(Vector2.down);
        yield return WaitForState(r.player, PlatformerPlayer2D.State.ClimbingWallDown, StateTimeout,
                                  "a gripping character pushing down");

        SpeedResult speed = new SpeedResult();
        yield return MeasureAverageSpeed(r.player, 0.2f, speed);

        Assert.AreEqual(-r.player.wallClimbDownSpeed, speed.verticalSpeed, SpeedTolerance + 0.5f,
            $"Climbing down should descend at wallClimbDownSpeed " +
            $"({r.player.wallClimbDownSpeed:F3}), but the character moved at " +
            $"{speed.verticalSpeed:F3} units/s.");
    }

    [UnityTest]
    public IEnumerator ClimbWall_NeutralInput_ReturnsToGrabbing()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToClimbingWallUp(Slot(26), r, p => p.maxWallGrabTime = 5f);

        r.player.Move(Vector2.zero);

        yield return WaitForState(r.player, PlatformerPlayer2D.State.Grabbing, StateTimeout,
                                  "a climbing character that released the stick");

        Assert.AreEqual(PlatformerPlayer2D.State.Grabbing, r.player.state,
            $"Letting go of the stick while climbing should leave the character gripping the wall, " +
            $"but it is {r.player.state}.");
    }

    [UnityTest]
    public IEnumerator ClimbOverEdge_ReachingTheWallTop_EntersClimbingWallOver()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToClimbingWallOver(Slot(27), r);

        Assert.AreEqual(PlatformerPlayer2D.State.ClimbingWallOver, r.player.state,
            $"Climbing past the top of a wall should start the vault, but the state is " +
            $"{r.player.state}.");
    }

    [UnityTest]
    public IEnumerator ClimbOverEdge_PushesTowardTheWallSide()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToClimbingWallOver(Slot(28), r);

        float startX = r.player.position.x;
        yield return StepSeconds(0.3f);

        Assert.Greater(r.player.position.x - startX, 0.1f,
            $"The vault should carry the character over the top of the wall it was climbing - to " +
            $"the right, since wallDirection is {r.player.wallDirection:F1} - but it moved " +
            $"{r.player.position.x - startX:F3} units horizontally.");
    }

    [UnityTest]
    public IEnumerator ClimbOverEdge_LaunchesToClimbOverEdgeJumpHeight()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToClimbingWallOver(Slot(29), r, p => p.climbOverEdgeJumpHeight = 1.5f);

        ApexResult apex = new ApexResult();
        yield return MeasureApex(r.player, 2f, apex);

        Assert.AreEqual(1.5f, apex.apexHeight, JumpHeightTolerance + 0.3f,
            $"The vault boost is climbOverEdgeJumpHeight (1.5 units) above where it started, but " +
            $"the character rose {apex.apexHeight:F3}.");
    }

    // EXPECTED RED (PP-10). ClimbOverEdge computes its launch speed from the raw
    // Physics2D.gravity.y, ignoring gravityModifier, and assigns velocity.y unconditionally
    // positive, ignoring GravityDirection. Under a heavier gravity the vault falls short of the
    // height it promises; under inverted gravity it fires the character into the wall it is trying
    // to climb over. Jump() gets both of these right - this is the inconsistent one.
    [UnityTest]
    public IEnumerator ClimbOverEdge_UnderAGravityModifier_StillReachesTheConfiguredHeight()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToClimbingWallOver(Slot(30), r, p =>
        {
            p.climbOverEdgeJumpHeight = 1.5f;
            p.gravityModifier = 2f;
        });

        ApexResult apex = new ApexResult();
        yield return MeasureApex(r.player, 2f, apex);

        Assert.AreEqual(1.5f, apex.apexHeight, JumpHeightTolerance + 0.3f,
            $"climbOverEdgeJumpHeight is a height in world units, so it should be reached whatever " +
            $"gravityModifier is set to. Under gravityModifier 2 the vault reached only " +
            $"{apex.apexHeight:F3} of the configured 1.5 units - the launch speed is computed from " +
            "raw Physics2D.gravity and never scaled.");
    }

    // ------------------------------------------------------------------
    // Wall jumps and grab jumps
    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator WallJump_FromWallSlidingWithCanWallJump_EntersWallJumping()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToWallSliding(Slot(31), r, p => p.canWallJump = true);

        r.player.Jump(true);

        Assert.AreEqual(PlatformerPlayer2D.State.WallJumping, r.player.state,
            $"A jump taken while wall sliding with canWallJump on should be a wall jump, but the " +
            $"state is {r.player.state}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator WallJump_WithCanWallJumpFalse_IsRefused()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToWallSliding(Slot(32), r, p =>
        {
            p.canWallJump = false;
            p.maxAirJumps = 0;
            p.fallJumpTimeLimit = 0f;
        });

        r.player.Jump(true, false);

        Assert.AreNotEqual(PlatformerPlayer2D.State.WallJumping, r.player.state,
            $"With canWallJump off a jump pressed against a wall should be refused, but the state " +
            $"is {r.player.state}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator WallJump_PushesAwayAtWallJumpHorizontalVelocity()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToWallSliding(Slot(33), r, p =>
        {
            p.canWallJump = true;
            p.wallJumpHorizontalVelocity = 6f;
        });

        Assert.AreEqual(1f, r.player.wallDirection, DirectionTolerance,
            "Precondition: the wall should be on the character's right.");

        r.player.Jump(true);
        Vector2 launch = r.player.velocity;

        Assert.AreEqual(-6f, launch.x, SpeedTolerance,
            $"A wall jump pushes AWAY from the wall at wallJumpHorizontalVelocity, so with the wall " +
            $"on the right the launch should be -6 units/s, but it is {launch.x:F3}.");
        Assert.Greater(launch.y, 1f,
            $"A wall jump should also carry the character upward, but velocity.y is {launch.y:F3}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator GrabJump_FromGrabbing_EntersWallJumping()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToGrabbing(Slot(34), r, p =>
        {
            p.canJumpWhenGrabbing = true;
            p.maxWallGrabTime = 5f;
        });

        r.player.Jump(true);

        Assert.AreEqual(PlatformerPlayer2D.State.WallJumping, r.player.state,
            $"Jumping off a wall the character is gripping should enter WallJumping, but the state " +
            $"is {r.player.state}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator GrabJump_WithCanJumpWhenGrabbingFalse_IsRefused()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToGrabbing(Slot(35), r, p =>
        {
            p.canJumpWhenGrabbing = false;
            p.maxWallGrabTime = 5f;
            p.maxAirJumps = 0;
        });

        r.player.Jump(true, false);

        Assert.AreEqual(PlatformerPlayer2D.State.Grabbing, r.player.state,
            $"With canJumpWhenGrabbing off the character should stay on the wall, but the state is " +
            $"{r.player.state}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator GrabJump_PushesAwayAtGrabJumpHorizontalVelocity()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToGrabbing(Slot(36), r, p =>
        {
            p.canJumpWhenGrabbing = true;
            p.grabJumpHorizontalVelocity = 5f;
            p.maxWallGrabTime = 5f;
        });

        Assert.AreEqual(1f, r.player.wallDirection, DirectionTolerance,
            "Precondition: the wall should be on the character's right.");

        r.player.Jump(true);

        Assert.AreEqual(-5f, r.player.velocity.x, SpeedTolerance,
            $"A grab jump pushes away from the wall at grabJumpHorizontalVelocity, so with the wall " +
            $"on the right the launch should be -5 units/s, but it is {r.player.velocity.x:F3}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator GrabJump_FromClimbingWallUp_IsAlsoAllowed()
    {
        PlayerResult r = new PlayerResult();
        yield return DriveToClimbingWallUp(Slot(37), r, p =>
        {
            p.canJumpWhenGrabbing = true;
            p.maxWallGrabTime = 5f;
        });

        r.player.Jump(true);

        Assert.AreEqual(PlatformerPlayer2D.State.WallJumping, r.player.state,
            $"canJumpWhenGrabbing covers the climbing states too - a character part way up a wall " +
            $"can still push off it - but the state is {r.player.state}.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator GrabJump_HeightScalesWithGrabJumpHeightRatio()
    {
        PlayerResult full = new PlayerResult();
        yield return DriveToGrabbing(Slot(38), full, p =>
        {
            p.canJumpWhenGrabbing = true;
            p.grabJumpHeightRatio = 1f;
            p.grabJumpHorizontalVelocity = 0f;
            p.maxWallGrabTime = 5f;
        });

        full.player.Jump(true);
        float fullLaunch = full.player.velocity.y;

        PlayerResult half = new PlayerResult();
        yield return DriveToGrabbing(Slot(39), half, p =>
        {
            p.canJumpWhenGrabbing = true;
            p.grabJumpHeightRatio = 0.5f;
            p.grabJumpHorizontalVelocity = 0f;
            p.maxWallGrabTime = 5f;
        });

        half.player.Jump(true);
        float halfLaunch = half.player.velocity.y;

        Assert.AreEqual(fullLaunch * 0.5f, halfLaunch, SpeedTolerance,
            $"grabJumpHeightRatio scales the launch speed, so a ratio of 0.5 should launch at half " +
            $"the speed of a ratio of 1: {halfLaunch:F3} against {fullLaunch:F3}.");

        yield return null;
    }
}
