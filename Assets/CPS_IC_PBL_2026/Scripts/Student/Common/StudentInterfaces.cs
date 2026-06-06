using System;
using System.Collections.Generic;
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

    public interface IRobotStationTracker
    {
        int CurrentStationId { get; }
    }

    public interface IRobotPrepositioner
    {
        bool TryPrepositionToStation(int stationId);
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
        IReadOnlyList<Vector3> BuildBaseRoute(
            int robotId,
            int fromStationId,
            int toStationId,
            Vector3 from,
            Vector3 to);

        IReadOnlyList<Vector3> BuildYieldCandidates(
            int robotId,
            Vector3 from,
            Vector3 originalTarget);

        bool IsBasePathBlocked(
            int robotId,
            Vector3 from,
            Vector3 to,
            out int blockingRobotId);

        bool IsBasePathBlocked(
            int robotId,
            int targetStationId,
            Vector3 from,
            Vector3 to,
            out int blockingRobotId,
            out bool waitForSameBox,
            out bool preferDetour);
    }

    public interface IPathReservationManager
    {
        bool TryReserveBaseSegment(
            int robotId,
            int taskId,
            Vector3 from,
            Vector3 to,
            float priority,
            out PathReservationToken token,
            out int blockingRobotId,
            out int blockingTaskId);

        void ReleaseBaseSegment(PathReservationToken token);
    }

    public interface IPathTrafficManager
    {
        void RegisterActiveBasePath(
            int robotId,
            int taskId,
            Vector3 from,
            Vector3 to,
            bool hasPayload);

        void ClearActiveBasePath(int robotId, int taskId);
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
