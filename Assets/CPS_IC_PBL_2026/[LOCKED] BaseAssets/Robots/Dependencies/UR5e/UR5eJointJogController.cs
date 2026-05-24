using UnityEngine;

namespace CPS.Lab10.UR5e
{
    [DisallowMultipleComponent]
    public class UR5eJointJogController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private UR5eJointController jointController;

        [Header("Jog Settings")]
        [SerializeField] private bool enableKeyboardJog = true;
        [SerializeField] private float jogSpeedDegreesPerSecond = 45f;

        [Header("Joint Keys: Negative / Positive")]
        [SerializeField] private KeyCode joint1Negative = KeyCode.Q;
        [SerializeField] private KeyCode joint1Positive = KeyCode.A;
        [SerializeField] private KeyCode joint2Negative = KeyCode.W;
        [SerializeField] private KeyCode joint2Positive = KeyCode.S;
        [SerializeField] private KeyCode joint3Negative = KeyCode.E;
        [SerializeField] private KeyCode joint3Positive = KeyCode.D;
        [SerializeField] private KeyCode joint4Negative = KeyCode.R;
        [SerializeField] private KeyCode joint4Positive = KeyCode.F;
        [SerializeField] private KeyCode joint5Negative = KeyCode.T;
        [SerializeField] private KeyCode joint5Positive = KeyCode.G;
        [SerializeField] private KeyCode joint6Negative = KeyCode.Y;
        [SerializeField] private KeyCode joint6Positive = KeyCode.H;

        private void Reset()
        {
            jointController = GetComponent<UR5eJointController>();
        }

        private void Update()
        {
            if (!enableKeyboardJog || jointController == null)
            {
                return;
            }

            float step = jogSpeedDegreesPerSecond * Time.deltaTime;

            JogJoint(0, joint1Negative, joint1Positive, step);
            JogJoint(1, joint2Negative, joint2Positive, step);
            JogJoint(2, joint3Negative, joint3Positive, step);
            JogJoint(3, joint4Negative, joint4Positive, step);
            JogJoint(4, joint5Negative, joint5Positive, step);
            JogJoint(5, joint6Negative, joint6Positive, step);
        }

        private void JogJoint(int jointIndex, KeyCode negativeKey, KeyCode positiveKey, float step)
        {
            float delta = 0f;

            if (Input.GetKey(negativeKey))
            {
                delta -= step;
            }

            if (Input.GetKey(positiveKey))
            {
                delta += step;
            }

            if (Mathf.Abs(delta) <= Mathf.Epsilon)
            {
                return;
            }

            float currentAngle = jointController.GetJointAngle(jointIndex);
            jointController.SetJointAngle(jointIndex, currentAngle + delta);
        }
    }
}
