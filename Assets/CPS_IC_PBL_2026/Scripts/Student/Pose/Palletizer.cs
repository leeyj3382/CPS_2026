using System.Collections.Generic;
using CPS.ICPBL.Common;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    public class Palletizer : MonoBehaviour, IPalletizer
    {
        private enum SlotState
        {
            Free = 0,
            Reserved = 1,
            Committed = 2
        }

        private sealed class Reservation
        {
            public BoxType BoxType;
            public int SlotIndex;
            public int RobotId;
        }

        [Header("Pose Source")]
        [SerializeField] private PoseTable poseTable;

        [Header("Optional Box Triggers")]
        [SerializeField] private BoxTrigger normalBoxTrigger;
        [SerializeField] private BoxTrigger abnormalBoxTrigger;
        [SerializeField] private bool registerBoxTriggerOnCommit = true;

        [Header("Slot Grid")]
        [SerializeField] private int columns = 4;
        [SerializeField] private int rows = 3;
        [SerializeField] private int normalSlotCount = 52;
        [SerializeField] private int abnormalSlotCount = 12;
        [SerializeField] private Vector3 slotSpacing = new Vector3(0.32f, 0f, 0.32f);
        [SerializeField] private float layerHeight = 0.22f;
        [SerializeField] private Vector3 normalGridOriginOffset = new Vector3(-0.48f, 0f, -0.32f);
        [SerializeField] private Vector3 abnormalGridOriginOffset = new Vector3(-0.48f, 0f, -0.32f);

        [Header("Place Offsets")]
        [SerializeField] private Vector3 placeApproachOffset = new Vector3(0f, 0.35f, 0f);
        [SerializeField] private Vector3 placeRetractOffset = new Vector3(0f, 0.45f, 0f);

        [Header("Scene Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private bool drawNormalSlots = true;
        [SerializeField] private bool drawAbnormalSlots = true;
        [SerializeField] private float gizmoRadius = 0.06f;

        [Header("Manual Test")]
        [SerializeField] private BoxType testBoxType = BoxType.Normal;
        [SerializeField] private int testRobotId = StudentConstants.RobotAId;
        [SerializeField] private int testTaskId = 9001;

        private SlotState[] normalSlots;
        private SlotState[] abnormalSlots;
        private readonly Dictionary<int, Reservation> reservationsByTask =
            new Dictionary<int, Reservation>();

        public BoxSlotPose ReserveNextSlot(BoxType boxType, int robotId, int taskId)
        {
            EnsureInitialized();

            if (taskId == StudentConstants.NoTaskId)
            {
                Debug.LogWarning("[Palletizer] Cannot reserve a slot for NoTaskId.");
                return null;
            }

            if (reservationsByTask.TryGetValue(taskId, out Reservation existing))
            {
                return BuildSlotPose(existing.BoxType, existing.SlotIndex, taskId);
            }

            SlotState[] slots = GetSlots(boxType);
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                if (slots[slotIndex] != SlotState.Free)
                {
                    continue;
                }

                BoxTrigger trigger = GetBoxTrigger(boxType);
                if (trigger != null && slotIndex >= trigger.SlotCount)
                {
                    continue;
                }

                if (trigger != null && trigger.IsSlotOccupied(slotIndex))
                {
                    slots[slotIndex] = SlotState.Committed;
                    continue;
                }

                slots[slotIndex] = SlotState.Reserved;
                reservationsByTask[taskId] = new Reservation
                {
                    BoxType = boxType,
                    SlotIndex = slotIndex,
                    RobotId = robotId
                };

                BoxSlotPose pose = BuildSlotPose(boxType, slotIndex, taskId);
                Debug.LogFormat(
                    this,
                    "[Palletizer] Reserved {0} slot={1} task={2} robot={3} place={4}.",
                    boxType,
                    slotIndex,
                    taskId,
                    robotId,
                    pose.placePos);
                return pose;
            }

            Debug.LogWarningFormat(this, "[Palletizer] No free slot for {0}.", boxType);
            return null;
        }

        public void CommitSlot(int taskId)
        {
            EnsureInitialized();

            if (!reservationsByTask.TryGetValue(taskId, out Reservation reservation))
            {
                Debug.LogWarningFormat(this, "[Palletizer] Commit ignored; no reservation for task={0}.", taskId);
                return;
            }

            SlotState[] slots = GetSlots(reservation.BoxType);
            slots[reservation.SlotIndex] = SlotState.Committed;
            if (registerBoxTriggerOnCommit)
            {
                RegisterBoxTriggerSlot(reservation.BoxType, reservation.SlotIndex);
            }

            reservationsByTask.Remove(taskId);
            Debug.LogFormat(
                this,
                "[Palletizer] Committed {0} slot={1} task={2} robot={3}.",
                reservation.BoxType,
                reservation.SlotIndex,
                taskId,
                reservation.RobotId);
        }

        public void ReleaseSlot(int taskId)
        {
            EnsureInitialized();

            if (!reservationsByTask.TryGetValue(taskId, out Reservation reservation))
            {
                return;
            }

            SlotState[] slots = GetSlots(reservation.BoxType);
            if (slots[reservation.SlotIndex] == SlotState.Reserved)
            {
                slots[reservation.SlotIndex] = SlotState.Free;
            }

            reservationsByTask.Remove(taskId);
            Debug.LogFormat(
                this,
                "[Palletizer] Released {0} slot={1} task={2} robot={3}.",
                reservation.BoxType,
                reservation.SlotIndex,
                taskId,
                reservation.RobotId);
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnValidate()
        {
            columns = Mathf.Max(1, columns);
            rows = Mathf.Max(1, rows);
            normalSlotCount = Mathf.Max(1, normalSlotCount);
            abnormalSlotCount = Mathf.Max(1, abnormalSlotCount);
            layerHeight = Mathf.Max(0f, layerHeight);
            gizmoRadius = Mathf.Max(0.01f, gizmoRadius);
        }

        private void EnsureInitialized()
        {
            int normalCount = Mathf.Max(1, normalSlotCount);
            int abnormalCount = Mathf.Max(1, abnormalSlotCount);
            bool resized = false;

            if (normalSlots == null || normalSlots.Length != normalCount)
            {
                normalSlots = new SlotState[normalCount];
                resized = true;
            }

            if (abnormalSlots == null || abnormalSlots.Length != abnormalCount)
            {
                abnormalSlots = new SlotState[abnormalCount];
                resized = true;
            }

            if (resized)
            {
                reservationsByTask.Clear();
            }
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

        private SlotState[] GetSlots(BoxType boxType)
        {
            if (boxType == BoxType.Normal)
            {
                return normalSlots;
            }

            if (boxType == BoxType.Abnormal)
            {
                return abnormalSlots;
            }

            throw new System.ArgumentOutOfRangeException(nameof(boxType), boxType, "Unsupported box type.");
        }

        private BoxSlotPose BuildSlotPose(BoxType boxType, int slotIndex, int taskId)
        {
            Vector3 placePos = GetSlotWorldPosition(boxType, slotIndex);
            return new BoxSlotPose
            {
                boxType = boxType,
                stationId = StudentConstants.GetBoxStationId(boxType),
                slotIndex = slotIndex,
                approachPos = placePos + placeApproachOffset,
                placePos = placePos,
                retractPos = placePos + placeRetractOffset,
                reserved = true,
                reservedByTaskId = taskId
            };
        }

        private Vector3 GetSlotWorldPosition(BoxType boxType, int slotIndex)
        {
            PoseTable table = ResolvePoseTable();
            if (table == null)
            {
                Debug.LogWarning("[Palletizer] PoseTable reference is missing; using Vector3.zero as box base.");
                return GetGridOffset(boxType, slotIndex);
            }

            StationPose boxPose = table.GetBoxBasePose(boxType);
            return boxPose.actionPos + GetGridOffset(boxType, slotIndex);
        }

        private Vector3 GetGridOffset(BoxType boxType, int slotIndex)
        {
            int footprintCount = Mathf.Max(1, columns * rows);
            int footprintIndex = slotIndex % footprintCount;
            int layerIndex = slotIndex / footprintCount;
            int column = footprintIndex % columns;
            int row = footprintIndex / columns;
            Vector3 origin = boxType == BoxType.Normal
                ? normalGridOriginOffset
                : abnormalGridOriginOffset;

            return origin + new Vector3(
                column * slotSpacing.x,
                layerIndex * layerHeight,
                row * slotSpacing.z);
        }

        private void RegisterBoxTriggerSlot(BoxType boxType, int slotIndex)
        {
            BoxTrigger trigger = GetBoxTrigger(boxType);
            if (trigger == null)
            {
                return;
            }

            if (!trigger.RegisterSlotPlacement(slotIndex))
            {
                Debug.LogWarningFormat(
                    this,
                    "[Palletizer] BoxTrigger rejected {0} slot={1}.",
                    boxType,
                    slotIndex);
            }
        }

        private BoxTrigger GetBoxTrigger(BoxType boxType)
        {
            if (boxType == BoxType.Normal)
            {
                return normalBoxTrigger;
            }

            if (boxType == BoxType.Abnormal)
            {
                return abnormalBoxTrigger;
            }

            return null;
        }

        [ContextMenu("Palletizer/Log Slot Summary")]
        private void LogSlotSummary()
        {
            EnsureInitialized();
            Debug.LogFormat(
                this,
                "[Palletizer] Normal committed/reserved/free = {0}/{1}/{2}, Abnormal committed/reserved/free = {3}/{4}/{5}.",
                CountSlots(normalSlots, SlotState.Committed),
                CountSlots(normalSlots, SlotState.Reserved),
                CountSlots(normalSlots, SlotState.Free),
                CountSlots(abnormalSlots, SlotState.Committed),
                CountSlots(abnormalSlots, SlotState.Reserved),
                CountSlots(abnormalSlots, SlotState.Free));
        }

        [ContextMenu("Palletizer/Log Detailed Slots")]
        private void LogDetailedSlots()
        {
            EnsureInitialized();
            Debug.LogFormat(
                this,
                "[Palletizer] Normal slots: {0}\n[Palletizer] Abnormal slots: {1}\n[Palletizer] Reservations: {2}",
                FormatSlotStates(normalSlots),
                FormatSlotStates(abnormalSlots),
                FormatReservations());
        }

        [ContextMenu("Palletizer/Test Reserve Slot")]
        private void TestReserveSlot()
        {
            BoxSlotPose pose = ReserveNextSlot(testBoxType, testRobotId, testTaskId);
            if (pose == null)
            {
                Debug.LogWarningFormat(
                    this,
                    "[Palletizer] Test reserve failed box={0} task={1}.",
                    testBoxType,
                    testTaskId);
            }
        }

        [ContextMenu("Palletizer/Test Commit Slot")]
        private void TestCommitSlot()
        {
            CommitSlot(testTaskId);
        }

        [ContextMenu("Palletizer/Test Release Slot")]
        private void TestReleaseSlot()
        {
            ReleaseSlot(testTaskId);
        }

        [ContextMenu("Palletizer/Clear All Slots")]
        private void ClearAllSlots()
        {
            EnsureInitialized();
            System.Array.Clear(normalSlots, 0, normalSlots.Length);
            System.Array.Clear(abnormalSlots, 0, abnormalSlots.Length);
            reservationsByTask.Clear();
        }

        private int CountSlots(SlotState[] slots, SlotState state)
        {
            int count = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == state)
                {
                    count++;
                }
            }

            return count;
        }

        private string FormatSlotStates(SlotState[] slots)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == SlotState.Free)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(i);
                builder.Append("=");
                builder.Append(slots[i]);
            }

            return builder.Length > 0 ? builder.ToString() : "none";
        }

        private string FormatReservations()
        {
            if (reservationsByTask.Count == 0)
            {
                return "none";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            foreach (KeyValuePair<int, Reservation> pair in reservationsByTask)
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append("task ");
                builder.Append(pair.Key);
                builder.Append(" -> ");
                builder.Append(pair.Value.BoxType);
                builder.Append(" slot ");
                builder.Append(pair.Value.SlotIndex);
                builder.Append(" robot ");
                builder.Append(pair.Value.RobotId);
            }

            return builder.ToString();
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
            {
                return;
            }

            EnsureInitialized();
            if (drawNormalSlots)
            {
                DrawSlots(BoxType.Normal, normalSlots);
            }

            if (drawAbnormalSlots)
            {
                DrawSlots(BoxType.Abnormal, abnormalSlots);
            }
        }

        private void DrawSlots(BoxType boxType, SlotState[] slots)
        {
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                Gizmos.color = GetSlotColor(slots[slotIndex]);
                Gizmos.DrawSphere(GetSlotWorldPosition(boxType, slotIndex), gizmoRadius);
            }
        }

        private Color GetSlotColor(SlotState state)
        {
            if (state == SlotState.Reserved)
            {
                return Color.yellow;
            }

            if (state == SlotState.Committed)
            {
                return Color.green;
            }

            return Color.white;
        }
    }
}
