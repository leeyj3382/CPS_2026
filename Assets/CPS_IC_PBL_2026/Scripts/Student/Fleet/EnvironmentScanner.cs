using System;
using System.Collections.Generic;
using CPS.ICPBL.Common;

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

            for (int conveyorId = StudentConstants.MinConveyorId;
                conveyorId <= StudentConstants.MaxConveyorId;
                conveyorId++)
            {
                float lastAssignedAt = 0f;
                if (lastAssignedAtByConveyor != null)
                {
                    lastAssignedAtByConveyor.TryGetValue(conveyorId, out lastAssignedAt);
                }

                snapshots[conveyorId - StudentConstants.MinConveyorId] = new ConveyorSnapshot
                {
                    conveyorId = conveyorId,
                    queueLength = environmentInfo.GetQueueLength(conveyorId),
                    productionPeriod = StudentConstants.GetConveyorProductionPeriod(conveyorId),
                    nextProductionAt = environmentInfo.NextProductionAt(conveyorId),
                    lastAssignedAt = lastAssignedAt,
                    isReserved = reservedConveyorIds != null && reservedConveyorIds.Contains(conveyorId)
                };
            }

            return snapshots;
        }
    }
}
