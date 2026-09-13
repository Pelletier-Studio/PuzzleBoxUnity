# PlatformerPlayer2D — API Reference / APIリファレンス

Full-featured 2D platformer character controller with 12-state state machine.

**Source**: `Assets/PuzzleBox/Scripts/Runtime/PlatformerPlayer2D.cs` (do not read directly)
**Namespace**: `PuzzleBox`
**Inherits**: `KinematicMotion2D` → `MonoBehaviour`

---

## State Enum

```csharp
public enum State
{
    Walking,           // Default ground state
    Running,           // Ground + run input held
    Dashing,           // Active dash (ground or air)
    Jumping,           // Ascending from jump
    WallJumping,       // Ascending from wall jump
    Falling,           // Airborne, descending
    WallSliding,       // Sliding down wall
    ClimbingWallUp,    // Climbing wall upward
    ClimbingWallDown,  // Climbing wall downward
    ClimbingWallOver,  // Vaulting over wall top
    Climbing,          // On climbable surface
    Grabbing           // Holding onto wall
}
```

### State Transition Rules / ステート遷移ルール

**From ground states** (Walking, Running):
- → Jumping (jump input)
- → Falling (walk off edge)
- → Dashing (dash input + `canDash`)
- → Climbing (up input + `canClimb`)
- → Grabbing (touching wall + grab input + `canGrabWall`)
- Walking ↔ Running (run input toggle)

**From Jumping / WallJumping**:
- → Falling (velocity.y < 0)
- → WallSliding (touching wall + velocity.y < 0)
- → Climbing (up input + `canClimb`)

**From Falling**:
- → Walking/Running (landed)
- → WallSliding (touching wall)
- → Climbing (up input + `canClimb`)
- → Jumping (air jump if `maxAirJumps` > 0 and jumps remaining)
- → Jumping (coyote time: `fallJumpTimeLimit` seconds after leaving ground)

**From Dashing**:
- → Walking/Running (dash timer ends + grounded)
- → Falling (dash timer ends + airborne)
- → Cancelled early if hitting wall or speed drops below `minDashSpeed`

**From WallSliding**:
- → Walking/Running (landed)
- → WallJumping (jump input + `canWallJump`)
- → Falling (no longer touching wall)

**From Grabbing**:
- → ClimbingWallUp (up input)
- → ClimbingWallDown (down input)
- → Falling (grab timer expires: `maxWallGrabTime`)
- → WallJumping (jump input + `canJumpWhenGrabbing`)
- → Falling (release grab or no longer touching wall)

**From ClimbingWallUp**:
- → ClimbingWallOver (no longer touching wall = reached top)
- → Grabbing (stop input)
- → Falling (grab timer expires)

**From ClimbingWallOver**:
- → Walking/Running (landed)
- → Falling (velocity.y < 0)

---

## Public Fields

### Movement / 移動
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `walkSpeed` | `float` | `3f` | Walk speed cap |
| `walkAcceleration` | `float` | `10f` | Walk acceleration |
| `runSpeed` | `float` | `10f` | Run speed cap |
| `runAcceleration` | `float` | `10f` | Run acceleration |
| `breakingForce` | `float` | `10f` | Ground deceleration |
| `airSpeed` | `float` | `2f` | Air horizontal speed cap |
| `airAcceleration` | `float` | `10f` | Air horizontal acceleration |
| `airBreakingForce` | `float` | `100f` | Air deceleration |
| `hangSpeed` | `float` | `1f` | ⚠️ Speed threshold for apex hang effect |
| `hangGravityRatio` | `float` | `1f` | ⚠️ Gravity multiplier during apex hang |
| `movementInputScaling` | `Vector2` | `(1,1)` | Scale/invert input axes |

> ⚠️ **Interaction**: When `|velocity.y| < hangSpeed`, gravity is multiplied by `hangGravityRatio`. Set `hangGravityRatio < 1` to create a floaty apex. This affects Jumping, WallJumping, and Falling states.

