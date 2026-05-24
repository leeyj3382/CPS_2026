using System;
using UnityEngine;

namespace CPS.ICPBL.Environment
{
    /// <summary>
    /// 12개 운영 위치(컨베이어 10 + Normal/Abnormal Box 2)의 좌표·자세 ScriptableObject.
    /// 로봇 베이스가 정차할 BasePosition·BaseYawDeg, 팔이 작업할 ArmAnchorPoint 를 제공한다.
    ///
    /// 좌표는 자산 설계서 §9.2 preliminary 값. Unity Scene 실측 후 2-7 단계에서 v0.2 갱신.
    /// 새 .asset 생성 시 OnEnable 에서 자동 초기화. Inspector 에서 수동 조정 가능.
    ///
    /// 학생 사용: IRobotController.GoToOperatingStation(stationId) 가 내부적으로 본 ScriptableObject 조회.
    /// </summary>
    [CreateAssetMenu(fileName = "OperatingStations", menuName = "CPS/OperatingStations", order = 100)]
    public class OperatingStations : ScriptableObject
    {
        [Serializable]
        public struct Station
        {
            public int Id;                  // 1~10 (Conveyor), 100 (Normal Box), 101 (Abnormal Box)
            public string Name;
            public Vector3 BasePosition;    // 로봇 베이스 정차 위치 (월드 좌표, y=0 기본)
            public float BaseYawDeg;        // 베이스 회전 (Y축, 도)
            public Vector3 ArmAnchorPoint;  // 팔이 작업할 기준점 (월드 좌표)
        }

        [Tooltip("12개 운영 위치. 새 .asset 생성 시 자산 설계서 §9.2 preliminary 좌표로 자동 초기화.")]
        public Station[] stations;

        // Station ID 상수 (학생·베이스 코드 모두에서 사용)
        public const int NormalBoxId = 100;
        public const int AbnormalBoxId = 101;

        private void OnEnable()
        {
            if (stations == null || stations.Length == 0)
            {
                InitializePreliminary();
            }
        }

        /// <summary>
        /// id 에 해당하는 Station 반환. 못 찾으면 빈 Station(Id=0) + 경고 로그.
        /// </summary>
        public Station GetStation(int id)
        {
            if (TryGetStation(id, out Station s)) return s;
            Debug.LogWarning($"[OperatingStations] Station id={id} not found. Valid: 1~10, 100, 101.");
            return default;
        }

        public bool TryGetStation(int id, out Station station)
        {
            if (stations != null)
            {
                for (int i = 0; i < stations.Length; i++)
                {
                    if (stations[i].Id == id)
                    {
                        station = stations[i];
                        return true;
                    }
                }
            }
            station = default;
            return false;
        }

        public int StationCount => stations != null ? stations.Length : 0;

