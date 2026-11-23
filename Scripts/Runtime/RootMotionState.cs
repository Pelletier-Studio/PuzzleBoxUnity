using UnityEngine;


namespace PuzzleBox
{
    public class RootMotionState : StateMachineBehaviour
    {
        public override void OnStateEnter(Animator animator, AnimatorStateInfo info, int layer)
        {
            animator.GetComponent<RootMotionHandler>().RegisterAnimatorState(layer, info.fullPathHash, this);
        }
    }
}

