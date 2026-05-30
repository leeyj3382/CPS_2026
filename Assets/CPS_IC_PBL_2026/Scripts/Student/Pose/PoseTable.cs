using System;
using CPS.ICPBL.Common;
using CPS.ICPBL.Environment;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    public class PoseTable : MonoBehaviour, IPoseProvider
    {
        private const float MinArmMoveDuration = 0.01f;

        [Header("Optional Base Station Source")]
        [SerializeField] private OperatingStations operatingStations;

        [Header("Conveyor Pick Offsets")]
        [SerializeField] private Vector3 conveyorApproachOffset = new Vector3(0f, 0.25f, 0f);
        [SerializeField] private Vector3 conveyorActionOffset = new Vector3(0f, -0.35f, 0f);
        [SerializeField] private Vector3 conveyorRetractOffset = new Vector3(0f, 0.35f, 0f);
        [SerializeField] private float conveyorArmMoveDuration = StudentConstants.DefaultArmMoveDurationSec;

        [Header("Box Base Offsets")]
        [SerializeField] private Vector3 boxApproachOffset = new Vector3(0f, 0.55f, 0f);
        [SerializeField] private Vector3 boxActionOffset = new Vector3(0f, 0.20f, 0f);
        [SerializeField] private Vector3 boxRetractOffset = new Vector3(0f, 0.65f, 0f);
        [SerializeField] private float boxArmMoveDuration = StudentConstants.DefaultArmMoveDurationSec;

        [Header("Manual Pose Overrides")]
        [SerializeField] private StationPose[] conveyorPickOverrides;
        [SerializeField] private StationPose normalBoxBaseOverride;
        [SerializeField] private StationPose abnormalBoxBaseOverride;

        [Header("Scene Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private int debugStationId = StudentConstants.MinConveyorId;
        [SerializeField] private float gizmoRadius = 0.08f;

        private readonly StationPose[] conveyorPickPoses = new StationPose[StudentConstants.MaxConveyorId + 1];
        private StationPose normalBoxBasePose;
        private StationPose abnormalBoxBasePose;
        private bool initialized;

        public StationPose GetConveyorPickPose(int conveyorId)
        {
            EnsureInitialized();

            if (!StudentConstants.IsConveyorId(conveyorId))
            {
                throw new ArgumentOutOfRangeException(nameof(conveyorId), conveyorId, "Conveyor id must be 1~10.");
            }

            return ClonePose(conveyorPickPoses[conveyorId]);
        }

        public bool TryGetConveyorPickPose(int conveyorId, out StationPose pose)
        {
            if (!StudentConstants.IsConveyorId(conveyorId))
            {
                pose = null;
                return false;
            }

            pose = GetConveyorPickPose(conveyorId);
            return pose != null;
        }

        public StationPose GetBoxBasePose(BoxType boxType)
        {
            EnsureInitialized();

            if (boxType == BoxType.Normal)
            {
                return ClonePose(normalBoxBasePose);
            }

            if (boxType == BoxType.Abnormal)
            {
                return ClonePose(abnormalBoxBasePose);
            }

            throw new ArgumentOutOfRangeException(nameof(boxType), boxType, "Unsupported box type.");
        }

        public bool TryGetBoxBasePose(BoxType boxType, out StationPose pose)
        {
            if (boxType == BoxType.Normal || boxType == BoxType.Abnormal)
            {
                pose = GetBoxBasePose(boxType);
                return pose != null;
            }

            pose = null;
            return false;
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnValidate()
        {
            conveyorArmMoveDuration = Mathf.Max(MinArmMoveDuration, conveyorArmMoveDuration);
            boxArmMoveDuration = Mathf.Max(MinArmMoveDuration, boxArmMoveDuration);
            gizmoRadius = Mathf.Max(0.01f, gizmoRadius);
            initialized = false;
        }

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            BuildDefaultConveyorPoses();
            BuildDefaultBoxPoses();
            ApplyOverrides();
            initialized = true;
        }

        private void BuildDefaultConveyorPoses()
        {
            for (int conveyorId = StudentConstants.MinConveyorId; conveyorId <= StudentConstants.MaxConveyorId; conveyorId++)
            {
                Vector3 anchor = GetArmAnchorPoint(conveyorId);
                conveyorPickPoses[conveyorId] = new StationPose
                {
                    stationId = conveyorId,
                    approachPos = anchor + conveyorApproachOffset,
                    actionPos = anchor + conveyorActionOffset,
                    retractPos = anchor + conveyorRetractOffset,
                    armMoveDuration = Mathf.Max(MinArmMoveDuration, conveyorArmMoveDuration)
                };
            }
        }

        private void BuildDefaultBoxPoses()
        {
            normalBoxBasePose = CreateBoxBasePose(StudentConstants.NormalBoxStationId);
            abnormalBoxBasePose = CreateBoxBasePose(StudentConstants.AbnormalBoxStationId);
        }

        private StationPose CreateBoxBasePose(int stationId)
        {
            Vector3 anchor = GetArmAnchorPoint(stationId);
            return new StationPose
            {
                stationId = stationId,
                approachPos = anchor + boxApproachOffset,
                actionPos = anchor + boxActionOffset,
                retractPos = anchor + boxRetractOffset,
                armMoveDuration = Mathf.Max(MinArmMoveDuration, boxArmMoveDuration)
            };
        }

        private Vector3 GetArmAnchorPoint(int stationId)
        {
            if (operatingStations != null && operatingStations.TryGetStation(stationId, out OperatingStations.Station station))
            {
                return station.ArmAnchorPoint;
            }

            return GetFallbackArmAnchorPoint(stationId);
        }

        private static Vector3 GetFallbackArmAnchorPoint(int stationId)
        {
            switch (stationId)
            {
                case 1:
                    return new Vector3(-9.5f, 1.5f, -7.6f);
                case 2:
                    return new Vector3(-9.5f, 1.5f, -3.6f);
                case 3:
                    return new Vector3(-9.5f, 1.5f, 0.4f);
                case 4:
                    return new Vector3(-9.5f, 1.5f, 4.4f);
                case 5:
                    return new Vector3(-9.5f, 1.5f, 8.4f);
                case 6:
                    return new Vector3(-7.1f, 1.5f, 12f);
                case 7:
                    return new Vector3(-3.1f, 1.5f, 12f);
                case 8:
                    return new Vector3(0.9f, 1.5f, 12f);
                case 9:
                    return new Vector3(4.9f, 1.5f, 12f);
                case 10:
                    return new Vector3(8.9f, 1.5f, 12f);
                case StudentConstants.NormalBoxStationId:
                    return new Vector3(0f, 0.5f, -8f);
                case StudentConstants.AbnormalBoxStationId:
                    return new Vector3(10.5f, 0.5f, 2.5f);
                default:
                    throw new ArgumentOutOfRangeException(nameof(stationId), stationId, "Unknown station id.");
            }
        }

        private void ApplyOverrides()
        {
            if (conveyorPickOverrides != null)
            {
                for (int i = 0; i < conveyorPickOverrides.Length; i++)
                {
                    StationPose overridePose = conveyorPickOverrides[i];
                    if (overridePose == null || !StudentConstants.IsConveyorId(overridePose.stationId))
                    {
                        continue;
                    }

                    conveyorPickPoses[overridePose.stationId] = NormalizePose(overridePose, overridePose.stationId);
                }
            }

            if (normalBoxBaseOverride != null && normalBoxBaseOverride.stationId == StudentConstants.NormalBoxStationId)
            {
                normalBoxBasePose = NormalizePose(normalBoxBaseOverride, StudentConstants.NormalBoxStationId);
            }

            if (abnormalBoxBaseOverride != null && abnormalBoxBaseOverride.stationId == StudentConstants.AbnormalBoxStationId)
            {
                abnormalBoxBasePose = NormalizePose(abnormalBoxBaseOverride, StudentConstants.AbnormalBoxStationId);
            }
        }

        private static StationPose NormalizePose(StationPose pose, int stationId)
        {
            StationPose normalized = ClonePose(pose);
            if (normalized == null)
            {
                return null;
            }

            normalized.stationId = stationId;
            normalized.armMoveDuration = Mathf.Max(MinArmMoveDuration, normalized.armMoveDuration);
            return normalized;
        }

        private static StationPose ClonePose(StationPose pose)
        {
            if (pose == null)
            {
                return null;
            }

            return new StationPose
            {
                stationId = pose.stationId,
                approachPos = pose.approachPos,
                actionPos = pose.actionPos,
                retractPos = pose.retractPos,
                armMoveDuration = pose.armMoveDuration
            };
        }

        [ContextMenu("PoseTable/Log Selected Station Pose")]
        private void LogSelectedStationPose()
        {
            StationPose pose = GetPoseForDebug(debugStationId);
            if (pose == null)
            {
                Debug.LogWarningFormat(this, "[PoseTable] Station {0} has no pose.", debugStationId);
                return;
            }

            Debug.LogFormat(
                this,
                "[PoseTable] Station {0}: approach={1}, action={2}, retract={3}, duration={4:0.00}",
                pose.stationId,
                pose.approachPos,
                pose.actionPos,
                pose.retractPos,
                pose.armMoveDuration);
        }

        [ContextMenu("PoseTable/Log All Poses")]
        private void LogAllPoses()
        {
            EnsureInitialized();

            for (int conveyorId = StudentConstants.MinConveyorId; conveyorId <= StudentConstants.MaxConveyorId; conveyorId++)
            {
                LogPose(conveyorPickPoses[conveyorId]);
            }

            LogPose(normalBoxBasePose);
            LogPose(abnormalBoxBasePose);
        }

        private void LogPose(StationPose pose)
        {
            if (pose == null)
            {
                return;
            }

            Debug.LogFormat(
                this,
                "[PoseTable] Station {0}: approach={1}, action={2}, retract={3}, duration={4:0.00}",
                pose.stationId,
                pose.approachPos,
                pose.actionPos,
                pose.retractPos,
                pose.armMoveDuration);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
            {
                return;
            }

            StationPose pose = GetPoseForDebug(debugStationId);
            if (pose == null)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(pose.approachPos, gizmoRadius);

            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(pose.actionPos, gizmoRadius);

            Gizmos.color = Color.green;
            Gizmos.DrawSphere(pose.retractPos, gizmoRadius);

            Gizmos.color = Color.white;
            Gizmos.DrawLine(pose.approachPos, pose.actionPos);
            Gizmos.DrawLine(pose.actionPos, pose.retractPos);
        }

        private StationPose GetPoseForDebug(int stationId)
        {
            EnsureInitialized();

            if (StudentConstants.IsConveyorId(stationId))
            {
                return conveyorPickPoses[stationId];
            }

            if (stationId == StudentConstants.NormalBoxStationId)
            {
                return normalBoxBasePose;
            }

            if (stationId == StudentConstants.AbnormalBoxStationId)
            {
                return abnormalBoxBasePose;
            }

            return null;
        }
    }
}
