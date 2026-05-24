using UnityEngine;

namespace CPS.Lab11.MobileManipulator
{
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class PickableObject : MonoBehaviour
    {
        [SerializeField] private Rigidbody targetRigidbody;
        [SerializeField] private bool restoreParentOnRelease;

        private Transform originalParent;
        private bool originalIsKinematic;
        private bool originalUseGravity;
        private bool originalDetectCollisions;
        private bool hasCachedRigidbodyState;

        public Rigidbody TargetRigidbody => targetRigidbody;
        public bool IsHeld { get; private set; }

        private void Reset()
        {
            targetRigidbody = GetComponent<Rigidbody>();
        }

        private void Awake()
        {
            if (targetRigidbody == null)
            {
                targetRigidbody = GetComponent<Rigidbody>();
            }

            originalParent = transform.parent;
            CacheRigidbodyState();
        }

        public void Attach(Transform attachPoint, bool snapToAttachPoint = true)
        {
            if (attachPoint == null)
            {
                Debug.LogWarning($"{nameof(PickableObject)} cannot attach because attachPoint is null.", this);
                return;
            }

            CacheRigidbodyState();
            SetHoldingPhysics();

            transform.SetParent(attachPoint, true);
            if (snapToAttachPoint)
            {
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }

            IsHeld = true;
        }

        public void Release()
        {
            Transform releaseParent = restoreParentOnRelease ? originalParent : null;
            transform.SetParent(releaseParent, true);
            RestoreRigidbodyState();
            IsHeld = false;
        }

        public void CacheRigidbodyState()
        {
            if (targetRigidbody == null)
            {
                return;
            }

            originalIsKinematic = targetRigidbody.isKinematic;
            originalUseGravity = targetRigidbody.useGravity;
            originalDetectCollisions = targetRigidbody.detectCollisions;
            hasCachedRigidbodyState = true;
        }

        public void SetHoldingPhysics()
        {
            if (targetRigidbody == null)
            {
                Debug.LogWarning($"{name} has no Rigidbody. Holding will use Transform parenting only.", this);
                return;
            }

            // kinematic 일 때 velocity·angularVelocity set 시 Unity 예외 → 토글 패턴.
            targetRigidbody.isKinematic = false;
            targetRigidbody.velocity = Vector3.zero;
            targetRigidbody.angularVelocity = Vector3.zero;
            targetRigidbody.useGravity = false;
            targetRigidbody.isKinematic = true;
        }

        public void RestoreRigidbodyState()
        {
            if (targetRigidbody == null || !hasCachedRigidbodyState)
            {
                return;
            }

            // constraints 해제 + 캐시 상태 복원. dynamic 으로 전환되어 자연 낙하.
            targetRigidbody.constraints = RigidbodyConstraints.None;
            targetRigidbody.isKinematic = originalIsKinematic;
            targetRigidbody.useGravity = originalUseGravity;
            targetRigidbody.detectCollisions = originalDetectCollisions;
        }
    }
}
