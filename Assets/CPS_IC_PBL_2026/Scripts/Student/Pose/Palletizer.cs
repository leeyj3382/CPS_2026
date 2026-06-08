using System.Collections.Generic;
using CPS.ICPBL.Common;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    public class Palletizer : MonoBehaviour, IPalletizer
    {
        private const float MinSlotSpacing = 0.01f;

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

        private struct SlotGrid
        {
            public BoxType BoxType;
            public int MajorCount;
            public int MinorCount;
            public int Layers;
            public bool MajorAxisIsX;
            public float MajorStart;
            public float MinorStart;
            public float MajorStep;
            public float MinorStep;
            public float LayerStartY;
            public float StepY;
        }

        [Header("Pose Source")]
        [SerializeField] private PoseTable poseTable;

        [Header("Optional Box Triggers")]
        [SerializeField] private BoxTrigger normalBoxTrigger;
        [SerializeField] private BoxTrigger abnormalBoxTrigger;
        [SerializeField] private bool registerBoxTriggerOnCommit = true;

        [Header("Normal Slot Grid")]
        [SerializeField] private int columns = 6;
        [SerializeField] private int rows = 3;
        [SerializeField] private int normalSlotCount = 52;
        [SerializeField] private Vector3 slotSpacing = new Vector3(0.28f, 0f, 0.28f);
        [SerializeField] private float layerHeight = 0.28f;
        [SerializeField] private float productSize = 0.25f;
        [SerializeField] private float sideClearance = 0.06f;
        [SerializeField] private float bottomClearance = 0.01f;
        [SerializeField] private Vector3 normalGridOriginOffset = new Vector3(-0.75f, 0f, -0.28f);
        [SerializeField] private float horizontalPadding = 0.2f;
        [SerializeField] private float verticalPadding = 0.135f;

        [Header("Abnormal Slot Grid")]
        [SerializeField] private int abnormalColumns = 3;
        [SerializeField] private int abnormalRows = 4;
        [SerializeField] private int abnormalSlotCount = 12;
        [SerializeField] private Vector3 abnormalSlotSpacing = new Vector3(0.3f, 0f, 0.28f);
        [SerializeField] private float abnormalLayerHeight = 0.28f;
        [SerializeField] private float abnormalProductSize = 0.25f;
        [SerializeField] private float abnormalSideClearance = 0.06f;
        [SerializeField] private float abnormalBottomClearance = 0.01f;
        [SerializeField] private Vector3 abnormalGridOriginOffset = new Vector3(-0.3f, 0f, -0.62f);
        [SerializeField] private float abnormalHorizontalPadding = 0.2f;
        [SerializeField] private float abnormalVerticalPadding = 0.135f;

        [Header("Slot Calculation")]
        [SerializeField] private bool fitGridToBoxBounds = true;

        [Header("Place Offsets")]
        [SerializeField] private float gravityDropHeightOffset = 0.02f;
        [SerializeField] private float upperLayerGravityDropHeightOffset = 0.04f;
        [SerializeField] private float highStackGravityDropHeightOffset = 0.04f;
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

                if (!IsSlotWithinCalculatedCapacity(boxType, slotIndex))
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
            abnormalColumns = Mathf.Max(1, abnormalColumns);
            abnormalRows = Mathf.Max(1, abnormalRows);
            normalSlotCount = Mathf.Max(1, normalSlotCount);
            abnormalSlotCount = Mathf.Max(1, abnormalSlotCount);
            slotSpacing.x = Mathf.Max(MinSlotSpacing, slotSpacing.x);
            slotSpacing.z = Mathf.Max(MinSlotSpacing, slotSpacing.z);
            layerHeight = Mathf.Max(MinSlotSpacing, layerHeight);
            productSize = Mathf.Max(MinSlotSpacing, productSize);
            sideClearance = Mathf.Max(0f, sideClearance);
            bottomClearance = Mathf.Max(0f, bottomClearance);
            horizontalPadding = Mathf.Max(0f, horizontalPadding);
            verticalPadding = Mathf.Max(0f, verticalPadding);
            abnormalSlotSpacing.x = Mathf.Max(MinSlotSpacing, abnormalSlotSpacing.x);
            abnormalSlotSpacing.z = Mathf.Max(MinSlotSpacing, abnormalSlotSpacing.z);
            abnormalLayerHeight = Mathf.Max(MinSlotSpacing, abnormalLayerHeight);
            abnormalProductSize = Mathf.Max(MinSlotSpacing, abnormalProductSize);
            abnormalSideClearance = Mathf.Max(0f, abnormalSideClearance);
            abnormalBottomClearance = Mathf.Max(0f, abnormalBottomClearance);
            abnormalHorizontalPadding = Mathf.Max(0f, abnormalHorizontalPadding);
            abnormalVerticalPadding = Mathf.Max(0f, abnormalVerticalPadding);
            gravityDropHeightOffset = Mathf.Max(0f, gravityDropHeightOffset);
            upperLayerGravityDropHeightOffset = Mathf.Max(0f, upperLayerGravityDropHeightOffset);
            highStackGravityDropHeightOffset = Mathf.Max(0f, highStackGravityDropHeightOffset);
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
            Vector3 placePos = GetSlotWorldPosition(boxType, slotIndex) + new Vector3(0f, GetDropHeightOffset(boxType, slotIndex), 0f);
            return new BoxSlotPose
            {
                boxType = boxType,
                stationId = StudentConstants.GetBoxStationId(boxType),
                slotIndex = slotIndex,
                approachPos = ApplyVerticalOffset(placePos, placeApproachOffset),
                placePos = placePos,
                retractPos = ApplyVerticalOffset(placePos, placeRetractOffset),
                reserved = true,
                reservedByTaskId = taskId
            };
        }

        private float GetDropHeightOffset(BoxType boxType, int slotIndex)
        {
            int footprintCount = Mathf.Max(1, GetConfiguredMajorCount(boxType) * GetConfiguredMinorCount(boxType));
            int layerIndex = slotIndex / footprintCount;
            if (layerIndex == 0)
            {
                return gravityDropHeightOffset;
            }

            if (layerIndex == 1)
            {
                return upperLayerGravityDropHeightOffset;
            }

            return highStackGravityDropHeightOffset;
        }

        private static Vector3 ApplyVerticalOffset(Vector3 basePos, Vector3 offset)
        {
            return new Vector3(basePos.x, basePos.y + offset.y, basePos.z);
        }

        private Vector3 GetSlotWorldPosition(BoxType boxType, int slotIndex)
        {
            if (ResolvePoseTable() == null)
            {
                Debug.LogWarning("[Palletizer] PoseTable reference is missing; using Vector3.zero as box base.");
            }

            if (fitGridToBoxBounds && TryGetBoxBounds(boxType, out Bounds boxBounds))
            {
                SlotGrid grid = BuildBoundsGrid(boxType, boxBounds);
                return GetGridPosition(grid, slotIndex);
            }

            Vector3 basePosition = Vector3.zero;
            PoseTable table = ResolvePoseTable();
            if (table != null)
            {
                StationPose boxPose = table.GetBoxBasePose(boxType);
                if (boxPose != null)
                {
                    basePosition = boxPose.actionPos;
                }
            }

            return basePosition + GetGridOffset(boxType, slotIndex);
        }

        private Vector3 GetGridOffset(BoxType boxType, int slotIndex)
        {
            int boxColumns = GetConfiguredMajorCount(boxType);
            int boxRows = GetConfiguredMinorCount(boxType);
            int footprintCount = Mathf.Max(1, boxColumns * boxRows);
            int footprintIndex = slotIndex % footprintCount;
            int layerIndex = slotIndex / footprintCount;
            GetOrderedGridIndices(boxType, boxColumns, boxRows, footprintIndex, out int column, out int row);
            Vector3 origin = boxType == BoxType.Normal
                ? normalGridOriginOffset
                : abnormalGridOriginOffset;
            Vector3 spacing = GetConfiguredSlotSpacing(boxType);
            float height = GetConfiguredLayerHeight(boxType);

            return origin + new Vector3(
                column * spacing.x,
                layerIndex * height,
                row * spacing.z);
        }

        private SlotGrid BuildBoundsGrid(BoxType boxType, Bounds boxBounds)
        {
            Vector3 spacing = GetConfiguredSlotSpacing(boxType);
            float stepX = Mathf.Max(MinSlotSpacing, spacing.x);
            float stepZ = Mathf.Max(MinSlotSpacing, spacing.z);
            float stepY = Mathf.Max(Mathf.Max(MinSlotSpacing, GetConfiguredLayerHeight(boxType)), GetConfiguredProductSize(boxType));
            float horizontalCenterInset = GetHorizontalCenterInset(boxType);
            float verticalCenterInset = GetVerticalCenterInset(boxType);
            bool majorAxisIsX = boxBounds.size.x >= boxBounds.size.z;

            int majorCount = GetConfiguredMajorCount(boxType);
            int minorCount = GetConfiguredMinorCount(boxType);
            float majorStep = majorAxisIsX ? stepX : stepZ;
            float minorStepMagnitude = majorAxisIsX ? stepZ : stepX;
            float majorMin = majorAxisIsX
                ? boxBounds.min.x + horizontalCenterInset
                : boxBounds.min.z + horizontalCenterInset;
            float majorMax = majorAxisIsX
                ? boxBounds.max.x - horizontalCenterInset
                : boxBounds.max.z - horizontalCenterInset;
            float minorMin = majorAxisIsX
                ? boxBounds.min.z + horizontalCenterInset
                : boxBounds.min.x + horizontalCenterInset;
            float minorMax = majorAxisIsX
                ? boxBounds.max.z - horizontalCenterInset
                : boxBounds.max.x - horizontalCenterInset;
            float majorStart = GetCenteredStart(majorMin, majorMax, majorCount, majorStep);
            float minorStart = GetCenteredStart(minorMin, minorMax, minorCount, minorStepMagnitude);

            return new SlotGrid
            {
                BoxType = boxType,
                MajorCount = majorCount,
                MinorCount = minorCount,
                Layers = CalculateSlotCount(boxBounds.size.y, stepY, verticalCenterInset),
                MajorAxisIsX = majorAxisIsX,
                MajorStart = majorStart,
                MinorStart = minorStart,
                MajorStep = majorStep,
                MinorStep = minorStepMagnitude,
                LayerStartY = boxBounds.min.y + verticalCenterInset,
                StepY = stepY
            };
        }

        private int GetConfiguredMajorCount(BoxType boxType)
        {
            return boxType == BoxType.Abnormal
                ? Mathf.Max(1, abnormalColumns)
                : Mathf.Max(1, columns);
        }

        private int GetConfiguredMinorCount(BoxType boxType)
        {
            return boxType == BoxType.Abnormal
                ? Mathf.Max(1, abnormalRows)
                : Mathf.Max(1, rows);
        }

        private Vector3 GetConfiguredSlotSpacing(BoxType boxType)
        {
            return boxType == BoxType.Abnormal
                ? abnormalSlotSpacing
                : slotSpacing;
        }

        private float GetConfiguredLayerHeight(BoxType boxType)
        {
            return boxType == BoxType.Abnormal
                ? abnormalLayerHeight
                : layerHeight;
        }

        private float GetConfiguredProductSize(BoxType boxType)
        {
            return boxType == BoxType.Abnormal
                ? abnormalProductSize
                : productSize;
        }

        private float GetConfiguredSideClearance(BoxType boxType)
        {
            return boxType == BoxType.Abnormal
                ? abnormalSideClearance
                : sideClearance;
        }

        private float GetConfiguredBottomClearance(BoxType boxType)
        {
            return boxType == BoxType.Abnormal
                ? abnormalBottomClearance
                : bottomClearance;
        }

        private float GetConfiguredHorizontalPadding(BoxType boxType)
        {
            return boxType == BoxType.Abnormal
                ? abnormalHorizontalPadding
                : horizontalPadding;
        }

        private float GetConfiguredVerticalPadding(BoxType boxType)
        {
            return boxType == BoxType.Abnormal
                ? abnormalVerticalPadding
                : verticalPadding;
        }

        private float GetHorizontalCenterInset(BoxType boxType)
        {
            return Mathf.Max(
                GetConfiguredHorizontalPadding(boxType),
                GetConfiguredProductSize(boxType) * 0.5f + GetConfiguredSideClearance(boxType));
        }

        private float GetVerticalCenterInset(BoxType boxType)
        {
            return Mathf.Max(
                GetConfiguredVerticalPadding(boxType),
                GetConfiguredProductSize(boxType) * 0.5f + GetConfiguredBottomClearance(boxType));
        }

        private static float GetCenteredStart(float min, float max, int count, float step)
        {
            float span = Mathf.Max(0f, (Mathf.Max(1, count) - 1) * step);
            return (min + max - span) * 0.5f;
        }

        private static Vector3 GetGridPosition(SlotGrid grid, int slotIndex)
        {
            int footprintCount = Mathf.Max(1, grid.MajorCount * grid.MinorCount);
            int footprintIndex = slotIndex % footprintCount;
            int layerIndex = slotIndex / footprintCount;
            GetOrderedGridIndices(
                grid.BoxType,
                grid.MajorCount,
                grid.MinorCount,
                footprintIndex,
                out int majorIndex,
                out int minorIndex);
            float major = grid.MajorStart + (majorIndex * grid.MajorStep);
            float minor = grid.MinorStart + (minorIndex * grid.MinorStep);
            float y = grid.LayerStartY + (layerIndex * grid.StepY);

            return grid.MajorAxisIsX
                ? new Vector3(major, y, minor)
                : new Vector3(minor, y, major);
        }

        private static void GetOrderedGridIndices(
            BoxType boxType,
            int majorCount,
            int minorCount,
            int footprintIndex,
            out int majorIndex,
            out int minorIndex)
        {
            if (boxType == BoxType.Normal)
            {
                minorIndex = footprintIndex % minorCount;
                int majorFromRight = footprintIndex / minorCount;
                majorIndex = Mathf.Max(0, majorCount - 1 - majorFromRight);
                return;
            }

            majorIndex = footprintIndex % majorCount;
            minorIndex = footprintIndex / majorCount;
        }

        private static int CalculateSlotCount(float size, float spacing, float padding)
        {
            float usableCenterSpan = Mathf.Max(0f, size - (padding * 2f));
            return Mathf.Max(1, Mathf.FloorToInt(usableCenterSpan / spacing) + 1);
        }

        private bool TryGetBoxBounds(BoxType boxType, out Bounds bounds)
        {
            BoxTrigger trigger = GetBoxTrigger(boxType);
            if (trigger != null && trigger.TryGetComponent(out BoxCollider boxCollider))
            {
                bounds = boxCollider.bounds;
                return true;
            }

            bounds = default;
            return false;
        }

        private bool IsSlotWithinCalculatedCapacity(BoxType boxType, int slotIndex)
        {
            if (!fitGridToBoxBounds || !TryGetBoxBounds(boxType, out Bounds boxBounds))
            {
                return true;
            }

            SlotGrid grid = BuildBoundsGrid(boxType, boxBounds);
            int capacity = Mathf.Max(1, grid.MajorCount * grid.MinorCount * grid.Layers);
            return slotIndex < Mathf.Max(capacity, GetSlots(boxType).Length);
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
