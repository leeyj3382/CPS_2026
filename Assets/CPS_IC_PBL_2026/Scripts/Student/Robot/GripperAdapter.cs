using System.Collections;
using CPS.Lab11.MobileManipulator;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    public sealed class GripperAdapter
    {
        private readonly SuctionGripper gripper;

        public GripperAdapter(SuctionGripper gripper)
        {
            this.gripper = gripper;
        }

        public bool IsConfigured
        {
            get { return gripper != null; }
        }

        public bool IsHolding
        {
            get { return gripper != null && gripper.IsHolding; }
        }

        public bool IsGraspReady
        {
            get { return gripper != null && gripper.IsGraspReady; }
        }

        public string LastFailureReason { get; private set; }

        private bool smoothAlignmentActive;
        private float smoothAlignmentStartedAt;
        private float smoothAlignmentDuration;
        private Quaternion smoothAlignmentStartRotation = Quaternion.identity;
        private PickableObject alignedObject;
        private Vector3 attachPointLocalInHeldObject;
        private bool hasAttachPointLocalInHeldObject;

        public IEnumerator WaitUntilGraspReady(float timeoutSec)
        {
            LastFailureReason = string.Empty;

            if (gripper == null)
            {
                LastFailureReason = "SuctionGripper reference is missing.";
                yield break;
            }

            float deadline = Time.time + Mathf.Max(0f, timeoutSec);
            while (Time.time <= deadline)
            {
                if (gripper.CurrentCandidate == null)
                {
                    LastFailureReason = "no candidate in DetectionTrigger or ContactProbe";
                }
                else if (gripper.CanGrip(out string reason))
                {
                    LastFailureReason = string.Empty;
                    yield break;
                }
                else
                {
                    LastFailureReason = reason;
                }

                yield return null;
            }

            if (string.IsNullOrEmpty(LastFailureReason))
            {
                LastFailureReason = "grasp ready timeout";
            }
        }

        public bool CanGrip(out string reason)
        {
            if (gripper == null)
            {
                reason = "SuctionGripper reference is missing.";
                LastFailureReason = reason;
                return false;
            }

            if (gripper.CurrentCandidate == null)
            {
                reason = "no candidate in DetectionTrigger or ContactProbe";
                LastFailureReason = reason;
                return false;
            }

            bool canGrip = gripper.CanGrip(out reason);
            LastFailureReason = canGrip ? string.Empty : reason;
            return canGrip;
        }

        public bool TryGrip(out string reason)
        {
            if (!CanGrip(out reason))
            {
                return false;
            }

            bool gripped = gripper.TryGrip();
            if (!gripped || !gripper.IsHolding)
            {
                reason = "TryGrip returned false or IsHolding stayed false.";
                LastFailureReason = reason;
                return false;
            }

            NormalizeHeldObjectForColorSensor();
            reason = string.Empty;
            LastFailureReason = string.Empty;
            return true;
        }

        public void BeginSmoothHeldObjectWorldGridAlignment(float durationSec)
        {
            if (gripper == null)
            {
                return;
            }

            PickableObject heldObject = gripper.HeldObject;
            if (heldObject == null)
            {
                return;
            }

            Transform heldTransform = heldObject.transform;
            smoothAlignmentStartRotation = heldTransform.rotation;
            smoothAlignmentStartedAt = Time.time;
            smoothAlignmentDuration = Mathf.Max(0f, durationSec);
            smoothAlignmentActive = smoothAlignmentDuration > 0f
                && Quaternion.Angle(smoothAlignmentStartRotation, Quaternion.identity) > 0.1f;

            if (!smoothAlignmentActive)
            {
                ForceHeldObjectWorldGridAlignment();
            }
        }

        public void MaintainHeldObjectWorldGridAlignment()
        {
            if (gripper == null || !gripper.IsHolding)
            {
                smoothAlignmentActive = false;
                ClearAlignmentState();
                return;
            }

            if (!smoothAlignmentActive)
            {
                AlignHeldObjectToWorldRotation(Quaternion.identity);
                return;
            }

            float elapsed = Time.time - smoothAlignmentStartedAt;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.001f, smoothAlignmentDuration));
            AlignHeldObjectToWorldRotation(Quaternion.Slerp(
                smoothAlignmentStartRotation,
                Quaternion.identity,
                t));

            if (t >= 1f)
            {
                smoothAlignmentActive = false;
            }
        }

        private void ForceHeldObjectWorldGridAlignment()
        {
            smoothAlignmentActive = false;
            AlignHeldObjectToWorldRotation(Quaternion.identity);
        }

        private void AlignHeldObjectToWorldRotation(Quaternion worldRotation)
        {
            if (gripper == null)
            {
                return;
            }

            PickableObject heldObject = gripper.HeldObject;
            if (heldObject == null)
            {
                return;
            }

            Transform heldTransform = heldObject.transform;
            Transform attachTransform = heldTransform.parent;
            if (attachTransform == null)
            {
                heldTransform.rotation = worldRotation;
                return;
            }

            CacheAttachPointLocalInHeldObject(heldObject, heldTransform, attachTransform);
            heldTransform.rotation = worldRotation;
            Vector3 attachedPointWorld = heldTransform.TransformPoint(attachPointLocalInHeldObject);
            heldTransform.position += attachTransform.position - attachedPointWorld;
        }

        private void CacheAttachPointLocalInHeldObject(
            PickableObject heldObject,
            Transform heldTransform,
            Transform attachTransform)
        {
            if (hasAttachPointLocalInHeldObject && alignedObject == heldObject)
            {
                return;
            }

            alignedObject = heldObject;
            attachPointLocalInHeldObject = heldTransform.InverseTransformPoint(attachTransform.position);
            hasAttachPointLocalInHeldObject = true;
        }

        private void ClearAlignmentState()
        {
            alignedObject = null;
            hasAttachPointLocalInHeldObject = false;
        }

        private void NormalizeHeldObjectForColorSensor()
        {
            PickableObject heldObject = gripper.HeldObject;
            if (heldObject == null || heldObject.gameObject == null)
            {
                return;
            }

            GameObject root = heldObject.gameObject;
            if (root.tag != "Product" || root.GetComponent<Renderer>() != null)
            {
                return;
            }

            foreach (Renderer childRenderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (childRenderer != null
                    && childRenderer.gameObject != root
                    && childRenderer.gameObject.tag == "Product")
                {
                    root.tag = "Untagged";
                    return;
                }
            }
        }

        public void Release()
        {
            if (gripper == null)
            {
                LastFailureReason = "SuctionGripper reference is missing.";
                return;
            }

            ForceHeldObjectWorldGridAlignment();
            gripper.Release();
            ClearAlignmentState();
        }
    }
}
