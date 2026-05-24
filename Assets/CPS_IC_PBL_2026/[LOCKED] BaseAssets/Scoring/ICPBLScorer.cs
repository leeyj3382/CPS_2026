using UnityEngine;
using CPS.ICPBL.Common;

namespace CPS.ICPBL.Scoring
{
    /// <summary>
    /// IC-PBL 2026 통합 채점기. 자산 설계서 §7.
    /// 시간·분류·미적재·idle·자원충돌·근접충돌 자동 측정 + 감점 적용.
    /// 학생은 본 컴포넌트를 직접 수정하면 큰 감점 (사후 검증).
    ///
    /// 점수 식 (§7.1):
    ///   score = 15 - timePenalty - wrongCount - unplacedCount - conflictPenalty - idlePenalty
    /// </summary>
    [DisallowMultipleComponent]
    public class ICPBLScorer : MonoBehaviour
    {
        // ====== 평가 수치 (Inspector 노출, Planning v0.2 에서 최종 확정) ======

        [Header("평가 수치 (Planning v0.2 에서 확정)")]
        [Tooltip("시간 목표 (초). 초과 시 5초당 -1점.")]
        public float T_TARGET = 220f;
        [Tooltip("both_idle 누적이 이 값을 초과하면 5초당 -1점. (2026: idle 채점 제외 → 표시만 유지)")]
        public float BOTH_IDLE_THRESHOLD = 50f;
        [Tooltip("Idle 을 점수 계산에 포함할지. 2026 결정: 제외.")]
        public bool includeIdleInScore = false;
        [Tooltip("자원충돌 + 근접충돌 감점 상한 (점).")]
        public int CONFLICT_PENALTY_CAP = 5;
        [Tooltip("시뮬레이션 자동 종료까지의 최대 시간 (초). 이후 FinalizeAndReport 자동 호출.")]
        public float maxSimulationDuration = 300f;

        // ====== Scene 참조 (Inspector slot) ======

        [Header("Scene References")]
        [SerializeField] private SpawnSystem spawnSystem;
        [SerializeField] private BoxTrigger normalBox;
        [SerializeField] private BoxTrigger abnormalBox;
        [SerializeField] private ConflictDetector conflictDetector;

        [Tooltip("RobotA / RobotB GameObject. IRobotController 구현 컴포넌트 (RobotController.cs) 가 부착되어 있어야 함.")]
        [SerializeField] private GameObject robotAGo;
        [SerializeField] private GameObject robotBGo;

        // ====== 내부 상태 ======

        private IRobotController robotA;
        private IRobotController robotB;
        private bool tracking;
        private bool finalized;
        private float simStartTime;

        private float idleA;
        private float idleB;
        private float bothIdle;
        private float completionTime;
        private int prevTotalPlaced;

        // ====== 공개 프로퍼티 (자산 설계서 §7.2 + ScoreboardScript HUD 용) ======

        public int CurrentScore => Mathf.Max(0, 15 - TimePenalty - WrongCount - UnplacedCount - ConflictPenalty - (includeIdleInScore ? IdlePenalty : 0));
        public float CurrentTime => tracking ? Time.time - simStartTime : 0f;
        public float CompletionTime => completionTime;

        public int CorrectCount => (normalBox != null ? normalBox.correctCount : 0)
                                 + (abnormalBox != null ? abnormalBox.correctCount : 0);
        public int WrongCount   => (normalBox != null ? normalBox.wrongCount : 0)
                                 + (abnormalBox != null ? abnormalBox.wrongCount : 0);
        public int UnplacedCount
        {
            get
            {
                int total = spawnSystem != null ? spawnSystem.totalProductNumber : 0;
                int placed = CorrectCount + WrongCount;
                return Mathf.Max(0, total - placed);
            }
        }

        public float IdleATime => idleA;
        public float IdleBTime => idleB;
        public float BothIdleTime => bothIdle;

        public int NearCollisions => conflictDetector != null ? conflictDetector.NearCollisionCount : 0;
        public int ResourceConflicts => conflictDetector != null ? conflictDetector.ResourceConflictCount : 0;
        public int TotalConflicts => NearCollisions + ResourceConflicts;

