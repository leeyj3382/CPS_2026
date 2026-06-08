using System;
using CPS.ICPBL.Environment;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    /// <summary>
    /// Selects one Fleet-owned pending task by estimated overflow risk, queue pressure,
    /// robot travel cost, and a weak robot-area preference.
    /// This selector is non-preemptive: reserved or running tasks are never reconsidered.
    /// </summary>
    public sealed class TaskAllocator : ITaskAllocator
    {
        private const float QueuePressureLeadPerItemSec = 1.25f;
        private const float FullQueueExtraLeadSec = 1.5f;
        private const float TravelCostPerMeterSec = 0.35f;
        private const float PreferredAreaPenaltySec = 3f;
        private const float WorkStealDeadlineAdvantageSec = 6f;

        private readonly OperatingStations operatingStations;

        public TaskAllocator(OperatingStations operatingStations = null)
        {
            this.operatingStations = operatingStations;
        }

        public WorkTask SelectBestTask(
            ConveyorSnapshot[] conveyors,
            StudentRobotSnapshot robot,
            WorkTask[] pendingTasks)
        {
            return SelectBestTask(conveyors, robot, pendingTasks, true);
        }

        public WorkTask SelectBestTask(
            ConveyorSnapshot[] conveyors,
            StudentRobotSnapshot robot,
            WorkTask[] pendingTasks,
            bool allowWorkStealing)
        {
            if (conveyors == null)
            {
                throw new ArgumentNullException(nameof(conveyors));
            }

            if (robot == null)
            {
                throw new ArgumentNullException(nameof(robot));
            }

            if (pendingTasks == null)
            {
                throw new ArgumentNullException(nameof(pendingTasks));
            }

            bool useOfficialNextProductionTimes = CanUseOfficialNextProductionTimes(
                conveyors,
                pendingTasks);
            PreferredAreaContext preferredArea = BuildPreferredAreaContext(
                conveyors,
                robot.baseSnapshot.RobotId,
                pendingTasks,
                useOfficialNextProductionTimes);

            TaskCandidate bestCandidate = default;
            bool hasBestCandidate = false;

            for (int i = 0; i < pendingTasks.Length; i++)
            {
                WorkTask task = pendingTasks[i];
                if (!IsPendingAndUnassigned(task))
                {
                    continue;
                }

                ConveyorSnapshot snapshot = FindSnapshot(conveyors, task.conveyorId);
                if (!IsEligible(snapshot))
                {
                    continue;
                }

                if (!allowWorkStealing
                    && !IsPreferredConveyorForRobot(
                        robot.baseSnapshot.RobotId,
                        snapshot.conveyorId))
                {
                    continue;
                }

                TaskCandidate candidate = BuildCandidate(
                    task,
                    snapshot,
                    robot,
                    preferredArea,
                    useOfficialNextProductionTimes);

                task.priorityScore = -candidate.dispatchCost;
                task.debugReason = BuildDebugReason(
                    candidate,
                    robot.baseSnapshot.RobotId,
                    useOfficialNextProductionTimes);

                if (!hasBestCandidate || IsBetterCandidate(candidate, bestCandidate))
                {
                    bestCandidate = candidate;
                    hasBestCandidate = true;
                }
            }

            return hasBestCandidate ? bestCandidate.task : null;
        }

        private static bool IsPendingAndUnassigned(WorkTask task)
        {
            return task != null
                && task.status == TaskStatus.Pending
                && task.assignedRobotId == StudentConstants.UnassignedRobotId;
        }

        private static ConveyorSnapshot FindSnapshot(ConveyorSnapshot[] conveyors, int conveyorId)
        {
            for (int i = 0; i < conveyors.Length; i++)
            {
                ConveyorSnapshot snapshot = conveyors[i];
                if (snapshot != null && snapshot.conveyorId == conveyorId)
                {
                    return snapshot;
                }
            }

            return null;
        }

        private static bool IsEligible(ConveyorSnapshot snapshot)
        {
            return snapshot != null
                && StudentConstants.IsConveyorId(snapshot.conveyorId)
                && snapshot.queueLength > 0
                && snapshot.productionPeriod > 0f
                && !snapshot.isReserved;
        }

        private static bool CanUseOfficialNextProductionTimes(
            ConveyorSnapshot[] conveyors,
            WorkTask[] pendingTasks)
        {
            bool hasCandidate = false;
            for (int i = 0; i < pendingTasks.Length; i++)
            {
                WorkTask task = pendingTasks[i];
                if (!IsPendingAndUnassigned(task))
                {
                    continue;
                }

                ConveyorSnapshot snapshot = FindSnapshot(conveyors, task.conveyorId);
                if (!IsEligible(snapshot))
                {
                    continue;
                }

                hasCandidate = true;
                if (snapshot.nextProductionAt < 0f)
                {
                    return false;
                }
            }

            return hasCandidate;
        }

        private static bool IsFull(ConveyorSnapshot snapshot)
        {
            return snapshot.queueLength >= StudentConstants.ConveyorQueueCapacity;
        }

        private static bool HasPriorityQueue(ConveyorSnapshot snapshot)
        {
            return snapshot.queueLength >= 2;
        }

        private static bool IsPreferredConveyorForRobot(int robotId, int conveyorId)
        {
            if (robotId == StudentConstants.RobotAId)
            {
                return conveyorId >= 1 && conveyorId <= 5;
            }

            if (robotId == StudentConstants.RobotBId)
            {
                return conveyorId >= 6 && conveyorId <= 10;
            }

            return false;
        }

        private static PreferredAreaContext BuildPreferredAreaContext(
            ConveyorSnapshot[] conveyors,
            int robotId,
            WorkTask[] pendingTasks,
            bool useOfficialNextProductionTimes)
        {
            PreferredAreaContext context = default;
            context.bestPreferredOverflowDeadline = float.PositiveInfinity;

            for (int i = 0; i < pendingTasks.Length; i++)
            {
                WorkTask task = pendingTasks[i];
                if (!IsPendingAndUnassigned(task))
                {
                    continue;
                }

                ConveyorSnapshot snapshot = FindSnapshot(conveyors, task.conveyorId);
                if (!IsEligible(snapshot)
                    || !IsPreferredConveyorForRobot(robotId, snapshot.conveyorId))
                {
                    continue;
                }

                float overflowDeadline = CalculateEstimatedOverflowDeadline(
                    snapshot,
                    useOfficialNextProductionTimes);
                context.hasPreferredCandidate = true;
                context.bestPreferredOverflowDeadline = Mathf.Min(
                    context.bestPreferredOverflowDeadline,
                    overflowDeadline);
            }

            return context;
        }

        private TaskCandidate BuildCandidate(
            WorkTask task,
            ConveyorSnapshot snapshot,
            StudentRobotSnapshot robot,
            PreferredAreaContext preferredArea,
            bool useOfficialNextProductionTimes)
        {
            bool inPreferredArea = IsPreferredConveyorForRobot(
                robot.baseSnapshot.RobotId,
                snapshot.conveyorId);
            float overflowDeadline = CalculateEstimatedOverflowDeadline(
                snapshot,
                useOfficialNextProductionTimes);
            float queuePressureLead = CalculateQueuePressureLead(snapshot);
            float travelCost = CalculateTravelCost(snapshot.conveyorId, robot);
            float areaPenalty = CalculateAreaPenalty(
                inPreferredArea,
                preferredArea,
                overflowDeadline);
            float pressureAdjustedDeadline = Mathf.Max(
                0f,
                overflowDeadline - queuePressureLead);
            float dispatchCost = pressureAdjustedDeadline + travelCost + areaPenalty;

            return new TaskCandidate
            {
                task = task,
                snapshot = snapshot,
                inPreferredArea = inPreferredArea,
                hasPriorityQueue = HasPriorityQueue(snapshot),
                overflowDeadline = overflowDeadline,
                queuePressureLead = queuePressureLead,
                pressureAdjustedDeadline = pressureAdjustedDeadline,
                travelCost = travelCost,
                areaPenalty = areaPenalty,
                dispatchCost = dispatchCost
            };
        }

        private static float CalculateEstimatedOverflowDeadline(
            ConveyorSnapshot snapshot,
            bool useOfficialNextProductionTimes)
        {
            int safeQueueLength = Mathf.Clamp(
                snapshot.queueLength,
                0,
                StudentConstants.ConveyorQueueCapacity);
            int productionsUntilOverflow = Mathf.Max(
                1,
                StudentConstants.ConveyorQueueCapacity - safeQueueLength + 1);

            if (useOfficialNextProductionTimes && snapshot.nextProductionAt >= 0f)
            {
                return snapshot.nextProductionAt
                    + ((productionsUntilOverflow - 1) * snapshot.productionPeriod);
            }

            return productionsUntilOverflow * snapshot.productionPeriod;
        }

        private static float CalculateQueuePressureLead(ConveyorSnapshot snapshot)
        {
            int safeQueueLength = Mathf.Clamp(
                snapshot.queueLength,
                0,
                StudentConstants.ConveyorQueueCapacity);
            float lead = safeQueueLength * QueuePressureLeadPerItemSec;
            if (IsFull(snapshot))
            {
                lead += FullQueueExtraLeadSec;
            }

            return lead;
        }

        private static float CalculateAreaPenalty(
            bool inPreferredArea,
            PreferredAreaContext preferredArea,
            float overflowDeadline)
        {
            if (inPreferredArea
                || !preferredArea.hasPreferredCandidate
                || overflowDeadline + WorkStealDeadlineAdvantageSec
                    < preferredArea.bestPreferredOverflowDeadline)
            {
                return 0f;
            }

            return PreferredAreaPenaltySec;
        }

        private float CalculateTravelCost(int conveyorId, StudentRobotSnapshot robot)
        {
            if (operatingStations == null)
            {
                return 0f;
            }

            if (!operatingStations.TryGetStation(conveyorId, out OperatingStations.Station targetStation))
            {
                return 0f;
            }

            if (!TryGetRobotPosition(robot, out Vector3 robotPosition))
            {
                return 0f;
            }

            return Vector3.Distance(robotPosition, targetStation.BasePosition)
                * TravelCostPerMeterSec;
        }

        private bool TryGetRobotPosition(StudentRobotSnapshot robot, out Vector3 position)
        {
            if (robot != null
                && (StudentConstants.IsConveyorId(robot.currentStationId)
                    || StudentConstants.IsBoxStationId(robot.currentStationId))
                && operatingStations.TryGetStation(
                    robot.currentStationId,
                    out OperatingStations.Station currentStation))
            {
                position = currentStation.BasePosition;
                return true;
            }

            if (robot != null && robot.baseSnapshot.Position != Vector3.zero)
            {
                position = robot.baseSnapshot.Position;
                return true;
            }

            position = default;
            return false;
        }

        private static bool IsBetterCandidate(TaskCandidate candidate, TaskCandidate best)
        {
            if (!ApproximatelyCost(candidate.dispatchCost, best.dispatchCost))
            {
                return candidate.dispatchCost < best.dispatchCost;
            }

            if (!ApproximatelyCost(candidate.overflowDeadline, best.overflowDeadline))
            {
                return candidate.overflowDeadline < best.overflowDeadline;
            }

            if (candidate.hasPriorityQueue != best.hasPriorityQueue)
            {
                return candidate.hasPriorityQueue;
            }

            if (candidate.snapshot.queueLength != best.snapshot.queueLength)
            {
                return candidate.snapshot.queueLength > best.snapshot.queueLength;
            }

            if (candidate.inPreferredArea != best.inPreferredArea)
            {
                return candidate.inPreferredArea;
            }

            if (!Mathf.Approximately(
                candidate.snapshot.productionPeriod,
                best.snapshot.productionPeriod))
            {
                return candidate.snapshot.productionPeriod < best.snapshot.productionPeriod;
            }

            if (!Mathf.Approximately(candidate.travelCost, best.travelCost))
            {
                return candidate.travelCost < best.travelCost;
            }

            if (!Mathf.Approximately(
                candidate.snapshot.lastAssignedAt,
                best.snapshot.lastAssignedAt))
            {
                return candidate.snapshot.lastAssignedAt < best.snapshot.lastAssignedAt;
            }

            if (!Mathf.Approximately(candidate.task.createdAt, best.task.createdAt))
            {
                return candidate.task.createdAt < best.task.createdAt;
            }

            return candidate.task.conveyorId < best.task.conveyorId;
        }

        private static bool ApproximatelyCost(float left, float right)
        {
            if (float.IsInfinity(left) || float.IsInfinity(right))
            {
                return left.Equals(right);
            }

            return Mathf.Approximately(left, right);
        }

        private static string BuildDebugReason(
            TaskCandidate candidate,
            int robotId,
            bool useOfficialNextProductionTimes)
        {
            string deadlineSource = useOfficialNextProductionTimes
                ? "next-production"
                : "period-fallback";
            return string.Format(
                "policy=overflow-urgency, robot={0}, priorityQueue={1}, preferredArea={2}, queue={3}, overflowDeadline={4:0.##}, pressureLead={5:0.##}, adjustedDeadline={6:0.##}, travelCost={7:0.##}, areaPenalty={8:0.##}, dispatchCost={9:0.##}, source={10}, period={11:0.##}",
                robotId,
                candidate.hasPriorityQueue,
                candidate.inPreferredArea,
                candidate.snapshot.queueLength,
                candidate.overflowDeadline,
                candidate.queuePressureLead,
                candidate.pressureAdjustedDeadline,
                candidate.travelCost,
                candidate.areaPenalty,
                candidate.dispatchCost,
                deadlineSource,
                candidate.snapshot.productionPeriod);
        }

        private struct PreferredAreaContext
        {
            public bool hasPreferredCandidate;
            public float bestPreferredOverflowDeadline;
        }

        private struct TaskCandidate
        {
            public WorkTask task;
            public ConveyorSnapshot snapshot;
            public bool inPreferredArea;
            public bool hasPriorityQueue;
            public float overflowDeadline;
            public float queuePressureLead;
            public float pressureAdjustedDeadline;
            public float travelCost;
            public float areaPenalty;
            public float dispatchCost;
        }
    }
}
