using System;
using System.Collections;
using UnityEngine;
using CPS.ICPBL.Common;
using CPS.ICPBL.Environment;
using CPS.Lab10.UR5e;
using CPS.Lab11.MobileManipulator;

namespace CPS.ICPBL.Robots
{
    /// <summary>
    /// 로봇 컨트롤러 — low-level movement API 만 제공.
    /// 변수로 받아 MoveBaseTo·MoveArmTo·GoToOperatingStation 호출.
    /// gripper · colorSensor · boxTrigger 등은 직접 SerializeField wire 해서 사용.
    /// </summary>
    [DisallowMultipleComponent]
    public class RobotController : MonoBehaviour, IRobotController
    {
        [Header("Identification")]
        [SerializeField] private int robotId = 0;
        [SerializeField] private Color identifierColor = Color.yellow;

        [Header("Sub-systems")]
        [SerializeField] private WaypointMobileBase mobileBase;
        [Tooltip("Down-facing IK 솔버. Awake 에서 자동 부착·wire.")]
        [SerializeField] private UR5eDownFacingIK ccdIk;
        [SerializeField] private UR5eJointController jointController;
        [SerializeField] private OperatingStations stations;

        // ===== 내부 tuning (private const) =====
        private const float baseMoveSpeedMultiplier = 10f;   // WaypointMobileBase prefab 기본 0.75 m/s → 7.5 m/s
        private const float baseTurnSpeedMultiplier = 5f;    // prefab 기본 180°/s → 900°/s
        private const float armDefaultDuration = 1.0f;

        // ===== 내부 상태 =====
        private bool sequenceBusy;
        private Coroutine activeSequence;
        private int currentStationId = -1;

        // ===== IRobotController =====
        public int RobotId => robotId;
        public Vector3 Position => transform.position;
        public bool IsBusy => sequenceBusy || (mobileBase != null && mobileBase.IsMoving);
        public Color IdentifierColor => identifierColor;

        public OperatingStations Stations => stations;   // 학생 코드가 station 정보 조회용

        private void Awake()
        {
            AutoWireIK();
            AdjustMobileBaseSpeed();
        }

        private void AutoWireIK()
        {
            if (jointController == null)
                jointController = GetComponentInChildren<UR5eJointController>(true);

            if (ccdIk == null)
            {
                ccdIk = GetComponentInChildren<UR5eDownFacingIK>(true);
                if (ccdIk == null)
                    ccdIk = gameObject.AddComponent<UR5eDownFacingIK>();
            }
        }

        /// <summary>WaypointMobileBase 의 prefab 기본값(0.75 m/s) 을 배수로 보정 — Lab 11 자산 수정 회피.</summary>
        private void AdjustMobileBaseSpeed()
        {
            if (mobileBase == null) return;
            var t = typeof(WaypointMobileBase);
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var moveField = t.GetField("moveSpeed", bf);
            if (moveField != null)
            {
                float cur = (float)moveField.GetValue(mobileBase);
                moveField.SetValue(mobileBase, cur * baseMoveSpeedMultiplier);
            }
            var turnField = t.GetField("turnSpeedDegreesPerSecond", bf);
            if (turnField != null)
            {
                float cur = (float)turnField.GetValue(mobileBase);
                turnField.SetValue(mobileBase, cur * baseTurnSpeedMultiplier);
            }
        }

        // ===== Movement API =====

        public void GoToOperatingStation(int stationId)
        {
            if (stations == null)
            {
                Debug.LogWarning($"[R#{robotId}] OperatingStations not assigned.");
                return;
            }
            if (!stations.TryGetStation(stationId, out OperatingStations.Station s))
            {
                Debug.LogWarning($"[R#{robotId}] Station id={stationId} not found.");
                return;
            }
            currentStationId = stationId;
            StartSequence(MoveBaseWithYawSequence(s.BasePosition, s.BaseYawDeg, null));
        }

        public void MoveBaseTo(Vector3 worldPos, Action onArrived = null)
        {
            if (mobileBase == null)
            {
                Debug.LogWarning($"[R#{robotId}] mobileBase not assigned.");
                onArrived?.Invoke();
                return;
            }
            StartSequence(MoveBaseSequence(worldPos, onArrived));
        }

        public void MoveArmTo(Vector3 worldPos, Quaternion worldRot, float duration = 1.0f, Action onArrived = null)
        {
            // UR5eDownFacingIK 만 사용 — down-facing 강제. worldRot 무시.
            if (ccdIk == null || jointController == null)
            {
                Debug.LogWarning($"[R#{robotId}] ccdIk/jointController not assigned.");
                onArrived?.Invoke();
                return;
            }
            StartCoroutine(MoveArmSmoothIKWithCallback(worldPos, duration, onArrived));
        }

        // ===== 내부 시퀀스 =====

        private void StartSequence(IEnumerator seq)
        {
            if (activeSequence != null) StopCoroutine(activeSequence);
            sequenceBusy = true;
            activeSequence = StartCoroutine(WrapSequence(seq));
        }

        private IEnumerator WrapSequence(IEnumerator seq)
        {
            yield return StartCoroutine(seq);
            sequenceBusy = false;
            activeSequence = null;
        }

        private IEnumerator MoveBaseWithYawSequence(Vector3 worldPos, float yawDeg, Action onArrived)
        {
            if (mobileBase == null) { onArrived?.Invoke(); yield break; }
            GameObject tmp = new GameObject($"_TempWaypoint_R{robotId}");
            tmp.transform.position = worldPos;
            tmp.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            mobileBase.MoveTo(tmp.transform);
            yield return new WaitUntil(() => !mobileBase.IsMoving);
            if (tmp != null) Destroy(tmp);
            onArrived?.Invoke();
        }

        private IEnumerator MoveBaseSequence(Vector3 worldPos, Action onArrived)
        {
            GameObject tmp = new GameObject($"_TempWaypoint_R{robotId}");
            tmp.transform.position = worldPos;
            tmp.transform.rotation = transform.rotation;
            mobileBase.MoveTo(tmp.transform);
            yield return new WaitUntil(() => !mobileBase.IsMoving);
            if (tmp != null) Destroy(tmp);
            onArrived?.Invoke();
        }

        /// <summary>Arm 을 target world position 으로 매끄럽게 보간 이동 (down-facing 유지).
        /// IK 는 한 번만 풀고 startPose↔endPose lerp.</summary>
        private IEnumerator MoveArmSmoothIK(Vector3 targetWorld, float duration)
        {
            if (ccdIk == null || jointController == null) yield break;

            UR5eJointPose startPose = jointController.GetCurrentPose().Copy();
            ccdIk.Solve(targetWorld, out var endPose);
            jointController.SetPose(startPose);

            float dur = Mathf.Max(0.001f, duration);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                if (t > 1f) t = 1f;
                jointController.SetPose(UR5eJointPose.Lerp(startPose, endPose, t));
                yield return null;
            }
            jointController.SetPose(endPose);
        }

        private IEnumerator MoveArmSmoothIKWithCallback(Vector3 worldPos, float duration, Action onArrived)
        {
            sequenceBusy = true;
            yield return StartCoroutine(MoveArmSmoothIK(worldPos, duration));
            sequenceBusy = false;
            onArrived?.Invoke();
        }
    }
}
