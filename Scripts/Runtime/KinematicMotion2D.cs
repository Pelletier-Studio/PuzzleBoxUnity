/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
 
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace PuzzleBox
{
    /**
     * This class manually controls movement and collision checks
     * for characters and moving objects.
     *
     * Unity has a component called "Rigidbody2D" that handles physics
     * automatically. But when you need precise movement like a platform
     * game character, automatic physics can easily cause issues such as
     * sinking into walls or unwanted sliding.
     *
     * This class controls physics by itself to avoid those issues.
     * It provides these features:
     *   ・Apply gravity (falling and landing)
     *   ・Check collisions with walls, ground, and ceilings, and push back
     *   ・Slide along slopes
     *   ・Follow moving ground (platforms, elevators, etc.)
     *   ・Push other kinematic objects
     *
     * To use this class, the same GameObject needs a Rigidbody2D component.
     * The Rigidbody2D type is set to "Kinematic" automatically.
     * Kinematic means it moves directly by script, not by automatic
     * physics engine simulation.
     */
    [RequireComponent(typeof(Rigidbody2D))]
    public class KinematicMotion2D : MonoBehaviour
    {
        public bool simulatePhysics = true;
        public float mass = 1f;

        [Header("Gravity")] // Show a header in the Inspector.
        public bool useGravity = true; // Should this be affected by gravity?

        
        public float gravityModifier = 1f; // Adjust the strength of gravity.

        [HideInInspector]
        public float gravityMultiplier = 1f; 


        [Header("Collision")]
        
        // Maximum ground slope angle. Slopes steeper than this are treated as walls.
        [Min(0)]
        public float maxGroundAngleDegrees = 45;

        // Maximum ceiling angle. Used like ground angle to tell ceilings from walls.
        [Min(0)]
        public float maxCeilingAngleDegrees = 45;

        // Use this to specify which layers this object should collide with.
        public LayerMask collisionMask = ~0; // "~0" means "everything" here.

        // To be sure that this object does not get stuck in other colliders,
        // we set a very small gap from other colliders. "margin" controls the size of this gap.
        [Min(0.005f)]
        public float margin = 0.005f;

        // To check if this object is standing on the ground, we cast a ray downward
        // to see if it collides with an object that can be considered ground.
        // This parameter controls how far below the object we check for the ground.
        [Min(0.001f)]
        protected float groundCheckDistance = 0.01f;

        // When another object collides with this one, we only move if "pushable" is true.
        public bool pushable = false;

        // If two "not pushable" objects (pushable=false), or two "pushable"
        // objects (pushable=true), collide while moving toward each other,
        // this value decides what happens.
        // The lower pushPriority side can be pushed away if pushable=true.
        // The higher pushPriority side always keeps moving to its target.
        // If both pushPriority values are the same, both stop at contact.
        public int pushPriority = 0;


        // Any movement below the value of "minSlideDistance" will be treated as no movement.
        [Min(0f)]
        public float minSlideDistance = 0f;

        // This is a more advanced parameter and usually does not need changing.
        // After a collision, movement direction can be changed and movement can continue.
        // (For example, when hitting a slope, it keeps moving up the slope instead of stopping.)
        // This value is the maximum number of times that process can repeat.
        int maxIterations = 2;


        [Header("Speed")]
        // Speed limits that cannot be exceeded.
        [Min(0)]
        public float maxSpeedUp = 100f;

        [Min(0)]
        public float maxSpeedDown = 100f;

        [Min(0)]
        public float maxSpeedSide = 100f;

        [Space]
        
        // When standing on another object that has a KinematicMotion2D component, should we follow its movement?
        public bool useGroundMotion = true;

        // When another object with a KinematicMotion2D component is standing on this object, what should happen
        // if we start moving downward very fast? If we move downward faster than free fall, and "sticky" is set to true,
        // that object will move with us and ignore gravity. If "sticky" is false, it will be left behind and fall as usual.
        // This is a useful setting for platforms like elevators.
        public bool sticky = true;

        // [HideInInspector] // Hide in the Inspector.
        public Vector2 velocity; // Movement speed. Usually changed by other components in code.

        [HideInInspector] // Hide in the Inspector.
        public Vector2 lastGroundVelocity; // Speed when this last touched the ground.

        // Whether this object is standing on the ground.
        public bool isGrounded { get; private set; }

        // This is true in the frame immediately after landing on the ground.
        public bool justLanded { get; private set; }

        // This is true in the frame immediately after leaving the ground.
        public bool justFell { get; private set; }

        // Time since leaving the ground (seconds).
        public float timeInAir { get; private set; }


        // You can "attach" this KinematicMotion2D to another KinematicMotion2D.
        // Attach means linking to another one, and the attached object fully follows
        // the motion of the "parent" object.
        // This is useful if you need to have one object carry another, like a character holding an item.
        // One object can have several objects attached to it, but an object can only be attached to one parent at a time.
        private KinematicMotion2D parent = null;
        private List<KinematicMotion2D> attachedMotions = new List<KinematicMotion2D>();

        // Attach this object to another KinematicMotion2D.
        // If parentMotion is null, this object will be detached from its current parent.
        public void AttachTo(KinematicMotion2D parentMotion)
        {
            if (parentMotion == null)
            {
                Detach();
                return;
            }

            if (parentMotion != parent)
            {
                Detach();
                parent = parentMotion;
                parent.attachedMotions.Add(this);
            }
        }

        // Detach this object from its current parent, if any.
        public void Detach()
        {
            if (parent != null)
            {
                parent.attachedMotions.Remove(this);
                parent = null;
            }
        }

        // Read-only view of the attachment graph. These do not change any behaviour;
        // they exist so attachment state can be inspected from outside this class
        // instead of being inferred from how things move.
        public KinematicMotion2D attachedTo => parent;
        public int attachedCount => attachedMotions.Count;
        public IReadOnlyList<KinematicMotion2D> attachments => attachedMotions;

        // Ground normal. (When not grounded, it points straight up.)
        public Vector2 groundNormal { get; private set; }

        // This returns the "right" direction relative to the ground normal.
        // In other words, this is the move direction for going right along the ground.
        public Vector2 groundRight
        {
            get
            {
                return new Vector2(groundNormal.y, -groundNormal.x);
            }
        }

        // How fast is the ground moving?
        public Vector2 groundVelocity
        {
            get
            {
                if (groundMotion != null)
                {
                    return groundMotion.velocity;
                }
                else
                {
                    return Vector2.zero;
                }
            }
        }

        float groundDistance = 0f;

        // The Rigidbody2D component attached to this GameObject.
        new public Rigidbody2D rigidbody
        {
            get
            {
                if (rb == null)
                {
                    rb = GetComponent<Rigidbody2D>();
                }

                return rb;
            }
        }

        // The position of this object, based on its Rigidbody2D component rather than its Transform.
        // (The two values are not necessarily the same!)
        public Vector2 position
        {
            get
            {
                return rigidbody.position;
            }

            set
            {
                rigidbody.position = value;
            }
        }

        // Get the combined bounds of all non-trigger colliders attached to this object and its children.
        // If updateColliders is true, it will refresh the list of colliders before calculating the bounds.
        public Bounds GetBounds(bool updateColliders = false)
        {
            if (updateColliders) {
                colliders = GetComponentsInChildren<Collider2D>();
            }
            Bounds totalBounds = new Bounds();
            bool init = false;
            foreach(Collider2D coll in colliders)
            {
                if (!coll.isTrigger)
                {
                    if (init)
                    {
                        totalBounds.Encapsulate(coll.bounds);
                    }
                    else
                    {
                        totalBounds = coll.bounds;
                        init = true;
                    }
                }
            }
            
            return totalBounds;
        }

        // This property returns the same value as GetBounds().
        public Bounds bounds
        {
            get
            {
                return GetBounds();
            }
        }

        // Can we push this other KinematicMotion2D object?
        // The delta parameter represents the intended movement of this object.
        protected virtual bool CanPush(KinematicMotion2D otherMotion, Vector2 delta)
        {
            if (otherMotion.groundMotion == this)
            {
                // If this is the ground, do not push the collision target.
                // The needed handling is done in GroundMoved.
                return false;
            }
            if (otherMotion.pushable == pushable)
            {
                return pushPriority > otherMotion.pushPriority;
            }
            else return otherMotion.pushable;
        }

        // The direction of gravity for this object, taking into account the global gravity and the gravity modifier.
        protected float GravityDirection
        {
            get
            {
                return Mathf.Sign(Physics2D.gravity.y) * Mathf.Sign(gravityModifier);
            }
        }


        // Struct representing a contact point during collision detection.
        public struct Contact
        {
            // The GameObject this contact belongs to.
            public GameObject self;
            public Rigidbody2D rigidbody;
            public Collider2D collider;
            public Vector2 point; // The point of contact.
            public Vector2 normal; // The normal vector at the contact point.
            public Vector2 direction; // The direction of the contact relative to this object.
            public Vector2 relativeVelocity; // The relative velocity at the contact point.
            public bool sliding; // Is the contact sliding?

            public bool Equals(Contact obj)
            {
                return obj.rigidbody == this.rigidbody && obj.collider == this.collider && obj.direction == this.direction;
            }

            public override bool Equals(System.Object obj)
            {
                return base.Equals(obj);
            }

            public override int GetHashCode()
            {
                return base.GetHashCode();
            }

            public static bool operator ==(Contact c1, Contact c2)
            {
                return c1.Equals(c2);
            }

            public static bool operator !=(Contact c1, Contact c2)
            {
                return !c1.Equals(c2);
            }
        }

        // Reference to components used by this script.
        protected Rigidbody2D rb;

        // Variables needed for collision checks.
        // They are declared outside methods for better performance.
        protected RaycastHit2D[] hits = new RaycastHit2D[8];
        protected RaycastHit2D[] colliderHits = new RaycastHit2D[8];
        protected Collider2D[] overlapColliders = new Collider2D[8];
        protected Collider2D[] overlaps = new Collider2D[8];
        protected ContactFilter2D contactFilter = new ContactFilter2D();

        protected Collider2D[] colliders;

        protected KinematicMotion2D groundMotion = null;

        // Ground normal used for following moving ground.
        // "groundNormal" above is intentionally not updated on the landing frame
        // (to prevent sliding on slopes; see UpdateGround). But following starts
        // immediately when landing, so follow logic needs the correct normal even
        // on that frame. So we store this separately.
        private Vector2 groundContactNormal = Vector2.up;

        // Target excluded from collision checks only while following.
        // When ground moves, it sends a "WillMove" notice before it actually moves.
        // So when follow logic runs, the ground is still at the old position.
        // Collision checks against that old position are not useful, so we ignore
        // the ground only during following.
        // (This mirrors the logic where ground ignores its riders. See Cast.)
        private KinematicMotion2D ignoredMotion = null;

        protected Vector2 positionAdjustment = Vector2.zero;

        protected virtual LayerMask GetCollisionMask()
        {
            return collisionMask;
        }


        protected virtual float ProcessCollision(RaycastHit2D hit, Vector2 direction, float distanceRemaining)
        {
            return distanceRemaining;
        }

        // Moves this object by the specified delta.
        public void MoveBy(Vector2 delta)
        {
            Slide(delta);
        }


        // This method performs the core KinematicMotion2D logic.
        // It moves the object up to the distance in the "delta" parameter.
        // If it collides on the way, it changes direction based on the hit surface,
        // and keeps moving along that surface. In other words, it "slides".
        // The "iteration" parameter is how many slide steps happened so far
        // in this single move.
        protected void Slide(Vector2 delta, int iterations = 0)
        {
            // First, get movement direction and distance.
            Vector2 direction = delta.normalized;
            float distance = delta.magnitude;

            // Ignore very small movement.
            if (distance < minSlideDistance || distance == 0f)
            {
                return;
            }

            // Use our custom method to check if moving this far will hit something.
            RaycastHit2D hit; // If a collision happens, details are stored here.
            bool collided = Cast(direction, distance, out hit); // Returns whether there was a collision.

            if (collided) // Collision happened...
            {
                float contactDistance = hit.distance; // Distance to collision point.

                // We can slide, so calculate remaining movement distance.
                float distanceRemaining = distance - contactDistance;

                // First, move as far as possible without colliding.
                MoveRigidbody(direction * contactDistance);

                distanceRemaining = ProcessCollision(hit, direction, distanceRemaining);

                if (distanceRemaining == 0)
                {
                    return;
                }

                // Do we still have remaining slide attempts?
                if (iterations < maxIterations)
                {
                    if (hit.rigidbody != null && hit.rigidbody.bodyType == RigidbodyType2D.Kinematic)
                    {
                        KinematicMotion2D otherMotion = hit.rigidbody.gameObject.GetComponent<KinematicMotion2D>();
                        Vector2 remainingDelta = direction * distanceRemaining;

                        if (otherMotion != null && CanPush(otherMotion, remainingDelta))
                        {
                            float totalMass = mass + otherMotion.mass;
                            float massRatio = totalMass > 0 ? mass / totalMass : 0f;
                            Vector2 startPosition = hit.rigidbody.position;
                            otherMotion.Slide(remainingDelta);
                            Vector2 pushDelta = hit.rigidbody.position - startPosition;
                            distanceRemaining = pushDelta.magnitude * massRatio;
                        }

                        Slide(direction * distanceRemaining, iterations + 1);
                        return;
                    }

                    if (hit.rigidbody != null && hit.rigidbody.bodyType == RigidbodyType2D.Dynamic)
                    {
                        // Cast the dynamic body's collider to find a safe push distance.
                        int dynamicHitCount = hit.collider.Cast(direction, contactFilter, colliderHits, distanceRemaining + margin);
                        float totalMass = mass + hit.rigidbody.mass;
                        float massRatio = totalMass > 0 ? mass / totalMass : 0f;
                        float pushDistance = distanceRemaining * massRatio;
                        for (int j = 0; j < dynamicHitCount; j++)
                        {
                            // Skip hits against our own colliders (the pushing body).
                            Collider2D hitCollider = colliderHits[j].collider;
                            bool isSelf = false;
                            foreach (Collider2D c in colliders)
                            {
                                if (c == hitCollider) { isSelf = true; break; }
                            }
                            if (isSelf) continue;

                            float d = colliderHits[j].distance - margin;
                            if (d < pushDistance)
                            {
                                pushDistance = d;
                            }
                        }
                        
                        pushDistance = Mathf.Max(0f, pushDistance);

                        // For dynamic bodies, MovePosition is delayed until next frame,
                        // so we write position directly for immediate synced movement.
                        hit.rigidbody.position += direction * pushDistance;
                        Physics2D.SyncTransforms();

                        // Keep sliding into the opened space.
                        Slide(direction * pushDistance, iterations + 1);
                        return;
                    }

                    // Sliding is allowed only on ground and on ceilings while airborne.
                    if (IsGroundNormal(hit.normal) || (IsCeilingNormal(hit.normal) && !isGrounded))
                    {
                        // "Right" direction relative to the hit surface.
                        Vector2 right = new Vector2(Mathf.Abs(hit.normal.y), hit.normal.y < 0 ? hit.normal.x : -hit.normal.x);

                        // Slide only in horizontal direction. This is not physically exact,
                        // but it prevents sliding right after landing from a fall.
                        Vector2 slideDelta = new Vector2(direction.x * distanceRemaining, 0);

                        // Convert the slideDelta into movement along the contact surface direction.
                        Vector2 projection = right * direction.x * distanceRemaining; // Keep total moved distance unchanged.

                        // This part uses a technique that is often hard for beginners:
                        // a recursive method (a method that calls itself).
                        // Up to here, we hit something and moved as far as possible.
                        // We want to keep moving with a new direction, but we may hit
                        // something again. To handle that, we run Slide again from the start.
                        // Recursion can loop forever, so we must track repeat count and
                        // stop after a fixed maximum, like this check does.

                        // Here we recursively call Slide to continue moving along the surface.
                        // If we made it here, it means that we tried moving but hit something along the way.
                        // However, we may still be able to move by changing direction along the surface (sliding).
                        // In order to attempt this adjusted movement, we call Slide again from within itself.
                        // This programming technique is called recursion. We have to be careful to avoid infinite loops,
                        // which is why we track the number of iterations and stop after a maximum limit.
                        Slide(projection, iterations + 1);
                    }
                }
            }
            else
            {
                // No collision happened, so move the object.
                // To avoid bad effects on normal physics behavior,
                // move through Rigidbody2D, not transform.
                MoveRigidbody(delta);
            }
        }

        protected int RigidbodyOverlap(ContactFilter2D contactFilter, Collider2D[] overlaps)
        {
            int totalHits = 0;
            foreach (Collider2D coll in colliders)
            {
                if (coll != null && !coll.isTrigger)
                {
                    int count = coll.Overlap(contactFilter, overlapColliders);
                    for (int i = 0; i < count; i++)
                    {
                        overlaps[totalHits] = overlapColliders[i];
                        totalHits++;
                        if (totalHits >= overlaps.Length)
                        {
                            return overlaps.Length;
                        }
                    }
                }
            }
            return totalHits;
        }

        protected int RigidbodyCast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] hits, float distance)
        {
            int totalHits = 0;
            foreach(Collider2D coll in colliders)
            {
                if (coll != null && !coll.isTrigger)
                {
                    int count = coll.Cast(direction, contactFilter, colliderHits, distance);
                    for (int i = 0; i < count; i++)
                    {
                        hits[totalHits] = colliderHits[i];
                        totalHits++;
                        if (totalHits >= hits.Length) {
                            return hits.Length;
                        }
                    }
                }
            }
            return totalHits;
        }

        // This is another important method in this component.
        // It checks whether moving in direction for distance will hit something.
        // It returns whether there was a collision. If there was,
        // hit stores the collision details.
        public bool Cast(Vector2 direction, float distance, out RaycastHit2D hit)
        {
            distance += margin; // Add gap margin to movement distance.
            bool collided = false; // Did collision happen? Start with false.

            hit = new RaycastHit2D(); // If no collision happens, hit stays default.
            contactFilter.layerMask = GetCollisionMask(); // Set Unity layers for collision filtering.
            contactFilter.useLayerMask = true;
            contactFilter.useTriggers = false;

            // Use Rigidbody2D "Cast" for collision checking.
            // "Cast" means checking what this collider would hit if it moved
            // in a given direction by a given distance in space.
            // This is a basic operation in game programming.
            int hitCount = RigidbodyCast(direction, contactFilter, hits, distance);

            // hitCount is the number of colliders hit.
            if (hitCount > 0) // Hit something...
            {
                // Check hit colliders one by one and find the nearest contact.
                for (int i = 0; i < hitCount; i++)
                {
                    // If farther than the current nearest hit, skip it.
                    if (hits[i].distance >= distance)
                    {
                        continue;
                    }

                    PlatformEffector2D effector = hits[i].collider.GetComponent<PlatformEffector2D>();
                    if (effector && effector.useOneWay) {
                        // Note: surfaceArc is not used yet.
                       Quaternion angle = effector.transform.rotation * Quaternion.Euler(0, 0, effector.rotationalOffset);
                       float dot = Vector2.Dot(velocity, angle * Vector3.up);
                       if (dot > 0 || hits[i].distance < 0.0001f) {
                            continue;
                        }
                    }

                    KinematicMotion2D otherMotion = hits[i].collider.GetComponentInParent<KinematicMotion2D>();
                    if (otherMotion != null &&
                        // If this side is the ground, do not treat as a collision.
                        (otherMotion.groundMotion == this ||
                        // Ground being followed has not moved yet, so do not treat as a collision.
                         otherMotion == ignoredMotion))
                    {
                        continue;
                    }

                    // Important: update "distance" only for contacts we do not ignore.
                    // If we update it for ignored contacts, a real contact behind them
                    // may be incorrectly skipped as "farther than the nearest hit".
                    distance = hits[i].distance;

                    // This is now the nearest contact found, so store its details.
                    hit = hits[i]; // Store collision details.
                    hit.distance -= margin; // Subtract margin to keep away from the collider.

                    collided = true; // Record that a collision happened.
                }
            }

            return collided; // Return whether a collision happened.
        }

        public static float maximumContactOffset
        {
            get
            {
                // The adjustment below corrects error from Unity's collision implementation.
                // Unity 2D physics uses the open-source library "Box2D" internally.
                // Box2D has a value called "b2_polygonRadius" used in collision checks.
                // https://github.com/erincatto/Box2D/blob/ef96a4f17f1c5527d20993b586b400c2617d6ae1/Box2D/Common/b2Settings.h#L81
                // In Unity, this can be adjusted by "Default Contact Offset" in project settings.
                // https://forum.unity.com/threads/what-is-default-contact-offset.750872/
                // But in 2D, "Default Contact Offset" seems inactive, and Box2D's
                // "b2_polygonRadius" is used even if settings are changed.
                // That value is "0.01f", so we add half of it here for a more
                // accurate contact position.

                return 0.005f;
            }
        }

        // Returns whether a surface with this normal counts as ground.
        public bool IsGroundNormal(Vector2 normal)
        {
            // Compute angle between the normal and up-direction reference, then
            // return whether it is below the threshold.
            return Vector2.Angle(Vector2.down * GravityDirection, normal) < maxGroundAngleDegrees;
        }

        public bool IsWallNormal(Vector2 normal)
        {
            return !IsGroundNormal(normal) && !IsCeilingNormal(normal);
        }

        // Returns whether this normal is a ceiling we can slide on.
        public bool IsCeilingNormal(Vector2 normal)
        {
            // Compute angle between the normal and up-direction reference, then
            // return whether it is below the threshold.
            return Vector2.Angle(Vector2.down, normal) < maxCeilingAngleDegrees;
        }

        protected virtual void Awake()
        {

        }

        // Initialization.
        protected virtual void Start()
        {
            rb = GetComponent<Rigidbody2D>(); // Get reference to the Rigidbody2D component.

            // Set Rigidbody2D type to "Kinematic".
            // Then this script, not the physics engine, moves the object.
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;

            colliders = GetComponentsInChildren<Collider2D>();

            // This script assumes gravity points straight down.
            // But project settings allow gravity in any direction.
            // If an incompatible gravity setting is detected,
            // output an error in the console.
            if (Physics2D.gravity.x != 0f || Physics2D.gravity.y > 0f)
            {
                Debug.LogError("This component works correctly only when gravity points straight down. Check your project settings.");
            }

            groundNormal = Vector2.up;
            groundContactNormal = Vector2.up;
        }

        private Contact[][] contactBuffer = new Contact[][] {
            new Contact[16],
            new Contact[16]
        };

        private int[] contactCounts = new int[]
        {
            0, 0
        };

        private int contactIndex = 0;
        private int oldContactIndex = 1;

        private void SetContactIndex(int index)
        {
            if (index == 0)
            {
                contactIndex = 0;
                oldContactIndex = 1;
            }
            else
            {
                contactIndex = 1;
                oldContactIndex = 0;
            }
        }

        private void SwitchContactBuffers()
        {
            SetContactIndex(oldContactIndex);
        }

        protected Contact[] contacts => contactBuffer[contactIndex];
        protected Contact[] oldContacts => contactBuffer[oldContactIndex];
        protected int contactCount => contactCounts[contactIndex];
        protected int oldContactCount => contactCounts[oldContactIndex];

        void CheckForContacts(Vector2 direction)
        {
            if (contactCount < contacts.Length)
            {
                contactFilter.layerMask = GetCollisionMask(); // Prepare Unity layers to include/exclude collisions.
                contactFilter.useLayerMask = true;
                contactFilter.useTriggers = false;

                int hitCount = RigidbodyCast(direction, contactFilter, hits, margin);
                int ndx = contactCount;
                for (int i = 0; i < hitCount && ndx < contacts.Length; i++, ndx++)
                {
                    contacts[ndx].self = gameObject;
                    contacts[ndx].rigidbody = hits[i].rigidbody;
                    contacts[ndx].collider = hits[i].collider;
                    contacts[ndx].normal = hits[i].normal;
                    contacts[ndx].point = hits[i].point;
                    contacts[ndx].direction = direction;

                    KinematicMotion2D km = hits[i].collider.gameObject.GetComponent<KinematicMotion2D>();
                    if (km != null)
                    {
                        contacts[ndx].relativeVelocity = velocity - km.velocity;
                    }
                    else
                    {
                        Rigidbody2D rb2d = hits[i].collider.gameObject.GetComponent<Rigidbody2D>();
                        if (rb2d != null)
                        {
                            contacts[ndx].relativeVelocity = velocity - rb2d.linearVelocity;
                        }
                        else
                        {
                            contacts[ndx].relativeVelocity = velocity;
                        }
                    }
                    contactCounts[contactIndex]++;
                }
            }
            
        }

        void UpdateContacts()
        {
            SwitchContactBuffers();

            contactCounts[contactIndex] = 0;

            CheckForContacts(Vector2.down);
            CheckForContacts(Vector2.right);
            CheckForContacts(Vector2.left);
            CheckForContacts(Vector2.up);

            // New contacts and continuing contacts.
            for (int i = 0; i < contactCount; i++)
            {
                bool found = false;
                for (int j = 0; j < oldContactCount; j++)
                {
                    if (contacts[i] == oldContacts[j])
                    {
                        ContactStay(contacts[i]);
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    ContactEnter(contacts[i]);
                }
            }

            // Contacts that ended.
            for (int i = 0; i < oldContactCount; i++)
            {
                bool found = false;
                for (int j = 0; j < contactCount; j++)
                {
                    if (oldContacts[i] == contacts[j])
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    ContactExit(oldContacts[i]);
                }
            }

        }

        // Called when an object is "crushed".
        //
        // "Crushed" means overlaps cannot be resolved no matter what.
        // In other words, this object is pushed by something moving
        // (KinematicMotion2D, regular kinematic Rigidbody2D, or dynamic Rigidbody2D)
        // and has no space to escape. You can identify the pusher from
        // contact.collider and contact.rigidbody.
        //
        // Notification is sent only once at the moment crushing starts
        // (same idea as justLanded for ground checks).
        // If you need to keep handling while crushed, store that state
        // on the receiver side.
        protected virtual void CrushedBy(Contact contact)
        {
            SendMessage("OnCrushedBy", contact, SendMessageOptions.DontRequireReceiver);
        }

        protected virtual void ContactEnter(Contact contact)
        {
            SendMessage("OnContactEnter", contact, SendMessageOptions.DontRequireReceiver);
        }

        protected virtual void ContactExit(Contact contact)
        {
            SendMessage("OnContactExit", contact, SendMessageOptions.DontRequireReceiver);
        }

        protected virtual void ContactStay(Contact contact)
        {
            SendMessage("OnContactStay", contact, SendMessageOptions.DontRequireReceiver);
        }

        private void MoveRigidbody(Vector2 delta)
        {
            WillMove?.Invoke(delta);
            
            rb.position += delta;

            // Move attached objects.
            foreach (KinematicMotion2D attached in attachedMotions)
            {
                attached.rb.position += delta;
            }
        }

        // Minimum follow movement to ignore.
        // Slide ends early only when "distance == 0f" exactly,
        // so even tiny error values like 1e-9 can still trigger a Cast by "margin".
        // When an object is exactly touching a surface, hit.distance is about 0,
        // and subtracting margin can make it negative, causing a bounce in the
        // opposite direction. This threshold prevents that.
        private const float carryEpsilon = 1e-5f;

        // Called when the ground this object is on moves.
        // Follow the ground movement (delta).
        private void GroundWillMove(Vector2 delta)
        {
            KinematicMotion2D ground = groundMotion;

            // A "sticky" ground always carries objects on it,
            // even when moving down very fast.
            // For non-sticky ground, do not follow in vertical direction.
            // Ground moving down faster than free fall moves away,
            // and the object falls by gravity.
            if (delta.y < 0f && ground != null && !ground.sticky)
            {
                delta.y = 0f;
            }

            // Like movement in FixedUpdate, split movement into
            // "horizontal" and "vertical" based on ground direction.
            // This way, if horizontal movement is blocked, vertical movement stays,
            // so the object is pushed along the ground surface
            // (without floating above it or sinking into it).
            Vector2 normal = groundContactNormal;
            Vector2 right = new Vector2(normal.y, -normal.x);

            float alongGround = Vector2.Dot(right, delta);
            float acrossGround = Vector2.Dot(normal, delta);

            // This method is called before ground actually moves,
            // so ground is still at its old position.
            // Ignore ground only while following so we do not collide with that old position.
            // Restore "ignoredMotion" so behavior stays correct even if an exception
            // happens during follow, or if follow calls become nested.
            KinematicMotion2D previousIgnored = ignoredMotion;
            ignoredMotion = ground;
            try
            {
                if (Mathf.Abs(alongGround) > carryEpsilon)
                {
                    MoveBy(right * alongGround);
                }

                if (Mathf.Abs(acrossGround) > carryEpsilon)
                {
                    MoveBy(normal * acrossGround);
                }
            }
            finally
            {
                ignoredMotion = previousIgnored;
            }
        }

        private Action<Vector2> WillMove;

        protected virtual void FixedUpdate()
        {
            float deltaSeconds = Time.fixedDeltaTime;

            if (!simulatePhysics)
            {
                return;
            }

            ProcessOverlaps();

            // Update grounded state.
            UpdateGroundedState();

            // Update time since leaving ground.
            if (!isGrounded)
            {
                timeInAir += deltaSeconds;
            }
            else
            {
                timeInAir = 0f;
            }

            if (useGravity && (!isGrounded || groundDistance > margin)) // Should gravity be applied?
            {
                // Accelerate in the gravity direction.
                // "Time.fixedDeltaTime" stores elapsed time since the previous FixedUpdate.
                velocity += Physics2D.gravity * deltaSeconds * gravityMultiplier * gravityModifier;
            }

            // Check speed limits.
            if (Mathf.Abs(velocity.x) > maxSpeedSide)
            {
                // Over the limit, so clamp to max speed.
                velocity.x = Mathf.Sign(velocity.x) * maxSpeedSide;
            }

            velocity.y = Mathf.Clamp(velocity.y, -Mathf.Abs(maxSpeedDown), maxSpeedUp);

           
            // Calculate move distance for this frame.
            Vector2 motion = velocity * deltaSeconds;


            // Store position before movement.
            Vector2 startPosition = rb.position;

            // For smooth and responsive feel, move horizontally first,
            // then vertically. Here, "horizontal" and "vertical" are relative
            // to ground direction, not absolute world directions.
            // If not grounded, they become true horizontal and true vertical.
            // This also uses vector projection.
            Vector2 horizontalMotion = groundRight * Vector2.Dot(groundRight, motion);
            Vector2 verticalMotion = groundNormal * Vector2.Dot(groundNormal, motion);

            positionAdjustment = Vector2.zero;

            // Move horizontally, then vertically.
            Slide(horizontalMotion);
            Slide(verticalMotion);

            // There may have been collisions and slides. Compute actual movement.
            Vector2 actualMotion = rb.position - startPosition - positionAdjustment;

            // Compute actual speed from actual movement.
            velocity = actualMotion / deltaSeconds;

            if (isGrounded)
            {
                // Store speed while grounded. This is needed for player air movement logic.
                lastGroundVelocity = velocity;
            }

            UpdateContacts();
        }


        // Moving objects can affect physics, so that work is done in FixedUpdate.
        // But animation updates are synced with Update, so animator-related work
        // should be done here.
        protected virtual void Update()
        {
        }

        protected virtual void OnDestroy()
        {
            if (parent != null)
            {
                Detach();
            }
        }

        private static void Separate(KinematicMotion2D objectToMove, Collider2D otherCollider)
        {
            // Get the other side's speed to judge which object caused overlap.
            Vector2 otherVelocity = Vector2.zero;
            KinematicMotion2D otherMotion = otherCollider.GetComponentInParent<KinematicMotion2D>();
            if (otherMotion != null)
            {
                otherVelocity = otherMotion.velocity;
            }
            else
            {
                Rigidbody2D otherRb = otherCollider.attachedRigidbody;
                if (otherRb != null)
                {
                    otherVelocity = otherRb.linearVelocity;
                }
            }

            foreach(Collider2D coll in objectToMove.colliders)
            {
                if (coll == otherCollider)
                continue;
                if (coll != null && !coll.isTrigger)
                {
                   ColliderDistance2D colliderDistance2D = coll.Distance(otherCollider);
                    if (colliderDistance2D.isOverlapped)
                    {
                        Vector2 delta = colliderDistance2D.normal * colliderDistance2D.distance;

                        // Decide whether overlap was caused by objectToMove or otherCollider movement.
                        // delta is the vector that pushes objectToMove out of overlap.
                        // -delta is the movement direction if objectToMove caused the overlap.
                        // If objectToMove is not moving into overlap and the other side is,
                        // skip separation.
                        Vector2 overlapDir = delta.normalized;
                        float selfContribution = Vector2.Dot(objectToMove.velocity, -overlapDir);
                        float otherContribution = Vector2.Dot(otherVelocity, overlapDir);

                        if (selfContribution <= 0.01f && otherContribution > 0f)
                        {
                            // Overlap was caused by the other side, not objectToMove.
                            continue;
                        }

                        // Apply separation only if it does not create a new overlap.
                        Vector2 originalPosition = objectToMove.rb.position;
                        objectToMove.rb.position += delta;
                        Physics2D.SyncTransforms();

                        ContactFilter2D overlapFilter = new ContactFilter2D();
                        overlapFilter.layerMask = objectToMove.GetCollisionMask();
                        overlapFilter.useLayerMask = true;
                        overlapFilter.useTriggers = false;

                        bool causesNewOverlap = false;
                        int overlapCount = coll.Overlap(overlapFilter, objectToMove.overlapColliders);
                        for (int j = 0; j < overlapCount; j++)
                        {
                            if (objectToMove.overlapColliders[j] != otherCollider)
                            {
                                causesNewOverlap = true;
                                break;
                            }
                        }

                        if (causesNewOverlap)
                        {
                            // Revert. Separation would push into another object.
                            objectToMove.rb.position = originalPosition;
                            Physics2D.SyncTransforms();
                        }
                    }
                }
            }
        }

        // Number of frames unresolved overlap must continue before judging "crushed".
        // ProcessOverlaps is limited by maxIterations, so deep overlap may not be fully
        // resolved in one frame. This prevents temporary states from being
        // misdetected as "crushed".
        private const int crushPersistenceFrames = 2;

        // Number of frames with unresolved overlap.
        private int unresolvedOverlapFrames = 0;

        // Whether this object has ever reached a no-overlap state.
        // Used to tell apart "crushed" from "placed inside geometry from the start".
        // "Crushed" means it changed from safe to trapped by something moving.
        // A badly spawned object stays overlapped from the beginning, without that change.
        private bool didEverResolveOverlaps = false;

        // Flag to avoid repeated notifications for the same "crushed" state.
        private bool crushReported = false;

        // Flag to avoid repeated bad-placement warnings (every frame would flood logs).
        private bool reportedBadPlacement = false;

        // Overlaps shallower than this are treated as normal contact and ignored.
        private float CrushPenetrationTolerance
        {
            get
            {
                return Mathf.Max(margin, maximumContactOffset);
            }
        }

        // Find the deepest unresolved overlap that remains.
        private bool FindUnresolvedOverlap(out Collider2D blockingCollider, out ColliderDistance2D deepest)
        {
            blockingCollider = null;
            deepest = new ColliderDistance2D();

            float worst = -CrushPenetrationTolerance;

            contactFilter.layerMask = GetCollisionMask();
            contactFilter.useLayerMask = true;
            contactFilter.useTriggers = false;

            int hitCount = RigidbodyOverlap(contactFilter, overlaps);
            for (int i = 0; i < hitCount; i++)
            {
                Collider2D other = overlaps[i];
                if (other == null)
                {
                    continue;
                }

                // Exclude this object's own colliders.
                if (other.attachedRigidbody != null && other.attachedRigidbody == rb)
                {
                    continue;
                }

                // Tilemaps are also skipped in ProcessOverlaps, so skip them here too.
                if (other.GetComponent<TilemapCollider2D>() != null)
                {
                    continue;
                }

                foreach (Collider2D coll in colliders)
                {
                    if (coll == null || coll.isTrigger || coll == other)
                    {
                        continue;
                    }

                    ColliderDistance2D colliderDistance2D = coll.Distance(other);
                    if (colliderDistance2D.isValid && colliderDistance2D.distance < worst)
                    {
                        worst = colliderDistance2D.distance;
                        deepest = colliderDistance2D;
                        blockingCollider = other;
                    }
                }
            }

            return blockingCollider != null;
        }

        private Contact BuildCrushContact(Collider2D blockingCollider, ColliderDistance2D separation)
        {
            Contact contact = new Contact();
            contact.self = gameObject;
            contact.collider = blockingCollider;
            contact.rigidbody = blockingCollider.attachedRigidbody;
            contact.point = separation.pointB;

            // ColliderDistance2D normal points from this side to the other side.
            // To match other contact events,
            // set normal to "from other surface toward self"
            // and direction to "from self toward other".
            contact.normal = -separation.normal;
            contact.direction = separation.normal;
            contact.sliding = false;

            Vector2 otherVelocity = Vector2.zero;
            KinematicMotion2D otherMotion = blockingCollider.GetComponentInParent<KinematicMotion2D>();
            if (otherMotion != null)
            {
                otherVelocity = otherMotion.velocity;
            }
            else if (contact.rigidbody != null)
            {
                otherVelocity = contact.rigidbody.linearVelocity;
            }
            contact.relativeVelocity = velocity - otherVelocity;

            return contact;
        }

        // Check whether ProcessOverlaps fully resolved overlaps,
        // and notify if needed.
        // Run this only on the outermost call.
        private void HandleUnresolvedOverlaps()
        {
            Collider2D blockingCollider;
            ColliderDistance2D separation;

            if (!FindUnresolvedOverlap(out blockingCollider, out separation))
            {
                // We reached a no-overlap state.
                didEverResolveOverlaps = true;

                if (unresolvedOverlapFrames > 0)
                {
                    unresolvedOverlapFrames--;
                }

                if (unresolvedOverlapFrames == 0)
                {
                    crushReported = false;
                }

                return;
            }

            unresolvedOverlapFrames++;

            if (unresolvedOverlapFrames < crushPersistenceFrames)
            {
                return;
            }

            if (!didEverResolveOverlaps)
            {
                // This object never reached a no-overlap state.
                // So it was not "crushed". It was placed in a trapped position
                // from the start. This is a placement mistake,
                // so report it to developers as a warning, not game logic.
                if (!reportedBadPlacement)
                {
                    reportedBadPlacement = true;
                    Debug.LogWarning(
                        $"KinematicMotion2D '{name}' is placed where overlap cannot be resolved." +
                        $" It is penetrating '{blockingCollider.name}' by {-separation.distance:F3} and has no room to escape." +
                        $" (Position: {rb.position}) Please check the spawn position.",
                        this);
                }

                return;
            }

            if (!crushReported)
            {
                crushReported = true;
                CrushedBy(BuildCrushContact(blockingCollider, separation));
            }
        }

        protected void ProcessOverlaps(int iterations = 0)
        {
            if (iterations > maxIterations)
            {
                return;
            }

            contactFilter.layerMask = GetCollisionMask(); // Prepare Unity layers to include/exclude collisions.
            contactFilter.useLayerMask = true;
            contactFilter.useTriggers = false;

            int hitCount = RigidbodyOverlap(contactFilter, overlaps);
            for (int i = 0; i < hitCount; i++)
            {
                KinematicMotion2D otherMotion = overlaps[i].GetComponentInParent<KinematicMotion2D>();

                if (otherMotion != null)
                {
                    if (otherMotion.pushPriority < pushPriority)
                    {
                        Separate(otherMotion, colliders[0]);
                    }
                    else
                    {
                        Separate(this, overlaps[i]);
                        ProcessOverlaps(iterations + 1);
                        break;
                    }
                }
                else
                {
                    // Overlap with tilemap colliders is hard to resolve correctly,
                    // so skip it for now (temporary handling).
                    TilemapCollider2D tilemapCollider = overlaps[i].GetComponent<TilemapCollider2D>();
                    if (tilemapCollider == null)
                    {
                        Separate(this, overlaps[i]);
                        ProcessOverlaps(iterations + 1);
                        break;
                    }

                }
            }

            // Up to here, overlap resolution was attempted.
            // Check the result only on the outermost call.
            // We judge by "what happened after trying to resolve",
            // so it does not depend on which branch Separate took.
            if (iterations == 0)
            {
                HandleUnresolvedOverlaps();
            }
        }

        // Detect ground.
        // Return true if a collider that counts as ground is found
        // in the given direction and distance.
        // Collision details are stored in hit.
        public bool CheckForGround(Vector2 direction, float distance, out RaycastHit2D hit)
        {
            hit = new RaycastHit2D(); // Initialize to default.

            if (!useGravity)
            {
                return false;
            }

            contactFilter.layerMask = GetCollisionMask(); // Prepare Unity layers to include/exclude collisions.
            contactFilter.useLayerMask = true;
            contactFilter.useTriggers = false;

            // Cast with Rigidbody2D.
            int hitCount = RigidbodyCast(direction, contactFilter, hits, distance + margin);
            for (int i = 0; i < hitCount; i++)
            {
                // Compare gravity direction and the hit surface normal.
                if (IsGroundNormal(hits[i].normal))
                {
                    // Ground found, so store details.
                    hit = hits[i];
                    hit.distance -= margin;
                    return true; // No need to search for more ground.
                }
            }

            // If we get here, no ground was detected.
            return false;
        }

        public void LateUpdate()
        {
            
        }

        protected virtual void Landed(float speed)
        {

        }

        protected virtual void Fell()
        {

        }

        void SetGroundMotion(KinematicMotion2D motion)
        {
            if (motion != groundMotion)
            {
                if (groundMotion != null)
                {
                    groundMotion.WillMove -= GroundWillMove;
                }

                groundMotion = motion;

                if (groundMotion != null)
                {
                    groundMotion.WillMove += GroundWillMove;
                }
            }
        }

        void UpdateGround(bool oldState, RaycastHit2D groundHit)
        {
            if (isGrounded)
            {
                // Update follow normal even on the landing frame.
                // Following starts the moment we stand on ground,
                // so we need the correct direction right away.
                groundContactNormal = groundHit.normal;

                // We are grounded, so store ground direction.
                if (oldState)
                {
                    // Update ground normal starting from the frame after landing.
                    // Updating on the landing moment can cause unwanted slope sliding.
                    groundNormal = groundHit.normal;
                    groundDistance = groundHit.distance;
                }

                // Check if we stand on an object that has KinematicMotion2D.
                // If yes, we receive that object's movement effect.
                if (useGroundMotion)
                {
                    SetGroundMotion(groundHit.collider.gameObject.GetComponentInParent<KinematicMotion2D>());
                }

                // Small but important handling here.
                // Even when grounded, if moving upward, treat as "not grounded".
                // Otherwise, jumping on slopes can be pushed sideways.
                // But climbing a slope can also have upward speed,
                // so compare ground direction and movement direction to
                // tell slope climbing apart from jump or launch movement.
                if (velocity.magnitude > 0.01f)
                {
                    // Is movement heading away from the ground?
                    if (Vector2.Dot(groundHit.normal, velocity.normalized) > 0.25f)
                    {
                        // We are about to "take off", so treat as not grounded.
                        // Since we are leaving the ground, set isGrounded to false.
                        // But if the ground is moving, we still want its effect,
                        // so keep groundMotion as is.
                        // (Without this, jumping from an upward-moving object
                        // may not work correctly.)
                        isGrounded = false;
                    }
                }
            }
            else
            {
                groundContactNormal = Vector2.up;
                SetGroundMotion(null);
            }

            justLanded = isGrounded && !oldState;
            justFell = !isGrounded && oldState;

            if (justLanded)
            {
                velocity -= groundVelocity;
                Landed(velocity.y);
            }

            if (justFell)
            {
                Fell();
            }
        }

        // This method updates whether the object is grounded and
        // also updates ground direction state.
        public void UpdateGroundedState()
        {
            bool oldState = isGrounded;

            // Start by assuming not grounded.
            isGrounded = false;
            groundNormal = Vector2.up;
            groundDistance = 0f;

            // Detect ground.
            RaycastHit2D groundHit = new RaycastHit2D();
            isGrounded = CheckForGround(Vector2.up * GravityDirection, groundCheckDistance, out groundHit);

            UpdateGround(oldState, groundHit);
        }
    }
} // namespace

