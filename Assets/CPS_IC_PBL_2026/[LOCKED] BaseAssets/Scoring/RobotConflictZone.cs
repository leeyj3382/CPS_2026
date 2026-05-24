using UnityEngine;
using CPS.ICPBL.Robots;

namespace CPS.ICPBL.Scoring
{
    /// <summary>
    /// Robot 베이스 주변 BoxCollider trigger — 다른 robot 이 진입하면 ConflictDetector 에 카운트 요청.
    /// 거리 임계값 대신 명시적 collider 영역으로 conflict 판정 → robot 베이스 크기·형상 변화에 robust.
    ///
    /// 양쪽 robot 에 모두 부착해도 OK — ConflictDetector 가 pair 중복 제거.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    [RequireComponent(typeof(Rigidbody))]
    public class RobotConflictZone : MonoBehaviour
    {
        [SerializeField] private RobotController owner;
        [SerializeField] private ConflictDetector conflictDetector;

        private void Reset()
        {
            AutoWire();
            EnsureTriggerCollider();
            EnsureKinematicRigidbody();
        }

        private void Awake()
        {
            AutoWire();
            EnsureTriggerCollider();
            EnsureKinematicRigidbody();
        }

        private void AutoWire()
        {
            if (owner == null) owner = GetComponentInParent<RobotController>();
            if (conflictDetector == null) conflictDetector = FindObjectOfType<ConflictDetector>();
        }

        private void EnsureTriggerCollider()
        {
            var bc = GetComponent<BoxCollider>();
            if (bc != null) bc.isTrigger = true;
        }

        /// <summary>Trigger 이벤트는 두 collider 중 최소 한 쪽에 Rigidbody 가 있어야 발생.
        /// Robot 베이스는 transform 직접 조작이라 Rigidbody 없음 → ConflictZone 에 kinematic Rigidbody 추가.</summary>
        private void EnsureKinematicRigidbody()
        {
            var rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            var otherRobot = other.GetComponentInParent<RobotController>();
            string ownerStr = owner != null ? $"R#{owner.RobotId}" : "NULL";
            string otherStr = otherRobot != null ? $"R#{otherRobot.RobotId}" : "no-robot";
            Debug.Log($"[ConflictZone {ownerStr}] OnTriggerEnter from {other.name} ({otherStr})");
            if (otherRobot == null || owner == null) return;
            if (otherRobot == owner) return;

            if (conflictDetector != null)
                conflictDetector.RegisterRobotZoneCollision(owner.RobotId, otherRobot.RobotId);
        }

        private void OnTriggerExit(Collider other)
        {
            var otherRobot = other.GetComponentInParent<RobotController>();
            if (otherRobot == null || owner == null) return;
            if (otherRobot == owner) return;

            if (conflictDetector != null)
                conflictDetector.EndRobotZoneCollision(owner.RobotId, otherRobot.RobotId);
        }
    }
}
