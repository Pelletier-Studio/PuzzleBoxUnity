# KinematicMotion2D — API Reference / APIリファレンス

Base class for manual 2D kinematic physics: gravity, collision, sliding, pushing, moving platforms.

**Source**: `Assets/PuzzleBox/Scripts/Runtime/KinematicMotion2D.cs` (do not read directly)
**Namespace**: `PuzzleBox`
**Inherits**: `MonoBehaviour`
**Requires**: `Rigidbody2D` (auto-set to Kinematic)

---

## Public Fields

### General
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `simulatePhysics` | `bool` | `true` | Enable/disable physics simulation |
| `mass` | `float` | `1f` | Mass for push calculations |

### Gravity
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `useGravity` | `bool` | `true` | Apply gravity |
| `gravityMultiplier` | `float` | `1f` | Gravity strength ratio |
| `gravityModifier` | `float` | (hidden) | Per-frame multiplier, reset to 1 each FixedUpdate |

### Collision / 衝突判定
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `maxGroundAngleDegrees` | `float` | `45` | Max slope angle for ground |
| `maxCeilingAngleDegrees` | `float` | `45` | Max angle for ceiling |
| `collisionMask` | `LayerMask` | `~0` (all) | Layers to collide with |
| `pushable` | `bool` | `false` | Can be pushed by others |
| `pushPriority` | `int` | `0` | Higher priority pushes lower |
| `minSlideDistance` | `float` | `0f` | Minimum movement threshold |

### Speed Limits / 速度制限
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `maxSpeedUp` | `float` | `100f` | Max upward speed |
| `maxSpeedDown` | `float` | `100f` | Max downward speed |
| `maxSpeedSide` | `float` | `100f` | Max horizontal speed |

### Motion / 移動
| Field | Type | Default | Description |
|-------|------|---------|------------|
| `useGroundMotion` | `bool` | `true` | Follow moving ground |
| `sticky` | `bool` | `true` | When acting **as ground**, drag riders down even when descending faster than free fall. Set `false` to let the surface drop away from whatever is standing on it |
| `velocity` | `Vector2` | — | Current velocity (read/write) |
| `lastGroundVelocity` | `Vector2` | — | Velocity when last grounded |

---

## Public Properties / パブリックプロパティ

| Property | Type | Access | Description |
|----------|------|--------|------------|
| `isGrounded` | `bool` | get | Standing on ground |
| `justLanded` | `bool` | get | Landed this frame |
| `justFell` | `bool` | get | Left ground this frame |
| `timeInAir` | `float` | get | Seconds since leaving ground |
| `groundNormal` | `Vector2` | get | Ground surface normal |
| `groundRight` | `Vector2` | get | Right direction along ground slope |
| `groundVelocity` | `Vector2` | get | Velocity of ground beneath |
| `rigidbody` | `Rigidbody2D` | get | Cached Rigidbody2D |
| `position` | `Vector2` | get/set | Rigidbody position |
| `bounds` | `Bounds` | get | Combined collider bounds |

---

## Public Methods / パブリックメソッド

```csharp

// Return the combined bounds of all non-trigger child colliders.
Bounds GetBounds(bool updateColliders = false)

// Move with collision detection (calls Slide internally).
void MoveBy(Vector2 delta)

// Check if movement would collide. Returns true if collision found.
bool Cast(Vector2 direction, float distance, out RaycastHit2D hit)

// Raycast for ground surface. Returns true if ground found.
bool CheckForGround(Vector2 direction, float distance, out RaycastHit2D hit)

// Force re-evaluation of grounded state.
void UpdateGroundedState()

// Classify a surface normal as ground, wall, or ceiling.
bool IsGroundNormal(Vector2 normal)
bool IsWallNormal(Vector2 normal)
bool IsCeilingNormal(Vector2 normal)

```

---

## Static Members / 静的メンバー

```csharp

// Returns 0.005f — overlap buffer used when generating
// internal shadow colliders for native Unity collision
// messages (see Collision Events below)
static float maximumContactOffset { get; }                                     

```

---

## Contact Struct / Contact構造体

