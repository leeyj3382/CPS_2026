using UnityEngine;

namespace CPS.Lab10.UR5e
{
    /// <summary>
    /// UR5e 의 position-only CCD IK 솔버. single seed, 위치만 보정.
    ///
    /// 분해 구조:
    ///   TryCalculatePoseForBaseTarget()      — 호출 흐름·복원
    ///     SolvePositionOnlyCCD()             — 외곽 iteration loop
    ///       ComputeSignedAngleForJoint()     — signed angle 계산
    ///         GetJointAxisWorld()            — local → world axis 변환
    /// </summary>
    [DisallowMultipleComponent]
    public class UR5ePositionOnlyIKSolver : MonoBehaviour
    {
        [Header("참조 오브젝트")]
        [SerializeField] private UR5eJointController jointController;
        [SerializeField] private UR5eJoint[] ikJoints = new UR5eJoint[UR5eJointPose.JointCount];

        [Tooltip("고정된 로봇 Base 좌표계입니다. 회전하는 Joint1_Base를 넣지 마세요.")]
        [SerializeField] private Transform baseFrame;

        [Tooltip("Joint6_Wrist3 아래에 있는 TCP Transform입니다.")]
        [SerializeField] private Transform tcpTransform;

        [Header("IK 설정")]
        [SerializeField] private int maxIterations = 80;
        [SerializeField] private float positionTolerance = 0.05f;
        [SerializeField] private float maxAngleStepPerIteration = 8f;
        [SerializeField] private bool restoreStartPoseAfterSolve = true;

        public float PositionTolerance => positionTolerance;

        private void Reset()
        {
            jointController = GetComponent<UR5eJointController>();
        }

        // === TODO 1 정답 ============================================================
        public bool TryCalculatePoseForBaseTarget(Vector3 targetPositionInBase, out UR5eJointPose solvedPose)
        {
            solvedPose = new UR5eJointPose();

            if (!ValidateReferences())
            {
                return false;
            }

            UR5eJointPose startPose = jointController.GetCurrentPose();
            Vector3 targetWorld = BaseToWorld(targetPositionInBase);

            bool success = SolvePositionOnlyCCD(targetWorld);

            solvedPose = jointController.GetCurrentPose().Copy();

            if (restoreStartPoseAfterSolve)
            {
                jointController.SetPose(startPose);
            }

            return success;
        }

        // === TODO 2 정답 ============================================================
        private bool SolvePositionOnlyCCD(Vector3 targetWorld)
        {
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                if (IsTargetReached(targetWorld))
                {
                    return true;
                }

                for (int jointIndex = ikJoints.Length - 1; jointIndex >= 0; jointIndex--)
                {
                    float deltaAngle = ComputeSignedAngleForJoint(jointIndex, targetWorld);
                    deltaAngle = Mathf.Clamp(deltaAngle, -maxAngleStepPerIteration, maxAngleStepPerIteration);

                    UR5eJoint joint = ikJoints[jointIndex];
                    joint.SetAngle(joint.CurrentAngle + deltaAngle);

                    if (IsTargetReached(targetWorld))
                    {
                        return true;
                    }
                }
            }

            return IsTargetReached(targetWorld);
        }

        // === TODO 3 정답 ============================================================
        private float ComputeSignedAngleForJoint(int jointIndex, Vector3 targetWorld)
        {
            Vector3 jointPosition = ikJoints[jointIndex].JointTransform.position;
            Vector3 axisWorld = GetJointAxisWorld(jointIndex);

            Vector3 toTcp = tcpTransform.position - jointPosition;
            Vector3 toTarget = targetWorld - jointPosition;

            Vector3 projectedTcp = Vector3.ProjectOnPlane(toTcp, axisWorld);
            Vector3 projectedTarget = Vector3.ProjectOnPlane(toTarget, axisWorld);

            if (projectedTcp.sqrMagnitude <= 1e-8f || projectedTarget.sqrMagnitude <= 1e-8f)
            {
                return 0f;
            }

            return Vector3.SignedAngle(projectedTcp, projectedTarget, axisWorld);
        }

        // === TODO 4 정답 ============================================================
        private Vector3 GetJointAxisWorld(int jointIndex)
        {
            UR5eJoint joint = ikJoints[jointIndex];
            Vector3 localAxis = joint.LocalAxis.sqrMagnitude > Mathf.Epsilon
                ? joint.LocalAxis.normalized
                : Vector3.up;
            return joint.JointTransform.TransformDirection(localAxis).normalized;
        }

        public Vector3 BaseToWorld(Vector3 pointInBase)
        {
            if (baseFrame == null)
            {
                return pointInBase;
            }

            return baseFrame.TransformPoint(pointInBase);
        }

        public Transform BaseFrame => baseFrame;
        public Transform TcpTransform => tcpTransform;

        public float GetTcpDistanceToWorldTarget(Vector3 targetWorld)
        {
            if (tcpTransform == null)
            {
                return float.PositiveInfinity;
            }

            return Vector3.Distance(tcpTransform.position, targetWorld);
        }

        private bool IsTargetReached(Vector3 targetWorld)
        {
            return GetTcpDistanceToWorldTarget(targetWorld) <= positionTolerance;
        }

        private bool ValidateReferences()
        {
            if (jointController == null)
            {
                Debug.LogWarning("IK solver requires a UR5eJointController.", this);
                return false;
            }

            if (tcpTransform == null)
            {
                Debug.LogWarning("IK solver requires a TCP Transform.", this);
                return false;
            }

            if (ikJoints == null || ikJoints.Length != UR5eJointPose.JointCount)
            {
                Debug.LogWarning("IK solver requires exactly 6 UR5eJoint references.", this);
                return false;
            }

            for (int i = 0; i < ikJoints.Length; i++)
            {
                if (ikJoints[i] == null || ikJoints[i].JointTransform == null)
                {
                    Debug.LogWarning("IK joint reference is missing at index " + i + ".", this);
                    return false;
                }
            }

            return true;
        }
    }
}
