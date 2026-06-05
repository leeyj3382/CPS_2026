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

        public bool TryReadHeldObjectColor(out Color color, out string source)
        {
            color = default;
            source = string.Empty;

            if (gripper == null)
            {
                source = "SuctionGripper reference is missing.";
                return false;
            }

            PickableObject heldObject = gripper.HeldObject;
            if (heldObject == null)
            {
                source = "No held object.";
                return false;
            }

            Renderer renderer = heldObject.GetComponentInChildren<Renderer>();
            if (renderer == null)
            {
                source = string.Format("Held object {0} has no renderer.", heldObject.name);
                return false;
            }

            if (!TryReadMaterialColor(renderer, out color))
            {
                source = string.Format("Held object {0} renderer has no readable color.", heldObject.name);
                return false;
            }

            source = string.Format("HeldObject:{0}/{1}", heldObject.name, renderer.name);
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
        }

        private static bool TryReadMaterialColor(Renderer renderer, out Color color)
        {
            color = default;
            if (renderer == null)
            {
                return false;
            }

            Material material = renderer.material;
            if (material == null)
            {
                return false;
            }

            if (material.HasProperty("_BaseColor"))
            {
                color = material.GetColor("_BaseColor");
                return true;
            }

            if (material.HasProperty("_Color"))
            {
                color = material.color;
                return true;
            }

            return false;
        }
    }
}
