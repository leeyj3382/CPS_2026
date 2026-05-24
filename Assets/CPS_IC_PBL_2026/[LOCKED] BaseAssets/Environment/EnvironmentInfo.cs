using UnityEngine;
using CPS.ICPBL.Common;

namespace CPS.ICPBL.Environment
{
    /// <summary>
    /// IEnvironmentInfo 구현체. SpawnSystem + ProductSpawner[] + BoxTrigger 의 정보를 노출.
    /// 큐 길이·박스 점유·시간 정보 조회용. 양품/불량품 판단은 카메라 센서로만 가능.
    /// </summary>
    [DisallowMultipleComponent]
    public class EnvironmentInfo : MonoBehaviour, IEnvironmentInfo
    {
        [Header("Scene References")]
        [SerializeField] private SpawnSystem spawnSystem;
        [SerializeField] private BoxTrigger normalBox;
        [SerializeField] private BoxTrigger abnormalBox;

        [Header("Constants")]
        [Tooltip("마지막 spawn 까지의 시간 (초).")]
        [SerializeField] private float productionEndTime = 180f;

        private bool simStarted;
        private float simStartTime;

        private void Update()
        {
            // SpawnSystem 의 deferred init 시점을 sim 시작 시각으로 사용
            if (!simStarted && spawnSystem != null && spawnSystem.IsInitialized)
            {
                simStartTime = Time.time;
                simStarted = true;
            }
        }

        // ====== IEnvironmentInfo ======

        public int GetQueueLength(int conveyorId)
        {
            if (spawnSystem == null || spawnSystem.conveyors == null) return 0;
            if (conveyorId < 1 || conveyorId > spawnSystem.conveyors.Length) return 0;
            var conv = spawnSystem.conveyors[conveyorId - 1];
            return conv != null ? conv.QueueCount : 0;
        }

        public int GetBoxOccupancy(BoxType box)
        {
            BoxTrigger target = (box == BoxType.Normal) ? normalBox : abnormalBox;
            return target != null ? target.OccupiedSlotCount : 0;
        }

        public float CurrentTime => simStarted ? Time.time - simStartTime : 0f;

        public float ProductionEndTime => productionEndTime;

        /// <summary>
        /// 다음 생산 절대 시각 — 정확한 구현은 SpawnSystem 의 코루틴 진행 정보 노출 필요.
        /// 현 버전은 stub (-1 반환). 학생 코드가 polling 으로 GetQueueLength 변화를 추적하면 우회 가능.
        /// </summary>
        public float NextProductionAt(int conveyorId)
        {
            // TODO: SpawnSystem 코루틴이 conveyor 별 next spawn 절대 시각을 노출하도록 확장 시 갱신.
            return -1f;
        }
    }
}
