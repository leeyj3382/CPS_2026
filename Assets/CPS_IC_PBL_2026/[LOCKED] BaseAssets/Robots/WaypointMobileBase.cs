using System.Collections;
using UnityEngine;

namespace CPS.Lab11.MobileManipulator
{
    [DisallowMultipleComponent]
    public class WaypointMobileBase : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 0.75f;
        [SerializeField] private float turnSpeedDegreesPerSecond = 180f;
        [SerializeField] private float stoppingDistance = 0.05f;
        [SerializeField] private bool alignToWaypointRotation = true;
        [SerializeField] private bool keepUpright = true;

        private Coroutine activeMove;

        public bool IsMoving => activeMove != null;

        public void MoveTo(Transform target)
        {
            StopMove();
            activeMove = StartCoroutine(MoveToRoutine(target));
        }

        public IEnumerator MoveToRoutine(Transform target)
        {
            if (target == null)
            {
                Debug.LogWarning("WaypointMobileBase requires a target waypoint.", this);
                activeMove = null;
                yield break;
            }

            while (Vector3.Distance(transform.position, target.position) > stoppingDistance)
            {
                Vector3 targetPosition = target.position;
                if (keepUpright)
                {
                    targetPosition.y = transform.position.y;
                }

                Vector3 direction = targetPosition - transform.position;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation,
                        targetRotation,
                        turnSpeedDegreesPerSecond * Time.deltaTime);
                }

                transform.position = Vector3.MoveTowards(
                    transform.position,
                    targetPosition,
                    moveSpeed * Time.deltaTime);

                yield return null;
            }

            if (alignToWaypointRotation)
            {
                while (Quaternion.Angle(transform.rotation, target.rotation) > 0.5f)
                {
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation,
                        target.rotation,
                        turnSpeedDegreesPerSecond * Time.deltaTime);
                    yield return null;
                }
            }

            activeMove = null;
            Debug.Log($"Mobile base arrived at {target.name}.", this);
        }

        public void StopMove()
        {
            if (activeMove == null)
            {
                return;
            }

            StopCoroutine(activeMove);
            activeMove = null;
        }
    }
}
