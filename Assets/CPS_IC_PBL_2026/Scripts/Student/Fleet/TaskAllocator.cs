using System;
using CPS.ICPBL.Environment;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    /// <summary>
    /// Selects one Fleet-owned pending task by its estimated queue-saturation deadline.
    /// This selector is non-preemptive: reserved or running tasks are never reconsidered.
    /// Task creation and reservation updates remain owned by FleetManager.
    /// </summary>
    public sealed class TaskAllocator : ITaskAllocator
    {
        private const float DistanceCostScale = 1f;

        private readonly OperatingStations operatingStations;
        private readonly bool enableDebugLogging;

        public TaskAllocator(
            OperatingStations operatingStations = null,
            bool enableDebugLogging = false)
        {
            this.operatingStations = operatingStations;
            this.enableDebugLogging = enableDebugLogging;
        }

        public WorkTask SelectBestTask(
            ConveyorSnapshot[] conveyors,
            StudentRobotSnapshot robot,
            WorkTask[] pendingTasks)
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

            WorkTask bestTask = null;
            ConveyorSnapshot bestSnapshot = null;
            float bestDeadline = float.PositiveInfinity;
            float bestEffectiveDeadline = float.PositiveInfinity;
            float bestDistanceCost = float.PositiveInfinity;
            bool bestInPreferredRange = false;
            bool bestHasPriorityQueue = false;
            bool useOfficialNextProductionTimes = CanUseOfficialNextProductionTimes(conveyors, pendingTasks);

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

                float estimatedDeadline = CalculateEstimatedSaturationDeadline(
                    snapshot,
                    useOfficialNextProductionTimes);
                float effectiveDeadline = CalculateEffectiveDeadline(snapshot, estimatedDeadline);
                float distanceCost = CalculateDistanceCost(snapshot.conveyorId, robot);
                bool inPreferredRange = IsPreferredConveyorForRobot(
                    robot.baseSnapshot.RobotId,
                    snapshot.conveyorId);
                bool hasPriorityQueue = HasPriorityQueue(snapshot);
                task.priorityScore = -effectiveDeadline;
                task.debugReason = BuildDebugReason(
                    snapshot,
                    robot.baseSnapshot.RobotId,
                    inPreferredRange,
                    hasPriorityQueue,
                    estimatedDeadline,
                    effectiveDeadline,
                    distanceCost,
                    useOfficialNextProductionTimes);
                LogCandidate(
                    snapshot,
                    estimatedDeadline,
                    effectiveDeadline);

                if (bestTask == null || IsBetterCandidate(
                    task,
                    snapshot,
                    estimatedDeadline,
                    effectiveDeadline,
                    distanceCost,
                    inPreferredRange,
                    hasPriorityQueue,
                    bestTask,
                    bestSnapshot,
                    bestDeadline,
                    bestEffectiveDeadline,
                    bestDistanceCost,
                    bestInPreferredRange,
                    bestHasPriorityQueue))
                {
                    bestTask = task;
                    bestSnapshot = snapshot;
                    bestDeadline = estimatedDeadline;
                    bestEffectiveDeadline = effectiveDeadline;
                    bestDistanceCost = distanceCost;
                    bestInPreferredRange = inPreferredRange;
                    bestHasPriorityQueue = hasPriorityQueue;
                }
            }

            LogSelection(bestTask);
            return bestTask;
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
            // Absolute official deadlines cannot be compared with relative fallback horizons.
            bool hasNonFullCandidate = false;
            for (int i = 0; i < pendingTasks.Length; i++)
            {
                WorkTask task = pendingTasks[i];
                if (!IsPendingAndUnassigned(task))
                {
                    continue;
                }

                ConveyorSnapshot snapshot = FindSnapshot(conveyors, task.conveyorId);
                if (!IsEligible(snapshot) || IsFull(snapshot))
                {
                    continue;
                }

                hasNonFullCandidate = true;
                if (snapshot.nextProductionAt < 0f)
                {
                    return false;
                }
            }

            return hasNonFullCandidate;
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

        private static float CalculateEstimatedSaturationDeadline(
            ConveyorSnapshot snapshot,
            bool useOfficialNextProductionTimes)
        {
            if (IsFull(snapshot))
            {
                return 0f;
            }

            if (snapshot.queueLength > 0 && float.IsInfinity(snapshot.nextProductionAt))
            {
                return 0f;
            }

            int slotsUntilFull = StudentConstants.ConveyorQueueCapacity - snapshot.queueLength;
            if (useOfficialNextProductionTimes)
            {
                return snapshot.nextProductionAt
                    + ((slotsUntilFull - 1) * snapshot.productionPeriod);
            }

            return slotsUntilFull * snapshot.productionPeriod;
        }

        private static float CalculateEffectiveDeadline(
            ConveyorSnapshot snapshot,
            float estimatedDeadline)
        {
            if (IsFull(snapshot) || float.IsInfinity(estimatedDeadline))
            {
                return estimatedDeadline;
            }

            float leadTime = 0f;
            if (IsCentralDropRiskConveyor(snapshot.conveyorId))
            {
                leadTime += snapshot.productionPeriod;
            }

            if (snapshot.queueLength >= 2)
            {
                leadTime += snapshot.productionPeriod * 0.5f;
            }

            return Mathf.Max(0f, estimatedDeadline - leadTime);
        }

        private static bool IsCentralDropRiskConveyor(int conveyorId)
        {
            return conveyorId >= 3 && conveyorId <= 5;
        }

        private float CalculateDistanceCost(int conveyorId, StudentRobotSnapshot robot)
        {
            if (operatingStations == null)
            {
                return 0f;
            }

            if (!operatingStations.TryGetStation(conveyorId, out OperatingStations.Station station))
            {
                return 0f;
            }

            return Vector3.Distance(robot.baseSnapshot.Position, station.BasePosition) * DistanceCostScale;
        }

        private static bool IsBetterCandidate(
            WorkTask candidateTask,
            ConveyorSnapshot candidateSnapshot,
            float candidateDeadline,
            float candidateEffectiveDeadline,
            float candidateDistanceCost,
            bool candidateInPreferredRange,
            bool candidateHasPriorityQueue,
            WorkTask bestTask,
            ConveyorSnapshot bestSnapshot,
            float bestDeadline,
            float bestEffectiveDeadline,
            float bestDistanceCost,
            bool bestInPreferredRange,
            bool bestHasPriorityQueue)
        {
            bool candidateIsFull = IsFull(candidateSnapshot);
            bool bestIsFull = IsFull(bestSnapshot);
            if (candidateIsFull != bestIsFull)
            {
                return candidateIsFull;
            }

            if (candidateIsFull && bestIsFull)
            {
                if (!ApproximatelyDeadline(
                    candidateSnapshot.nextProductionAt,
                    bestSnapshot.nextProductionAt))
                {
                    return candidateSnapshot.nextProductionAt < bestSnapshot.nextProductionAt;
                }

                if (!Mathf.Approximately(
                    candidateSnapshot.productionPeriod,
                    bestSnapshot.productionPeriod))
                {
                    return candidateSnapshot.productionPeriod < bestSnapshot.productionPeriod;
                }
            }

            if (!ApproximatelyDeadline(candidateEffectiveDeadline, bestEffectiveDeadline))
            {
                return candidateEffectiveDeadline < bestEffectiveDeadline;
            }

            if (!ApproximatelyDeadline(candidateDeadline, bestDeadline))
            {
                return candidateDeadline < bestDeadline;
            }

            if (candidateHasPriorityQueue != bestHasPriorityQueue)
            {
                return candidateHasPriorityQueue;
            }

            if (candidateSnapshot.queueLength != bestSnapshot.queueLength)
            {
                return candidateSnapshot.queueLength > bestSnapshot.queueLength;
            }

            if (candidateInPreferredRange != bestInPreferredRange)
            {
                return candidateInPreferredRange;
            }

            if (!Mathf.Approximately(candidateSnapshot.productionPeriod, bestSnapshot.productionPeriod))
            {
                return candidateSnapshot.productionPeriod < bestSnapshot.productionPeriod;
            }

            if (!Mathf.Approximately(candidateDistanceCost, bestDistanceCost))
            {
                return candidateDistanceCost < bestDistanceCost;
            }

            if (!Mathf.Approximately(candidateSnapshot.lastAssignedAt, bestSnapshot.lastAssignedAt))
            {
                return candidateSnapshot.lastAssignedAt < bestSnapshot.lastAssignedAt;
            }

            if (!Mathf.Approximately(candidateTask.createdAt, bestTask.createdAt))
            {
                return candidateTask.createdAt < bestTask.createdAt;
            }

            return candidateTask.conveyorId < bestTask.conveyorId;
        }

        private static bool ApproximatelyDeadline(float left, float right)
        {
            if (float.IsInfinity(left) || float.IsInfinity(right))
            {
                return left.Equals(right);
            }

            return Mathf.Approximately(left, right);
        }

        private static string BuildDebugReason(
            ConveyorSnapshot snapshot,
            int robotId,
            bool inPreferredRange,
            bool hasPriorityQueue,
            float estimatedDeadline,
            float effectiveDeadline,
            float distanceCost,
            bool useOfficialNextProductionTimes)
        {
            string deadlineSource = GetDeadlineSource(snapshot, useOfficialNextProductionTimes);
            return string.Format(
                "policy=earliest-saturation-first, robot={0}, priorityQueue={1}, preferredRange={2}, queue={3}, saturationDeadline={4:0.##}, effectiveDeadline={5:0.##}, source={6}, period={7:0.##}, distanceTieCost={8:0.##}",
                robotId,
                hasPriorityQueue,
                inPreferredRange,
                snapshot.queueLength,
                estimatedDeadline,
                effectiveDeadline,
                deadlineSource,
                snapshot.productionPeriod,
                distanceCost);
        }

        private static string GetDeadlineSource(
            ConveyorSnapshot snapshot,
            bool useOfficialNextProductionTimes)
        {
            if (IsFull(snapshot))
            {
                return "queue-full";
            }

            if (snapshot.queueLength > 0 && float.IsInfinity(snapshot.nextProductionAt))
            {
                return "post-production-drain";
            }

            return useOfficialNextProductionTimes ? "next-production" : "period-fallback";
        }

        private void LogCandidate(
            ConveyorSnapshot snapshot,
            float estimatedDeadline,
            float effectiveDeadline)
        {
            if (!enableDebugLogging)
            {
                return;
            }

            Debug.LogFormat(
                "[Debug][TaskCandidate] conveyor={0} queue={1} reserved={2} next={3} estimatedDeadline={4} effectiveDeadline={5}",
                snapshot.conveyorId,
                snapshot.queueLength,
                snapshot.isReserved,
                snapshot.nextProductionAt,
                estimatedDeadline,
                effectiveDeadline);
        }

        private void LogSelection(WorkTask bestTask)
        {
            if (!enableDebugLogging)
            {
                return;
            }

            Debug.Log(bestTask == null
                ? "[Debug][TaskAllocator] No task selected"
                : string.Format(
                    "[Debug][TaskAllocator] Selected conveyor={0}, priority={1}, reason={2}",
                    bestTask.conveyorId,
                    bestTask.priorityScore,
                    bestTask.debugReason));
        }
    }
}
