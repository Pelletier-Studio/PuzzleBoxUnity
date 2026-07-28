# KinematicMotion2D — API Reference / APIリファレンス

Base class for manual 2D kinematic physics: gravity, collision, sliding, pushing, moving platforms.
2Dキネマティック物理の基底クラス：重力、衝突判定、スライド移動、押し出し、動く地面への追従。

**Source**: `Assets/PuzzleBox/Scripts/Runtime/KinematicMotion2D.cs` (do not read directly / 直接読まないでください)
**Namespace**: `PuzzleBox`
**Inherits**: `MonoBehaviour`
**Requires**: `Rigidbody2D` (auto-set to Kinematic)

---

## Public Fields / パブリックフィールド

### General / 全般
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `simulatePhysics` | `bool` | `true` | Enable/disable physics simulation / 物理演算の有効・無効 |
| `mass` | `float` | `1f` | Mass for push calculations / 押し出し計算用の質量 |

### Gravity / 重力
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `useGravity` | `bool` | `true` | Apply gravity / 重力の影響を受けるか |
| `gravityMultiplier` | `float` | `1f` | Gravity strength ratio / 重力の強さの倍率 |
| `gravityModifier` | `float` | (hidden) | Per-frame multiplier, reset to 1 each FixedUpdate / 毎フレームリセットされる倍率 |

### Collision / 衝突判定
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `maxGroundAngleDegrees` | `float` | `45` | Max slope angle for ground / 地面と認識する最大傾斜角 |
| `maxCeilingAngleDegrees` | `float` | `45` | Max angle for ceiling / 天井と認識する最大角度 |
| `collisionMask` | `LayerMask` | `~0` (all) | Layers to collide with / 衝突するレイヤー |
| `pushable` | `bool` | `false` | Can be pushed by others / 他のオブジェクトに押されるか |
| `pushPriority` | `int` | `0` | Higher priority pushes lower / 高い値が低い値を押す |
| `minSlideDistance` | `float` | `0f` | Minimum movement threshold / 最小移動距離 |

### Speed Limits / 速度制限
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `maxSpeedUp` | `float` | `100f` | Max upward speed / 上方向の最大速度 |
| `maxSpeedDown` | `float` | `100f` | Max downward speed / 下方向の最大速度 |
| `maxSpeedSide` | `float` | `100f` | Max horizontal speed / 横方向の最大速度 |

### Motion / 移動
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `useGroundMotion` | `bool` | `true` | Follow moving ground / 動く地面に追従するか |
| `velocity` | `Vector2` | — | Current velocity (read/write) / 現在の速度（読み書き可） |
| `lastGroundVelocity` | `Vector2` | — | Velocity when last grounded / 最後に地面にいた時の速度 |

---

## Public Properties / パブリックプロパティ

| Property | Type | Access | Description |
|----------|------|--------|------------|
| `isGrounded` | `bool` | get | Standing on ground / 地面に立っているか |
| `justLanded` | `bool` | get | Landed this frame / 今フレーム着地したか |
| `justFell` | `bool` | get | Left ground this frame / 今フレーム地面を離れたか |
| `timeInAir` | `float` | get | Seconds since leaving ground / 地面を離れてからの秒数 |
| `groundNormal` | `Vector2` | get | Ground surface normal / 地面の法線 |
| `groundRight` | `Vector2` | get | Right direction along ground slope / 地面に沿った右方向 |
| `groundVelocity` | `Vector2` | get | Velocity of ground beneath / 足元の地面の速度 |
| `rigidbody` | `Rigidbody2D` | get | Cached Rigidbody2D / キャッシュされたRigidbody2D |
| `position` | `Vector2` | get/set | Rigidbody position / リジッドボディの位置 |
| `bounds` | `Bounds` | get | Combined collider bounds / コライダの合計バウンズ |

---

## Public Methods / パブリックメソッド