        /// <summary>
        /// 자산 설계서 §9.2 preliminary 좌표로 초기화 (12개).
        /// Inspector 우클릭 "Reset to Preliminary Defaults" 로 수동 호출 가능.
        /// </summary>
        [ContextMenu("Reset to Preliminary Defaults (자산 설계서 §9.2)")]
        public void InitializePreliminary()
        {
            stations = new Station[]
            {
                // ===== Robot prefab scale=2 가정. UR5e reach ≈ 1.7m. =====
                // BasePos ↔ ArmAnchor 거리 1.5m 안전 (수평 1.2m + y 0.9m → √(1.44+0.81) ≈ 1.5m)

                // ===== 사용자 Scene 측정 기반 (2026-05-18) =====
                // 컨베이어 point1 local=(-0.6, 1.5, -2). World 변환 후 ArmAnchor.
                // ArmMountPoint = BasePos + (0, ~1.0, 0). reach 한계 1.7m (Joint1↔wrist3) 안.

                // ----- Conveyors 1~5 (서쪽 라인, BasePos x=-8, BaseYaw=-90° = 270°) -----
                // ArmAnchor = ConveyorPos(-11.5,0,z) + R(-90)×(-0.6,1.5,-2) = (-9.5, 1.5, z-0.6)
                new Station { Id = 1,  Name = "Conveyor_1",  BasePosition = new Vector3(-8f, 0f, -7f), BaseYawDeg = -90f, ArmAnchorPoint = new Vector3(-9.5f, 1.5f, -7.6f) },
                new Station { Id = 2,  Name = "Conveyor_2",  BasePosition = new Vector3(-8f, 0f, -3f), BaseYawDeg = -90f, ArmAnchorPoint = new Vector3(-9.5f, 1.5f, -3.6f) },
                new Station { Id = 3,  Name = "Conveyor_3",  BasePosition = new Vector3(-8f, 0f,  1f), BaseYawDeg = -90f, ArmAnchorPoint = new Vector3(-9.5f, 1.5f,  0.4f) },
                new Station { Id = 4,  Name = "Conveyor_4",  BasePosition = new Vector3(-8f, 0f,  5f), BaseYawDeg = -90f, ArmAnchorPoint = new Vector3(-9.5f, 1.5f,  4.4f) },
                new Station { Id = 5,  Name = "Conveyor_5",  BasePosition = new Vector3(-8f, 0f,  9f), BaseYawDeg = -90f, ArmAnchorPoint = new Vector3(-9.5f, 1.5f,  8.4f) },

                // ----- Conveyors 6~10 (북쪽 라인, BasePos z=10.5, BaseYaw=0°) -----
                // ArmAnchor = ConveyorPos(x,0,14) + (-0.6, 1.5, -2) = (x-0.6, 1.5, 12)
                new Station { Id = 6,  Name = "Conveyor_6",  BasePosition = new Vector3(-6.5f, 0f, 10.5f), BaseYawDeg = 0f, ArmAnchorPoint = new Vector3(-7.1f, 1.5f, 12f) },
                new Station { Id = 7,  Name = "Conveyor_7",  BasePosition = new Vector3(-2.5f, 0f, 10.5f), BaseYawDeg = 0f, ArmAnchorPoint = new Vector3(-3.1f, 1.5f, 12f) },
                new Station { Id = 8,  Name = "Conveyor_8",  BasePosition = new Vector3( 1.5f, 0f, 10.5f), BaseYawDeg = 0f, ArmAnchorPoint = new Vector3( 0.9f, 1.5f, 12f) },
                new Station { Id = 9,  Name = "Conveyor_9",  BasePosition = new Vector3( 5.5f, 0f, 10.5f), BaseYawDeg = 0f, ArmAnchorPoint = new Vector3( 4.9f, 1.5f, 12f) },
                new Station { Id = 10, Name = "Conveyor_10", BasePosition = new Vector3( 9.5f, 0f, 10.5f), BaseYawDeg = 0f, ArmAnchorPoint = new Vector3( 8.9f, 1.5f, 12f) },

                // ----- Boxes (사용자 측정 BasePos 사용) -----
                // ⚠️ 거리 약 2.0m → reach 1.7m 초과 가능. 박스 BasePos 더 가까이 (z=-7 또는 x=9) 권장 시 재측정.
                // Normal Box 실제: (0, 0, -8). BasePos (0, 0, -6), Yaw 180°. ArmAnchor 박스 중심 위 0.5m.
                new Station { Id = NormalBoxId,   Name = "Normal_Box",   BasePosition = new Vector3(0f, 0f, -6f), BaseYawDeg = 180f, ArmAnchorPoint = new Vector3(0f, 0.5f, -8f) },
                // Abnormal Box 실제: (10.5, 0, 2.5). BasePos (8.5, 0, 2.5), Yaw 90°.
                new Station { Id = AbnormalBoxId, Name = "Abnormal_Box", BasePosition = new Vector3(8.5f, 0f, 2.5f), BaseYawDeg = 90f, ArmAnchorPoint = new Vector3(10.5f, 0.5f, 2.5f) },
            };

            Debug.Log($"[OperatingStations] Initialized {stations.Length} stations with preliminary coordinates (자산 설계서 §9.2). Unity 실측 후 보정 필요.");
        }
    }
}
