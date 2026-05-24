using UnityEngine;

namespace CPS.Lab11.MobileManipulator
{
    [DisallowMultipleComponent]
    public class SuctionGripper : MonoBehaviour
    {
        [Header("Sensor References")]
        [SerializeField] private GripperTriggerSensor triggerSensor;
        [SerializeField] private GripperContactSensor contactSensor;
        [SerializeField] private Transform attachPoint;

        [Header("Grip Conditions")]
        [SerializeField] private float requiredContactTime = 0.2f;
        [SerializeField] private bool allowContactCandidateFallback = true;
        [SerializeField] private bool useAttachDistanceLimit;
        [SerializeField] private float maxAttachDistance = 0.08f;
        [SerializeField] private float maxGripSpeed = 0.5f;
        [SerializeField] private int minContactCount = 1;
        [SerializeField] private bool keepPickWorldPose = true;
        [SerializeField] private bool snapObjectToAttachPoint;

        [Header("Release Behavior")]
        [SerializeField] private bool liftObjectAboveSurfaceOnRelease = true;
        [SerializeField] private float releaseSurfaceClearance = 0.005f;
        [SerializeField] private float releaseSurfaceProbeDistance = 0.5f;
        [SerializeField] private LayerMask releaseSurfaceLayers = ~0;

        [Header("Debug Visual")]
        [SerializeField] private Renderer sensorDebugRenderer;
        [SerializeField] private Color idleColor = Color.gray;
        [SerializeField] private Color candidateColor = Color.yellow;
        [SerializeField] private Color readyColor = Color.green;
        [SerializeField] private Color holdingColor = Color.cyan;

        public PickableObject HeldObject { get; private set; }
        public PickableObject CurrentCandidate => GetCurrentCandidate();
        public bool IsHolding => HeldObject != null;
        public bool IsGraspReady => CanGrip(out _);

        private readonly RaycastHit[] releaseSurfaceHits = new RaycastHit[16];

        private void Reset()
        {
            triggerSensor = GetComponentInChildren<GripperTriggerSensor>();
            contactSensor = GetComponentInChildren<GripperContactSensor>();
            Transform attach = transform.Find("AttachPoint");
            attachPoint = attach != null ? attach : transform;
        }

        private void Update()
        {
            UpdateDebugVisual();
        }

        public bool TryGrip()
        {
            if (!CanGrip(out string reason))
            {
                Debug.Log($"Grip failed: {reason}", this);
                return false;
            }

            HeldObject = CurrentCandidate;
            HeldObject.Attach(attachPoint, !keepPickWorldPose && snapObjectToAttachPoint);
            Debug.Log($"Grip success: {HeldObject.name}", this);
            return true;
        }

        public void Release()
        {
            if (HeldObject == null)
            {
                return;
            }

            PickableObject released = HeldObject;
            if (liftObjectAboveSurfaceOnRelease)
            {
                LiftObjectAboveSurface(released);
            }

            HeldObject.Release();
            HeldObject = null;
            triggerSensor?.Clear();
            contactSensor?.Clear();
            Debug.Log($"Released: {released.name}", this);
        }

        public bool CanGrip(out string reason)
        {
            reason = string.Empty;

            if (HeldObject != null)
            {
                reason = "already holding an object";
                return false;
            }

            PickableObject candidate = CurrentCandidate;
            if (candidate == null)
            {
                reason = "no candidate in DetectionTrigger or ContactProbe";
                return false;
            }

            bool isInsideTrigger = triggerSensor != null && triggerSensor.Contains(candidate);
            bool isContacting = contactSensor != null && contactSensor.IsContacting(candidate, minContactCount);
            if (!isInsideTrigger && !(allowContactCandidateFallback && isContacting))
            {
                reason = "candidate is not inside DetectionTrigger";
                return false;
            }

            if (!isContacting)
            {
                reason = "candidate is not contacting ContactProbe";
                return false;
            }

            float contactTime = contactSensor.GetContactTime(candidate);
            if (contactTime < requiredContactTime)
            {
                reason = $"contact time too short ({contactTime:0.00}s)";
                return false;
            }

            if (attachPoint == null)
            {
                reason = "attachPoint is null";
                return false;
            }

            if (useAttachDistanceLimit)
            {
                float distance = Vector3.Distance(attachPoint.position, candidate.transform.position);
                if (distance > maxAttachDistance)
                {
                    reason = $"attach distance too far ({distance:0.000}m)";
                    return false;
                }
            }

            float relativeSpeed = contactSensor.GetRelativeSpeed(candidate);
            if (relativeSpeed > maxGripSpeed)
            {
                reason = $"relative speed too high ({relativeSpeed:0.00}m/s)";
                return false;
            }

            return true;
        }

        private PickableObject GetCurrentCandidate()
        {
            PickableObject triggerCandidate = triggerSensor != null ? triggerSensor.CurrentCandidate : null;
            if (triggerCandidate != null)
            {
                return triggerCandidate;
            }

            return allowContactCandidateFallback && contactSensor != null
                ? contactSensor.CurrentContactCandidate
                : null;
        }

        private void LiftObjectAboveSurface(PickableObject pickable)
        {
            if (pickable == null || !TryGetObjectBounds(pickable, out Bounds objectBounds))
            {
                return;
            }

            Vector3 rayOrigin = objectBounds.center + Vector3.up * (objectBounds.extents.y + releaseSurfaceProbeDistance);
            float rayDistance = objectBounds.size.y + releaseSurfaceProbeDistance * 2f;

            int hitCount = Physics.RaycastNonAlloc(
                rayOrigin,
                Vector3.down,
                releaseSurfaceHits,
                rayDistance,
                releaseSurfaceLayers,
                QueryTriggerInteraction.Ignore);

            bool foundSurface = false;
            RaycastHit nearestSurfaceHit = default;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = releaseSurfaceHits[i];
                if (hit.collider == null
                    || hit.collider.transform.IsChildOf(pickable.transform)
                    || hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (!foundSurface || hit.distance < nearestSurfaceHit.distance)
                {
                    nearestSurfaceHit = hit;
                    foundSurface = true;
                }
            }

            if (!foundSurface)
            {
                return;
            }

            float targetBottomY = nearestSurfaceHit.point.y + releaseSurfaceClearance;
            float liftDistance = targetBottomY - objectBounds.min.y;
            if (liftDistance <= 0f)
            {
                return;
            }

            pickable.transform.position += Vector3.up * liftDistance;
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

        private void UpdateDebugVisual()
        {
            if (sensorDebugRenderer == null)
            {
                return;
            }

            Color color = idleColor;
            if (IsHolding)
            {
                color = holdingColor;
            }
            else if (IsGraspReady)
            {
                color = readyColor;
            }
            else if (CurrentCandidate != null)
            {
                color = candidateColor;
            }

            sensorDebugRenderer.material.color = color;
        }
    }
}
