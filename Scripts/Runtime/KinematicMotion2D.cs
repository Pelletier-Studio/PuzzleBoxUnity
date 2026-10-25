/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
 
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

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
        #region Inspector Fields
        public bool simulatePhysics = true;
        public float mass = 1f;

        [Header("Gravity")] // Show a header in the Inspector.
        public bool useGravity = true; // Should this be affected by gravity?

        
        // The gravityModifier parameter is meant to adjust the global strength
        // of the gravity applied to this object. This can be used to make the object
        // appear to float, or even completely reverse the direction of gravity by 
        // setting this value to a negative number.
        public float gravityModifier = 1f; // Adjust the strength of gravity.

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
        public float margin = 0.02f;

        // To check if this object is standing on the ground, we cast a ray downward
        // to see if it collides with an object that can be considered ground.
        // This parameter controls how far below the object we check for the ground.
        // (Note that this distance is added to the margin parameter above. The
        // ground check distance is never less than the margin.)
        [Min(0.001f)]
        public float groundCheckDistance = 0.01f;

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

        // When moving along slopes, we can force the object to adjust its direction to stick
        // to the ground. In practice, movement directions that are slightly off the ground's
        // orientation can cause the object to lift off the ground, causing jagged motion.
        // We calculate the dot product between the movement direction and the ground normal.
        // If it is below the stickyGroundThreshold, we adjust the movement direction to follow
        // the ground exactly.
        [Range(0f, 1f)]
        public float stickyGroundThreshold = 0.08f;

        // This is a more advanced parameter and usually does not need changing.
        // After a collision, movement direction can be changed and movement can continue.
        // (For example, when hitting a slope, it keeps moving up the slope instead of stopping.)
        // This value is the maximum number of times that process can repeat.
        [Min(1)]
        public int maxIterations = 2;
        

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

        // Friction between this object and the ground it stands on (or, as ground, with what stands on it).
        // It only acts on the ground: it slows the object's motion relative to the ground until it rests on it.
        // Both sides need friction: the combined value is the square root of the product of the two, and at
        // 1 the relative speed drops by the strength of gravity each second. With no friction, an object
        // landing on a moving platform keeps its own motion and the platform slides away under it.
        [Min(0)]
        public float friction = 0f;

        // The friction of ground that has no KinematicMotion2D (static geometry), used in its place
        // when this object stands on it.
        [Min(0)]
        public float defaultGroundFriction = 0f;

        #endregion

        #region Accessors
        // Whether this object is standing on the ground.
        public bool isGrounded { get; private set; }

        // This is true in the frame immediately after landing on the ground.
        public bool justLanded { get; private set; }

        // This is true in the frame immediately after leaving the ground.
        public bool justFell { get; private set; }

        // Time since leaving the ground (seconds).
        public float timeInAir { get; private set; }

        // The direction of gravity for this object, taking into account the global gravity and the gravity modifier.
        protected float GravityDirection
        {
            get
            {
                return Mathf.Sign(Physics2D.gravity.y) * Mathf.Sign(gravityModifier);
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
                // When we move this object, we must make sure to also move all
                // attached objects as well.
                Vector2 delta = value - rigidbody.position;
                rigidbody.position = value;
                MoveAttachedMotions(delta);
            }
        }
        

        #endregion

        #region Public Fields

        // [HideInInspector] // Hide in the Inspector.
        public Vector2 velocity; // Movement speed. Usually changed by other components in code.

        [HideInInspector] // Hide in the Inspector.
        public Vector2 lastGroundVelocity; // Speed when this last touched the ground.

        // The gravityMultiplier parameter is meant only for scripts and should not
        // be modified or displayed in the inspector. It is meant to allow scripts to
        // temporarily modify the gravity without affecting the global gravity setting,
        // or gravityModifier. This is necessary to implement mechanics such as adjustable jump
        // height, or any other temporary effect that modifies gravity.
        [HideInInspector]
        public float gravityMultiplier = 1f; 

        #endregion

        #region Events

        private Action<Vector2> WillMove;
        private Action<KinematicMotion2D> UpdateCompleted;

        protected virtual void Landed(float speed) { }

        protected virtual void Fell() { }

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


        #endregion

        #region Attachment

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

            if (parentMotion == this)
            {
                Debug.LogWarning($"Attempted to attach '{name}' to itself.");
                return;
            }

            // Make sure we don't create a cycle in the attachment chain.
            if (parentMotion.IsAttachedTo(this))
            {
                Debug.LogWarning(
                    $"Attempted to attach '{name}' to '{parentMotion.name}', which is already attached to it. ");
                return;
            }

            if (parentMotion != parent)
            {
                Detach();
                parent = parentMotion;
                parent.attachedMotions.Add(this);
                parent.UpdateColliders();
            }
        }

        // Detach this object from its current parent, if any.
        public void Detach()
        {
            if (parent != null)
            {
                parent.attachedMotions.Remove(this);
                parent.UpdateColliders();
                parent = null;
            }
        }

        // Read-only view of the attachment graph. These do not change any behaviour;
        // they exist so attachment state can be inspected from outside this class
        // instead of being inferred from how things move.
        public KinematicMotion2D attachedTo
        {
            get
            {
                KinematicMotion2D result = parent;
                while(result && result.parent != null)
                {
                    result = result.parent;
                }
                return result;
            }
        }

        // public int attachedCount => attachedMotions.Count;
        public IReadOnlyList<KinematicMotion2D> attachments => attachedMotions;

        protected bool IsAttachedTo(KinematicMotion2D otherMotion)
        {
            KinematicMotion2D current = parent;
            while(current != null)
            {
                if(current == otherMotion)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        // The body at the top of this one's attachment chain - itself, when unattached.
        // Only the root actually simulates; everything below it is carried.
        private KinematicMotion2D AttachmentRoot
        {
            get
            {
                KinematicMotion2D result = this;
                while (result.parent != null)
                {
                    result = result.parent;
                }
                return result;
            }
        }

        // Are the two bodies part of one attached subtree? Members of a subtree act as a
        // single rigid body, so they never collide with each other and never report
        // contacts against each other.
        //
        // Compares roots rather than walking one chain, so siblings are covered too - two
        // items held by the same holder are as much "the same body" as holder and item.
        protected bool IsInSameAttachedSubtree(KinematicMotion2D otherMotion)
        {
            if (otherMotion == null)
            {
                return false;
            }

            if (otherMotion == this)
            {
                return true;
            }

            // Two unattached bodies each root to themselves, so this stays false for them.
            return otherMotion.AttachmentRoot == AttachmentRoot;
        }

        public float combinedMass
        {
            get
            {
                float m = mass;

                foreach (var attached in attachedMotions)
                {
                    m += attached.combinedMass;
                }

                return m;
            }
        }

        #endregion
        
        #region Ground
        
        protected KinematicMotion2D groundMotion = null;

        // Ground normal used for following moving ground.
        // "groundNormal" above is intentionally not updated on the landing frame
        // (to prevent sliding on slopes; see UpdateGround). But following starts
        // immediately when landing, so follow logic needs the correct normal even
        // on that frame. So we store this separately.
        private Vector2 groundContactNormal = Vector2.up;

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

        void SetGroundMotion(KinematicMotion2D motion)
        {
            // Watch out for this special case: we landed on something that is attached to something else.
            KinematicMotion2D motionParent = motion ? motion.attachedTo : null;
            if (motionParent != null)
            {
                motion = motionParent;
            }

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
                groundNormal = groundHit.normal;
                groundDistance = groundHit.distance;

                // Snap to ground: Should we add a flag to turn this off?
                if (groundDistance > 0) // Note: groundDistance is measured from the margin
                {
                    MoveBy(Vector2.up * GravityDirection * groundDistance);
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

                // Check if we stand on an object that has KinematicMotion2D.
                // If yes, we receive that object's movement effect.
                // While taking off, keep it only if we were grounded until now: the ground then hands
                // over its velocity when we leave (see FellOffGround). If we were already airborne,
                // that handover has happened, and riding the ground again would move us twice.
                if (useGroundMotion && (isGrounded || oldState))
                {
                    SetGroundMotion(groundHit.collider.gameObject.GetComponentInParent<KinematicMotion2D>());
                }
            }

            justLanded = isGrounded && !oldState;
            justFell = !isGrounded && oldState;

            if (justLanded)
            {
                if (groundMotion)
                {
                    LandOnMovingGround();
                }
                Landed(velocity.y);
            }

            if (justFell)
            {
                if (groundMotion)
                {
                    groundMotion.UpdateCompleted += FellOffGround;
                    if (groundMotion.updateTime >= updateTime)
                    {
                        // Ground was already updated this frame...
                        this.UpdateCompleted += FellOffGround;
                    }
                }
                Fell();
            }
        }

        // Our velocity was a world velocity until now, and is relative to the ground from now on, so we
        // keep our horizontal velocity relative to the ground. (Friction may then bring it to zero.)
        void LandOnMovingGround()
        {
            float groundX = groundMotion.velocity.x;
            velocity.x -= groundX;

            // If the ground has already moved during this step, it did so before we were riding it,
            // so we catch up with its horizontal motion now. (Its vertical motion is taken care of by
            // the ground check, which found it where it is now.)
            if (Time.inFixedTimeStep && groundMotion.updateTime >= Time.fixedTime)
            {
                GroundWillMove(new Vector2(groundX * Time.fixedDeltaTime, 0f));
            }
        }

        void FellOffGround(KinematicMotion2D sender)
        {
            // Whichever of the two notified us, the other must not fire later (say, after we land back on this ground).
            sender.UpdateCompleted -= FellOffGround;
            UpdateCompleted -= FellOffGround;
            if (groundMotion)
            {
                groundMotion.UpdateCompleted -= FellOffGround;

                Vector2 v = groundMotion.velocity;
                if (!groundMotion.sticky && v.y < 0f)
                {
                    v.y = 0f;
                }
                velocity += v;
                groundContactNormal = Vector2.up;
                SetGroundMotion(null);
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

            // Cast with Rigidbody2D. (Don't use attached colliders for ground detection.)
            int hitCount = RigidbodyCast(direction, contactFilter, hits, distance + margin, false);
            for (int i = 0; i < hitCount; i++)
            {
                // Ignore if in same attachment subtree
                KinematicMotion2D otherMotion = hits[i].collider.gameObject.GetComponent<KinematicMotion2D>();
                if (otherMotion && IsInSameAttachedSubtree(otherMotion))
                {
                    continue;
                }

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

        

        // Called when the ground this object is on moves.
        // Follow the ground movement (delta).
        private void GroundWillMove(Vector2 delta)
        {
            KinematicMotion2D ground = groundMotion;

            if (IsAttachedTo(ground))
            {
                // Let attachment handle the movement.
                return;
            }

            // A "sticky" ground always carries objects on it,
            // even when moving down very fast.
            // For non-sticky ground, do not follow in vertical direction.
            // Ground moving down faster than free fall moves away,
            // and the object falls by gravity.
            if (delta.y < 0f && ground != null && !ground.sticky)
            {
                delta.y = 0f;
            }

            // When something blocks us while the ground carries us, we slide along that obstacle,
            // keeping the part of the movement that is across the ground (along its normal).
            // That keeps our height above the ground: measured from the ground, we slide along
            // its surface and never sink into it or float above it.
            Vector2 normal = groundContactNormal;

            // This method is called before ground actually moves,
            // so ground is still at its old position.
            // Ignore ground only while following so we do not collide with that old position.
            // Restore "ignoredMotion" so behavior stays correct even if an exception
            // happens during follow, or if follow calls become nested.
            KinematicMotion2D previousIgnored = ignoredMotion;
            ignoredMotion = ground;
            try
            {
                Vector2 remaining = delta;
                for (int i = 0; i <= maxIterations && remaining.magnitude > carryEpsilon; i++)
                {
                    // Find out what (if anything) will stop us, then move as far as we can.
                    // (MoveBy still pushes whatever can be pushed out of the way.)
                    Vector2 start = rigidbody.position;
                    bool blocked = Cast(remaining.normalized, remaining.magnitude, out RaycastHit2D hit);
                    MoveBy(remaining);
                    remaining -= rigidbody.position - start;

                    if (!blocked || remaining.magnitude <= carryEpsilon)
                    {
                        break;
                    }

                    // Redirect what is left along the obstacle, keeping its component across the ground.
                    // If the obstacle runs parallel to the ground (a ceiling above us, a ledge below),
                    // there is no way to do that, so we stop.
                    Vector2 obstacleTangent = new Vector2(hit.normal.y, -hit.normal.x);
                    float obstacleAcross = Vector2.Dot(obstacleTangent, normal);
                    if (Mathf.Abs(obstacleAcross) < carryEpsilon)
                    {
                        break;
                    }
                    remaining = obstacleTangent * (Vector2.Dot(remaining, normal) / obstacleAcross);
                }
            }
            finally
            {
                ignoredMotion = previousIgnored;
            }
        }

        



        #endregion
        
        #region Collisions

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

        // Get the combined bounds of all non-trigger colliders attached to this object and its children.
        // If updateColliders is true, it will refresh the list of colliders before calculating the bounds.
        public Bounds GetBounds(bool updateColliders = false)
        {
            if (updateColliders) {
                UpdateColliders();
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
            return otherMotion.groundMotion != this && otherMotion.pushable && pushPriority > otherMotion.pushPriority;

        }

        protected RaycastHit2D[] hits = new RaycastHit2D[8];
        protected RaycastHit2D[] colliderHits = new RaycastHit2D[8];
        // protected Collider2D[] overlapColliders = new Collider2D[8];
        // protected Collider2D[] overlaps = new Collider2D[8];

        // Results of the edge-zone overlap checks. This needs a buffer of its own: the
        // zone checks run while a RigidbodyOverlap result is being walked, and sharing
        // "overlapColliders" or "overlaps" would overwrite the very list being read.
        // protected Collider2D[] zoneResults = new Collider2D[8];
        protected ContactFilter2D contactFilter = new ContactFilter2D();

        private Collider2D[] _ownColliders;
        private List<Collider2D> _combinedColliders;

        // Rebuilds this body's footprint: its own colliders followed by those of everything
        // attached below it. Deliberately does NOT propagate upward - see UpdateColliders.
        //
        // Splitting the downward rebuild from the upward propagation is what makes it safe
        // to build a child's list from inside the parent's rebuild. Were this one method,
        // that nested call would propagate straight back into the parent and Clear() the
        // very list being enumerated.
        private void RebuildColliders()
        {
            _ownColliders = GetComponentsInChildren<Collider2D>();

            if (_combinedColliders == null)
            {
                _combinedColliders = new List<Collider2D>();
            }
            else
            {
                _combinedColliders.Clear();
            }

            foreach (var collider in _ownColliders)
            {
                _combinedColliders.Add(collider);
            }

            foreach (var motion in attachedMotions)
            {
                if (motion == null)
                {
                    continue;
                }

                // A body attached before its Start() has run has no list yet. Build it here
                // rather than dereferencing null: attaching an inactive body must not break
                // the parent's own footprint.
                if (motion._combinedColliders == null)
                {
                    motion.RebuildColliders();
                }

                foreach (var collider in motion._combinedColliders)
                {
                    _combinedColliders.Add(collider);
                }
            }
        }

        protected void UpdateColliders()
        {
            RebuildColliders();

            // Our footprint is part of every ancestor's footprint, so they have to be
            // rebuilt too. AttachTo keeps the graph acyclic, so this walk terminates.
            if (parent != null)
            {
                parent.UpdateColliders();
            }
        }

        protected List<Collider2D> colliders => _combinedColliders;

        // Target excluded from collision checks only while following.
        // When ground moves, it sends a "WillMove" notice before it actually moves.
        // So when follow logic runs, the ground is still at the old position.
        // Collision checks against that old position are not useful, so we ignore
        // the ground only during following.
        // (This mirrors the logic where ground ignores its riders. See Cast.)
        private KinematicMotion2D ignoredMotion = null;

        protected virtual LayerMask GetCollisionMask()
        {
            return collisionMask;
        }


        protected virtual float ProcessCollision(RaycastHit2D hit, Vector2 direction, float distanceRemaining)
        {
            return distanceRemaining;
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
            return Vector2.Angle(Vector2.up * GravityDirection, normal) < maxCeilingAngleDegrees;
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
                    KinematicMotion2D hitMotion = hits[i].collider.GetComponentInParent<KinematicMotion2D>();

                    // A body and the things attached to it are one rigid body. Touching
                    // yourself is not a collision, so it is not an event either.
                    if (IsInSameAttachedSubtree(hitMotion))
                    {
                        ndx--; // This slot was not filled, so do not advance past it.
                        continue;
                    }

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

        // Runs the contact pass for every body attached below this one.
        //
        // An attached body's FixedUpdate is suspended, so this is the only place its
        // contacts can come from. Each body samples its OWN colliders, so Contact.self
        // identifies the body the event belongs to, exactly as it does for an unattached one.
        void UpdateAttachedContacts()
        {
            foreach (KinematicMotion2D attached in attachedMotions)
            {
                if (attached == null)
                {
                    continue;
                }

                attached.UpdateContacts();
                attached.UpdateAttachedContacts();
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

       

        #endregion
        
        #region Overlaps

        

        // Pushes this object out of anything it is overlapping.
        protected void ProcessOverlaps()
        {
            // We probe in multiple directions to find the closest overlap.
            Vector2[] probeDirections = new Vector2[]
            {
                Vector2.right,
                Vector2.left,
                Vector2.up,
                Vector2.down,
                Vector2.up + Vector2.right,
                Vector2.up + Vector2.left,
                Vector2.down + Vector2.right,
                Vector2.down + Vector2.left
            };

            RaycastHit2D hit = new RaycastHit2D(); // If no collision happens, hit stays default.
            contactFilter.layerMask = GetCollisionMask(); // Set Unity layers for collision filtering.
            contactFilter.useLayerMask = true;
            contactFilter.useTriggers = false;
            float closestDistance = float.MaxValue;
            Vector2 closestProbeDirection = Vector2.right;
            bool foundOverlap = false;

            // Look for overlaps in every probe direction
            foreach(Vector2 direction in probeDirections)
            {
                int hitCount = RigidbodyCast(direction, contactFilter, hits, margin - maximumContactOffset);
                for (int i = 0; i < hitCount; i++)
                {
                    float distance = hits[i].distance;

                    // Something resting exactly at our margin is not overlapping us.
                    if (distance + maximumContactOffset >= margin - carryEpsilon)
                    {
                        continue;
                    }

                    // Ignore overlaps if we are acting as the ground to the other object.
                    // If we don't skip this, objects standing on us will impede our motion.
                    // The object standing on us will resolve its own overlap, so this doesn't cause issues.
                    KinematicMotion2D other = hits[i].collider.GetComponent<KinematicMotion2D>();
                    if (other && other.AttachmentRoot.groundMotion == this)
                    {
                        continue;
                    }

                    // Ignore overlaps if we are in the same attachment tree
                    if (IsInSameAttachedSubtree(other))
                    {
                        continue;
                    }

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        hit = hits[i];
                        closestProbeDirection = direction;
                        foundOverlap = true;
                    }
                }
            }

            if (foundOverlap)
            {
                // Adjust the overlap distance to account for Box2D margin
                hit.distance += maximumContactOffset;

                // What did we overlap with?

                // Is this the ground?
                if (IsGroundNormal(hit.normal))
                {
                    // We need to keep the object's x position intact: push straight up.
                    MoveRigidbody(Vector2.up * (margin - hit.distance));
                }
                // Is this a ceiling?
                else if(IsCeilingNormal(hit.normal))
                {
                    // We need to keep the object's x position intact: push straight down.
                    MoveRigidbody(Vector2.down * (margin - hit.distance));
                }
                // Neither ground nor ceiling: this is a wall.
                else
                {
                    // We need to keep the object's y position intact: push straight horizontally.
                    if (closestProbeDirection.y == 0)
                    {
                        MoveRigidbody(closestProbeDirection * -(margin - hit.distance));
                    } else
                    {
                        MoveRigidbody(new Vector2(closestProbeDirection.x * -(margin - hit.distance), 0));
                    }
                }

                // Now let's update the velocity
                velocity = PostCollisionVelocity(velocity, hit);
            }
        }

        #endregion

        #region Movement

        // Minimum follow movement to ignore.
        // Slide ends early only when "distance == 0f" exactly,
        // so even tiny error values like 1e-9 can still trigger a Cast by "margin".
        // When an object is exactly touching a surface, hit.distance is about 0,
        // and subtracting margin can make it negative, causing a bounce in the
        // opposite direction. This threshold prevents that.
        private const float carryEpsilon = 1e-5f;

        private Vector2 lastSlideMovement = Vector2.zero;

        private Vector2 slideStartVelocity = Vector2.zero; // Velocity the current move started with. Collision rules apply to it.
        private void MoveAttachedMotions(Vector2 delta)
        {
            foreach (KinematicMotion2D attached in attachedMotions)
            {
                if (attached == null)
                {
                    continue;
                }

                // Go through the "rigidbody" property rather than the rb field: a body can be
                // attached before its Start() has run (an inactive one, say), and the parent's
                // own movement must not be lost to a null reference thrown from inside its Slide.
                Rigidbody2D attachedBody = attached.rigidbody;
                if (attachedBody != null)
                {
                    attachedBody.position += delta;
                }

                attached.MoveAttachedMotions(delta);
            }
        }

        private void MoveRigidbody(Vector2 delta)
        {
            WillMove?.Invoke(delta);
            
            rb.position += delta;

            // Move attached objects.
            MoveAttachedMotions(delta);
        }

        
        // Moves this object by the specified delta.
        public void MoveBy(Vector2 delta)
        {
            Slide(delta, Vector2.zero);
        }


        // This method performs the core KinematicMotion2D logic.
        // It moves the object up to the distance in the "delta" parameter.
        // If it collides on the way, it changes direction based on the hit surface,
        // and keeps moving along that surface. In other words, it "slides".
        // The "iteration" parameter is how many slide steps happened so far
        // in this single move.
        protected Vector2 Slide(Vector2 delta, Vector2 v, int iterations = 0)
        {
            // First, get movement direction and distance.
            Vector2 direction = delta.normalized;
            float distance = delta.magnitude;
            lastSlideMovement = Vector2.zero;
            if (iterations == 0) slideStartVelocity = v;

            // Ignore very small movement.
            if (distance < minSlideDistance || distance <= 0f)
            {
                return v;
            }

            if (iterations > maxIterations)
            {
                return v;
            }

            // Use our custom method to check if moving this far will hit something.
            RaycastHit2D hit; // If a collision happens, details are stored here.
            bool collided = Cast(direction, distance, out hit); // Returns whether there was a collision.

            if (collided) // Collision happened...
            {
                float contactDistance = Mathf.Max(0f, hit.distance); // Distance to collision point.
                
                // This is a simple check to see if we are wedged.
                if (contactDistance <= carryEpsilon && iterations > 0)
                {
                    return Vector2.zero;
                }

                // We can slide, so calculate remaining movement distance.
                float distanceRemaining = distance - contactDistance;

                // First, move as far as possible without colliding.
                lastSlideMovement = direction * contactDistance;
                MoveRigidbody(lastSlideMovement);
                
                distanceRemaining = ProcessCollision(hit, direction, distanceRemaining);

                // Nothing (or only a rounding residual) is left of this move, so this was not really a collision.
                if (distanceRemaining <= carryEpsilon)
                {
                    return v;
                }

                // Check for two special cases: other KinematicMotion2D and dynamic rigidbodies.
                // We only try to push other objects when we still have slide attempts left,
                // since opening up space we are not allowed to move into is pointless.
                if (iterations < maxIterations)
                {
                    if (hit.rigidbody != null && hit.rigidbody.bodyType == RigidbodyType2D.Kinematic)
                    {
                        KinematicMotion2D otherMotion = hit.rigidbody.gameObject.GetComponent<KinematicMotion2D>();
                        if (otherMotion != null)
                        {
                            // Pushing anything attached pushes its whole group, as one body.
                            otherMotion = otherMotion.AttachmentRoot;
                        }
                        Vector2 remainingDelta = direction * distanceRemaining;

                        if (otherMotion != null && CanPush(otherMotion, remainingDelta))
                        {
                            float totalMass = combinedMass + otherMotion.combinedMass;
                            float massRatio = totalMass > 0 ? combinedMass / totalMass : 0f;
                            // Both of us slow down together: push the other object by our share of the movement,
                            // then follow it by however far it actually moved.
                            Vector2 startPosition = hit.rigidbody.position;
                            otherMotion.Slide(remainingDelta * massRatio, Vector2.zero, 0);
                            Vector2 pushDelta = hit.rigidbody.position - startPosition;
                            distanceRemaining = Mathf.Max(0f, Vector2.Dot(pushDelta, direction));

                            // Follow it into the space it opened up. That space is exactly as large as our
                            // remaining movement, so we ignore the object we just pushed: otherwise rounding
                            // can make us hit it again at zero distance and stop as if we were wedged.
                            // (If it could not move at all, we fall through and collide with it like a wall.)
                            if (distanceRemaining > carryEpsilon)
                            {
                                KinematicMotion2D previousIgnored = ignoredMotion;
                                ignoredMotion = otherMotion;
                                try
                                {
                                    return Slide(direction * distanceRemaining, v, iterations + 1);
                                }
                                finally
                                {
                                    ignoredMotion = previousIgnored;
                                }
                            }
                        }
                        // else
                        // {
                        //     return Slide(direction * distanceRemaining, v, iterations + 1);
                        // }
                    }

                    if (hit.rigidbody != null && hit.rigidbody.bodyType == RigidbodyType2D.Dynamic)
                    {
                        // Cast the dynamic body's collider to find a safe push distance.
                        int dynamicHitCount = hit.collider.Cast(direction, contactFilter, colliderHits, distanceRemaining + margin);
                        float totalMass = combinedMass + hit.rigidbody.mass;
                        float massRatio = totalMass > 0 ? combinedMass / totalMass : 0f;
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
                        // (If it could not move at all, we fall through and collide with it like a wall.)
                        if (pushDistance > carryEpsilon)
                        {
                            return Slide(direction * pushDistance, v, iterations + 1);
                        }
                    }

                    // If we made it this far, this means that we ran into an obstacle.
                    // How we react depends on the kind of surface we hit:
                    //   ・Ground and ceilings never alter horizontal speed.
                    //   ・Walls never alter vertical speed, as long as we are in the air.
                    //   ・Running into a wall while standing on the ground stops us completely.

                    // The movement that is still left to perform in this frame.
                    Vector2 remaining = direction * distanceRemaining;

                    // Calculate the velocity after this collision.
                    float remainingTime = distanceRemaining / v.magnitude; // Time left in this move, measured before v changes.
                    v = PostCollisionVelocity(slideStartVelocity, hit);

                    // If the velocity is zero, or if we have no remaining movement, we can stop immediately.
                    if (v == Vector2.zero || remaining.magnitude < minSlideDistance)
                    {
                        return v;
                    }

                    // There is still movement left to perform.

                    // Hitting a ceiling, or the ground: keep horizontal speed unchanged.
                    if (IsGroundNormal(hit.normal) || IsCeilingNormal(hit.normal))
                    {
                        if (Mathf.Abs(v.x) > float.Epsilon)
                        {
                            remaining = v * remainingTime;
                            return Slide(remaining, v, iterations + 1); 
                        } else
                        {
                            return Vector2.zero;
                        }
                    }

                    // Hitting a wall while in the air: keep vertical speed unchanged.
                    else
                    {
                        if (Mathf.Abs(v.y) > float.Epsilon)
                        {
                            remaining = v * remainingTime;
                            return Slide(remaining, v, iterations + 1);
                        } else
                        {
                            return Vector2.zero;
                        }
                    }
                }
            }
            else
            {
                // No collision happened, so move the object.
                MoveRigidbody(delta);
                lastSlideMovement = delta;
            }

            return v;
        }

        // Adds an impulse to the body. If the body has a parent, the impulse is passed up to the parent.
        public void AddImpulse(Vector2 impulse)
        {
            if (parent != null)
            {
                parent.AddImpulse(impulse);
            } else
            {
                velocity += impulse;
            }
        }

        #endregion

        #region Rigidbody2D

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


        // Reference to components used by this script.
        protected Rigidbody2D rb;

        protected int RigidbodyCast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] hits, float distance, bool useAttachedColliders = true)
        {
            int totalHits = 0;
            IEnumerable<Collider2D> colls = useAttachedColliders ? (IEnumerable<Collider2D>)colliders : _ownColliders;
            if (colls == null)
            {
                // Neither list exists until Start() has run.
                return 0;
            }
            foreach(Collider2D coll in colls)
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

        // Returns whether "other" is a one-way platform that we are currently allowed
        // to pass through while travelling in "direction".
        //
        // "touching" allows a contact that is already at zero distance to be ignored,
        // so we do not get pushed back the moment we start entering the platform.
        protected static bool IsOneWayPassThrough(Collider2D other, Vector2 direction, bool touching = false)
        {
            if (other == null)
            {
                return false;
            }

            PlatformEffector2D effector = other.GetComponent<PlatformEffector2D>();
            if (effector == null || !effector.useOneWay)
            {
                return false;
            }

            if (direction == Vector2.zero)
            {
                return false;
            }

            // Effector's "up" direction, including rotational offset.
            Vector2 up = Quaternion.Euler(0, 0, effector.rotationalOffset) * effector.transform.up;

            // Is this direction within the one-way surface arc?
            bool inOneWayArc = Vector2.Angle(up, direction) <= effector.surfaceArc * 0.5f;

            float dot = Vector2.Dot(direction, up);

            // Only pass through if we are inside the arc AND moving the "through" direction.
            return inOneWayArc && (dot > 0f || touching);
        }

        // This is another important method in this component.
        // It checks whether moving in direction for distance will hit something.
        // It returns whether there was a collision. If there was,
        // hit stores the collision details.
        public bool Cast(Vector2 direction, float distance, out RaycastHit2D hit)
        {
            // Add gap margin to movement distance.
            distance += margin;
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
                float nearestDistance = distance - margin;
                
                // Check hit colliders one by one and find the nearest contact.
                for (int i = 0; i < hitCount; i++)
                {
                    if (IsOneWayPassThrough(hits[i].collider, direction, hits[i].distance < 0.0001f))
                    {
                        continue;
                    }

                    KinematicMotion2D otherMotion = hits[i].collider.GetComponentInParent<KinematicMotion2D>();
                    if (otherMotion != null &&
                        // If this side is the ground, do not treat as a collision.
                        (otherMotion.AttachmentRoot.groundMotion == this ||
                        // Ground being followed has not moved yet, so do not treat as a collision.
                         otherMotion.AttachmentRoot == ignoredMotion))
                    {
                        continue;
                    }

                    // We should not collide with an object attached to us, or with the object we are attached to,
                    // or with an object attached to the same object we are attached to.
                    if (IsInSameAttachedSubtree(otherMotion))
                    {
                        continue;
                    }

                    // We now need to adjust the distance to account for the margin.
                    float pushDistance = margin / Mathf.Abs(Vector2.Dot(hits[i].normal, direction));
                    float safeDistance = hits[i].distance - pushDistance * (1f - maximumContactOffset / margin);
                    if (safeDistance >= nearestDistance)
                    {
                        continue;
                    }

                    // This is now the nearest contact found, so store its details.
                    hit = hits[i]; // Store collision details.
                    hit.distance = safeDistance;
                    nearestDistance = safeDistance;

                    collided = true; // Record that a collision happened.
                }
            }

            return collided; // Return whether a collision happened.
        }

        Vector2 PostCollisionVelocity(Vector2 velocity, RaycastHit2D hit)
        {
            Vector2 v = velocity;
            if (IsCeilingNormal(hit.normal) || (IsGroundNormal(hit.normal) && isGrounded))
            {
                // Case #1: Hitting the ceiling, or hitting the ground while already grounded.
                // We conserve the horizontal velocity and orient velocity parallel to the surface.
                v.y = v.x * hit.normal.x / -hit.normal.y;
            }
            else if (IsGroundNormal(hit.normal) && !isGrounded)
            {
                // Case #2: We hit the ground while not grounded: conserve only the horizontal velocity.
                v.y = v.x * hit.normal.x / -hit.normal.y;
            }
            else if (isGrounded)
            {
                // Case #3: We hit a wall while grounded.
                v = Vector2.zero;
            }
            else
            {
                // Case #4: We hit a wall and we are not grounded
                // We conserve the vertical velocity and orient velocity parallel to the surface.
                v.x = v.y * hit.normal.y / -hit.normal.x;
            }

            return v;
        }

        


        #endregion


        #region MonoBehaviour

        // Remember when the state was updated. We can use this to verify the order in which
        // two KinematicMotion2D instances were processed.
        float updateTime = 0f;

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

            UpdateColliders();

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

        protected virtual void FixedUpdate()
        {
            updateTime = Time.fixedTime;
            float deltaSeconds = Time.fixedDeltaTime;

            if (!simulatePhysics)
            {
                return;
            }

            if (parent != null)
            {
                // When we are attached to another object, all physics processing
                // is handled by the parent. Before we return, let's store the parent's velocity as our own
                velocity = parent.velocity;
                return;
            }
            
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

            if (isGrounded && stickyGroundThreshold > 0f)
            {
                float dot = Vector2.Dot(groundNormal, velocity.normalized);
                if (Mathf.Abs(dot) < stickyGroundThreshold)
                {
                    velocity = Mathf.Sign(velocity.x) * groundRight * velocity.magnitude;
                }
            }

            if (useGravity && (!isGrounded || groundDistance > margin)) // Should gravity be applied?
            {
                // Accelerate in the gravity direction.
                // "Time.fixedDeltaTime" stores elapsed time since the previous FixedUpdate.
                velocity += Physics2D.gravity * deltaSeconds * gravityMultiplier * gravityModifier;
            }

            if (isGrounded && friction > 0f)
            {
                // Apply friction. While grounded, our velocity is relative to the ground, so friction
                // brings it to zero: we come to rest on the ground, and then move along with it.
                // Both sides need friction; they combine as the square root of their product. Static
                // ground has no friction of its own, so defaultGroundFriction stands in for it.
                float totalFriction = Mathf.Sqrt(friction * (groundMotion ? groundMotion.friction : defaultGroundFriction));
                float deceleration = totalFriction * Physics2D.gravity.magnitude;
                velocity.x = Mathf.MoveTowards(velocity.x, 0f, deceleration * deltaSeconds);
            }

            // Check speed limits.
            if (Mathf.Abs(velocity.x) > maxSpeedSide)
            {
                // Over the limit, so clamp to max speed.
                velocity.x = Mathf.Sign(velocity.x) * maxSpeedSide;
            }

            velocity.y = Mathf.Clamp(velocity.y, -Mathf.Abs(maxSpeedDown), maxSpeedUp);

            // If we are grounded, vertical velocity should be 0
            if (isGrounded)
            {
                velocity = groundRight * (velocity.x / groundRight.x);
            }

            // Calculate move distance for this frame.
            Vector2 motion = velocity * deltaSeconds;

            // Store position before movement.
            Vector2 startPosition = rb.position;      

            // This is where movement is actually applied
            velocity = Slide(motion, velocity);

            // After movement, check to see if we are overlapping with anything, and resolve any overlap if needed.
            ProcessOverlaps();
        
            if (isGrounded)
            {
                // Store speed while grounded. This is needed for player air movement logic.
                lastGroundVelocity = velocity + groundVelocity;
            }

            UpdateContacts();
            UpdateAttachedContacts();

            UpdateCompleted?.Invoke(this);

            // Debug.Log($"{name}, time: {updateTime}, p: {rb.position:F6}, v: {velocity:F6}, isGrounded: {isGrounded}, groundVelocity: {groundVelocity:F6}");
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

            // We also need to detach our children
            while(attachedMotions.Count > 0)
            {
                attachedMotions[0].Detach();
            }
        }

        public void LateUpdate()
        {
            
        }

        #endregion

    }
} // namespace
