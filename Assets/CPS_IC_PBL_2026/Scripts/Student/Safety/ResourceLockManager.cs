using System.Collections.Generic;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    [DisallowMultipleComponent]
    public sealed class ResourceLockManager : MonoBehaviour, IResourceLockManager
    {
        private sealed class LockRecord
        {
            public ResourceLockToken Token;
            public float LastWarningAt;
        }

        [Header("Diagnostics")]
        [SerializeField] private bool logWarnings = true;
        [SerializeField] private bool warnLongHeldLocks = true;
        [SerializeField, Min(0.1f)] private float longHoldWarningSec = 10f;
        [SerializeField, Min(0.1f)] private float warningIntervalSec = 5f;

        private readonly Dictionary<ResourceKey, LockRecord> locks =
            new Dictionary<ResourceKey, LockRecord>();

        public int ActiveLockCount => locks.Count;

        public bool TryAcquire(
            ResourceKey key,
            int robotId,
            int taskId,
            out ResourceLockToken token)
        {
            token = null;

            if (locks.TryGetValue(key, out LockRecord existing))
            {
                Warn(string.Format(
                    "Acquire failed key={0} requestedBy robot={1} task={2}; heldBy robot={3} task={4}.",
                    key,
                    robotId,
                    taskId,
                    existing.Token.robotId,
                    existing.Token.taskId));
                return false;
            }

            token = new ResourceLockToken
            {
                key = key,
                robotId = robotId,
                taskId = taskId,
                acquiredAt = Time.time
            };

            locks.Add(key, new LockRecord
            {
                Token = token,
                LastWarningAt = Time.time
            });
            return true;
        }

        public void Release(ResourceLockToken token)
        {
            if (token == null)
            {
                Warn("Ignored null lock token release.");
                return;
            }

            if (!locks.TryGetValue(token.key, out LockRecord existing))
            {
                Warn(string.Format(
                    "Ignored release for unlocked key={0} robot={1} task={2}.",
                    token.key,
                    token.robotId,
                    token.taskId));
                return;
            }

            if (!Matches(existing.Token, token))
            {
                Warn(string.Format(
                    "Ignored release with stale token key={0} tokenRobot={1} tokenTask={2} ownerRobot={3} ownerTask={4}.",
                    token.key,
                    token.robotId,
                    token.taskId,
                    existing.Token.robotId,
                    existing.Token.taskId));
                return;
            }

            locks.Remove(token.key);
        }

        public bool IsLocked(ResourceKey key)
        {
            return locks.ContainsKey(key);
        }

        public bool TryGetOwner(ResourceKey key, out int robotId, out int taskId)
        {
            if (locks.TryGetValue(key, out LockRecord record))
            {
                robotId = record.Token.robotId;
                taskId = record.Token.taskId;
                return true;
            }

            robotId = StudentConstants.UnassignedRobotId;
            taskId = StudentConstants.NoTaskId;
            return false;
        }

        public void ClearAll()
        {
            locks.Clear();
        }

        private void Update()
        {
            if (!warnLongHeldLocks || locks.Count == 0)
            {
                return;
            }

            float now = Time.time;
            foreach (KeyValuePair<ResourceKey, LockRecord> pair in locks)
            {
                ResourceLockToken token = pair.Value.Token;
                float heldFor = now - token.acquiredAt;
                if (heldFor < longHoldWarningSec)
                {
                    continue;
                }

                if (now - pair.Value.LastWarningAt < warningIntervalSec)
                {
                    continue;
                }

                pair.Value.LastWarningAt = now;
                Warn(string.Format(
                    "Long-held lock key={0} robot={1} task={2} heldFor={3:0.0}s.",
                    token.key,
                    token.robotId,
                    token.taskId,
                    heldFor));
            }
        }

        private void OnValidate()
        {
            longHoldWarningSec = Mathf.Max(0.1f, longHoldWarningSec);
            warningIntervalSec = Mathf.Max(0.1f, warningIntervalSec);
        }

        private static bool Matches(ResourceLockToken left, ResourceLockToken right)
        {
            return left != null
                && right != null
                && left.key == right.key
                && left.robotId == right.robotId
                && left.taskId == right.taskId
                && Mathf.Approximately(left.acquiredAt, right.acquiredAt);
        }

        private void Warn(string message)
        {
            if (logWarnings)
            {
                Debug.LogWarning(string.Format("[ResourceLockManager] {0}", message), this);
            }
        }
    }
}
