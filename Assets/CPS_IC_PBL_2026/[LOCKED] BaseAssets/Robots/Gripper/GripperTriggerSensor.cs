using System.Collections.Generic;
using UnityEngine;

namespace CPS.Lab11.MobileManipulator
{
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class GripperTriggerSensor : MonoBehaviour
    {
        [Header("Trigger Debug")]
        [SerializeField] private bool reconcileOverlaps = true;
        [SerializeField] private bool logCandidateChanges = true;
        [SerializeField] private bool logOverlapRefresh;
        [SerializeField] private float overlapPadding = 0.002f;
        [SerializeField] private LayerMask overlapLayers = ~0;

        private readonly List<PickableObject> candidates = new List<PickableObject>();
        private readonly Collider[] overlapBuffer = new Collider[32];
        private Collider sensorCollider;

        public PickableObject CurrentCandidate => candidates.Count > 0 ? candidates[0] : null;
        public IReadOnlyList<PickableObject> Candidates => candidates;
        public int CandidateCount => candidates.Count;
        public bool HasCandidate => CurrentCandidate != null;
        public string DebugSummary => CurrentCandidate != null
            ? $"{CurrentCandidate.name} ({candidates.Count})"
            : $"None ({candidates.Count})";

        private void Awake()
        {
            sensorCollider = GetComponent<Collider>();
            if (sensorCollider != null)
            {
                sensorCollider.isTrigger = true;
            }
        }

        private void Reset()
        {
            Collider sensorCollider = GetComponent<Collider>();
            sensorCollider.isTrigger = true;
        }

        private void FixedUpdate()
        {
            if (reconcileOverlaps)
            {
                RefreshCandidatesFromOverlap();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            PickableObject pickable = other.GetComponentInParent<PickableObject>();
            AddCandidate(pickable, "entered", other);
        }

        private void OnTriggerStay(Collider other)
        {
            if (!reconcileOverlaps)
            {
                return;
            }

            PickableObject pickable = other.GetComponentInParent<PickableObject>();
            AddCandidate(pickable, "stayed", other, false);
        }

        private void OnTriggerExit(Collider other)
        {
            PickableObject pickable = other.GetComponentInParent<PickableObject>();
            if (pickable == null)
            {
                return;
            }

            if (reconcileOverlaps && IsOverlapping(pickable))
            {
                if (logCandidateChanges)
                {
                    Debug.Log($"Grip candidate exit ignored because overlap is still true: {pickable.name} via {other.name}", this);
                }

                return;
            }

            if (candidates.Remove(pickable))
            {
                if (logCandidateChanges)
                {
                    Debug.Log($"Grip candidate exited: {pickable.name} via {other.name}. Count={candidates.Count}", this);
                }
            }
        }

        public bool Contains(PickableObject pickable)
        {
            return pickable != null && candidates.Contains(pickable);
        }

        public void Clear()
        {
            candidates.Clear();
        }

        [ContextMenu("Log Trigger Candidates")]
        public void LogTriggerCandidates()
        {
            Debug.Log($"Trigger candidates: {DebugSummary}", this);
            for (int i = 0; i < candidates.Count; i++)
            {
                PickableObject candidate = candidates[i];
                Debug.Log($"Candidate[{i}]: {(candidate != null ? candidate.name : "null")}", this);
            }
        }

        [ContextMenu("Refresh Candidates From Overlap")]
        public void RefreshCandidatesFromOverlap()
        {
            if (sensorCollider == null)
            {
                sensorCollider = GetComponent<Collider>();
            }

            if (sensorCollider == null)
            {
                return;
            }

            Bounds bounds = sensorCollider.bounds;
            Vector3 halfExtents = bounds.extents + Vector3.one * overlapPadding;
            int hitCount = Physics.OverlapBoxNonAlloc(
                bounds.center,
                halfExtents,
                overlapBuffer,
                Quaternion.identity,
                overlapLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = overlapBuffer[i];
                if (hit == null || hit == sensorCollider || hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                PickableObject pickable = hit.GetComponentInParent<PickableObject>();
                if (pickable == null || !IsOverlapping(hit))
                {
                    continue;
                }

                AddCandidate(pickable, "overlap-refresh", hit, logOverlapRefresh);
            }
        }

        private void AddCandidate(PickableObject pickable, string eventName, Collider sourceCollider, bool shouldLog = true)
        {
            if (pickable == null || candidates.Contains(pickable))
            {
                return;
            }

            candidates.Add(pickable);
            if (logCandidateChanges && shouldLog)
            {
                Debug.Log($"Grip candidate {eventName}: {pickable.name} via {sourceCollider.name}. Count={candidates.Count}", this);
            }
        }

        private bool IsOverlapping(PickableObject pickable)
        {
            if (pickable == null)
            {
                return false;
            }

            Collider[] colliders = pickable.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider candidateCollider = colliders[i];
                if (candidateCollider != null && IsOverlapping(candidateCollider))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsOverlapping(Collider other)
        {
            if (sensorCollider == null)
            {
                sensorCollider = GetComponent<Collider>();
            }

            return sensorCollider != null
                && other != null
                && Physics.ComputePenetration(
                    sensorCollider,
                    sensorCollider.transform.position,
                    sensorCollider.transform.rotation,
                    other,
                    other.transform.position,
                    other.transform.rotation,
                    out _,
                    out _);
        }
    }
}