### Jump / ジャンプ
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `minJumpHeight` | `float` | `1f` | Height on tap |
| `maxJumpHeight` | `float` | `3f` | Height on hold |
| `jumpAngle` | `float` | `0°` | Angular offset from vertical |
| `jumpHeightSpeedBoost` | `float` | `2f` | ⚠️ Extra height from horizontal speed |
| `jumpGroundCheckDistance` | `float` | `0f` | Downward raycast for near-ground jump |
| `jumpBufferTime` | `float` | `0.04f` | Input buffer window (seconds) |
| `maxAirJumps` | `int` | `1` | Air jumps allowed |
| `airJumpHeightRatio` | `float` | `0.5f` | Air jump height vs normal |
| `fallJumpTimeLimit` | `float` | `~0.05f` | Coyote time (seconds) |

> ⚠️ **Interaction**: `jumpHeightSpeedBoost` adds `speedRatio * jumpHeightSpeedBoost` to `maxJumpHeight`, where `speedRatio = |velocity.x| / max(walkSpeed, runSpeed)`. Running fast = jumping higher.

> ⚠️ **Computed value**: `breakGravityMultiplier` (internal) = `gravityMultiplier * (adjustedMaxJumpHeight / minJumpHeight)`. This controls how fast the player falls after releasing jump. The ratio `maxJumpHeight / minJumpHeight` determines jump feel — a large ratio means much stronger gravity on release.

### Wall Jump
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `canWallJump` | `bool` | `false` | Enable wall jump |
| `wallJumpHeightRatio` | `float` | `1f` | Height ratio vs normal jump |
| `wallJumpHorizontalVelocity` | `float` | `1f` | Horizontal kick-off speed |

### Grab Jump
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `canJumpWhenGrabbing` | `bool` | `true` | Jump while grabbing wall |
| `grabJumpHeightRatio` | `float` | `1f` | Height ratio |
| `grabJumpHorizontalVelocity` | `float` | `1f` | Horizontal kick-off |

### Climb Jump
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `canJumpWhenClimbing` | `bool` | `true` | Jump while climbing |
| `climbJumpHeightRatio` | `float` | `1f` | Height ratio |

### Wall Detection
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `wallCheckVerticalOffset` | `float` | `0f` | Raycast vertical offset from center |
| `wallCheckDistance` | `float` | `0.3f` | Raycast max distance |
| `wallSlideGravityRatio` | `float` | `0.5f` | Gravity ratio while wall sliding |
| `wallSlideMaxSpeed` | `float` | `5f` | Max wall slide speed |

### Wall Grab
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `canGrabWall` | `bool` | `false` | Enable wall grab |
| `maxWallGrabTime` | `float` | `1f` | Max grab duration (seconds) |
| `wallClimbUpSpeed` | `float` | `2f` | Climb up speed |
| `wallClimbDownSpeed` | `float` | `4f` | Climb down speed |
| `climbOverEdgeJumpHeight` | `float` | `1f` | Edge vault height |
| `climbOverHorizontalVelocity` | `float` | `2f` | Edge vault horizontal speed |

### Climbing (Ladders/Surfaces) 
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `canClimb` | `bool` | `false` | Enable climbing  |
| `climbSpeedUp` | `float` | `2f` | Upward climb speed |
| `climbSpeedDown` | `float` | `3f` | Downward climb speed |
| `climbSpeedSide` | `float` | `2f` | Sideways climb speed |
| `climbAcceleration` | `float` | `20f` | Climbing acceleration |
| `climbBreakingForce` | `float` | `50f` | Climbing deceleration |
| `climbCollisionMask` | `LayerMask` | `~0` | Collision layers while climbing |

### Dash
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `canDash` | `bool` | `false` | Enable dash |
| `dashSpeedSide` | `float` | `20f` | Horizontal dash speed |
| `dashSpeedUp` | `float` | `20f` | Upward dash speed |
| `dashSpeedDown` | `float` | `0f` | Downward dash speed |
| `dashTime` | `float` | `0.2f` | Dash duration (seconds) |
| `dashSpeedCurve` | `AnimationCurve` | Linear(0,1→1,1) | Speed over dash lifetime |
| `minDashSpeed` | `float` | `0f` | Cancel dash below this speed |
| `dashInputFreezeTime` | `float` | `0.2f` | Ignore input after dash start |
| `dashCoolDownTime` | `float` | `2f` | Cooldown between dashes |
| `limitDashAngle` | `bool` | `true` | Snap to 8 directions |
| `dashGravityRatio` | `float` | `0f` | Gravity during dash |

