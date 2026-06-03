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

            reason = string.Empty;
            LastFailureReason = string.Empty;
            return true;
        }

        public void Release()
        {
            if (gripper == null)
            {
                LastFailureReason = "SuctionGripper reference is missing.";
                return;
            }

            gripper.Release();
            LastFailureReason = gripper.IsHolding
                ? "Release returned but IsHolding stayed true."
                : string.Empty;
        }

        public bool TryAlignHeldObjectCenterTo(
            Vector3 worldCenter,
            out float alignmentError,
            out string reason)
        {
            alignmentError = float.PositiveInfinity;
            reason = string.Empty;

            if (gripper == null)
            {
                reason = "SuctionGripper reference is missing.";
                LastFailureReason = reason;
                return false;
            }

            PickableObject heldObject = gripper.HeldObject;
            if (heldObject == null)
            {
                reason = "No held object to align.";
                LastFailureReason = reason;
                return false;
            }

            Vector3 currentCenter = TryGetObjectBounds(heldObject, out Bounds bounds)
                ? bounds.center
                : heldObject.transform.position;
            Vector3 delta = worldCenter - currentCenter;
            heldObject.transform.position += delta;

            Rigidbody body = heldObject.TargetRigidbody;
            if (body != null)
            {
                body.position = heldObject.transform.position;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();
            Vector3 alignedCenter = TryGetObjectBounds(heldObject, out Bounds alignedBounds)
                ? alignedBounds.center
                : heldObject.transform.position;
            alignmentError = Vector3.Distance(alignedCenter, worldCenter);
            LastFailureReason = string.Empty;
            return true;
        }

        public IEnumerator WaitUntilReleased(float timeoutSec)
        {
            if (gripper == null)
            {
                LastFailureReason = "SuctionGripper reference is missing.";
                yield break;
            }

            float deadline = Time.time + Mathf.Max(0f, timeoutSec);
            while (Time.time <= deadline)
            {
                if (!gripper.IsHolding)
                {
                    LastFailureReason = string.Empty;
                    yield break;
                }

                LastFailureReason = "waiting for gripper release confirmation";
                yield return null;
            }

            if (gripper.IsHolding)
            {
                LastFailureReason = "release confirmation timeout";
            }
        }

        private static bool TryGetObjectBounds(PickableObject pickable, out Bounds bounds)
        {
            Collider[] colliders = pickable.GetComponentsInChildren<Collider>();
            bounds = default;
            bool hasBounds = false;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider objectCollider = colliders[i];
                if (objectCollider == null || !objectCollider.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = objectCollider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(objectCollider.bounds);
                }
            }

            return hasBounds;
        }
    }
}
