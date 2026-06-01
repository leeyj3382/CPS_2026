using UnityEngine;

namespace CPS.ICPBL.Student
{
    [DisallowMultipleComponent]
    public sealed class StudentBootstrap : MonoBehaviour
    {
        [SerializeField] private StudentSceneReferences sceneReferences;
        [SerializeField] private bool configureOnAwake = true;
        [SerializeField] private bool logWarnings = true;

        private void Awake()
        {
            if (configureOnAwake)
            {
                Configure();
            }
        }

        public void Configure()
        {
            if (sceneReferences == null)
            {
                sceneReferences = GetComponent<StudentSceneReferences>();
            }

            if (sceneReferences == null)
            {
                Warn("StudentSceneReferences is not assigned.");
                return;
            }

            ConfigureRobot(
                "RobotA",
                sceneReferences.RobotAAgent,
                sceneReferences.RobotAController,
                sceneReferences.RobotAGripper,
                sceneReferences.RobotAColorSensor,
                sceneReferences.RobotAColorArea);

            ConfigureRobot(
                "RobotB",
                sceneReferences.RobotBAgent,
                sceneReferences.RobotBController,
                sceneReferences.RobotBGripper,
                sceneReferences.RobotBColorSensor,
                sceneReferences.RobotBColorArea);

            ConfigureDeadlockGuard();
            ConfigureFleet();
        }

        private void ConfigureRobot(
            string label,
            RobotAgent agent,
            CPS.ICPBL.Common.IRobotController controller,
            CPS.Lab11.MobileManipulator.SuctionGripper gripper,
            global::ColorSensor colorSensor,
            global::ColorArea colorArea)
        {
            if (agent == null)
            {
                Warn(string.Format("{0} agent is not assigned.", label));
                return;
            }

            if (controller == null || gripper == null || (colorSensor == null && colorArea == null))
            {
                Warn(string.Format("{0} has missing robot references.", label));
            }

            agent.Configure(
                controller,
                gripper,
                colorSensor,
                colorArea,
                sceneReferences.PoseProvider,
                sceneReferences.Palletizer,
                sceneReferences.ColorClassifier,
                sceneReferences.ResourceLockManager,
                sceneReferences.PathPlanner,
                sceneReferences.TelemetryLogger);
        }

        private void ConfigureFleet()
        {
            if (!sceneReferences.HasRequiredFleetReferences())
            {
                Warn("FleetManager or EnvironmentInfo is not assigned.");
                return;
            }

            sceneReferences.FleetManager.Configure(
                sceneReferences.EnvironmentInfo,
                sceneReferences.OperatingStations,
                sceneReferences.RobotAAgent,
                sceneReferences.RobotBAgent,
                sceneReferences.TelemetryLogger);
        }

        private void ConfigureDeadlockGuard()
        {
            if (sceneReferences.DeadlockGuard == null)
            {
                return;
            }

            sceneReferences.DeadlockGuard.Configure(
                sceneReferences.RobotAAgent,
                sceneReferences.RobotBAgent,
                sceneReferences.TelemetryLogger);
        }

        private void Warn(string message)
        {
            if (logWarnings)
            {
                Debug.LogWarning(string.Format("[StudentBootstrap] {0}", message), this);
            }
        }
    }
}