```csharp
public struct Contact
{
    public GameObject self;          // The owning GameObject
    public Rigidbody2D rigidbody;    // Other rigidbody
    public Collider2D collider;      // Other collider
    public Vector2 point;            // Contact point
    public Vector2 normal;           // Contact normal
    public Vector2 direction;        // Cast direction that found this
    public Vector2 relativeVelocity; // Relative velocity
    public bool sliding;             // Whether sliding
}
```

---

## Collision Events / 衝突イベント

KinematicMotion2D reports contacts through two independent, parallel systems. Code that reacts to
collisions should generally prefer the first one.

### 1. Contact / OnContactEnter, OnContactStay, OnContactExit (recommended)

The class's own contact system. Driven entirely by its own raycasts every `FixedUpdate` (not by
Unity's physics engine), and dispatched via `SendMessage`:

```csharp
void OnContactEnter(KinematicMotion2D.Contact contact)
void OnContactStay(KinematicMotion2D.Contact contact)
void OnContactExit(KinematicMotion2D.Contact contact)
```

This works identically and reliably regardless of what's on the other side of the contact — static
geometry (e.g. Tilemap), a Dynamic Rigidbody2D, or another KinematicMotion2D. **Use this API for all
new gameplay code**, including Kinematic-vs-Kinematic cases (see limitation below).

### 1b. Crush / OnCrushedBy

```csharp
void OnCrushedBy(KinematicMotion2D.Contact contact)
```

Fired when the body is **crushed** — squeezed to the point where its overlap with something else
cannot be resolved in any direction. Concretely: after `ProcessOverlaps` has finished trying to
separate the body, a penetration deeper than `margin` still remains.

The crusher is identified through `contact.collider` and `contact.rigidbody`, **not** through a
`KinematicMotion2D` reference — a crusher may be another `KinematicMotion2D`, a plain kinematic
`Rigidbody2D` moved with `MovePosition`, or a dynamic `Rigidbody2D`. When several things overlap the
body at once, the deepest penetration is reported.

**Edge-triggered.** It fires once when the crush begins, not every frame it lasts (the same idea as
`justLanded`). Hold your own state if you need to keep reacting while it persists.

> **Note — crush vs. bad placement.** A body placed inside geometry it cannot escape looks
> geometrically identical to a crushed one. They are told apart by history: a crush is a *transition*
> out of a resolved state, whereas a badly-placed body never had one. A body that has never once
> been free of overlaps logs a **warning** (once, naming the blocking collider) instead of firing
> `OnCrushedBy`, because that is an authoring error rather than a gameplay event. Note that a body
> repositioned by code — `motion.position = ...` — into an inescapable spot **is** reported as a
> crush, since it was resolved earlier in its life. If you teleport bodies around, validate the
> destination.

### 2. Native Unity messages

KinematicMotion2D also makes a best-effort attempt to trigger Unity's own `OnCollisionEnter2D`,
`OnCollisionStay2D` and `OnCollisionExit2D`, since code written against a plain Rigidbody2D commonly
expects these. Support depends on what's on the other side of the contact:

| Other side | Native messages | Notes |
|---|---|---|
| Static geometry, no Rigidbody2D (e.g. Tilemap) | ✅ Supported | via internal shadow collider |
| Dynamic Rigidbody2D | ✅ Supported | Box2D always generates contacts against Dynamic bodies |
| Another KinematicMotion2D (Kinematic-vs-Kinematic) | ❌ Not supported | use `OnContactEnter`/`OnContactStay`/`OnContactExit` instead |

**Kinematic-vs-Kinematic is intentionally unsupported for native messages.** It depends on
`Rigidbody2D.useFullKinematicContacts`, which has a long history of being unreliable across Unity
versions even when correctly configured on both bodies. Since true Kinematic-vs-Kinematic gameplay
collisions are rare (moving platforms are typically implemented via `groundMotion` following, not
collision response), this case is left unsupported rather than worked around.

#### How native messages are made to work

- All movement/collision resolution (`Cast`, `Slide`, ground detection) deliberately keeps a `margin`
  gap (default `0.02`) between colliders and surrounding geometry. This is required to avoid catching
  on the internal seams between adjacent Tilemap tile colliders — closing this gap reintroduces that
  problem.
