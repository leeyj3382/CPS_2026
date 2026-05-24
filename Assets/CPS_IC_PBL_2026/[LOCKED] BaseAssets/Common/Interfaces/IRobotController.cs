using System;
using UnityEngine;

namespace CPS.ICPBL.Common
{
    /// <summary>
    /// 로봇의 low-level 동작 API. 변수로 받아 호출만 하면 됨.
    /// Pick/Place/Inspect 같은 고수준 시퀀스는 본 메서드들을 조합해 직접 작성.
    /// 로봇의 sub-system(gripper · colorSensor · boxes 등)은 SerializeField 로 wire 해서 사용.
    /// </summary>
    public interface IRobotController
    {
        int RobotId { get; }
        Vector3 Position { get; }
        bool IsBusy { get; }

        // === Movement API (low-level) ===
        /// <summary>OperatingStations 의 BasePosition·BaseYawDeg 로 베이스 이동.</summary>
        void GoToOperatingStation(int stationId);

        /// <summary>World 좌표로 베이스 이동 (yaw 변경 없음).</summary>
        void MoveBaseTo(Vector3 worldPos, Action onArrived = null);

        /// <summary>World 좌표로 arm TCP 이동 (UR5eDownFacingIK 사용, down-facing 자동 유지).
        /// worldRot 은 무시됨 (down-facing 강제).</summary>
        void MoveArmTo(Vector3 worldPos, Quaternion worldRot, float duration = 1.0f, Action onArrived = null);
    }
}
