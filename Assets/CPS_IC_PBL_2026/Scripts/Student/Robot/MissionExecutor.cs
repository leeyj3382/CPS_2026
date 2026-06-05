using System;
using System.Collections;
using System.Collections.Generic;
using CPS.ICPBL.Common;
using CPS.ICPBL.Environment;
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
            public IPathReservationManager PathReservationManager;
            public IPathTrafficManager PathTrafficManager;
            public OperatingStations OperatingStations;
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
            public float PayloadLockWaitLogIntervalSec = 2f;
            public float PathReservationLogIntervalSec = 1f;
            public float PayloadPathPriorityBonus = 1000f;
            public float TaskAgePriorityScale = 0.01f;
            public float PathYieldWaitSec = 2f;
            public float PathYieldDistance = 4.5f;
            public float PathYieldMoveTimeoutSec = 6f;
            public float PathYieldCooldownSec = 0.8f;
            public int PathYieldMaxAttempts = 5;
            public float BasePathCheckIntervalSec = 0.05f;
            public float BasePathResumeCheckIntervalSec = 0.1f;
            public float BaseStopSettleSec = 0.05f;
            public float BaseBlockedEscapeSec = 2f;
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
            public bool PayloadSecured;
            public bool ConveyorPickReported;
            public float NextPathYieldAt;
            public int PathYieldAttempts;
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
                yield return AcquireLock(
                    context,
                    conveyorKey,
                    MissionFailureReason.CollisionRisk,
                    "conveyor lock");
            }

            if (!context.Failed)
            {
                yield return RunPickSequence(context);
            }

            if (!context.Failed)
            {
                ReleaseKey(context, conveyorKey);
                ReportConveyorPicked(context);
            }

            if (!context.Failed)
            {
                yield return RunClassification(context);
            }

            if (!context.Failed)
            {
                ResourceKey boxKey = new ResourceKey(
                    StudentConstants.GetBoxLockType(context.DestinationBoxType),
                    context.Result.destinationStationId);

                if (!context.Failed)
                {
                    yield return AcquireCentralZoneIfNeeded(context, context.Result.destinationStationId);
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
                    yield return AcquireLock(
                        context,
                        boxKey,
                        MissionFailureReason.BoxLockFailed,
                        "box lock");
                }

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
            float nextPayloadWaitLogAt = Time.time
                + Mathf.Max(0.1f, settings.PayloadLockWaitLogIntervalSec);
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

            while (context.PayloadSecured)
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

                if (Time.time >= nextPayloadWaitLogAt)
                {
                    nextPayloadWaitLogAt = Time.time
                        + Mathf.Max(0.1f, settings.PayloadLockWaitLogIntervalSec);
                    LogMessage("Lock", string.Format(
                        "Task={0} robot={1} is holding payload; continuing to wait for {2}: {3}.",
                        context.Request.taskId,
                        context.Request.robotId,
                        label,
                        key));
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
            yield return MoveBaseToStation(context, stationId, label);

            if (!context.Failed)
            {
                dependencies.SetCurrentStationId?.Invoke(stationId);
            }
        }

        private IEnumerator MoveBaseToStation(
            MissionContext context,
            int stationId,
            string label)
        {
            OperatingStations.Station targetStation = default;
            bool hasStationPosition = false;
            if (dependencies.OperatingStations != null)
            {
                hasStationPosition = dependencies.OperatingStations.TryGetStation(
                    stationId,
                    out targetStation);
            }

            if (hasStationPosition)
            {
                RegisterActiveBasePath(context, targetStation.BasePosition);
                dependencies.Controller.GoToOperatingStation(stationId);
                yield return WaitForBaseMoveWithDynamicStop(
                    context,
                    stationId,
                    targetStation.BasePosition,
                    label);
                ClearActiveBasePath(context);
            }
            else
            {
                dependencies.Controller.GoToOperatingStation(stationId);
                yield return WaitForControllerIdle(context, settings.MoveTimeoutSec, label);
            }
        }

        private IEnumerator WaitForBaseMoveWithDynamicStop(
            MissionContext context,
            int stationId,
            Vector3 targetPosition,
            string label)
        {
            float movingDeadline = Time.time + Mathf.Max(0f, settings.MoveTimeoutSec);
            float nextCheckAt = Time.time;

            while (dependencies.Controller.IsBusy)
            {
                if (Time.time > movingDeadline)
                {
                    dependencies.SetState?.Invoke(RobotRuntimeState.Stuck);
                    Fail(context, MissionFailureReason.MoveTimeout, string.Format(
                        "Timed out while waiting for {0}.",
                        label));
                    yield break;
                }

                if (Time.time >= nextCheckAt
                    && IsPathBlockedByRobot(
                        context,
                        stationId,
                        targetPosition,
                        out int blockingRobotId,
                        out bool waitForSameBox,
                        out bool preferDetour))
                {
                    LogMessage("Path", string.Format(
                        "Robot={0} task={1} stopping before {2}; blockedBy robot={3}.",
                        context.Request.robotId,
                        context.Request.taskId,
                        label,
                        blockingRobotId));

                    dependencies.Controller.MoveBaseTo(dependencies.Controller.Position);
                    yield return WaitForControllerIdle(
                        context,
                        Mathf.Max(0.1f, settings.PathYieldMoveTimeoutSec),
                        string.Format("base stop before {0}", label));
                    if (context.Failed)
                    {
                        yield break;
                    }

                    ClearActiveBasePath(context);
                    yield return new WaitForSeconds(Mathf.Max(0f, settings.BaseStopSettleSec));
                    bool detoured = false;
                    if (CanAttemptDynamicDetour(
                        context,
                        blockingRobotId,
                        waitForSameBox,
                        preferDetour))
                    {
                        yield return TryYieldFromBlockedPath(
                            context,
                            stationId,
                            dependencies.Controller.Position,
                            targetPosition,
                            label,
                            value => detoured = value);
                        if (context.Failed)
                        {
                            yield break;
                        }

                        if (detoured)
                        {
                            movingDeadline = Time.time + Mathf.Max(0f, settings.MoveTimeoutSec);
                            RegisterActiveBasePath(context, targetPosition);
                            dependencies.Controller.GoToOperatingStation(stationId);
                            nextCheckAt = Time.time + Mathf.Max(0.01f, settings.BasePathCheckIntervalSec);
                            continue;
                        }
                    }

                    float blockedWaitStartedAt = Time.time;
                    while (IsPathBlockedByRobot(
                        context,
                        stationId,
                        targetPosition,
                        out blockingRobotId,
                        out waitForSameBox,
                        out preferDetour))
                    {
                        if (blockingRobotId == StudentConstants.UnassignedRobotId)
                        {
                            LogMessage("Path", string.Format(
                                "Robot={0} task={1} blocked by static keep-out before {2}; waiting.",
                                context.Request.robotId,
                                context.Request.taskId,
                                label));
                            yield return new WaitForSeconds(Mathf.Max(
                                0.01f,
                                settings.PathYieldCooldownSec));
                            continue;
                        }

                        if (CanAttemptDynamicDetour(
                                context,
                                blockingRobotId,
                                waitForSameBox,
                                preferDetour)
                            || CanAttemptBlockedEscape(
                                context,
                                blockingRobotId,
                                waitForSameBox,
                                blockedWaitStartedAt))
                        {
                            bool retriedDetour = false;
                            yield return TryYieldFromBlockedPath(
                                context,
                                stationId,
                                dependencies.Controller.Position,
                                targetPosition,
                                label,
                                value => retriedDetour = value);
                            if (context.Failed)
                            {
                                yield break;
                            }

                            if (retriedDetour)
                            {
                                blockedWaitStartedAt = Time.time;
                                break;
                            }
                        }

                        LogMessage("Path", string.Format(
                            "Robot={0} task={1} waiting to resume {2}; blockedBy robot={3}{4}.",
                            context.Request.robotId,
                            context.Request.taskId,
                            label,
                            blockingRobotId,
                            waitForSameBox ? " sameBox=true" : string.Empty));
                        yield return new WaitForSeconds(Mathf.Max(
                            0.01f,
                            settings.BasePathResumeCheckIntervalSec));
                    }

                    movingDeadline = Time.time + Mathf.Max(0f, settings.MoveTimeoutSec);
                    RegisterActiveBasePath(context, targetPosition);
                    dependencies.Controller.GoToOperatingStation(stationId);
                    nextCheckAt = Time.time + Mathf.Max(0.01f, settings.BasePathCheckIntervalSec);
                    continue;
                }

                nextCheckAt = Time.time + Mathf.Max(0.01f, settings.BasePathCheckIntervalSec);
                yield return null;
            }
        }

        private bool IsPathBlockedByRobot(
            MissionContext context,
            int targetStationId,
            Vector3 targetPosition,
            out int blockingRobotId,
            out bool waitForSameBox,
            out bool preferDetour)
        {
            blockingRobotId = StudentConstants.UnassignedRobotId;
            waitForSameBox = false;
            preferDetour = false;
            if (dependencies.PathPlanner == null)
            {
                return false;
            }

            return dependencies.PathPlanner.IsBasePathBlocked(
                context.Request.robotId,
                targetStationId,
                dependencies.Controller.Position,
                targetPosition,
                out blockingRobotId,
                out waitForSameBox,
                out preferDetour);
        }

        private bool CanAttemptDynamicDetour(
            MissionContext context,
            int blockingRobotId,
            bool waitForSameBox,
            bool preferDetour)
        {
            if (!preferDetour || waitForSameBox)
            {
                return false;
            }

            if (Time.time < context.NextPathYieldAt)
            {
                return false;
            }

            return context.PathYieldAttempts < Mathf.Max(0, settings.PathYieldMaxAttempts);
        }

        private bool CanAttemptBlockedEscape(
            MissionContext context,
            int blockingRobotId,
            bool waitForSameBox,
            float blockedWaitStartedAt)
        {
            if (blockingRobotId == StudentConstants.UnassignedRobotId || waitForSameBox)
            {
                return false;
            }

            if (Time.time - blockedWaitStartedAt < Mathf.Max(0.1f, settings.BaseBlockedEscapeSec))
            {
                return false;
            }

            if (Time.time < context.NextPathYieldAt)
            {
                return false;
            }

            return context.PathYieldAttempts < Mathf.Max(0, settings.PathYieldMaxAttempts);
        }

        private void RegisterActiveBasePath(MissionContext context, Vector3 targetPosition)
        {
            dependencies.PathTrafficManager?.RegisterActiveBasePath(
                context.Request.robotId,
                context.Request.taskId,
                dependencies.Controller.Position,
                targetPosition,
                context.PayloadSecured);
        }

        private void ClearActiveBasePath(MissionContext context)
        {
            dependencies.PathTrafficManager?.ClearActiveBasePath(
                context.Request.robotId,
                context.Request.taskId);
        }

        private IEnumerator ReserveBaseSegment(
            MissionContext context,
            Vector3 from,
            Vector3 to,
            string label,
            Action<PathReservationToken> onReserved)
        {
            if (dependencies.PathReservationManager == null)
            {
                yield break;
            }

            Vector3 currentFrom = from;
            float nextLogAt = Time.time;
            float waitStartedAt = Time.time;
            while (true)
            {
                currentFrom = dependencies.Controller.Position;
                float priority = CalculatePathPriority(context);
                if (dependencies.PathReservationManager.TryReserveBaseSegment(
                    context.Request.robotId,
                    context.Request.taskId,
                    currentFrom,
                    to,
                    priority,
                    out PathReservationToken token,
                    out int blockingRobotId,
                    out int blockingTaskId))
                {
                    onReserved?.Invoke(token);
                    yield break;
                }

                if (ShouldYieldFromBlockedPath(context, waitStartedAt))
                {
                    bool yielded = false;
                    yield return TryYieldFromBlockedPath(
                        context,
                        StudentConstants.NoStationId,
                        currentFrom,
                        to,
                        label,
                        value => yielded = value);

                    currentFrom = dependencies.Controller.Position;
                    waitStartedAt = Time.time;
                    if (context.Failed)
                    {
                        yield break;
                    }

                    if (yielded)
                    {
                        continue;
                    }
                }

                if (Time.time >= nextLogAt)
                {
                    nextLogAt = Time.time
                        + Mathf.Max(0.1f, settings.PathReservationLogIntervalSec);
                    LogMessage("Path", string.Format(
                        "Robot={0} task={1} waiting for safe base segment {2}; blockedBy robot={3} task={4}.",
                        context.Request.robotId,
                        context.Request.taskId,
                        label,
                        blockingRobotId,
                        blockingTaskId));
                }

                yield return null;
            }
        }

        private bool ShouldYieldFromBlockedPath(MissionContext context, float waitStartedAt)
        {
            if (context.PayloadSecured)
            {
                return false;
            }

            if (context.PathYieldAttempts >= Mathf.Max(0, settings.PathYieldMaxAttempts))
            {
                return false;
            }

            if (Time.time < context.NextPathYieldAt)
            {
                return false;
            }

            return Time.time - waitStartedAt >= Mathf.Max(0.1f, settings.PathYieldWaitSec);
        }

        private IEnumerator TryYieldFromBlockedPath(
            MissionContext context,
            int targetStationId,
            Vector3 from,
            Vector3 originalTarget,
            string label,
            Action<bool> onYielded)
        {
            context.NextPathYieldAt = Time.time + Mathf.Max(0.1f, settings.PathYieldCooldownSec);
            IReadOnlyList<Vector3> candidates = dependencies.PathPlanner != null
                ? dependencies.PathPlanner.BuildYieldCandidates(
                    context.Request.robotId,
                    from,
                    originalTarget)
                : BuildYieldCandidates(context, from, originalTarget);

            for (int i = 0; i < candidates.Count; i++)
            {
                from = dependencies.Controller.Position;
                Vector3 candidate = candidates[i];
                if (Vector3.Distance(from, candidate) <= 0.2f)
                {
                    continue;
                }

                if (IsPathBlockedByRobot(
                    context,
                    targetStationId,
                    candidate,
                    out int candidateBlockingRobotId,
                    out _,
                    out _))
                {
                    LogMessage("Path", string.Format(
                        "Yield candidate unsafe robot={0} task={1} label={2}; blockedBy robot={3}.",
                        context.Request.robotId,
                        context.Request.taskId,
                        label,
                        candidateBlockingRobotId));
                    continue;
                }

                float priority = CalculatePathPriority(context);
                if (!dependencies.PathReservationManager.TryReserveBaseSegment(
                    context.Request.robotId,
                    context.Request.taskId,
                    from,
                    candidate,
                    priority,
                    out PathReservationToken yieldToken,
                    out int blockingRobotId,
                    out int blockingTaskId))
                {
                    LogMessage("Path", string.Format(
                        "Yield candidate blocked robot={0} task={1} label={2}; blockedBy robot={3} task={4}.",
                        context.Request.robotId,
                        context.Request.taskId,
                        label,
                        blockingRobotId,
                        blockingTaskId));
                    continue;
                }

                context.PathYieldAttempts++;
                LogMessage("Path", string.Format(
                    "Yield robot={0} task={1} label={2} to=({3:0.0},{4:0.0}) attempt={5}.",
                    context.Request.robotId,
                    context.Request.taskId,
                    label,
                    candidate.x,
                    candidate.z,
                        context.PathYieldAttempts));

                RegisterActiveBasePath(context, candidate);
                dependencies.Controller.MoveBaseTo(candidate);
                bool arrived = false;
                yield return WaitForDetourMove(
                    context,
                    candidate,
                    Mathf.Max(0.1f, settings.PathYieldMoveTimeoutSec),
                    string.Format("path yield before {0}", label),
                    value => arrived = value);
                ReleaseBaseSegment(yieldToken);

                if (!context.Failed && arrived)
                {
                    onYielded?.Invoke(true);
                    yield break;
                }

                ClearActiveBasePath(context);
                from = dependencies.Controller.Position;
                continue;
            }

            ClearActiveBasePath(context);
            LogMessage("Path", string.Format(
                "No safe yield candidate robot={0} task={1} label={2}.",
                context.Request.robotId,
                context.Request.taskId,
                label));
            onYielded?.Invoke(false);
        }

        private IEnumerator WaitForDetourMove(
            MissionContext context,
            Vector3 targetPosition,
            float timeoutSec,
            string label,
            Action<bool> onArrived)
        {
            float deadline = Time.time + Mathf.Max(0f, timeoutSec);
            float nextCheckAt = Time.time;
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

                if (Time.time >= nextCheckAt
                    && IsPathBlockedByRobot(
                        context,
                        StudentConstants.NoStationId,
                        targetPosition,
                        out int blockingRobotId,
                        out _,
                        out _))
                {
                    LogMessage("Path", string.Format(
                        "Detour stopped robot={0} task={1} label={2}; blockedBy robot={3}.",
                        context.Request.robotId,
                        context.Request.taskId,
                        label,
                        blockingRobotId));

                    dependencies.Controller.MoveBaseTo(dependencies.Controller.Position);
                    yield return WaitForControllerIdle(
                        context,
                        Mathf.Max(0.1f, settings.PathYieldMoveTimeoutSec),
                        string.Format("detour stop during {0}", label));
                    onArrived?.Invoke(false);
                    yield break;
                }

                nextCheckAt = Time.time + Mathf.Max(0.01f, settings.BasePathCheckIntervalSec);
                yield return null;
            }

            onArrived?.Invoke(true);
        }

        private Vector3[] BuildYieldCandidates(
            MissionContext context,
            Vector3 from,
            Vector3 originalTarget)
        {
            float distance = Mathf.Max(0.5f, settings.PathYieldDistance);
            Vector3 direction = FlattenXZ(originalTarget - from);
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.forward;
            }

            direction.Normalize();
            Vector3 perpendicular = new Vector3(-direction.z, 0f, direction.x);
            if (context.Request.robotId == StudentConstants.RobotBId)
            {
                perpendicular = -perpendicular;
            }

            Vector3 backward = -direction;
            Vector3 diagonalA = (perpendicular + backward).normalized;
            Vector3 diagonalB = (-perpendicular + backward).normalized;

            return new[]
            {
                ClampYieldCandidate(from + perpendicular * distance),
                ClampYieldCandidate(from - perpendicular * distance),
                ClampYieldCandidate(from + backward * distance),
                ClampYieldCandidate(from + diagonalA * distance),
                ClampYieldCandidate(from + diagonalB * distance)
            };
        }

        private static Vector3 FlattenXZ(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private static Vector3 ClampYieldCandidate(Vector3 value)
        {
            value.x = Mathf.Clamp(value.x, -9.5f, 10.5f);
            value.z = Mathf.Clamp(value.z, -8.0f, 11.5f);
            return value;
        }

        private float CalculatePathPriority(MissionContext context)
        {
            float priority = context.PayloadSecured ? settings.PayloadPathPriorityBonus : 0f;
            priority += Mathf.Max(0f, Time.time - context.Request.requestTime)
                * Mathf.Max(0f, settings.TaskAgePriorityScale);
            priority -= context.Request.robotId * 0.001f;
            return priority;
        }

        private void ReleaseBaseSegment(PathReservationToken token)
        {
            dependencies.PathReservationManager?.ReleaseBaseSegment(token);
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

        private void ReportConveyorPicked(MissionContext context)
        {
            if (context.ConveyorPickReported)
            {
                return;
            }

            context.ConveyorPickReported = true;
            NotifyProgress(context, MissionProgressType.ConveyorPicked);
        }

        private void NotifyProgress(MissionContext context, MissionProgressType type)
        {
            if (context.Request?.onProgress == null)
            {
                return;
            }

            var progress = new MissionProgressEvent
            {
                taskId = context.Request.taskId,
                robotId = context.Request.robotId,
                conveyorId = context.Request.conveyorId,
                type = type,
                occurredAt = Time.time
            };

            try
            {
                context.Request.onProgress(progress);
            }
            catch (Exception exception)
            {
                LogMessage("Mission", string.Format(
                    "Progress callback failed task={0} robot={1} type={2}: {3}",
                    context.Request.taskId,
                    context.Request.robotId,
                    type,
                    exception.Message));
            }
        }

        private IEnumerator GripWithRetry(MissionContext context)
        {
            int attempts = Mathf.Max(0, settings.GripRetryCount) + 1;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                yield return dependencies.Gripper.WaitUntilGraspReady(settings.GripReadyTimeoutSec);
                if (dependencies.Gripper.TryGrip(out string reason))
                {
                    context.PayloadSecured = true;
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
            Color lastSensedColor = StudentConstants.DefaultSensorColor;
            string lastColorSource = string.Empty;
            string lastClassifierMessage = string.Empty;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Color sensedColor = ReadSensedColor(out string colorSource);
                lastSensedColor = sensedColor;
                lastColorSource = colorSource;
                ColorClassificationResult classification =
                    dependencies.ColorClassifier.Classify(sensedColor);
                context.Classification = classification;

                if (classification != null)
                {
                    lastClassifierMessage = classification.message;
                    context.Result.classificationResult = classification.result;
                    LogMessage("Color", string.Format(
                        "Classified task={0} robot={1} result={2} reliable={3} source={4} sensed={5} blueDistance={6:0.000} redDistance={7:0.000} message={8}.",
                        context.Request.taskId,
                        context.Request.robotId,
                        classification.result,
                        classification.reliable,
                        colorSource,
                        FormatColor(sensedColor),
                        classification.blueDistance,
                        classification.redDistance,
                        classification.message));

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

            Fail(context, MissionFailureReason.ClassificationFailed, string.Format(
                "Color classification was unreliable or unknown. source={0} sensed={1} message={2}",
                string.IsNullOrEmpty(lastColorSource) ? "unknown" : lastColorSource,
                FormatColor(lastSensedColor),
                string.IsNullOrEmpty(lastClassifierMessage) ? "No classifier result." : lastClassifierMessage));
        }

        private Color ReadSensedColor(out string source)
        {
            if (dependencies.Gripper != null && dependencies.Gripper.IsHolding)
            {
                if (dependencies.Gripper.TryReadHeldObjectColor(out Color heldColor, out source))
                {
                    return heldColor;
                }

                string heldColorFailure = source;
                if (dependencies.ColorSensor != null && dependencies.ColorSensor.area != null)
                {
                    source = string.Format(
                        "{0}; fallback=ColorSensor.area:{1}",
                        heldColorFailure,
                        dependencies.ColorSensor.area.name);
                    return dependencies.ColorSensor.area.color;
                }

                if (dependencies.ColorArea != null)
                {
                    source = string.Format(
                        "{0}; fallback=ColorArea:{1}",
                        heldColorFailure,
                        dependencies.ColorArea.name);
                    return dependencies.ColorArea.color;
                }

                source = heldColorFailure;
                return StudentConstants.DefaultSensorColor;
            }

            if (dependencies.ColorSensor != null && dependencies.ColorSensor.area != null)
            {
                source = string.Format("ColorSensor.area:{0}", dependencies.ColorSensor.area.name);
                return dependencies.ColorSensor.area.color;
            }

            if (dependencies.ColorArea != null)
            {
                source = string.Format("ColorArea:{0}", dependencies.ColorArea.name);
                return dependencies.ColorArea.color;
            }

            source = "DefaultSensorColor";
            return StudentConstants.DefaultSensorColor;
        }

        private static string FormatColor(Color color)
        {
            return string.Format(
                "rgba({0:0.000},{1:0.000},{2:0.000},{3:0.000})",
                color.r,
                color.g,
                color.b,
                color.a);
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

            context.PayloadSecured = false;
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
            ClearActiveBasePath(context);

            if (context.Failed)
            {
                CleanupFailure(context);
            }

            ReleaseAllLocks(context);
            if (context.Failed && context.PayloadSecured && !context.ConveyorPickReported)
            {
                ReportConveyorPicked(context);
            }

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
                    "Mission failed while holding payload task={0} robot={1}; keeping gripper closed to avoid dropping object in mid-air.",
                    context.Request.taskId,
                    context.Request.robotId));
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
