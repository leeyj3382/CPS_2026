using UnityEngine;
using CPS.Lab10.UR5e;

namespace CPS.ICPBL.Robots
{
    /// <summary>
    /// Down-facing position IK 솔버. RobotController.MoveArmTo 가 내부적으로 호출.
    /// 핵심 아이디어:
    ///   - CCD position IK 를 기반으로 하되, J5·J6 를 freeze 해서 TCP orientation 가변 차단.
    ///   - 매 outer iteration 마다 (J1·J2·J3 CCD 한 pass) + (J4 alignment 한 번) 끼워서
    ///     TCP grasp axis 가 world -Y(down) 와 정렬되도록 강제.
    ///   - 수렴 시 결과는 [Base yaw + Shoulder + Elbow + Wrist1(aligned)] 4개만 변하고,
    ///     J5·J6 는 inspector 초기값 그대로.
    /// </summary>
    [DisallowMultipleComponent]
    public class UR5eDownFacingIK : MonoBehaviour
    {
        [Header("참조 오브젝트 (Reset 으로 자동 wire)")]
        [SerializeField] private UR5eJointController jointController;
        [SerializeField] private UR5eJoint[] ikJoints = new UR5eJoint[UR5eJointPose.JointCount];
        [Tooltip("고정 base frame (회전하는 Joint1_Base 가 아님).")]
        [SerializeField] private Transform baseFrame;
        [Tooltip("Joint6_Wrist3 아래 TCP transform.")]
        [SerializeField] private Transform tcpTransform;

        [Header("IK 설정")]
        [SerializeField] private int maxIterations = 60;
        [SerializeField] private float positionTolerance = 0.02f;
        [SerializeField] private float orientationToleranceDeg = 1.0f;
        [SerializeField] private float maxAngleStepPerIteration = 8f;

        [Header("TCP grasp 축 (어떤 TCP local axis 가 grasp 방향인지)")]
        [Tooltip("SuctionGripper AttachPoint 의 grasp 방향. 기본 Forward(+Z).")]
        [SerializeField] private TcpAxis tcpGraspAxis = TcpAxis.Forward;

        public enum TcpAxis { Forward, Up, Right, NegForward, NegUp, NegRight }

        [Header("결과 로그")]
        [SerializeField] private bool verbose = false;

        public float PositionTolerance => positionTolerance;
        public Transform BaseFrame => baseFrame;
        public Transform TcpTransform => tcpTransform;
        public float LastPositionError { get; private set; }
        public float LastOrientationErrorDeg { get; private set; }

        private void Reset()
        {
            AutoWire();
        }

        private void OnValidate()
        {
            if (jointController == null) AutoWire();
        }

        private void Awake()
        {
            AutoWire();
        }

        private void AutoWire()
        {
            // IK 컴포넌트가 robot prefab 밖 별도 GameObject 에 부착되어도
            // robot 내부 joint 들을 찾을 수 있도록 Scene 전역 검색 fallback.
            if (jointController == null)
            {
                jointController = GetComponentInChildren<UR5eJointController>(true);
                if (jointController == null)
                    jointController = FindObjectOfType<UR5eJointController>(true);
            }

            Transform searchRoot = jointController != null ? jointController.transform.root : transform;

            if (ikJoints == null || ikJoints.Length != 6 || ikJoints[0] == null)
            {
                ikJoints = new UR5eJoint[6];
                string[] names = { "Joint1_Base", "Joint2_Shoulder", "Joint3_Elbow",
                                   "Joint4_Wrist1", "Joint5_Wrist2", "Joint6_Wrist3" };
                var all = searchRoot.GetComponentsInChildren<UR5eJoint>(true);
                foreach (var j in all)
                    for (int i = 0; i < 6; i++)
                        if (j.name == names[i]) { ikJoints[i] = j; break; }
            }

            if (baseFrame == null)
                baseFrame = FindByName(searchRoot, "ArmMountPoint") ?? FindByName(searchRoot, "BaseFrame") ?? searchRoot;

            if (tcpTransform == null)
                tcpTransform = FindByName(searchRoot, "AttachPoint") ?? FindByName(searchRoot, "TCP");
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            Transform p = root.parent;
            while (p != null) { if (p.name == name) return p; p = p.parent; }
            foreach (Transform c in root.GetComponentsInChildren<Transform>(true))
                if (c.name == name) return c;
            return null;
        }

