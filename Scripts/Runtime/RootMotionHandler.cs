using System.Collections.Generic;
using UnityEngine;

namespace PuzzleBox
{
    [RequireComponent(typeof(Animator))]
    public class RootMotionHandler : MonoBehaviour
    {
        public PlatformerPlayer2D player2D;

        private Dictionary<int, RootMotionState>[] rootMotionStatesForLayers;
        private Animator animator;

        Vector3 positionDelta = Vector3.zero;
        Quaternion rotationDelta = Quaternion.identity;

        bool simulatePhysicsDefault = false;
        RootMotionState activeState = null;

        void Awake()
        {                
            animator = GetComponent<Animator>();
            rootMotionStatesForLayers = new Dictionary<int, RootMotionState>[animator.layerCount];
            for (int i = 0; i < rootMotionStatesForLayers.Length; i++)
            {
                rootMotionStatesForLayers[i] = new Dictionary<int, RootMotionState>();
            }
        }

        void OnEnable()
        {
            if (player2D == null)
            {
                player2D = GetComponentInParent<PlatformerPlayer2D>();
            }

            if (player2D)
            {
                player2D.OnEarlyFixedUpdate += EarlyFixedUpdate;
            }
        }

        void OnDisable()
        {
            if (player2D)
            {
                player2D.OnEarlyFixedUpdate -= EarlyFixedUpdate;
            }
        }

        void EarlyFixedUpdate(KinematicMotion2D motion)
        {
            if (positionDelta != Vector3.zero)
            {
                // motion.MoveBy(positionDelta);
                motion.rigidbody.position += (Vector2)positionDelta;
                positionDelta = Vector3.zero;
            }
            
            rotationDelta = Quaternion.identity;
        }

        public void RegisterAnimatorState(int layer, int hash, RootMotionState state)
        {
            if (layer >= 0 && layer < rootMotionStatesForLayers.Length) {
                rootMotionStatesForLayers[layer][hash] = state;
            }
        }

        void UpdateActiveRootMotionState()
        {
            RootMotionState newState = null;
            for (int i = 0; i < animator.layerCount; i++)
            {
                if (animator.GetLayerWeight(i) <= 0f)
                {
                    continue;
                }

                if (animator.IsInTransition(i))
                {
                    rootMotionStatesForLayers[i].TryGetValue(animator.GetNextAnimatorStateInfo(i).fullPathHash, out newState);
                } else
                {
                    rootMotionStatesForLayers[i].TryGetValue(animator.GetCurrentAnimatorStateInfo(i).fullPathHash, out newState);
                }
            }
            if (newState && !activeState)
            {
                simulatePhysicsDefault = player2D.simulatePhysics;
            } else if (!newState && activeState)
            {
                player2D.simulatePhysics = simulatePhysicsDefault;
            }

            activeState = newState;
        }

        void OnAnimatorMove()
        {
            if (player2D)
            {
                UpdateActiveRootMotionState();
                if (activeState != null)
                {
                    positionDelta += animator.deltaPosition;
                    rotationDelta = animator.deltaRotation * rotationDelta;
                }
            }
            
        }
    }
}
