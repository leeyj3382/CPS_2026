using CPS.ICPBL.Common;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    public class CalibrationManager : MonoBehaviour
    {
        [Header("Pose Source")]
        [SerializeField] private PoseTable poseTable;
        [SerializeField] private int stationId = StudentConstants.MinConveyorId;

        [Header("Preview Offsets")]
        [SerializeField] private Vector3 approachOffset;
        [SerializeField] private Vector3 actionOffset;
        [SerializeField] private Vector3 retractOffset;

        [Header("Scene Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private bool drawBasePose = true;
        [SerializeField] private float gizmoRadius = 0.08f;

        public int StationId
        {
            get => stationId;
            set => stationId = value;
        }

        public bool TryGetBasePose(out StationPose pose)
        {
            PoseTable table = ResolvePoseTable();
            if (table == null)
            {
                pose = null;
                return false;
            }

            if (StudentConstants.IsConveyorId(stationId))
            {
                pose = table.GetConveyorPickPose(stationId);
                return pose != null;
            }

            if (stationId == StudentConstants.NormalBoxStationId)
            {
                pose = table.GetBoxBasePose(BoxType.Normal);
                return pose != null;
            }

            if (stationId == StudentConstants.AbnormalBoxStationId)
            {
                pose = table.GetBoxBasePose(BoxType.Abnormal);
                return pose != null;
            }

            pose = null;
            return false;
        }

        public bool TryGetPreviewPose(out StationPose pose)
        {
            if (!TryGetBasePose(out StationPose basePose))
            {
                pose = null;
                return false;
            }

            pose = new StationPose
            {
                stationId = basePose.stationId,
                approachPos = basePose.approachPos + approachOffset,
                actionPos = basePose.actionPos + actionOffset,
                retractPos = basePose.retractPos + retractOffset,
                armMoveDuration = basePose.armMoveDuration
            };
            return true;
        }

        private void OnValidate()
        {
            gizmoRadius = Mathf.Max(0.01f, gizmoRadius);
        }

        private PoseTable ResolvePoseTable()
        {
            if (poseTable != null)
            {
                return poseTable;
            }

            poseTable = GetComponent<PoseTable>();
            return poseTable;
        }

        [ContextMenu("Calibration/Log Base Pose")]
        private void LogBasePose()
        {
            if (!TryGetBasePose(out StationPose pose))
            {
                Debug.LogWarningFormat(
                    this,
                    "[CalibrationManager] No base pose for station {0}.",
                    stationId);
                return;
            }

            LogPose("Base", pose);
        }

        [ContextMenu("Calibration/Log Preview Pose")]
        private void LogPreviewPose()
        {
            if (!TryGetPreviewPose(out StationPose pose))
            {
                Debug.LogWarningFormat(
                    this,
                    "[CalibrationManager] No preview pose for station {0}.",
                    stationId);
                return;
            }

            LogPose("Preview", pose);
        }

        [ContextMenu("Calibration/Reset Preview Offsets")]
        private void ResetPreviewOffsets()
        {
            approachOffset = Vector3.zero;
            actionOffset = Vector3.zero;
            retractOffset = Vector3.zero;
        }

        private void LogPose(string label, StationPose pose)
        {
            Debug.LogFormat(
                this,
                "[CalibrationManager] {0} station {1}: approach={2}, action={3}, retract={4}, duration={5:0.00}",
                label,
                pose.stationId,
                pose.approachPos,
                pose.actionPos,
                pose.retractPos,
                pose.armMoveDuration);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
            {
                return;
            }

            if (drawBasePose && TryGetBasePose(out StationPose basePose))
            {
                DrawPose(basePose, Color.gray, Color.gray, Color.gray, gizmoRadius * 0.65f);
            }

            if (TryGetPreviewPose(out StationPose previewPose))
            {
                DrawPose(previewPose, Color.cyan, Color.yellow, Color.green, gizmoRadius);
            }
        }

        private void DrawPose(
            StationPose pose,
            Color approachColor,
            Color actionColor,
            Color retractColor,
            float radius)
        {
            Gizmos.color = approachColor;
            Gizmos.DrawSphere(pose.approachPos, radius);

            Gizmos.color = actionColor;
            Gizmos.DrawSphere(pose.actionPos, radius);

            Gizmos.color = retractColor;
            Gizmos.DrawSphere(pose.retractPos, radius);

            Gizmos.color = Color.white;
            Gizmos.DrawLine(pose.approachPos, pose.actionPos);
            Gizmos.DrawLine(pose.actionPos, pose.retractPos);
        }
    }
}
