using System;
using CPS.ICPBL.Common;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    public interface IRobotAgent
    {
        int RobotId { get; }
        RobotRuntimeState State { get; }
        bool CanAcceptTask { get; }
        void StartMission(MissionRequest request, Action<MissionResult> onFinished);
    }

    public interface ITaskAllocator
    {
        WorkTask SelectBestTask(
            ConveyorSnapshot[] conveyors,
            StudentRobotSnapshot robot,
            WorkTask[] pendingTasks
        );
    }

    public interface IPoseProvider
    {
        StationPose GetConveyorPickPose(int conveyorId);
        StationPose GetBoxBasePose(BoxType boxType);
    }

    public interface IPalletizer
    {
        BoxSlotPose ReserveNextSlot(BoxType boxType, int robotId, int taskId);
        void CommitSlot(int taskId);
        void ReleaseSlot(int taskId);
    }

    public interface IColorClassifier
    {
        ColorClassificationResult Classify(Color sensedColor);
    }

    public interface IResourceLockManager
    {
        bool TryAcquire(ResourceKey key, int robotId, int taskId, out ResourceLockToken token);
        void Release(ResourceLockToken token);
        bool IsLocked(ResourceKey key);
    }

    public interface IPathPlanner
    {
        bool RequiresCentralZone(int robotId, int fromStationId, int toStationId);
    }

    public interface ITelemetryLogger
    {
        void LogTaskCreated(WorkTask task);
        void LogTaskAssigned(WorkTask task, int robotId);
        void LogMissionResult(MissionResult result);
        void LogLock(string action, ResourceKey key, int robotId, int taskId);
        void LogMessage(string category, string message);
    }
}
