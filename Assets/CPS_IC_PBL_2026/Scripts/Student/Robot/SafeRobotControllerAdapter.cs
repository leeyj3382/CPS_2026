using System;
using System.Collections;
using CPS.ICPBL.Common;
using CPS.ICPBL.Robots;
using CPS.Lab10.UR5e;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    public interface IArmMotionGuard
    {
        bool TryValidateArmTarget(
            Vector3 worldPos,
            out float positionError,
            out float orientationErrorDeg,
            out string message);

        bool ConsumeLastArmMoveFailure(out string message);
    }

    public sealed class SafeRobotControllerAdapter : IRobotController, IArmMotionGuard
    {
        public sealed class Settings
        {
            public bool AllowPositionOnlyFallback = true;
            public float PositionOnlyFallbackTolerance = 0.04f;
            public float MaxFallbackOrientationErrorDeg = 25f;
            public int PositionOnlyFallbackMaxIterations = 80;
            public float PositionOnlyFallbackMaxAngleStepDeg = 8f;
            public float MinArmMoveDurationSec = 0.01f;
            public ITelemetryLogger TelemetryLogger;
        }

        private readonly IRobotController inner;
        private readonly MonoBehaviour coroutineHost;
        private Settings settings;
        private UR5eDownFacingIK downFacingIk;
        private UR5eJointController jointController;
        private UR5eJoint[] ikJoints;
        private Transform tcpTransform;
        private Coroutine activeArmSequence;
        private bool armBusy;
        private string lastArmMoveFailure = string.Empty;

        private SafeRobotControllerAdapter(IRobotController inner, Settings settings)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            coroutineHost = inner as MonoBehaviour;
            this.settings = NormalizeSettings(settings);
            ResolveArmComponents();
        }

        public static IRobotController Wrap(IRobotController controller, Settings settings)
        {
            if (controller == null)
            {
                return null;
            }

            if (controller is SafeRobotControllerAdapter existing)
            {
                existing.settings = NormalizeSettings(settings);
                existing.ResolveArmComponents();
                return existing;
            }

            return controller is MonoBehaviour
                ? new SafeRobotControllerAdapter(controller, settings)
                : controller;
        }

        public static IRobotController Unwrap(IRobotController controller)
        {
            return controller is SafeRobotControllerAdapter adapter
                ? adapter.inner
                : controller;
        }

        public int RobotId => inner.RobotId;
        public Vector3 Position => inner.Position;
        public bool IsBusy => inner.IsBusy || armBusy;

        public void GoToOperatingStation(int stationId)
        {
            inner.GoToOperatingStation(stationId);
        }

        public void MoveBaseTo(Vector3 worldPos, Action onArrived = null)
        {
            inner.MoveBaseTo(worldPos, onArrived);
        }

        public void MoveArmTo(
            Vector3 worldPos,
            Quaternion worldRot,
            float duration = 1.0f,
            Action onArrived = null)
        {
            lastArmMoveFailure = string.Empty;

            if (!ResolveArmComponents())
            {
                inner.MoveArmTo(worldPos, worldRot, duration, onArrived);
                return;
            }

            if (!TryCalculateArmPose(
                worldPos,
                out UR5eJointPose startPose,
                out UR5eJointPose targetPose,
                out _,
                out _,
                out string message))
            {
                lastArmMoveFailure = message;
                Log("ArmIK", message);
                onArrived?.Invoke();
                return;
            }

            if (activeArmSequence != null)
            {
                coroutineHost.StopCoroutine(activeArmSequence);
                activeArmSequence = null;
                armBusy = false;
            }

            activeArmSequence = coroutineHost.StartCoroutine(
                MoveArmSequence(startPose, targetPose, duration, onArrived));
        }

        public bool TryValidateArmTarget(
            Vector3 worldPos,
            out float positionError,
            out float orientationErrorDeg,
            out string message)
        {
            positionError = 0f;
            orientationErrorDeg = 0f;
            message = string.Empty;

            if (!ResolveArmComponents())
            {
                return true;
            }

            return TryCalculateArmPose(
                worldPos,
                out _,
                out _,
                out positionError,
                out orientationErrorDeg,
                out message);
        }

        public bool ConsumeLastArmMoveFailure(out string message)
        {
            message = lastArmMoveFailure;
            lastArmMoveFailure = string.Empty;
            return !string.IsNullOrEmpty(message);
        }

        private IEnumerator MoveArmSequence(
            UR5eJointPose startPose,
            UR5eJointPose targetPose,
            float durationSec,
            Action onArrived)
        {
            armBusy = true;
            jointController.SetPose(startPose);

            float duration = Mathf.Max(settings.MinArmMoveDurationSec, durationSec);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                jointController.SetPose(UR5eJointPose.Lerp(startPose, targetPose, t));
                yield return null;
            }

            jointController.SetPose(targetPose);
            activeArmSequence = null;
            armBusy = false;
            onArrived?.Invoke();
        }

        private bool TryCalculateArmPose(
            Vector3 worldPos,
            out UR5eJointPose startPose,
            out UR5eJointPose targetPose,
            out float positionError,
            out float orientationErrorDeg,
            out string message)
        {
            startPose = jointController.GetCurrentPose().Copy();
            targetPose = startPose.Copy();

            if (TryCalculateDownFacingPose(
                worldPos,
                startPose,
                out UR5eJointPose downFacingPose,
                out positionError,
                out orientationErrorDeg))
            {
                targetPose = downFacingPose.Copy();
                message = string.Empty;
                return true;
            }

            float strictPositionError = positionError;
            float strictOrientationErrorDeg = orientationErrorDeg;
            if (settings.AllowPositionOnlyFallback)
            {
                if (TryCalculatePositionOnlyPose(
                    worldPos,
                    startPose,
                    out UR5eJointPose positionOnlyPose,
                    out float fallbackPositionError,
                    out float fallbackOrientationErrorDeg)
                    && fallbackPositionError <= settings.PositionOnlyFallbackTolerance
                    && fallbackOrientationErrorDeg <= settings.MaxFallbackOrientationErrorDeg)
                {
                    positionError = fallbackPositionError;
                    orientationErrorDeg = fallbackOrientationErrorDeg;
                    targetPose = positionOnlyPose.Copy();
                    message = string.Format(
                        "Down-facing IK failed strict tolerance for target={0}; using independent position-only fallback pose. strictPosErr={1:0.000}m, strictOriErr={2:0.0}deg, fallbackPosErr={3:0.000}m, fallbackOriErr={4:0.0}deg.",
                        worldPos,
                        strictPositionError,
                        strictOrientationErrorDeg,
                        positionError,
                        orientationErrorDeg);
                    Log("ArmIK", message);
                    return true;
                }
            }

            message = string.Format(
                "IK failed for target={0}; strictPosErr={1:0.000}m, strictOriErr={2:0.0}deg. Failed pose was restored and not applied.",
                worldPos,
                positionError,
                orientationErrorDeg);
            return false;
        }

        private bool TryCalculateDownFacingPose(
            Vector3 worldPos,
            UR5eJointPose startPose,
            out UR5eJointPose targetPose,
            out float positionError,
            out float orientationErrorDeg)
        {
            bool solved = downFacingIk.Solve(worldPos, out UR5eJointPose solvedPose);
            positionError = downFacingIk.LastPositionError;
            orientationErrorDeg = downFacingIk.LastOrientationErrorDeg;
            targetPose = solved ? solvedPose.Copy() : startPose.Copy();
            jointController.SetPose(startPose);
            return solved;
        }

        private bool TryCalculatePositionOnlyPose(
            Vector3 worldPos,
            UR5eJointPose startPose,
            out UR5eJointPose targetPose,
            out float positionError,
            out float orientationErrorDeg)
        {
            targetPose = startPose.Copy();
            positionError = float.PositiveInfinity;
            orientationErrorDeg = float.PositiveInfinity;

            if (!ResolvePositionOnlyComponents())
            {
                jointController.SetPose(startPose);
                return false;
            }

            jointController.SetPose(startPose);
            int maxIterations = Mathf.Max(1, settings.PositionOnlyFallbackMaxIterations);
            float maxStep = Mathf.Max(0.1f, settings.PositionOnlyFallbackMaxAngleStepDeg);

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                positionError = GetTcpDistanceTo(worldPos);
                if (positionError <= settings.PositionOnlyFallbackTolerance)
                {
                    break;
                }

                for (int jointIndex = ikJoints.Length - 1; jointIndex >= 0; jointIndex--)
                {
                    float delta = ComputeSignedAngleForJoint(jointIndex, worldPos);
                    delta = Mathf.Clamp(delta, -maxStep, maxStep);
                    UR5eJoint joint = ikJoints[jointIndex];
                    joint.SetAngle(joint.CurrentAngle + delta);

                    positionError = GetTcpDistanceTo(worldPos);
                    if (positionError <= settings.PositionOnlyFallbackTolerance)
                    {
                        break;
                    }
                }
            }

            positionError = GetTcpDistanceTo(worldPos);
            orientationErrorDeg = GetTcpDownOrientationErrorDeg();
            targetPose = jointController.GetCurrentPose().Copy();
            jointController.SetPose(startPose);
            return positionError <= settings.PositionOnlyFallbackTolerance;
        }

        private float ComputeSignedAngleForJoint(int jointIndex, Vector3 targetWorld)
        {
            UR5eJoint joint = ikJoints[jointIndex];
            if (joint == null || joint.JointTransform == null || tcpTransform == null)
            {
                return 0f;
            }

            Vector3 jointPosition = joint.JointTransform.position;
            Vector3 axisWorld = GetJointAxisWorld(joint);
            Vector3 toTcp = tcpTransform.position - jointPosition;
            Vector3 toTarget = targetWorld - jointPosition;
            Vector3 projectedTcp = Vector3.ProjectOnPlane(toTcp, axisWorld);
            Vector3 projectedTarget = Vector3.ProjectOnPlane(toTarget, axisWorld);
            if (projectedTcp.sqrMagnitude <= 1e-8f
                || projectedTarget.sqrMagnitude <= 1e-8f)
            {
                return 0f;
            }

            return Vector3.SignedAngle(projectedTcp, projectedTarget, axisWorld);
        }

        private static Vector3 GetJointAxisWorld(UR5eJoint joint)
        {
            Vector3 localAxis = joint.LocalAxis.sqrMagnitude > Mathf.Epsilon
                ? joint.LocalAxis.normalized
                : Vector3.up;
            return joint.JointTransform.TransformDirection(localAxis).normalized;
        }

        private float GetTcpDistanceTo(Vector3 worldPos)
        {
            return tcpTransform != null
                ? Vector3.Distance(tcpTransform.position, worldPos)
                : float.PositiveInfinity;
        }

        private float GetTcpDownOrientationErrorDeg()
        {
            return tcpTransform != null
                ? Vector3.Angle(tcpTransform.forward, Vector3.down)
                : float.PositiveInfinity;
        }

        private bool ResolveArmComponents()
        {
            if (coroutineHost == null)
            {
                return false;
            }

            if (downFacingIk == null)
            {
                downFacingIk = coroutineHost.GetComponentInChildren<UR5eDownFacingIK>(true);
            }

            if (jointController == null)
            {
                jointController = coroutineHost.GetComponentInChildren<UR5eJointController>(true);
            }

            return downFacingIk != null && jointController != null;
        }

        private bool ResolvePositionOnlyComponents()
        {
            if (coroutineHost == null || jointController == null)
            {
                return false;
            }

            if (tcpTransform == null && downFacingIk != null)
            {
                tcpTransform = downFacingIk.TcpTransform;
            }

            if (tcpTransform == null)
            {
                Transform root = coroutineHost.transform;
                tcpTransform = FindByName(root, "AttachPoint") ?? FindByName(root, "TCP");
            }

            if (!HasAllJoints(ikJoints))
            {
                ikJoints = FindIkJoints();
            }

            return tcpTransform != null && HasAllJoints(ikJoints);
        }

        private UR5eJoint[] FindIkJoints()
        {
            var joints = new UR5eJoint[UR5eJointPose.JointCount];
            UR5eJoint[] candidates = coroutineHost.GetComponentsInChildren<UR5eJoint>(true);
            for (int jointIndex = 0; jointIndex < joints.Length; jointIndex++)
            {
                string exactName = string.Format("Joint{0}", jointIndex + 1);
                for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
                {
                    UR5eJoint candidate = candidates[candidateIndex];
                    if (candidate != null && candidate.name.Contains(exactName))
                    {
                        joints[jointIndex] = candidate;
                        break;
                    }
                }
            }

            return joints;
        }

        private static bool HasAllJoints(UR5eJoint[] joints)
        {
            if (joints == null || joints.Length != UR5eJointPose.JointCount)
            {
                return false;
            }

            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null || joints[i].JointTransform == null)
                {
                    return false;
                }
            }

            return true;
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private void Log(string category, string message)
        {
            if (settings.TelemetryLogger != null)
            {
                settings.TelemetryLogger.LogMessage(category, message);
            }
            else if (coroutineHost != null)
            {
                Debug.Log(string.Format("[SafeRobotControllerAdapter] {0}", message), coroutineHost);
            }
        }

        private static Settings NormalizeSettings(Settings source)
        {
            Settings normalized = source ?? new Settings();
            normalized.PositionOnlyFallbackTolerance = Mathf.Max(
                0.001f,
                normalized.PositionOnlyFallbackTolerance);
            normalized.MaxFallbackOrientationErrorDeg = Mathf.Max(
                0f,
                normalized.MaxFallbackOrientationErrorDeg);
            normalized.PositionOnlyFallbackMaxIterations = Mathf.Max(
                1,
                normalized.PositionOnlyFallbackMaxIterations);
            normalized.PositionOnlyFallbackMaxAngleStepDeg = Mathf.Max(
                0.1f,
                normalized.PositionOnlyFallbackMaxAngleStepDeg);
            normalized.MinArmMoveDurationSec = Mathf.Max(
                0.001f,
                normalized.MinArmMoveDurationSec);
            return normalized;
        }
    }
}
