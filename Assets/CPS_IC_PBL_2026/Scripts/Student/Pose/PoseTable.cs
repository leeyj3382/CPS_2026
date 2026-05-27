using System;
using CPS.ICPBL.Common;
using CPS.ICPBL.Environment;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    public class PoseTable : MonoBehaviour, IPoseProvider
    {
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

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnValidate()
        {
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
                    armMoveDuration = conveyorArmMoveDuration
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
                armMoveDuration = boxArmMoveDuration
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

                    conveyorPickPoses[overridePose.stationId] = ClonePose(overridePose);
                }
            }

            if (normalBoxBaseOverride != null && normalBoxBaseOverride.stationId == StudentConstants.NormalBoxStationId)
            {
                normalBoxBasePose = ClonePose(normalBoxBaseOverride);
            }

            if (abnormalBoxBaseOverride != null && abnormalBoxBaseOverride.stationId == StudentConstants.AbnormalBoxStationId)
            {
                abnormalBoxBasePose = ClonePose(abnormalBoxBaseOverride);
            }
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
    }
}
