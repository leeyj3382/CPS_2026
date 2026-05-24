using UnityEngine;

namespace CPS.Lab10.UR5e
{
    [DisallowMultipleComponent]
    public class UR5eMoveJController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private UR5eJointController jointController;

        [Header("MoveJ Settings")]
        [SerializeField] private float defaultDuration = 2f;
        [SerializeField] private bool useSmoothStepInterpolation;

        private Coroutine activeMove;

        public bool IsMoving => activeMove != null;

        private void Reset()
        {
            jointController = GetComponent<UR5eJointController>();
        }

        public void MoveJToPose(UR5eJointPose targetPose)
        {
            MoveJToPose(targetPose, defaultDuration);
        }

        public void MoveJToPose(UR5eJointPose targetPose, float duration)
        {
            StopMove();

            activeMove = StartCoroutine(MoveJ(targetPose, duration));
        }

        public void StopMove()
        {
            if (activeMove == null)
            {
                return;
            }

            StopCoroutine(activeMove);
            activeMove = null;
        }

        public System.Collections.IEnumerator MoveJ(UR5eJointPose targetPose, float duration)
        {
            if (jointController == null)
            {
                Debug.LogWarning("MoveJ requires an assigned UR5eJointController.", this);
                activeMove = null;
                yield break;
            }

            if (targetPose == null)
            {
                Debug.LogWarning("MoveJ target pose is null.", this);
                activeMove = null;
                yield break;
            }

            UR5eJointPose startPose = jointController.GetCurrentPose();
            float safeDuration = Mathf.Max(0.0001f, duration);
            float elapsed = 0f;

            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / safeDuration);

                if (useSmoothStepInterpolation)
                {
                    t = t * t * (3f - 2f * t);
                }

                jointController.SetPose(UR5eJointPose.Lerp(startPose, targetPose, t));
                yield return null;
            }

            jointController.SetPose(targetPose);
            activeMove = null;
        }
    }
}
