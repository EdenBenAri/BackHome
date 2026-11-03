using UnityEngine;

namespace StarterAssets
{
    public class UICanvasControllerInput : MonoBehaviour
    {

        [Header("Output")]
        public StarterAssetsInputs starterAssetsInputs;

        public void VirtualMoveInput(Vector2 virtualMoveDirection)
        {
            if (starterAssetsInputs != null)
                starterAssetsInputs.MoveInput(virtualMoveDirection);
        }

        public void BeginMoveGesture()
        {
            if (starterAssetsInputs != null)
                starterAssetsInputs.BeginMoveGesture();
        }

        public void EndMoveGesture()
        {
            if (starterAssetsInputs != null)
                starterAssetsInputs.EndMoveGesture();
        }

        public void VirtualLookInput(Vector2 virtualLookDirection)
        {
            starterAssetsInputs.LookInput(virtualLookDirection);
        }

        public void VirtualJumpInput(bool virtualJumpState)
        {
            starterAssetsInputs.JumpInput(virtualJumpState);
        }

        public void VirtualSprintInput(bool virtualSprintState)
        {
            starterAssetsInputs.SprintInput(virtualSprintState);
        }
        
    }

}
