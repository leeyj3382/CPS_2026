using CPS.ICPBL.Common;
using CPS.ICPBL.Environment;
using CPS.Lab11.MobileManipulator;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    [DisallowMultipleComponent]
    public sealed class StudentSceneReferences : MonoBehaviour
    {
        [Header("Environment")]
        [SerializeField] private EnvironmentInfo environmentInfo;
        [SerializeField] private OperatingStations operatingStations;
        [SerializeField] private global::BoxTrigger normalBox;
        [SerializeField] private global::BoxTrigger abnormalBox;

        [Header("Fleet")]
        [SerializeField] private FleetManager fleetManager;

        [Header("Robot A")]
        [SerializeField] private RobotAgent robotAAgent;
        [SerializeField] private MonoBehaviour robotAController;
        [SerializeField] private SuctionGripper robotAGripper;
        [SerializeField] private global::ColorSensor robotAColorSensor;
        [SerializeField] private global::ColorArea robotAColorArea;

        [Header("Robot B")]
        [SerializeField] private RobotAgent robotBAgent;
        [SerializeField] private MonoBehaviour robotBController;
        [SerializeField] private SuctionGripper robotBGripper;
        [SerializeField] private global::ColorSensor robotBColorSensor;
        [SerializeField] private global::ColorArea robotBColorArea;

        [Header("Student Services")]
        [SerializeField] private MonoBehaviour poseProvider;
        [SerializeField] private MonoBehaviour palletizer;
        [SerializeField] private MonoBehaviour colorClassifier;
        [SerializeField] private MonoBehaviour resourceLockManager;
        [SerializeField] private MonoBehaviour pathPlanner;
        [SerializeField] private DeadlockGuard deadlockGuard;
        [SerializeField] private MonoBehaviour telemetryLogger;

        public IEnvironmentInfo EnvironmentInfo => environmentInfo;
        public OperatingStations OperatingStations => operatingStations;
        public global::BoxTrigger NormalBox => normalBox;
        public global::BoxTrigger AbnormalBox => abnormalBox;
        public FleetManager FleetManager => fleetManager;

        public RobotAgent RobotAAgent => robotAAgent;
        public IRobotController RobotAController => Resolve<IRobotController>(robotAController);
        public SuctionGripper RobotAGripper => robotAGripper;
        public global::ColorSensor RobotAColorSensor => robotAColorSensor;
        public global::ColorArea RobotAColorArea => ResolveColorArea(robotAColorArea, robotAColorSensor);

        public RobotAgent RobotBAgent => robotBAgent;
        public IRobotController RobotBController => Resolve<IRobotController>(robotBController);
        public SuctionGripper RobotBGripper => robotBGripper;
        public global::ColorSensor RobotBColorSensor => robotBColorSensor;
        public global::ColorArea RobotBColorArea => ResolveColorArea(robotBColorArea, robotBColorSensor);

        public IPoseProvider PoseProvider => Resolve<IPoseProvider>(poseProvider);
        public IPalletizer Palletizer => Resolve<IPalletizer>(palletizer);
        public IColorClassifier ColorClassifier => Resolve<IColorClassifier>(colorClassifier);
        public IResourceLockManager ResourceLockManager => Resolve<IResourceLockManager>(resourceLockManager);
        public IPathPlanner PathPlanner => Resolve<IPathPlanner>(pathPlanner);
        public DeadlockGuard DeadlockGuard => deadlockGuard;
        public ITelemetryLogger TelemetryLogger => Resolve<ITelemetryLogger>(telemetryLogger);

        public bool HasRequiredFleetReferences()
        {
            return EnvironmentInfo != null && fleetManager != null;
        }

        public bool HasRobotAReferences()
        {
            return robotAAgent != null
                && RobotAController != null
                && robotAGripper != null
                && (robotAColorSensor != null || robotAColorArea != null);
        }

        public bool HasRobotBReferences()
        {
            return robotBAgent != null
                && RobotBController != null
                && robotBGripper != null
                && (robotBColorSensor != null || robotBColorArea != null);
        }

        private static T Resolve<T>(MonoBehaviour component)
            where T : class
        {
            return component as T;
        }

        private static global::ColorArea ResolveColorArea(
            global::ColorArea colorArea,
            global::ColorSensor colorSensor)
        {
            if (colorArea != null)
            {
                return colorArea;
            }

            return colorSensor != null ? colorSensor.area : null;
        }
    }
}
