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


        // Whether to forcibly separate overlapping objects.
        public bool separateOverlaps = true;

        // How we get out of an object we have ended up inside of.
        //
        // When an overlap has to be resolved, the hard part is not how far to move but
        // which WAY to move. To answer that, we check four thin zones just inside the
        // edges of our own collider - up, right, down and left - and see which of them
        // are inside something else. A body sunk into the floor has its "down" zone
        // buried and the other three clear, so the way out is up.
        //
        // This value is the thickness of those zones, and it is also the distance we move
        // per physics step while climbing out. Larger values detect deeper overlaps in one
        // check and escape faster, but a body will visibly pop out of walls. Smaller
        // values are gentler, but take more steps to escape a deep overlap.
        //
        // (Only used when separateOverlaps is turned on.)
        [Min(0f)]
        public float overlapMargin = 0.02f;

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

        protected virtual void Landed(float speed) { }

        protected virtual void Fell() { }

        
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

        public int attachedCount => attachedMotions.Count;
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

        protected RaycastHit2D[] hits = new RaycastHit2D[8];
        protected RaycastHit2D[] colliderHits = new RaycastHit2D[8];
        protected Collider2D[] overlapColliders = new Collider2D[8];
        protected Collider2D[] overlaps = new Collider2D[8];

        // Results of the edge-zone overlap checks. This needs a buffer of its own: the
        // zone checks run while a RigidbodyOverlap result is being walked, and sharing
        // "overlapColliders" or "overlaps" would overwrite the very list being read.
        protected Collider2D[] zoneResults = new Collider2D[8];
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

        // Which edge of our own bounding box is buried inside another collider.
        // These are flags, so several of them can be set at once: a body wedged into
        // the corner between the floor and a wall has both Down and (say) Right set.
        [Flags]
        private enum OverlapEdge
        {
            None = 0,
            Up = 1,
            Right = 2,
            Down = 4,
            Left = 8
        }

        // The result of one pass of edge-zone checks.
        private struct OverlapVerdict
        {
            // Every edge found buried, across every collider in our footprint.
            public OverlapEdge edges;

            // The obstacle we are buried in. Used to build the "crushed" notification,
            // and to decide whether the obstacle should be pushed instead of us.
            public Collider2D blockingCollider;

            // Which of OUR colliders is the one that is buried. With a merged footprint
            // this may belong to an attached body rather than to us, and that body has to
            // be the one that hears about a crush.
            public Collider2D penetratedCollider;
        }

        // The overlap zone thickness actually used.
        //
        // overlapMargin is clamped here rather than trusted directly, because a field
        // added to this class after a prefab was saved deserializes to zero on that
        // prefab: Unity does not run field initializers for already-serialized objects,
        // and [Min] only clamps edits made in the Inspector. A zero-thickness zone would
        // detect nothing at all, so we never go below the contact offset.
        // (CrushPenetrationTolerance clamps itself the same way.)
        private float EffectiveOverlapMargin
        {
            get
            {
                return Mathf.Max(overlapMargin, maximumContactOffset);
            }
        }

        // How far inside the edge each zone starts.
        //
        // Box2D inflates every shape it tests by its "polygon radius", so a zone placed
        // flush against the edge would report a hit on a surface we are merely TOUCHING,
        // not penetrating. Starting the zone this far in means only real penetration
        // registers, which is what keeps a body resting normally on the ground - sitting
        // a "margin" clear of it - from looking like it is buried in it.
        private static float OverlapZoneInset
        {
            get
            {
                return maximumContactOffset * 2f;
            }
        }

        // Builds one of the four edge zones for a collider's world bounding box.
        //
        // Each zone is a short, thin strip lying just inside the middle of one edge, and
        // every zone is completely contained in the bounding box.
        //
        //        +------------------+
        //        |      #####       |     thickness of each strip: m
        //        |  #           #   |     distance from the edge:  inset
        //        |  #           #   |     length: half of that edge
        //        |      #####       |
        //        +------------------+
        //
        // The strips deliberately sample only the middle half of each edge, and stop well
        // short of the corners. Running them the full length of the edge makes the readings
        // alias badly: a body sunk into the floor would have its left and right strips dip
        // into that same floor, and a body embedded in a wall would have its top and bottom
        // strips reach into that same wall. Keeping the strips short and central means each
        // one reports mostly on the side it belongs to.
        private static void GetEdgeZone(Bounds bounds, OverlapEdge edge, float m, float inset,
                                        out Vector2 center, out Vector2 size)
        {
            Vector2 c = bounds.center;
            Vector2 e = bounds.extents;

            // Length of each strip along its long axis: half the length of its edge,
            // which leaves a clear quarter at each end.
            float spanX = e.x;
            float spanY = e.y;

            switch (edge)
            {
                case OverlapEdge.Up:
                    center = new Vector2(c.x, c.y + e.y - inset - m * 0.5f);
                    size = new Vector2(spanX, m);
                    break;

                case OverlapEdge.Down:
                    center = new Vector2(c.x, c.y - e.y + inset + m * 0.5f);
                    size = new Vector2(spanX, m);
                    break;

                case OverlapEdge.Left:
                    center = new Vector2(c.x - e.x + inset + m * 0.5f, c.y);
                    size = new Vector2(m, spanY);
                    break;

                default: // OverlapEdge.Right
                    center = new Vector2(c.x + e.x - inset - m * 0.5f, c.y);
                    size = new Vector2(m, spanY);
                    break;
            }
        }

        // Is this collider something we should actually be pushed out of?
        //
        // Excludes our own colliders, everything in the same attached subtree (a body and
        // the things attached to it are one rigid body, so it cannot be buried in itself),
        // and one-way platforms we are deliberately passing through. That last exclusion
        // is essential: Cast already allowed the movement into the platform, so pushing
        // back out of it here would cancel that movement out, and the platform would look
        // like a solid floor.
        private bool IsForeignObstacle(Collider2D other, Vector2 travelDirection)
        {
            if (other == null || other.isTrigger)
            {
                return false;
            }

            if (other.attachedRigidbody != null && other.attachedRigidbody == rigidbody)
            {
                return false;
            }

            if (IsInSameAttachedSubtree(other.GetComponentInParent<KinematicMotion2D>()))
            {
                return false;
            }

            if (IsOneWayPassThrough(other, travelDirection))
            {
                return false;
            }

            return true;
        }

        // Checks all four edge zones of every collider in our footprint, and reports which
        // edges are buried.
        //
        // This is the heart of the overlap handling. Rather than asking the physics engine
        // "which way is the shortest way out of this collider?" - a question whose answer
        // depends on the obstacle's shape, and which is wrong as often as it is right - we
        // ask four cheap box queries "which of MY edges is inside something?". The answer
        // does not depend on what the obstacle is made of, which is why this works equally
        // well against boxes, polygons, composite colliders and tilemaps.
        private OverlapVerdict FindBuriedEdges()
        {
            OverlapVerdict verdict = new OverlapVerdict();

            if (colliders == null)
            {
                // The collider list does not exist until Start() has run.
                return verdict;
            }

            contactFilter.layerMask = GetCollisionMask(); // Prepare Unity layers to include/exclude collisions.
            contactFilter.useLayerMask = true;
            contactFilter.useTriggers = false;

            Vector2 travelDirection = velocity.normalized;
            float inset = OverlapZoneInset;

            foreach (Collider2D coll in colliders)
            {
                if (coll == null || coll.isTrigger)
                {
                    continue;
                }

                Bounds bounds = coll.bounds;

                // Keep the strips inside the box however thin the collider is. A collider
                // too small to hold a strip at all is skipped.
                float m = Mathf.Min(EffectiveOverlapMargin,
                                    (bounds.extents.x - inset) * 0.5f,
                                    (bounds.extents.y - inset) * 0.5f);
                if (m <= 0f)
                {
                    continue;
                }

                for (int i = 0; i < 4; i++)
                {
                    OverlapEdge edge = (OverlapEdge)(1 << i);

                    Vector2 center;
                    Vector2 size;
                    GetEdgeZone(bounds, edge, m, inset, out center, out size);

                    // The zones are always axis-aligned, hence the zero angle.
                    int count = Physics2D.OverlapBox(center, size, 0f, contactFilter, zoneResults);

                    for (int j = 0; j < count; j++)
                    {
                        if (!IsForeignObstacle(zoneResults[j], travelDirection))
                        {
                            continue;
                        }

                        verdict.edges |= edge;

                        // Remember the first real obstacle we meet, so that a crush can
                        // name both what crushed us and which part of us it caught.
                        if (verdict.blockingCollider == null)
                        {
                            verdict.blockingCollider = zoneResults[j];
                            verdict.penetratedCollider = coll;
                        }

                        break; // This edge is buried. No need to look at the rest.
                    }
                }
            }

            return verdict;
        }

        // Picks which way to push, given the set of buried edges.
        // Also reports whether the body is "pinned", meaning there is no way out at all.
        //
        // A direction is only worth taking if the edge we would leave by is CLEAR while the
        // edge opposite it is buried. Just asking "is my bottom edge buried?" is not enough,
        // because each zone runs most of the length of its edge and therefore catches any
        // obstacle the body is deeply inside of, whichever side that obstacle is on.
        //
        // Two examples show why the far side has to be consulted. A body sunk into the floor
        // has its bottom edge buried, but the lower parts of its left and right edges are
        // inside that same floor, so those two zones report buried as well. A body embedded
        // sideways in a tall wall has its right edge buried, and the right-hand parts of its
        // top and bottom edges are inside that same wall, so those report buried too.
        //
        //     sunk into the floor          embedded in a wall       squeezed by two walls
        //      +--------+                   +------+##               #+--------+#
        //      |        |                   |      |##               #|        |#
        //   ===|========|===                |      |##               #|        |#
        //   ###|########|###                +------+##               #+--------+#
        //   ####+------+####
        //    Down, Left, Right              Right, Up, Down              Left, Right
        //    Up is clear -> go up           Left is clear -> go left     nothing is clear
        //                                                                -> pinned, crushed
        //
        // In each case exactly the right answer falls out of one rule: escape towards the
        // clear side of a buried axis. Vertical is tested first, so a body wedged into the
        // corner between the floor and a wall lifts out of the floor before it deals with
        // the wall - which is nearly always the correction a platform game wants.
        private static Vector2 ChooseEscapeDirection(OverlapEdge edges, out bool pinned)
        {
            bool up = (edges & OverlapEdge.Up) != 0;
            bool down = (edges & OverlapEdge.Down) != 0;
            bool left = (edges & OverlapEdge.Left) != 0;
            bool right = (edges & OverlapEdge.Right) != 0;

            pinned = false;

            // Vertical first.
            if (down && !up)
            {
                return Vector2.up;
            }

            if (up && !down)
            {
                return Vector2.down;
            }

            if (right && !left)
            {
                return Vector2.left;
            }

            if (left && !right)
            {
                return Vector2.right;
            }

            // Either nothing is buried at all, or every way out is blocked by the far side
            // of the very axis we would have escaped along.
            pinned = edges != OverlapEdge.None;
            return Vector2.zero;
        }

        // Moves this body, and everything attached below it, by delta.
        //
        // The position is written directly and transforms are synced straight away, so
        // that any further query in this same frame sees the new position. Going through
        // MovePosition instead would defer the move to the next physics step, and every
        // check made in between would still see the old, overlapping position.
        private void StepOutOfOverlap(Vector2 delta)
        {
            rigidbody.position += delta;
            MoveAttachedMotions(delta);
            Physics2D.SyncTransforms();
        }

        // Handles the overlaps the edge zones cannot see.
        //
        // Each zone covers only the middle half of its edge, and only the outer
        // "overlapMargin" of the body's depth, so a shallow overlap confined to a corner -
        // the quarter of each edge no strip reaches - leaves all four zones clear even
        // though an overlap exists.
        //
        // That is a case where the shortest way out really is the sensible way out, the
        // penetration being shallow by definition, so here we do fall back to asking the
        // physics engine for a direction - one step at a time, like every other case.
        //
        // Note that an obstacle lying through the MIDDLE of the body is not handled here.
        // It buries the left and right zones while leaving the top and bottom clear, which
        // is indistinguishable from a body squeezed between two walls, and is reported as
        // being pinned. For a body spawned that way that is the right outcome anyway: it
        // never resolves, so it earns the bad-placement warning.
        private void StepOutOfBlindOverlap()
        {
            if (colliders == null)
            {
                return;
            }

            contactFilter.layerMask = GetCollisionMask();
            contactFilter.useLayerMask = true;
            contactFilter.useTriggers = false;

            Vector2 travelDirection = velocity.normalized;

            int hitCount = RigidbodyOverlap(contactFilter, overlaps);
            for (int i = 0; i < hitCount; i++)
            {
                Collider2D other = overlaps[i];
                if (!IsForeignObstacle(other, travelDirection))
                {
                    continue;
                }

                foreach (Collider2D coll in colliders)
                {
                    if (coll == null || coll.isTrigger || coll == other)
                    {
                        continue;
                    }

                    ColliderDistance2D separation = coll.Distance(other);
                    if (separation.isValid && separation.isOverlapped)
                    {
                        // "normal" points from our collider towards the other one, so the
                        // way out is the opposite direction.
                        StepOutOfOverlap(-separation.normal * EffectiveOverlapMargin);
                        return;
                    }
                }
            }
        }

        // Number of frames an inescapable overlap must persist before we judge "crushed".
        // This keeps a single frame of a transient wedge - two moving objects passing
        // through the same spot, say - from being reported as a crush.
        private const int crushPersistenceFrames = 2;

        // Number of consecutive frames with no way out of an overlap.
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

        // Find the deepest overlap that still remains.
        //
        // This no longer decides whether we are crushed - the edge zones answer that
        // directly. It is kept for two narrower jobs: telling whether we are overlapping
        // anything at all (which separates a crush from a bad spawn), and supplying the
        // contact point and normal that the crush notification carries.
        //
        // "penetratedCollider" reports which of OUR colliders is the one dug into. With a
        // merged footprint that collider may belong to an attached body rather than to this
        // one, and the crush event has to reach whichever body owns it.
        private bool FindUnresolvedOverlap(out Collider2D blockingCollider, out ColliderDistance2D deepest, out Collider2D penetratedCollider)
        {
            blockingCollider = null;
            penetratedCollider = null;
            deepest = new ColliderDistance2D();

            float worst = -CrushPenetrationTolerance;

            contactFilter.layerMask = GetCollisionMask();
            contactFilter.useLayerMask = true;
            contactFilter.useTriggers = false;

            Vector2 travelDirection = velocity.normalized;

            int hitCount = RigidbodyOverlap(contactFilter, overlaps);
            for (int i = 0; i < hitCount; i++)
            {
                Collider2D other = overlaps[i];

                // Our own colliders, our attached subtree, and one-way platforms we are
                // passing through are excluded for the same reasons as in the zone pass.
                if (!IsForeignObstacle(other, travelDirection))
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
                        penetratedCollider = coll;
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

        // Decides whether this object is "crushed", and notifies if so.
        //
        // With edge zones this is a direct geometric question instead of a guess, and
        // ChooseEscapeDirection has already answered it: "pinned" means the body is buried
        // on both sides of the axis it would have escaped along, so there is no room to go
        // anywhere. Being pinned horizontally counts just as much as vertically - a
        // character squeezed between a wall and something moving towards it is crushed,
        // even though nothing is stopping it from moving up or down.
        //
        // The old test - "still overlapping after a couple of frames" - cannot be used any
        // more, because overlaps are now resolved one small step per frame. A deep but
        // perfectly escapable overlap legitimately stays unresolved for many frames, and
        // would have been reported as a crush every single time.
        private void HandleUnresolvedOverlaps(OverlapVerdict verdict, bool pinned)
        {
            Collider2D blockingCollider;
            ColliderDistance2D separation;
            Collider2D penetratedCollider;

            if (!FindUnresolvedOverlap(out blockingCollider, out separation, out penetratedCollider))
            {
                // We reached a no-overlap state, so anything that happens from here on is
                // something being done to us, not a mistake in where we were placed.
                didEverResolveOverlaps = true;
            }

            if (!pinned)
            {
                // There is a way out, whether or not we have finished taking it.
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

            // The zone pass knows exactly which pair of colliders is involved, so prefer
            // its answer, and fall back to the distance query only if it has none.
            if (verdict.blockingCollider != null)
            {
                blockingCollider = verdict.blockingCollider;
                penetratedCollider = verdict.penetratedCollider;

                if (penetratedCollider != null)
                {
                    separation = penetratedCollider.Distance(blockingCollider);
                }
            }

            if (blockingCollider == null || !separation.isValid)
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
                        $" (Position: {rigidbody.position}) Please check the spawn position.",
                        this);
                }

                return;
            }

            if (!crushReported)
            {
                crushReported = true;
                CrushedBy(BuildCrushContact(blockingCollider, separation));

                // The penetrated collider may belong to an attached body rather than to this
                // one. That body's own simulation is suspended, so this pass is the only place
                // it can hear that it is being crushed. Its copy is built from its own frame of
                // reference, so Contact.self and relativeVelocity describe that body.
                //
                // Its crushReported flag is deliberately left alone: this branch already fires
                // once per crush episode, and setting a flag nothing will ever clear (its own
                // HandleUnresolvedOverlaps does not run while attached) would suppress a real
                // crush later, after it is detached.
                KinematicMotion2D owner = penetratedCollider != null
                    ? penetratedCollider.GetComponentInParent<KinematicMotion2D>()
                    : null;

                if (owner != null && owner != this)
                {
                    owner.CrushedBy(owner.BuildCrushContact(blockingCollider, separation));
                }
            }
        }

        // Pushes this object out of anything it is overlapping, by one small step.
        //
        // The work is split in two: the edge zones decide WHICH WAY to go, and this method
        // takes a single step of "overlapMargin" in that direction. One step per frame is
        // enough - a deep overlap is an error state being recovered from, not something to
        // fix instantly - and stepping out gradually never shoves an object across the level
        // in a single frame the way a one-shot correction can.
        protected void ProcessOverlaps()
        {
            OverlapVerdict verdict = FindBuriedEdges();

            bool pinned;
            Vector2 direction = ChooseEscapeDirection(verdict.edges, out pinned);

            if (direction != Vector2.zero)
            {
                float step = EffectiveOverlapMargin;

                // Who gives way? If the obstacle is a KinematicMotion2D that yields to us,
                // it is pushed out of us instead of the other way round. The direction is
                // simply reversed: it goes the way we would have gone, backwards.
                KinematicMotion2D otherMotion = verdict.blockingCollider != null
                    ? verdict.blockingCollider.GetComponentInParent<KinematicMotion2D>()
                    : null;

                if (otherMotion != null && otherMotion.pushPriority < pushPriority)
                {
                    otherMotion.StepOutOfOverlap(-direction * step);
                }
                else
                {
                    StepOutOfOverlap(direction * step);
                }
            }
            else if (verdict.edges == OverlapEdge.None)
            {
                // No edge is buried, but we may still be overlapping something the zones
                // cannot see. (If an edge IS buried and we got no direction, then we are
                // pinned, and there is nowhere to step to.)
                StepOutOfBlindOverlap();
            }

            HandleUnresolvedOverlaps(verdict, pinned);
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

        // When we collide with a surface, we follow that surface by scaling its direction
        // so that one of its components keeps the value it had before the collision.
        // (Which component depends on the kind of surface. See Slide.)
        // That means dividing by one component of the surface direction, so we need to make
        // sure that component is not (nearly) zero. This is the threshold used for that check.
        private const float tangentEpsilon = 1e-5f;

        // Use this to make sure the surface we collide with is actually facing against 
        // the direction of movement.
        private const float contactDirectionEpsilon = -0.0001f;

        private Vector2 lastSlideMovement = Vector2.zero;

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

            // Ignore very small movement.
            if (distance < minSlideDistance || distance <= 0f)
            {
                return v;
            }

            // Use our custom method to check if moving this far will hit something.
            RaycastHit2D hit; // If a collision happens, details are stored here.
            bool collided = Cast(direction, distance, out hit); // Returns whether there was a collision.

            if (collided) // Collision happened...
            {
                float contactDistance = Mathf.Max(0f, hit.distance); // Distance to collision point.

                // We can slide, so calculate remaining movement distance.
                float distanceRemaining = distance - contactDistance;

                // First, move as far as possible without colliding.
                lastSlideMovement = direction * contactDistance;
                MoveRigidbody(lastSlideMovement);
                
                distanceRemaining = ProcessCollision(hit, direction, distanceRemaining);

                if (distanceRemaining <= 0)
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
                        Vector2 remainingDelta = direction * distanceRemaining;

                        if (otherMotion != null && CanPush(otherMotion, remainingDelta))
                        {
                            float totalMass = combinedMass + otherMotion.combinedMass;
                            float massRatio = totalMass > 0 ? combinedMass / totalMass : 0f;
                            Vector2 startPosition = hit.rigidbody.position;
                            otherMotion.Slide(remainingDelta, Vector2.zero, 0);
                            Vector2 pushDelta = hit.rigidbody.position - startPosition;
                            distanceRemaining = pushDelta.magnitude * massRatio;
                        }

                        return Slide(direction * distanceRemaining, v, iterations + 1);
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
                        return Slide(direction * pushDistance, v, iterations + 1);
                    }
                }

                // If we made it this far, this means that we ran into an obstacle.
                // How we react depends on the kind of surface we hit:
                //   ・Ground and ceilings never alter horizontal speed.
                //   ・Walls never alter vertical speed, as long as we are in the air.
                //   ・Running into a wall while standing on the ground stops us completely.

                // The direction along the surface we hit. This is the surface normal rotated
                // by 90 degrees, so it always points "right" relative to that normal.
                Vector2 hitDirection = new Vector2(hit.normal.y, -hit.normal.x);

                // The movement that is still left to perform in this frame.
                Vector2 remaining = direction * distanceRemaining;

                // Velocity and movement after the collision is resolved.
                Vector2 slideVelocity;
                Vector2 slideMotion;

                if (IsGroundNormal(hit.normal) || IsCeilingNormal(hit.normal))
                {
                    // Colliding with the ground or with a ceiling must never alter horizontal
                    // speed. So we keep the horizontal component of the velocity exactly as it
                    // is, and replace the vertical component with whatever value makes the
                    // movement parallel to the surface.

                    // Ground and ceiling surfaces are never vertical, so hitDirection.x should
                    // never be zero here. We check anyway, because very large values of
                    // maxGroundAngleDegrees or maxCeilingAngleDegrees can classify a
                    // near-vertical surface as ground or ceiling. We cannot follow such a
                    // surface without altering horizontal speed, so we stop instead.
                    if (Mathf.Abs(hitDirection.x) < tangentEpsilon)
                    {
                        return Vector2.zero;
                    }

                    // Scaling hitDirection this way makes the x component of the result exactly
                    // equal to v.x, whichever way the surface is leaning. Note that this also
                    // handles a horizontal speed of zero correctly: we simply stop, because a
                    // horizontal speed of zero has to stay zero.
                    slideVelocity = hitDirection * (v.x / hitDirection.x);

                    // The movement that is left for this frame is redirected in the same way:
                    // its horizontal part is preserved and its vertical part follows the surface.
                    slideMotion = hitDirection * (remaining.x / hitDirection.x);
                }
                else if (isGrounded)
                {
                    // We ran into a wall while standing on the ground.
                    // There is nowhere left to slide to, so we stop and clear the velocity.
                    return Vector2.zero;
                }
                else
                {
                    Debug.Log($"Hit a wall: velocity = {v}, iteration = {iterations}, hit distance = {hit.distance}, dot: {Vector2.Dot(hit.normal, direction)}");
                    // We ran into a wall while in the air. This must never alter vertical speed,
                    // so this case is the mirror image of the one above: we keep the vertical
                    // component of the velocity and replace the horizontal one.

                    // Walls are never horizontal, so hitDirection.y should never be zero here.
                    // As above, extreme angle settings could still make it happen.
                    if (Mathf.Abs(hitDirection.y) < tangentEpsilon)
                    {
                        return Vector2.zero;
                    }

                    slideVelocity = hitDirection * (v.y / hitDirection.y);
                    slideMotion = hitDirection * (remaining.y / hitDirection.y);
                }

                // Keep moving along the surface, if we are still allowed to.
                //
                // This part uses a technique that is often hard for beginners:
                // a recursive method (a method that calls itself).
                // Up to here, we hit something and moved as far as possible.
                // We want to keep moving in the new direction, but we may hit
                // something again. To handle that, we run Slide again from the start.
                // Recursion can loop forever, so we must track the repeat count and
                // stop after a fixed maximum, like the check below does.
                //
                // Note that we let Slide decide whether the remaining movement is worth
                // performing: when there is (almost) nothing left to move, it simply
                // returns the velocity we pass to it, unchanged.
                if (iterations < maxIterations)
                {
                    return Slide(slideMotion, slideVelocity, iterations + 1);
                }

                // We ran out of slide attempts. We still report the velocity that the surface
                // allows, because otherwise the object would keep the speed it had before the
                // collision and drive straight into the surface again on the next frame.
                return slideVelocity;
            }
            else
            {
                // No collision happened, so move the object.
                MoveRigidbody(delta);
                lastSlideMovement = delta;
                return v;
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

        // Variables needed for collision checks.
        // They are declared outside methods for better performance.
        
          protected Vector2 positionAdjustment = Vector2.zero;
    
        protected int RigidbodyOverlap(ContactFilter2D contactFilter, Collider2D[] overlaps)
        {
            int totalHits = 0;
            if (colliders == null)
            {
                return 0;
            }
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
        // This has to be consulted everywhere we resolve collisions, not only in Cast.
        // A pass-through leaves us overlapping the platform, and overlap resolution
        // (ProcessOverlaps) would otherwise push us straight back out,
        // undoing the movement Slide just performed.
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

                    if (IsOneWayPassThrough(hits[i].collider, direction, hits[i].distance < 0.0001f))
                    {
                        continue;
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

                    // An attached subtree moves as one rigid body, so it must not collide with
                    // itself. The query casts every collider in the subtree, which means a
                    // child's collider can report a hit on its own parent just as easily as the
                    // other way round - both directions have to be excluded, not just one.
                    if (IsInSameAttachedSubtree(otherMotion))
                    {
                        continue;
                    }

                    // We need to check the normal of the surface against the direction of movement.
                    // When sliding along a wall, it is possible to get stuck if the downward motion
                    // intersects with the wall.
                    float dot = Vector2.Dot(hit.normal, direction);
                    if (dot > contactDirectionEpsilon)
                    {
                        continue; // Ignore this collision
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

        


        #endregion


        #region MonoBehaviour

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
            float deltaSeconds = Time.fixedDeltaTime;

            if (!simulatePhysics)
            {
                return;
            }

            if (parent != null)
            {
                // When we are attached to another object, all physics processing
                // is handled by the parent.
                return;
            }

            if (separateOverlaps)
            {
                ProcessOverlaps();
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

            bool usedGravity = false;
            if (useGravity && (!isGrounded || groundDistance > margin)) // Should gravity be applied?
            {
                // Accelerate in the gravity direction.
                // "Time.fixedDeltaTime" stores elapsed time since the previous FixedUpdate.
                velocity += Physics2D.gravity * deltaSeconds * gravityMultiplier * gravityModifier;
                usedGravity = true;
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
                velocity = groundRight * Vector2.Dot(groundRight, velocity);
            }

           
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
            Vector2 verticalMotion = motion - horizontalMotion;
            // Vector2 horizontalMotion = new Vector2(motion.x, 0);
            // Vector2 verticalMotion = new Vector2(0, motion.y);            

            positionAdjustment = Vector2.zero;

            // if (Mathf.Sign(motion.y) != GravityDirection)
            // {
            //     // Going against the direction of gravity: move vertically, then horizontally.
            //     Slide(verticalMotion);
            //     Slide(horizontalMotion);
            // } else
            // {
            //     // Going in the same direction as gravity: move horizontally, then vertically.
            //     Slide(horizontalMotion);
            //     Slide(verticalMotion);
            // }

            // Vector2 v = Vector2.zero;
            // collidedWithWall = false;
            // Slide(horizontalMotion);
            // if (!collidedWithWall)
            // {
            //     v += horizontalMotion;
            // }
            // collidedWithWall = false;
            // Slide(verticalMotion);
            // if (!collidedWithWall)
            // {
            //     v += verticalMotion;
            // }

            // velocity = v / deltaSeconds;
            velocity = Slide(motion, velocity);
            


            if (isGrounded)
            {
                // Store speed while grounded. This is needed for player air movement logic.
                lastGroundVelocity = velocity;
            }

            UpdateContacts();
            UpdateAttachedContacts();

            Debug.DrawLine(rb.position, startPosition, Color.red, 3);
            Debug.DrawRay(rb.position, velocity.normalized, usedGravity ? (isGrounded ? Color.orange : Color.purple) : Color.green, 3);
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
