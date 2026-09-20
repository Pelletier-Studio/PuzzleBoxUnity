/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
 
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PuzzleBox
{
    /**
     * This class implements a generic character controller for 2D side-scrolling games.
     * Although the class has been designed with platformer games in mind, it can be used
     * for any type of 2D or 2.5D game with horizontal motion. (In other words, it is not
     * meant for top-down 2D games.)
     * The class is fairly customizable, providing easy ways to toggle various mechanics such
     * as multiple jumps, wall and ladder climbing, dashing and other movement mechanics.
     * There are also many controls for fine-tuning these movements.
     */
    public class PlatformerPlayer2D : KinematicMotion2D
    {
        
        #region Inspector Fields
        // Fields displayed in the Unity inspector.

        
        [Header("Movement")]

        public bool faceMotionDirection = true;
        public float walkSpeed = 3f; // Maximum speed when walking, in units per second
        public float walkAcceleration = 10f;

        public float runSpeed = 10f; // Maximum speed when running, in units per second
        public float runAcceleration = 10f;
        public float breakingForce = 10f; // Deceleration force applied when changing direction or stopping

        [Space]
        public float airSpeed = 2f; // Maximum speed when in the air, in units per second
        public float airAcceleration = 10f;
        public float airBreakingForce = 100f; // Deceleration force applied when changin direction in the air

        [Space]

        // When jumping, if the vertical speed is below hangSpeed, we can apply reduced gravity.
        // The effect is that the character can be made to "hang" at the apex of a jump. Set this to 0 to disable it.
        [Min(0)]
        public float hangSpeed = 1f;

        // How much gravity is reduced when the character is hanging at the apex of a jump. If you set this to a
        // value greater than 1, the character will actually start falling sooner than normal. If the value is 0,
        // the character will become stuck at the apex of the jump. Set to 1 for no reduction in gravity.
        [Min(0)]
        public float hangGravityRatio = 1f;

        [Space]

        // Input scaling allows you to invert the player's input along specific axes.
        /// For example, setting this to (-1, 1) will invert the horizontal input while keeping the vertical input unchanged.
        public Vector2 movementInputScaling = Vector2.one;


        [Header("Jumping")]

        // The jump height is controlled by how long the jump button is held. For characters, such as NPCs that are controlled by 
        // scripts, the jump height can be similarly controlled by the interval between calling Jump(true) and Jump(false). That said,
        // for script-controlled characters, an easier approach is to set minJumpHeight to the desired height just before calling Jump(true),
        // and calling Jump(false) in the next frame. Note that while the jump height can be the same for both methods, the jump *arc* will
        // be slightly different.

        // The smallest jump height. This is how high the character jumps if the jump button is released immediately after being pressed.
        [Min(0.1f)]
        public float minJumpHeight = 1f;

        // The maximum jump height. This is how high a *stationary* character jumps if the jump button is held down.
        [Min(0.1f)]
        public float maxJumpHeight = 3f;

        // We can cause the character to jump at an angle instead of straight up, by setting the jumpAngle (degrees) variable.
        public float jumpAngle = 0f;

        // We can adjust the maximum jump height based on the character's last ground speed. For example, setting this to 2 means
        // that if the character is moving at its maximum ground speed possible, the maximum jump height will be twice as high as when stationary.
        public float jumpHeightSpeedBoost = 2f;

        // We can allow the character to jump in mid-air if it is close enough to the ground. There are two different methods for
        // achieving this. The first method, is to use a different ground check distance to check if we can jump or not.
        // When the character is in mid-air, but withing jumpGroundCheckDistance from the ground, it can still perform a jump.
        // Set this to 0, to disable this feature.
        public float jumpGroundCheckDistance = 0f;

        // The second method we can use is called "jump buffering". When the player presses the jump button (or a script calls Jump(true)),
        // the input is remembered for a short period of time (jumpBufferTime). If the character becomes able to jump within this time window,
        // it will automatically perform the jump. If used correctly, both jump buffering and loose ground check distance can make the game
        // feel more responsive (and forgiving). Normally, these methods are not used together.
        public float jumpBufferTime = 0.04f;

        // How many times the character can jump while in mid-air. Set this to 0 to disable air jumps.
        public int maxAirJumps = 1;

        // The height of an air jump relative to a normal jump (ratio). For example setting this to 0.5 causes air jumps to be 
        // half as high as ground jumps.
        public float airJumpHeightRatio = 0.5f;

        // A common mechanic in platformers is called "coyote time." This allows the character to still jump for a short time after leaving the ground.
        // The fallJumpTimeLimit variable specifies the duration of this grace period.
        public float fallJumpTimeLimit = 3f / 60f;

        [Space]

        // If canWallJump is true, the character will be able to perform jumps when near a wall, even if not on the ground.
        public bool canWallJump = false;

        // Like for air jumps, we can specify the height of a wall jump relative to a normal jump.
        public float wallJumpHeightRatio = 1f;

        // When performing a wall jump, we can give the character a push away from the wall. Use wallJumpHorizontalVelocity to
        // set the initial horizontal velocity when performing a wall jump.
        public float wallJumpHorizontalVelocity = 1f;

        [Space]
        // Can the character jump while grabbing a wall?
        public bool canJumpWhenGrabbing = true;

        // The height ratio of a jump performed while grabbing a wall relative to a normal jump.
        public float grabJumpHeightRatio = 1f;

        // Like wall jumps, we can specify the horizontal velocity of a jump performed while grabbing a wall.
        public float grabJumpHorizontalVelocity = 1f;

        [Space]
        // Can the character jump while climbing? See the climbing section  for more details about climbing mechanics.
        // Note that performing a jump while climbing will interrupt the climbing state.
        public bool canJumpWhenClimbing = true;

        // The height ratio of a jump performed while climbing relative to a normal jump.
        public float climbJumpHeightRatio = 1f;


        [Header("Walls")]

        // How far does the character needs to be from a wall to be considered "near" it for wall-related mechanics.
        public float wallCheckDistance = 0.3f;

        // When looking for walls, we can adjust the height, relative to the center of the character's collider, at which the wall check is performed.
        public float wallCheckVerticalOffset = 0f;

        // When falling near a wall, we can change the gravity to make the character slide down the wall more slowly.
        public float wallSlideGravityRatio = 0.5f;

        // We can also limit the maximum speed at which the character slides down the wall.
        public float wallSlideMaxSpeed = 5f;

        [Space]
        // Can the character grab walls? When the character grabs onto a wall, it does will not be affected by gravity.
        public bool canGrabWall = false;

        // We can limit the time, in seconds, during which a character can continuously grab a wall.
        public float maxWallGrabTime = 1f;

        [Space]
        // The maximum speed at which the character can climb up when grabbing a wall.
        public float wallClimbUpSpeed = 2f;

        // The maximum speed at which the character can climb down when grabbing a wall.
        public float wallClimbDownSpeed = 4f;

        // When climbing up while grabbing a wall, we automatically give the character a boost to climb over the edge.
        // Change climbOverEdgeJumpHeight and climbOverHorizontalVelocity to adjust this boost.
        public float climbOverEdgeJumpHeight = 1f;
        public float climbOverHorizontalVelocity = 2f;


        [Header("Climbing")]

        // "Climbing" is a mechanic that is completely distinct from wall grabbing. As a matter of fact, it has nothing to do with walls at all.
        // Climbing is usually used to implement ladders or vines. When canClimb is enabled, **the character will not be affected by gravity**.
        // Furthermore, when canClimb is true, vertical input will cause the character to move up and down. There is no concept of a 
        // "climbable surface." Instead, to implement climbable surfaces, you should use triggers to turn canClimb on and off when moving in and
        // out of climbable areas.

        public bool canClimb = false;

        // The maximum speed at which the character moves up when climbing.
        public float climbSpeedUp = 2f;

        // The maximum speed at which the character moves down when climbing.
        public float climbSpeedDown = 3f;

        // The maximum speed at which the character moves sideways when climbing.
        public float climbSpeedSide = 2f;

        // The acceleration and breaking force applied when climbing.
        public float climbAcceleration = 20f;

        public float climbBreakingForce = 50f;

        // We can use a separate collision mask when climbing. This can be very useful to allow the character to
        // pass through certain objects only when climbing.
        public LayerMask climbCollisionMask = ~0;


        [Header("Dashing")]

        // A "dash" is a sudden burst of speed in a given direction. If canDash is false, dash commands will be ignored.
        public bool canDash = false;

        // The start speed of the dash in the side direction.
        public float dashSpeedSide = 20f;

        // The start speed of the dash in the upward direction.
        public float dashSpeedUp = 20f;

        // The start speed of the dash in the downward direction.
        public float dashSpeedDown = 0f;

        // The duration of a dash, in seconds.
        public float dashTime = 0.2f;

        // We can use an animation curve to control how the speed changes over the duration of the dash.
        public AnimationCurve dashSpeedCurve = AnimationCurve.Linear(0, 1, 1, 1);

        // When performing a dash, if the character's speed falls below this value, the dash will be aborted.
        // This usually happens when the character collides with an obstacle.
        public float minDashSpeed = 0f;

        // We can temporarily block all inputs from the player for a duration of time (in seconds) after starting a dash.
        public float dashInputFreezeTime = 0.2f;

        // We can prevent the player from dashing again for a certain cooldown period (in seconds) after performing a dash.
        public float dashCoolDownTime = 2f;

        // If limitDashAngle is true, the dash direction will be restricted to left, right, up, down, and 45-degree diagonal directions.
        // If limitDashAngle is false, the dash direction will be determined by the player's input. Note that when playing with a 
        // keyboard, this has no real effect, but with a gamepad or analog stick, there is a big difference between the two dash modes.
        public bool limitDashAngle = true;

        // How strongly does gravity affect the character while dashing. A value of 0 means no gravity, and 1 means full gravity.
        public float dashGravityRatio = 0f;

        

        [Header("Animation")]

        // When the character dies, we probably will want to play some sort of animation, but it is very hard to reliably determine when
        // this animation will end. This can lead to situations where the character is dead, but subsequent processing is stalled, causing
        // a nasty freeze bug. We can set a timeout (in seconds) to automatically destroy the object after dying, to allow time for the 
        // animations to play out but still triggering the destruction of the object without relying on unreliable animation events.
        public float deathAnimationTimeoutSeconds = 2f;

        #endregion

        #region Accessors
        // Public fields accessible from getters and setters.

        
        public bool isRunning { get; private set; }
        public bool isJumping { get; private set; }

        public bool isGrabbing { get; private set; }

        public bool isDashing { get; private set; }

        public Vector2 motionInput { get; private set; }

        public Vector2 facingDirection { get; private set; }

        public State state { get; protected set; }

        // A vector representing the direction of the player's input. If there is no significant input, it defaults to the facing direction.
        public Vector2 inputDirection
        {
            get
            {
                if (motionInput.magnitude > SMALL_INPUT_THRESHOLD)
                {
                    return motionInput.normalized;
                }
                else
                {
                    return facingDirection;
                }
            }
        }

        public bool isTouchingWall { get; private set; }

        public float wallDirection { get; private set; }



        #endregion

        #region Public Fields
        // Public fields that can be accessed and modified directly.

        #endregion

        #region Events
        // C# events and overridable methods for handling various gameplay events.

        public Action OnDestroyed; // Invoked when the player object is destroyed.
        public Action OnJumped; // The character just jumped
        public Action OnLanded; // The character just landed on the ground
        public Action OnDied; // The character has died (distinct from being destroyed)
        public event Action<bool> OnInputEnabledChanged; // Invoked when player input is enabled or disabled.

        // Use the OnLanded event instead or overriding this method.
        protected override void Landed(float speed)
        {
            OnLanded?.Invoke();
            SendMessage("DidLand", SendMessageOptions.DontRequireReceiver);
        }

        #endregion

        #region Input
        // User input handling


        // When acceptInput is false, player input is ignored.
        public bool acceptInput = true;

        // Threshold used to determine if the player touched the motion inputs.
        protected const float SMALL_INPUT_THRESHOLD = 0.1f;


        private Vector2 rawMotionInput;

    
        // A timer used to temporarily freeze player input, for instance during dashing.
        private Utils.Timer inputFreezeTimer = new Utils.Timer();

        // A buffered input for jumping.
        private Utils.BufferedInput<bool> jumpInput = new Utils.BufferedInput<bool>(0.04f);

        
        // Below are callbacks for handling messages sent by the PlayerInput component in
        // Send Messages or Broadcast Messages mode. Make sure that the following actions are
        // defined in the input actions asset:
        // - Move (Vector2)
        // - Run (bool)
        // - GrabWall (bool)
        // - Dash ()
        // - Jump (bool)

        void OnMove(object val)
        {
            if (!acceptInput) return;
            Move(PuzzleBox.InputValue.GetValue<Vector2>(val));
        }

        void OnRun(object val)
        {
            if (!acceptInput) return;
            Run(PuzzleBox.InputValue.IsPressed(val));
        }

        void OnGrabWall(object val)
        {
            if (!acceptInput) return;
            GrabWall(PuzzleBox.InputValue.IsPressed(val));
        }

        void OnDash(object val)
        {
            if (!acceptInput) return;
            Dash();
        }

        void OnJump(object val)
        {
            if (!acceptInput) return;
            Jump(PuzzleBox.InputValue.IsPressed(val), true);
        }

        // This method is used to disable user input after death.
        void SetUserInputEnabled(bool enabled)
        {
            OnInputEnabledChanged?.Invoke(enabled);

            PlayerInput playerInput = GetComponent<PlayerInput>();
            if (playerInput != null)
            {
                playerInput.enabled = enabled;
            }
        }


        #endregion

        #region State Machine
        // Player state management


        // The state machine is at the core of this component's functionality. The player can be
        // in one of the states defined below:
        public enum State
        {
            Walking,
            Running,
            Dashing,
            Jumping,
            WallJumping,
            Falling,
            WallSliding,
            ClimbingWallUp,
            ClimbingWallDown,
            ClimbingWallOver,
            Climbing,
            Grabbing
        }

        // Update the player's state when they are near a wall.
        void UpdateStateOnWall()
        {

            if (!isGrabbing || !canGrabWall || !isTouchingWall || wallGrabTimer.isFinished)
            {
                if (isGrounded)
                {
                    state = State.Walking;
                } else
                {
                    state = State.Falling;
                }
            }
            else if (motionInput.y > SMALL_INPUT_THRESHOLD && wallClimbUpSpeed > 0)
            {
                state = State.ClimbingWallUp;
            }
            else if (motionInput.y < -SMALL_INPUT_THRESHOLD && wallClimbDownSpeed > 0)
            {
                state = State.ClimbingWallDown;
            }
        }

        // Update the player's state when they are on the ground.
        void UpdateStateOnGround()
        {
            if (isGrounded)
            {
                if (isTouchingWall && isGrabbing && canGrabWall)
                {
                    TryGrabbingWall();
                }
                else if(canClimb && motionInput.y > SMALL_INPUT_THRESHOLD)
                {
                    state = State.Climbing;
                }
                else if (isRunning)
                {
                    state = State.Running;
                }
                else
                {
                    state = State.Walking;
                }
            }
        }

        // Update the player's state when they are in the air.
        void UpdateStateInAir()
        {
            if (!isGrounded)
            {
                if (state == State.Climbing)
                {
                    if (!canClimb)
                    {
                        state = State.Falling;
                    }
                }
                else if (isTouchingWall)
                {
                    if (isGrabbing && canGrabWall)
                    {
                        TryGrabbingWall();
                    }
                    else if (canClimb && motionInput.y > 0)
                    {
                        state = State.Climbing;
                    }
                    else if (velocity.y < 0)
                    {
                        state = State.WallSliding;
                    }
                    else if (!isJumping)
                    {
                        state = State.Falling;
                    }
                }
                else if (isJumping)
                {
                    if (canClimb && motionInput.y > 0)
                    {
                        state = State.Climbing;
                    }
                    else if (velocity.y < 0)
                    {
                        state = State.Falling;
                    }
                    else
                    {
                        state = State.Jumping;
                    }
                }
                else
                {
                    if (canClimb && motionInput.y > 0)
                    {
                        state = State.Climbing;
                    }
                    else if (velocity.y < 0)
                    {
                        state = State.Falling;
                    }
                }
            }
        }

        // Entry point for updating the player's state each frame.
        void UpdateState()
        {
            // Update the timers
            dashCoolDownTimer.Tick(Time.fixedDeltaTime);
            inputFreezeTimer.Tick(Time.fixedDeltaTime);

            if (isGrounded)
            {
                wallGrabTimer.Reset(maxWallGrabTime);
            }

            if (state == State.Grabbing || state == State.ClimbingWallUp || state == State.ClimbingWallDown)
            {
                if (!isGrounded)
                {
                    wallGrabTimer.active = true;
                    wallGrabTimer.Tick(Time.fixedDeltaTime);
                }
            }
            else
            {
                wallGrabTimer.active = false;
            }

            // Update the player's state
            switch (state)
            {

                case State.Walking:
                case State.Running:
                case State.Climbing:
                    if (isGrounded)
                    {
                        UpdateStateOnGround();
                    }
                    else
                    {
                        UpdateStateInAir();
                    }
                    break;

                case State.Dashing:

                    dashTimer.Tick(Time.fixedDeltaTime);

                    if (velocity.magnitude <= minDashSpeed)
                    {
                        dashTimer.Cancel();
                    }

                    if (dashTimer.isFinished)
                    {
                        if (isGrounded)
                        {
                            // Don't forget to set default state so we don't get stuck in dash
                            state = State.Walking;
                            UpdateStateOnGround();
                        }
                        else
                        {
        
                            state = State.Falling;
                            UpdateStateInAir();
                        }
                    }

                    break;

                case State.WallJumping:
                case State.Jumping:
                    if (isGrounded)
                    {
                        UpdateStateOnGround();
                    }
                    else
                    {
                        UpdateStateInAir();
                    }
                    break;

                case State.Falling:
                    if (isGrounded)
                    {
                        UpdateStateOnGround();
                    }
                    else
                    {
                        UpdateStateInAir();
                    }
                    break;

                case State.WallSliding:
                    if (isGrounded)
                    {
                        UpdateStateOnGround();
                    }
                    else
                    {
                        UpdateStateInAir();
                    }
                    break;

                case State.ClimbingWallUp:
                    if (!isTouchingWall)
                    {
                        ClimbOverEdge();
                        state = State.ClimbingWallOver;
                    }
                    else
                    {
                        UpdateStateOnWall();
                    }
                    break;

                case State.ClimbingWallDown:
                    UpdateStateOnWall();
                    break;

                case State.ClimbingWallOver:
                    if (isGrounded)
                    {
                        UpdateStateOnGround();
                    }
                    else if (velocity.y < 0)
                    {
                        UpdateStateInAir();
                    }
                    break;

                case State.Grabbing:
                    UpdateStateOnWall();
                    break;
            }
        }

        #endregion

        #region Movement

        // Toggle the running state of the player.
        public void Run(bool state)
        {
            isRunning = state;
        }

        // Move the player based on input.
        public void Move(Vector2 input)
        {
            rawMotionInput = Vector2.Scale(input, movementInputScaling);

            motionInput = rawMotionInput;

            dashTimer.Cancel();
        }

        // Move the player when they are in the air.
        void MoveInAir()
        {
            float horizontalVelocity = velocity.x;
            float horizontalDirection = Mathf.Sign(horizontalVelocity);
            float groundHorizontalDirection = Mathf.Sign(lastGroundVelocity.x);
            float speedLimit = airSpeed;

            // Horizontal movement while in the air
            if (Mathf.Abs(motionInput.x) > 0)
            {
                Vector2 velocityChange = Vector2.right * motionInput.x * airAcceleration * Time.fixedDeltaTime;
                velocity += velocityChange;
            }

            if (horizontalDirection == groundHorizontalDirection)
            {
                speedLimit = Mathf.Max(Mathf.Abs(lastGroundVelocity.x), speedLimit);
            }

            if (Mathf.Abs(velocity.x) > speedLimit)
            {
                float targetVelocityX = Mathf.Sign(velocity.x) * speedLimit;
                float maxStep = airBreakingForce * Time.fixedDeltaTime;
                velocity.x = Mathf.MoveTowards(velocity.x, targetVelocityX, maxStep);
            }
        }

        // Update the player's velocity when they are interacting with a wall.
        void ApplyWallMotion()
        {
            gravityMultiplier = 0;

            if (state == State.ClimbingWallUp)
            {
                velocity.y = motionInput.y * wallClimbUpSpeed;
            }

            else if (state == State.ClimbingWallDown)
            {
                velocity.y = motionInput.y * wallClimbDownSpeed;
            }

            else if (state == State.Grabbing)
            {
                velocity = Vector2.zero;
            }
        }

        // Update the player's velocity when they are climbing.
        void ApplyClimbingMotion()
        {
            gravityMultiplier = 0f;

            if (Mathf.Abs(motionInput.x) > SMALL_INPUT_THRESHOLD)
            {
                velocity.x += motionInput.x * climbAcceleration * Time.fixedDeltaTime;
                velocity.x = Mathf.Clamp(velocity.x, -climbSpeedSide, climbSpeedSide);
            }
            else
            {
                float breakDirection = -Mathf.Sign(velocity.x);
                float deltaV = breakDirection * climbBreakingForce * Time.fixedDeltaTime;
                velocity.x += deltaV;
                if (Mathf.Sign(velocity.x) == breakDirection)
                {
                    velocity.x = 0;
                }
            }

            if (Mathf.Abs(motionInput.y) > SMALL_INPUT_THRESHOLD)
            {
                velocity.y += motionInput.y * climbAcceleration * Time.fixedDeltaTime;
                velocity.y = Mathf.Clamp(velocity.y, -climbSpeedDown, climbSpeedUp);
            }
            else
            {
                float breakDirection = -Mathf.Sign(velocity.y);
                float deltaV = breakDirection * climbBreakingForce * Time.fixedDeltaTime;
                velocity.y += deltaV;
                if (Mathf.Sign(velocity.y) == breakDirection)
                {
                    velocity.y = 0;
                }
            }
        }

        // Update the player's velocity when they are in the air.
        void ApplyAirMotion()
        {
            if (state == State.Dashing)
            {
                gravityMultiplier = dashGravityRatio;
                velocity = CalculateDashVelocity();
            }

            else if (state == State.Jumping)
            {
                if (isJumping) {
                    gravityMultiplier = jumpGravityMultiplier;
                } else if (velocity.y > 0) {
                    // If we are in jumping state but the jumping flag is false, this
                    // means that the player has just released the jump button. If the character is still
                    // moving upwards, we break the jump by applying a stronger gravity.
                    // However, we have to make sure to clamp the velocity so that the break doesn't 
                    // accidentally reverses the direction of motion.
                    float gravityStep = Physics2D.gravity.y * breakGravityMultiplier * Time.fixedDeltaTime;
                    if (velocity.y + gravityStep < 0)
                    {
                        velocity.y = 0;
                        gravityMultiplier = 0;
                    }
                    else
                    {
                        gravityMultiplier = breakGravityMultiplier;
                    }
                } else {
                    // If the player is already falling when the jump ends, set the gravity to normal.
                    gravityMultiplier = normalGravityMultiplier;
                }

                MoveInAir();
            }

            else if (state == State.WallJumping)
            {
                gravityMultiplier = jumpGravityMultiplier;

                MoveInAir();
            }

            else if (state == State.Falling)
            {
                isJumping = false;
                gravityMultiplier = normalGravityMultiplier;

                MoveInAir();
            }

            else if (state == State.Grabbing || state == State.ClimbingWallUp || state == State.ClimbingWallDown)
            {
                ApplyWallMotion();
            }

            else if (state == State.ClimbingWallOver)
            {
                gravityMultiplier = normalGravityMultiplier;
                velocity.x = climbOverHorizontalVelocity * wallDirection;
            }

            else if (state == State.WallSliding)
            {
 
                if (wallSlideWaitTimer.isFinished)
                {
                    gravityMultiplier = wallSlideGravityRatio * normalGravityMultiplier;
                }
            }

            else if (state == State.Climbing)
            {
                ApplyClimbingMotion();
            }

            switch(state)
            {
                case State.Jumping:
                case State.WallJumping:
                case State.Falling:
                    if (Mathf.Abs(velocity.y) < hangSpeed)
                    {
                        gravityMultiplier *= hangGravityRatio;
                    }
                    break;
            }
        }

        void ApplyGroundMotion()
        {
            // Reset the air jump count when the player is on the ground.
            airJumps = 0;

            // Reset the gravity multiplier to the normal value.
            gravityMultiplier = normalGravityMultiplier;

            if (state == State.Grabbing || state == State.ClimbingWallUp || state == State.ClimbingWallDown)
            {
                ApplyWallMotion();
            }

            else if (state == State.Walking || state == State.Running)
            {
                if (Mathf.Abs(motionInput.x) > 0)
                {
                    float acceleration = isRunning ? runAcceleration : walkAcceleration;
                    // Correct for the case when gravityModifier is negative (gravity is inverted), 
                    // which causes groundRight to be reversed in world coordinates.
                    Vector2 velocityChange = groundRight * (motionInput.x * -GravityDirection * acceleration * Time.fixedDeltaTime);
                    velocity += velocityChange;
                }
                else
                {
                    Vector2 breakDirection = velocity.normalized * -1f;
                    float groundSpeed = velocity.magnitude;
                    Vector2 velocityChange = breakDirection * Mathf.Min(breakingForce * Time.fixedDeltaTime, groundSpeed);
                    velocity += velocityChange;
                }

                Vector2 v = groundRight * Vector2.Dot(groundRight, velocity);
                float speed = velocity.magnitude;
                float maxSpeed;

                if (isRunning)
                {
                    maxSpeed = runSpeed;
                }
                else
                {
                    maxSpeed = walkSpeed;
                }

                if (speed > maxSpeed)
                {
                    Vector2 breakDirection = v.normalized * -1f;
                    Vector2 velocityChange = breakDirection * (speed - maxSpeed);
                    velocity += velocityChange;
                }
            }
            else if (state == State.Dashing)
            {
                velocity = CalculateDashVelocity();
            }
            else if (state == State.Climbing)
            {
                ApplyClimbingMotion();
            }
        }

        #endregion

        #region Jumping

        // Counter for the number of consecutive air jumps.
        int airJumps = 0;

        bool isAirJump = false;
        bool isWallJump = false;
        bool isGrabJump = false;
        bool isClimbJump = false;

        // Gravity multiplier applied when the jump button is held.
        // The value is calculated when a jump is performed, based on minimum and maximum jump heights.
        float jumpGravityMultiplier = 1f;

        // Check to see if we can jump
        bool CanJump()
        {
            isWallJump = false;
            isAirJump = false;
            isGrabJump = false;
            isClimbJump = false;

            if (minJumpHeight <= 0 || maxJumpHeight <= 0)
            {
                return false;
            }

            switch(state)
            {
                case State.Walking:
                case State.Running:
                    return true;

                case State.Grabbing:
                case State.ClimbingWallUp:
                case State.ClimbingWallDown:
                    isGrabJump = canJumpWhenGrabbing;
                    return isGrabJump;

                case State.Climbing:
                    isClimbJump = canJumpWhenClimbing;
                    return isClimbJump;
                
                case State.WallSliding:
                    isWallJump = canWallJump;
                    return canWallJump;

                case State.Dashing:
                    if (isGrounded)
                    {
                        return acceptInput;
                    }
                    else
                    {
                        goto case State.Falling;
                    }
                case State.Falling:
                    if (timeInAir <= fallJumpTimeLimit)
                    {
                        // Check for "coyote time" - allow jumping shortly after leaving the ground.
                        return true;
                    }
                    else if (maxAirJumps > 0 && airJumps <= maxAirJumps)
                    {
                        // We haven't reached the limit for air jumps, so we can jump.
                        isAirJump = true;
                        return true;
                    }
                    else if (velocity.y < 0)
                    {
                        // At this point, we are falling in the air. We might land soon.
                        // If we detect the ground within the specified distance, allow an extra jump.
                        RaycastHit2D groundHit;
                        return CheckForGround(velocity.normalized, jumpGroundCheckDistance, out groundHit);
                    }
                    else
                    {
                        return false;
                    }

                default:
                    return false;
            }
        }

        private Vector2 CalculateJumpVector(float angleOffsetDegrees, float jumpVelocity)
        {
            float theta = Mathf.PI * 0.5f + Mathf.Deg2Rad * angleOffsetDegrees;
            Vector2 jumpDir = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));

            Vector2 v = jumpDir;
            float vel = jumpVelocity;
            if (Mathf.Abs(v.y) > 0.0001f)
            {
                v.x *= vel / v.y;
                v.y = vel;
            }
            else
            {
                v.y = 0;
                v.x = vel;
            }

            return v;
        }

        // Start or cancel a jump based on the input state.
        public void Jump(bool jumpState, bool bufferInput = true)
        {
            if (!acceptInput) return;

            if (jumpState) // Try to start a jump
            {
                if (CanJump()) // If we can jump...
                {
                    float jumpVelocity = 0; // The initial jump velocity, which will be calculated below.

                    float maxSpeed = Mathf.Max(walkSpeed, runSpeed);
                    float speedRatio = Mathf.Abs(velocity.x) / maxSpeed;
                    float adjustedMaxJumpHeight = maxJumpHeight + speedRatio * jumpHeightSpeedBoost;

                    // Calculate the jump using a parabolic trajectory. Determine the initial velocity needed to reach maxJumpHeight under normal gravity.
                    // Holding the jump button will allow the character to rise to maxJumpHeight under normal gravity.
                    // Releasing the button will increase gravity, causing a faster fall (breakGravityMultiplier).
                    // If gravityModifier is negative, the effective gravity is reversed, so the jump will also be in the opposite direction.
                    float effectiveGravityY = Physics2D.gravity.y * gravityModifier;
                    jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(effectiveGravityY) * normalGravityMultiplier * adjustedMaxJumpHeight) * -GravityDirection;
                    jumpGravityMultiplier = normalGravityMultiplier;
                    breakGravityMultiplier = normalGravityMultiplier * (adjustedMaxJumpHeight / minJumpHeight);

                    // This is a minor adjustment. Due to the implementation of KinematicMotion2D, the velocity may decrease due to gravity
                    // before the jump movement starts. To negate the effect of gravity in the first frame, we increase the jump velocity.
                    jumpVelocity += -effectiveGravityY * jumpGravityMultiplier * Time.fixedDeltaTime;

                    if (isAirJump)
                    {
                        Vector2 v = CalculateJumpVector(jumpAngle * -Mathf.Sign(facingDirection.x), jumpVelocity * airJumpHeightRatio);
                       
                        velocity.x += v.x;
                        velocity.y = v.y;

                        state = State.Jumping;
                    }
                    else if (isWallJump)
                    {
                        Vector2 v = CalculateJumpVector(0, jumpVelocity * wallJumpHeightRatio);
                        
                        velocity.x = -wallDirection * wallJumpHorizontalVelocity;
                        velocity.y = v.y;

                        state = State.WallJumping;
                    }
                    else if (isGrabJump)
                    {
                        Vector2 v = CalculateJumpVector(0, jumpVelocity * grabJumpHeightRatio);

                        velocity.x = -wallDirection * grabJumpHorizontalVelocity;
                        velocity.y = v.y;

                        state = State.WallJumping;
                    }
                    else if (isClimbJump)
                    {
                        Vector2 v = CalculateJumpVector(jumpAngle * -Mathf.Sign(facingDirection.x), jumpVelocity * climbJumpHeightRatio);

                        velocity.x += v.x;
                        velocity.y = v.y;

                        state = State.Jumping;
                    }
                    else
                    {
                        Vector2 v = CalculateJumpVector(jumpAngle * -Mathf.Sign(facingDirection.x), jumpVelocity);

                        velocity.x += v.x;
                        velocity.y = v.y;

                        state = State.Jumping;
                    }


                    // If the ground is moving, add its velocity to the player's velocity.
                    velocity += groundVelocity;
                    lastGroundVelocity += groundVelocity;

                    // Apply gravity adjustment.
                    gravityMultiplier = jumpGravityMultiplier;

                    airJumps++; // Increase the jump count.
                    isJumping = true; // Remember that the player is jumping. Dash

                    jumpInput.Reset();

                    OnJumped?.Invoke();

                    SendMessage("DidJump", SendMessageOptions.DontRequireReceiver);

                    // Forcefully recheck whether the player is grounded.
                    // Otherwise, immediately after jumping on a slope, the movement direction may deviate horizontally.
                    UpdateGroundedState();
                }
                else if (bufferInput)
                {
                    jumpInput.Set(jumpState, jumpBufferTime);
                }
            }
            else // Jump ended
            {
                // The jump has ended, change the gravity adjustment.
                gravityMultiplier = breakGravityMultiplier;
                isJumping = false;
            }
        }

        #endregion

        #region Wall Interaction

        public void GrabWall(bool grabbing)
        {
            if (!acceptInput) return;

            isGrabbing = grabbing;
        }

        [HideInInspector]
        public Utils.Timer wallSlideWaitTimer = new Utils.Timer();
        [HideInInspector]
        public Utils.Timer wallGrabTimer = new Utils.Timer();

        void TryGrabbingWall()
        {
            if (state != State.Grabbing && state != State.WallJumping && !wallGrabTimer.isFinished)
            {
                state = State.Grabbing;
                wallGrabTimer.Start(maxWallGrabTime);
            }
        }

        void ClimbOverEdge()
        {
            float jumpVelocity = Mathf.Sqrt(-2f * Physics2D.gravity.y * normalGravityMultiplier * climbOverEdgeJumpHeight);
            velocity.y = jumpVelocity;
            velocity.x = climbOverHorizontalVelocity * wallDirection;
        }

        private RaycastHit2D[] wallRayHits = new RaycastHit2D[8];

        protected void UpdateWallTouchingState()
        {
            isTouchingWall = false;

            bool touchingRight = false;
            bool touchingLeft = false;

            // Raycast from the character's collider center, offset upwards by wallCheckVerticalOffset.
            Vector2 origin = (Vector2)bounds.center + Vector2.up * wallCheckVerticalOffset;
            float distance = bounds.extents.x + wallCheckDistance;
            LayerMask mask = GetCollisionMask();

            // Right direction (use NonAlloc to get all hits to exclude the character's own collider)
            int rightCount = Physics2D.RaycastNonAlloc(origin, Vector2.right, wallRayHits, distance, mask);
            for (int i = 0; i < rightCount; i++)
            {
                if (wallRayHits[i].collider.gameObject != gameObject)
                {
                    touchingRight = true;
                    break;
                }
            }

            // Left...
            int leftCount = Physics2D.RaycastNonAlloc(origin, Vector2.left, wallRayHits, distance, mask);
            for (int i = 0; i < leftCount; i++)
            {
                if (wallRayHits[i].collider.gameObject != gameObject)
                {
                    touchingLeft = true;
                    break;
                }
            }

            isTouchingWall = touchingRight || touchingLeft;

            if (touchingRight && touchingLeft)
            {
                wallDirection = facingDirection.x < 0 ? -1 : 1;
            }
            else if (isTouchingWall)
            {
                wallDirection = touchingLeft ? -1 : 1;
            }
        }

        #endregion

        #region Climbing

        

        public void StopClimbing()
        {
            if (state == State.Climbing)
            {
                state = State.Falling;
            }
        }

        #endregion

        #region Dashing

        [HideInInspector]
        public Utils.Timer dashTimer = new Utils.Timer();

        [HideInInspector]
        public Utils.Timer dashCoolDownTimer = new Utils.Timer();

        private Vector2 _dashDirection = Vector2.right;
        public Vector2 dashDirection
        {
            get => _dashDirection;
            private set => _dashDirection = value;
        }

        public void PerformDash(Vector2 direction, float speed)
        {
            dashDirection = direction;
            velocity = direction * speed;

            dashTimer.Start(dashTime);
            dashCoolDownTimer.Start(dashCoolDownTime);
            if (acceptInput)
            {
                inputFreezeTimer.Start(dashInputFreezeTime);
            }

            state = State.Dashing;

            SendMessage("DidDash", SendMessageOptions.DontRequireReceiver);
        }

        public void Dash()
        {
            if (!acceptInput) return;

            if (canDash && dashCoolDownTimer.isFinished)
            {
                if (motionInput.magnitude > SMALL_INPUT_THRESHOLD)
                {
                    dashDirection = motionInput.normalized;
                }
                else
                {
                    dashDirection = facingDirection;
                }

                if (limitDashAngle)
                {
                    float angle = Mathf.Atan2(dashDirection.y, dashDirection.x);
                    angle = Mathf.Round(angle * 1.2732395447351626861510701069801f) * 0.78539816339744830961566084581988f;
                    dashDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }

                PerformDash(dashDirection, CalculateDashSpeed(dashDirection));
            }
        }

        float CalculateDashSpeed(Vector2 direction)
        {
            float verticalSpeed = direction.y < 0 ? dashSpeedDown : dashSpeedUp;
            return Mathf.Abs(direction.x) * dashSpeedSide + Mathf.Abs(direction.y) * verticalSpeed;
        }

        Vector2 CalculateDashVelocity()
        {
            float phase = dashTimer.phase;
            float adjustment = 1f;
            if (dashSpeedCurve.keys.Length >= 2)
            {
                adjustment = dashSpeedCurve.Evaluate(phase);
            }
            return dashDirection * CalculateDashSpeed(dashDirection) * adjustment;
        }

        #endregion

        #region Collisions

        protected override bool CanPush(KinematicMotion2D otherMotion, Vector2 delta)
        {
           bool canPush = base.CanPush(otherMotion, delta);
            
           return otherMotion.pushable && canPush;
        }

        bool AvoidPlatformEdge(Vector2 avoidDirection, float distanceX, float distanceY)
        {
            RaycastHit2D hit;
            Vector2 p = rb.position;
            bool canMove = Cast(avoidDirection, distanceX, out hit) == false;
            if (canMove)
            {
                rb.position += avoidDirection * distanceX;
                canMove = Cast(Vector2.up, distanceY, out hit) == false;
                if (canMove)
                {
                    rb.position += Vector2.up * distanceY;
                    Slide(avoidDirection * -distanceX);
                    positionAdjustment += Vector2.right * (rb.position.x - p.x);
                    return true;
                }
                else if (hit.distance > 0)
                {
                    rb.position += Vector2.up * hit.distance;
                    Slide(avoidDirection * -distanceX);
                    positionAdjustment += Vector2.right * (rb.position.x - p.x);
                    return true;
                }
                else
                {
                    rb.position = p;
                }
            }

            return false;
        }

        protected override LayerMask GetCollisionMask()
        {
            return state == State.Climbing ? climbCollisionMask : collisionMask;
        }

        protected override float ProcessCollision(RaycastHit2D hit, Vector2 direction, float distanceRemaining)
        {
            float edgeSize = 0.5f;

            if (direction.y > 0)
            {
                Vector2 p = rb.position;
                Vector2 delta = direction * distanceRemaining;
                Vector2 avoidDirection = Vector2.right;

                bool didMove = AvoidPlatformEdge(Vector2.right, edgeSize, delta.y) || AvoidPlatformEdge(Vector2.left, edgeSize, delta.y);
                if (didMove)
                {
                    float moveDistance = (rb.position - p).magnitude;
                    return Mathf.Max(0f, distanceRemaining - moveDistance);
                }
            }
            return base.ProcessCollision(hit, direction, distanceRemaining);
        }

        protected override void ContactEnter(Contact contact)
        {
            base.ContactEnter(contact);

            if (state == State.Dashing)
            {
                if (IsWallNormal(contact.normal) && Vector2.Dot(contact.direction, velocity.normalized) > 0.707)
                {
                    dashTimer.Cancel();
                }
            }
        }

        #endregion

        #region Physics

        // Remember the normal gravity multiplier.
        float normalGravityMultiplier = 1f;        
        float breakGravityMultiplier = 1f;

        #endregion

        #region Destruction

        private bool isKilled = false;
        private Coroutine waitForDeathCoroutine = null;

        public void Kill()
        {
            if (!isKilled)
            {
                // isKilled = true;
                SetUserInputEnabled(false);
                OnDied?.Invoke();
                waitForDeathCoroutine = StartCoroutine(WaitForDeath());

                SendMessage("WasKilled", SendMessageOptions.DontRequireReceiver);
            }
            
        }

        IEnumerator WaitForDeath()
        {
            yield return new WaitForSeconds(deathAnimationTimeoutSeconds);
            DestroySelf();
        }

        public void DestroySelf()
        {
            if (waitForDeathCoroutine != null)
            {
                StopCoroutine(waitForDeathCoroutine);
                waitForDeathCoroutine = null;
            }

            SendMessage("WasDestroyed", SendMessageOptions.DontRequireReceiver);
            OnDestroyed?.Invoke();
            Destroy(gameObject);
        }

        #endregion

        #region Facing Direction

        protected Vector2 defaultFacingDirection = Vector2.right;

        private SpriteRenderer spriteRenderer;

        void UpdateFacingDirection()
        {
            if (acceptInput)
            {
                if (state == State.Grabbing || state == State.ClimbingWallUp || state == State.ClimbingWallDown)
                {
                    facingDirection = Vector2.right * wallDirection;
                }
                else
                {
                    if (motionInput.magnitude < SMALL_INPUT_THRESHOLD)
                    {
                        facingDirection = defaultFacingDirection;
                    }
                    else
                    {
                        facingDirection = motionInput.normalized;

                        defaultFacingDirection = motionInput.x < -SMALL_INPUT_THRESHOLD ? Vector2.left : (motionInput.x > SMALL_INPUT_THRESHOLD ? Vector2.right : defaultFacingDirection);
                    }
                }
            }

        }

        #endregion

        #region MonoBehaviour
        // Unity MonoBehaviour lifecycle methods

        protected override void Awake()
        {
            base.Awake();

            dashTimer.OnStart += () => isDashing = true;
            dashTimer.OnEnd += () => isDashing = false;

            inputFreezeTimer.OnStart += () => { acceptInput = false; motionInput = Vector2.zero; };
            inputFreezeTimer.OnEnd += () => { acceptInput = true; motionInput = rawMotionInput; };


        }

        protected override void Start()
        {
            base.Start();

            spriteRenderer = GetComponent<SpriteRenderer>();

            normalGravityMultiplier = gravityMultiplier;
            facingDirection = defaultFacingDirection;
            wallDirection = 1;
            state = State.Walking;
            jumpInput.duration = jumpBufferTime;
        }

        protected override void Update()
        {
            base.Update();

            if (spriteRenderer != null && faceMotionDirection)
            {
                spriteRenderer.flipX = facingDirection.x < 0;
            }
        }

        protected override void FixedUpdate()
        {
            UpdateState();

            // Process motion

            if (isGrounded)
            {
                ApplyGroundMotion();
            }
            else
            {
                ApplyAirMotion();
            }

            base.FixedUpdate();

            UpdateWallTouchingState();

            if (state == State.WallSliding)
            {
                if (wallSlideWaitTimer.isFinished)
                {
                    if (velocity.y < -wallSlideMaxSpeed)
                    {
                        velocity.y = -wallSlideMaxSpeed;
                    }
                }
            }

            UpdateFacingDirection();

            // Check the jump buffer
            if (jumpInput.HasValue())
            {
                Jump(jumpInput.Get(), false);
            }

        }

