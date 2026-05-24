using CPS.ICPBL.Common;
using CPS.ICPBL.Environment;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    public static class StudentConstants
    {
        public const int RobotAId = 0;
        public const int RobotBId = 1;
        public const int UnassignedRobotId = -1;

        public const int NoTaskId = -1;
        public const int NoStationId = -1;

        public const int MinConveyorId = 1;
        public const int MaxConveyorId = 10;
        public const int ConveyorQueueCapacity = 3;

        public const int NormalBoxStationId = OperatingStations.NormalBoxId;
        public const int AbnormalBoxStationId = OperatingStations.AbnormalBoxId;
        public const int CentralZoneResourceId = 0;

        public const float DefaultMissionTimeoutSec = 60f;
        public const float DefaultMoveTimeoutSec = 15f;
        public const float DefaultLockTimeoutSec = 5f;
        public const float DefaultGripReadyTimeoutSec = 3f;
        public const float DefaultArmMoveDurationSec = 1f;

        public const float ColorReliableThreshold = 0.15f;
        public const float ColorAmbiguousDistanceDelta = 0.05f;

        public static readonly Color NormalBlue = new Color(0.192f, 0.250f, 0.868f, 1f);
        public static readonly Color AbnormalRed = new Color(0.877f, 0.211f, 0.211f, 1f);
        public static readonly Color DefaultSensorColor = Color.white;

        // Index 0 is unused so conveyor id 1~10 can be used directly.
        public static readonly float[] ConveyorProductionPeriods =
        {
            0f,
            15f,
            18f,
            20f,
            20f,
            30f,
            36f,
            45f,
            45f,
            60f,
            90f
        };

        public static bool IsConveyorId(int stationId)
        {
            return stationId >= MinConveyorId && stationId <= MaxConveyorId;
        }

        public static bool IsRobotId(int robotId)
        {
            return robotId == RobotAId || robotId == RobotBId;
        }

        public static bool IsBoxStationId(int stationId)
        {
            return stationId == NormalBoxStationId || stationId == AbnormalBoxStationId;
        }

        public static bool TryGetBoxType(ClassificationResult result, out BoxType boxType)
        {
            if (result == ClassificationResult.Normal)
            {
                boxType = BoxType.Normal;
                return true;
            }

            if (result == ClassificationResult.Abnormal)
            {
                boxType = BoxType.Abnormal;
                return true;
            }

            boxType = default;
            return false;
        }

        public static int GetBoxStationId(BoxType boxType)
        {
            if (boxType == BoxType.Normal)
            {
                return NormalBoxStationId;
            }

            if (boxType == BoxType.Abnormal)
            {
                return AbnormalBoxStationId;
            }

            return NoStationId;
        }

        public static LockResourceType GetBoxLockType(BoxType boxType)
        {
            if (boxType == BoxType.Normal)
            {
                return LockResourceType.NormalBox;
            }

            if (boxType == BoxType.Abnormal)
            {
                return LockResourceType.AbnormalBox;
            }

            return LockResourceType.CentralZone;
        }

        public static float GetConveyorProductionPeriod(int conveyorId)
        {
            if (!IsConveyorId(conveyorId))
            {
                return 0f;
            }

            return ConveyorProductionPeriods[conveyorId];
        }
    }
}