        /// <summary>World target 으로 IK 풀기. solved=true 면 position + orientation 둘 다 tolerance 안.</summary>
        public bool Solve(Vector3 targetWorld, out UR5eJointPose solvedPose)
        {
            solvedPose = new UR5eJointPose();
            if (!ValidateReferences()) return false;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                // Inner: J1·J2·J3 CCD (단일 pass)
                CcdSinglePassPositionOnly(targetWorld);

                // Then: J4 align → TCP grasp axis → world -Y
                AlignJoint4ToDown();

                // Convergence check
                float posErr = Vector3.Distance(tcpTransform.position, targetWorld);
                float oriErr = AngleBetweenTcpAndDown();

                LastPositionError = posErr;
                LastOrientationErrorDeg = oriErr;

                if (verbose && iter % 10 == 0)
                    Debug.Log($"[DownFacingIK] iter={iter} posErr={posErr:F4} oriErr={oriErr:F2}°");

                if (posErr <= positionTolerance && oriErr <= orientationToleranceDeg)
                {
                    solvedPose = jointController.GetCurrentPose().Copy();
                    return true;
                }
            }

            solvedPose = jointController.GetCurrentPose().Copy();
            return false;
        }

        /// <summary>J1·J2·J3 만 위치 보정 1 pass (CCD backward 순서).</summary>
        private void CcdSinglePassPositionOnly(Vector3 targetWorld)
        {
            for (int j = 2; j >= 0; j--)   // J3 → J2 → J1
            {
                float delta = ComputeSignedAngleForJoint(j, targetWorld);
                delta = Mathf.Clamp(delta, -maxAngleStepPerIteration, maxAngleStepPerIteration);
                var joint = ikJoints[j];
                joint.SetAngle(joint.CurrentAngle + delta);
            }
        }

        /// <summary>J4 회전으로 TCP grasp 축을 world -Y 와 정렬.
        /// J4 axis 에 수직인 평면에서 (현재 tcp 축, target down) 사이 signed angle 만큼 J4 회전.</summary>
        private void AlignJoint4ToDown()
        {
            var j4 = ikJoints[3];
            if (j4 == null) return;

            Vector3 j4Axis = GetJointAxisWorld(3);
            Vector3 tcpAxis = GetTcpAxisWorld();
            Vector3 targetDown = Vector3.down;

            Vector3 projTcp = Vector3.ProjectOnPlane(tcpAxis, j4Axis);
            Vector3 projTarget = Vector3.ProjectOnPlane(targetDown, j4Axis);
            if (projTcp.sqrMagnitude < 1e-8f || projTarget.sqrMagnitude < 1e-8f) return;

            float angle = Vector3.SignedAngle(projTcp, projTarget, j4Axis);
            angle = Mathf.Clamp(angle, -maxAngleStepPerIteration, maxAngleStepPerIteration);
            j4.SetAngle(j4.CurrentAngle + angle);
        }

        private float ComputeSignedAngleForJoint(int jointIndex, Vector3 targetWorld)
        {
            Vector3 jointPos = ikJoints[jointIndex].JointTransform.position;
            Vector3 axisWorld = GetJointAxisWorld(jointIndex);
            Vector3 toTcp = tcpTransform.position - jointPos;
            Vector3 toTarget = targetWorld - jointPos;
            Vector3 pTcp = Vector3.ProjectOnPlane(toTcp, axisWorld);
            Vector3 pTar = Vector3.ProjectOnPlane(toTarget, axisWorld);
            if (pTcp.sqrMagnitude < 1e-8f || pTar.sqrMagnitude < 1e-8f) return 0f;
            return Vector3.SignedAngle(pTcp, pTar, axisWorld);
        }

        private Vector3 GetJointAxisWorld(int jointIndex)
        {
            var joint = ikJoints[jointIndex];
            Vector3 local = joint.LocalAxis.sqrMagnitude > Mathf.Epsilon
                ? joint.LocalAxis.normalized
                : Vector3.up;
            return joint.JointTransform.TransformDirection(local).normalized;
        }

        private Vector3 GetTcpAxisWorld()
        {
            switch (tcpGraspAxis)
            {
                case TcpAxis.Forward:    return tcpTransform.forward;
                case TcpAxis.Up:         return tcpTransform.up;
                case TcpAxis.Right:      return tcpTransform.right;
                case TcpAxis.NegForward: return -tcpTransform.forward;
                case TcpAxis.NegUp:      return -tcpTransform.up;
                case TcpAxis.NegRight:   return -tcpTransform.right;
                default:                 return tcpTransform.forward;
            }
        }

        private float AngleBetweenTcpAndDown()
        {
            return Vector3.Angle(GetTcpAxisWorld(), Vector3.down);
        }

        private bool ValidateReferences()
        {
            if (jointController == null) { Debug.LogWarning("[DownFacingIK] jointController missing.", this); return false; }
            if (tcpTransform == null) { Debug.LogWarning("[DownFacingIK] tcpTransform missing.", this); return false; }
            if (ikJoints == null || ikJoints.Length != 6) return false;
            for (int i = 0; i < 6; i++) if (ikJoints[i] == null) { Debug.LogWarning($"[DownFacingIK] joint[{i}] missing."); return false; }
            return true;
        }
    }
}
