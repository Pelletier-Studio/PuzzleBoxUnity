# KinematicMotion2D — API Reference

Base class for scripted 2D kinematic physics: gravity, collide-and-slide, moving platforms, pushing,
attachments and contact events.

- **Source**: `Assets/PuzzleBox/Scripts/Runtime/KinematicMotion2D.cs` (~1800 lines). Read this document
  instead; open the source only for something not covered here.
- **Namespace**: `PuzzleBox`. **Inherits**: `MonoBehaviour`. **Requires**: `Rigidbody2D`.
- **Subclasses**: `PlatformerPlayer2D`. To change player behaviour, prefer `PlatformerPlayerExtension`
  hooks (see `PlatformerPlayer2D.API.md`) over subclassing.
- **Specification**: the PlayMode tests in `Tests/Runtime/` (see [Tests](#tests)). Each suite's class
  summary lists the rules it enforces. If this document and the code disagree, the code and tests win;
  fix this document.

---

## Core concepts

- **Kinematic, self-simulated.** `Start` sets the `Rigidbody2D` to Kinematic (with
  `useFullKinematicContacts`). Every `FixedUpdate` the body applies gravity to `velocity`, then moves by
  `velocity * dt` with its own casts. Box2D never moves it; dynamic bodies treat it as immovable.
- **Margin.** The body always keeps a gap of `margin` (default 0.02) between its colliders and
  everything else. "In contact" means within `margin`. Casts, ground checks, contact events and overlap
  correction are all measured from this gap.
- **Collide-and-slide.** A move that hits something stops at the margin, then the velocity is changed by
  the surface rules below and what is left of the move continues along the new velocity, up to
  `maxIterations` times.
- **Surface classes.** With normal `n` and "up" opposite to gravity: *ground* if `angle(n, up) <
  maxGroundAngleDegrees`, *ceiling* if `angle(n, down) < maxCeilingAngleDegrees`, otherwise *wall*.
- **Gravity must point straight down** (`Physics2D.gravity.x == 0`, `y <= 0`); `Start` logs an error
  otherwise. A negative `gravityModifier` reverses gravity for this body, and ground checks and the
  ground/ceiling classification flip with it.

---

## Inspector fields

| Field | Type | Default | Meaning |
|---|---|---|---|
| `simulatePhysics` | `bool` | `true` | `false` skips `FixedUpdate` entirely (no gravity, no movement, no ground or contact updates). `MoveBy` still works. |
| `mass` | `float` | `1` | Used in push ratios. Pushing uses `combinedMass` (this body plus everything attached below it). |
| `useGravity` | `bool` | `true` | Applies gravity. **Also gates ground detection**: with `false`, `CheckForGround` always fails, so the body is never grounded and never rides a platform. To float a body that should still stand on things, set `gravityMultiplier = 0` instead. |
| `gravityModifier` | `float` | `1` | Persistent gravity strength for this body. Negative reverses gravity. |
| `maxGroundAngleDegrees` | `float` | `45` | Steepest slope that counts as ground (strict `<`). |
| `maxCeilingAngleDegrees` | `float` | `45` | Steepest overhang that counts as ceiling (strict `<`). |
| `collisionMask` | `LayerMask` | `~0` | Layers collided with. Read through `GetCollisionMask()`. Triggers are always ignored. |
| `margin` | `float` | `0.02` | Gap kept from everything (min 0.005). |
| `groundCheckDistance` | `float` | `0.01` | How far beyond `margin` the ground check reaches. |
| `pushable` | `bool` | `false` | Whether other bodies may push this one. |
| `pushPriority` | `int` | `0` | A pusher must have a **strictly higher** priority than the body it pushes. |
| `minSlideDistance` | `float` | `0` | Moves shorter than this are dropped. |
| `stickyGroundThreshold` | `float` | `0.08` | While grounded, a velocity within this much (`abs(dot(groundNormal, v̂))`) of parallel to the ground is snapped exactly parallel to it, keeping its magnitude. |
| `maxIterations` | `int` | `2` | Re-slides allowed after collisions in one move. Pushing only happens while iterations remain. |
| `maxSpeedUp` / `maxSpeedDown` / `maxSpeedSide` | `float` | `100` | Velocity clamps, applied after gravity each step. |
| `useGroundMotion` | `bool` | `true` | Ride `KinematicMotion2D` platforms this body stands on. |
| `sticky` | `bool` | `true` | As a **platform**: when moving down, carry riders down however fast. `false`: riders are not carried downward (they fall under gravity), and hand over only horizontal velocity when it drops away from them. |
| `friction` | `float` | `0` | Friction with the ground this body stands on (or, as ground, with what stands on it). While grounded, slows `velocity.x` (relative to the ground) toward 0 at `sqrt(friction * groundFriction) * Physics2D.gravity.magnitude`, where `groundFriction` is the ground's `friction`, or `defaultGroundFriction` on static ground. Needs friction on both sides. Ground only. |
| `defaultGroundFriction` | `float` | `0` | Friction used for static ground (no `KinematicMotion2D`) when this body stands on it. |

## Script fields (hidden in the Inspector)

| Field | Type | Meaning |
|---|---|---|
| `velocity` | `Vector2` | Read/write. While grounded on a `KinematicMotion2D`, it is **relative to that platform**; world velocity is `velocity + groundVelocity`. While attached, it mirrors the root's velocity (see [Attachments](#attachments)). |
| `gravityMultiplier` | `float` | Temporary gravity scale for scripts (e.g. variable jump height). Never reset automatically. Effective gravity is `Physics2D.gravity * gravityMultiplier * gravityModifier`. |
| `lastGroundVelocity` | `Vector2` | World velocity (`velocity + groundVelocity`) at the last grounded step. |

## Properties

| Property | Type | Meaning |
|---|---|---|
| `isGrounded` | `bool` | Standing on ground. `false` on a step where velocity points away from the ground (`dot(groundNormal, v̂) > 0.25`), so a jump or launch leaves immediately. |
| `justLanded` / `justFell` | `bool` | Ground state changed this step. |
| `timeInAir` | `float` | Seconds since last grounded (0 while grounded). |
| `groundNormal` | `Vector2` | Normal of the ground beneath; `Vector2.up` when airborne. |
| `groundRight` | `Vector2` | Unit direction "right" along the ground. |
| `groundVelocity` | `Vector2` | `velocity` of the platform being ridden, or zero. |
| `position` | `Vector2` | The `Rigidbody2D` position. **Setting it teleports**: no collision check, riders are not carried, attachments move with it. |
| `rigidbody` | `Rigidbody2D` | Cached `Rigidbody2D`. |
| `bounds` | `Bounds` | Same as `GetBounds()`. |
| `attachedTo` | `KinematicMotion2D` | **Root** of the attachment chain, or `null` if not attached. |
| `attachments` | `IReadOnlyList<KinematicMotion2D>` | Bodies attached directly to this one. |
| `combinedMass` | `float` | `mass` plus the `combinedMass` of every attachment. |
| `maximumContactOffset` | `static float` | `0.005`. Box2D reports cast contacts this much short (half of its polygon radius); the class corrects for it. |

## Public methods

```csharp
// Move by delta with full collide-and-slide and pushing; velocity is not changed. Riders are carried.
// Call it on the root of an attachment chain: on an attached body it moves that body alone.
void MoveBy(Vector2 delta)

// Would moving `distance` along unit `direction` hit something? hit.distance is how far the body can
// travel while keeping its margin. Ignores: triggers, layers outside the mask, one-way platforms being
// passed through, its own attachment group, bodies riding it (and what they hold), and the platform
// currently carrying it. Casts with this body's colliders plus its attachments'.
bool Cast(Vector2 direction, float distance, out RaycastHit2D hit)

// Cast this body's OWN colliders (not attachments) up to `distance + margin` and return the first hit
// with a ground normal; hit.distance is measured from the margin. Skips the attachment group.
// Always false when useGravity is false.
bool CheckForGround(Vector2 direction, float distance, out RaycastHit2D hit)

// Recompute isGrounded and ground state now. Has side effects: may snap the body down onto the
// ground, change the platform being ridden, and call Landed/Fell.
void UpdateGroundedState()

bool IsGroundNormal(Vector2 normal)
bool IsWallNormal(Vector2 normal)    // neither ground nor ceiling
bool IsCeilingNormal(Vector2 normal)

// Combined bounds of all non-trigger colliders of this body, its children and its attachments.
Bounds GetBounds(bool updateColliders = false)

// Add to velocity. On an attached body, the impulse goes to the root.
void AddImpulse(Vector2 impulse)

void AttachTo(KinematicMotion2D parent)  // null detaches; see Attachments
void Detach()
```

---

## FixedUpdate order

1. Record `updateTime`. Return if `!simulatePhysics`.
2. **Attached:** copy the parent's `velocity` and return. The root does everything else.
3. `UpdateGroundedState()`: ground check (`margin + groundCheckDistance` along gravity); snap down to
   the ground if it is below the margin; select the platform to ride; take-off check; `justLanded` /
   `justFell`; `Landed(velocity.y)` / `Fell()`.
4. `timeInAir` += dt, or reset to 0 when grounded.
5. If grounded: `stickyGroundThreshold` alignment.
6. If `useGravity` and not grounded: `velocity += Physics2D.gravity * dt * gravityMultiplier * gravityModifier`.
   If grounded: friction.
7. Clamp `velocity.x` to `±maxSpeedSide`, `velocity.y` to `[-maxSpeedDown, maxSpeedUp]`.
8. If grounded: project velocity onto the ground, keeping `velocity.x`.
9. `velocity = Slide(velocity * dt, velocity)`: one collide-and-slide move; the returned velocity
   reflects the collision rules (it is not recomputed from displacement).
10. `ProcessOverlaps()`: resolve overlaps left by other bodies moving into this one.
11. If grounded: `lastGroundVelocity = velocity + groundVelocity`.
12. Contact events for this body, then for every attached body below it.
13. Invoke the internal `UpdateCompleted` callback (used to hand over platform velocity on leaving).

---

## Collide-and-slide

On each hit the body moves up to the margin, `ProcessCollision` is called, a push is attempted (see
[Pushing](#pushing)), and otherwise the velocity the move **started with** is changed by the surface hit:

| Hit | New velocity |
|---|---|
| Ground or ceiling | Keep `vx`, redirected along the surface. |
| Wall, airborne | Keep `vy`, redirected along the wall. (`vx` may reverse on overhanging walls.) |
| Wall, grounded | Zero. |
| Wedged (hit again at zero distance after a slide) | Zero. |

The rest of the move continues at the new velocity for the remaining time. A new velocity with no
component left to keep (e.g. `vx == 0` on a ground hit) stops the move with zero velocity.

**Overlap correction** (`ProcessOverlaps`, after each move): probes 8 directions for penetration into
the margin, takes the deepest, and moves out of it in one go (ground: straight up, ceiling: straight
down, wall: horizontally), then applies the table above. It ignores contacts exactly at the margin, the
body's own attachment group, and bodies riding it. Box2D rests dynamic bodies closer than `margin`, so
a dynamic body leaning on a `KinematicMotion2D` can make it yield by up to `margin`.

**One-way platforms** (`PlatformEffector2D` with `useOneWay`): `Cast` lets the body through when moving
within the effector's surface arc in its "through" direction. Ground checks still stand on them.

---

## Moving ground

A body grounded on a collider whose GameObject (or a parent) has a `KinematicMotion2D` rides it, if
`useGroundMotion`. If that body is attached to something, the **root** of its chain is ridden.

- **Carry.** Whenever the platform moves (its own slide, `MoveBy`, overlap correction, being pushed),
  it first notifies riders, which move by the same delta. Setting `position` does not notify. Results
  are the same whichever of rider and platform updates first.
- **The carry is collision-checked** against everything except the platform (which has not moved yet)
  and can push pushable bodies. If something blocks it, the rest is redirected along the obstacle,
  keeping its component along the platform normal: measured from the platform, the rider slides along
  its surface and never sinks into it or floats above it. An obstacle parallel to the platform stops it.
- **A platform ignores its riders**, and whatever they hold, in `Cast` and `ProcessOverlaps`, and never
  pushes them. Riders resolve their own overlaps.
- **Non-sticky platforms** do not carry riders downward; they fall under gravity.
- **A body attached to its platform** is moved by the attachment only, never carried a second time.
- **Landing** on a `KinematicMotion2D` keeps the body's horizontal velocity relative to it:
  `velocity.x` becomes world `vx` minus the platform's. A body landing back after jumping off lands on
  the spot it left; a body falling straight down lands moving against the platform, and without
  friction the platform slides away under it. If the platform already moved earlier in this step, the
  body catches up with that horizontal motion on landing.
- **Friction** (see the `friction` field) then slows the relative velocity until the body rests on the
  platform and rides along.
- **Leaving** (walking off, jumping, or the platform moving away): at the end of the step the platform's
  velocity is added to the body's, so world momentum is kept. A non-sticky platform moving down hands
  over only its horizontal velocity. After that the body moves by its own velocity only.
- **Dynamic bodies** are stood on like static ground: no carry, no pressing down, no dragging along.

---

## Pushing

When a move hits another body while iterations remain:

- **Another `KinematicMotion2D`.** The body hit is replaced by the **root of its attachment chain**. It is
  pushed if `CanPush(root, delta)`; the default requires `root.pushable`, `this.pushPriority >
  root.pushPriority`, and that the root is not standing on this body. Whether the pusher is pushable
  does not matter.
- **A dynamic `Rigidbody2D`.** Always pushed. Its free space is found by casting its collider, and its
  position is written directly (followed by `Physics2D.SyncTransforms`).
- **Ratio.** Both move `remaining * combinedMass / (combinedMass + otherMass)` and stay in contact. A
  pushed `KinematicMotion2D` slides by that amount with its own collisions (so it can push others, and
  carries its riders and attachments).
- **Velocities.** The pusher keeps its velocity; only its displacement shrinks. The pushed body gets no
  velocity.
- **Blocked.** If the pushed body cannot move, the pusher meets it like a wall.
- Bodies held by the pusher add to its `combinedMass`, and a held body touching something pushes it or
  is blocked by it as if the holder had touched it.

---

## Attachments

`child.AttachTo(parent)` makes `child` part of `parent`'s rigid group, like a held item. A body has at
most one parent and any number of attachments.

- **Graph.** `AttachTo` re-parents an attached body (its own attachments come along). `AttachTo(null)`
  and `Detach()` unlink. Attaching to itself or to its own descendant logs a warning and does nothing.
  Destroying a body detaches it from its parent and detaches its children.
- **Only the root simulates.** An attached body skips its `FixedUpdate` (no gravity, ground state or
  movement of its own) and keeps its offset from its parent: everything that moves the root moves the
  group by the same delta.
- **Velocity.** An attached body copies its parent's `velocity` in its own `FixedUpdate`. On `Detach` it
  keeps that velocity and simulates again.
- **One body for collisions.** The root's `Cast` uses the colliders of the whole group, so any member
  can stop it. Members never collide with, stand on, push apart, or raise contact events against each
  other, whatever their overlap.
- **Ground detection uses only the root's own colliders.** A body is never grounded on its own
  attachments, and an item held below a body does not make it grounded.
- **Others' view.** A body standing on an attached member rides the root. Pushing any member pushes the
  whole group, with the root's `pushable`/`pushPriority` and the group's `combinedMass`.
- **Colliders.** The group's collider lists are rebuilt on attach and detach. After adding or removing
  colliders at runtime, call `UpdateColliders()`.

---

## Contact events

After each move the body casts its own and its attachments' colliders a distance of `margin` along the
four axes and reports what it finds, via `SendMessage` on its own GameObject:

```csharp
void OnContactEnter(KinematicMotion2D.Contact contact)  // first step within margin
void OnContactStay(KinematicMotion2D.Contact contact)   // every following step (not the Enter step)
void OnContactExit(KinematicMotion2D.Contact contact)   // the step it leaves
```

- A contact is identified by `(rigidbody, collider, direction)`: one per other collider and probe axis.
- Triggers, colliders beyond the margin, and layers outside the collision mask raise nothing.
- A contact made by an attached body is reported by that body (`self` = it) **and** by the root
  (`self` = root). Members of a group never report each other.
- These are the only collision events. `OnCollision*2D` messages are not supported.
- Per step, a body records at most 16 contacts, and each cast keeps at most 8 hits.

```csharp
public struct Contact
{
    public GameObject self;          // The body reporting the contact
    public Rigidbody2D rigidbody;    // Other side's Rigidbody2D (null for static geometry)
    public Collider2D collider;      // Other side's collider
    public Vector2 point;
    public Vector2 normal;           // Normal of the touched surface
    public Vector2 direction;        // Probe axis that found it
    public Vector2 relativeVelocity; // velocity minus the other side's velocity (KinematicMotion2D or Rigidbody2D)
    public bool sliding;             // Never set; always false
}
```

---

## Extension points

Subclasses that override `Start` or `FixedUpdate` must call the base method.

```csharp
protected virtual void Awake()          // Empty
protected virtual void Start()          // Sets up the Rigidbody2D and collider lists; checks gravity
protected virtual void FixedUpdate()    // See "FixedUpdate order"
protected virtual void Update()         // Empty
protected virtual void OnDestroy()      // Detaches from parent and children
public void LateUpdate()                // Empty, NOT virtual

protected virtual void Landed(float speed)  // On landing; speed = velocity.y (see Caveats)
protected virtual void Fell()               // On leaving the ground
protected virtual bool CanPush(KinematicMotion2D other, Vector2 delta)  // `other` is an attachment root
protected virtual LayerMask GetCollisionMask()  // Default: collisionMask

// Called after the body has moved up to a hit, before the push or slide response. The return value
// replaces the distance still to travel; returning ~0 (<= 1e-5) ends the move with velocity unchanged.
protected virtual float ProcessCollision(RaycastHit2D hit, Vector2 direction, float distanceRemaining)

// Default implementations send OnContactEnter/Stay/Exit; call base to keep the messages.
protected virtual void ContactEnter(Contact contact)
protected virtual void ContactStay(Contact contact)
protected virtual void ContactExit(Contact contact)
```

Protected helpers and state:

```csharp
protected Vector2 Slide(Vector2 delta, Vector2 v, int iterations = 0) // Collide-and-slide move; returns velocity after collisions
protected void ProcessOverlaps()
protected int RigidbodyCast(Vector2 direction, ContactFilter2D filter, RaycastHit2D[] hits, float distance,
                            bool useAttachedColliders = true)       // Returns 0 before Start
protected void UpdateColliders()                                     // Rebuild collider lists (this body and its ancestors)
protected List<Collider2D> colliders                                 // Own colliders followed by the attachments'
protected bool IsAttachedTo(KinematicMotion2D other)                 // Is `other` an ancestor?
protected bool IsInSameAttachedSubtree(KinematicMotion2D other)      // Same root?
protected static bool IsOneWayPassThrough(Collider2D other, Vector2 direction, bool touching = false)
protected float GravityDirection                                     // -1 for normal gravity, +1 reversed
protected KinematicMotion2D groundMotion                             // Platform being ridden (a root), or null
protected Contact[] contacts, oldContacts; protected int contactCount, oldContactCount
protected Rigidbody2D rb
protected RaycastHit2D[] hits, colliderHits; protected ContactFilter2D contactFilter  // Shared scratch buffers
```

---

## Caveats

Observed in the code; not covered by tests unless stated.

- **Pushing a chain under-moves** (test `PushesAChain`, ignored): each push applies its own mass ratio,
  so with three equal bodies the ratios compound to 1/4 instead of 1/3.
- **`Landed(speed)`** receives `velocity.y` from the step after the landing hit, which has already been
  projected onto the ground (usually 0 on flat ground), not the impact speed.
- **Attached velocity can lag one step**: it is copied in the attached body's own `FixedUpdate`, so it
  is stale if that runs before the root's. An attached body with `simulatePhysics == false` never copies
  it.
- **`MoveBy` on an attached body** moves that body alone and breaks its offset from the group.

---

## Tests

PlayMode, assembly `PuzzleBox.Tests.Runtime`, in `Tests/Runtime/`. `CollideAndSlideFixture` is the shared
harness; `SteppedKinematicMotion2D` steps bodies one `FixedUpdate` at a time.

| Suite | Covers |
|---|---|
| `CollideAndSlideSingleSurfaceTests` | Surface rules against one surface, every orientation and direction |
| `CollideAndSlideMultiSurfaceTests` | Corners, wedging, sequences of surfaces |
| `GroundMotionTests` | Carrying, `sticky`, landing on and leaving platforms |
| `PushingTests` | Push rule, mass ratios, blocked pushes |
| `DynamicBodyTests` | Interaction with dynamic `Rigidbody2D` |
| `ContactEventTests` | `OnContactEnter/Stay/Exit` |
| `AttachmentTests` | Attachment API and rules (the other suites also have held-item variants) |
