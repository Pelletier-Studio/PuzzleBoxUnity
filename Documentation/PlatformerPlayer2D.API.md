# PlatformerPlayer2D — API Reference / APIリファレンス

Full-featured 2D platformer character controller with 12-state state machine.
12ステートのステートマシンを持つ2Dプラットフォーマーキャラクターコントローラー。

**Source**: `Assets/PuzzleBox/Scripts/Runtime/PlatformerPlayer2D.cs` (do not read directly / 直接読まないでください)
**Namespace**: `PuzzleBox`
**Inherits**: `KinematicMotion2D` → `MonoBehaviour`

---

## State Enum / ステート列挙型

```csharp
public enum State
{
    Walking,           // Default ground state / デフォルトの地上ステート
    Running,           // Ground + run input held / 地上＋走り入力中
    Dashing,           // Active dash (ground or air) / ダッシュ中（地上・空中）
    Jumping,           // Ascending from jump / ジャンプ上昇中
    WallJumping,       // Ascending from wall jump / 壁ジャンプ上昇中
    Falling,           // Airborne, descending / 空中落下中
    WallSliding,       // Sliding down wall / 壁を滑り落ちている
    ClimbingWallUp,    // Climbing wall upward / 壁を登っている
    ClimbingWallDown,  // Climbing wall downward / 壁を下りている
    ClimbingWallOver,  // Vaulting over wall top / 壁の上端を越えている
    Climbing,          // On climbable surface / 登れる面を登っている
    Grabbing           // Holding onto wall / 壁に掴まっている
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

## Public Fields / パブリックフィールド

### Movement / 移動
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `walkSpeed` | `float` | `3f` | Walk speed cap / 歩行速度上限 |
| `walkAcceleration` | `float` | `10f` | Walk acceleration / 歩行加速度 |
| `runSpeed` | `float` | `10f` | Run speed cap / 走り速度上限 |
| `runAcceleration` | `float` | `10f` | Run acceleration / 走り加速度 |
| `breakingForce` | `float` | `10f` | Ground deceleration / 地上減速力 |
| `airSpeed` | `float` | `2f` | Air horizontal speed cap / 空中横移動速度上限 |
| `airAcceleration` | `float` | `10f` | Air horizontal acceleration / 空中横加速度 |
| `airBreakingForce` | `float` | `100f` | Air deceleration / 空中減速力 |
| `hangSpeed` | `float` | `1f` | ⚠️ Speed threshold for apex hang effect / 頂点滞空効果の速度しきい値 |
| `hangGravityRatio` | `float` | `1f` | ⚠️ Gravity multiplier during apex hang / 頂点滞空中の重力倍率 |
| `movementInputScaling` | `Vector2` | `(1,1)` | Scale/invert input axes / 入力軸のスケール・反転 |

> ⚠️ **Interaction**: When `|velocity.y| < hangSpeed`, gravity is multiplied by `hangGravityRatio`. Set `hangGravityRatio < 1` to create a floaty apex. This affects Jumping, WallJumping, and Falling states.

### Jump / ジャンプ
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `minJumpHeight` | `float` | `1f` | Height on tap / ボタン短押しの高さ |
| `maxJumpHeight` | `float` | `3f` | Height on hold / ボタン長押しの高さ |
| `jumpAngle` | `float` | `0°` | Angular offset from vertical / 垂直からの角度オフセット |
| `jumpHeightSpeedBoost` | `float` | `2f` | ⚠️ Extra height from horizontal speed / 助走による追加高さ |
| `jumpGroundCheckDistance` | `float` | `0f` | Downward raycast for near-ground jump / 着地寸前ジャンプの検出距離 |
| `jumpBufferTime` | `float` | `0.04f` | Input buffer window (seconds) / 入力バッファ時間（秒） |
| `maxAirJumps` | `int` | `1` | Air jumps allowed / 空中ジャンプ回数 |
| `airJumpHeightRatio` | `float` | `0.5f` | Air jump height vs normal / 空中ジャンプの高さ比率 |
| `fallJumpTimeLimit` | `float` | `~0.05f` | Coyote time (seconds) / コヨーテタイム（秒） |

> ⚠️ **Interaction**: `jumpHeightSpeedBoost` adds `speedRatio * jumpHeightSpeedBoost` to `maxJumpHeight`, where `speedRatio = |velocity.x| / max(walkSpeed, runSpeed)`. Running fast = jumping higher.

> ⚠️ **Computed value**: `breakGravityMultiplier` (internal) = `gravityMultiplier * (adjustedMaxJumpHeight / minJumpHeight)`. This controls how fast the player falls after releasing jump. The ratio `maxJumpHeight / minJumpHeight` determines jump feel — a large ratio means much stronger gravity on release.

### Wall Jump / 壁ジャンプ
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `canWallJump` | `bool` | `false` | Enable wall jump / 壁ジャンプを有効にする |
| `wallJumpHeightRatio` | `float` | `1f` | Height ratio vs normal jump / 通常ジャンプとの高さ比率 |
| `wallJumpHorizontalVelocity` | `float` | `1f` | Horizontal kick-off speed / 横方向の蹴り出し速度 |

### Grab Jump / 掴みジャンプ
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `canJumpWhenGrabbing` | `bool` | `true` | Jump while grabbing wall / 壁掴み中にジャンプできるか |
| `grabJumpHeightRatio` | `float` | `1f` | Height ratio / 高さ比率 |
| `grabJumpHorizontalVelocity` | `float` | `1f` | Horizontal kick-off / 横方向の蹴り出し速度 |

### Climb Jump / 登りジャンプ
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `canJumpWhenClimbing` | `bool` | `true` | Jump while climbing / 登り中にジャンプできるか |
| `climbJumpHeightRatio` | `float` | `1f` | Height ratio / 高さ比率 |

### Wall Detection / 壁検知
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `wallCheckVerticalOffset` | `float` | `0f` | Raycast vertical offset from center / レイキャストの垂直オフセット |
| `wallCheckDistance` | `float` | `0.3f` | Raycast max distance / レイキャストの最大距離 |
| `wallSlideGravityRatio` | `float` | `0.5f` | Gravity ratio while wall sliding / 壁滑り中の重力比率 |
| `wallSlideMaxSpeed` | `float` | `5f` | Max wall slide speed / 壁滑りの最大速度 |

### Wall Grab / 壁掴み
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `canGrabWall` | `bool` | `false` | Enable wall grab / 壁掴みを有効にする |
| `maxWallGrabTime` | `float` | `1f` | Max grab duration (seconds) / 最大掴み時間（秒） |
| `wallClimbUpSpeed` | `float` | `2f` | Climb up speed / 壁登り速度 |
| `wallClimbDownSpeed` | `float` | `4f` | Climb down speed / 壁下り速度 |
| `climbOverEdgeJumpHeight` | `float` | `1f` | Edge vault height / 壁上端越えの高さ |
| `climbOverHorizontalVelocity` | `float` | `2f` | Edge vault horizontal speed / 壁上端越えの横速度 |

### Climbing (Ladders/Surfaces) / 登り（梯子・登れる面）
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `canClimb` | `bool` | `false` | Enable climbing / 登りを有効にする |
| `climbSpeedUp` | `float` | `2f` | Upward climb speed / 上方向の登り速度 |
| `climbSpeedDown` | `float` | `3f` | Downward climb speed / 下方向の登り速度 |
| `climbSpeedSide` | `float` | `2f` | Sideways climb speed / 横方向の登り速度 |
| `climbAcceleration` | `float` | `20f` | Climbing acceleration / 登り加速度 |
| `climbBreakingForce` | `float` | `50f` | Climbing deceleration / 登り減速力 |
| `climbCollisionMask` | `LayerMask` | `~0` | Collision layers while climbing / 登り中の衝突レイヤー |

### Dash / ダッシュ
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `canDash` | `bool` | `false` | Enable dash / ダッシュを有効にする |
| `dashSpeedSide` | `float` | `20f` | Horizontal dash speed / 横ダッシュ速度 |
| `dashSpeedUp` | `float` | `20f` | Upward dash speed / 上ダッシュ速度 |
| `dashSpeedDown` | `float` | `0f` | Downward dash speed / 下ダッシュ速度 |
| `dashTime` | `float` | `0.2f` | Dash duration (seconds) / ダッシュ持続時間（秒） |
| `dashSpeedCurve` | `AnimationCurve` | Linear(0,1→1,1) | Speed over dash lifetime / ダッシュ中の速度カーブ |
| `minDashSpeed` | `float` | `0f` | Cancel dash below this speed / この速度以下でダッシュ中止 |
| `dashInputFreezeTime` | `float` | `0.2f` | Ignore input after dash start / ダッシュ後の入力無視時間 |
| `dashCoolDownTime` | `float` | `2f` | Cooldown between dashes / ダッシュのクールダウン |
| `limitDashAngle` | `bool` | `true` | Snap to 8 directions / 8方向にスナップする |
| `dashGravityRatio` | `float` | `0f` | Gravity during dash / ダッシュ中の重力比率 |

### Other / その他
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `deathAnimationTimeoutSeconds` | `float` | `2f` | Delay before Destroy after Kill() / Kill()後の破棄までの遅延 |

---

## Timers / タイマー

| Timer | Purpose | Duration Source |
|-------|---------|----------------|
| `dashTimer` | Dash duration / ダッシュ持続時間 | `dashTime` |
| `dashCoolDownTimer` | Dash cooldown / ダッシュクールダウン | `dashCoolDownTime` |
| `wallSlideWaitTimer` | Delay before wall slide gravity kicks in / 壁滑り重力開始までの遅延 | (external) |
| `wallGrabTimer` | Wall grab time limit / 壁掴みの時間制限 | `maxWallGrabTime` |

All are `PuzzleBox.Utils.Timer` with properties: `timeLeft`, `totalTime`, `phase` (0→1), `isFinished`, `active`.

---

## Public Properties / パブリックプロパティ

| Property | Type | Access | Description |
|----------|------|--------|------------|
| `state` | `State` | get | Current state / 現在のステート |
| `isRunning` | `bool` | get | Run input held / 走り入力中 |
| `isJumping` | `bool` | get | Currently in jump ascent / ジャンプ上昇中 |
| `isGrabbing` | `bool` | get | Grab input held / 掴み入力中 |
| `isDashing` | `bool` | get | Dash timer active / ダッシュタイマー稼働中 |
| `isTouchingWall` | `bool` | get | Wall detected via raycast / レイキャストで壁検出 |
| `wallDirection` | `float` | get | -1 (left wall) or 1 (right wall) / 壁の方向 |
| `motionInput` | `Vector2` | get | Processed movement input / 処理済み移動入力 |
| `facingDirection` | `Vector2` | get | Current facing / 現在の向き |
| `dashDirection` | `Vector2` | get | Dash direction / ダッシュ方向 |
| `inputDirection` | `Vector2` | get | motionInput if significant, else facingDirection / 入力があればその方向、なければ向き |

Also inherits from KinematicMotion2D: `isGrounded`, `velocity`, `position`, `groundNormal`, `groundRight`, `timeInAir`, `bounds`, etc.

---

## Public Methods / パブリックメソッド

```csharp
void Move(Vector2 input)
// Set movement input. Cancels active dash.
// 移動入力を設定。ダッシュをキャンセルする。

