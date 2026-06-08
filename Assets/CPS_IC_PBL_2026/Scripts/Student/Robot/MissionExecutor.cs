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
            public IPathTimeReservationManager PathTimeReservationManager;
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
            public bool EnableDestinationBoxStaging = true;
            public bool EnableSameBoxFarStaging = true;
            public float SameBoxFarStagingMinDistance = 7.5f;
            public float SameBoxStagingMaxDistanceRatio = 1.65f;
            public float SameBoxStagingMaxExtraDistance = 5f;
            public bool EnableBoxApproachGate = true;
            public bool EnableConveyorFiveSixChokeGate = true;
            public bool EnableBoxExitClearance = true;
            public float BoxExitClearanceDistance = 2.2f;
            public float EmergencyYieldDistance = 2.2f;
            public float EmergencyYieldMaxDistanceRatio = 2.5f;
            public float EmergencyYieldMaxExtraDistance = 8f;
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
            public string CurrentMoveLabel = string.Empty;
            public int CurrentMoveStationId = StudentConstants.NoStationId;
            public Vector3 CurrentMoveTarget;
            public Vector3 LastKnownBasePosition;

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
        private readonly List<Vector3> sameBoxStagingCandidates = new List<Vector3>(4);
        private readonly List<Vector3> emergencyYieldCandidates = new List<Vector3>(16);
        private readonly List<Vector3> boxApproachWaitCandidates = new List<Vector3>(8);
        private readonly List<Vector3> boxApproachWaitRoute = new List<Vector3>(8);
        private static readonly Dictionary<int, MissionContext> ActiveContextsByRobot =
            new Dictionary<int, MissionContext>();
        private const float DebugGridCellSize = 3f;
        private const float DebugGridMinX = -12f;
        private const float DebugGridMinZ = -9f;
        private const int DebugGridColumnCount = 8;
        private static readonly Vector3 NormalBoxRobotAStagingOffset = new Vector3(-3.8f, 0f, 1.2f);
        private static readonly Vector3 NormalBoxRobotBStagingOffset = new Vector3(3.8f, 0f, 1.2f);
        private static readonly Vector3 AbnormalBoxRobotAStagingOffset = new Vector3(-3.6f, 0f, -1.4f);
        private static readonly Vector3 AbnormalBoxRobotBStagingOffset = new Vector3(-3.6f, 0f, 2.2f);
        private static readonly Vector3 FarNormalBoxApproachStagingPosition =
            new Vector3(6.8f, 0f, -2.8f);
        private static readonly Vector3 FarAbnormalBoxApproachStagingPosition =
            new Vector3(5.6f, 0f, 1.0f);

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

            RegisterActiveMission(context);
            dependencies.SetState?.Invoke(RobotRuntimeState.Reserved);

            ResourceKey conveyorKey = new ResourceKey(
                LockResourceType.Conveyor,
                request.conveyorId);
            ResourceKey conveyorChokeKey = GetConveyorChokeKey(request.conveyorId);

            yield return AcquireCentralZoneIfNeeded(context, request.conveyorId);

            if (!context.Failed)
            {
                yield return AcquireConveyorChokeIfNeeded(context, request.conveyorId);
            }

            if (!context.Failed)
            {
                yield return MoveToStation(
                    context,
                    request.conveyorId,
                    RobotRuntimeState.MovingToConveyor,
                    "conveyor station");
            }

            ReleaseKey(context, conveyorChokeKey);
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
                ResourceKey boxApproachKey = GetBoxApproachKey(context.Result.destinationStationId);

                if (ShouldStageBeforeDestinationBox(
                    context,
                    boxKey,
                    boxApproachKey,
                    out Vector3 stagingPosition))
                {
                    yield return MoveToDestinationBoxStaging(context, stagingPosition);
                }

                if (!context.Failed)
                {
                    yield return AcquireCentralZoneIfNeeded(context, context.Result.destinationStationId);
                }

                if (!context.Failed)
                {
                    yield return AcquireBoxApproachIfNeeded(context, boxApproachKey);
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
                if (!context.Failed)
                {
                    yield return MoveToBoxExitClearance(context);
                }

                ReleaseKey(context, boxApproachKey);
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

        private IEnumerator AcquireConveyorChokeIfNeeded(MissionContext context, int stationId)
        {
            if (!settings.EnableConveyorFiveSixChokeGate || !RequiresConveyorChokeGate(stationId))
            {
                yield break;
            }

            yield return AcquireLock(
                context,
                GetConveyorChokeKey(stationId),
                MissionFailureReason.CollisionRisk,
                "5/6 conveyor choke");
        }

        private IEnumerator AcquireBoxApproachIfNeeded(MissionContext context, ResourceKey key)
        {
            if (!settings.EnableBoxApproachGate || !StudentConstants.IsBoxStationId(key.id))
            {
                yield break;
            }

            if (HasLock(context, key))
            {
                yield break;
            }

            bool hasBoxStationPosition = TryGetStationBasePosition(key.id, out Vector3 boxStationPosition);
            if (TryAcquireLockNow(context, key, "box approach gate"))
            {
                yield break;
            }

            if (context.PayloadSecured && hasBoxStationPosition)
            {
                yield return StageBeforeBoxApproachLock(context, boxStationPosition, key);
                if (context.Failed || HasLock(context, key))
                {
                    yield break;
                }
            }

            yield return AcquireLock(
                context,
                key,
                MissionFailureReason.CollisionRisk,
                "box approach gate");
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

        private bool TryAcquireLockNow(MissionContext context, ResourceKey key, string label)
        {
            if (dependencies.LockManager == null)
            {
                LogMessage("Lock", string.Format(
                    "No lock manager; proceeding without {0} for task={1}.",
                    label,
                    context.Request.taskId));
                return true;
            }

            if (HasLock(context, key))
            {
                return true;
            }

            if (!dependencies.LockManager.TryAcquire(
                key,
                context.Request.robotId,
                context.Request.taskId,
                out ResourceLockToken token))
            {
                return false;
            }

            context.LockTokens.Add(token);
            dependencies.TelemetryLogger?.LogLock(
                "Acquire",
                key,
                context.Request.robotId,
                context.Request.taskId);
            return true;
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
                yield return MoveBaseToTarget(
                    context,
                    stationId,
                    targetStation.BasePosition,
                    label,
                    () => dependencies.Controller.GoToOperatingStation(stationId));
            }
            else
            {
                dependencies.Controller.GoToOperatingStation(stationId);
                yield return WaitForControllerIdle(context, settings.MoveTimeoutSec, label);
            }
        }

        private IEnumerator MoveToDestinationBoxStaging(
            MissionContext context,
            Vector3 stagingPosition)
        {
            LogMessage("Path", string.Format(
                "Robot={0} task={1} staging before box station={2} from conveyor={3} target=({4:0.0},{5:0.0}).",
                context.Request.robotId,
                context.Request.taskId,
                context.Result.destinationStationId,
                context.Request.conveyorId,
                stagingPosition.x,
                stagingPosition.z));

            dependencies.SetState?.Invoke(RobotRuntimeState.MovingToBox);
            yield return MoveBaseToTarget(
                context,
                StudentConstants.NoStationId,
                stagingPosition,
                "box staging",
                () => dependencies.Controller.MoveBaseTo(stagingPosition));

            if (!context.Failed)
            {
                dependencies.SetCurrentStationId?.Invoke(StudentConstants.NoStationId);
            }
        }

        private IEnumerator MoveToBoxExitClearance(MissionContext context)
        {
            if (!settings.EnableBoxExitClearance)
            {
                yield break;
            }

            if (!TryGetStationBasePosition(
                context.Result.destinationStationId,
                out Vector3 boxStationPosition))
            {
                yield break;
            }

            Vector3 exitPosition = GetBoxExitClearancePosition(
                context.Result.destinationStationId,
                boxStationPosition);
            exitPosition = ClampWorldPosition(exitPosition);
            if (DistanceXZ(exitPosition, dependencies.Controller.Position) <= 0.25f)
            {
                yield break;
            }

            LogMessage("Path", string.Format(
                "Robot={0} task={1} clearing box approach station={2} to=({3:0.0},{4:0.0}) before releasing approach gate.",
                context.Request.robotId,
                context.Request.taskId,
                context.Result.destinationStationId,
                exitPosition.x,
                exitPosition.z));

            yield return MoveBaseToTarget(
                context,
                StudentConstants.NoStationId,
                exitPosition,
                "box exit clearance",
                () => dependencies.Controller.MoveBaseTo(exitPosition));

            if (!context.Failed)
            {
                dependencies.SetCurrentStationId?.Invoke(StudentConstants.NoStationId);
            }
        }

        private bool ShouldStageBeforeDestinationBox(
            MissionContext context,
            ResourceKey boxKey,
            ResourceKey boxApproachKey,
            out Vector3 stagingPosition)
        {
            stagingPosition = Vector3.zero;
            if (!settings.EnableDestinationBoxStaging || !context.PayloadSecured)
            {
                return false;
            }

            if (!IsFarSourceForDestinationBox(context))
            {
                return false;
            }

            if (!TryGetStationBasePosition(
                context.Result.destinationStationId,
                out Vector3 boxStationPosition))
            {
                return false;
            }

            bool boxLockContended = dependencies.LockManager != null
                && dependencies.LockManager.IsLocked(boxKey);
            bool boxApproachContended = settings.EnableBoxApproachGate
                && dependencies.LockManager != null
                && dependencies.LockManager.IsLocked(boxApproachKey);
            bool boxPathContended = IsDestinationBoxPathContended(context, boxStationPosition);
            if (!boxLockContended && !boxApproachContended && !boxPathContended)
            {
                return false;
            }

            if (!TryGetFarSourceBoxStagingPosition(context, out stagingPosition))
            {
                return false;
            }

            stagingPosition = ClampWorldPosition(stagingPosition);
            if (DistanceXZ(stagingPosition, dependencies.Controller.Position) <= 0.25f)
            {
                return false;
            }

            return !IsPathBlockedByRobot(
                context,
                StudentConstants.NoStationId,
                stagingPosition,
                out _,
                out _,
                out _);
        }

        private static ResourceKey GetBoxApproachKey(int stationId)
        {
            return new ResourceKey(LockResourceType.BoxApproach, stationId);
        }

        private static ResourceKey GetConveyorChokeKey(int stationId)
        {
            return RequiresConveyorChokeGate(stationId)
                ? new ResourceKey(
                    LockResourceType.ConveyorChoke,
                    StudentConstants.ConveyorFiveSixChokeResourceId)
                : new ResourceKey(LockResourceType.ConveyorChoke, StudentConstants.NoStationId);
        }

        private static bool RequiresConveyorChokeGate(int stationId)
        {
            return stationId == 5 || stationId == 6;
        }

        private Vector3 GetBoxExitClearancePosition(int stationId, Vector3 boxStationPosition)
        {
            float distance = Mathf.Max(0.2f, settings.BoxExitClearanceDistance);
            if (stationId == StudentConstants.NormalBoxStationId)
            {
                return boxStationPosition + Vector3.forward * distance;
            }

            if (stationId == StudentConstants.AbnormalBoxStationId)
            {
                return boxStationPosition + Vector3.left * distance;
            }

            return boxStationPosition;
        }

        private IEnumerator StageBeforeBoxApproachLock(
            MissionContext context,
            Vector3 boxStationPosition,
            ResourceKey boxApproachKey)
        {
            if (!settings.EnableDestinationBoxStaging)
            {
                yield break;
            }

            IReadOnlyList<Vector3> candidates =
                BuildBoxApproachWaitCandidates(context, boxStationPosition, boxApproachKey);
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector3 candidate = candidates[i];
                if (DistanceXZ(candidate, dependencies.Controller.Position) <= 0.25f)
                {
                    continue;
                }

                BuildBoxApproachWaitRoute(context, candidate, boxApproachWaitRoute);
                if (boxApproachWaitRoute.Count == 0)
                {
                    continue;
                }

                if (IsBoxApproachWaitRouteBlocked(
                    context,
                    boxApproachWaitRoute,
                    out int blockingRobotId))
                {
                    LogMessage("Path", string.Format(
                        "Box approach wait route unsafe robot={0} task={1} cell={2}; blockedBy robot={3}.",
                        context.Request.robotId,
                        context.Request.taskId,
                        GetGridCellNumber(candidate),
                        blockingRobotId));
                    continue;
                }

                TryGetOtherBoxApproachContext(
                    context,
                    boxApproachKey,
                    out MissionContext approachOwner);
                LogMessage("Path", string.Format(
                    "Robot={0} task={1} waiting for box approach at cell={2} target=({3:0.0},{4:0.0}) ownerRobot={5} ownerNext={6} ownNext={7}.",
                    context.Request.robotId,
                    context.Request.taskId,
                    GetGridCellNumber(candidate),
                    candidate.x,
                    candidate.z,
                    approachOwner != null
                        ? approachOwner.Request.robotId
                        : StudentConstants.UnassignedRobotId,
                    approachOwner != null
                        ? approachOwner.Request.predictedNextConveyorId
                        : StudentConstants.NoStationId,
                    context.Request.predictedNextConveyorId));
                yield return MoveToBoxApproachWaitRoute(context, boxApproachWaitRoute);
                yield break;
            }

            LogMessage("Path", string.Format(
                "No safe box approach wait staging robot={0} task={1} station={2}.",
                context.Request.robotId,
                context.Request.taskId,
                context.Result.destinationStationId));
        }

        private IReadOnlyList<Vector3> BuildBoxApproachWaitCandidates(
            MissionContext context,
            Vector3 boxStationPosition,
            ResourceKey boxApproachKey)
        {
            boxApproachWaitCandidates.Clear();
            if (context.Result.destinationStationId != StudentConstants.NormalBoxStationId)
            {
                IReadOnlyList<Vector3> fallback =
                    BuildSameBoxStagingCandidates(context, boxStationPosition);
                for (int i = 0; i < fallback.Count; i++)
                {
                    AddBoxApproachWaitCandidate(fallback[i]);
                }

                return boxApproachWaitCandidates;
            }

            TryGetOtherBoxApproachContext(
                context,
                boxApproachKey,
                out MissionContext approachOwner);
            int ownerNextConveyorId = approachOwner != null
                ? approachOwner.Request.predictedNextConveyorId
                : StudentConstants.NoStationId;

            if (ownerNextConveyorId >= StudentConstants.MinConveyorId
                && ownerNextConveyorId <= 3)
            {
                AddNormalBoxWaitCells(20, 28, 27, 11, 22, 30, 36);
            }
            else if (ownerNextConveyorId >= 5
                && ownerNextConveyorId <= StudentConstants.MaxConveyorId)
            {
                AddNormalBoxWaitCells(11, 20, 28, 22, 30, 36);
            }
            else if (context.Request.conveyorId <= 4)
            {
                AddNormalBoxWaitCells(22, 30, 20, 11, 28, 36);
            }
            else
            {
                AddNormalBoxWaitCells(22, 30, 36, 20, 11);
            }

            return boxApproachWaitCandidates;
        }

        private void AddNormalBoxWaitCells(params int[] cellNumbers)
        {
            for (int i = 0; i < cellNumbers.Length; i++)
            {
                AddBoxApproachWaitCandidate(GetGridCellCenter(cellNumbers[i]));
            }
        }

        private void AddBoxApproachWaitCandidate(Vector3 candidate)
        {
            candidate = ClampWorldPosition(candidate);
            for (int i = 0; i < boxApproachWaitCandidates.Count; i++)
            {
                if (DistanceXZ(boxApproachWaitCandidates[i], candidate) <= 0.25f)
                {
                    return;
                }
            }

            boxApproachWaitCandidates.Add(candidate);
        }

        private void BuildBoxApproachWaitRoute(
            MissionContext context,
            Vector3 candidate,
            List<Vector3> route)
        {
            route.Clear();
            if (context.Result.destinationStationId != StudentConstants.NormalBoxStationId)
            {
                AddBoxApproachWaitRoutePoint(route, candidate);
                return;
            }

            int cellNumber = GetGridCellNumber(candidate);
            Vector3 from = dependencies.Controller.Position;
            bool fromUpperLeft = from.x < -2.5f && from.z > 0.5f;
            bool fromUpper = from.z > 0.5f;

            switch (cellNumber)
            {
                case 22:
                    if (fromUpperLeft)
                    {
                        AddBoxApproachWaitRouteCells(route, 34, 35, 36, 30, 22);
                    }
                    else if (fromUpper)
                    {
                        AddBoxApproachWaitRouteCells(route, 30, 22);
                    }
                    else
                    {
                        AddBoxApproachWaitRouteCells(route, 22);
                    }

                    break;
                case 30:
                    if (fromUpperLeft)
                    {
                        AddBoxApproachWaitRouteCells(route, 34, 35, 36, 30);
                    }
                    else
                    {
                        AddBoxApproachWaitRouteCells(route, 30);
                    }

                    break;
                case 20:
                    if (fromUpperLeft)
                    {
                        AddBoxApproachWaitRouteCells(route, 27, 28, 20);
                    }
                    else if (fromUpper)
                    {
                        AddBoxApproachWaitRouteCells(route, 28, 20);
                    }
                    else
                    {
                        AddBoxApproachWaitRouteCells(route, 20);
                    }

                    break;
                case 11:
                    if (fromUpperLeft)
                    {
                        AddBoxApproachWaitRouteCells(route, 27, 19, 11);
                    }
                    else
                    {
                        AddBoxApproachWaitRouteCells(route, 11);
                    }

                    break;
                case 28:
                    if (fromUpperLeft)
                    {
                        AddBoxApproachWaitRouteCells(route, 27, 28);
                    }
                    else
                    {
                        AddBoxApproachWaitRouteCells(route, 28);
                    }

                    break;
                case 36:
                    if (fromUpperLeft)
                    {
                        AddBoxApproachWaitRouteCells(route, 34, 35, 36);
                    }
                    else
                    {
                        AddBoxApproachWaitRouteCells(route, 36);
                    }

                    break;
                default:
                    AddBoxApproachWaitRoutePoint(route, candidate);
                    break;
            }
        }

        private void AddBoxApproachWaitRouteCells(List<Vector3> route, params int[] cellNumbers)
        {
            for (int i = 0; i < cellNumbers.Length; i++)
            {
                AddBoxApproachWaitRoutePoint(route, GetGridCellCenter(cellNumbers[i]));
            }
        }

        private void AddBoxApproachWaitRoutePoint(List<Vector3> route, Vector3 point)
        {
            point = ClampWorldPosition(point);
            Vector3 previous = route.Count > 0
                ? route[route.Count - 1]
                : dependencies.Controller.Position;
            if (DistanceXZ(previous, point) <= 0.25f)
            {
                return;
            }

            route.Add(point);
        }

        private bool IsBoxApproachWaitRouteBlocked(
            MissionContext context,
            IReadOnlyList<Vector3> route,
            out int blockingRobotId)
        {
            blockingRobotId = StudentConstants.UnassignedRobotId;
            if (dependencies.PathPlanner == null)
            {
                return false;
            }

            Vector3 from = dependencies.Controller.Position;
            for (int i = 0; i < route.Count; i++)
            {
                Vector3 to = route[i];
                if (dependencies.PathPlanner.IsBasePathBlocked(
                    context.Request.robotId,
                    StudentConstants.NoStationId,
                    from,
                    to,
                    out blockingRobotId,
                    out _,
                    out _))
                {
                    return true;
                }

                from = to;
            }

            return false;
        }

        private IEnumerator MoveToBoxApproachWaitRoute(
            MissionContext context,
            IReadOnlyList<Vector3> route)
        {
            dependencies.SetState?.Invoke(RobotRuntimeState.MovingToBox);
            for (int i = 0; i < route.Count; i++)
            {
                Vector3 target = route[i];
                if (DistanceXZ(target, dependencies.Controller.Position) <= 0.25f)
                {
                    continue;
                }

                yield return MoveBaseToTarget(
                    context,
                    StudentConstants.NoStationId,
                    target,
                    "box approach wait cell",
                    () => dependencies.Controller.MoveBaseTo(target));
                if (context.Failed)
                {
                    yield break;
                }
            }

            dependencies.SetCurrentStationId?.Invoke(StudentConstants.NoStationId);
        }

        private static bool TryGetOtherBoxApproachContext(
            MissionContext context,
            ResourceKey boxApproachKey,
            out MissionContext other)
        {
            other = null;
            foreach (KeyValuePair<int, MissionContext> entry in ActiveContextsByRobot)
            {
                MissionContext candidate = entry.Value;
                if (candidate == null
                    || candidate == context
                    || candidate.Request.robotId == context.Request.robotId)
                {
                    continue;
                }

                if (HasLock(candidate, boxApproachKey))
                {
                    other = candidate;
                    return true;
                }

                if (other == null && candidate.PayloadSecured)
                {
                    other = candidate;
                }
            }

            return other != null;
        }

        private bool IsDestinationBoxPathContended(
            MissionContext context,
            Vector3 boxStationPosition)
        {
            if (dependencies.PathPlanner == null)
            {
                return false;
            }

            return dependencies.PathPlanner.IsBasePathBlocked(
                context.Request.robotId,
                context.Result.destinationStationId,
                dependencies.Controller.Position,
                boxStationPosition,
                out _,
                out bool waitForSameBox,
                out _)
                && waitForSameBox;
        }

        private static bool IsFarSourceForDestinationBox(MissionContext context)
        {
            return (context.DestinationBoxType == BoxType.Normal
                    && context.Request.conveyorId >= 6
                    && context.Request.conveyorId <= StudentConstants.MaxConveyorId)
                || (context.DestinationBoxType == BoxType.Abnormal
                    && context.Request.conveyorId >= StudentConstants.MinConveyorId
                    && context.Request.conveyorId <= 5);
        }

        private static bool TryGetFarSourceBoxStagingPosition(
            MissionContext context,
            out Vector3 stagingPosition)
        {
            if (context.DestinationBoxType == BoxType.Normal)
            {
                stagingPosition = FarNormalBoxApproachStagingPosition;
                return true;
            }

            if (context.DestinationBoxType == BoxType.Abnormal)
            {
                stagingPosition = FarAbnormalBoxApproachStagingPosition;
                return true;
            }

            stagingPosition = Vector3.zero;
            return false;
        }

        private static Vector3 GetDestinationBoxStagingOffset(MissionContext context)
        {
            if (context.DestinationBoxType == BoxType.Normal)
            {
                return context.Request.robotId == StudentConstants.RobotAId
                    ? NormalBoxRobotAStagingOffset
                    : NormalBoxRobotBStagingOffset;
            }

            return context.Request.robotId == StudentConstants.RobotBId
                ? AbnormalBoxRobotBStagingOffset
                : AbnormalBoxRobotAStagingOffset;
        }

        private bool TryGetStationBasePosition(int stationId, out Vector3 basePosition)
        {
            if (dependencies.OperatingStations != null
                && dependencies.OperatingStations.TryGetStation(
                    stationId,
                    out OperatingStations.Station station))
            {
                basePosition = station.BasePosition;
                return true;
            }

            if (stationId == StudentConstants.NormalBoxStationId)
            {
                basePosition = new Vector3(0f, 0f, -6f);
                return true;
            }

            if (stationId == StudentConstants.AbnormalBoxStationId)
            {
                basePosition = new Vector3(8.5f, 0f, 2.5f);
                return true;
            }

            basePosition = Vector3.zero;
            return false;
        }

        private IEnumerator MoveBaseToTarget(
            MissionContext context,
            int targetStationId,
            Vector3 targetPosition,
            string label,
            Action finalMove)
        {
            yield return MoveBaseToTargetReactive(
                context,
                targetStationId,
                targetPosition,
                label,
                finalMove);
        }

        private IEnumerator FollowTimedRoute(
            MissionContext context,
            TimedRouteReservationToken token,
            int targetStationId,
            Vector3 targetPosition,
            string label,
            Action finalMove)
        {
            int lastMoveSegmentIndex = FindLastTimedMoveSegmentIndex(token);
            if (lastMoveSegmentIndex < 0)
            {
                finalMove?.Invoke();
                yield return WaitForControllerIdle(context, settings.MoveTimeoutSec, label);
                dependencies.PathTimeReservationManager?.ReleaseTimedBaseRoute(token);
                yield break;
            }

            for (int i = 0; i < token.segments.Count; i++)
            {
                TimedRouteSegment segment = token.segments[i];
                yield return WaitForTimedSegmentStart(context, segment, label);
                if (context.Failed)
                {
                    break;
                }

                if (segment.isWait || Vector3.Distance(segment.from, segment.to) <= 0.05f)
                {
                    yield return WaitForTimedSegmentEnd(context, segment);
                    if (context.Failed)
                    {
                        break;
                    }

                    continue;
                }

                RegisterActiveBasePath(context, segment.to);
                if (i == lastMoveSegmentIndex)
                {
                    finalMove?.Invoke();
                }
                else
                {
                    dependencies.Controller.MoveBaseTo(segment.to);
                }

                float segmentTimeout = Mathf.Max(
                    settings.MoveTimeoutSec,
                    Mathf.Max(0.1f, segment.endTime - segment.startTime) + 4f);
                yield return WaitForControllerIdle(
                    context,
                    segmentTimeout,
                    string.Format("timed {0}", label));
                ClearActiveBasePath(context);
                if (context.Failed)
                {
                    break;
                }
            }

            dependencies.PathTimeReservationManager?.ReleaseTimedBaseRoute(token);
        }

        private IEnumerator WaitForTimedSegmentStart(
            MissionContext context,
            TimedRouteSegment segment,
            string label)
        {
            while (Time.time < segment.startTime)
            {
                MaintainHeldPayloadAlignment(context);
                yield return null;
            }
        }

        private IEnumerator WaitForTimedSegmentEnd(
            MissionContext context,
            TimedRouteSegment segment)
        {
            while (Time.time < segment.endTime)
            {
                MaintainHeldPayloadAlignment(context);
                yield return null;
            }
        }

        private static int FindLastTimedMoveSegmentIndex(TimedRouteReservationToken token)
        {
            if (token == null)
            {
                return -1;
            }

            for (int i = token.segments.Count - 1; i >= 0; i--)
            {
                TimedRouteSegment segment = token.segments[i];
                if (!segment.isWait && Vector3.Distance(segment.from, segment.to) > 0.05f)
                {
                    return i;
                }
            }

            return -1;
        }

        private IEnumerator MoveBaseToTargetReactive(
            MissionContext context,
            int stationId,
            Vector3 targetPosition,
            string label,
            Action move)
        {
            SetCurrentMoveContext(context, stationId, targetPosition, label);
            RegisterActiveBasePath(context, targetPosition);
            move?.Invoke();
            yield return WaitForBaseMoveWithDynamicStop(
                context,
                stationId,
                targetPosition,
                label,
                move);
            ClearActiveBasePath(context);
            ClearCurrentMoveContext(context, label);
        }

        private IEnumerator WaitForBaseMoveWithDynamicStop(
            MissionContext context,
            int stationId,
            Vector3 targetPosition,
            string label,
            Action restartMove)
        {
            float movingDeadline = Time.time + Mathf.Max(0f, settings.MoveTimeoutSec);
            float nextCheckAt = Time.time;
            bool releaseBoxApproachWhileBlocked =
                settings.EnableBoxApproachGate && StudentConstants.IsBoxStationId(stationId);
            ResourceKey boxApproachKey = releaseBoxApproachWhileBlocked
                ? GetBoxApproachKey(stationId)
                : default;

            while (dependencies.Controller.IsBusy)
            {
                context.LastKnownBasePosition = dependencies.Controller.Position;
                MaintainHeldPayloadAlignment(context);
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
                    if (releaseBoxApproachWhileBlocked && HasLock(context, boxApproachKey))
                    {
                        LogMessage("Lock", string.Format(
                            "Robot={0} task={1} releasing box approach gate while blocked before {2}.",
                            context.Request.robotId,
                            context.Request.taskId,
                            label));
                        ReleaseKey(context, boxApproachKey);
                    }

                    yield return new WaitForSeconds(Mathf.Max(0f, settings.BaseStopSettleSec));
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

                        if (CanYieldForPayloadBoxApproach(context, blockingRobotId, label))
                        {
                            bool yieldedForPayloadBox = false;
                            yield return TryYieldForPayloadBoxApproach(
                                context,
                                stationId,
                                targetPosition,
                                blockingRobotId,
                                value => yieldedForPayloadBox = value);
                            if (context.Failed)
                            {
                                yield break;
                            }

                            if (yieldedForPayloadBox)
                            {
                                blockedWaitStartedAt = Time.time;
                                break;
                            }
                        }

                        if (CanYieldForBoxExitClearance(context, blockingRobotId, label))
                        {
                            bool yieldedForBoxExit = false;
                            yield return TryYieldForBoxExitClearance(
                                context,
                                stationId,
                                targetPosition,
                                blockingRobotId,
                                value => yieldedForBoxExit = value);
                            if (context.Failed)
                            {
                                yield break;
                            }

                            if (yieldedForBoxExit)
                            {
                                blockedWaitStartedAt = Time.time;
                                break;
                            }
                        }

                        bool sameBoxStagingEscape = CanAttemptSameBoxFarStaging(
                            context,
                            blockingRobotId,
                            waitForSameBox,
                            stationId,
                            targetPosition,
                            blockedWaitStartedAt);
                        bool sameBoxEscape = !sameBoxStagingEscape
                            && CanAttemptSameBoxEscape(
                                context,
                                blockingRobotId,
                                waitForSameBox,
                                blockedWaitStartedAt);
                        bool dynamicDetour = CanAttemptDynamicDetour(
                                context,
                                blockingRobotId,
                                waitForSameBox,
                                preferDetour,
                                blockedWaitStartedAt);
                        bool blockedEscape = CanAttemptBlockedEscape(
                                context,
                                blockingRobotId,
                                waitForSameBox,
                                blockedWaitStartedAt);
                        if (dynamicDetour
                            || blockedEscape
                            || sameBoxStagingEscape
                            || sameBoxEscape)
                        {
                            bool retriedDetour = false;
                            bool emergencyYield = !context.PayloadSecured
                                && !waitForSameBox
                                && (dynamicDetour || blockedEscape);
                            yield return TryYieldFromBlockedPath(
                                context,
                                sameBoxEscape || sameBoxStagingEscape
                                    ? StudentConstants.NoStationId
                                    : stationId,
                                dependencies.Controller.Position,
                                targetPosition,
                                label,
                                sameBoxStagingEscape,
                                emergencyYield,
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
                    if (releaseBoxApproachWhileBlocked)
                    {
                        yield return AcquireBoxApproachIfNeeded(context, boxApproachKey);
                        if (context.Failed)
                        {
                            yield break;
                        }
                    }

                    RegisterActiveBasePath(context, targetPosition);
                    restartMove?.Invoke();
                    nextCheckAt = Time.time + Mathf.Max(0.01f, settings.BasePathCheckIntervalSec);
                    continue;
                }

                nextCheckAt = Time.time + Mathf.Max(0.01f, settings.BasePathCheckIntervalSec);
                yield return null;
            }

            MaintainHeldPayloadAlignment(context);
        }

        private bool CanYieldForPayloadBoxApproach(
            MissionContext context,
            int blockingRobotId,
            string label)
        {
            if (context.PayloadSecured
                || string.Equals(label, "box station", StringComparison.Ordinal)
                || blockingRobotId == StudentConstants.UnassignedRobotId)
            {
                return false;
            }

            if (Time.time < context.NextPathYieldAt)
            {
                return false;
            }

            if (!ActiveContextsByRobot.TryGetValue(
                blockingRobotId,
                out MissionContext blockingContext)
                || blockingContext == null
                || !blockingContext.PayloadSecured
                || !StudentConstants.IsBoxStationId(blockingContext.Result.destinationStationId))
            {
                return false;
            }

            return string.Equals(
                    blockingContext.CurrentMoveLabel,
                    "box station",
                    StringComparison.Ordinal)
                || blockingContext.CurrentMoveStationId == blockingContext.Result.destinationStationId;
        }

        private IEnumerator TryYieldForPayloadBoxApproach(
            MissionContext context,
            int stationId,
            Vector3 originalTarget,
            int blockingRobotId,
            Action<bool> onYielded)
        {
            if (!ActiveContextsByRobot.TryGetValue(
                blockingRobotId,
                out MissionContext blockingContext)
                || blockingContext == null)
            {
                onYielded?.Invoke(false);
                yield break;
            }

            context.NextPathYieldAt = Time.time + Mathf.Max(0.1f, settings.PathYieldCooldownSec);
            IReadOnlyList<Vector3> candidates = BuildPayloadBoxApproachYieldCandidates(
                context,
                stationId,
                blockingContext);
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector3 from = dependencies.Controller.Position;
                Vector3 candidate = candidates[i];
                if (DistanceXZ(from, candidate) <= 0.2f)
                {
                    continue;
                }

                bool movingAwayFromBlocker = IsMovingAwayFromBlockingRobot(
                    from,
                    candidate,
                    blockingContext);
                if (!movingAwayFromBlocker)
                {
                    continue;
                }

                if (IsPathBlockedByRobot(
                    context,
                    StudentConstants.NoStationId,
                    candidate,
                    out int candidateBlockingRobotId,
                    out _,
                    out _)
                    && candidateBlockingRobotId != blockingRobotId)
                {
                    LogMessage("Path", string.Format(
                        "Payload-box yield candidate unsafe robot={0} task={1} cell={2}; blockedBy robot={3}.",
                        context.Request.robotId,
                        context.Request.taskId,
                        GetGridCellNumber(candidate),
                        candidateBlockingRobotId));
                    continue;
                }

                PathReservationToken yieldToken = null;
                bool reserved = false;
                if (dependencies.PathReservationManager != null)
                {
                    float priority = CalculatePathPriority(context);
                    reserved = dependencies.PathReservationManager.TryReserveBaseSegment(
                        context.Request.robotId,
                        context.Request.taskId,
                        from,
                        candidate,
                        priority,
                        out yieldToken,
                        out int reservationBlockingRobotId,
                        out int reservationBlockingTaskId);
                    if (!reserved
                        && reservationBlockingRobotId != blockingRobotId)
                    {
                        LogMessage("Path", string.Format(
                            "Payload-box yield candidate blocked robot={0} task={1} cell={2}; blockedBy robot={3} task={4}.",
                            context.Request.robotId,
                            context.Request.taskId,
                            GetGridCellNumber(candidate),
                            reservationBlockingRobotId,
                            reservationBlockingTaskId));
                        continue;
                    }
                }

                context.PathYieldAttempts++;
                LogMessage("Path", string.Format(
                    "Payload-box yield robot={0} task={1} toCell={2} to=({3:0.0},{4:0.0}) blockerRobot={5} blockerConveyor={6} blockerNext={7} ownTarget={8}.",
                    context.Request.robotId,
                    context.Request.taskId,
                    GetGridCellNumber(candidate),
                    candidate.x,
                    candidate.z,
                    blockingRobotId,
                    blockingContext.Request.conveyorId,
                    blockingContext.Request.predictedNextConveyorId,
                    stationId));

                RegisterActiveBasePath(context, candidate);
                dependencies.Controller.MoveBaseTo(candidate);
                bool arrived = false;
                yield return WaitForYieldMoveIgnoringBlocker(
                    context,
                    candidate,
                    blockingRobotId,
                    Mathf.Max(0.1f, settings.PathYieldMoveTimeoutSec),
                    "payload box approach",
                    value => arrived = value);
                if (reserved)
                {
                    ReleaseBaseSegment(yieldToken);
                }

                ClearActiveBasePath(context);
                if (!context.Failed && arrived)
                {
                    onYielded?.Invoke(true);
                    yield break;
                }
            }

            onYielded?.Invoke(false);
        }

        private IReadOnlyList<Vector3> BuildPayloadBoxApproachYieldCandidates(
            MissionContext context,
            int stationId,
            MissionContext blockingContext)
        {
            boxApproachWaitCandidates.Clear();
            if (blockingContext.Result.destinationStationId == StudentConstants.NormalBoxStationId)
            {
                if (stationId >= StudentConstants.MinConveyorId && stationId <= 4)
                {
                    AddNormalBoxWaitCells(22, 30, 36, 20, 28, 11);
                }
                else
                {
                    AddNormalBoxWaitCells(20, 28, 11, 22, 30, 36);
                }

                return boxApproachWaitCandidates;
            }

            if (blockingContext.Result.destinationStationId == StudentConstants.AbnormalBoxStationId)
            {
                AddBoxApproachWaitCandidate(FarAbnormalBoxApproachStagingPosition);
                if (TryGetStationBasePosition(
                    StudentConstants.AbnormalBoxStationId,
                    out Vector3 abnormalBoxPosition))
                {
                    AddBoxApproachWaitCandidate(
                        abnormalBoxPosition + AbnormalBoxRobotAStagingOffset);
                    AddBoxApproachWaitCandidate(
                        abnormalBoxPosition + AbnormalBoxRobotBStagingOffset);
                }
            }

            return boxApproachWaitCandidates;
        }

        private IEnumerator WaitForYieldMoveIgnoringBlocker(
            MissionContext context,
            Vector3 targetPosition,
            int ignoredBlockingRobotId,
            float timeoutSec,
            string label,
            Action<bool> onArrived)
        {
            float deadline = Time.time + Mathf.Max(0f, timeoutSec);
            float nextCheckAt = Time.time;
            while (dependencies.Controller.IsBusy)
            {
                context.LastKnownBasePosition = dependencies.Controller.Position;
                MaintainHeldPayloadAlignment(context);
                if (Time.time > deadline)
                {
                    dependencies.SetState?.Invoke(RobotRuntimeState.Stuck);
                    Fail(context, MissionFailureReason.MoveTimeout, string.Format(
                        "Timed out while yielding for {0}.",
                        label));
                    yield break;
                }

                if (Time.time >= nextCheckAt
                    && IsPathBlockedByRobot(
                        context,
                        StudentConstants.NoStationId,
                        targetPosition,
                        out int currentBlockingRobotId,
                        out _,
                        out _)
                    && (currentBlockingRobotId != ignoredBlockingRobotId
                        || !ActiveContextsByRobot.TryGetValue(
                            ignoredBlockingRobotId,
                            out MissionContext blockingContext)
                        || !IsMovingAwayFromBlockingRobot(
                            dependencies.Controller.Position,
                            targetPosition,
                            blockingContext)))
                {
                    dependencies.Controller.MoveBaseTo(dependencies.Controller.Position);
                    yield return WaitForControllerIdle(
                        context,
                        Mathf.Max(0.1f, settings.PathYieldMoveTimeoutSec),
                        string.Format("{0} yield stop", label));
                    onArrived?.Invoke(false);
                    yield break;
                }

                nextCheckAt = Time.time + Mathf.Max(0.01f, settings.BasePathCheckIntervalSec);
                yield return null;
            }

            context.LastKnownBasePosition = dependencies.Controller.Position;
            onArrived?.Invoke(true);
        }

        private bool CanYieldForBoxExitClearance(
            MissionContext context,
            int blockingRobotId,
            string label)
        {
            if (context.PayloadSecured
                || string.Equals(label, "box exit clearance", StringComparison.Ordinal)
                || blockingRobotId == StudentConstants.UnassignedRobotId)
            {
                return false;
            }

            if (!ActiveContextsByRobot.TryGetValue(
                blockingRobotId,
                out MissionContext blockingContext)
                || blockingContext == null)
            {
                return false;
            }

            return string.Equals(
                    blockingContext.CurrentMoveLabel,
                    "box exit clearance",
                    StringComparison.Ordinal)
                && StudentConstants.IsBoxStationId(blockingContext.Result.destinationStationId);
        }

        private IEnumerator TryYieldForBoxExitClearance(
            MissionContext context,
            int stationId,
            Vector3 originalTarget,
            int blockingRobotId,
            Action<bool> onYielded)
        {
            if (!ActiveContextsByRobot.TryGetValue(
                blockingRobotId,
                out MissionContext blockingContext)
                || blockingContext == null)
            {
                onYielded?.Invoke(false);
                yield break;
            }

            context.NextPathYieldAt = Time.time + Mathf.Max(0.1f, settings.PathYieldCooldownSec);
            IReadOnlyList<Vector3> candidates = BuildBoxExitClearanceYieldCandidates(
                context,
                stationId,
                blockingContext);
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector3 from = dependencies.Controller.Position;
                Vector3 candidate = candidates[i];
                if (DistanceXZ(from, candidate) <= 0.2f)
                {
                    continue;
                }

                bool movingAwayFromBlocker = IsMovingAwayFromBlockingRobot(
                    from,
                    candidate,
                    blockingContext);
                if (!movingAwayFromBlocker)
                {
                    continue;
                }

                if (IsPathBlockedByRobot(
                    context,
                    StudentConstants.NoStationId,
                    candidate,
                    out int candidateBlockingRobotId,
                    out _,
                    out _)
                    && candidateBlockingRobotId != blockingRobotId)
                {
                    LogMessage("Path", string.Format(
                        "Box-exit yield candidate unsafe robot={0} task={1} cell={2}; blockedBy robot={3}.",
                        context.Request.robotId,
                        context.Request.taskId,
                        GetGridCellNumber(candidate),
                        candidateBlockingRobotId));
                    continue;
                }

                PathReservationToken yieldToken = null;
                bool reserved = false;
                if (dependencies.PathReservationManager != null)
                {
                    float priority = CalculatePathPriority(context);
                    reserved = dependencies.PathReservationManager.TryReserveBaseSegment(
                        context.Request.robotId,
                        context.Request.taskId,
                        from,
                        candidate,
                        priority,
                        out yieldToken,
                        out int reservationBlockingRobotId,
                        out int reservationBlockingTaskId);
                    if (!reserved
                        && (reservationBlockingRobotId != blockingRobotId
                            || !movingAwayFromBlocker))
                    {
                        LogMessage("Path", string.Format(
                            "Box-exit yield candidate blocked robot={0} task={1} cell={2}; blockedBy robot={3} task={4}.",
                            context.Request.robotId,
                            context.Request.taskId,
                            GetGridCellNumber(candidate),
                            reservationBlockingRobotId,
                            reservationBlockingTaskId));
                        continue;
                    }
                }

                context.PathYieldAttempts++;
                LogMessage("Path", string.Format(
                    "Box-exit yield robot={0} task={1} toCell={2} to=({3:0.0},{4:0.0}) blockerRobot={5} blockerNext={6} ownTarget={7}.",
                    context.Request.robotId,
                    context.Request.taskId,
                    GetGridCellNumber(candidate),
                    candidate.x,
                    candidate.z,
                    blockingRobotId,
                    blockingContext.Request.predictedNextConveyorId,
                    stationId));

                RegisterActiveBasePath(context, candidate);
                dependencies.Controller.MoveBaseTo(candidate);
                bool arrived = false;
                yield return WaitForBoxExitYieldMove(
                    context,
                    candidate,
                    blockingRobotId,
                    Mathf.Max(0.1f, settings.PathYieldMoveTimeoutSec),
                    value => arrived = value);
                if (reserved)
                {
                    ReleaseBaseSegment(yieldToken);
                }

                ClearActiveBasePath(context);
                if (!context.Failed && arrived)
                {
                    onYielded?.Invoke(true);
                    yield break;
                }
            }

            onYielded?.Invoke(false);
        }

        private IReadOnlyList<Vector3> BuildBoxExitClearanceYieldCandidates(
            MissionContext context,
            int stationId,
            MissionContext blockingContext)
        {
            boxApproachWaitCandidates.Clear();
            int blockerNext = blockingContext.Request.predictedNextConveyorId;
            if (blockerNext >= StudentConstants.MinConveyorId && blockerNext <= 3)
            {
                AddNormalBoxWaitCells(22, 30, 36, 20, 28, 11);
            }
            else if (blockerNext >= 5 && blockerNext <= StudentConstants.MaxConveyorId)
            {
                AddNormalBoxWaitCells(20, 28, 11, 22, 30, 36);
            }
            else if (stationId >= StudentConstants.MinConveyorId && stationId <= 4)
            {
                AddNormalBoxWaitCells(20, 28, 11, 22, 30);
            }
            else
            {
                AddNormalBoxWaitCells(22, 30, 36, 20, 28);
            }

            return boxApproachWaitCandidates;
        }

        private IEnumerator WaitForBoxExitYieldMove(
            MissionContext context,
            Vector3 targetPosition,
            int blockingRobotId,
            float timeoutSec,
            Action<bool> onArrived)
        {
            float deadline = Time.time + Mathf.Max(0f, timeoutSec);
            float nextCheckAt = Time.time;
            while (dependencies.Controller.IsBusy)
            {
                context.LastKnownBasePosition = dependencies.Controller.Position;
                MaintainHeldPayloadAlignment(context);
                if (Time.time > deadline)
                {
                    dependencies.SetState?.Invoke(RobotRuntimeState.Stuck);
                    Fail(context, MissionFailureReason.MoveTimeout, "Timed out while yielding for box exit clearance.");
                    yield break;
                }

                if (Time.time >= nextCheckAt
                    && IsPathBlockedByRobot(
                        context,
                        StudentConstants.NoStationId,
                        targetPosition,
                        out int currentBlockingRobotId,
                        out _,
                        out _)
                    && (currentBlockingRobotId != blockingRobotId
                        || !ActiveContextsByRobot.TryGetValue(
                            blockingRobotId,
                            out MissionContext blockingContext)
                        || !IsMovingAwayFromBlockingRobot(
                            dependencies.Controller.Position,
                            targetPosition,
                            blockingContext)))
                {
                    dependencies.Controller.MoveBaseTo(dependencies.Controller.Position);
                    yield return WaitForControllerIdle(
                        context,
                        Mathf.Max(0.1f, settings.PathYieldMoveTimeoutSec),
                        "box-exit yield stop");
                    onArrived?.Invoke(false);
                    yield break;
                }

                nextCheckAt = Time.time + Mathf.Max(0.01f, settings.BasePathCheckIntervalSec);
                yield return null;
            }

            context.LastKnownBasePosition = dependencies.Controller.Position;
            onArrived?.Invoke(true);
        }

        private static bool IsMovingAwayFromBlockingRobot(
            Vector3 from,
            Vector3 candidate,
            MissionContext blockingContext)
        {
            if (blockingContext == null)
            {
                return false;
            }

            Vector3 blockingPosition = blockingContext.LastKnownBasePosition;
            return DistanceXZ(candidate, blockingPosition)
                >= DistanceXZ(from, blockingPosition) + 0.35f;
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
            bool preferDetour,
            float blockedWaitStartedAt)
        {
            if (context.PayloadSecured)
            {
                return false;
            }

            if (!preferDetour || waitForSameBox)
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

        private bool CanAttemptBlockedEscape(
            MissionContext context,
            int blockingRobotId,
            bool waitForSameBox,
            float blockedWaitStartedAt)
        {
            if (context.PayloadSecured)
            {
                return false;
            }

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

        private bool CanAttemptSameBoxFarStaging(
            MissionContext context,
            int blockingRobotId,
            bool waitForSameBox,
            int targetStationId,
            Vector3 targetPosition,
            float blockedWaitStartedAt)
        {
            if (!settings.EnableSameBoxFarStaging || !context.PayloadSecured)
            {
                return false;
            }

            if (!waitForSameBox || blockingRobotId == StudentConstants.UnassignedRobotId)
            {
                return false;
            }

            if (!StudentConstants.IsBoxStationId(targetStationId))
            {
                return false;
            }

            if (DistanceXZ(dependencies.Controller.Position, targetPosition)
                < Mathf.Max(0.5f, settings.SameBoxFarStagingMinDistance))
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

        private bool CanAttemptSameBoxEscape(
            MissionContext context,
            int blockingRobotId,
            bool waitForSameBox,
            float blockedWaitStartedAt)
        {
            if (context.PayloadSecured)
            {
                return false;
            }

            if (!waitForSameBox || blockingRobotId == StudentConstants.UnassignedRobotId)
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
            context.LastKnownBasePosition = dependencies.Controller.Position;
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
                        false,
                        false,
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
            bool useSameBoxStagingCandidates,
            bool useEmergencyYieldCandidates,
            Action<bool> onYielded)
        {
            context.NextPathYieldAt = Time.time + Mathf.Max(0.1f, settings.PathYieldCooldownSec);
            IReadOnlyList<Vector3> candidates;
            if (useSameBoxStagingCandidates)
            {
                candidates = BuildSameBoxStagingCandidates(context, originalTarget);
            }
            else if (useEmergencyYieldCandidates)
            {
                candidates = BuildEmergencyYieldCandidates(context, from, originalTarget);
            }
            else if (dependencies.PathPlanner != null)
            {
                candidates = dependencies.PathPlanner.BuildYieldCandidates(
                    context.Request.robotId,
                    from,
                    originalTarget);
            }
            else
            {
                candidates = BuildYieldCandidates(context, from, originalTarget);
            }

            float maxDetourDistanceRatio = useSameBoxStagingCandidates
                ? Mathf.Max(1f, settings.SameBoxStagingMaxDistanceRatio)
                : useEmergencyYieldCandidates
                ? Mathf.Max(1f, settings.EmergencyYieldMaxDistanceRatio)
                : 1.35f;
            float maxDetourDistanceExtra = useSameBoxStagingCandidates
                ? Mathf.Max(0f, settings.SameBoxStagingMaxExtraDistance)
                : useEmergencyYieldCandidates
                ? Mathf.Max(0f, settings.EmergencyYieldMaxExtraDistance)
                : 2f;

            for (int i = 0; i < candidates.Count; i++)
            {
                from = dependencies.Controller.Position;
                Vector3 candidate = candidates[i];
                if (DistanceXZ(from, candidate) <= 0.2f)
                {
                    continue;
                }

                float directDistance = DistanceXZ(from, originalTarget);
                float detourDistance = DistanceXZ(from, candidate)
                    + DistanceXZ(candidate, originalTarget);
                float maxAllowedDetourDistance = useEmergencyYieldCandidates
                    ? Mathf.Max(
                        directDistance * maxDetourDistanceRatio,
                        directDistance + maxDetourDistanceExtra)
                    : Mathf.Min(
                        directDistance * maxDetourDistanceRatio,
                        directDistance + maxDetourDistanceExtra);
                if (directDistance > 0.05f
                    && detourDistance > maxAllowedDetourDistance)
                {
                    LogMessage("Path", string.Format(
                        "Yield candidate too expensive robot={0} task={1} label={2}; direct={3:0.00} detour={4:0.00} limit={5:0.00}.",
                        context.Request.robotId,
                        context.Request.taskId,
                        label,
                        directDistance,
                        detourDistance,
                        maxAllowedDetourDistance));
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

        private IReadOnlyList<Vector3> BuildSameBoxStagingCandidates(
            MissionContext context,
            Vector3 boxPosition)
        {
            sameBoxStagingCandidates.Clear();

            if (!StudentConstants.IsBoxStationId(context.Result.destinationStationId))
            {
                return sameBoxStagingCandidates;
            }

            if (IsFarSourceForDestinationBox(context)
                && TryGetFarSourceBoxStagingPosition(context, out Vector3 farStagingPosition))
            {
                AddSameBoxStagingCandidate(farStagingPosition);
            }

            AddSameBoxStagingCandidate(
                boxPosition + GetDestinationBoxStagingOffset(context));

            if (context.DestinationBoxType == BoxType.Normal)
            {
                AddSameBoxStagingCandidate(boxPosition + NormalBoxRobotAStagingOffset);
                AddSameBoxStagingCandidate(boxPosition + NormalBoxRobotBStagingOffset);
            }
            else
            {
                AddSameBoxStagingCandidate(boxPosition + AbnormalBoxRobotAStagingOffset);
                AddSameBoxStagingCandidate(boxPosition + AbnormalBoxRobotBStagingOffset);
            }

            return sameBoxStagingCandidates;
        }

        private void AddSameBoxStagingCandidate(Vector3 candidate)
        {
            candidate = ClampWorldPosition(candidate);
            for (int i = 0; i < sameBoxStagingCandidates.Count; i++)
            {
                if (DistanceXZ(sameBoxStagingCandidates[i], candidate) <= 0.25f)
                {
                    return;
                }
            }

            sameBoxStagingCandidates.Add(candidate);
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
                MaintainHeldPayloadAlignment(context);
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

            MaintainHeldPayloadAlignment(context);
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

        private IReadOnlyList<Vector3> BuildEmergencyYieldCandidates(
            MissionContext context,
            Vector3 from,
            Vector3 originalTarget)
        {
            emergencyYieldCandidates.Clear();
            AddEmergencyBoxYieldCandidates(from, originalTarget);

            float nearDistance = Mathf.Max(0.5f, settings.EmergencyYieldDistance);
            float farDistance = Mathf.Max(nearDistance, settings.PathYieldDistance);
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

            AddEmergencyYieldCandidate(from + perpendicular * nearDistance);
            AddEmergencyYieldCandidate(from - perpendicular * nearDistance);
            AddEmergencyYieldCandidate(from + backward * nearDistance);
            AddEmergencyYieldCandidate(from + diagonalA * nearDistance);
            AddEmergencyYieldCandidate(from + diagonalB * nearDistance);
            AddEmergencyYieldCandidate(from + perpendicular * farDistance);
            AddEmergencyYieldCandidate(from - perpendicular * farDistance);
            AddEmergencyYieldCandidate(from + backward * farDistance);
            AddEmergencyYieldCandidate(from + diagonalA * farDistance);
            AddEmergencyYieldCandidate(from + diagonalB * farDistance);

            return emergencyYieldCandidates;
        }

        private void AddEmergencyBoxYieldCandidates(Vector3 from, Vector3 originalTarget)
        {
            const float boxNeighborhoodRadius = 6.5f;

            if (TryGetStationBasePosition(
                StudentConstants.NormalBoxStationId,
                out Vector3 normalBoxPosition)
                && (DistanceXZ(from, normalBoxPosition) <= boxNeighborhoodRadius
                    || DistanceXZ(originalTarget, normalBoxPosition) <= boxNeighborhoodRadius))
            {
                AddOrderedEmergencyPair(
                    from,
                    normalBoxPosition + NormalBoxRobotAStagingOffset,
                    normalBoxPosition + NormalBoxRobotBStagingOffset);
                AddEmergencyYieldCandidate(FarNormalBoxApproachStagingPosition);
            }

            if (TryGetStationBasePosition(
                StudentConstants.AbnormalBoxStationId,
                out Vector3 abnormalBoxPosition)
                && (DistanceXZ(from, abnormalBoxPosition) <= boxNeighborhoodRadius
                    || DistanceXZ(originalTarget, abnormalBoxPosition) <= boxNeighborhoodRadius))
            {
                AddOrderedEmergencyPair(
                    from,
                    abnormalBoxPosition + AbnormalBoxRobotAStagingOffset,
                    abnormalBoxPosition + AbnormalBoxRobotBStagingOffset);
                AddEmergencyYieldCandidate(FarAbnormalBoxApproachStagingPosition);
            }
        }

        private void AddOrderedEmergencyPair(Vector3 from, Vector3 first, Vector3 second)
        {
            if (DistanceXZ(from, first) <= DistanceXZ(from, second))
            {
                AddEmergencyYieldCandidate(first);
                AddEmergencyYieldCandidate(second);
                return;
            }

            AddEmergencyYieldCandidate(second);
            AddEmergencyYieldCandidate(first);
        }

        private void AddEmergencyYieldCandidate(Vector3 candidate)
        {
            candidate = ClampYieldCandidate(candidate);
            for (int i = 0; i < emergencyYieldCandidates.Count; i++)
            {
                if (DistanceXZ(emergencyYieldCandidates[i], candidate) <= 0.25f)
                {
                    return;
                }
            }

            emergencyYieldCandidates.Add(candidate);
        }

        private static Vector3 FlattenXZ(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private static float DistanceXZ(Vector3 left, Vector3 right)
        {
            left.y = 0f;
            right.y = 0f;
            return Vector3.Distance(left, right);
        }

        private static Vector3 GetGridCellCenter(int cellNumber)
        {
            int safeCellNumber = Mathf.Max(1, cellNumber);
            int zeroBased = safeCellNumber - 1;
            int row = zeroBased / DebugGridColumnCount;
            int column = zeroBased % DebugGridColumnCount;
            return new Vector3(
                DebugGridMinX + (column + 0.5f) * DebugGridCellSize,
                0f,
                DebugGridMinZ + (row + 0.5f) * DebugGridCellSize);
        }

        private static int GetGridCellNumber(Vector3 position)
        {
            int column = Mathf.FloorToInt((position.x - DebugGridMinX) / DebugGridCellSize);
            int row = Mathf.FloorToInt((position.z - DebugGridMinZ) / DebugGridCellSize);
            if (column < 0 || column >= DebugGridColumnCount || row < 0)
            {
                return StudentConstants.NoStationId;
            }

            return row * DebugGridColumnCount + column + 1;
        }

        private static Vector3 ClampYieldCandidate(Vector3 value)
        {
            value.x = Mathf.Clamp(value.x, -9.5f, 10.5f);
            value.z = Mathf.Clamp(value.z, -8.0f, 11.5f);
            return value;
        }

        private static Vector3 ClampWorldPosition(Vector3 value)
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
                    dependencies.Gripper.BeginSmoothHeldObjectWorldGridAlignment(
                        StudentConstants.DefaultHeldObjectAlignDurationSec);
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
            MaintainHeldPayloadAlignment(context);
            if (StudentConstants.DefaultPlaceSettleBeforeReleaseSec > 0f)
            {
                yield return new WaitForSeconds(StudentConstants.DefaultPlaceSettleBeforeReleaseSec);
            }

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
                MaintainHeldPayloadAlignment(context);
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

            MaintainHeldPayloadAlignment(context);
        }

        private void MaintainHeldPayloadAlignment(MissionContext context)
        {
            if (context == null || !context.PayloadSecured)
            {
                return;
            }

            if (dependencies.Gripper == null || !dependencies.Gripper.IsHolding)
            {
                return;
            }

            dependencies.Gripper.MaintainHeldObjectWorldGridAlignment();
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

        private void SetCurrentMoveContext(
            MissionContext context,
            int stationId,
            Vector3 targetPosition,
            string label)
        {
            if (context == null)
            {
                return;
            }

            context.CurrentMoveLabel = label ?? string.Empty;
            context.CurrentMoveStationId = stationId;
            context.CurrentMoveTarget = targetPosition;
            context.LastKnownBasePosition = dependencies.Controller.Position;
        }

        private static void ClearCurrentMoveContext(MissionContext context, string label)
        {
            if (context == null
                || !string.Equals(context.CurrentMoveLabel, label, StringComparison.Ordinal))
            {
                return;
            }

            context.CurrentMoveLabel = string.Empty;
            context.CurrentMoveStationId = StudentConstants.NoStationId;
            context.CurrentMoveTarget = Vector3.zero;
        }

        private static void RegisterActiveMission(MissionContext context)
        {
            if (context == null || context.Request == null)
            {
                return;
            }

            ActiveContextsByRobot[context.Request.robotId] = context;
        }

        private static void UnregisterActiveMission(MissionContext context)
        {
            if (context == null || context.Request == null)
            {
                return;
            }

            if (ActiveContextsByRobot.TryGetValue(
                context.Request.robotId,
                out MissionContext activeContext)
                && activeContext == context)
            {
                ActiveContextsByRobot.Remove(context.Request.robotId);
            }
        }

        private static bool HasLock(MissionContext context, ResourceKey key)
        {
            if (context == null)
            {
                return false;
            }

            for (int i = 0; i < context.LockTokens.Count; i++)
            {
                ResourceLockToken token = context.LockTokens[i];
                if (token != null && token.key == key)
                {
                    return true;
                }
            }

            return false;
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
            UnregisterActiveMission(context);

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
