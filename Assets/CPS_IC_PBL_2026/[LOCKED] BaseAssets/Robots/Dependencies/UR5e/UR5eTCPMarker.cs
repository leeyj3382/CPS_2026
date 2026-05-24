using UnityEngine;

namespace CPS.Lab10.UR5e
{
    [DisallowMultipleComponent]
    public class UR5eTCPMarker : MonoBehaviour
    {
        [Header("TCP Tracking")]
        [SerializeField] private Transform tcpTransform;
        [SerializeField] private bool followPosition = true;
        [SerializeField] private bool followRotation = true;

        [Header("Optional Distance Target")]
        [SerializeField] private Transform distanceTarget;
        [SerializeField] private TextMesh distanceText;
        [SerializeField] private bool logDistance;
        [SerializeField] private float logInterval = 0.5f;

        private float nextLogTime;

        public float DistanceToTarget
        {
            get
            {
                if (tcpTransform == null || distanceTarget == null)
                {
                    return 0f;
                }

                return Vector3.Distance(tcpTransform.position, distanceTarget.position);
            }
        }

        private void LateUpdate()
        {
            if (tcpTransform == null)
            {
                return;
            }

            if (followPosition)
            {
                transform.position = tcpTransform.position;
            }

            if (followRotation)
            {
                transform.rotation = tcpTransform.rotation;
            }

            UpdateDistanceText();

            if (logDistance && distanceTarget != null && Time.time >= nextLogTime)
            {
                Debug.Log("TCP distance to target: " + DistanceToTarget.ToString("F3") + " m", this);
                nextLogTime = Time.time + Mathf.Max(0.05f, logInterval);
            }
        }

        private void UpdateDistanceText()
        {
            if (distanceText == null)
            {
                return;
            }

            if (distanceTarget == null)
            {
                distanceText.text = string.Empty;
                return;
            }

            distanceText.text = DistanceToTarget.ToString("F3") + " m";
        }
    }
}
