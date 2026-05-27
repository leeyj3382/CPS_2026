using System;
using System.Collections;
using CPS.ICPBL.Common;
using CPS.Lab11.MobileManipulator;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    [DisallowMultipleComponent]
    public sealed class RobotAgent : MonoBehaviour, IRobotAgent
    {
        [Header("Robot References")]
        [SerializeField] private int robotId = StudentConstants.RobotAId;
        [SerializeField] private MonoBehaviour robotControllerComponent;
        [SerializeField] private SuctionGripper suctionGripper;
        [SerializeField] private global::ColorSensor colorSensor;
        [SerializeField] private global::ColorArea colorArea;

        [Header("Student Service References")]
        [SerializeField] private MonoBehaviour poseProviderComponent;
        [SerializeField] private MonoBehaviour palletizerComponent;
        [SerializeField] private MonoBehaviour colorClassifierComponent;
        [SerializeField] private MonoBehaviour lockManagerComponent;
        [SerializeField] private MonoBehaviour pathPlannerComponent;
        [SerializeField] private MonoBehaviour telemetryLoggerComponent;

        [Header("Mission Timing")]
        [SerializeField, Min(0.1f)] private float moveTimeoutSec =
            StudentConstants.DefaultMoveTimeoutSec;
        [SerializeField, Min(0.1f)] private float lockTimeoutSec =
            StudentConstants.DefaultLockTimeoutSec;
        [SerializeField, Min(0.1f)] private float gripReadyTimeoutSec =
            StudentConstants.DefaultGripReadyTimeoutSec;
        [SerializeField, Min(0f)] private float gripRetryWaitSec = 0.2f;
        [SerializeField, Min(0)] private int gripRetryCount = 1;
        [SerializeField, Min(0f)] private float colorRetryWaitSec = 0.1f;
        [SerializeField, Min(0)] private int colorRetryCount = 1;

        [Header("Debug")]
        [SerializeField] private bool logWithoutTelemetry = true;

        private IRobotController robotController;
        private IPoseProvider poseProvider;
        private IPalletizer palletizer;
        private IColorClassifier colorClassifier;
        private IResourceLockManager lockManager;
        private IPathPlanner pathPlanner;
        private ITelemetryLogger telemetryLogger;
        private Coroutine activeMission;
        private int currentStationId = StudentConstants.NoStationId;

        public int RobotId
        {
            get { return robotId; }
        }

        public RobotRuntimeState State { get; private set; } = RobotRuntimeState.Idle;

        public bool CanAcceptTask
        {
            get { return activeMission == null && State == RobotRuntimeState.Idle; }
        }

        public int CurrentStationId
        {
            get { return currentStationId; }
        }

        private void Awake()
        {
            ResolveSerializedReferences();
        }

        private void OnValidate()
        {
            moveTimeoutSec = Mathf.Max(0.1f, moveTimeoutSec);
            lockTimeoutSec = Mathf.Max(0.1f, lockTimeoutSec);
            gripReadyTimeoutSec = Mathf.Max(0.1f, gripReadyTimeoutSec);
            gripRetryWaitSec = Mathf.Max(0f, gripRetryWaitSec);
            gripRetryCount = Mathf.Max(0, gripRetryCount);
            colorRetryWaitSec = Mathf.Max(0f, colorRetryWaitSec);
            colorRetryCount = Mathf.Max(0, colorRetryCount);
        }

        public void Configure(
            IRobotController controller,
            SuctionGripper gripper,
            global::ColorArea area,
            IPoseProvider poseProvider,
            IPalletizer palletizer,
            IColorClassifier colorClassifier,
            IResourceLockManager lockManager,
            IPathPlanner pathPlanner,
            ITelemetryLogger telemetryLogger = null)
        {
            Configure(
                controller,
                gripper,
                null,
                area,
                poseProvider,
                palletizer,
                colorClassifier,
                lockManager,
                pathPlanner,
                telemetryLogger);
        }

        public void Configure(
            IRobotController controller,
            SuctionGripper gripper,
            global::ColorSensor sensor,
            IPoseProvider poseProvider,
            IPalletizer palletizer,
            IColorClassifier colorClassifier,
            IResourceLockManager lockManager,
            IPathPlanner pathPlanner,
            ITelemetryLogger telemetryLogger = null)
        {
            Configure(
                controller,
                gripper,
                sensor,
                null,
                poseProvider,
                palletizer,
                colorClassifier,
                lockManager,
                pathPlanner,
                telemetryLogger);
        }

        public void Configure(
            IRobotController controller,
            SuctionGripper gripper,
            global::ColorSensor sensor,
            global::ColorArea area,
            IPoseProvider poseProvider,
            IPalletizer palletizer,
            IColorClassifier colorClassifier,
            IResourceLockManager lockManager,
            IPathPlanner pathPlanner,
            ITelemetryLogger telemetryLogger = null)
        {
            robotController = controller;
            robotControllerComponent = controller as MonoBehaviour;
            suctionGripper = gripper;
            colorSensor = sensor;
            colorArea = area;
            this.poseProvider = poseProvider;
            this.palletizer = palletizer;
            this.colorClassifier = colorClassifier;
            this.lockManager = lockManager;
            this.pathPlanner = pathPlanner;
            this.telemetryLogger = telemetryLogger;

            poseProviderComponent = poseProvider as MonoBehaviour;
            palletizerComponent = palletizer as MonoBehaviour;
            colorClassifierComponent = colorClassifier as MonoBehaviour;
            lockManagerComponent = lockManager as MonoBehaviour;
            pathPlannerComponent = pathPlanner as MonoBehaviour;
            telemetryLoggerComponent = telemetryLogger as MonoBehaviour;

            if (robotController != null)
            {
                robotId = robotController.RobotId;
            }
        }

        public void ConfigureServices(
            IPoseProvider poseProvider,
            IPalletizer palletizer,
            IColorClassifier colorClassifier,
            IResourceLockManager lockManager,
            IPathPlanner pathPlanner,
            ITelemetryLogger telemetryLogger = null)
        {
            this.poseProvider = poseProvider;
            this.palletizer = palletizer;
            this.colorClassifier = colorClassifier;
            this.lockManager = lockManager;
            this.pathPlanner = pathPlanner;
            this.telemetryLogger = telemetryLogger;

            poseProviderComponent = poseProvider as MonoBehaviour;
            palletizerComponent = palletizer as MonoBehaviour;
            colorClassifierComponent = colorClassifier as MonoBehaviour;
            lockManagerComponent = lockManager as MonoBehaviour;
            pathPlannerComponent = pathPlanner as MonoBehaviour;
            telemetryLoggerComponent = telemetryLogger as MonoBehaviour;
        }

        public void StartMission(MissionRequest request, Action<MissionResult> onFinished)
        {
            if (activeMission != null || State != RobotRuntimeState.Idle)
            {
                MissionResult busyResult = CreateImmediateFailure(
                    request,
                    MissionFailureReason.Unknown,
                    "RobotAgent is already running a mission.");
                onFinished?.Invoke(busyResult);
                return;
            }

            ResolveSerializedReferences();
            activeMission = StartCoroutine(RunMission(request, onFinished));
        }

        private IEnumerator RunMission(MissionRequest request, Action<MissionResult> onFinished)
        {
            MissionResult result = null;
            var executor = new MissionExecutor(
                BuildDependencies(),
                BuildSettings());

            yield return executor.Execute(request, missionResult => result = missionResult);

            if (result == null)
            {
                result = CreateImmediateFailure(
                    request,
                    MissionFailureReason.Unknown,
                    "MissionExecutor did not return a result.");
            }

            activeMission = null;
            SetState(result.success ? RobotRuntimeState.Completed : RobotRuntimeState.Failed);
            onFinished?.Invoke(result);
            SetState(RobotRuntimeState.Idle);
        }

        private MissionExecutor.Dependencies BuildDependencies()
        {
            return new MissionExecutor.Dependencies
            {
                Controller = robotController,
                Gripper = new GripperAdapter(suctionGripper),
                ColorSensor = colorSensor,
                ColorArea = colorArea,
                PoseProvider = poseProvider,
                Palletizer = palletizer,
                ColorClassifier = colorClassifier,
                LockManager = lockManager,
                PathPlanner = pathPlanner,
                TelemetryLogger = telemetryLogger,
                GetCurrentStationId = () => currentStationId,
                SetCurrentStationId = stationId => currentStationId = stationId,
                SetState = SetState
            };
        }

        private MissionExecutor.Settings BuildSettings()
        {
            return new MissionExecutor.Settings
            {
                MoveTimeoutSec = moveTimeoutSec,
                LockTimeoutSec = lockTimeoutSec,
                GripReadyTimeoutSec = gripReadyTimeoutSec,
                GripRetryWaitSec = gripRetryWaitSec,
                GripRetryCount = gripRetryCount,
                ColorRetryWaitSec = colorRetryWaitSec,
                ColorRetryCount = colorRetryCount
            };
        }

        private void ResolveSerializedReferences()
        {
            if (robotController == null)
            {
                robotController = ResolveInterface<IRobotController>(robotControllerComponent)
                    ?? FindLocalInterface<IRobotController>();
            }

            if (poseProvider == null)
            {
                poseProvider = ResolveInterface<IPoseProvider>(poseProviderComponent);
            }

            if (palletizer == null)
            {
                palletizer = ResolveInterface<IPalletizer>(palletizerComponent);
            }

            if (colorClassifier == null)
            {
                colorClassifier = ResolveInterface<IColorClassifier>(colorClassifierComponent);
            }

            if (lockManager == null)
            {
                lockManager = ResolveInterface<IResourceLockManager>(lockManagerComponent);
            }

            if (pathPlanner == null)
            {
                pathPlanner = ResolveInterface<IPathPlanner>(pathPlannerComponent);
            }

            if (telemetryLogger == null)
            {
                telemetryLogger = ResolveInterface<ITelemetryLogger>(telemetryLoggerComponent);
            }

            if (robotController != null)
            {
                robotId = robotController.RobotId;
            }
        }

        private void SetState(RobotRuntimeState nextState)
        {
            if (State == nextState)
            {
                return;
            }

            State = nextState;
            string message = string.Format(
                "Robot {0} state={1} station={2}.",
                robotId,
                State,
                currentStationId);

            if (telemetryLogger != null)
            {
                telemetryLogger.LogMessage("Robot", message);
            }
            else if (logWithoutTelemetry)
            {
                Debug.Log(string.Format("[RobotAgent] {0}", message), this);
            }
        }

        private MissionResult CreateImmediateFailure(
            MissionRequest request,
            MissionFailureReason reason,
            string message)
        {
            int taskId = request != null ? request.taskId : StudentConstants.NoTaskId;
            int requestRobotId = request != null ? request.robotId : robotId;
            int conveyorId = request != null ? request.conveyorId : StudentConstants.NoStationId;
            return new MissionResult
            {
                taskId = taskId,
                robotId = requestRobotId,
                conveyorId = conveyorId,
                success = false,
                classificationResult = ClassificationResult.Unknown,
                destinationStationId = StudentConstants.NoStationId,
                failureReason = reason,
                message = message,
                startedAt = Time.time,
                finishedAt = Time.time
            };
        }

        private static T ResolveInterface<T>(MonoBehaviour component)
            where T : class
        {
            return component as T;
        }

        private T FindLocalInterface<T>()
            where T : class
        {
            MonoBehaviour[] components = GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is T result)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
