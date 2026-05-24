using System.Collections;
using CPS.Lab10.UR5e;
using UnityEngine;

namespace CPS.Lab11.MobileManipulator
{
    [DisallowMultipleComponent]
    public class UR5ePosePresetPlayer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private UR5eJointController jointController;

        [Header("Reference Zero Pose")]
        [SerializeField] private bool useStoredReferencePose = true;
        [SerializeField] private UR5eJoint[] referenceJoints = new UR5eJoint[UR5eJointPose.JointCount];
        [SerializeField] private Quaternion[] referenceLocalRotations = new Quaternion[UR5eJointPose.JointCount];

        [Header("Pose Presets")]
        [SerializeField] private UR5eJointPose homePose = new UR5eJointPose(0f, 0f, 0f, 0f, 0f, 0f);
        [SerializeField] private UR5eJointPose approachPose = new UR5eJointPose(0f, 20f, -45f, -30f, 75f, 0f);
        [SerializeField] private UR5eJointPose pickPose = new UR5eJointPose(0f, 35f, -65f, -45f, 80f, 0f);
        [SerializeField] private UR5eJointPose carryPose = new UR5eJointPose(0f, -10f, -55f, -45f, 75f, 0f);
        [SerializeField] private UR5eJointPose placePose = new UR5eJointPose(90f, 25f, -60f, -40f, 75f, 0f);

        [Header("Motion")]
        [SerializeField] private float defaultDuration = 1.5f;
        [SerializeField] private bool useSmoothStepInterpolation = true;

        private Coroutine activeMove;

        public UR5eJointController JointController => jointController;
        public bool IsMoving => activeMove != null;

        private void Reset()
        {
            jointController = GetComponentInChildren<UR5eJointController>();
            CaptureCurrentReferencePose();
        }

        private void Awake()
        {
            EnsureReferencePose();
        }

        public void PlayHome() => PlayPose(homePose);
        public void PlayApproach() => PlayPose(approachPose);
        public void PlayPick() => PlayPose(pickPose);
        public void PlayCarry() => PlayPose(carryPose);
        public void PlayPlace() => PlayPose(placePose);

        public void ApplyPoseInstant(MobileManipulatorPose poseName)
        {
            ApplyPoseInstant(GetPose(poseName));
        }

        public void ApplyPoseInstant(UR5eJointPose pose)
        {
            StopMove();

            if (jointController == null)
            {
                Debug.LogWarning("UR5ePosePresetPlayer requires a UR5eJointController.", this);
                return;
            }

            if (pose == null)
            {
                Debug.LogWarning("UR5ePosePresetPlayer target pose is null.", this);
                return;
            }

            ApplyPoseToJoints(pose);
        }

        public bool SaveCurrentAsPose(MobileManipulatorPose poseName)
        {
            if (jointController == null)
            {
                Debug.LogWarning("UR5ePosePresetPlayer requires a UR5eJointController.", this);
                return false;
            }

            SetPose(poseName, jointController.GetCurrentPose());
            return true;
        }

        public void PlayPose(UR5eJointPose pose)
        {
            StopMove();
            activeMove = StartCoroutine(PlayPoseRoutine(pose, defaultDuration));
        }

        public IEnumerator PlayPoseRoutine(UR5eJointPose pose)
        {
            yield return PlayPoseRoutine(pose, defaultDuration);
        }

        public IEnumerator PlayPoseRoutine(UR5eJointPose pose, float duration)
        {
            if (jointController == null)
            {
                Debug.LogWarning("UR5ePosePresetPlayer requires a UR5eJointController.", this);
                activeMove = null;
                yield break;
            }

            if (pose == null)
            {
                Debug.LogWarning("UR5ePosePresetPlayer target pose is null.", this);
                activeMove = null;
                yield break;
            }

            UR5eJointPose startPose = jointController.GetCurrentPose();
            float safeDuration = Mathf.Max(0.0001f, duration);
            float elapsed = 0f;

            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / safeDuration);
                if (useSmoothStepInterpolation)
                {
                    t = t * t * (3f - 2f * t);
                }

