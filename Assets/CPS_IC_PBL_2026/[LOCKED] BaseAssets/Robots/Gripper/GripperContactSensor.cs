using System.Collections.Generic;
using UnityEngine;

namespace CPS.Lab11.MobileManipulator
{
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class GripperContactSensor : MonoBehaviour
    {
        private sealed class ContactState
        {
            public int ContactCount;
            public float ContactTime;
            public float RelativeSpeed;
            public float LastSeenTime;
        }

        [Header("Contact Detection")]
        [SerializeField] private bool useOverlapFallback = true;
        [SerializeField] private float overlapPadding = 0.002f;
        [SerializeField] private LayerMask overlapLayers = ~0;

        private readonly Collider[] overlapBuffer = new Collider[32];
        private readonly Dictionary<PickableObject, ContactState> contactStates = new Dictionary<PickableObject, ContactState>();
        private readonly List<PickableObject> staleContacts = new List<PickableObject>();
        private Collider sensorCollider;

        public PickableObject CurrentContactCandidate
        {
            get
            {
                foreach (KeyValuePair<PickableObject, ContactState> pair in contactStates)
                {
                    if (pair.Key != null && pair.Value.ContactCount > 0)
                    {
                        return pair.Key;
                    }
                }

                return null;
            }
        }

        private void Awake()
        {
            sensorCollider = GetComponent<Collider>();
        }

        private void Reset()
        {
            Collider sensorCollider = GetComponent<Collider>();
            sensorCollider.isTrigger = false;
        }

        private void FixedUpdate()
        {
            if (useOverlapFallback)
            {
                UpdateOverlapContacts();
            }

            RemoveStaleContacts();
        }

        private void OnCollisionStay(Collision collision)
        {
            PickableObject pickable = collision.collider.GetComponentInParent<PickableObject>();
            if (pickable == null)
            {
                return;
            }

            UpdateContact(pickable, collision.contactCount, collision.relativeVelocity.magnitude);
        }

        private void OnCollisionExit(Collision collision)
        {
            PickableObject pickable = collision.collider.GetComponentInParent<PickableObject>();
            if (pickable != null)
            {
                contactStates.Remove(pickable);
            }
        }

        public bool IsContacting(PickableObject target, int minContactCount = 1)
        {
            return target != null
                && contactStates.TryGetValue(target, out ContactState state)
                && state.ContactCount >= minContactCount;
        }

        public float GetContactTime(PickableObject target)
        {
            return target != null && contactStates.TryGetValue(target, out ContactState state)
                ? state.ContactTime
                : 0f;
        }

        public float GetRelativeSpeed(PickableObject target)
        {
            return target != null && contactStates.TryGetValue(target, out ContactState state)
                ? state.RelativeSpeed
                : float.PositiveInfinity;
        }

        public int GetContactCount(PickableObject target)
        {
            return target != null && contactStates.TryGetValue(target, out ContactState state)
                ? state.ContactCount
                : 0;
        }

        public void Clear()
        {
            contactStates.Clear();
        }

        private void UpdateOverlapContacts()
        {
            if (sensorCollider == null)
            {
                sensorCollider = GetComponent<Collider>();
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
                if (pickable == null)
                {
                    continue;
                }

                if (!Physics.ComputePenetration(
                    sensorCollider,
                    sensorCollider.transform.position,
                    sensorCollider.transform.rotation,
                    hit,
                    hit.transform.position,
                    hit.transform.rotation,
                    out _,
                    out _))
                {
                    continue;
                }

                UpdateContact(pickable, 1, GetRelativeSpeed(hit));
            }
        }

        private void UpdateContact(PickableObject pickable, int contactCount, float relativeSpeed)
        {
            if (!contactStates.TryGetValue(pickable, out ContactState state))
            {
                state = new ContactState();
                contactStates.Add(pickable, state);
            }

            state.ContactCount = Mathf.Max(state.ContactCount, contactCount);
            state.ContactTime += Time.fixedDeltaTime;
            state.RelativeSpeed = relativeSpeed;
            state.LastSeenTime = Time.time;
        }

        private float GetRelativeSpeed(Collider other)
        {
            Rigidbody ownRigidbody = sensorCollider.attachedRigidbody;
            Rigidbody otherRigidbody = other.attachedRigidbody;

            Vector3 ownVelocity = ownRigidbody != null ? ownRigidbody.velocity : Vector3.zero;
            Vector3 otherVelocity = otherRigidbody != null ? otherRigidbody.velocity : Vector3.zero;
            return (ownVelocity - otherVelocity).magnitude;
        }

        private void RemoveStaleContacts()
        {
            staleContacts.Clear();
            float staleDelay = Mathf.Max(Time.fixedDeltaTime * 2f, 0.02f);

            foreach (KeyValuePair<PickableObject, ContactState> pair in contactStates)
            {
                if (Time.time - pair.Value.LastSeenTime > staleDelay)
                {
                    staleContacts.Add(pair.Key);
                }
            }

            for (int i = 0; i < staleContacts.Count; i++)
            {
                contactStates.Remove(staleContacts[i]);
            }
        }
    }
}
