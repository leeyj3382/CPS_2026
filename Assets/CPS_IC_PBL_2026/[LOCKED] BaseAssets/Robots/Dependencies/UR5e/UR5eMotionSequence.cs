using UnityEngine;

namespace CPS.Lab10.UR5e
{
    [DisallowMultipleComponent]
    public class UR5eMotionSequence : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private UR5eMoveJController moveJController;

        [Header("Teaching Poses")]
        [SerializeField] private UR5eJointPose home = new UR5eJointPose(0f, 0f, 0f, 0f, 0f, 0f);
        [SerializeField] private UR5eJointPose ready = new UR5eJointPose(0f, 0f, 90f, 0f, 90f, 0f);
        [SerializeField] private UR5eJointPose pick = new UR5eJointPose(180f, 52.5f, -67f, -25.5f, -88f, 0f);
        [SerializeField] private UR5eJointPose place = new UR5eJointPose(90f, 18f, -81.5f, -100f, -80f, 0f);

        [Header("Timing")]
        [SerializeField] private float moveDuration = 2f;
        [SerializeField] private float pauseBetweenMoves = 0.25f;

        [Header("Keyboard Demo")]
        [SerializeField] private bool enableKeyboardShortcuts = true;
        [SerializeField] private KeyCode runSequenceKey = KeyCode.Space;
        [SerializeField] private KeyCode homeKey = KeyCode.Alpha1;
        [SerializeField] private KeyCode readyKey = KeyCode.Alpha2;
        [SerializeField] private KeyCode pickKey = KeyCode.Alpha3;
        [SerializeField] private KeyCode placeKey = KeyCode.Alpha4;

        private Coroutine activeSequence;

        private void Reset()
        {
            moveJController = GetComponent<UR5eMoveJController>();
        }

        private void Update()
        {
            if (!enableKeyboardShortcuts)
            {
                return;
            }

            if (Input.GetKeyDown(runSequenceKey))
            {
                RunDemoSequence();
            }
            else if (Input.GetKeyDown(homeKey))
            {
                MoveToHome();
            }
            else if (Input.GetKeyDown(readyKey))
            {
                MoveToReady();
            }
            else if (Input.GetKeyDown(pickKey))
            {
                MoveToPick();
            }
            else if (Input.GetKeyDown(placeKey))
            {
                MoveToPlace();
            }
        }

        public void RunDemoSequence()
        {
            if (activeSequence != null)
            {
                StopCoroutine(activeSequence);
                activeSequence = null;
            }

            if (moveJController != null)
            {
                moveJController.StopMove();
            }

            activeSequence = StartCoroutine(SequenceRoutine());
        }

        public void MoveToHome()
        {
            MoveToPose(home);
        }

        public void MoveToReady()
        {
            MoveToPose(ready);
        }

        public void MoveToPick()
        {
            MoveToPose(pick);
        }

        public void MoveToPlace()
        {
            MoveToPose(place);
        }

        private void MoveToPose(UR5eJointPose pose)
        {
            if (activeSequence != null)
            {
                StopCoroutine(activeSequence);
                activeSequence = null;
            }

            if (moveJController == null)
            {
                Debug.LogWarning("Motion sequence requires an assigned UR5eMoveJController.", this);
                return;
            }

            moveJController.MoveJToPose(pose, moveDuration);
        }

        private System.Collections.IEnumerator SequenceRoutine()
        {
            if (moveJController == null)
            {
                Debug.LogWarning("Motion sequence requires an assigned UR5eMoveJController.", this);
                activeSequence = null;
                yield break;
            }

            yield return moveJController.MoveJ(home, moveDuration);
            yield return new WaitForSeconds(pauseBetweenMoves);
            yield return moveJController.MoveJ(ready, moveDuration);
            yield return new WaitForSeconds(pauseBetweenMoves);
            yield return moveJController.MoveJ(pick, moveDuration);
            yield return new WaitForSeconds(pauseBetweenMoves);
            yield return moveJController.MoveJ(place, moveDuration);
            yield return new WaitForSeconds(pauseBetweenMoves);
            yield return moveJController.MoveJ(home, moveDuration);

            activeSequence = null;
        }
    }
}
