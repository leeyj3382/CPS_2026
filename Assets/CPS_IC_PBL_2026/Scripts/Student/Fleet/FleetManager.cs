using System;
using System.Collections.Generic;
using CPS.ICPBL.Common;
using CPS.ICPBL.Environment;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    /// <summary>
    /// Owns Fleet scheduling state and dispatches non-preemptive missions to available robots.
    /// Motion, classification, palletizing, and physical locks remain in other slices.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FleetManager : MonoBehaviour
    {
        [Header("Official Scene References")]
        [SerializeField] private EnvironmentInfo environmentInfoComponent;
        [SerializeField] private OperatingStations operatingStations;

        [Header("Scheduling")]
        [SerializeField, Min(0.05f)] private float pollingIntervalSec = 0.25f;
        [SerializeField, Min(0)] private int maxRetryCount = 1;
        [SerializeField] private bool runAutomatically = true;
        [SerializeField] private bool enableDistanceTieBreaker;
        [SerializeField] private bool logEventsWithoutTelemetry = true;
        [SerializeField, Min(0f)] private float postPickConveyorCooldownSec = 0f;

        [Header("Predictive Scheduling")]
        [SerializeField] private bool enableAnticipatedConveyorTasks = true;
        [SerializeField, Min(0f)] private float anticipatedTaskLookaheadSec = 4f;
        [SerializeField, Min(0f)] private float anticipatedTaskGraceSec = 4f;

        private readonly HashSet<int> reservedConveyorIds = new HashSet<int>();
        private readonly Dictionary<int, float> lastAssignedAtByConveyor =
            new Dictionary<int, float>();
        private readonly Dictionary<int, float> conveyorSchedulingAvailableAt =
            new Dictionary<int, float>();
        private readonly Dictionary<int, WorkTask> activeTaskByConveyor =
            new Dictionary<int, WorkTask>();
        private readonly Dictionary<int, StudentRobotSnapshot> snapshotByRobot =
            new Dictionary<int, StudentRobotSnapshot>();
        private readonly List<IRobotAgent> robotAgents = new List<IRobotAgent>(2);
        private readonly List<WorkTask> tasks = new List<WorkTask>();

        private IEnvironmentInfo environmentInfo;
        private ITelemetryLogger telemetryLogger;
        private EnvironmentScanner environmentScanner;
        private TaskAllocator taskAllocator;
        private float nextPollingAt;
        private int nextTaskId = 1;

        public IReadOnlyList<WorkTask> Tasks => tasks;
        public IReadOnlyCollection<int> ReservedConveyorIds => reservedConveyorIds;
        public ConveyorSnapshot[] LatestSnapshots { get; private set; } =
            Array.Empty<ConveyorSnapshot>();
        public bool IsConfigured => environmentScanner != null;

        private void Awake()
        {
            if (environmentInfoComponent != null)
            {
                ConfigureEnvironment(environmentInfoComponent, operatingStations, null);
            }
        }

        private void Update()
        {
            if (!runAutomatically || environmentScanner == null)
            {
                return;
            }

            float currentTime = environmentInfo.CurrentTime;
            if (currentTime < nextPollingAt)
            {
                return;
            }

            RunSchedulingCycle();
            nextPollingAt = currentTime + pollingIntervalSec;
        }

        private void OnValidate()
        {
            pollingIntervalSec = Mathf.Max(0.05f, pollingIntervalSec);
            maxRetryCount = Mathf.Max(0, maxRetryCount);
            postPickConveyorCooldownSec = Mathf.Max(0f, postPickConveyorCooldownSec);
            anticipatedTaskLookaheadSec = Mathf.Max(0f, anticipatedTaskLookaheadSec);
            anticipatedTaskGraceSec = Mathf.Max(0f, anticipatedTaskGraceSec);
        }

        /// <summary>
        /// Runtime wiring entry point for StudentBootstrap once B and D implementations exist.
        /// Null robot or telemetry dependencies are allowed while those slices are absent.
        /// </summary>
        public void Configure(
            IEnvironmentInfo info,
            OperatingStations stationData,
            IRobotAgent robotA,
            IRobotAgent robotB,
            ITelemetryLogger logger = null)
        {
            ConfigureEnvironment(info, stationData, logger);
            ConfigureRobotAgents(robotA, robotB);
        }

        public void ConfigureEnvironment(
            IEnvironmentInfo info,
            OperatingStations stationData = null,
            ITelemetryLogger logger = null)
        {
            environmentInfo = info ?? throw new ArgumentNullException(nameof(info));
            operatingStations = stationData;
            telemetryLogger = logger;
            environmentScanner = new EnvironmentScanner(environmentInfo);
            taskAllocator = new TaskAllocator(
                enableDistanceTieBreaker ? operatingStations : null);
            nextPollingAt = 0f;
        }

        public void ConfigureRobotAgents(IRobotAgent robotA, IRobotAgent robotB)
        {
            robotAgents.Clear();
            RegisterRobotAgent(robotA);
            RegisterRobotAgent(robotB);
        }

        public void RegisterRobotAgent(IRobotAgent robotAgent)
        {
            if (robotAgent == null)
            {
                return;
            }

            if (!StudentConstants.IsRobotId(robotAgent.RobotId))
            {
                throw new ArgumentException("Robot id must be RobotAId or RobotBId.", nameof(robotAgent));
            }

            for (int i = 0; i < robotAgents.Count; i++)
            {
                if (robotAgents[i].RobotId == robotAgent.RobotId)
                {
                    throw new ArgumentException("A robot agent with this id is already registered.", nameof(robotAgent));
                }
            }

            robotAgents.Add(robotAgent);
        }

        /// <summary>
        /// Updates optional location data used only to break equal-deadline selections.
        /// </summary>
        public void UpdateRobotSnapshot(StudentRobotSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            int robotId = snapshot.baseSnapshot.RobotId;
            if (!StudentConstants.IsRobotId(robotId))
            {
                throw new ArgumentException("Snapshot robot id must be RobotAId or RobotBId.", nameof(snapshot));
            }

            snapshotByRobot[robotId] = snapshot;
        }

        /// <summary>
        /// Performs one observation, task refresh, and dispatch pass.
        /// Agent dependencies may be absent; pending tasks are still produced for inspection.
        /// </summary>
        public ConveyorSnapshot[] RunSchedulingCycle()
        {
            if (environmentScanner == null)
            {
                throw new InvalidOperationException("FleetManager must be configured with IEnvironmentInfo before scheduling.");
            }

            LatestSnapshots = environmentScanner.Scan(
                reservedConveyorIds,
                lastAssignedAtByConveyor);

            RefreshPendingTasks(LatestSnapshots);
            DispatchAvailableRobots(LatestSnapshots);
            return LatestSnapshots;
        }

        private void RefreshPendingTasks(ConveyorSnapshot[] snapshots)
        {
            bool hasVisibleQueue = HasVisibleQueue(snapshots);
            for (int i = 0; i < snapshots.Length; i++)
            {
                ConveyorSnapshot snapshot = snapshots[i];
                if (snapshot == null)
                {
                    continue;
                }

                if (snapshot.queueLength <= 0)
                {
                    if (CanCreateAnticipatedTask(snapshot, hasVisibleQueue))
                    {
                        CreateTask(snapshot, true);
                    }
                    else
                    {
                        CancelEmptyPendingTask(snapshot.conveyorId);
                    }

                    continue;
                }

                MarkTaskAvailableIfAnticipated(snapshot.conveyorId);

                if (!IsConveyorSchedulingAvailable(snapshot.conveyorId))
                {
                    continue;
                }

                if (snapshot.isReserved || HasActiveTask(snapshot.conveyorId))
                {
                    continue;
                }

                CreateTask(snapshot, false);
            }
        }

        private bool HasVisibleQueue(ConveyorSnapshot[] snapshots)
        {
            for (int i = 0; i < snapshots.Length; i++)
            {
                ConveyorSnapshot snapshot = snapshots[i];
                if (snapshot != null && snapshot.queueLength > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanCreateAnticipatedTask(
            ConveyorSnapshot snapshot,
            bool hasVisibleQueue)
        {
            if (!enableAnticipatedConveyorTasks
                || !hasVisibleQueue
                || snapshot == null
                || snapshot.queueLength > 0
                || snapshot.isReserved
                || HasActiveTask(snapshot.conveyorId)
                || !IsConveyorSchedulingAvailable(snapshot.conveyorId))
            {
                return false;
            }

            float timeUntilNextProduction = snapshot.nextProductionAt - environmentInfo.CurrentTime;
            return timeUntilNextProduction >= 0f
                && timeUntilNextProduction <= anticipatedTaskLookaheadSec;
        }

        private WorkTask CreateTask(ConveyorSnapshot snapshot, bool anticipated)
        {
            WorkTask task = new WorkTask
            {
                taskId = nextTaskId++,
                conveyorId = snapshot.conveyorId,
                createdAt = environmentInfo.CurrentTime,
                anticipated = anticipated,
                expectedAvailableAt = anticipated
                    ? snapshot.nextProductionAt
                    : environmentInfo.CurrentTime,
                status = TaskStatus.Pending
            };

            tasks.Add(task);
            activeTaskByConveyor[task.conveyorId] = task;
            LogTaskCreated(task);
            return task;
        }

        private void MarkTaskAvailableIfAnticipated(int conveyorId)
        {
            if (!activeTaskByConveyor.TryGetValue(conveyorId, out WorkTask task)
                || task == null
                || !task.anticipated)
            {
                return;
            }

            task.anticipated = false;
            task.expectedAvailableAt = environmentInfo.CurrentTime;
        }

        private bool HasActiveTask(int conveyorId)
        {
            if (!activeTaskByConveyor.TryGetValue(conveyorId, out WorkTask task))
            {
                return false;
            }

            return task.status == TaskStatus.Pending
                || task.status == TaskStatus.Reserved
                || task.status == TaskStatus.Running;
        }

        private bool IsConveyorSchedulingAvailable(int conveyorId)
        {
            if (!conveyorSchedulingAvailableAt.TryGetValue(conveyorId, out float availableAt))
            {
                return true;
            }

            if (environmentInfo.CurrentTime < availableAt)
            {
                return false;
            }

            conveyorSchedulingAvailableAt.Remove(conveyorId);
            return true;
        }

        private void CancelEmptyPendingTask(int conveyorId)
        {
            if (!activeTaskByConveyor.TryGetValue(conveyorId, out WorkTask task))
            {
                return;
            }

            if (task.status != TaskStatus.Pending
                || task.assignedRobotId != StudentConstants.UnassignedRobotId)
            {
                return;
            }

            if (task.anticipated
                && environmentInfo.CurrentTime <= task.expectedAvailableAt + anticipatedTaskGraceSec)
            {
                return;
            }

            task.status = TaskStatus.Cancelled;
            activeTaskByConveyor.Remove(conveyorId);
            LogMessage("Scheduling", string.Format(
                "Cancelled task={0} because conveyor={1} queue is empty.",
                task.taskId,
                conveyorId));
        }

        private void DispatchAvailableRobots(ConveyorSnapshot[] snapshots)
        {
            for (int i = 0; i < robotAgents.Count; i++)
            {
                IRobotAgent robotAgent = robotAgents[i];
                if (!CanDispatch(robotAgent))
                {
                    continue;
                }

                WorkTask[] pendingTasks = BuildPendingTaskArray();
                if (pendingTasks.Length == 0)
                {
                    return;
                }

                StudentRobotSnapshot robotSnapshot = GetRobotSnapshot(robotAgent);
                WorkTask selectedTask = taskAllocator.SelectBestTask(
                    snapshots,
                    robotSnapshot,
                    pendingTasks);

                if (selectedTask == null)
                {
                    continue;
                }

                DispatchTask(robotAgent, selectedTask);
            }
        }

        private bool CanDispatch(IRobotAgent robotAgent)
        {
            return robotAgent != null
                && robotAgent.CanAcceptTask
                && FindInFlightTask(robotAgent.RobotId) == null;
        }

        private WorkTask[] BuildPendingTaskArray()
        {
            var pendingTasks = new List<WorkTask>();
            for (int i = 0; i < tasks.Count; i++)
            {
                WorkTask task = tasks[i];
                if (task.status == TaskStatus.Pending
                    && task.assignedRobotId == StudentConstants.UnassignedRobotId)
                {
                    pendingTasks.Add(task);
                }
            }

            return pendingTasks.ToArray();
        }

        private StudentRobotSnapshot GetRobotSnapshot(IRobotAgent robotAgent)
        {
            if (!snapshotByRobot.TryGetValue(robotAgent.RobotId, out StudentRobotSnapshot snapshot))
            {
                snapshot = new StudentRobotSnapshot();
                snapshotByRobot[robotAgent.RobotId] = snapshot;
            }

            WorkTask inFlightTask = FindInFlightTask(robotAgent.RobotId);
            snapshot.baseSnapshot.RobotId = robotAgent.RobotId;
            snapshot.baseSnapshot.IsBusy = !robotAgent.CanAcceptTask || inFlightTask != null;
            snapshot.baseSnapshot.TargetConveyorId =
                inFlightTask != null ? inFlightTask.conveyorId : (int?)null;
            snapshot.state = robotAgent.State;
            snapshot.currentTaskId =
                inFlightTask != null ? inFlightTask.taskId : StudentConstants.NoTaskId;
            return snapshot;
        }

        private WorkTask FindInFlightTask(int robotId)
        {
            for (int i = 0; i < tasks.Count; i++)
            {
                WorkTask task = tasks[i];
                if (task.assignedRobotId == robotId
                    && (task.status == TaskStatus.Reserved || task.status == TaskStatus.Running))
                {
                    return task;
                }
            }

            return null;
        }

        private void DispatchTask(IRobotAgent robotAgent, WorkTask task)
        {
            int robotId = robotAgent.RobotId;
            float assignedAt = environmentInfo.CurrentTime;

            task.assignedRobotId = robotId;
            task.assignedAt = assignedAt;
            task.status = TaskStatus.Reserved;
            reservedConveyorIds.Add(task.conveyorId);
            lastAssignedAtByConveyor[task.conveyorId] = assignedAt;
            MarkSnapshotReservation(task.conveyorId, true);
            LogTaskAssigned(task, robotId);

            var request = new MissionRequest
            {
                taskId = task.taskId,
                robotId = robotId,
                conveyorId = task.conveyorId,
                requestTime = assignedAt,
                timeoutSec = StudentConstants.DefaultMissionTimeoutSec,
                onProgress = progress => OnMissionProgress(task.taskId, progress)
            };

            task.status = TaskStatus.Running;
            try
            {
                robotAgent.StartMission(
                    request,
                    result => OnMissionFinished(task.taskId, result));
            }
            catch (Exception exception)
            {
                OnMissionFinished(task.taskId, CreateDispatchFailure(task, exception.Message));
            }
        }

        private void OnMissionProgress(int expectedTaskId, MissionProgressEvent progress)
        {
            if (progress == null || progress.type != MissionProgressType.ConveyorPicked)
            {
                return;
            }

            WorkTask task = FindTaskById(expectedTaskId);
            if (task == null
                || (task.status != TaskStatus.Reserved && task.status != TaskStatus.Running))
            {
                return;
            }

            if (progress.taskId != task.taskId
                || progress.robotId != task.assignedRobotId
                || progress.conveyorId != task.conveyorId)
            {
                LogMessage("Scheduling", string.Format(
                    "Ignored mismatched progress event task={0} robot={1} conveyor={2}.",
                    progress.taskId,
                    progress.robotId,
                    progress.conveyorId));
                return;
            }

            MarkConveyorPicked(task);
        }

        private void OnMissionFinished(int expectedTaskId, MissionResult result)
        {
            try
            {
                HandleMissionFinished(expectedTaskId, result);
            }
            catch (Exception exception)
            {
                WorkTask task = FindTaskById(expectedTaskId);
                if (task != null)
                {
                    ReleaseReservation(task.conveyorId);
                    task.status = TaskStatus.Failed;
                    task.assignedRobotId = StudentConstants.UnassignedRobotId;
                    RemoveActiveTaskIfMatches(task.conveyorId, task);
                }

                LogMessage("Scheduling", string.Format(
                    "Mission finish handling failed task={0}: {1}",
                    expectedTaskId,
                    exception.Message));
            }
        }

        private void HandleMissionFinished(int expectedTaskId, MissionResult result)
        {
            WorkTask task = FindTaskById(expectedTaskId);
            if (task == null
                || (task.status != TaskStatus.Reserved && task.status != TaskStatus.Running))
            {
                LogMessage("Scheduling", string.Format(
                    "Ignored callback for inactive task={0}.",
                    expectedTaskId));
                return;
            }

            if (result == null
                || result.taskId != task.taskId
                || result.robotId != task.assignedRobotId)
            {
                result = CreateDispatchFailure(task, "Mission callback did not match assigned task and robot.");
            }

            if (!task.conveyorPicked)
            {
                ReleaseReservation(task.conveyorId);
            }

            LogMissionResult(result);

            if (result.success)
            {
                task.status = TaskStatus.Completed;
                RemoveActiveTaskIfMatches(task.conveyorId, task);
                LogMessage("Scheduling", string.Format("Completed task={0}.", task.taskId));
                return;
            }

            if (task.conveyorPicked)
            {
                task.status = TaskStatus.Failed;
                RemoveActiveTaskIfMatches(task.conveyorId, task);
                LogMessage("Scheduling", string.Format(
                    "Failed task={0} after conveyor pick; not retrying removed conveyor item.",
                    task.taskId));
                return;
            }

            task.retryCount++;
            if (task.retryCount <= maxRetryCount)
            {
                task.status = TaskStatus.Pending;
                task.assignedRobotId = StudentConstants.UnassignedRobotId;
                task.assignedAt = 0f;
                LogMessage("Scheduling", string.Format(
                    "Retry pending task={0}, retryCount={1}.",
                    task.taskId,
                    task.retryCount));
                return;
            }

            task.status = TaskStatus.Failed;
            RemoveActiveTaskIfMatches(task.conveyorId, task);
            LogMessage("Scheduling", string.Format(
                "Failed task={0} after retryCount={1}.",
                task.taskId,
                task.retryCount));
        }

        private void MarkConveyorPicked(WorkTask task)
        {
            if (task == null || task.conveyorPicked)
            {
                return;
            }

            task.conveyorPicked = true;
            ReleaseReservation(task.conveyorId);
            RemoveActiveTaskIfMatches(task.conveyorId, task);

            if (postPickConveyorCooldownSec > 0f)
            {
                conveyorSchedulingAvailableAt[task.conveyorId] =
                    environmentInfo.CurrentTime + postPickConveyorCooldownSec;
            }

            LogMessage("Scheduling", string.Format(
                "Released conveyor={0} after pick task={1}; cooldown={2:0.00}s.",
                task.conveyorId,
                task.taskId,
                postPickConveyorCooldownSec));
        }

        private void RemoveActiveTaskIfMatches(int conveyorId, WorkTask task)
        {
            if (activeTaskByConveyor.TryGetValue(conveyorId, out WorkTask activeTask)
                && activeTask == task)
            {
                activeTaskByConveyor.Remove(conveyorId);
            }
        }

        private WorkTask FindTaskById(int taskId)
        {
            for (int i = 0; i < tasks.Count; i++)
            {
                if (tasks[i].taskId == taskId)
                {
                    return tasks[i];
                }
            }

            return null;
        }

        private MissionResult CreateDispatchFailure(WorkTask task, string message)
        {
            return new MissionResult
            {
                taskId = task.taskId,
                robotId = task.assignedRobotId,
                conveyorId = task.conveyorId,
                success = false,
                failureReason = MissionFailureReason.Unknown,
                message = message,
                startedAt = task.assignedAt,
                finishedAt = environmentInfo.CurrentTime
            };
        }

        private void ReleaseReservation(int conveyorId)
        {
            reservedConveyorIds.Remove(conveyorId);
            MarkSnapshotReservation(conveyorId, false);
        }

        private void MarkSnapshotReservation(int conveyorId, bool isReserved)
        {
            for (int i = 0; i < LatestSnapshots.Length; i++)
            {
                ConveyorSnapshot snapshot = LatestSnapshots[i];
                if (snapshot != null && snapshot.conveyorId == conveyorId)
                {
                    snapshot.isReserved = isReserved;
                    return;
                }
            }
        }

        private void LogTaskCreated(WorkTask task)
        {
            if (telemetryLogger != null)
            {
                telemetryLogger.LogTaskCreated(task);
                return;
            }

            LogFallback(string.Format(
                "Created task={0} conveyor={1}.",
                task.taskId,
                task.conveyorId));
        }

        private void LogTaskAssigned(WorkTask task, int robotId)
        {
            if (telemetryLogger != null)
            {
                telemetryLogger.LogTaskAssigned(task, robotId);
                return;
            }

            LogFallback(string.Format(
                "Assigned task={0} conveyor={1} robot={2}.",
                task.taskId,
                task.conveyorId,
                robotId));
        }

        private void LogMissionResult(MissionResult result)
        {
            if (telemetryLogger != null)
            {
                telemetryLogger.LogMissionResult(result);
                return;
            }

            LogFallback(string.Format(
                "Result task={0} robot={1} success={2} reason={3}.",
                result.taskId,
                result.robotId,
                result.success,
                result.failureReason));
        }

        private void LogMessage(string category, string message)
        {
            if (telemetryLogger != null)
            {
                telemetryLogger.LogMessage(category, message);
                return;
            }

            LogFallback(string.Format("{0}: {1}", category, message));
        }

        private void LogFallback(string message)
        {
            if (logEventsWithoutTelemetry)
            {
                Debug.Log(string.Format("[FleetManager] {0}", message), this);
            }
        }
    }
}