#if UNITY_EDITOR
        GUIStyle guiStyle = new GUIStyle();

        // By implementing this method, we can draw custom gizmos in the Unity scene view.
        private void OnDrawGizmosSelected()
        {
            if (isActiveAndEnabled)
            {
                 // Here, we draw green lines representing the jump height.
                Vector3 high = transform.position + Vector3.up * maxJumpHeight;
                Vector3 low = transform.position + Vector3.up * minJumpHeight;
                Gizmos.color = Color.green;
                Gizmos.DrawLine(low + Vector3.left * 0.5f, low + Vector3.right * 0.5f);
                Gizmos.DrawLine(high + Vector3.left * 0.5f, high + Vector3.right * 0.5f);


                guiStyle.normal.textColor = Color.green;
                guiStyle.alignment = TextAnchor.MiddleLeft;
                Handles.Label(high + Vector3.right * 0.6f, "Jump Max", guiStyle);
                Handles.Label(low + Vector3.right * 0.6f, "Jump Min", guiStyle);

                // Visualize wall detection raycasts
                Bounds bounds = GetBounds(true);
                Vector3 rayOrigin =  bounds.center + Vector3.up * wallCheckVerticalOffset;
                float rayLength = bounds.extents.x + wallCheckDistance;

                Gizmos.color = Color.cyan;

                // Right ray
                Gizmos.DrawLine(rayOrigin, rayOrigin + Vector3.right * rayLength);
                guiStyle.alignment = TextAnchor.MiddleLeft;
                Handles.Label(rayOrigin + Vector3.right * (rayLength + 0.1f), "Wall Check", guiStyle);

                // Left ray
                Gizmos.DrawLine(rayOrigin, rayOrigin + Vector3.left * rayLength);
                guiStyle.alignment = TextAnchor.MiddleRight;
                Handles.Label(rayOrigin + Vector3.left * (rayLength + 0.1f), "Wall Check", guiStyle);
            }
        }
#endif


        #endregion

    }
} // namespace
