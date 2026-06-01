using System.Collections.Generic;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    [DisallowMultipleComponent]
    public sealed class DeadlockGuard : MonoBehaviour
    {
        [Header("Observed Robots")]
        [SerializeField] private RobotAgent[] robotAgents;
        [SerializeField] private MonoBehaviour telemetryLoggerComponent;

        [Header("Diagnostics")]
        [SerializeField] private bool enableDiagnostics = true;
        [SerializeField, Min(0.1f)] private float waitingWarningSec =
            StudentConstants.DefaultLockTimeoutSec;
        [SerializeField, Min(0.1f)] private float warningIntervalSec = 2f;
        [SerializeField] private bool logWithoutTelemetry = true;

        private readonly Dictionary<int, float> waitingSinceByRobot =
            new Dictionary<int, float>();
        private readonly Dictionary<int, float> lastWarningAtByRobot =
            new Dictionary<int, float>();

        private ITelemetryLogger telemetryLogger;

        public void Configure(
            RobotAgent robotA,
            RobotAgent robotB,
            ITelemetryLogger logger = null)
        {
            robotAgents = new[] { robotA, robotB };
            telemetryLogger = logger;
            telemetryLoggerComponent = logger as MonoBehaviour;
        }

        private void Awake()
        {
            ResolveSerializedReferences();
        }

        private void Update()
        {
            if (!enableDiagnostics || robotAgents == null)
            {
                return;
            }

            float now = Time.time;
            for (int i = 0; i < robotAgents.Length; i++)
            {
                RobotAgent robot = robotAgents[i];
                if (robot == null)
                {
                    continue;
                }

                UpdateRobot(robot, now);
            }
        }

        private void OnValidate()
        {
            waitingWarningSec = Mathf.Max(0.1f, waitingWarningSec);
            warningIntervalSec = Mathf.Max(0.1f, warningIntervalSec);
        }

        private void ResolveSerializedReferences()
        {
            if (telemetryLogger == null)
            {
                telemetryLogger = telemetryLoggerComponent as ITelemetryLogger;
            }
        }

        private void UpdateRobot(RobotAgent robot, float now)
        {
            int robotId = robot.RobotId;
            if (robot.State != RobotRuntimeState.WaitingForLock)
            {
                ClearWaitingState(robotId, robot.State, now);
                return;
            }

            if (!waitingSinceByRobot.ContainsKey(robotId))
            {
                waitingSinceByRobot[robotId] = now;
                lastWarningAtByRobot[robotId] = now;
                LogMessage(string.Format(
                    "Robot {0} entered WaitingForLock.",
                    robotId));
                return;
            }

            float waitingFor = now - waitingSinceByRobot[robotId];
            float lastWarningAt = lastWarningAtByRobot.TryGetValue(
                robotId,
                out float value)
                ? value
                : waitingSinceByRobot[robotId];

            if (waitingFor < waitingWarningSec || now - lastWarningAt < warningIntervalSec)
            {
                return;
            }

            lastWarningAtByRobot[robotId] = now;
            LogMessage(string.Format(
                "Robot {0} has waited for lock for {1:0.0}s. MissionExecutor owns timeout/failure handling.",
                robotId,
                waitingFor));
        }

        private void ClearWaitingState(int robotId, RobotRuntimeState state, float now)
        {
            if (!waitingSinceByRobot.TryGetValue(robotId, out float waitingSince))
            {
                return;
            }

            float waitedFor = now - waitingSince;
            waitingSinceByRobot.Remove(robotId);
            lastWarningAtByRobot.Remove(robotId);

            LogMessage(string.Format(
                "Robot {0} left WaitingForLock after {1:0.0}s; state={2}.",
                robotId,
                waitedFor,
                state));
        }

        private void LogMessage(string message)
        {
            ResolveSerializedReferences();

            if (telemetryLogger != null)
            {
                telemetryLogger.LogMessage("Deadlock", message);
                return;
            }

            if (logWithoutTelemetry)
            {
                Debug.LogWarning(string.Format("[DeadlockGuard] {0}", message), this);
            }
        }
    }
}
