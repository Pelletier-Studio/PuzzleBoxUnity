using UnityEngine;



namespace PuzzleBox
{
    /**
    * This component, when attached to a GameObject with an Animator, will use inverse kinematics
    * to attempt to position a humanoid character's feet correctly on the ground. This component
    * only works with 2D physics and 2D colliders. There is no need for a rigidbody or collider on
    * the object on which this component is attached.
    */
    [RequireComponent(typeof(Animator))]
    public class FootIK2D : MonoBehaviour
    {
        public LayerMask groundLayer;

        [Tooltip("Max height above the character's base at which ground is detected.")]
        public float stepHeight = 0.5f;

        [Tooltip("Max depth below the character's base at which ground is detected.")]
        public float stepDepth = 0.5f;

        private Animator animator;

        private KinematicMotion2D kinematicMotion2D;

        void Awake()
        {
            animator = GetComponent<Animator>();
            kinematicMotion2D = GetComponentInParent<KinematicMotion2D>();
        }

        void OnAnimatorIK(int layerIndex)
        {
            bool grounded = kinematicMotion2D ? kinematicMotion2D.isGrounded : false;

            if (grounded)
            {
                bool leftHit = GroundOffset(AvatarIKGoal.LeftFoot, out float leftOffset, out Vector3 leftNormal);
                bool rightHit = GroundOffset(AvatarIKGoal.RightFoot, out float rightOffset, out Vector3 rightNormal);

                // Lower the body so the leg over the lowest ground can reach it.
                float bodyOffset = Mathf.Min(leftHit ? leftOffset : 0f, rightHit ? rightOffset : 0f, 0f);
                animator.bodyPosition += Vector3.up * bodyOffset;

                SetFoot(AvatarIKGoal.LeftFoot, leftHit, leftOffset, leftNormal);
                SetFoot(AvatarIKGoal.RightFoot, rightHit, rightOffset, rightNormal);
            }
            
        }

        // Height of the ground under the foot relative to the character's base.
        bool GroundOffset(AvatarIKGoal goal, out float offset, out Vector3 normal)
        {
            Vector3 footPosition = animator.GetIKPosition(goal);
            Vector2 origin = new(footPosition.x, transform.position.y + stepHeight);
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, stepHeight + stepDepth, groundLayer);

            offset = hit.collider ? hit.point.y - transform.position.y : 0f;
            normal = hit.collider ? (Vector3)hit.normal : Vector3.up;
            return hit.collider;
        }

        void SetFoot(AvatarIKGoal goal, bool hit, float offset, Vector3 normal)
        {
            float weight = hit ? 1f : 0f;
            animator.SetIKPositionWeight(goal, weight);
            animator.SetIKRotationWeight(goal, weight);

            if (!hit)
                return;

            // Shift the animated foot by the ground offset so lifted feet stay lifted.
            animator.SetIKPosition(goal, animator.GetIKPosition(goal) + Vector3.up * offset);
            animator.SetIKRotation(goal, Quaternion.FromToRotation(Vector3.up, normal) * animator.GetIKRotation(goal));
        }
    }

}
