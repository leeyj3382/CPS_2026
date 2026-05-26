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

        private readonly HashSet<int> reservedConveyorIds = new HashSet<int>();
        private readonly Dictionary<int, float> lastAssignedAtByConveyor =
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
            for (int i = 0; i < snapshots.Length; i++)
            {
                ConveyorSnapshot snapshot = snapshots[i];
                if (snapshot == null)
                {
                    continue;
                }

                if (snapshot.queueLength <= 0)
                {
                    CancelEmptyPendingTask(snapshot.conveyorId);
                    continue;
                }

                if (snapshot.isReserved || HasActiveTask(snapshot.conveyorId))
                {
                    continue;
                }

                WorkTask task = new WorkTask
                {
                    taskId = nextTaskId++,
                    conveyorId = snapshot.conveyorId,
                    createdAt = environmentInfo.CurrentTime,
                    status = TaskStatus.Pending
                };

                tasks.Add(task);
                activeTaskByConveyor[task.conveyorId] = task;
                LogTaskCreated(task);
            }
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
                timeoutSec = StudentConstants.DefaultMissionTimeoutSec
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

        private void OnMissionFinished(int expectedTaskId, MissionResult result)
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

            ReleaseReservation(task.conveyorId);
            LogMissionResult(result);

            if (result.success)
            {
                task.status = TaskStatus.Completed;
                activeTaskByConveyor.Remove(task.conveyorId);
                LogMessage("Scheduling", string.Format("Completed task={0}.", task.taskId));
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
            activeTaskByConveyor.Remove(task.conveyorId);
            LogMessage("Scheduling", string.Format(
                "Failed task={0} after retryCount={1}.",
                task.taskId,
                task.retryCount));
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
