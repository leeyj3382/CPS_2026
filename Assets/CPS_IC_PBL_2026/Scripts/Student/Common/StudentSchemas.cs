using System;
using CPS.ICPBL.Common;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    [Serializable]
    public class ConveyorSnapshot
    {
        public int conveyorId;
        public int queueLength;
        public float productionPeriod;
        public float nextProductionAt;
        public float lastAssignedAt;
        public bool isReserved;
    }

    [Serializable]
    public class StudentRobotSnapshot
    {
        public RobotSnapshot baseSnapshot;
        public RobotRuntimeState state;
        public int currentStationId;
        public int currentTaskId = StudentConstants.NoTaskId;
    }

    [Serializable]
    public class WorkTask
    {
        public int taskId;
        public int conveyorId;
        public int assignedRobotId = StudentConstants.UnassignedRobotId;
        public float createdAt;
        public float assignedAt;
        public float priorityScore;
        public TaskStatus status;
        public int retryCount;
        public string debugReason;
    }

    [Serializable]
    public class MissionRequest
    {
        public int taskId;
        public int robotId;
        public int conveyorId;
        public float requestTime;
        public float timeoutSec = StudentConstants.DefaultMissionTimeoutSec;
    }

    [Serializable]
    public class MissionResult
    {
        public int taskId;
        public int robotId;
        public int conveyorId;
        public bool success;
        public ClassificationResult classificationResult;
        public int destinationStationId;
        public MissionFailureReason failureReason;
        public string message;
        public float startedAt;
        public float finishedAt;
    }

    [Serializable]
    public class StationPose
    {
        public int stationId;
        public Vector3 approachPos;
        public Vector3 actionPos;
        public Vector3 retractPos;
        public float armMoveDuration = StudentConstants.DefaultArmMoveDurationSec;
    }

    [Serializable]
    public class BoxSlotPose
    {
        public BoxType boxType;
        public int stationId;
        public int slotIndex;
        public Vector3 approachPos;
        public Vector3 placePos;
        public Vector3 retractPos;
        public bool reserved;
        public int reservedByTaskId = StudentConstants.NoTaskId;
    }

    [Serializable]
    public class ColorClassificationResult
    {
        public ClassificationResult result;
        public Color sensedColor;
        public float blueDistance;
        public float redDistance;
        public bool reliable;
        public string message;
    }

    [Serializable]
    public struct ResourceKey : IEquatable<ResourceKey>
    {
        public LockResourceType type;
        public int id;

        public ResourceKey(LockResourceType type, int id)
        {
            this.type = type;
            this.id = id;
        }

        public bool Equals(ResourceKey other)
        {
            return type == other.type && id == other.id;
        }

        public override bool Equals(object obj)
        {
            return obj is ResourceKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)type * 397) ^ id;
            }
        }

        public override string ToString()
        {
            return string.Format("{0}:{1}", type, id);
        }

        public static bool operator ==(ResourceKey left, ResourceKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ResourceKey left, ResourceKey right)
        {
            return !left.Equals(right);
        }
    }

    [Serializable]
    public class ResourceLockToken
    {
        public ResourceKey key;
        public int robotId;
        public int taskId;
        public float acquiredAt;
    }
}
