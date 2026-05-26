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

        public TaskAllocator(OperatingStations operatingStations = null)
        {
            this.operatingStations = operatingStations;
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
            float bestDistanceCost = float.PositiveInfinity;
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
                float distanceCost = CalculateDistanceCost(snapshot.conveyorId, robot);
                task.priorityScore = -estimatedDeadline;
                task.debugReason = BuildDebugReason(
                    snapshot,
                    estimatedDeadline,
                    distanceCost,
                    useOfficialNextProductionTimes);

                if (bestTask == null || IsBetterCandidate(
                    task,
                    snapshot,
                    estimatedDeadline,
                    distanceCost,
                    bestTask,
                    bestSnapshot,
                    bestDeadline,
                    bestDistanceCost))
                {
                    bestTask = task;
                    bestSnapshot = snapshot;
                    bestDeadline = estimatedDeadline;
                    bestDistanceCost = distanceCost;
                }
            }

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

        private static float CalculateEstimatedSaturationDeadline(
            ConveyorSnapshot snapshot,
            bool useOfficialNextProductionTimes)
        {
            if (IsFull(snapshot))
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
            float candidateDistanceCost,
            WorkTask bestTask,
            ConveyorSnapshot bestSnapshot,
            float bestDeadline,
            float bestDistanceCost)
        {
            bool candidateIsFull = IsFull(candidateSnapshot);
            bool bestIsFull = IsFull(bestSnapshot);
            if (candidateIsFull != bestIsFull)
            {
                return candidateIsFull;
            }

            if (!Mathf.Approximately(candidateDeadline, bestDeadline))
            {
                return candidateDeadline < bestDeadline;
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

        private static string BuildDebugReason(
            ConveyorSnapshot snapshot,
            float estimatedDeadline,
            float distanceCost,
            bool useOfficialNextProductionTimes)
        {
            string deadlineSource = IsFull(snapshot)
                ? "queue-full"
                : useOfficialNextProductionTimes ? "next-production" : "period-fallback";
            return string.Format(
                "policy=non-preemptive-edf, saturationDeadline={0:0.##}, source={1}, queue={2}, period={3:0.##}, distanceTieCost={4:0.##}",
                estimatedDeadline,
                deadlineSource,
                snapshot.queueLength,
                snapshot.productionPeriod,
                distanceCost);
        }
    }
}
