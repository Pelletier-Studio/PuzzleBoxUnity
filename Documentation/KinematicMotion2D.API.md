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
static float maximumContactOffset { get; } // Returns 0.005f — overlap buffer used when generating
                                            // internal shadow colliders for native Unity collision
                                            // messages (see Collision Events below)
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

## Collision Events / 衝突イベント

KinematicMotion2D reports contacts through two independent, parallel systems. Code that reacts to
collisions should generally prefer the first one.
KinematicMotion2Dは接触を二つの独立した仕組みで通知します。新しく書くコードは基本的に一つ目を使ってください。

### 1. Contact / OnContactEnter, OnContactStay, OnContactExit (recommended / 推奨)

The class's own contact system. Driven entirely by its own raycasts every `FixedUpdate` (not by
Unity's physics engine), and dispatched via `SendMessage`:
このクラス独自の接触判定システムです。Unity物理エンジンではなく、毎`FixedUpdate`で自前のレイキャストにより
判定し、`SendMessage`で通知されます。

```csharp
void OnContactEnter(KinematicMotion2D.Contact contact)
void OnContactStay(KinematicMotion2D.Contact contact)
void OnContactExit(KinematicMotion2D.Contact contact)
```

This works identically and reliably regardless of what's on the other side of the contact — static
geometry (e.g. Tilemap), a Dynamic Rigidbody2D, or another KinematicMotion2D. **Use this API for all
new gameplay code**, including Kinematic-vs-Kinematic cases (see limitation below).
相手が静的なジオメトリ（Tilemap等）、Dynamic Rigidbody2D、他のKinematicMotion2Dのいずれであっても同じように
動作します。**新しく書くゲームプレイコードはこちらを使ってください。**（Kinematic同士の場合を含みます。
下記の制限を参照。）

### 2. Native Unity messages / Unity純正メッセージ

KinematicMotion2D also makes a best-effort attempt to trigger Unity's own `OnCollisionEnter2D`,
`OnCollisionStay2D` and `OnCollisionExit2D`, since code written against a plain Rigidbody2D commonly
expects these. Support depends on what's on the other side of the contact:
`OnCollisionEnter2D`、`OnCollisionStay2D`、`OnCollisionExit2D`といったUnity純正のメッセージも、通常の
Rigidbody2Dを前提に書かれたコードのために、できる限り発行されるようにしています。ただし、相手によって
対応状況が異なります。

| Other side / 相手 | Native messages / 純正メッセージ | Notes / 備考 |
|---|---|---|
| Static geometry, no Rigidbody2D (e.g. Tilemap) / 静的ジオメトリ | ✅ Supported / 対応 | via internal shadow collider |
| Dynamic Rigidbody2D | ✅ Supported / 対応 | Box2D always generates contacts against Dynamic bodies |
| Another KinematicMotion2D (Kinematic-vs-Kinematic) / 他のKinematicMotion2D | ❌ Not supported / 非対応 | use `OnContactEnter`/`OnContactStay`/`OnContactExit` instead |

**Kinematic-vs-Kinematic is intentionally unsupported for native messages.** It depends on
`Rigidbody2D.useFullKinematicContacts`, which has a long history of being unreliable across Unity
versions even when correctly configured on both bodies. Since true Kinematic-vs-Kinematic gameplay
collisions are rare (moving platforms are typically implemented via `groundMotion` following, not
collision response), this case is left unsupported rather than worked around.
**Kinematic同士の組み合わせは、意図的に純正メッセージの対象外としています。** これは
`Rigidbody2D.useFullKinematicContacts`に依存しますが、両方のRigidbody2Dで正しく設定していても、
Unityのバージョンによって信頼できない挙動をすることが知られています。Kinematic同士が実際に衝突判定を
必要とする場面は稀（動く足場は衝突ではなく`groundMotion`の追従で実装するのが一般的）なため、この
組み合わせは無理に対応せず、非対応として扱っています。

#### How native messages are made to work / 内部の仕組み

- All movement/collision resolution (`Cast`, `Slide`, ground detection) deliberately keeps a `margin`
  gap (default `0.02`) between colliders and surrounding geometry. This is required to avoid catching
  on the internal seams between adjacent Tilemap tile colliders — closing this gap reintroduces that
  problem.
  移動・衝突判定（`Cast`、`Slide`、接地判定）は、常に`margin`分の隙間（既定値`0.02`）を意図的に保ちます。
  これはタイルマップの隣接タイルの継ぎ目に引っかかる問題を避けるためで、この隙間を詰めると同じ問題が
  再発します。
- Because that gap keeps colliders from ever truly touching, each of the object's own colliders gets
  an additional, internal "shadow collider" — a slightly larger copy (inflated by `margin +
  maximumContactOffset`, via `edgeRadius` or a polygon offset) that exists solely to give Box2D a
  genuine overlap to detect as "touching". It is excluded from every one of the class's own movement
  and overlap queries (`Cast`, `Slide`, `ProcessOverlaps`, `CheckForGround`, `RigidbodyCast`,
  `RigidbodyOverlap`), so it never affects collision *resolution* — only native message generation.
  この隙間のせいでコライダ同士が実際に触れ合うことがないため、各コライダには内部的な「影コライダ」が
  追加されます。これは元のコライダよりわずかに大きい複製（`edgeRadius`または多角形オフセットにより
  `margin + maximumContactOffset`分膨らませたもの）で、Box2Dに実際の重なりを検出させ「接触」と判定させる
  ためだけに存在します。このクラス自身の移動・重なり判定（`Cast`、`Slide`、`ProcessOverlaps`、
  `CheckForGround`、`RigidbodyCast`、`RigidbodyOverlap`）には一切使われないため、衝突の解決には影響せず、
  純正メッセージの発行にのみ関わります。
- The Rigidbody2D never sleeps (`sleepMode = RigidbodySleepMode2D.NeverSleep`). A resting kinematic
  body that stops moving would otherwise fall asleep after `Time To Sleep` seconds (Project Settings),
  and Box2D stops re-evaluating "still touching" contacts for sleeping bodies — which would silently
  stop `OnCollisionStay2D` shortly after landing.
  Rigidbody2Dはスリープしません（`sleepMode = RigidbodySleepMode2D.NeverSleep`）。そうしないと、静止した
  キネマティックボディはProject Settingsの`Time To Sleep`秒後にスリープしてしまい、Box2Dはスリープ中の
  ボディに対して接触の継続判定を行わなくなるため、着地後しばらくすると`OnCollisionStay2D`が発行されなく
  なってしまいます。
- Net effect: the object's physically-solid footprint (as seen by Unity's physics engine and other
  Rigidbody2D) is very slightly larger — by `margin + maximumContactOffset` (~`0.025` units by
  default) — than its logical/visual footprint used for movement. This is usually imperceptible, but
  matters if you need pixel-precise contact behavior against this object.
  結果として、このオブジェクトの物理的に「実体のある」大きさ（Unity物理エンジンや他のRigidbody2Dから
  見た大きさ）は、移動に使われる論理的・見た目上の大きさよりわずかに大きくなります（既定値で
  `margin + maximumContactOffset` ≒ `0.025`単位）。通常は気にならない程度ですが、このオブジェクトに
  対してピクセル単位の正確な接触判定が必要な場合は注意してください。

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