- Because that gap keeps colliders from ever truly touching, each of the object's own colliders gets
  an additional, internal "shadow collider" — a slightly larger copy (inflated by `margin +
  maximumContactOffset`, via `edgeRadius` or a polygon offset) that exists solely to give Box2D a
  genuine overlap to detect as "touching". It is excluded from every one of the class's own movement
  and overlap queries (`Cast`, `Slide`, `ProcessOverlaps`, `CheckForGround`, `RigidbodyCast`,
  `RigidbodyOverlap`), so it never affects collision *resolution* — only native message generation.
- The Rigidbody2D never sleeps (`sleepMode = RigidbodySleepMode2D.NeverSleep`). A resting kinematic
  body that stops moving would otherwise fall asleep after `Time To Sleep` seconds (Project Settings),
  and Box2D stops re-evaluating "still touching" contacts for sleeping bodies — which would silently
  stop `OnCollisionStay2D` shortly after landing.
- Net effect: the object's physically-solid footprint (as seen by Unity's physics engine and other
  Rigidbody2D) is very slightly larger — by `margin + maximumContactOffset` (~`0.025` units by
  default) — than its logical/visual footprint used for movement. This is usually imperceptible, but
  matters if you need pixel-precise contact behavior against this object.

---

## Virtual/Protected Extension Points / 仮想・保護メソッド（拡張ポイント）

These are available to subclasses. **For extensions, use `PlatformerPlayerExtension` hooks instead.**

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

## FixedUpdate Execution Order

1. Early exit if `simulatePhysics == false`
2. `ProcessOverlaps()` — resolve collider overlaps
3. Reset `gravityModifier = 1f`
4. `UpdateGroundedState()` — detect ground contact
5. Update `timeInAir` (increment if airborne, reset if grounded)
6. Apply gravity: `velocity += Physics2D.gravity * dt * gravityMultiplier * gravityModifier`
7. Clamp `velocity.x` to `±maxSpeedSide`
8. Clamp `velocity.y` to `[-maxSpeedDown, maxSpeedUp]`
9. Compute `motion = velocity * dt`
10. Decompose motion into ground-relative horizontal and vertical components
11. `Slide(horizontalMotion)` — horizontal movement with collision
12. `Slide(verticalMotion)` — vertical movement with collision
13. Recompute `velocity` from actual displacement
14. Cache `lastGroundVelocity` if grounded
15. `UpdateContacts()` — fire ContactEnter/Stay/Exit events

---

## Moving Ground (Carry) / 動く地面への追従

When a body is grounded on another `KinematicMotion2D`, it follows that platform's motion. This
happens through a private `WillMove` callback rather than in the body's own `FixedUpdate`.

**Order matters.** The platform notifies its riders *before* it applies its own position change, so
during the carry the platform is still at its old position. Two rules follow from that:

1. **The platform is excluded from the carry's collision queries.** Casting against geometry that
   has not moved yet is meaningless — it would block the carry against the platform's own stale
   surface. This is the symmetric counterpart of the existing rule by which a platform ignores its
   own riders. Only *third-party* geometry can stop a carry.

2. **The carry is decomposed in the platform's surface basis**, exactly as a body's own motion is
   in step 10 above: a tangential component along the surface, then a component along the surface
   normal, each slid separately. If an obstacle blocks the tangential part, the normal part still
   applies — which is what keeps the body on a surface that is moving out from under it.

**Specification.** When a body resting on a slanted moving platform meets an obstacle that impedes
the body but not the platform, the body is pushed in a direction orthogonal to the platform normal
— i.e. measured in the platform's frame it slides along the surface and never leaves it.


### `sticky`

The one deliberate exception. A platform descending faster than free fall would otherwise drop away
from its rider. `sticky == true` (the default) drags the rider down regardless of speed; with
`sticky == false` the vertical part of the carry is skipped and the body is left to fall under
gravity. This is a feature, not a workaround for the ordering above.

> Note: the carry is collision-checked in every direction, so a `sticky` platform descending into
> solid geometry leaves its rider on that geometry rather than dragging it through.