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
            public float MinArmMoveDurationSec = 0.01f;
            public ITelemetryLogger TelemetryLogger;
        }

        private readonly IRobotController inner;
        private readonly MonoBehaviour coroutineHost;
        private Settings settings;
        private UR5eDownFacingIK downFacingIk;
        private UR5eJointController jointController;
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
            bool solved = downFacingIk.Solve(worldPos, out UR5eJointPose solvedPose);
            positionError = downFacingIk.LastPositionError;
            orientationErrorDeg = downFacingIk.LastOrientationErrorDeg;
            targetPose = solvedPose.Copy();
            jointController.SetPose(startPose);

            if (solved)
            {
                message = string.Empty;
                return true;
            }

            if (settings.AllowPositionOnlyFallback
                && positionError <= settings.PositionOnlyFallbackTolerance
                && orientationErrorDeg <= settings.MaxFallbackOrientationErrorDeg)
            {
                message = string.Format(
                    "Down-facing IK missed strict tolerance for target={0}, but position fallback is accepted; posErr={1:0.000}m, oriErr={2:0.0}deg.",
                    worldPos,
                    positionError,
                    orientationErrorDeg);
                Log("ArmIK", message);
                return true;
            }

            message = string.Format(
                "IK failed for target={0}; posErr={1:0.000}m, oriErr={2:0.0}deg. Failed pose was restored and not applied.",
                worldPos,
                positionError,
                orientationErrorDeg);
            return false;
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
            normalized.MinArmMoveDurationSec = Mathf.Max(
                0.001f,
                normalized.MinArmMoveDurationSec);
            return normalized;
        }
    }
}