void Run(bool state)
// Toggle run mode.
// 走りモードの切り替え。

void Jump(bool jumpState, bool bufferInput = true)
// jumpState=true: initiate jump. jumpState=false: release (variable height).
// true:ジャンプ開始、false:ボタン離す（可変高さ制御）。

void Dash()
// Initiate dash in input direction (or facing direction).
// 入力方向（または向き方向）にダッシュ開始。

void GrabWall(bool grabbing)
// Toggle wall grab.
// 壁掴みの切り替え。

void StopClimbing()
// Force exit Climbing state → Falling.
// 登りステートを強制解除→落下。

void Kill()
// Start death sequence: disable input → OnDied → wait → DestroySelf.
// 死亡シーケンス開始：入力無効→OnDied→待機→破棄。

void DestroySelf()
// Immediate destroy: OnDestroyed → Destroy(gameObject).
// 即座に破棄：OnDestroyed→ゲームオブジェクト破棄。
```

---

## Events & Messages / イベントとメッセージ

### C# Events (subscribe in code) / C#イベント（コードで購読）
```csharp
Action OnJumped;       // Fired when jump executes / ジャンプ実行時
Action OnLanded;       // Fired when landing on ground / 着地時
Action OnDied;         // Fired when Kill() called / Kill()呼び出し時
Action OnDestroyed;    // Fired just before Destroy / 破棄直前
event Action<bool> OnInputEnabledChanged; // When input is toggled / 入力の有効・無効変更時
```

### SendMessage (received by sibling components) / SendMessage（同オブジェクトのコンポーネントが受信）
| Message | When |
|---------|------|
| `DidJump` | Jump executed / ジャンプ実行 |
| `DidDash` | Dash started / ダッシュ開始 |
| `DidLand` | Landed on ground / 着地 |
| `WasKilled` | Kill() called / Kill()呼び出し |
| `WasDestroyed` | About to be destroyed / 破棄直前 |

### Contact Messages (from KinematicMotion2D) / 接触メッセージ
| Message | Parameter | When |
|---------|-----------|------|
| `OnContactEnter` | `Contact` | New collision / 新しい接触 |
| `OnContactExit` | `Contact` | Collision ended / 接触終了 |
| `OnContactStay` | `Contact` | Ongoing collision / 接触継続 |

---

## Input Actions / 入力アクション

The player receives input via Unity's Input System `SendMessage` callbacks:
| Callback | Input Type | Maps To |
|----------|-----------|---------|
| `OnMove(object)` | `Vector2` | `Move()` |
| `OnRun(object)` | `bool` (press) | `Run()` |
| `OnJump(object)` | `bool` (press/release) | `Jump()` |
| `OnDash(object)` | `bool` (press) | `Dash()` |
| `OnGrabWall(object)` | `bool` (press) | `GrabWall()` |
