using System.Collections;
using CPS.Lab11.MobileManipulator;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    public sealed class GripperAdapter
    {
        private const float SuctionFaceOffsetFromAttachPoint = 0f;
        private const float ReleaseSurfaceProbePadding = 0.45f;
        private const float ReleaseSurfaceContactOffset = 0.012f;

        private readonly SuctionGripper gripper;
        private readonly RaycastHit[] releaseSurfaceHits = new RaycastHit[16];
        private PickableObject lastReleasedObject;

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

        public void ClearSensorCandidates()
        {
            LastFailureReason = string.Empty;
            if (gripper == null)
            {
                return;
            }

            GripperTriggerSensor[] triggerSensors =
                gripper.GetComponentsInChildren<GripperTriggerSensor>(true);
            for (int i = 0; i < triggerSensors.Length; i++)
            {
                triggerSensors[i]?.Clear();
            }

            GripperContactSensor[] contactSensors =
                gripper.GetComponentsInChildren<GripperContactSensor>(true);
            for (int i = 0; i < contactSensors.Length; i++)
            {
                contactSensors[i]?.Clear();
            }
        }

        public bool TryGetCurrentCandidatePickTarget(float contactInset, out Vector3 worldPos)
        {
            worldPos = default;
            if (gripper == null)
            {
                return false;
            }

            PickableObject candidate = gripper.CurrentCandidate;
            if (candidate == null || !TryGetObjectBounds(candidate, out Bounds objectBounds))
            {
                return false;
            }

            Vector3 topCenter = new Vector3(
                objectBounds.center.x,
                objectBounds.max.y - Mathf.Max(0f, contactInset),
                objectBounds.center.z);
            Vector3 suctionAxis = gripper.transform.forward.normalized;
            worldPos = topCenter - (suctionAxis * SuctionFaceOffsetFromAttachPoint);
            return true;
        }

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
            SnapHeldObjectTopToSuctionFace();
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

            PickableObject releasedObject = gripper.HeldObject;
            ForceHeldObjectWorldGridAlignment();
            gripper.Release();
            EnableReleasedObjectVerticalSettlePhysics(releasedObject);
            lastReleasedObject = releasedObject;
            ClearAlignmentState();
        }

        public void FreezeLastReleasedObjectAtProductCenter(Vector3 productCenter)
        {
            PickableObject releasedObject = lastReleasedObject;
            if (releasedObject == null)
            {
                return;
            }

            MoveReleasedObjectCenterTo(releasedObject, productCenter);
            FreezeReleasedObject(releasedObject);
        }

        public void FreezeLastReleasedObjectOnSurface()
        {
            PickableObject releasedObject = lastReleasedObject;
            if (releasedObject == null)
            {
                return;
            }

            SettleReleasedObjectOntoSurface(releasedObject);
            FreezeReleasedObject(releasedObject);
        }

        private static void FreezeReleasedObject(PickableObject releasedObject)
        {
            Rigidbody body = releasedObject.TargetRigidbody;
            if (body == null)
            {
                return;
            }

            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
            body.constraints = RigidbodyConstraints.FreezeAll;
            body.detectCollisions = true;
        }

        private static void MoveReleasedObjectCenterTo(
            PickableObject releasedObject,
            Vector3 productCenter)
        {
            if (releasedObject == null)
            {
                return;
            }

            Physics.SyncTransforms();
            if (!TryGetObjectBounds(releasedObject, out Bounds objectBounds))
            {
                return;
            }

            releasedObject.transform.position += productCenter - objectBounds.center;
            Physics.SyncTransforms();
        }

        private void SnapHeldObjectTopToSuctionFace()
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
            if (attachTransform == null || !TryGetObjectBounds(heldObject, out Bounds objectBounds))
            {
                return;
            }

            Vector3 suctionAxis = attachTransform.forward.normalized;
            Vector3 suctionFace = attachTransform.position
                + suctionAxis * SuctionFaceOffsetFromAttachPoint;
            float targetTopProjection = Vector3.Dot(suctionFace, suctionAxis);
            float currentTopProjection = GetMinimumBoundsProjection(objectBounds, suctionAxis);
            heldTransform.position += suctionAxis * (targetTopProjection - currentTopProjection);
            Physics.SyncTransforms();
            ClearAlignmentState();
        }

        private void SettleReleasedObjectOntoSurface(PickableObject releasedObject)
        {
            if (releasedObject == null || gripper == null)
            {
                return;
            }

            Physics.SyncTransforms();
            if (!TryGetObjectBounds(releasedObject, out Bounds objectBounds))
            {
                return;
            }

            Vector3 rayOrigin = objectBounds.center
                + Vector3.up * (objectBounds.extents.y + ReleaseSurfaceProbePadding);
            float rayDistance = objectBounds.size.y + (ReleaseSurfaceProbePadding * 2f);
            int hitCount = Physics.RaycastNonAlloc(
                rayOrigin,
                Vector3.down,
                releaseSurfaceHits,
                rayDistance,
                ~0,
                QueryTriggerInteraction.Ignore);

            bool foundSurface = false;
            RaycastHit nearestSurfaceHit = default;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = releaseSurfaceHits[i];
                if (hit.collider == null
                    || hit.collider.transform.IsChildOf(releasedObject.transform)
                    || hit.collider.transform.IsChildOf(gripper.transform))
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

            float targetBottomY = nearestSurfaceHit.point.y + ReleaseSurfaceContactOffset;
            float settleDistance = targetBottomY - objectBounds.min.y;
            if (Mathf.Abs(settleDistance) <= 0.0001f)
            {
                return;
            }

            releasedObject.transform.position += Vector3.up * settleDistance;
            Physics.SyncTransforms();
        }

        private static void EnableReleasedObjectVerticalSettlePhysics(PickableObject releasedObject)
        {
            if (releasedObject == null || releasedObject.TargetRigidbody == null)
            {
                return;
            }

            Rigidbody body = releasedObject.TargetRigidbody;
            body.isKinematic = false;
            body.useGravity = true;
            body.detectCollisions = true;
            body.constraints = RigidbodyConstraints.FreezePositionX
                | RigidbodyConstraints.FreezePositionZ
                | RigidbodyConstraints.FreezeRotation;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
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

        private static float GetMinimumBoundsProjection(Bounds bounds, Vector3 axis)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            float minimum = float.PositiveInfinity;

            minimum = Mathf.Min(minimum, Vector3.Dot(new Vector3(min.x, min.y, min.z), axis));
            minimum = Mathf.Min(minimum, Vector3.Dot(new Vector3(min.x, min.y, max.z), axis));
            minimum = Mathf.Min(minimum, Vector3.Dot(new Vector3(min.x, max.y, min.z), axis));
            minimum = Mathf.Min(minimum, Vector3.Dot(new Vector3(min.x, max.y, max.z), axis));
            minimum = Mathf.Min(minimum, Vector3.Dot(new Vector3(max.x, min.y, min.z), axis));
            minimum = Mathf.Min(minimum, Vector3.Dot(new Vector3(max.x, min.y, max.z), axis));
            minimum = Mathf.Min(minimum, Vector3.Dot(new Vector3(max.x, max.y, min.z), axis));
            minimum = Mathf.Min(minimum, Vector3.Dot(new Vector3(max.x, max.y, max.z), axis));
            return minimum;
        }
    }
}