        public int TimePenalty
        {
            get
            {
                if (completionTime <= 0f) return 0;
                float over = completionTime - T_TARGET;
                if (over <= 0f) return 0;
                return Mathf.CeilToInt(over / 5f);
            }
        }

        public int IdlePenalty
        {
            get
            {
                float over = bothIdle - BOTH_IDLE_THRESHOLD;
                if (over <= 0f) return 0;
                return Mathf.CeilToInt(over / 5f);
            }
        }

        public int ConflictPenalty => Mathf.Min(CONFLICT_PENALTY_CAP, TotalConflicts);

        public bool IsTracking => tracking;
        public bool IsFinalized => finalized;

        // ====== Unity lifecycle ======

        private void Awake()
        {
            if (robotAGo != null) robotA = robotAGo.GetComponent<IRobotController>();
            if (robotBGo != null) robotB = robotBGo.GetComponent<IRobotController>();
            if (robotA == null)
                Debug.LogWarning("[ICPBLScorer] robotAGo 에 IRobotController 컴포넌트 없음. Idle 측정 불가 (RobotController.cs 작성·부착 필요).");
            if (robotB == null)
                Debug.LogWarning("[ICPBLScorer] robotBGo 에 IRobotController 컴포넌트 없음. Idle 측정 불가.");
        }

        private void Update()
        {
            // SpawnSystem 이 deferred init 되면 자동 tracking 시작
            if (!tracking && !finalized && spawnSystem != null && spawnSystem.IsInitialized)
            {
                BeginTracking();
            }

            if (!tracking || finalized) return;

            float dt = Time.deltaTime;

            // ---- Idle time 누적 ----
            bool aBusy = robotA != null && robotA.IsBusy;
            bool bBusy = robotB != null && robotB.IsBusy;
            if (!aBusy) idleA += dt;
            if (!bBusy) idleB += dt;
            if (!aBusy && !bBusy) bothIdle += dt;

            // ---- completion_time: 마지막 박스 진입 시각 ----
            int totalPlaced = CorrectCount + WrongCount;
            if (totalPlaced > prevTotalPlaced)
            {
                completionTime = CurrentTime;
            }
            prevTotalPlaced = totalPlaced;

            // ---- 자동 종료 조건 ----
            bool allPlaced = spawnSystem != null && totalPlaced >= spawnSystem.totalProductNumber;
            bool timedOut = CurrentTime >= maxSimulationDuration;
            if (allPlaced || timedOut)
            {
                ScoreReport r = FinalizeAndReport();
                Debug.Log($"[ICPBLScorer] AUTO FINALIZE ({(allPlaced ? "all placed" : "timeout")}): " + r.ToString());
            }
        }

        // ====== Public API (§7.2) ======

        public void BeginTracking()
        {
            tracking = true;
            finalized = false;
            simStartTime = Time.time;
            idleA = idleB = bothIdle = 0f;
            completionTime = 0f;
            prevTotalPlaced = 0;
            if (conflictDetector != null) conflictDetector.BeginTracking();
            Debug.Log($"[ICPBLScorer] Tracking begin (seed={spawnSystem?.seed}, T_TARGET={T_TARGET}s)");
        }

        public ScoreReport FinalizeAndReport()
        {
            if (finalized) return BuildReport();

            finalized = true;
            tracking = false;
            if (conflictDetector != null) conflictDetector.StopTracking();

            // completion_time 미설정이면 (한 개도 못 들어감) 현재 시각
            if (completionTime <= 0f) completionTime = CurrentTime;

            ScoreReport r = BuildReport();
            Debug.Log("[ICPBLScorer] " + r.ToString());
            return r;
        }

        private ScoreReport BuildReport()
        {
            return new ScoreReport
            {
                FinalScore = CurrentScore,
                CompletionTime = completionTime,
                CorrectCount = CorrectCount,
                WrongCount = WrongCount,
                UnplacedCount = UnplacedCount,
                IdleA = idleA,
                IdleB = idleB,
                BothIdle = bothIdle,
                ResourceConflicts = ResourceConflicts,
                NearCollisions = NearCollisions,
                TimePenalty = TimePenalty,
                IdlePenalty = IdlePenalty,
                ConflictPenalty = ConflictPenalty
            };
        }
    }
}