                ApplyPoseToJoints(UR5eJointPose.Lerp(startPose, pose, t));
                yield return null;
            }

            ApplyPoseToJoints(pose);
            activeMove = null;
        }

        public IEnumerator PlayNamedPoseRoutine(MobileManipulatorPose poseName)
        {
            yield return PlayPoseRoutine(GetPose(poseName), defaultDuration);
        }

        public UR5eJointPose GetPose(MobileManipulatorPose poseName)
        {
            switch (poseName)
            {
                case MobileManipulatorPose.Home:
                    return homePose;
                case MobileManipulatorPose.Approach:
                    return approachPose;
                case MobileManipulatorPose.Pick:
                    return pickPose;
                case MobileManipulatorPose.Carry:
                    return carryPose;
                case MobileManipulatorPose.Place:
                    return placePose;
                default:
                    return homePose;
            }
        }

        public void SetPose(MobileManipulatorPose poseName, UR5eJointPose pose)
        {
            if (pose == null)
            {
                Debug.LogWarning("UR5ePosePresetPlayer target pose is null.", this);
                return;
            }

            UR5eJointPose poseCopy = pose.Copy();
            switch (poseName)
            {
                case MobileManipulatorPose.Home:
                    homePose = poseCopy;
                    break;
                case MobileManipulatorPose.Approach:
                    approachPose = poseCopy;
                    break;
                case MobileManipulatorPose.Pick:
                    pickPose = poseCopy;
                    break;
                case MobileManipulatorPose.Carry:
                    carryPose = poseCopy;
                    break;
                case MobileManipulatorPose.Place:
                    placePose = poseCopy;
                    break;
            }
        }

        public void SetReferencePose(UR5eJoint[] joints, Quaternion[] localRotations)
        {
            if (joints == null || localRotations == null)
            {
                return;
            }

            int count = Mathf.Min(UR5eJointPose.JointCount, Mathf.Min(joints.Length, localRotations.Length));
            if (referenceJoints == null || referenceJoints.Length != UR5eJointPose.JointCount)
            {
                referenceJoints = new UR5eJoint[UR5eJointPose.JointCount];
            }

            if (referenceLocalRotations == null || referenceLocalRotations.Length != UR5eJointPose.JointCount)
            {
                referenceLocalRotations = new Quaternion[UR5eJointPose.JointCount];
            }

            for (int i = 0; i < count; i++)
            {
                referenceJoints[i] = joints[i];
                referenceLocalRotations[i] = localRotations[i];
            }
        }

        public void CaptureCurrentReferencePose()
        {
            if (jointController == null)
            {
                return;
            }

            UR5eJoint[] joints = jointController.GetComponentsInChildren<UR5eJoint>(true);
            Quaternion[] rotations = new Quaternion[Mathf.Min(UR5eJointPose.JointCount, joints.Length)];
            for (int i = 0; i < rotations.Length; i++)
            {
                Transform jointTransform = joints[i].JointTransform != null ? joints[i].JointTransform : joints[i].transform;
                rotations[i] = jointTransform.localRotation;
            }

            SetReferencePose(joints, rotations);
        }

        private void ApplyPoseToJoints(UR5eJointPose pose)
        {
            if (!useStoredReferencePose)
            {
                jointController.SetPose(pose);
                return;
            }

            EnsureReferencePose();

            for (int i = 0; i < UR5eJointPose.JointCount; i++)
            {
                UR5eJoint joint = referenceJoints[i];
                if (joint == null)
                {
                    jointController.SetJointAngle(i, pose.GetAngle(i));
                    continue;
                }

                Transform jointTransform = joint.JointTransform != null ? joint.JointTransform : joint.transform;
                jointTransform.localRotation = referenceLocalRotations[i];
                joint.CacheInitialLocalRotation();
                joint.SetAngle(pose.GetAngle(i));
            }
        }

        private void EnsureReferencePose()
        {
            if (!HasValidReferencePose())
            {
                CaptureCurrentReferencePose();
            }
        }

        private bool HasValidReferencePose()
        {
            if (jointController == null
                || referenceJoints == null
                || referenceLocalRotations == null
                || referenceJoints.Length != UR5eJointPose.JointCount
                || referenceLocalRotations.Length != UR5eJointPose.JointCount)
            {
                return false;
            }

            for (int i = 0; i < UR5eJointPose.JointCount; i++)
            {
                if (referenceJoints[i] == null)
                {
                    return false;
                }
            }

            return true;
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

    public enum MobileManipulatorPose
    {
        Home,
        Approach,
        Pick,
        Carry,
        Place
    }
}
