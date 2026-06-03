using System;
using System.Collections;
using CPS.ICPBL.Common;
using CPS.ICPBL.Environment;
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
        [SerializeField] private OperatingStations operatingStations;
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
        [SerializeField, Min(0f)] private float releaseConfirmTimeoutSec = 0.5f;
        [SerializeField, Min(0f)] private float colorRetryWaitSec = 0.1f;
        [SerializeField, Min(0)] private int colorRetryCount = 1;

        [Header("Box Fine Positioning")]
        [SerializeField] private bool useBoxFinePositioning = true;
        [SerializeField] private Vector3 normalBoxFineBasePosition = new Vector3(0f, 0f, -7f);
        [SerializeField] private Vector3 abnormalBoxFineBasePosition = new Vector3(9f, 0f, 2.5f);
        [SerializeField, Min(0.1f)] private float boxFineMoveTimeoutSec = 6f;

        [Header("Arm IK Guard")]
        [SerializeField] private bool useSafeArmMotion = true;
        [SerializeField] private bool allowPositionOnlyIkFallback = true;
        [SerializeField, Min(0.001f)] private float positionOnlyIkFallbackTolerance = 0.04f;
        [SerializeField, Min(0f)] private float maxFallbackOrientationErrorDeg = 25f;

        [Header("Debug")]
        [SerializeField] private bool logWithoutTelemetry = true;

        [Header("Manual Test")]
        [SerializeField] private int debugConveyorId = StudentConstants.MinConveyorId;
        [SerializeField] private int debugTaskId = 9001;
        [SerializeField] private bool logDebugMissionResult = true;

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
            get
            {
                return activeMission == null
                    && State == RobotRuntimeState.Idle
                    && (suctionGripper == null || !suctionGripper.IsHolding);
            }
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
            releaseConfirmTimeoutSec = Mathf.Max(0f, releaseConfirmTimeoutSec);
            colorRetryWaitSec = Mathf.Max(0f, colorRetryWaitSec);
            colorRetryCount = Mathf.Max(0, colorRetryCount);
            boxFineMoveTimeoutSec = Mathf.Max(0.1f, boxFineMoveTimeoutSec);
            positionOnlyIkFallbackTolerance = Mathf.Max(0.001f, positionOnlyIkFallbackTolerance);
            maxFallbackOrientationErrorDeg = Mathf.Max(0f, maxFallbackOrientationErrorDeg);
            debugConveyorId = Mathf.Clamp(
                debugConveyorId,
                StudentConstants.MinConveyorId,
                StudentConstants.MaxConveyorId);
            debugTaskId = Mathf.Max(1, debugTaskId);
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
            ITelemetryLogger telemetryLogger = null,
            OperatingStations stationData = null)
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
                telemetryLogger,
                stationData);
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
            ITelemetryLogger telemetryLogger = null,
            OperatingStations stationData = null)
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
                telemetryLogger,
                stationData);
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
            ITelemetryLogger telemetryLogger = null,
            OperatingStations stationData = null)
        {
            suctionGripper = gripper;
            colorSensor = sensor;
            colorArea = area;
            this.poseProvider = poseProvider;
            this.palletizer = palletizer;
            this.colorClassifier = colorClassifier;
            this.lockManager = lockManager;
            this.pathPlanner = pathPlanner;
            this.telemetryLogger = telemetryLogger;
            operatingStations = stationData;
            AssignRobotController(controller);

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
            ITelemetryLogger telemetryLogger = null,
            OperatingStations stationData = null)
        {
            this.poseProvider = poseProvider;
            this.palletizer = palletizer;
            this.colorClassifier = colorClassifier;
            this.lockManager = lockManager;
            this.pathPlanner = pathPlanner;
            this.telemetryLogger = telemetryLogger;
            operatingStations = stationData;

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

        [ContextMenu("RobotAgent/Start Debug Mission")]
        private void StartDebugMission()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[RobotAgent] Debug mission can only run in Play Mode.", this);
                return;
            }

            if (!CanAcceptTask)
            {
                Debug.LogWarning("[RobotAgent] Debug mission ignored because robot is busy.", this);
                return;
            }

            if (!StudentConstants.IsConveyorId(debugConveyorId))
            {
                Debug.LogWarningFormat(
                    this,
                    "[RobotAgent] Invalid debug conveyor id={0}.",
                    debugConveyorId);
                return;
            }

            ResolveSerializedReferences();
            var request = new MissionRequest
            {
                taskId = debugTaskId,
                robotId = robotId,
                conveyorId = debugConveyorId,
                requestTime = Time.time,
                timeoutSec = StudentConstants.DefaultMissionTimeoutSec
            };

            StartMission(request, LogDebugMissionResult);
        }

        private void LogDebugMissionResult(MissionResult result)
        {
            if (!logDebugMissionResult || result == null)
            {
                return;
            }

            Debug.LogFormat(
                this,
                "[RobotAgent] Debug mission result task={0} robot={1} conveyor={2} success={3} class={4} destination={5} reason={6} message={7}",
                result.taskId,
                result.robotId,
                result.conveyorId,
                result.success,
                result.classificationResult,
                result.destinationStationId,
                result.failureReason,
                result.message);
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

            if (!result.success && suctionGripper != null && suctionGripper.IsHolding)
            {
                LogRobotMessage(
                    "RobotAgent failed while still holding a payload; staying Failed until manual recovery.");
                yield break;
            }

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
                PathReservationManager = pathPlanner as IPathReservationManager,
                OperatingStations = operatingStations,
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
                ReleaseConfirmTimeoutSec = releaseConfirmTimeoutSec,
                ColorRetryWaitSec = colorRetryWaitSec,
                ColorRetryCount = colorRetryCount,
                UseBoxFinePositioning = useBoxFinePositioning,
                NormalBoxFineBasePosition = normalBoxFineBasePosition,
                AbnormalBoxFineBasePosition = abnormalBoxFineBasePosition,
                BoxFineMoveTimeoutSec = boxFineMoveTimeoutSec
            };
        }

        private void ResolveSerializedReferences()
        {
            if (robotController == null)
            {
                AssignRobotController(
                    ResolveInterface<IRobotController>(robotControllerComponent)
                    ?? FindLocalInterface<IRobotController>());
            }
            else if (useSafeArmMotion && !(robotController is SafeRobotControllerAdapter))
            {
                AssignRobotController(robotController);
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

            if (robotController is SafeRobotControllerAdapter)
            {
                robotController = SafeRobotControllerAdapter.Wrap(
                    robotController,
                    BuildArmGuardSettings());
            }

            if (robotController != null)
            {
                robotId = robotController.RobotId;
            }
        }

        private void AssignRobotController(IRobotController controller)
        {
            if (controller == null)
            {
                robotController = null;
                return;
            }

            IRobotController unwrapped = SafeRobotControllerAdapter.Unwrap(controller);
            if (unwrapped is MonoBehaviour controllerBehaviour)
            {
                robotControllerComponent = controllerBehaviour;
            }

            robotController = useSafeArmMotion
                ? SafeRobotControllerAdapter.Wrap(unwrapped, BuildArmGuardSettings())
                : unwrapped;

            robotId = robotController.RobotId;
        }

        private SafeRobotControllerAdapter.Settings BuildArmGuardSettings()
        {
            return new SafeRobotControllerAdapter.Settings
            {
                AllowPositionOnlyFallback = allowPositionOnlyIkFallback,
                PositionOnlyFallbackTolerance = positionOnlyIkFallbackTolerance,
                MaxFallbackOrientationErrorDeg = maxFallbackOrientationErrorDeg,
                TelemetryLogger = telemetryLogger
            };
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

        private void LogRobotMessage(string message)
        {
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
