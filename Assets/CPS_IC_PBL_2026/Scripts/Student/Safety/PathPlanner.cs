using UnityEngine;

namespace CPS.ICPBL.Student
{
    [DisallowMultipleComponent]
    public sealed class PathPlanner : MonoBehaviour, IPathPlanner
    {
        [Header("Robot Home Conveyor Ranges")]
        [SerializeField] private int robotAMinConveyor = 1;
        [SerializeField] private int robotAMaxConveyor = 5;
        [SerializeField] private int robotBMinConveyor = 6;
        [SerializeField] private int robotBMaxConveyor = 10;

        [Header("Central Zone Policy")]
        [SerializeField] private bool requireCentralZoneForCrossSideMove = true;
        [SerializeField] private bool requireCentralZoneForBoxAccess = true;

        public bool RequiresCentralZone(int robotId, int fromStationId, int toStationId)
        {
            if (fromStationId == toStationId)
            {
                return false;
            }

            if (!IsKnownStation(fromStationId) || !IsKnownStation(toStationId))
            {
                return false;
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
    }
}
