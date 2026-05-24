using UnityEngine;

namespace CPS.Lab10.UR5e
{
    [System.Serializable]
    public class UR5eJointPose
    {
        public const int JointCount = 6;

        [Header("Joint Angles")]
        [SerializeField] private float baseJoint;
        [SerializeField] private float shoulder;
        [SerializeField] private float elbow;
        [SerializeField] private float wrist1;
        [SerializeField] private float wrist2;
        [SerializeField] private float wrist3;

        public float BaseJoint
        {
            get => baseJoint;
            set => baseJoint = value;
        }

        public float Shoulder
        {
            get => shoulder;
            set => shoulder = value;
        }

        public float Elbow
        {
            get => elbow;
            set => elbow = value;
        }

        public float Wrist1
        {
            get => wrist1;
            set => wrist1 = value;
        }

        public float Wrist2
        {
            get => wrist2;
            set => wrist2 = value;
        }

        public float Wrist3
        {
            get => wrist3;
            set => wrist3 = value;
        }

        public UR5eJointPose()
        {
        }

        public UR5eJointPose(float baseJoint, float shoulder, float elbow, float wrist1, float wrist2, float wrist3)
        {
            this.baseJoint = baseJoint;
            this.shoulder = shoulder;
            this.elbow = elbow;
            this.wrist1 = wrist1;
            this.wrist2 = wrist2;
            this.wrist3 = wrist3;
        }

        public float GetAngle(int jointIndex)
        {
            switch (jointIndex)
            {
                case 0:
                    return baseJoint;
                case 1:
                    return shoulder;
                case 2:
                    return elbow;
                case 3:
                    return wrist1;
                case 4:
                    return wrist2;
                case 5:
                    return wrist3;
                default:
                    Debug.LogWarning("UR5eJointPose index must be between 0 and 5.");
                    return 0f;
            }
        }

        public void SetAngle(int jointIndex, float angle)
        {
            switch (jointIndex)
            {
                case 0:
                    baseJoint = angle;
                    break;
                case 1:
                    shoulder = angle;
                    break;
                case 2:
                    elbow = angle;
                    break;
                case 3:
                    wrist1 = angle;
                    break;
                case 4:
                    wrist2 = angle;
                    break;
                case 5:
                    wrist3 = angle;
                    break;
                default:
                    Debug.LogWarning("UR5eJointPose index must be between 0 and 5.");
                    break;
            }
        }

        public UR5eJointPose Copy()
        {
            return new UR5eJointPose(baseJoint, shoulder, elbow, wrist1, wrist2, wrist3);
        }

        public static UR5eJointPose Lerp(UR5eJointPose from, UR5eJointPose to, float t)
        {
            t = Mathf.Clamp01(t);

            return new UR5eJointPose(
                Mathf.Lerp(from.baseJoint, to.baseJoint, t),
                Mathf.Lerp(from.shoulder, to.shoulder, t),
                Mathf.Lerp(from.elbow, to.elbow, t),
                Mathf.Lerp(from.wrist1, to.wrist1, t),
                Mathf.Lerp(from.wrist2, to.wrist2, t),
                Mathf.Lerp(from.wrist3, to.wrist3, t));
        }
    }
}
