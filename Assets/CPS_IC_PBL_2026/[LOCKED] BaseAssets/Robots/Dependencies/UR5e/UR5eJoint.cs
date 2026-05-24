using UnityEngine;

namespace CPS.Lab10.UR5e
{
    [DisallowMultipleComponent]
    public class UR5eJoint : MonoBehaviour
    {
        [Header("Joint Transform")]
        [SerializeField] private Transform jointTransform;
        [SerializeField] private Vector3 localAxis = Vector3.up;
        [SerializeField] private bool autoConfigureFromObjectName = true;

        [Header("Angle Limits")]
        [SerializeField] private float minAngle = -180f;
        [SerializeField] private float maxAngle = 180f;

        [Header("Runtime State")]
        [SerializeField] private float currentAngle;

        private Quaternion initialLocalRotation;
        private bool hasInitialRotation;

        public Transform JointTransform => jointTransform;
        public Vector3 LocalAxis => localAxis;
        public float MinAngle => minAngle;
        public float MaxAngle => maxAngle;
        public float CurrentAngle => currentAngle;

        private void Reset()
        {
            jointTransform = transform;
            if (ShouldAutoConfigureFromObjectName())
            {
                AutoConfigureFromObjectName();
            }
        }

        private void Awake()
        {
            if (ShouldAutoConfigureFromObjectName())
            {
                AutoConfigureFromObjectName();
            }

            CacheInitialLocalRotation();
            ApplyCurrentAngle();
        }

        public void CacheInitialLocalRotation()
        {
            if (ShouldAutoConfigureFromObjectName())
            {
                AutoConfigureFromObjectName();
            }

            if (jointTransform == null || jointTransform != transform)
            {
                jointTransform = transform;
            }

            initialLocalRotation = jointTransform.localRotation;
            hasInitialRotation = true;
        }

        public void SetAngle(float angle)
        {
            currentAngle = Mathf.Clamp(angle, minAngle, maxAngle);
            ApplyCurrentAngle();
        }

        public float ClampAngle(float angle)
        {
            return Mathf.Clamp(angle, minAngle, maxAngle);
        }

        public void ApplyCurrentAngle()
        {
            if (jointTransform == null)
            {
                jointTransform = transform;
            }

            if (!hasInitialRotation)
            {
                CacheInitialLocalRotation();
            }

            Vector3 axis = localAxis.sqrMagnitude > 0f ? localAxis.normalized : Vector3.up;
            jointTransform.localRotation = initialLocalRotation * Quaternion.AngleAxis(currentAngle, axis);
        }

        private bool ShouldAutoConfigureFromObjectName()
        {
            return autoConfigureFromObjectName
                || name.Contains("Joint1")
                || name.Contains("Joint2")
                || name.Contains("Joint3")
                || name.Contains("Joint4")
                || name.Contains("Joint5")
                || name.Contains("Joint6");
        }

        private void AutoConfigureFromObjectName()
        {
            jointTransform = transform;

            if (name.Contains("Joint1"))
            {
                localAxis = Vector3.up;
                minAngle = -180f;
                maxAngle = 180f;
            }
            else if (name.Contains("Joint2"))
            {
                localAxis = new Vector3(0f, 0f, -1f);
                minAngle = -135f;
                maxAngle = 135f;
            }
            else if (name.Contains("Joint3"))
            {
                localAxis = Vector3.forward;
                minAngle = -150f;
                maxAngle = 150f;
            }
            else if (name.Contains("Joint4"))
            {
                localAxis = new Vector3(0f, 0f, -1f);
                minAngle = -180f;
                maxAngle = 180f;
            }
            else if (name.Contains("Joint5"))
            {
                localAxis = Vector3.up;
                minAngle = -120f;
                maxAngle = 120f;
            }
            else if (name.Contains("Joint6"))
            {
                localAxis = new Vector3(0f, 0f, -1f);
                minAngle = -180f;
                maxAngle = 180f;
            }
        }
    }
}
