using System.Collections.Generic;
using CPS.ICPBL.Common;
using CPS.ICPBL.Environment;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    [DisallowMultipleComponent]
    public sealed class PathPlanner : MonoBehaviour, IPathPlanner, IPathReservationManager, IPathTrafficManager
    {
        private sealed class ActiveBasePath
        {
            public int RobotId;
            public int TaskId;
            public Vector3 From;
            public Vector3 To;
            public bool HasPayload;
            public float StartedAt;
            public float RegisteredAt;
        }

        [Header("Robot Home Conveyor Ranges")]
        [SerializeField] private int robotAMinConveyor = 1;
        [SerializeField] private int robotAMaxConveyor = 5;
        [SerializeField] private int robotBMinConveyor = 6;
        [SerializeField] private int robotBMaxConveyor = 10;

        [Header("Central Zone Policy")]
        [SerializeField] private bool requireCentralZoneForUnknownStation = true;
        [SerializeField] private bool requireCentralZoneForCrossSideMove = true;
        [SerializeField] private bool requireCentralZoneForBoxAccess = true;

        [Header("Base Segment Reservation")]
        [SerializeField] private bool enableSegmentReservation = true;
        [SerializeField, Min(0.1f)] private float segmentClearanceRadius = 2.6f;
        [SerializeField, Min(0.1f)] private float stationaryClearanceRadius = 2.5f;
        [SerializeField, Min(0.1f)] private float hardSegmentClearanceRadius = 1.8f;
        [SerializeField, Min(0.1f)] private float hardStationaryClearanceRadius = 1.8f;
        [SerializeField, Min(0.1f)] private float reservationStaleSec = 8f;

        [Header("Dynamic Conflict Timing")]
        [SerializeField] private bool enableTemporalConflictCheck = true;
        [SerializeField, Min(0.1f)] private float estimatedBaseSpeed = 7.5f;
        [SerializeField, Min(0f)] private float conflictTimeWindowSec = 0.9f;
        [SerializeField, Min(0.1f)] private float movingBlockDistance = 2.35f;
        [SerializeField, Min(0.1f)] private float movingResumeDistance = 2.8f;
        [SerializeField, Min(0.1f)] private float stationaryWorkClearanceRadius = 3.2f;
        [SerializeField, Min(0f)] private float ignoreBehindDistance = 0.35f;
        [SerializeField] private bool stopImmediatelyForLowerPriorityCrossing = true;
        [SerializeField, Min(0.1f)] private float crossingHoldDistance = 3.0f;
        [SerializeField, Min(0f)] private float crossingPriorityMargin = 0.8f;
        [SerializeField, Min(0f)] private float pathStartPriorityMarginSec = 0.15f;
        [SerializeField, Min(0.1f)] private float activePathStaleSec = 45f;

        [Header("Lane Routing")]
        [SerializeField] private bool enableLaneRouting = false;
        [SerializeField] private float robotALaneX = -7.2f;
        [SerializeField] private float robotBLaneX = 7.2f;
        [SerializeField] private float lowerLaneZ = -7.4f;
        [SerializeField] private float upperLaneZ = 9.3f;
        [SerializeField] private float waypointMergeDistance = 0.6f;
        [SerializeField] private float detourDistance = 4.5f;
        [SerializeField] private float worldMinX = -9.5f;
        [SerializeField] private float worldMaxX = 10.5f;
        [SerializeField] private float worldMinZ = -8.0f;
        [SerializeField] private float worldMaxZ = 11.5f;

        [Header("Static Keep-Out Zones")]
        [SerializeField] private bool avoidBoxKeepOutZones = true;
        [SerializeField] private Vector3 normalBoxCenter = new Vector3(0f, 0f, -8f);
        [SerializeField] private Vector3 abnormalBoxCenter = new Vector3(10.5f, 0f, 2.5f);
        [SerializeField, Min(0.1f)] private float boxKeepOutRadius = 2.25f;
        [SerializeField, Min(0.1f)] private float boxBypassPadding = 1.6f;

        private readonly List<PathReservationToken> activeReservations =
            new List<PathReservationToken>();

        private readonly Dictionary<int, ActiveBasePath> activeBasePaths =
            new Dictionary<int, ActiveBasePath>();

        private readonly List<Vector3> reusableRoute = new List<Vector3>(5);
        private readonly List<Vector3> reusableYieldCandidates = new List<Vector3>(16);

        private IRobotController[] robotControllers;
        private IRobotAgent[] robotAgents;
        private OperatingStations operatingStations;
        private ITelemetryLogger telemetryLogger;

        public void ConfigureRobots(
            IRobotController robotA,
            IRobotController robotB,
            ITelemetryLogger logger = null,
            IRobotAgent robotAAgent = null,
            IRobotAgent robotBAgent = null,
            OperatingStations stationData = null)
        {
            NormalizeSettings();
            robotControllers = new[] { robotA, robotB };
            robotAgents = new[] { robotAAgent, robotBAgent };
            operatingStations = stationData;
            telemetryLogger = logger;
        }

        public bool RequiresCentralZone(int robotId, int fromStationId, int toStationId)
        {
            if (enableSegmentReservation)
            {
                return false;
            }

            if (fromStationId == toStationId)
            {
                return false;
            }

            bool fromKnown = IsKnownStation(fromStationId);
            bool toKnown = IsKnownStation(toStationId);
            if (!fromKnown || !toKnown)
            {
                return requireCentralZoneForUnknownStation && (fromKnown || toKnown);
            }

            if (requireCentralZoneForBoxAccess
                && (StudentConstants.IsBoxStationId(fromStationId)
                    || StudentConstants.IsBoxStationId(toStationId)))
            {
                return true;
            }

            if (!requireCentralZoneForCrossSideMove)
            {
                return false;
            }

            if (IsCrossSideMove(fromStationId, toStationId))
            {
                return true;
            }

            if (robotId == StudentConstants.RobotAId && IsRobotBOwnedConveyor(toStationId))
            {
                return true;
            }

            if (robotId == StudentConstants.RobotBId && IsRobotAOwnedConveyor(toStationId))
            {
                return true;
            }

            return false;
        }

        public IReadOnlyList<Vector3> BuildBaseRoute(
            int robotId,
            int fromStationId,
            int toStationId,
            Vector3 from,
            Vector3 to)
        {
            reusableRoute.Clear();

            if (!enableLaneRouting || IsNearSamePoint(from, to))
            {
                AddRoutePoint(to);
                return reusableRoute;
            }

            float laneX = GetLaneX(robotId, toStationId);
            float targetLaneZ = GetLaneZ(toStationId, to);

            AddRoutePoint(new Vector3(laneX, from.y, from.z));
            AddRoutePoint(new Vector3(laneX, from.y, targetLaneZ));
            AddRoutePoint(new Vector3(to.x, from.y, targetLaneZ));
            AddRoutePoint(to);

            return reusableRoute;
        }

        public IReadOnlyList<Vector3> BuildYieldCandidates(
            int robotId,
            Vector3 from,
            Vector3 originalTarget)
        {
            reusableYieldCandidates.Clear();

            float distance = Mathf.Max(0.5f, detourDistance);
            Vector3 direction = FlattenXZ(originalTarget - from);
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.forward;
            }

            direction.Normalize();
            Vector3 perpendicular = new Vector3(-direction.z, 0f, direction.x);
            if (robotId == StudentConstants.RobotBId)
            {
                perpendicular = -perpendicular;
            }

            Vector3 backward = -direction;
            Vector3 diagonalA = (perpendicular + backward).normalized;
            Vector3 diagonalB = (-perpendicular + backward).normalized;
            float laneX = GetLaneX(robotId, StudentConstants.NoStationId);

            AddBoxBypassCandidates(from, originalTarget);
            AddYieldCandidate(from + perpendicular * distance);
            AddYieldCandidate(from - perpendicular * distance);
            AddYieldCandidate(new Vector3(laneX, from.y, from.z));
            AddYieldCandidate(from + backward * distance);
            AddYieldCandidate(from + diagonalA * distance);
            AddYieldCandidate(from + diagonalB * distance);

            return reusableYieldCandidates;
        }

        public bool IsBasePathBlocked(
            int robotId,
            Vector3 from,
            Vector3 to,
            out int blockingRobotId)
        {
            return IsBasePathBlocked(
                robotId,
                StudentConstants.NoStationId,
            from,
            to,
            out blockingRobotId,
            out _,
            out _);
        }

        public bool IsBasePathBlocked(
            int robotId,
            int targetStationId,
            Vector3 from,
            Vector3 to,
            out int blockingRobotId,
            out bool waitForSameBox,
            out bool preferDetour)
        {
            blockingRobotId = StudentConstants.UnassignedRobotId;
            waitForSameBox = false;
            preferDetour = false;

            if (robotControllers == null || IsNearSamePoint(from, to))
            {
                return false;
            }

            Vector2 start = ToXZ(from);
            Vector2 end = ToXZ(to);
            Vector2 path = end - start;
            float pathLengthSqr = path.sqrMagnitude;
            if (pathLengthSqr <= Mathf.Epsilon)
            {
                return false;
            }

            int targetBoxStationId = GetTargetBoxStationId(targetStationId, to);
            if (IsSameBoxClaimedByEarlierPath(
                robotId,
                targetBoxStationId,
                out int sameBoxBlockingRobotId))
            {
                blockingRobotId = sameBoxBlockingRobotId;
                waitForSameBox = true;
                return true;
            }

            for (int i = 0; i < robotControllers.Length; i++)
            {
                IRobotController controller = robotControllers[i];
                if (controller == null || controller.RobotId == robotId)
                {
                    continue;
                }

                int fixedWorkStationId = GetFixedWorkStationId(controller);
                bool isBlockingConveyorWork =
                    StudentConstants.IsConveyorId(fixedWorkStationId)
                    && fixedWorkStationId != targetStationId;

                int workingBoxStationId = GetWorkingBoxStationId(controller);
                if (workingBoxStationId != StudentConstants.NoStationId)
                {
                    float workerDistanceToPath = PointSegmentDistanceXZ(controller.Position, from, to);
                    if (targetBoxStationId == workingBoxStationId)
                    {
                        if (CurrentRobotHasSameBoxPriority(
                            robotId,
                            targetBoxStationId,
                            controller.RobotId)
                            && workerDistanceToPath > hardStationaryClearanceRadius)
                        {
                            continue;
                        }

                        blockingRobotId = controller.RobotId;
                        waitForSameBox = true;
                        return true;
                    }

                    if (workerDistanceToPath <= stationaryWorkClearanceRadius)
                    {
                        blockingRobotId = controller.RobotId;
                        preferDetour = workerDistanceToPath > hardStationaryClearanceRadius;
                        return true;
                    }
                }

                if (isBlockingConveyorWork
                    && PointSegmentDistanceXZ(controller.Position, from, to) <= stationaryWorkClearanceRadius)
                {
                    blockingRobotId = controller.RobotId;
                    preferDetour = true;
                    return true;
                }

                Vector2 other = ToXZ(controller.Position);
                float progress = Vector2.Dot(other - start, path) / pathLengthSqr;
                if (progress <= 0f || progress > 1f)
                {
                    continue;
                }

                float distanceAhead = Mathf.Sqrt(pathLengthSqr) * progress;
                if (distanceAhead <= ignoreBehindDistance)
                {
                    continue;
                }

                float distanceToPath = PointSegmentDistance(other, start, end);
                float threshold = controller.IsBusy
                    ? movingBlockDistance
                    : stationaryWorkClearanceRadius;
                if (distanceToPath <= threshold)
                {
                    if (CurrentRobotHasSameBoxPriority(
                        robotId,
                        targetBoxStationId,
                        controller.RobotId)
                        && distanceToPath > hardStationaryClearanceRadius)
                    {
                        continue;
                    }

                    if (EmptyRobotHasPriority(robotId, controller.RobotId)
                        && distanceToPath > hardStationaryClearanceRadius)
                    {
                        continue;
                    }

                    blockingRobotId = controller.RobotId;
                    preferDetour = isBlockingConveyorWork
                        && distanceToPath > hardStationaryClearanceRadius;
                    return true;
                }
            }

            return IsBlockedByCrossingPath(robotId, from, to, out blockingRobotId);
        }

        public void RegisterActiveBasePath(
            int robotId,
            int taskId,
            Vector3 from,
            Vector3 to,
            bool hasPayload)
        {
            if (!StudentConstants.IsRobotId(robotId))
            {
                return;
            }

            float startedAt = Time.time;
            if (activeBasePaths.TryGetValue(robotId, out ActiveBasePath existing)
                && existing != null
                && existing.TaskId == taskId)
            {
                startedAt = existing.StartedAt;
            }

            activeBasePaths[robotId] = new ActiveBasePath
            {
                RobotId = robotId,
                TaskId = taskId,
                From = from,
                To = to,
                HasPayload = hasPayload,
                StartedAt = startedAt,
                RegisteredAt = Time.time
            };

            telemetryLogger?.LogMessage(
                "Path",
                string.Format(
                    "Active path robot={0} task={1} from=({2:0.0},{3:0.0}) to=({4:0.0},{5:0.0}) payload={6}.",
                    robotId,
                    taskId,
                    from.x,
                    from.z,
                    to.x,
                    to.z,
                    hasPayload));
        }

        public void ClearActiveBasePath(int robotId, int taskId)
        {
            if (!activeBasePaths.TryGetValue(robotId, out ActiveBasePath path))
            {
                return;
            }

            if (path.TaskId != taskId)
            {
                return;
            }

            activeBasePaths.Remove(robotId);
        }

        public bool TryReserveBaseSegment(
            int robotId,
            int taskId,
            Vector3 from,
            Vector3 to,
            float priority,
            out PathReservationToken token,
            out int blockingRobotId,
            out int blockingTaskId)
        {
            token = null;
            blockingRobotId = StudentConstants.UnassignedRobotId;
            blockingTaskId = StudentConstants.NoTaskId;

            if (!enableSegmentReservation || IsNearSamePoint(from, to))
            {
                return true;
            }

            PurgeStaleReservations();

            if (CrossesStaticKeepOut(from, to))
            {
                blockingRobotId = StudentConstants.UnassignedRobotId;
                blockingTaskId = StudentConstants.NoTaskId;
                telemetryLogger?.LogMessage(
                    "Path",
                    string.Format(
                        "Blocked static keep-out robot={0} task={1} from=({2:0.0},{3:0.0}) to=({4:0.0},{5:0.0}).",
                        robotId,
                        taskId,
                        from.x,
                        from.z,
                        to.x,
                        to.z));
                return false;
            }

            for (int i = 0; i < activeReservations.Count; i++)
            {
                PathReservationToken existing = activeReservations[i];
                if (existing == null || existing.robotId == robotId)
                {
                    continue;
                }

                if (SegmentsConflict(from, to, existing.from, existing.to, hardSegmentClearanceRadius))
                {
                    blockingRobotId = existing.robotId;
                    blockingTaskId = existing.taskId;
                    return false;
                }

                if (SegmentsConflict(from, to, existing.from, existing.to, segmentClearanceRadius)
                    && HasTemporalConflict(from, to, existing))
                {
                    blockingRobotId = existing.robotId;
                    blockingTaskId = existing.taskId;
                    return false;
                }
            }

            if (ConflictsWithStationaryRobot(
                robotId,
                from,
                to,
                out blockingRobotId,
                out blockingTaskId))
            {
                return false;
            }

            token = new PathReservationToken
            {
                robotId = robotId,
                taskId = taskId,
                from = from,
                to = to,
                priority = priority,
                acquiredAt = Time.time,
                expiresAt = Time.time + reservationStaleSec
            };

            activeReservations.Add(token);
            telemetryLogger?.LogMessage(
                "Path",
                string.Format(
                    "Reserve robot={0} task={1} from=({2:0.0},{3:0.0}) to=({4:0.0},{5:0.0}) priority={6:0.00}.",
                    robotId,
                    taskId,
                    from.x,
                    from.z,
                    to.x,
                    to.z,
                    priority));
            return true;
        }

        public void ReleaseBaseSegment(PathReservationToken token)
        {
            if (token == null)
            {
                return;
            }

            for (int i = activeReservations.Count - 1; i >= 0; i--)
            {
                if (activeReservations[i] == token)
                {
                    activeReservations.RemoveAt(i);
                    telemetryLogger?.LogMessage(
                        "Path",
                        string.Format(
                            "Release robot={0} task={1}.",
                            token.robotId,
                            token.taskId));
                    return;
                }
            }
        }

        private void OnValidate()
        {
            NormalizeSettings();
        }

        private void NormalizeSettings()
        {
            robotAMinConveyor = Mathf.Clamp(
                robotAMinConveyor,
                StudentConstants.MinConveyorId,
                StudentConstants.MaxConveyorId);
            robotAMaxConveyor = Mathf.Clamp(
                robotAMaxConveyor,
                robotAMinConveyor,
                StudentConstants.MaxConveyorId);
            robotBMinConveyor = Mathf.Clamp(
                robotBMinConveyor,
                StudentConstants.MinConveyorId,
                StudentConstants.MaxConveyorId);
            robotBMaxConveyor = Mathf.Clamp(
                robotBMaxConveyor,
                robotBMinConveyor,
                StudentConstants.MaxConveyorId);
            segmentClearanceRadius = Mathf.Max(2.6f, segmentClearanceRadius);
            stationaryClearanceRadius = Mathf.Max(2.5f, stationaryClearanceRadius);
            hardSegmentClearanceRadius = Mathf.Min(
                Mathf.Max(1.8f, hardSegmentClearanceRadius),
                segmentClearanceRadius);
            hardStationaryClearanceRadius = Mathf.Min(
                Mathf.Max(1.8f, hardStationaryClearanceRadius),
                stationaryClearanceRadius);
            reservationStaleSec = Mathf.Max(0.1f, reservationStaleSec);
            estimatedBaseSpeed = Mathf.Max(0.1f, estimatedBaseSpeed);
            conflictTimeWindowSec = Mathf.Max(0f, conflictTimeWindowSec);
            movingBlockDistance = Mathf.Max(2.35f, movingBlockDistance);
            movingResumeDistance = Mathf.Max(Mathf.Max(2.8f, movingBlockDistance), movingResumeDistance);
            stationaryWorkClearanceRadius = Mathf.Max(movingResumeDistance, stationaryWorkClearanceRadius);
            ignoreBehindDistance = Mathf.Max(0f, ignoreBehindDistance);
            crossingHoldDistance = Mathf.Max(3.0f, crossingHoldDistance);
            crossingPriorityMargin = Mathf.Max(0.8f, crossingPriorityMargin);
            pathStartPriorityMarginSec = Mathf.Max(0f, pathStartPriorityMarginSec);
            activePathStaleSec = Mathf.Max(0.1f, activePathStaleSec);
            waypointMergeDistance = Mathf.Max(0.1f, waypointMergeDistance);
            detourDistance = Mathf.Max(4.5f, detourDistance);
            boxKeepOutRadius = Mathf.Max(2.25f, boxKeepOutRadius);
            boxBypassPadding = Mathf.Max(1.6f, boxBypassPadding);
        }

        private bool IsCrossSideMove(int fromStationId, int toStationId)
        {
            return (IsRobotAOwnedConveyor(fromStationId) && IsRobotBOwnedConveyor(toStationId))
                || (IsRobotBOwnedConveyor(fromStationId) && IsRobotAOwnedConveyor(toStationId));
        }

        private bool IsRobotAOwnedConveyor(int stationId)
        {
            return stationId >= robotAMinConveyor && stationId <= robotAMaxConveyor;
        }

        private bool IsRobotBOwnedConveyor(int stationId)
        {
            return stationId >= robotBMinConveyor && stationId <= robotBMaxConveyor;
        }

        private static bool IsKnownStation(int stationId)
        {
            return StudentConstants.IsConveyorId(stationId)
                || StudentConstants.IsBoxStationId(stationId);
        }

        private void PurgeStaleReservations()
        {
            float now = Time.time;
            for (int i = activeReservations.Count - 1; i >= 0; i--)
            {
                PathReservationToken token = activeReservations[i];
                if (token == null || token.expiresAt <= now)
                {
                    activeReservations.RemoveAt(i);
                }
            }
        }

        private bool ConflictsWithStationaryRobot(
            int robotId,
            Vector3 from,
            Vector3 to,
            out int blockingRobotId,
            out int blockingTaskId)
        {
            blockingRobotId = StudentConstants.UnassignedRobotId;
            blockingTaskId = StudentConstants.NoTaskId;

            if (robotControllers == null)
            {
                return false;
            }

            for (int i = 0; i < robotControllers.Length; i++)
            {
                IRobotController controller = robotControllers[i];
                if (controller == null || controller.RobotId == robotId)
                {
                    continue;
                }

                Vector3 otherPosition = controller.Position;
                float distanceToPath = PointSegmentDistanceXZ(otherPosition, from, to);
                if (distanceToPath <= hardStationaryClearanceRadius)
                {
                    blockingRobotId = controller.RobotId;
                    blockingTaskId = StudentConstants.NoTaskId;
                    return true;
                }

                if (controller.IsBusy && distanceToPath > hardStationaryClearanceRadius)
                {
                    continue;
                }

                if (MovingAwayFromNearbyRobot(from, to, otherPosition)
                    && distanceToPath > movingResumeDistance)
                {
                    continue;
                }

                if (DistanceXZ(to, otherPosition) > DistanceXZ(from, otherPosition)
                    && DistanceXZ(from, otherPosition) <= stationaryClearanceRadius * 1.25f
                    && distanceToPath > movingResumeDistance)
                {
                    continue;
                }

                float clearanceRadius = controller.IsBusy
                    ? stationaryClearanceRadius
                    : stationaryWorkClearanceRadius;
                if (distanceToPath <= clearanceRadius)
                {
                    blockingRobotId = controller.RobotId;
                    blockingTaskId = StudentConstants.NoTaskId;
                    return true;
                }
            }

            return false;
        }

        private bool HasTemporalConflict(
            Vector3 from,
            Vector3 to,
            PathReservationToken existing)
        {
            if (!enableTemporalConflictCheck || existing == null)
            {
                return true;
            }

            ClosestPointsOnSegmentsXZ(
                from,
                to,
                existing.from,
                existing.to,
                out Vector3 candidateConflictPoint,
                out Vector3 existingConflictPoint);

            Vector3 existingCurrent = GetRobotPosition(existing.robotId, existing.from);
            float candidateTime = DistanceXZ(from, candidateConflictPoint) / estimatedBaseSpeed;
            float existingTime = DistanceXZ(existingCurrent, existingConflictPoint) / estimatedBaseSpeed;

            return Mathf.Abs(candidateTime - existingTime) <= conflictTimeWindowSec;
        }

        private bool IsBlockedByCrossingPath(
            int robotId,
            Vector3 from,
            Vector3 to,
            out int blockingRobotId)
        {
            blockingRobotId = StudentConstants.UnassignedRobotId;
            if (activeBasePaths.Count == 0)
            {
                return false;
            }

            PurgeStaleActivePaths();

            foreach (KeyValuePair<int, ActiveBasePath> pair in activeBasePaths)
            {
                ActiveBasePath other = pair.Value;
                if (other == null || other.RobotId == robotId)
                {
                    continue;
                }

                ClosestPointsOnSegmentsXZ(
                    from,
                    to,
                    other.From,
                    other.To,
                    out Vector3 myCrossPoint,
                    out Vector3 otherCrossPoint);

                if (DistanceXZ(myCrossPoint, otherCrossPoint) > segmentClearanceRadius)
                {
                    continue;
                }

                Vector3 otherCurrent = GetRobotPosition(other.RobotId, other.From);
                if (HasPassedPoint(from, to, myCrossPoint)
                    || HasPassedPoint(otherCurrent, other.To, otherCrossPoint))
                {
                    continue;
                }

                float myDistanceToCrossing = DistanceXZ(from, myCrossPoint);
                float otherDistanceToCrossing = DistanceXZ(otherCurrent, otherCrossPoint);
                int targetBoxStationId = GetTargetBoxStationId(StudentConstants.NoStationId, to);
                bool currentHasSameBoxPriority = CurrentRobotHasSameBoxPriority(
                    robotId,
                    targetBoxStationId,
                    other.RobotId);
                bool otherHasPriority =
                    !currentHasSameBoxPriority
                    && (OtherEmptyRobotHasPriority(robotId, other.RobotId)
                    || otherDistanceToCrossing + crossingPriorityMargin < myDistanceToCrossing
                    || (Mathf.Abs(otherDistanceToCrossing - myDistanceToCrossing) <= crossingPriorityMargin
                        && other.RobotId < robotId));

                if (currentHasSameBoxPriority || EmptyRobotHasPriority(robotId, other.RobotId))
                {
                    otherHasPriority = false;
                }

                if (!otherHasPriority)
                {
                    continue;
                }

                if (stopImmediatelyForLowerPriorityCrossing
                    || myDistanceToCrossing <= crossingHoldDistance
                    || otherDistanceToCrossing <= crossingHoldDistance)
                {
                    blockingRobotId = other.RobotId;
                    return true;
                }
            }

            return false;
        }

        private int GetWorkingBoxStationId(IRobotController controller)
        {
            if (controller == null)
            {
                return StudentConstants.NoStationId;
            }

            IRobotAgent agent = GetRobotAgent(controller.RobotId);
            bool isWorking = agent != null
                ? IsBoxWorkState(agent.State)
                : controller.IsBusy;
            if (!isWorking)
            {
                return StudentConstants.NoStationId;
            }

            Vector3 position = controller.Position;
            if (DistanceXZ(position, GetBoxStationPosition(StudentConstants.NormalBoxStationId))
                <= stationaryWorkClearanceRadius)
            {
                return StudentConstants.NormalBoxStationId;
            }

            if (DistanceXZ(position, GetBoxStationPosition(StudentConstants.AbnormalBoxStationId))
                <= stationaryWorkClearanceRadius)
            {
                return StudentConstants.AbnormalBoxStationId;
            }

            return StudentConstants.NoStationId;
        }

        private int GetFixedWorkStationId(IRobotController controller)
        {
            if (controller == null)
            {
                return StudentConstants.NoStationId;
            }

            IRobotAgent agent = GetRobotAgent(controller.RobotId);
            if (agent == null || !IsFixedConveyorWorkState(agent.State))
            {
                return StudentConstants.NoStationId;
            }

            return FindNearestConveyorStation(controller.Position);
        }

        private int FindNearestConveyorStation(Vector3 position)
        {
            if (operatingStations == null)
            {
                return StudentConstants.NoStationId;
            }

            int nearestStationId = StudentConstants.NoStationId;
            float nearestDistance = float.MaxValue;
            for (int stationId = StudentConstants.MinConveyorId;
                stationId <= StudentConstants.MaxConveyorId;
                stationId++)
            {
                if (!operatingStations.TryGetStation(
                    stationId,
                    out OperatingStations.Station station))
                {
                    continue;
                }

                float distance = DistanceXZ(position, station.BasePosition);
                if (distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = distance;
                nearestStationId = stationId;
            }

            return nearestDistance <= stationaryWorkClearanceRadius
                ? nearestStationId
                : StudentConstants.NoStationId;
        }

        private bool EmptyRobotHasPriority(int robotId, int otherRobotId)
        {
            return !RobotHasPayload(robotId) && RobotHasPayload(otherRobotId);
        }

        private bool OtherEmptyRobotHasPriority(int robotId, int otherRobotId)
        {
            return RobotHasPayload(robotId) && !RobotHasPayload(otherRobotId);
        }

        private bool RobotHasPayload(int robotId)
        {
            if (activeBasePaths.TryGetValue(robotId, out ActiveBasePath path)
                && path != null)
            {
                return path.HasPayload;
            }

            IRobotAgent agent = GetRobotAgent(robotId);
            return agent != null && IsPayloadState(agent.State);
        }

        private static bool IsBoxWorkState(RobotRuntimeState state)
        {
            return state == RobotRuntimeState.Picking
                || state == RobotRuntimeState.Placing
                || state == RobotRuntimeState.Releasing
                || state == RobotRuntimeState.Retracting;
        }

        private static bool IsPayloadState(RobotRuntimeState state)
        {
            return state == RobotRuntimeState.MovingToBox
                || state == RobotRuntimeState.Placing;
        }

        private static bool IsFixedConveyorWorkState(RobotRuntimeState state)
        {
            return state == RobotRuntimeState.Picking
                || state == RobotRuntimeState.Retracting;
        }

        private IRobotAgent GetRobotAgent(int robotId)
        {
            if (robotAgents == null)
            {
                return null;
            }

            for (int i = 0; i < robotAgents.Length; i++)
            {
                IRobotAgent agent = robotAgents[i];
                if (agent != null && agent.RobotId == robotId)
                {
                    return agent;
                }
            }

            return null;
        }

        private int GetTargetBoxStationId(int targetStationId, Vector3 targetPosition)
        {
            if (StudentConstants.IsBoxStationId(targetStationId))
            {
                return targetStationId;
            }

            if (DistanceXZ(targetPosition, GetBoxStationPosition(StudentConstants.NormalBoxStationId))
                <= waypointMergeDistance)
            {
                return StudentConstants.NormalBoxStationId;
            }

            if (DistanceXZ(targetPosition, GetBoxStationPosition(StudentConstants.AbnormalBoxStationId))
                <= waypointMergeDistance)
            {
                return StudentConstants.AbnormalBoxStationId;
            }

            return StudentConstants.NoStationId;
        }

        private bool IsSameBoxClaimedByEarlierPath(
            int robotId,
            int targetBoxStationId,
            out int blockingRobotId)
        {
            blockingRobotId = StudentConstants.UnassignedRobotId;
            if (!StudentConstants.IsBoxStationId(targetBoxStationId)
                || activeBasePaths.Count == 0)
            {
                return false;
            }

            PurgeStaleActivePaths();

            activeBasePaths.TryGetValue(robotId, out ActiveBasePath mine);
            foreach (KeyValuePair<int, ActiveBasePath> pair in activeBasePaths)
            {
                ActiveBasePath other = pair.Value;
                if (other == null || other.RobotId == robotId)
                {
                    continue;
                }

                int otherTargetBoxStationId = GetTargetBoxStationId(
                    StudentConstants.NoStationId,
                    other.To);
                if (otherTargetBoxStationId != targetBoxStationId)
                {
                    continue;
                }

                bool otherHasPriority = mine == null
                    || other.StartedAt + pathStartPriorityMarginSec < mine.StartedAt
                    || (Mathf.Abs(other.StartedAt - mine.StartedAt) <= pathStartPriorityMarginSec
                        && other.RobotId < robotId);
                if (!otherHasPriority)
                {
                    continue;
                }

                blockingRobotId = other.RobotId;
                return true;
            }

            return false;
        }

        private bool CurrentRobotHasSameBoxPriority(
            int robotId,
            int targetBoxStationId,
            int otherRobotId)
        {
            if (!StudentConstants.IsBoxStationId(targetBoxStationId))
            {
                return false;
            }

            if (!activeBasePaths.TryGetValue(robotId, out ActiveBasePath mine)
                || mine == null)
            {
                return false;
            }

            if (!activeBasePaths.TryGetValue(otherRobotId, out ActiveBasePath other)
                || other == null)
            {
                return false;
            }

            int otherTargetBoxStationId = GetTargetBoxStationId(
                StudentConstants.NoStationId,
                other.To);
            if (otherTargetBoxStationId != targetBoxStationId)
            {
                return false;
            }

            if (mine.StartedAt + pathStartPriorityMarginSec < other.StartedAt)
            {
                return true;
            }

            if (other.StartedAt + pathStartPriorityMarginSec < mine.StartedAt)
            {
                return false;
            }

            return robotId < otherRobotId;
        }

        private static Vector3 GetBoxStationPosition(int stationId)
        {
            if (stationId == StudentConstants.NormalBoxStationId)
            {
                return new Vector3(0f, 0f, -6f);
            }

            if (stationId == StudentConstants.AbnormalBoxStationId)
            {
                return new Vector3(8.5f, 0f, 2.5f);
            }

            return Vector3.zero;
        }

        private void PurgeStaleActivePaths()
        {
            float now = Time.time;
            var staleRobotIds = new List<int>();
            foreach (KeyValuePair<int, ActiveBasePath> pair in activeBasePaths)
            {
                ActiveBasePath path = pair.Value;
                if (path == null || now - path.RegisteredAt >= activePathStaleSec)
                {
                    staleRobotIds.Add(pair.Key);
                }
            }

            for (int i = 0; i < staleRobotIds.Count; i++)
            {
                activeBasePaths.Remove(staleRobotIds[i]);
            }
        }

        private static bool HasPassedPoint(Vector3 current, Vector3 target, Vector3 point)
        {
            Vector2 currentToTarget = ToXZ(target) - ToXZ(current);
            Vector2 currentToPoint = ToXZ(point) - ToXZ(current);
            if (currentToTarget.sqrMagnitude <= Mathf.Epsilon)
            {
                return true;
            }

            return Vector2.Dot(currentToPoint, currentToTarget) < -0.05f;
        }

        private Vector3 GetRobotPosition(int robotId, Vector3 fallback)
        {
            if (robotControllers == null)
            {
                return fallback;
            }

            for (int i = 0; i < robotControllers.Length; i++)
            {
                IRobotController controller = robotControllers[i];
                if (controller != null && controller.RobotId == robotId)
                {
                    return controller.Position;
                }
            }

            return fallback;
        }

        private bool MovingAwayFromNearbyRobot(Vector3 from, Vector3 to, Vector3 otherPosition)
        {
            float startDistance = DistanceXZ(from, otherPosition);
            if (startDistance > stationaryClearanceRadius)
            {
                return false;
            }

            return DistanceXZ(to, otherPosition) > startDistance + 0.25f;
        }

        private float GetLaneX(int robotId, int stationId)
        {
            if (StudentConstants.IsBoxStationId(stationId))
            {
                return stationId == StudentConstants.NormalBoxStationId
                    ? robotALaneX
                    : robotBLaneX;
            }

            return robotId == StudentConstants.RobotBId ? robotBLaneX : robotALaneX;
        }

        private float GetLaneZ(int stationId, Vector3 target)
        {
            if (stationId == StudentConstants.NormalBoxStationId)
            {
                return lowerLaneZ;
            }

            if (stationId == StudentConstants.AbnormalBoxStationId)
            {
                return target.z;
            }

            if (stationId >= 6 && stationId <= StudentConstants.MaxConveyorId)
            {
                return upperLaneZ;
            }

            return target.z;
        }

        private void AddRoutePoint(Vector3 point)
        {
            point = ClampToWorld(point);

            if (reusableRoute.Count > 0
                && DistanceXZ(reusableRoute[reusableRoute.Count - 1], point) <= waypointMergeDistance)
            {
                return;
            }

            reusableRoute.Add(point);
        }

        private void AddYieldCandidate(Vector3 point)
        {
            point = ClampToWorld(point);

            for (int i = 0; i < reusableYieldCandidates.Count; i++)
            {
                if (DistanceXZ(reusableYieldCandidates[i], point) <= waypointMergeDistance)
                {
                    return;
                }
            }

            reusableYieldCandidates.Add(point);
        }

        private void AddBoxBypassCandidates(Vector3 from, Vector3 target)
        {
            if (!avoidBoxKeepOutZones)
            {
                return;
            }

            AddBoxBypassCandidates(from, target, normalBoxCenter);
            AddBoxBypassCandidates(from, target, abnormalBoxCenter);
        }

        private void AddBoxBypassCandidates(Vector3 from, Vector3 target, Vector3 center)
        {
            if (PointSegmentDistanceXZ(center, from, target) > boxKeepOutRadius + 0.05f)
            {
                return;
            }

            float radius = boxKeepOutRadius + boxBypassPadding;
            Vector3 toTarget = FlattenXZ(target - from);
            if (toTarget.sqrMagnitude <= 0.0001f)
            {
                toTarget = Vector3.forward;
            }

            toTarget.Normalize();
            Vector3 side = new Vector3(-toTarget.z, 0f, toTarget.x);
            Vector3 sideA = center + side * radius;
            Vector3 sideB = center - side * radius;
            Vector3 forwardA = sideA + toTarget * radius;
            Vector3 forwardB = sideB + toTarget * radius;
            Vector3 awayFromStart = FlattenXZ(from - center);
            if (awayFromStart.sqrMagnitude <= 0.0001f)
            {
                awayFromStart = -toTarget;
            }

            awayFromStart.Normalize();

            List<Vector3> candidates = new List<Vector3>(12);
            AddBoxBypassCandidate(candidates, sideA, center);
            AddBoxBypassCandidate(candidates, forwardA, center);
            AddBoxBypassCandidate(candidates, sideB, center);
            AddBoxBypassCandidate(candidates, forwardB, center);
            AddBoxBypassCandidate(candidates, center + awayFromStart * radius, center);
            AddBoxBypassCandidate(candidates, center + Vector3.left * radius, center);
            AddBoxBypassCandidate(candidates, center + Vector3.right * radius, center);
            AddBoxBypassCandidate(candidates, center + Vector3.forward * radius, center);
            AddBoxBypassCandidate(candidates, center + Vector3.back * radius, center);
            AddBoxBypassCandidate(candidates, center + (Vector3.left + Vector3.forward).normalized * radius, center);
            AddBoxBypassCandidate(candidates, center + (Vector3.left + Vector3.back).normalized * radius, center);
            AddBoxBypassCandidate(candidates, center + (Vector3.right + Vector3.forward).normalized * radius, center);
            AddBoxBypassCandidate(candidates, center + (Vector3.right + Vector3.back).normalized * radius, center);

            candidates.Sort((left, right) =>
                CalculateBypassScore(from, target, left).CompareTo(
                    CalculateBypassScore(from, target, right)));

            for (int i = 0; i < candidates.Count; i++)
            {
                AddYieldCandidate(candidates[i]);
            }
        }

        private void AddBoxBypassCandidate(List<Vector3> candidates, Vector3 point, Vector3 center)
        {
            point = ClampToWorld(point);
            if (DistanceXZ(point, center) <= boxKeepOutRadius + 0.35f)
            {
                return;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (DistanceXZ(candidates[i], point) <= waypointMergeDistance)
                {
                    return;
                }
            }

            candidates.Add(point);
        }

        private float CalculateBypassScore(Vector3 from, Vector3 target, Vector3 candidate)
        {
            float score = DistanceXZ(from, candidate) + DistanceXZ(candidate, target);
            if (CrossesStaticKeepOut(from, candidate))
            {
                score += 1000f;
            }

            if (CrossesStaticKeepOut(candidate, target))
            {
                score += 100f;
            }

            return score;
        }

        private Vector3 ClampToWorld(Vector3 value)
        {
            value.x = Mathf.Clamp(value.x, worldMinX, worldMaxX);
            value.z = Mathf.Clamp(value.z, worldMinZ, worldMaxZ);
            return value;
        }

        private static Vector3 FlattenXZ(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private bool SegmentsConflict(
            Vector3 leftFrom,
            Vector3 leftTo,
            Vector3 rightFrom,
            Vector3 rightTo,
            float clearanceRadius)
        {
            return SegmentDistanceXZ(leftFrom, leftTo, rightFrom, rightTo) <= clearanceRadius;
        }

        private bool CrossesStaticKeepOut(Vector3 from, Vector3 to)
        {
            if (!avoidBoxKeepOutZones)
            {
                return false;
            }

            return CrossesBoxKeepOut(from, to, normalBoxCenter, StudentConstants.NormalBoxStationId)
                || CrossesBoxKeepOut(from, to, abnormalBoxCenter, StudentConstants.AbnormalBoxStationId);
        }

        private bool CrossesBoxKeepOut(
            Vector3 from,
            Vector3 to,
            Vector3 boxCenter,
            int stationId)
        {
            if (IsAllowedBoxApproach(from, to, stationId))
            {
                return false;
            }

            if (IsLeavingBoxApproach(from, to, boxCenter, stationId))
            {
                return false;
            }

            return PointSegmentDistanceXZ(boxCenter, from, to) <= boxKeepOutRadius;
        }

        private bool IsAllowedBoxApproach(Vector3 from, Vector3 to, int stationId)
        {
            if (stationId == StudentConstants.NormalBoxStationId)
            {
                Vector3 station = GetBoxStationPosition(stationId);
                return DistanceXZ(to, station) <= 0.35f
                    && from.z >= station.z - 0.2f;
            }

            if (stationId == StudentConstants.AbnormalBoxStationId)
            {
                Vector3 station = GetBoxStationPosition(stationId);
                return DistanceXZ(to, station) <= 0.35f
                    && from.x <= station.x + 0.2f;
            }

            return false;
        }

        private bool IsLeavingBoxApproach(
            Vector3 from,
            Vector3 to,
            Vector3 boxCenter,
            int stationId)
        {
            Vector3 station;
            if (stationId == StudentConstants.NormalBoxStationId)
            {
                station = GetBoxStationPosition(stationId);
            }
            else if (stationId == StudentConstants.AbnormalBoxStationId)
            {
                station = GetBoxStationPosition(stationId);
            }
            else
            {
                return false;
            }

            if (DistanceXZ(from, station) > 0.75f)
            {
                return false;
            }

            float startDistance = DistanceXZ(from, boxCenter);
            float endDistance = DistanceXZ(to, boxCenter);
            return endDistance > startDistance + 0.25f;
        }

        private static bool IsNearSamePoint(Vector3 from, Vector3 to)
        {
            return DistanceXZ(from, to) <= 0.05f;
        }

        private static float SegmentDistanceXZ(
            Vector3 leftFrom,
            Vector3 leftTo,
            Vector3 rightFrom,
            Vector3 rightTo)
        {
            Vector2 a = ToXZ(leftFrom);
            Vector2 b = ToXZ(leftTo);
            Vector2 c = ToXZ(rightFrom);
            Vector2 d = ToXZ(rightTo);

            if (SegmentsIntersect(a, b, c, d))
            {
                return 0f;
            }

            return Mathf.Min(
                Mathf.Min(PointSegmentDistance(a, c, d), PointSegmentDistance(b, c, d)),
                Mathf.Min(PointSegmentDistance(c, a, b), PointSegmentDistance(d, a, b)));
        }

        private static void ClosestPointsOnSegmentsXZ(
            Vector3 leftFrom,
            Vector3 leftTo,
            Vector3 rightFrom,
            Vector3 rightTo,
            out Vector3 leftClosest,
            out Vector3 rightClosest)
        {
            Vector2 a = ToXZ(leftFrom);
            Vector2 b = ToXZ(leftTo);
            Vector2 c = ToXZ(rightFrom);
            Vector2 d = ToXZ(rightTo);

            if (TryGetSegmentIntersection(a, b, c, d, out Vector2 intersection))
            {
                leftClosest = new Vector3(intersection.x, leftFrom.y, intersection.y);
                rightClosest = new Vector3(intersection.x, rightFrom.y, intersection.y);
                return;
            }

            Vector2 bestLeft = a;
            Vector2 bestRight = ClosestPointOnSegment(a, c, d);
            float bestDistance = Vector2.SqrMagnitude(bestLeft - bestRight);

            ConsiderClosestPair(b, ClosestPointOnSegment(b, c, d), ref bestLeft, ref bestRight, ref bestDistance);
            ConsiderClosestPair(ClosestPointOnSegment(c, a, b), c, ref bestLeft, ref bestRight, ref bestDistance);
            ConsiderClosestPair(ClosestPointOnSegment(d, a, b), d, ref bestLeft, ref bestRight, ref bestDistance);

            leftClosest = new Vector3(bestLeft.x, leftFrom.y, bestLeft.y);
            rightClosest = new Vector3(bestRight.x, rightFrom.y, bestRight.y);
        }

        private static float PointSegmentDistanceXZ(Vector3 point, Vector3 segmentFrom, Vector3 segmentTo)
        {
            return PointSegmentDistance(ToXZ(point), ToXZ(segmentFrom), ToXZ(segmentTo));
        }

        private static float DistanceXZ(Vector3 left, Vector3 right)
        {
            return Vector2.Distance(ToXZ(left), ToXZ(right));
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }

        private static float PointSegmentDistance(Vector2 point, Vector2 segmentFrom, Vector2 segmentTo)
        {
            return Vector2.Distance(point, ClosestPointOnSegment(point, segmentFrom, segmentTo));
        }

        private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 segmentFrom, Vector2 segmentTo)
        {
            Vector2 segment = segmentTo - segmentFrom;
            float sqrMagnitude = segment.sqrMagnitude;
            if (sqrMagnitude <= Mathf.Epsilon)
            {
                return segmentFrom;
            }

            float t = Vector2.Dot(point - segmentFrom, segment) / sqrMagnitude;
            t = Mathf.Clamp01(t);
            return segmentFrom + segment * t;
        }

        private static void ConsiderClosestPair(
            Vector2 left,
            Vector2 right,
            ref Vector2 bestLeft,
            ref Vector2 bestRight,
            ref float bestDistance)
        {
            float distance = Vector2.SqrMagnitude(left - right);
            if (distance >= bestDistance)
            {
                return;
            }

            bestLeft = left;
            bestRight = right;
            bestDistance = distance;
        }

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float abC = Cross(b - a, c - a);
            float abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c);
            float cdB = Cross(d - c, b - c);

            if (Mathf.Approximately(abC, 0f) && IsPointOnSegment(c, a, b))
            {
                return true;
            }

            if (Mathf.Approximately(abD, 0f) && IsPointOnSegment(d, a, b))
            {
                return true;
            }

            if (Mathf.Approximately(cdA, 0f) && IsPointOnSegment(a, c, d))
            {
                return true;
            }

            if (Mathf.Approximately(cdB, 0f) && IsPointOnSegment(b, c, d))
            {
                return true;
            }

            return (abC > 0f) != (abD > 0f) && (cdA > 0f) != (cdB > 0f);
        }

        private static bool TryGetSegmentIntersection(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d,
            out Vector2 intersection)
        {
            intersection = default;
            Vector2 r = b - a;
            Vector2 s = d - c;
            float denominator = Cross(r, s);
            if (Mathf.Abs(denominator) <= 0.0001f)
            {
                return false;
            }

            float t = Cross(c - a, s) / denominator;
            float u = Cross(c - a, r) / denominator;
            if (t < 0f || t > 1f || u < 0f || u > 1f)
            {
                return false;
            }

            intersection = a + t * r;
            return true;
        }

        private static bool IsPointOnSegment(Vector2 point, Vector2 segmentFrom, Vector2 segmentTo)
        {
            return point.x >= Mathf.Min(segmentFrom.x, segmentTo.x) - 0.001f
                && point.x <= Mathf.Max(segmentFrom.x, segmentTo.x) + 0.001f
                && point.y >= Mathf.Min(segmentFrom.y, segmentTo.y) - 0.001f
                && point.y <= Mathf.Max(segmentFrom.y, segmentTo.y) + 0.001f;
        }

        private static float Cross(Vector2 left, Vector2 right)
        {
            return left.x * right.y - left.y * right.x;
        }
    }
}