### Other
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `deathAnimationTimeoutSeconds` | `float` | `2f` | Delay before Destroy after Kill() |

---

## Timers

| Timer | Purpose | Duration Source |
|-------|---------|----------------|
| `dashTimer` | Dash duration | `dashTime` |
| `dashCoolDownTimer` | Dash cooldown | `dashCoolDownTime` |
| `wallSlideWaitTimer` | Delay before wall slide gravity kicks in | (external) |
| `wallGrabTimer` | Wall grab time limit | `maxWallGrabTime` |

All are `PuzzleBox.Utils.Timer` with properties: `timeLeft`, `totalTime`, `phase` (0→1), `isFinished`, `active`.

---

## Public Properties

| Property | Type | Access | Description |
|----------|------|--------|------------|
| `state` | `State` | get | Current state |
| `isRunning` | `bool` | get | Run input held |
| `isJumping` | `bool` | get | Currently in jump ascent |
| `isGrabbing` | `bool` | get | Grab input held |
| `isDashing` | `bool` | get | Dash timer active |
| `isTouchingWall` | `bool` | get | Wall detected via raycast |
| `wallDirection` | `float` | get | -1 (left wall) or 1 (right wall)  |
| `motionInput` | `Vector2` | get | Processed movement input |
| `facingDirection` | `Vector2` | get | Current facing |
| `dashDirection` | `Vector2` | get | Dash direction |
| `inputDirection` | `Vector2` | get | motionInput if significant, else facingDirection |

Also inherits from KinematicMotion2D: `isGrounded`, `velocity`, `position`, `groundNormal`, `groundRight`, `timeInAir`, `bounds`, etc.

---

## Public Methods / パブリックメソッド

```csharp

// Set movement input. Cancels active dash.
void Move(Vector2 input)

// Toggle run mode.
void Run(bool state)


// jumpState=true: initiate jump. jumpState=false: release (variable height).
void Jump(bool jumpState, bool bufferInput = true)

// Initiate dash in input direction (or facing direction).
void Dash()

// Toggle wall grab.
void GrabWall(bool grabbing)

// Force exit Climbing state → Falling.
void StopClimbing()

// Start death sequence: disable input → OnDied → wait → DestroySelf.
void Kill()

// Immediate destroy: OnDestroyed → Destroy(gameObject).
void DestroySelf()

```

---

## Events & Messages

### C# Events (subscribe in code) 
```csharp
Action OnJumped;       // Fired when jump executes
Action OnLanded;       // Fired when landing on ground
Action OnDied;         // Fired when Kill() called / Kill()
Action OnDestroyed;    // Fired just before Destroy
event Action<bool> OnInputEnabledChanged; // When input is toggled
```

### SendMessage (received by sibling components) 
| Message | When |
|---------|------|
| `DidJump` | Jump executed |
| `DidDash` | Dash started |
| `DidLand` | Landed on ground |
| `WasKilled` | Kill() called |
| `WasDestroyed` | About to be destroyed |

### Contact Messages (from KinematicMotion2D) 
| Message | Parameter | When |
|---------|-----------|------|
| `OnContactEnter` | `Contact` | New collision |
| `OnContactExit` | `Contact` | Collision ended |
| `OnContactStay` | `Contact` | Ongoing collision |

---

## Input Actions

The player receives input via Unity's Input System `SendMessage` callbacks:
| Callback | Input Type | Maps To |
|----------|-----------|---------|
| `OnMove(object)` | `Vector2` | `Move()` |
| `OnRun(object)` | `bool` (press) | `Run()` |
| `OnJump(object)` | `bool` (press/release) | `Jump()` |
| `OnDash(object)` | `bool` (press) | `Dash()` |
| `OnGrabWall(object)` | `bool` (press) | `GrabWall()` |
