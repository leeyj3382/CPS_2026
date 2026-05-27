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

        public bool TryGripWithRetry(int retryCount, float waitBetweenRetrySec)
        {
            int attempts = Mathf.Max(0, retryCount) + 1;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                if (TryGrip(out _))
                {
                    return true;
                }
            }

            return false;
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
    }
}
