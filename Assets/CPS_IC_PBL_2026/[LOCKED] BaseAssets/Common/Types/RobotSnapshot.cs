using UnityEngine;

namespace CPS.ICPBL.Common
{
    /// <summary>한 로봇의 순간 상태 스냅샷. 상대 로봇 상태 조회용.</summary>
    public struct RobotSnapshot
    {
        public int RobotId;
        public Vector3 Position;
        public bool IsBusy;
        public bool IsHolding;
        public int? TargetConveyorId;   // 이동 중인 타깃 (null = idle)
    }
}
