# PlatformerPlayer2D — API Reference

Side-view 2D character controller built on `KinematicMotion2D`: walking and running, variable-height
jumps, air jumps, coyote time, jump buffering, wall slide, wall jump, wall grab and wall climbing,
ladder-style climbing, dashing, and death.

- **Source**: `Assets/PuzzleBox/Scripts/Runtime/PlatformerPlayer2D.cs` (~1750 lines). Read this document
  instead; open the source only for something not covered here.
- **Namespace**: `PuzzleBox`. **Inherits**: `KinematicMotion2D` → `MonoBehaviour`. **Requires**:
  `Rigidbody2D` (inherited). **Optional**: `SpriteRenderer` (flipped to face the motion), `PlayerInput`
  (disabled by `Kill()`), both on the same GameObject.
- **Base class**: physics, collisions, moving platforms, pushing, attachments and contact events are in
  [KinematicMotion2D.API.md](KinematicMotion2D.API.md). This document covers only what the player adds.
- **Extending**: prefer a `PlatformerPlayerExtension` component (see [Extension points](#extension-points))
  over subclassing.
- **Inspector**: the custom editor `Scripts/Editor/PlatformerPlayer2DEditor.cs` (Japanese UI) draws each
  field explicitly. **A new field does not appear in the Inspector until it is added there.** Fields it
  does not draw are listed under [Inspector fields](#inspector-fields).
- **Prefab**: `Prefabs/Player.prefab` overrides several of the defaults below (e.g. hang and jump
  heights); read it for the shipped tuning.
- **Specification**: there are no tests for this class (see [Tests](#tests)). If this document and the
  code disagree, the code wins; fix this document.

---

## Core concepts

- **One state machine drives everything.** Each `FixedUpdate` first picks a `State` (see
  [States](#states)), then sets `velocity` and `gravityMultiplier` for that state (see
  [Motion per state](#motion-per-state)), then lets `KinematicMotion2D` move the body.
- **State decisions lag one step.** `UpdateState` runs before the base move, so it reads the previous
  step's `isGrounded`, `isTouchingWall` and `velocity`. The exception is landing: `Landed()` reruns
  `UpdateState` inside the base move so `OnLanded` listeners see a ground state.
- **Commands set held flags.** `Move`, `Run`, `GrabWall` and `Jump(true/false)` set flags that stay until
  changed (`motionInput`, `isRunning`, `isGrabbing`, `isJumping`). The state machine reads them every step.
  Input callbacks and scripts (e.g. NPC controllers) use the same public methods.
- **The player owns `gravityMultiplier`.** Almost every state rewrites it every step, so do not use it
  from scripts. Use `gravityModifier` for persistent changes. The "normal" value is whatever
  `gravityMultiplier` was at `Start` (`normalGravityMultiplier`, private).
- **Input threshold.** Most input tests use `SMALL_INPUT_THRESHOLD` (0.1). Ground walking and ground
  friction use any non-zero `motionInput.x`.
- **Velocity is relative to the platform** while grounded on a `KinematicMotion2D` (inherited). Speed caps
  and the jump speed boost all use this relative velocity.
- **Ground friction is the ground brake and the traction limit.** Braking and acceleration on the ground
  come from the inherited `friction` / `defaultGroundFriction` (see [Ground movement](#ground-movement)).
  With zero friction on either side the character can neither start nor stop on the ground.

---

## Inspector fields

Defaults are the code's. Inherited fields are in [KinematicMotion2D.API.md](KinematicMotion2D.API.md);
the ones that matter most here are `friction`, `defaultGroundFriction`, `gravityModifier` and
`pushPriority`.

The custom Inspector does **not** draw: `movementInputScaling`, `climbingJumpCoolDown`, `dashSpeedCurve`,
`minDashSpeed`, `deathAnimationTimeoutSeconds`, `acceptInput`, and the inherited `margin`,
`minSlideDistance`, `stickyGroundThreshold`, `maxIterations`, `sticky`. Edit them in Debug mode or from
code.

### Movement

| Field | Type | Default | Meaning |
|---|---|---|---|
| `faceMotionDirection` | `bool` | `true` | Set `SpriteRenderer.flipX` when `facingDirection.x < 0`. |
| `walkSpeed` | `float` | `3` | Ground speed cap while not `isRunning`. Speed above it is clipped instantly. |
| `walkAcceleration` | `float` | `10` | **Maximum** ground acceleration, scaled by `motionInput.x`. The actual value is `min(walkAcceleration, GroundFriction)`. |
| `runSpeed` / `runAcceleration` | `float` | `10` / `10` | Same, while `isRunning`. |
| `airSpeed` | `float` | `2` | Air speed cap for `velocity.x`. Raised to `abs(lastGroundVelocity.x)` while moving the same way as it, so a running jump keeps its speed. |
| `airAcceleration` | `float` | `10` | Air acceleration, scaled by `motionInput.x`. |
| `airBreakingForce` | `float` | `100` | Deceleration toward the air cap **while above it**. Not a brake: below the cap and without input, `velocity.x` is kept. |
| `hangSpeed` | `float` | `1` | Min 0. While `abs(velocity.y) < hangSpeed` in Jumping, WallJumping or Falling, gravity is multiplied by `hangGravityRatio`. 0 disables. |
| `hangGravityRatio` | `float` | `1` | Min 0. `< 1` floats at the apex, `0` stops there, `> 1` drops sooner. 1 disables. |
| `movementInputScaling` | `Vector2` | `(1, 1)` | Per-axis multiplier applied by `Move()`, e.g. `(-1, 1)` inverts horizontal input. |

### Jumping

Height ratios multiply the jump **velocity**, so height scales with the **square** of the ratio
(0.5 ≈ a quarter of the height).

| Field | Type | Default | Meaning |
|---|---|---|---|
| `minJumpHeight` | `float` | `1` | Min 0.1. Height when the jump is released at once (clamped to `maxJumpHeight`). |
| `maxJumpHeight` | `float` | `3` | Min 0.1. Height when held, from standing. |
| `jumpAngle` | `float` | `0` | Degrees from vertical, tilted toward `facingDirection`. Adds `vy * tan(angle)` to `velocity.x`. Ground, air and climb jumps only. |
| `jumpHeightSpeedBoost` | `float` | `2` | Added to the held height at full speed: `+ min(1, abs(velocity.x) / max(walkSpeed, runSpeed)) * jumpHeightSpeedBoost`. |
| `jumpGroundCheckDistance` | `float` | `0` | When Falling with no coyote time and **no air jumps left**, allow a ground jump if ground is within this distance (`CheckForGround`). |
| `jumpBufferTime` | `float` | `0.04` | A refused `Jump(true)` is retried at the end of every step for this long (`Time.time`). |
| `maxAirJumps` | `int` | `1` | Air jumps per airborne period. The count resets only in grounded motion. |
| `airJumpHeightRatio` | `float` | `0.5` | Velocity ratio for air jumps. |
| `fallJumpTimeLimit` | `float` | `0.05` | Coyote time: in Falling or Dashing with `timeInAir <=` this, a jump counts as a ground jump. |
| `canWallJump` | `bool` | `false` | Allow jumping from WallSliding. **Without it there is no jump of any kind from WallSliding.** |
| `wallJumpHeightRatio` | `float` | `1` | Velocity ratio. |
| `wallJumpHorizontalVelocity` | `float` | `1` | `velocity.x = -wallDirection * this`. |
| `canJumpWhenGrabbing` | `bool` | `true` | Allow jumping from Grabbing, ClimbingWallUp and ClimbingWallDown. |
| `grabJumpHeightRatio` | `float` | `1` | Velocity ratio. |
| `grabJumpHorizontalVelocity` | `float` | `1` | `velocity.x = -wallDirection * this`. |
| `canJumpWhenClimbing` | `bool` | `true` | Allow jumping from Climbing. |
| `climbJumpHeightRatio` | `float` | `1` | Velocity ratio. |

### Walls

| Field | Type | Default | Meaning |
|---|---|---|---|
| `wallCheckDistance` | `float` | `0.3` | Wall rays reach `bounds.extents.x + wallCheckDistance` from the center. |
| `wallCheckVerticalOffset` | `float` | `0` | Height of the wall rays relative to `bounds.center`. |
| `wallSlideGravityRatio` | `float` | `0.5` | Gravity multiplier (× normal) while WallSliding. |
| `wallSlideMaxSpeed` | `float` | `5` | Fall speed clamp while WallSliding, applied after the move. |
| `wallSlideWaitTime` | `float` | `0` | Seconds after entering WallSliding before `wallSlideGravityRatio` and `wallSlideMaxSpeed` apply. Until then the character falls with the gravity it already had. |
| `canGrabWall` | `bool` | `false` | Allow grabbing walls. |
| `maxWallGrabTime` | `float` | `1` | Max **continuous** airborne grab, in seconds (see [Walls](#walls-1)). |
| `wallClimbUpSpeed` | `float` | `2` | `velocity.y = motionInput.y * this` in ClimbingWallUp. 0 disables climbing up. |
| `wallClimbDownSpeed` | `float` | `4` | Same for ClimbingWallDown. 0 disables climbing down. |
| `climbOverEdgeJumpHeight` | `float` | `1` | Vertical boost when ClimbingWallUp loses the wall at the top. |
| `climbOverHorizontalVelocity` | `float` | `2` | `velocity.x` toward the wall during ClimbingWallOver. |

### Climbing (ladders, vines)

| Field | Type | Default | Meaning |
|---|---|---|---|
| `canClimb` | `bool` | `false` | Up input enters Climbing **anywhere**; there are no climbable surfaces. Turn it on and off with triggers. |
| `climbSpeedUp` / `climbSpeedDown` / `climbSpeedSide` | `float` | `2` / `3` / `2` | Climbing speed caps. |
| `climbAcceleration` | `float` | `20` | Per-axis acceleration while input is held. |
| `climbBreakingForce` | `float` | `50` | Per-axis deceleration toward 0 without input. |
| `climbCollisionMask` | `LayerMask` | `~0` | Replaces `collisionMask` while Climbing (`GetCollisionMask`): collisions and wall rays. |
| `climbingJumpCoolDown` | `float` | `0.5` | After a climb jump, airborne Climbing cannot be re-entered for this long. Landing clears it. |

### Dashing

| Field | Type | Default | Meaning |
|---|---|---|---|
| `canDash` | `bool` | `false` | Allow `Dash()`. |
| `dashSpeedSide` / `dashSpeedUp` / `dashSpeedDown` | `float` | `20` / `20` / `0` | Dash speed is `abs(dir.x) * side + abs(dir.y) * (dir.y < 0 ? down : up)`. A 45° dash is therefore faster than a straight one. |
| `dashTime` | `float` | `0.2` | Dash duration, in seconds. |
| `dashSpeedCurve` | `AnimationCurve` | `Linear(0,1,1,1)` | Speed multiplier over the dash, phase 0 → 1. Ignored with fewer than 2 keys. |
| `minDashSpeed` | `float` | `0` | The dash ends when `velocity.magnitude <= this`. At 0, only a dead stop ends it. |
| `dashInputFreezeTime` | `float` | `0.2` | Player input is ignored for this long after a dash starts. |
| `dashCoolDownTime` | `float` | `2` | Measured from dash **start**. |
| `limitDashAngle` | `bool` | `true` | Round the dash direction to the nearest 45°. |
| `dashGravityRatio` | `float` | `0` | Gravity multiplier while dashing in the air. |

### Animation

| Field | Type | Default | Meaning |
|---|---|---|---|
| `deathAnimationTimeoutSeconds` | `float` | `2` | Realtime delay between `Kill()` and `DestroySelf()`. |

## Script fields (hidden in the Inspector)

| Field | Type | Meaning |
|---|---|---|
| `acceptInput` | `bool` | Default `true`. Gates the input callbacks and the buffered jump, **not** the public methods. See [Input](#input). |
| `dashTimer` | `Utils.Timer` | Dash duration. Started by `PerformDash`; ticked only while in Dashing. Its `OnStart` / `OnEnd` drive `isDashing`. |
| `dashCoolDownTimer` | `Utils.Timer` | Started with each dash; `Dash()` requires it finished. |
| `wallGrabTimer` | `Utils.Timer` | Grab time left. Reset to full every grounded step; ticked only while airborne in Grabbing, ClimbingWallUp or ClimbingWallDown. |
| `wallSlideWaitTimer` | `Utils.Timer` | Started with `wallSlideWaitTime` (silently, no callbacks) when WallSliding is entered from another state; ticked every step. Wall-slide gravity and the speed clamp wait until it is finished. |
| `climbingJumpCoolDownTimer` | `Utils.Timer` | Started by climb jumps; cancelled on the ground and by `StopClimbing()`. |
| `OnJumped`, `OnLanded`, `OnDied`, `OnDestroyed`, `OnInputEnabledChanged` | `Action` / `Action<bool>` | See [Events and messages](#events-and-messages). |

`Utils.Timer` (`Scripts/Runtime/Utils/Timer.cs`): `timeLeft`, `totalTime`, `timeElapsed`, `phase` (0 → 1),
`isFinished` (`timeLeft <= 0`), `active` (pause). `Start(t)` only **extends** a running timer; `Reset(t)`
sets it unconditionally; `Cancel()` always invokes `OnCancel` and `OnEnd`.

## Properties

| Property | Type | Meaning |
|---|---|---|
| `state` | `State` | Current state. Setter is protected. |
| `isRunning` | `bool` | Flag set by `Run()`. |
| `isGrabbing` | `bool` | Flag set by `GrabWall()` (grab input held), not "is holding a wall": use `state`. |
| `isJumping` | `bool` | From a jump until `Jump(false)` or the Falling state. Holding it keeps normal gravity while rising. |
| `isDashing` | `bool` | `dashTimer` is running (see [Caveats](#caveats)). |
| `motionInput` | `Vector2` | Input after `movementInputScaling`. Zero during the dash input freeze. |
| `inputDirection` | `Vector2` | `motionInput.normalized` if its magnitude is over 0.1, else `facingDirection`. |
| `facingDirection` | `Vector2` | See [Facing](#facing). Can be diagonal. |
| `isTouchingWall` | `bool` | A wall ray hit something this step. |
| `wallDirection` | `float` | `-1` (left) or `1` (right). Keeps its last value when no wall is touched; `1` at `Start`. |
| `dashDirection` | `Vector2` | Unit direction of the current or last dash. |

## Public methods

```csharp
// Set the movement input (multiplied by movementInputScaling). It stays until changed. Not gated by
// acceptInput, but overwritten when acceptInput changes (see Input).
void Move(Vector2 input)

void Run(bool state)          // Held flag: Running state on the ground; picks runSpeed/runAcceleration.
void GrabWall(bool grabbing)  // Held flag: grabs when also touching a wall with canGrabWall.

// true: jump now if allowed (see Jumping); otherwise, if bufferInput, retry for jumpBufferTime.
// false: release; while still rising, gravity becomes stronger (variable height).
void Jump(bool jumpState, bool bufferInput = true)

// Dash toward motionInput (or facingDirection without input) if canDash and the cooldown is over.
void Dash()

// Dash without checking canDash or the cooldown; starts both timers and the input freeze.
// `speed` sets only the first step's velocity: later steps use the dashSpeed* fields.
void PerformDash(Vector2 direction, float speed)

// Climbing → Falling (even when grounded) and cancel the climb-jump cooldown. canClimb is unchanged,
// so held up input re-enters Climbing on the next step.
void StopClimbing()

// Once: disable input (acceptInput, PlayerInput) → OnDied → "WasKilled"; then DestroySelf() after
// deathAnimationTimeoutSeconds (realtime). Physics keeps running.
void Kill()

// Once: "WasDestroyed" → OnDestroyed → Destroy(gameObject).
void DestroySelf()
```

---

## FixedUpdate order

1. `UpdateState()`:
   - Tick `dashCoolDownTimer`, the input freeze timer, `wallSlideWaitTimer` and
     `climbingJumpCoolDownTimer`.
   - If grounded: reset `wallGrabTimer` to full and cancel the climb-jump cooldown.
   - If airborne in a grab state: tick `wallGrabTimer`.
   - Pick the state (see [States](#states)).
2. If grounded and no jump started since the last step: `ApplyGroundMotion()`, else `ApplyAirMotion()`.
   These set `velocity` and `gravityMultiplier` for the state (see [Motion per state](#motion-per-state)).
3. `base.FixedUpdate()`, the [KinematicMotion2D order](KinematicMotion2D.API.md#fixedupdate-order):
   ground check (may call `Landed` → `UpdateState`, `OnLanded`, `DidLand`), gravity, ground friction
   (overridden, see [Ground movement](#ground-movement)), speed limits, move.
4. `UpdateWallTouchingState()`: update `isTouchingWall` and `wallDirection`.
5. WallSliding: clamp `velocity.y` to `>= -wallSlideMaxSpeed` (if `wallSlideWaitTimer` is finished).
6. `UpdateFacingDirection()`.
7. Buffered jump: if one is pending and `acceptInput`, `Jump(true, false)`.

`Update()`: handles `acceptInput` changes (see [Input](#input)), then sets `SpriteRenderer.flipX`.
`Jump()` and `Dash()` called from input callbacks run outside `FixedUpdate`; they change `velocity` and
`state` immediately.

---

## States

```csharp
public enum State
{
    Walking, Running,       // On the ground (Walking also briefly after walking off a ledge)
    Dashing,
    Jumping, WallJumping,   // Rising from a jump; WallJumping only while still near the wall
    Falling,
    WallSliding,            // Airborne, touching a wall, moving down
    ClimbingWallUp, ClimbingWallDown, Grabbing,  // Holding a wall
    ClimbingWallOver,       // Boost over the top of a climbed wall
    Climbing                // Ladder-style climbing (canClimb)
}
```

`UpdateState` dispatches on the current state:

| Current state | Next |
|---|---|
| Walking, Running, Climbing, Jumping, WallJumping, Falling, WallSliding | Ground rules if grounded, else air rules. |
| Dashing | Tick `dashTimer`; cancel it if `velocity.magnitude <= minDashSpeed`. When it is finished: grounded → Walking, then ground rules; airborne → Falling, then air rules. Otherwise stay. |
| ClimbingWallUp | No wall touched → `ClimbOverEdge()` and ClimbingWallOver. Otherwise wall rules. |
| ClimbingWallDown, Grabbing | Wall rules. |
| ClimbingWallOver | Grounded → ground rules; `velocity.y < 0` → air rules; otherwise stay. |

The rules are checked top to bottom; the first match wins. "Up" and "down" mean `motionInput.y` beyond
±0.1. "Can grab" means `isGrabbing && canGrabWall` and the grab is not refused (see [Walls](#walls-1)).

**Ground rules** (only when grounded):
1. Touching a wall and can grab → Grabbing.
2. `canClimb` and up → Climbing.
3. `isRunning` → Running.
4. Walking.

**Air rules** (only when airborne):
1. In Climbing: Falling if `!canClimb`, otherwise stay.
2. Touching a wall: can grab → Grabbing; else `canClimb`, up and climb cooldown over → Climbing; else
   `velocity.y < 0` → WallSliding; else not `isJumping` → Falling; else stay.
3. `isJumping`: climb (as above) → Climbing; `velocity.y < 0` → Falling; else **Jumping** (this is how
   WallJumping becomes Jumping once away from the wall).
4. Otherwise: climb → Climbing; `velocity.y < 0` → Falling; else stay.

**Wall rules** (Grabbing, ClimbingWallUp, ClimbingWallDown):
- Not `isGrabbing`, not `canGrabWall`, no wall touched, or `wallGrabTimer` finished → ground or air rules.
- Up and `wallClimbUpSpeed > 0` → ClimbingWallUp; down and `wallClimbDownSpeed > 0` → ClimbingWallDown;
  else Grabbing.

Outside `UpdateState`: `Jump()` sets Jumping or WallJumping, `PerformDash()` sets Dashing, and
`StopClimbing()` sets Falling.

---

## Motion per state

Grounded motion always starts by resetting the air jump count and setting gravity to normal.

| State | Grounded | Airborne | `gravityMultiplier` (airborne) |
|---|---|---|---|
| Walking, Running | [Ground movement](#ground-movement) | Nothing (first step off a ledge) | Unchanged |
| Dashing | Dash velocity | Dash velocity | `dashGravityRatio` |
| Jumping | — | Air movement | Normal while `isJumping`; after release, while rising, `breakGravityMultiplier` (clamped so it cannot reverse `velocity.y`); then normal |
| WallJumping | — | Air movement | Normal, even after release |
| Falling | — | Air movement; clears `isJumping` | Normal |
| WallSliding | — | **Nothing: no horizontal control** | `wallSlideGravityRatio` × normal once `wallSlideWaitTimer` is finished; unchanged before |
| Grabbing | `velocity = 0` | `velocity = 0` | 0 |
| ClimbingWallUp / Down | `velocity.y = motionInput.y * wallClimbUp/DownSpeed` | Same | 0 |
| ClimbingWallOver | — | `velocity.x = climbOverHorizontalVelocity * wallDirection` | Normal |
| Climbing | Ladder movement | Ladder movement | 0 |

"—" means nothing is done, which happens only for the step in which those states touch the ground.
Afterwards, in Jumping, WallJumping and Falling, gravity is multiplied by `hangGravityRatio` while
`abs(velocity.y) < hangSpeed`.

### Ground movement

Walking and Running only:
- **Input** (`motionInput.x != 0`): `velocity += groundRight * motionInput.x * -GravityDirection *
  min(walk/runAcceleration, GroundFriction) * dt`. Then if `velocity.magnitude` exceeds
  `walkSpeed` / `runSpeed` (by `isRunning`), it is clipped to it at once.
- **No input**: the `ApplyGroundFriction` override lets the base friction act: `velocity.x` moves toward 0
  at `GroundFriction`. In every other state, ground friction is skipped.
- `GroundFriction = sqrt(friction * groundFriction) * Physics2D.gravity.magnitude`, where
  `groundFriction` is the platform's `friction`, or `defaultGroundFriction` on static ground. A low
  value gives slow starts and long slides.

### Air movement

Jumping, WallJumping and Falling only:
- `velocity.x += motionInput.x * airAcceleration * dt` (world x).
- Cap: `airSpeed`, raised to `abs(lastGroundVelocity.x)` when `velocity.x` has the same sign
  (`Mathf.Sign(0)` is `+1`).
- Above the cap, `velocity.x` moves toward it at `airBreakingForce`. There is no drag below it.

### Ladder movement

Climbing, each axis separately: with input beyond ±0.1, accelerate at `climbAcceleration` and clamp
(`±climbSpeedSide`; `-climbSpeedDown` to `climbSpeedUp`); without input, slow toward 0 at
`climbBreakingForce`.

---

## Jumping

`Jump(true)` checks the current state:

| State | Allowed | Kind |
|---|---|---|
| Walking, Running | Always | Ground |
| Grabbing, ClimbingWallUp, ClimbingWallDown | `canJumpWhenGrabbing` | Grab → WallJumping |
| Climbing | `canJumpWhenClimbing` | Climb → Jumping |
| WallSliding | `canWallJump` | Wall → WallJumping |
| Jumping, WallJumping | Air jumps left | Air → Jumping |
| Falling, Dashing | `timeInAir <= fallJumpTimeLimit` → ground; else air jumps left → air; else falling and ground within `jumpGroundCheckDistance` → ground | |
| ClimbingWallOver | Never | |

**Velocity.** With `g = abs(Physics2D.gravity.y * gravityModifier)` and `H = maxJumpHeight +
speedRatio * jumpHeightSpeedBoost`:
- `vy = sqrt(2 * g * normal * H)`, signed against gravity, plus one step of gravity to make up for the
  base applying gravity before the first move.
- `breakGravityMultiplier = normal * H / minJumpHeight`, so a jump released at once peaks near
  `minJumpHeight`.
- The kind's height ratio multiplies `vy`.

| Kind | `velocity.x` | `velocity.y` |
|---|---|---|
| Ground, air, climb | `+= vy * tan(jumpAngle)` toward the facing side | `vy` × ratio |
| Wall, grab | `= -wallDirection * wall/grabJumpHorizontalVelocity` | `vy` × ratio |

Then the jump: increments the air jump count (air jumps only), sets `isJumping`, clears the jump buffer,
invokes `OnJumped` and sends `DidJump`. A climb jump also starts `climbingJumpCoolDownTimer`.
`Jump(false)` clears `isJumping`.

**Buffering.** A refused `Jump(true, true)` is stored for `jumpBufferTime` and retried at the end of every
`FixedUpdate` with `Jump(true, false)`. A later `Jump(false)` does not clear it.

---

## Dashing

- **Direction**: `motionInput.normalized` if its magnitude is over 0.1, else `facingDirection`. Rounded
  to 45° if `limitDashAngle`.
- **Start** (`PerformDash`): set `velocity`, start `dashTimer` and `dashCoolDownTimer`, start the input
  freeze (only if `acceptInput`), enter Dashing, send `DidDash`. There is no C# event for dashes.
- **Each step**: only the velocity component along `dashDirection` is set (`dash speed *
  dashSpeedCurve(phase)`). The perpendicular component is kept, so gravity (scaled by
  `dashGravityRatio`) builds up into an arc. The exception is a purely vertical dash, where the dash
  axis and gravity's axis are the same and gravity has no effect.
- **Ends** when `dashTimer` finishes or is cancelled. It is cancelled by:
  - speed `<= minDashSpeed`;
  - a new contact with a wall whose probe direction is within 45° of the velocity;
  - an `OnMove` input message (only after the input freeze, which by default lasts as long as the dash).
- **After**: the velocity carries over. On the ground, Walking clips it to `walkSpeed` at once; in the
  air, `airBreakingForce` brings it down to the air cap.
- **Input freeze**: on start, `acceptInput = false` and `motionInput = 0`; on end, `acceptInput = true`
  and the last input is restored.

---

## Walls

- **Detection** (after each move): one ray left and one right from `bounds.center + up *
  wallCheckVerticalOffset`, length `bounds.extents.x + wallCheckDistance`, mask `GetCollisionMask()`.
  Hits on the player's own GameObject are ignored. If both sides hit, `wallDirection` follows the
  facing side.
- **Wall slide**: entering WallSliding starts `wallSlideWaitTimer` with `wallSlideWaitTime`. Once it is
  finished: gravity × `wallSlideGravityRatio`, fall speed capped at `wallSlideMaxSpeed`. No horizontal
  control: leaving the wall takes a wall jump, the ground, or existing horizontal velocity.
- **Grab**:
  - A grab is refused if `wallGrabTimer` is finished or the state is WallJumping, so the character
    cannot re-grab the wall it just jumped off.
  - Entering Grabbing from another state calls `wallGrabTimer.Start(maxWallGrabTime)`. That **refills**
    a partly used timer: the limit applies to continuous grabbing, and once it has run out, no grab is
    possible until grounded.
  - While holding, `velocity` is zero (or vertical when climbing) and the character stays where it was
    when the grab began, up to `wallCheckDistance` from the wall.
- **Climb over**: when ClimbingWallUp no longer touches a wall, `velocity.y` is set for a
  `climbOverEdgeJumpHeight` rise and `velocity.x` to `climbOverHorizontalVelocity` toward the wall,
  until the character lands or starts falling.

## Climbing

`canClimb` turns the whole area into a ladder: up input enters Climbing. In Climbing, gravity is 0 and the
collision mask is `climbCollisionMask`. On the ground, Climbing needs up held every step; once airborne,
it lasts until `canClimb` turns off, a jump, a dash, or `StopClimbing()` with no up input. Entering it
from the air requires the climb-jump cooldown to be over.

## Facing

After each move:
- **Holding a wall** (Grabbing, ClimbingWallUp/Down): `facingDirection = (wallDirection, 0)`.
- **Otherwise, with `abs(motionInput.x) >= 0.1`**: `facingDirection = motionInput.normalized`, which can
  be diagonal. The last horizontal side is remembered (protected `defaultFacingDirection`).
- **Otherwise**: `facingDirection` is that remembered side, which starts as right.

---

## Input

Input arrives as `SendMessage` callbacks. They can come from a `PlayerInput` in Send/Broadcast Messages
mode, or from `PlayerInputHelper` (`Scripts/Runtime/Utils/PlayerInputHelper.cs`), which forwards a
`PlayerInput` on another GameObject. The parameter must be `UnityEngine.InputSystem.InputValue` or
`PuzzleBox.InputValue`; anything else reads as zero or not pressed. From scripts, call the public methods
instead.

| Callback | Action | Effect |
|---|---|---|
| `OnMove` | `Move` (Value) | Store the raw input; if `acceptInput`: `Move(value)`, and cancel a dash in progress. |
| `OnRun` | `Run` | Press: `Run(true)` if `acceptInput`. Release: `Run(false)` always. |
| `OnGrabWall` | `GrabWall` | Press: `GrabWall(true)` if `acceptInput`. Release: `GrabWall(false)` always. |
| `OnJump` | `Jump` | Press: `Jump(true, true)` if `acceptInput`. Release: `Jump(false, true)` always. |
| `OnDash` | `Dash` | `Dash()` if `acceptInput`, on **any** message (press or release). |

Run, GrabWall and Jump need release messages. `Input/Platformer_Actions.inputactions` gives Jump, Run,
Dash and GrabWall a Press interaction set to Press and Release.

`acceptInput`:
- Blocks press callbacks and the buffered jump.
- Public methods ignore it.
- When it changes, `Update` calls `Move(Vector2.zero)` (on `false`) or `Move(<last raw input>)` (on
  `true`), replacing any motion set by a script.
- Only `Kill()` invokes `OnInputEnabledChanged`; setting the field directly does not.

---

## Events and messages

C# callbacks, invoked before the matching message:

```csharp
public Action OnJumped;      // In Jump(), after velocity and state are set
public Action OnLanded;      // In Landed(), inside the base move, after a forced UpdateState()
public Action OnDied;        // In Kill()
public Action OnDestroyed;   // In DestroySelf(), just before Destroy(gameObject)
public event Action<bool> OnInputEnabledChanged;  // Only from Kill() (false)
```

All but `OnInputEnabledChanged` are plain fields: assigning with `=` replaces every other subscriber;
use `+=`.

`SendMessage` to the player's own GameObject (`DontRequireReceiver`):

| Message | When |
|---|---|
| `DidJump` | After `OnJumped` |
| `DidLand` | After `OnLanded` |
| `DidDash` | Dash started (no C# event) |
| `WasKilled` | After `OnDied` |
| `WasDestroyed` | Before `OnDestroyed` |

Contact messages (`OnContactEnter/Stay/Exit`) come from the base class.

---

## Extension points

**`PlatformerPlayerExtension`** (`Scripts/Runtime/PlatformerPlayerExtension.cs`) is the preferred way to
add behaviour. Put a subclass next to the player; it requires `PlatformerPlayer2D`. It caches
`player` in `Awake` and offers empty virtual hooks to override:

```csharp
protected virtual void OnPlayerJumped() / OnPlayerLanded() / OnPlayerDied() / OnPlayerDestroyed()
protected virtual void OnPlayerInputEnabledChanged(bool enabled)
protected virtual void DidJump() / DidDash() / DidLand() / WasKilled() / WasDestroyed()  // Messages
protected virtual void OnContactEnter/Exit/Stay(KinematicMotion2D.Contact contact)        // Messages
```

It subscribes to the C# events in `OnEnable`, so a subclass overriding `OnEnable` / `OnDisable` must
call the base method. Dashing is available only as `DidDash`.

**Subclassing.** The player overrides these base members; they can be overridden again, but call the
base:

```csharp
protected override void Awake()        // Wires dashTimer → isDashing, the input freeze callbacks
protected override void Start()        // Captures normal gravity; Walking; facing right; timers
protected override void Update()       // acceptInput changes, sprite flip
protected override void FixedUpdate()  // See "FixedUpdate order"
protected override void Landed(float speed)               // UpdateState(), OnLanded, DidLand
protected override void ApplyGroundFriction(float dt)     // Friction only when braking (Walking/Running, no input)
protected override LayerMask GetCollisionMask()           // climbCollisionMask while Climbing
protected override bool CanPush(KinematicMotion2D other, Vector2 delta)  // Base rule (also requires other.pushable)
protected override float ProcessCollision(RaycastHit2D hit, Vector2 direction, float distanceRemaining)  // Effectively base
protected override void ContactEnter(Contact contact)     // Cancels a dash on a head-on wall contact
```

Also protected: the `state` setter, `SMALL_INPUT_THRESHOLD` (0.1), `defaultFacingDirection`, and
`UpdateWallTouchingState()`. The state machine and motion code (`UpdateState`, `ApplyGroundMotion`,
`ApplyAirMotion`, `CanJump`, …) are private. To change them, edit the class.

---

## Caveats

Observed in the code; not covered by tests.

- **A buffered jump ignores its release.** If the button is pressed and released before the buffered
  jump fires, it fires with `isJumping == true` and rises to full height.
- **Jumping out of a dash leaves `isDashing` true.** `dashTimer` ticks only in Dashing, so it stays
  running until the next dash.
- **Height ratios scale velocity, not height** (height ∝ ratio²).
- **No jump from WallSliding without `canWallJump`**, not even an air jump, and no horizontal control
  while sliding.
- **`OnDash` dashes on release too.** With a Press and Release interaction, releasing dashes again if the
  cooldown is already over.
- **The input freeze ends with `acceptInput = true`**, even if `Kill()` disabled input during the dash.
- **Moving-platform jumps**: `Jump()` adds `groundVelocity` to `lastGroundVelocity`, which already
  includes it, so the air speed cap uses twice the platform's speed.
- **Reversed gravity is partial.** Ground movement and jump velocity follow `GravityDirection`. Air
  acceleration, `ClimbOverEdge`, the wall-slide clamp, the release-gravity clamp and the jump-height gizmos
  assume normal gravity.
- **Some timers tick twice on landing** (the cooldowns, the input freeze, `wallSlideWaitTimer`,
  `dashTimer`): `Landed()` reruns
  `UpdateState`.
- **Pushing needs a higher `pushPriority`** (base rule). At the default 0 the player pushes nothing with
  priority 0. The `CanPush` override adds nothing to the base.
- **`ProcessCollision` is a placeholder**: head-bump corner correction (`AvoidPlatformEdge`) is commented
  out and always returns `false`.
- **Wall rays only skip the player's own GameObject**: colliders on child objects or held attachments, and
  triggers (when `Physics2D.queriesHitTriggers`), count as walls.

---

## Tests

None. `Tests/Runtime/` covers `KinematicMotion2D` only; this document was written from the code.
`OnDrawGizmosSelected` draws the min/max jump heights (without the speed boost) and the wall rays, which
helps when checking behaviour by hand.
