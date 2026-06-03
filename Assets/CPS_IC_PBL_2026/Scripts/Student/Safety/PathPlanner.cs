using System.Collections.Generic;
using CPS.ICPBL.Common;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    [DisallowMultipleComponent]
    public sealed class PathPlanner : MonoBehaviour, IPathPlanner, IPathReservationManager
    {
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
        [SerializeField, Min(0.1f)] private float segmentClearanceRadius = 1.8f;
        [SerializeField, Min(0.1f)] private float stationaryClearanceRadius = 1.6f;
        [SerializeField, Min(0.1f)] private float reservationStaleSec = 8f;

        [Header("Lane Routing")]
        [SerializeField] private bool enableLaneRouting = true;
        [SerializeField] private float robotALaneX = -7.2f;
        [SerializeField] private float robotBLaneX = 7.2f;
        [SerializeField] private float lowerLaneZ = -7.4f;
        [SerializeField] private float upperLaneZ = 9.3f;
        [SerializeField] private float waypointMergeDistance = 0.6f;
        [SerializeField] private float detourDistance = 3f;
        [SerializeField] private float worldMinX = -9.5f;
        [SerializeField] private float worldMaxX = 10.5f;
        [SerializeField] private float worldMinZ = -8.0f;
        [SerializeField] private float worldMaxZ = 11.5f;

        private readonly List<PathReservationToken> activeReservations =
            new List<PathReservationToken>();

        private readonly List<Vector3> reusableRoute = new List<Vector3>(5);
        private readonly List<Vector3> reusableYieldCandidates = new List<Vector3>(6);

        private IRobotController[] robotControllers;
        private ITelemetryLogger telemetryLogger;

        public void ConfigureRobots(
            IRobotController robotA,
            IRobotController robotB,
            ITelemetryLogger logger = null)
        {
            robotControllers = new[] { robotA, robotB };
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

            AddYieldCandidate(from + perpendicular * distance);
            AddYieldCandidate(from - perpendicular * distance);
            AddYieldCandidate(new Vector3(laneX, from.y, from.z));
            AddYieldCandidate(from + backward * distance);
            AddYieldCandidate(from + diagonalA * distance);
            AddYieldCandidate(from + diagonalB * distance);

            return reusableYieldCandidates;
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

            for (int i = 0; i < activeReservations.Count; i++)
            {
                PathReservationToken existing = activeReservations[i];
                if (existing == null || existing.robotId == robotId)
                {
                    continue;
                }

                if (SegmentsConflict(from, to, existing.from, existing.to, segmentClearanceRadius))
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
            segmentClearanceRadius = Mathf.Max(0.1f, segmentClearanceRadius);
            stationaryClearanceRadius = Mathf.Max(0.1f, stationaryClearanceRadius);
            reservationStaleSec = Mathf.Max(0.1f, reservationStaleSec);
            waypointMergeDistance = Mathf.Max(0.1f, waypointMergeDistance);
            detourDistance = Mathf.Max(0.5f, detourDistance);
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
                if (MovingAwayFromNearbyRobot(from, to, otherPosition))
                {
                    continue;
                }

                if (DistanceXZ(to, otherPosition) > DistanceXZ(from, otherPosition)
                    && DistanceXZ(from, otherPosition) <= stationaryClearanceRadius * 1.25f)
                {
                    continue;
                }

                if (PointSegmentDistanceXZ(otherPosition, from, to) <= stationaryClearanceRadius)
                {
                    blockingRobotId = controller.RobotId;
                    blockingTaskId = StudentConstants.NoTaskId;
                    return true;
                }
            }

            return false;
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
            Vector2 segment = segmentTo - segmentFrom;
            float sqrMagnitude = segment.sqrMagnitude;
            if (sqrMagnitude <= Mathf.Epsilon)
            {
                return Vector2.Distance(point, segmentFrom);
            }

            float t = Vector2.Dot(point - segmentFrom, segment) / sqrMagnitude;
            t = Mathf.Clamp01(t);
            return Vector2.Distance(point, segmentFrom + segment * t);
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
