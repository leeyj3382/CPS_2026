using System;
using System.Collections.Generic;
using CPS.ICPBL.Common;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    /// <summary>
    /// Converts official environment observations into scheduling snapshots.
    /// Reservation and assignment history remain owned by FleetManager.
    /// </summary>
    public sealed class EnvironmentScanner
    {
        private readonly IEnvironmentInfo environmentInfo;

        public EnvironmentScanner(IEnvironmentInfo environmentInfo)
        {
            this.environmentInfo = environmentInfo
                ?? throw new ArgumentNullException(nameof(environmentInfo));
        }

        /// <summary>
        /// Captures all conveyor states. TaskAllocator decides which snapshots are eligible tasks.
        /// </summary>
        public ConveyorSnapshot[] Scan(
            ISet<int> reservedConveyorIds = null,
            IReadOnlyDictionary<int, float> lastAssignedAtByConveyor = null)
        {
            int snapshotCount = StudentConstants.MaxConveyorId - StudentConstants.MinConveyorId + 1;
            var snapshots = new ConveyorSnapshot[snapshotCount];
            float currentTime = environmentInfo.CurrentTime;
            float productionEndTime = environmentInfo.ProductionEndTime;

            for (int conveyorId = StudentConstants.MinConveyorId;
                conveyorId <= StudentConstants.MaxConveyorId;
                conveyorId++)
            {
                float lastAssignedAt = 0f;
                if (lastAssignedAtByConveyor != null)
                {
                    lastAssignedAtByConveyor.TryGetValue(conveyorId, out lastAssignedAt);
                }

                float productionPeriod = StudentConstants.GetConveyorProductionPeriod(conveyorId);
                float nextProductionAt = environmentInfo.NextProductionAt(conveyorId);
                if (nextProductionAt < 0f)
                {
                    nextProductionAt = EstimateNextProductionAt(
                        currentTime,
                        productionPeriod,
                        productionEndTime);
                }

                snapshots[conveyorId - StudentConstants.MinConveyorId] = new ConveyorSnapshot
                {
                    conveyorId = conveyorId,
                    queueLength = environmentInfo.GetQueueLength(conveyorId),
                    productionPeriod = productionPeriod,
                    nextProductionAt = nextProductionAt,
                    lastAssignedAt = lastAssignedAt,
                    isReserved = reservedConveyorIds != null && reservedConveyorIds.Contains(conveyorId)
                };
            }

            return snapshots;
        }

        private static float EstimateNextProductionAt(
            float currentTime,
            float productionPeriod,
            float productionEndTime)
        {
            if (productionPeriod <= 0f)
            {
                return float.PositiveInfinity;
            }

            float safeCurrentTime = System.Math.Max(0f, currentTime);
            float nextCycle = Mathf.Floor(safeCurrentTime / productionPeriod) + 1f;
            float nextProductionAt = nextCycle * productionPeriod;
            return nextProductionAt <= productionEndTime
                ? nextProductionAt
                : float.PositiveInfinity;
        }
    }
}
