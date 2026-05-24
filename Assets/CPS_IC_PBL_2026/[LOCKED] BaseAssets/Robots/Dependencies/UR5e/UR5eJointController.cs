using UnityEngine;

namespace CPS.Lab10.UR5e
{
    [DisallowMultipleComponent]
    public class UR5eJointController : MonoBehaviour
    {
        [Header("UR5e Joint Chain")]
        [SerializeField] private UR5eJoint[] joints = new UR5eJoint[UR5eJointPose.JointCount];

        [Header("Startup")]
        [SerializeField] private bool applyInspectorAnglesOnStart = true;

        public int JointCount => joints != null ? joints.Length : 0;

        private void Reset()
        {
            joints = GetComponentsInChildren<UR5eJoint>();
        }

        private void Awake()
        {
            CacheInitialLocalRotations();

            if (applyInspectorAnglesOnStart)
            {
                ApplyCurrentAngles();
            }
        }

        public void CacheInitialLocalRotations()
        {
            if (joints == null)
            {
                return;
            }

            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] != null)
                {
                    joints[i].CacheInitialLocalRotation();
                }
            }
        }

        public UR5eJointPose GetCurrentPose()
        {
            UR5eJointPose pose = new UR5eJointPose();

            for (int i = 0; i < UR5eJointPose.JointCount; i++)
            {
                pose.SetAngle(i, GetJointAngle(i));
            }

            return pose;
        }

        public void SetPose(UR5eJointPose pose)
        {
            if (pose == null)
            {
                Debug.LogWarning("Cannot set UR5e pose because the target pose is null.", this);
                return;
            }

            for (int i = 0; i < UR5eJointPose.JointCount; i++)
            {
                SetJointAngle(i, pose.GetAngle(i));
            }
        }

        public void SetJointAngle(int jointIndex, float angle)
        {
            UR5eJoint joint = GetJoint(jointIndex);
            if (joint == null)
            {
                return;
            }

            joint.SetAngle(angle);
        }

        public float GetJointAngle(int jointIndex)
        {
            UR5eJoint joint = GetJoint(jointIndex);
            return joint != null ? joint.CurrentAngle : 0f;
        }

        public void ApplyCurrentAngles()
        {
            if (joints == null)
            {
                return;
            }

            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] != null)
                {
                    joints[i].ApplyCurrentAngle();
                }
            }
        }

        private UR5eJoint GetJoint(int jointIndex)
        {
            if (joints == null || jointIndex < 0 || jointIndex >= joints.Length)
            {
                Debug.LogWarning("UR5e joint index must match an assigned joint in the controller.", this);
                return null;
            }

            if (joints[jointIndex] == null)
            {
                Debug.LogWarning("UR5e joint is not assigned at index " + jointIndex + ".", this);
            }

            return joints[jointIndex];
        }
    }
}