```csharp
Bounds GetBounds(bool updateColliders = false)
// Returns combined bounds of all non-trigger child colliders.
// 全ての非トリガー子コライダの合計バウンズを返す。

void MoveBy(Vector2 delta)
// Move with collision detection (calls Slide internally).
// 衝突判定付きの移動（内部でSlideを呼ぶ）。

bool Cast(Vector2 direction, float distance, out RaycastHit2D hit)
// Check if movement would collide. Returns true if collision found.
// 移動が衝突するかチェック。衝突があればtrueを返す。

bool CheckForGround(Vector2 direction, float distance, out RaycastHit2D hit)
// Raycast for ground surface. Returns true if ground found.
// 地面をレイキャストで検出。地面があればtrueを返す。

void UpdateGroundedState()
// Force re-evaluation of grounded state.
// 接地状態を強制的に再評価する。

bool IsGroundNormal(Vector2 normal)
bool IsWallNormal(Vector2 normal)
bool IsCeilingNormal(Vector2 normal)
// Classify a surface normal as ground, wall, or ceiling.
// 面の法線を地面・壁・天井に分類する。
```

---

## Static Members / 静的メンバー

```csharp
static float maximumContactOffset { get; } // Returns 0.005f — Box2D polygon radius correction
```

---

## Contact Struct / Contact構造体

```csharp
public struct Contact
{
    public GameObject self;          // The owning GameObject / 所有するGameObject
    public Rigidbody2D rigidbody;    // Other rigidbody / 相手のリジッドボディ
    public Collider2D collider;      // Other collider / 相手のコライダ
    public Vector2 point;            // Contact point / 接触点
    public Vector2 normal;           // Contact normal / 接触の法線
    public Vector2 direction;        // Cast direction that found this / 検出時のキャスト方向
    public Vector2 relativeVelocity; // Relative velocity / 相対速度
    public bool sliding;             // Whether sliding / スライド中か
}
```

---

## Virtual/Protected Extension Points / 仮想・保護メソッド（拡張ポイント）

These are available to subclasses. **For extensions, use `PlatformerPlayerExtension` hooks instead.**
サブクラスで使用可。**拡張には `PlatformerPlayerExtension` のフックを使ってください。**

```csharp
protected virtual void Awake()
protected virtual void Start()
protected virtual void Update()
protected virtual void FixedUpdate()        // Main physics loop (see execution order below)
protected virtual void Landed(float speed)  // Called on landing; speed = vertical velocity at impact
protected virtual void Fell()               // Called when leaving ground
protected virtual bool CanPush(KinematicMotion2D other, Vector2 delta)
protected virtual float ProcessCollision(RaycastHit2D hit, Vector2 direction, float distanceRemaining)
protected virtual LayerMask GetCollisionMask()
protected virtual void ContactEnter(Contact contact)
protected virtual void ContactExit(Contact contact)
protected virtual void ContactStay(Contact contact)

// Protected helpers:
protected void Slide(Vector2 delta, int iterations = 0)
protected int RigidbodyCast(Vector2 direction, ContactFilter2D filter, RaycastHit2D[] hits, float distance)
protected int RigidbodyOverlap(ContactFilter2D filter, Collider2D[] overlaps)
protected void ProcessOverlaps(int iterations = 0)
```

---

## FixedUpdate Execution Order / FixedUpdate実行順序

1. Early exit if `simulatePhysics == false`
2. `ProcessOverlaps()` — resolve collider overlaps / コライダの重なり解消
3. Reset `gravityModifier = 1f`
4. `UpdateGroundedState()` — detect ground contact / 接地状態を更新
5. Update `timeInAir` (increment if airborne, reset if grounded)
6. Apply gravity: `velocity += Physics2D.gravity * dt * gravityMultiplier * gravityModifier`
7. Clamp `velocity.x` to `±maxSpeedSide`
8. Clamp `velocity.y` to `[-maxSpeedDown, maxSpeedUp]`
9. Compute `motion = velocity * dt`
10. Decompose motion into ground-relative horizontal and vertical components
11. `Slide(horizontalMotion)` — horizontal movement with collision / 横移動（衝突判定付き）
12. `Slide(verticalMotion)` — vertical movement with collision / 縦移動（衝突判定付き）
13. Recompute `velocity` from actual displacement
14. Cache `lastGroundVelocity` if grounded
15. `UpdateContacts()` — fire ContactEnter/Stay/Exit events / 接触イベントを発行
