using System;
using System.Collections.Generic;
using UnityEngine;

namespace CPS.ICPBL.Scoring
{
    /// <summary>
    /// 두 로봇 간 충돌 감지 유틸. ICPBLScorer 내부 사용 + 학생 코드 외부 호출 가능.
    ///
    /// 측정 항목 (자산 설계서 §7.1):
    ///  • near_collision: 두 로봇 거리 &lt; nearCollisionDistance (기본 1.5m), edge trigger
    ///  • resource_conflict: 외부에서 RegisterResourceConflict() 호출 시 카운트 (자원 lock 구현에서)
    /// </summary>
    [DisallowMultipleComponent]
    public class ConflictDetector : MonoBehaviour
    {
        [Header("Robot references")]
        [Tooltip("로봇 A GameObject. Transform.position 으로 거리 측정.")]
        [SerializeField] private GameObject robotA;
        [Tooltip("로봇 B GameObject.")]
        [SerializeField] private GameObject robotB;

        [Header("Detection Mode")]
        [Tooltip("거리 기반 near collision 활성. false 면 RobotConflictZone (BoxCollider trigger) 만 사용.")]
        [SerializeField] private bool enableDistanceCheck = false;
        [Tooltip("거리 기반 enable 시 두 로봇이 이 거리 미만으로 진입하면 +1 (edge trigger).")]
        [SerializeField] private float nearCollisionDistance = 1.5f;

        // 누적 카운트 (읽기 전용)
        public int NearCollisionCount { get; private set; }
        public int ResourceConflictCount { get; private set; }

        // 이벤트 (ICPBLScorer 가 구독)
        public event Action<float> OnNearCollisionDetected;       // arg: 충돌 시점 거리
        public event Action OnResourceConflictRegistered;

        public float NearCollisionDistance => nearCollisionDistance;
        public bool IsActive => robotA != null && robotB != null;

        private bool wasInsideNearZone;
        private bool tracking;

        [Header("Debug")]
        [Tooltip("매 N 초마다 두 robot 거리 로그. 0 이면 비활성.")]
        [SerializeField] private float debugLogInterval = 0f;
        private float debugLogTimer;

        private void Awake()
        {
            // Inspector slot 비어있으면 Scene 의 RobotController 자동 wire
            if (robotA == null || robotB == null)
            {
                var found = FindObjectsOfType<CPS.ICPBL.Robots.RobotController>();
                foreach (var r in found)
                {
                    if (r.RobotId == 0 && robotA == null) robotA = r.gameObject;
                    else if (r.RobotId == 1 && robotB == null) robotB = r.gameObject;
                }
                if (robotA == null && found.Length > 0) robotA = found[0].gameObject;
                if (robotB == null && found.Length > 1) robotB = found[1].gameObject;
            }

            Debug.Log($"[ConflictDetector] Awake — robotA={(robotA!=null?robotA.name:"NULL")} " +
                      $"robotB={(robotB!=null?robotB.name:"NULL")} threshold={nearCollisionDistance}m");
        }

        /// <summary>SpawnSystem.IsInitialized 이후 외부에서 호출하여 측정 시작.</summary>
        public void BeginTracking()
        {
            ResetCounts();
            tracking = true;
            Debug.Log($"[ConflictDetector] BeginTracking — IsActive={IsActive}");
        }

        public void StopTracking() => tracking = false;

        private void Update()
        {
            if (!tracking || !IsActive) return;
            if (!enableDistanceCheck) return;

            float dist = Vector3.Distance(robotA.transform.position, robotB.transform.position);
            bool isInside = dist < nearCollisionDistance;

            if (debugLogInterval > 0f)
            {
                debugLogTimer += Time.deltaTime;
                if (debugLogTimer >= debugLogInterval)
                {
                    debugLogTimer = 0f;
                    Debug.Log($"[ConflictDetector] dist={dist:F2}m (threshold={nearCollisionDistance:F2}m, inside={isInside})");
                }
            }

            if (isInside && !wasInsideNearZone)
            {
                NearCollisionCount++;
                OnNearCollisionDetected?.Invoke(dist);
                Debug.Log($"[ConflictDetector] Near collision (distance) #{NearCollisionCount} (dist={dist:F3}m)");
            }
            wasInsideNearZone = isInside;
        }

        // ===== Trigger 기반 (RobotConflictZone) =====
        private readonly HashSet<(int, int)> ongoingZonePairs = new HashSet<(int, int)>();

        /// <summary>RobotConflictZone 의 OnTriggerEnter 가 호출. 같은 pair 가 이미 overlap 중이면 중복 카운트 안 함.</summary>
        public void RegisterRobotZoneCollision(int robotIdA, int robotIdB)
        {
            Debug.Log($"[ConflictDetector] Zone collision attempt: robot{robotIdA} ↔ robot{robotIdB} (tracking={tracking})");
            if (!tracking) return;
            var pair = robotIdA < robotIdB ? (robotIdA, robotIdB) : (robotIdB, robotIdA);
            if (ongoingZonePairs.Contains(pair)) return;
            ongoingZonePairs.Add(pair);

            NearCollisionCount++;
            OnNearCollisionDetected?.Invoke(0f);
            Debug.Log($"[ConflictDetector] Robot zone collision (trigger) #{NearCollisionCount}: robot{pair.Item1} ↔ robot{pair.Item2}");
        }

        /// <summary>RobotConflictZone 의 OnTriggerExit 가 호출.</summary>
        public void EndRobotZoneCollision(int robotIdA, int robotIdB)
        {
            var pair = robotIdA < robotIdB ? (robotIdA, robotIdB) : (robotIdB, robotIdA);
            ongoingZonePairs.Remove(pair);
        }

        /// <summary>
        /// 자원 락 contention 등록. 자원 lock 구현에서 호출하면 카운트에 반영됨.
        /// </summary>
        public void RegisterResourceConflict(int requesterRobotId, int holderRobotId, string resourceLabel)
        {
            ResourceConflictCount++;
            OnResourceConflictRegistered?.Invoke();
            Debug.Log($"[ConflictDetector] Resource conflict #{ResourceConflictCount}: robot{requesterRobotId} contested {resourceLabel} held by robot{holderRobotId}");
        }

        public void ResetCounts()
        {
            NearCollisionCount = 0;
            ResourceConflictCount = 0;
            wasInsideNearZone = false;
            ongoingZonePairs.Clear();
        }
    }
}
