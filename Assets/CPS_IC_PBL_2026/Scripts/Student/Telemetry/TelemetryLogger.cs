using UnityEngine;

namespace CPS.ICPBL.Student
{
    [DisallowMultipleComponent]
    public sealed class TelemetryLogger : MonoBehaviour, ITelemetryLogger
    {
        [Header("Output")]
        [SerializeField] private bool enableLogging = true;
        [SerializeField] private string logPrefix = "Telemetry";
        [SerializeField] private bool includeTimestamp = true;
        [SerializeField] private bool suppressConsecutiveDuplicates = true;

        private string lastCategory;
        private string lastMessage;
        private int duplicateCount;

        public void LogTaskCreated(WorkTask task)
        {
            if (task == null)
            {
                LogMessage("Task", "Create ignored null task.");
                return;
            }

            Write("Task", string.Format(
                "Created task={0} conveyor={1} createdAt={2:0.00} status={3}.",
                task.taskId,
                task.conveyorId,
                task.createdAt,
                task.status));
        }

        public void LogTaskAssigned(WorkTask task, int robotId)
        {
            if (task == null)
            {
                LogMessage("Task", string.Format(
                    "Assign ignored null task robot={0}.",
                    robotId));
                return;
            }

            Write("Task", string.Format(
                "Assigned task={0} conveyor={1} robot={2} assignedAt={3:0.00} retry={4}.",
                task.taskId,
                task.conveyorId,
                robotId,
                task.assignedAt,
                task.retryCount));
        }

        public void LogMissionResult(MissionResult result)
        {
            if (result == null)
            {
                LogMessage("Mission", "Result ignored null mission result.");
                return;
            }

            Write("Mission", string.Format(
                "Result task={0} robot={1} conveyor={2} success={3} class={4} destination={5} reason={6} duration={7:0.00}s message=\"{8}\".",
                result.taskId,
                result.robotId,
                result.conveyorId,
                result.success,
                result.classificationResult,
                result.destinationStationId,
                result.failureReason,
                Mathf.Max(0f, result.finishedAt - result.startedAt),
                Sanitize(result.message)));
        }

        public void LogLock(string action, ResourceKey key, int robotId, int taskId)
        {
            Write("Lock", string.Format(
                "{0} key={1} robot={2} task={3}.",
                Sanitize(action),
                key,
                robotId,
                taskId));
        }

        public void LogMessage(string category, string message)
        {
            Write(category, message);
        }

        private void Write(string category, string message)
        {
            if (!enableLogging)
            {
                return;
            }

            string safeCategory = string.IsNullOrWhiteSpace(category)
                ? "General"
                : category.Trim();
            string safeMessage = Sanitize(message);

            if (suppressConsecutiveDuplicates
                && safeCategory == lastCategory
                && safeMessage == lastMessage)
            {
                duplicateCount++;
                return;
            }

            FlushDuplicateCount();

            lastCategory = safeCategory;
            lastMessage = safeMessage;
            duplicateCount = 0;

            Debug.Log(FormatLine(safeCategory, safeMessage), this);
        }

        private void FlushDuplicateCount()
        {
            if (!enableLogging || duplicateCount <= 0)
            {
                return;
            }

            Debug.Log(FormatLine(
                lastCategory,
                string.Format("Previous message repeated {0} time(s).", duplicateCount)),
                this);
        }

        private string FormatLine(string category, string message)
        {
            if (includeTimestamp)
            {
                return string.Format(
                    "[{0}][{1:0.00}s][{2}] {3}",
                    logPrefix,
                    Time.time,
                    category,
                    message);
            }

            return string.Format("[{0}][{1}] {2}", logPrefix, category, message);
        }

        private static string Sanitize(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace('\n', ' ');
        }

        private void OnDisable()
        {
            FlushDuplicateCount();
            duplicateCount = 0;
        }
    }
}
