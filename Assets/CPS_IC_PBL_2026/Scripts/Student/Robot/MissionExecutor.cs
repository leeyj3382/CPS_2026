using System;
using System.Collections;
using System.Collections.Generic;
using CPS.ICPBL.Common;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    public sealed class MissionExecutor
    {
        public sealed class Dependencies
        {
            public IRobotController Controller;
            public GripperAdapter Gripper;
            public global::ColorSensor ColorSensor;
            public global::ColorArea ColorArea;
            public IPoseProvider PoseProvider;
            public IPalletizer Palletizer;
            public IColorClassifier ColorClassifier;
            public IResourceLockManager LockManager;
            public IPathPlanner PathPlanner;
            public ITelemetryLogger TelemetryLogger;
            public Func<int> GetCurrentStationId;
            public Action<int> SetCurrentStationId;
            public Action<RobotRuntimeState> SetState;
        }

        public sealed class Settings
        {
            public float MoveTimeoutSec = StudentConstants.DefaultMoveTimeoutSec;
            public float LockTimeoutSec = StudentConstants.DefaultLockTimeoutSec;
            public float GripReadyTimeoutSec = StudentConstants.DefaultGripReadyTimeoutSec;
            public float GripRetryWaitSec = 0.2f;
            public int GripRetryCount = 1;
            public float ColorRetryWaitSec = 0.1f;
            public int ColorRetryCount = 1;
        }

        private sealed class MissionContext
        {
            public readonly MissionRequest Request;
            public readonly MissionResult Result;
            public readonly List<ResourceLockToken> LockTokens =
                new List<ResourceLockToken>();

            public bool Failed;
            public bool SlotReserved;
            public bool SlotCommitted;
            public BoxSlotPose ReservedSlot;
            public BoxType DestinationBoxType;
            public ColorClassificationResult Classification;

            public MissionContext(MissionRequest request)
            {
                Request = request;
                Result = new MissionResult
                {
                    taskId = request != null ? request.taskId : StudentConstants.NoTaskId,
                    robotId = request != null ? request.robotId : StudentConstants.UnassignedRobotId,
                    conveyorId = request != null ? request.conveyorId : StudentConstants.NoStationId,
                    classificationResult = ClassificationResult.Unknown,
                    destinationStationId = StudentConstants.NoStationId,
                    failureReason = MissionFailureReason.None,
                    message = string.Empty,
                    startedAt = Time.time
                };
            }
        }

        private readonly Dependencies dependencies;
        private readonly Settings settings;

        public MissionExecutor(Dependencies dependencies, Settings settings = null)
        {
            this.dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            this.settings = settings ?? new Settings();
        }

        public IEnumerator Execute(MissionRequest request, Action<MissionResult> onFinished)
        {
            var context = new MissionContext(request);
            if (!ValidateRequestAndDependencies(context))
            {
                Finish(context, onFinished);
                yield break;
            }

            dependencies.SetState?.Invoke(RobotRuntimeState.Reserved);

            ResourceKey conveyorKey = new ResourceKey(
                LockResourceType.Conveyor,
                request.conveyorId);

            yield return AcquireCentralZoneIfNeeded(context, request.conveyorId);
            if (!context.Failed)
            {
                yield return AcquireLock(
                    context,
                    conveyorKey,
                    MissionFailureReason.CollisionRisk,
                    "conveyor lock");
            }

            if (!context.Failed)
            {
                yield return MoveToStation(
                    context,
                    request.conveyorId,
                    RobotRuntimeState.MovingToConveyor,
                    "conveyor station");
            }

            ReleaseKey(context, new ResourceKey(
                LockResourceType.CentralZone,
                StudentConstants.CentralZoneResourceId));

            if (!context.Failed)
            {
                yield return RunPickSequence(context);
            }

            if (!context.Failed)
            {
                yield return RunClassification(context);
            }

            ReleaseKey(context, conveyorKey);

            if (!context.Failed)
            {
                ResourceKey boxKey = new ResourceKey(
                    StudentConstants.GetBoxLockType(context.DestinationBoxType),
                    context.Result.destinationStationId);

                yield return AcquireCentralZoneIfNeeded(context, context.Result.destinationStationId);
                if (!context.Failed)
                {
                    yield return AcquireLock(
                        context,
                        boxKey,
                        MissionFailureReason.BoxLockFailed,
                        "box lock");
                }

                if (!context.Failed)
                {
                    yield return MoveToStation(
                        context,
                        context.Result.destinationStationId,
                        RobotRuntimeState.MovingToBox,
                        "box station");
                }

                ReleaseKey(context, new ResourceKey(
                    LockResourceType.CentralZone,
                    StudentConstants.CentralZoneResourceId));

                if (!context.Failed)
                {
                    yield return RunPlaceSequence(context);
                }

                ReleaseKey(context, boxKey);
            }

            if (!context.Failed)
            {
                context.Result.success = true;
                context.Result.failureReason = MissionFailureReason.None;
                context.Result.message = "OK";
            }

            Finish(context, onFinished);
        }

        private bool ValidateRequestAndDependencies(MissionContext context)
        {
            MissionRequest request = context.Request;
            if (request == null)
            {
                Fail(context, MissionFailureReason.Unknown, "MissionRequest is null.");
                return false;
            }

            if (dependencies.Controller == null)
            {
                Fail(context, MissionFailureReason.Unknown, "IRobotController reference is missing.");
                return false;
            }

            if (dependencies.Gripper == null || !dependencies.Gripper.IsConfigured)
            {
                Fail(context, MissionFailureReason.GripFailed, "SuctionGripper reference is missing.");
                return false;
            }

            if (dependencies.PoseProvider == null)
            {
                Fail(context, MissionFailureReason.Unknown, "IPoseProvider reference is missing.");
                return false;
            }

            if (dependencies.Palletizer == null)
            {
                Fail(context, MissionFailureReason.PlaceFailed, "IPalletizer reference is missing.");
                return false;
            }

            if (dependencies.ColorClassifier == null)
            {
                Fail(context, MissionFailureReason.ClassificationFailed, "IColorClassifier reference is missing.");
                return false;
            }

            if (!HasColorSource())
            {
                Fail(context, MissionFailureReason.ClassificationFailed, "ColorSensor or ColorArea reference is missing.");
                return false;
            }

            if (!StudentConstants.IsConveyorId(request.conveyorId))
            {
                Fail(context, MissionFailureReason.Unknown, "Invalid conveyor id.");
                return false;
            }

            if (request.robotId != dependencies.Controller.RobotId)
            {
                Fail(context, MissionFailureReason.Unknown, "Mission robot id does not match controller robot id.");
                return false;
            }

            return true;
        }

        private bool HasColorSource()
        {
            return dependencies.ColorArea != null
                || (dependencies.ColorSensor != null && dependencies.ColorSensor.area != null);
        }

        private IEnumerator AcquireCentralZoneIfNeeded(MissionContext context, int toStationId)
        {
            if (dependencies.PathPlanner == null)
            {
                yield break;
            }

            int fromStationId = dependencies.GetCurrentStationId != null
                ? dependencies.GetCurrentStationId()
                : StudentConstants.NoStationId;
            if (fromStationId == StudentConstants.NoStationId)
            {
                yield break;
            }

            if (!dependencies.PathPlanner.RequiresCentralZone(
                context.Request.robotId,
                fromStationId,
                toStationId))
            {
                yield break;
            }

            yield return AcquireLock(
                context,
                new ResourceKey(LockResourceType.CentralZone, StudentConstants.CentralZoneResourceId),
                MissionFailureReason.CollisionRisk,
                "central zone lock");
        }

        private IEnumerator AcquireLock(
            MissionContext context,
            ResourceKey key,
            MissionFailureReason failureReason,
            string label)
        {
            if (dependencies.LockManager == null)
            {
                LogMessage("Lock", string.Format(
                    "No lock manager; proceeding without {0} for task={1}.",
                    label,
                    context.Request.taskId));
                yield break;
            }

            dependencies.SetState?.Invoke(RobotRuntimeState.WaitingForLock);
            float deadline = Time.time + Mathf.Max(0f, settings.LockTimeoutSec);
            while (Time.time <= deadline)
            {
                if (dependencies.LockManager.TryAcquire(
                    key,
                    context.Request.robotId,
                    context.Request.taskId,
                    out ResourceLockToken token))
                {
                    context.LockTokens.Add(token);
                    dependencies.TelemetryLogger?.LogLock(
                        "Acquire",
                        key,
                        context.Request.robotId,
                        context.Request.taskId);
                    yield break;
                }

                yield return null;
            }

            dependencies.TelemetryLogger?.LogLock(
                "Timeout",
                key,
                context.Request.robotId,
                context.Request.taskId);
            Fail(context, failureReason, string.Format("Timed out waiting for {0}: {1}.", label, key));
        }

        private IEnumerator MoveToStation(
            MissionContext context,
            int stationId,
            RobotRuntimeState state,
            string label)
        {
            dependencies.SetState?.Invoke(state);
            dependencies.Controller.GoToOperatingStation(stationId);
            yield return WaitForControllerIdle(context, settings.MoveTimeoutSec, label);

            if (!context.Failed)
            {
                dependencies.SetCurrentStationId?.Invoke(stationId);
            }
        }

        private IEnumerator RunPickSequence(MissionContext context)
        {
            dependencies.SetState?.Invoke(RobotRuntimeState.Picking);
            StationPose pose = dependencies.PoseProvider.GetConveyorPickPose(
                context.Request.conveyorId);
            if (pose == null)
            {
                Fail(context, MissionFailureReason.Unknown, "PoseProvider returned null conveyor pick pose.");
                yield break;
            }

            ResourceKey armKey = new ResourceKey(
                LockResourceType.RobotArmZone,
                context.Request.conveyorId);
            yield return AcquireLock(
                context,
                armKey,
                MissionFailureReason.CollisionRisk,
                "conveyor arm zone lock");
            if (context.Failed)
            {
                yield break;
            }

            dependencies.SetState?.Invoke(RobotRuntimeState.Picking);
            yield return MoveArmTo(context, pose.approachPos, pose.armMoveDuration, "pick approach");
            if (context.Failed)
            {
                yield break;
            }

            yield return MoveArmTo(context, pose.actionPos, pose.armMoveDuration, "pick action");
            if (context.Failed)
            {
                yield break;
            }

            yield return GripWithRetry(context);
            if (context.Failed)
            {
                yield break;
            }

            dependencies.SetState?.Invoke(RobotRuntimeState.Retracting);
            yield return MoveArmTo(context, pose.retractPos, pose.armMoveDuration, "pick retract");
            ReleaseKey(context, armKey);
        }

        private IEnumerator GripWithRetry(MissionContext context)
        {
            int attempts = Mathf.Max(0, settings.GripRetryCount) + 1;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                yield return dependencies.Gripper.WaitUntilGraspReady(settings.GripReadyTimeoutSec);
                if (dependencies.Gripper.TryGrip(out string reason))
                {
                    LogMessage("Grip", string.Format(
                        "Grip success task={0} robot={1} attempt={2}.",
                        context.Request.taskId,
                        context.Request.robotId,
                        attempt + 1));
                    yield break;
                }

                LogMessage("Grip", string.Format(
                    "Grip failed task={0} robot={1} attempt={2}, reason={3}.",
                    context.Request.taskId,
                    context.Request.robotId,
                    attempt + 1,
                    string.IsNullOrEmpty(reason) ? dependencies.Gripper.LastFailureReason : reason));

                if (attempt + 1 < attempts)
                {
                    yield return new WaitForSeconds(Mathf.Max(0f, settings.GripRetryWaitSec));
                }
            }

            Fail(context, MissionFailureReason.GripFailed, dependencies.Gripper.LastFailureReason);
        }

        private IEnumerator RunClassification(MissionContext context)
        {
            dependencies.SetState?.Invoke(RobotRuntimeState.Inspecting);

            int attempts = Mathf.Max(0, settings.ColorRetryCount) + 1;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Color sensedColor = ReadSensedColor();
                ColorClassificationResult classification =
                    dependencies.ColorClassifier.Classify(sensedColor);
                context.Classification = classification;

                if (classification != null)
                {
                    context.Result.classificationResult = classification.result;
                    LogMessage("Color", string.Format(
                        "Classified task={0} robot={1} result={2} reliable={3} blueDistance={4:0.000} redDistance={5:0.000}.",
                        context.Request.taskId,
                        context.Request.robotId,
                        classification.result,
                        classification.reliable,
                        classification.blueDistance,
                        classification.redDistance));

                    if (classification.reliable
                        && StudentConstants.TryGetBoxType(
                            classification.result,
                            out BoxType boxType))
                    {
                        context.DestinationBoxType = boxType;
                        context.Result.destinationStationId =
                            StudentConstants.GetBoxStationId(boxType);
                        yield break;
                    }
                }

                if (attempt + 1 < attempts)
                {
                    yield return new WaitForSeconds(Mathf.Max(0f, settings.ColorRetryWaitSec));
                }
            }

            Fail(context, MissionFailureReason.ClassificationFailed, "Color classification was unreliable or unknown.");
        }

        private Color ReadSensedColor()
        {
            if (dependencies.ColorSensor != null && dependencies.ColorSensor.area != null)
            {
                return dependencies.ColorSensor.area.color;
            }

            return dependencies.ColorArea != null
                ? dependencies.ColorArea.color
                : StudentConstants.DefaultSensorColor;
        }

        private IEnumerator RunPlaceSequence(MissionContext context)
        {
            context.ReservedSlot = dependencies.Palletizer.ReserveNextSlot(
                context.DestinationBoxType,
                context.Request.robotId,
                context.Request.taskId);
            context.SlotReserved = context.ReservedSlot != null;

            if (context.ReservedSlot == null)
            {
                Fail(context, MissionFailureReason.PlaceFailed, "Palletizer returned null slot.");
                yield break;
            }

            dependencies.SetState?.Invoke(RobotRuntimeState.Placing);
            ResourceKey armKey = new ResourceKey(
                LockResourceType.RobotArmZone,
                context.Result.destinationStationId);
            yield return AcquireLock(
                context,
                armKey,
                MissionFailureReason.CollisionRisk,
                "box arm zone lock");
            if (context.Failed)
            {
                yield break;
            }

            dependencies.SetState?.Invoke(RobotRuntimeState.Placing);
            yield return MoveArmTo(context, context.ReservedSlot.approachPos, StudentConstants.DefaultArmMoveDurationSec, "place approach");
            if (context.Failed)
            {
                yield break;
            }

            yield return MoveArmTo(context, context.ReservedSlot.placePos, StudentConstants.DefaultArmMoveDurationSec, "place action");
            if (context.Failed)
            {
                yield break;
            }

            dependencies.SetState?.Invoke(RobotRuntimeState.Releasing);
            dependencies.Gripper.Release();
            yield return null;
            if (dependencies.Gripper.IsHolding)
            {
                Fail(context, MissionFailureReason.PlaceFailed, "Release completed but gripper still holds the object.");
                yield break;
            }

            dependencies.Palletizer.CommitSlot(context.Request.taskId);
            context.SlotCommitted = true;

            yield return MoveArmTo(context, context.ReservedSlot.retractPos, StudentConstants.DefaultArmMoveDurationSec, "place retract");
            if (context.Failed)
            {
                yield break;
            }

            ReleaseKey(context, armKey);
        }

        private IEnumerator MoveArmTo(
            MissionContext context,
            Vector3 worldPos,
            float durationSec,
            string label)
        {
            dependencies.Controller.MoveArmTo(
                worldPos,
                Quaternion.identity,
                Mathf.Max(0.01f, durationSec));
            yield return WaitForControllerIdle(context, settings.MoveTimeoutSec, label);
        }

        private IEnumerator WaitForControllerIdle(
            MissionContext context,
            float timeoutSec,
            string label)
        {
            float deadline = Time.time + Mathf.Max(0f, timeoutSec);
            while (dependencies.Controller.IsBusy)
            {
                if (Time.time > deadline)
                {
                    dependencies.SetState?.Invoke(RobotRuntimeState.Stuck);
                    Fail(context, MissionFailureReason.MoveTimeout, string.Format(
                        "Timed out while waiting for {0}.",
                        label));
                    yield break;
                }

                yield return null;
            }
        }

        private void ReleaseKey(MissionContext context, ResourceKey key)
        {
            if (dependencies.LockManager == null)
            {
                return;
            }

            for (int i = context.LockTokens.Count - 1; i >= 0; i--)
            {
                ResourceLockToken token = context.LockTokens[i];
                if (token != null && token.key == key)
                {
                    dependencies.LockManager.Release(token);
                    context.LockTokens.RemoveAt(i);
                    dependencies.TelemetryLogger?.LogLock(
                        "Release",
                        key,
                        context.Request.robotId,
                        context.Request.taskId);
                    return;
                }
            }
        }

        private void ReleaseAllLocks(MissionContext context)
        {
            if (dependencies.LockManager == null)
            {
                context.LockTokens.Clear();
                return;
            }

            for (int i = context.LockTokens.Count - 1; i >= 0; i--)
            {
                ResourceLockToken token = context.LockTokens[i];
                if (token == null)
                {
                    continue;
                }

                dependencies.LockManager.Release(token);
                dependencies.TelemetryLogger?.LogLock(
                    "Release",
                    token.key,
                    context.Request.robotId,
                    context.Request.taskId);
            }

            context.LockTokens.Clear();
        }

        private void Finish(MissionContext context, Action<MissionResult> onFinished)
        {
            if (context.Failed)
            {
                CleanupFailure(context);
            }

            ReleaseAllLocks(context);
            context.Result.finishedAt = Time.time;
            onFinished?.Invoke(context.Result);
        }

        private void CleanupFailure(MissionContext context)
        {
            if (context.SlotReserved && !context.SlotCommitted)
            {
                dependencies.Palletizer?.ReleaseSlot(context.Request.taskId);
                context.SlotReserved = false;
            }

            if (dependencies.Gripper != null && dependencies.Gripper.IsHolding)
            {
                LogMessage("Grip", string.Format(
                    "Emergency release after mission failure task={0} robot={1}.",
                    context.Request.taskId,
                    context.Request.robotId));
                dependencies.Gripper.Release();
            }
        }

        private void Fail(MissionContext context, MissionFailureReason reason, string message)
        {
            if (context.Failed)
            {
                return;
            }

            context.Failed = true;
            context.Result.success = false;
            context.Result.failureReason = reason;
            context.Result.message = string.IsNullOrEmpty(message) ? reason.ToString() : message;
            LogMessage("Mission", string.Format(
                "Mission failed task={0} robot={1} reason={2} message={3}.",
                context.Result.taskId,
                context.Result.robotId,
                reason,
                context.Result.message));
        }

        private void LogMessage(string category, string message)
        {
            dependencies.TelemetryLogger?.LogMessage(category, message);
        }
    }
}
