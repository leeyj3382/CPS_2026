using System.Collections.Generic;
using CPS.ICPBL.Common;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    public class Palletizer : MonoBehaviour, IPalletizer
    {
        private const float MinSlotSpacing = 0.01f;
        private const float SuctionFaceOffsetFromAttachPoint = 0f;

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

        [Header("Slot Grid")]
        [SerializeField] private int columns = 6;
        [SerializeField] private int rows = 3;
        [SerializeField] private int abnormalColumns = 4;
        [SerializeField] private int abnormalRows = 3;
        [SerializeField] private int normalSlotCount = 52;
        [SerializeField] private int abnormalSlotCount = 12;
        [SerializeField] private Vector3 slotSpacing = new Vector3(0.3f, 0f, 0.28f);
        [SerializeField] private float layerHeight = 0.25f;
        [SerializeField] private float productSize = 0.25f;
        [SerializeField] private float sideClearance = 0.06f;
        [SerializeField] private float bottomClearance = 0f;
        [SerializeField] private Vector3 normalGridOriginOffset = new Vector3(-0.8f, 0f, -0.48f);
        [SerializeField] private Vector3 abnormalGridOriginOffset = new Vector3(-0.8f, 0f, -0.48f);
        [SerializeField] private Vector3 abnormalBoundsGridOffset = new Vector3(-0.04f, 0f, 0f);
        [SerializeField] private bool fitGridToBoxBounds = true;
        [SerializeField] private float horizontalPadding = 0.2f;
        [SerializeField] private float normalExtraHorizontalPadding = 0f;
        [SerializeField] private float abnormalExtraHorizontalPadding = 0f;
        [SerializeField] private float verticalPadding = 0.125f;

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
            normalExtraHorizontalPadding = Mathf.Max(0f, normalExtraHorizontalPadding);
            abnormalExtraHorizontalPadding = Mathf.Max(0f, abnormalExtraHorizontalPadding);
            verticalPadding = Mathf.Max(0f, verticalPadding);
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
            Vector3 productCenterPos = GetSlotWorldPosition(boxType, slotIndex);
            Vector3 placePos = GetToolPlacePosition(productCenterPos);
            return new BoxSlotPose
            {
                boxType = boxType,
                stationId = StudentConstants.GetBoxStationId(boxType),
                slotIndex = slotIndex,
                approachPos = ApplyVerticalOffset(placePos, placeApproachOffset),
                placePos = placePos,
                productCenterPos = productCenterPos,
                retractPos = ApplyVerticalOffset(placePos, placeRetractOffset),
                reserved = true,
                reservedByTaskId = taskId
            };
        }

        private Vector3 GetToolPlacePosition(Vector3 productCenterPos)
        {
            float verticalToolOffset = (productSize * 0.5f) + SuctionFaceOffsetFromAttachPoint;
            return productCenterPos + Vector3.up * verticalToolOffset;
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
                Vector3 gridPosition = GetGridPosition(grid, slotIndex);
                return boxType == BoxType.Abnormal
                    ? gridPosition + abnormalBoundsGridOffset
                    : gridPosition;
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
            int column = footprintIndex % boxColumns;
            int row = footprintIndex / boxColumns;
            Vector3 origin = boxType == BoxType.Normal
                ? normalGridOriginOffset
                : abnormalGridOriginOffset;

            return origin + new Vector3(
                column * slotSpacing.x,
                layerIndex * layerHeight,
                row * slotSpacing.z);
        }

        private SlotGrid BuildBoundsGrid(BoxType boxType, Bounds boxBounds)
        {
            float stepX = Mathf.Max(MinSlotSpacing, slotSpacing.x);
            float stepZ = Mathf.Max(MinSlotSpacing, slotSpacing.z);
            float stepY = Mathf.Max(Mathf.Max(MinSlotSpacing, layerHeight), productSize);
            float horizontalCenterInset = GetHorizontalCenterInset(boxType);
            float verticalCenterInset = GetVerticalCenterInset();
            bool majorAxisIsX = boxBounds.size.x >= boxBounds.size.z;

            int majorCount = GetConfiguredMajorCount(boxType);
            int minorCount = GetConfiguredMinorCount(boxType);
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
            float majorStep = GetFittedStep(
                majorMin,
                majorMax,
                majorCount,
                majorAxisIsX ? stepX : stepZ);
            float minorStepMagnitude = GetFittedStep(
                minorMin,
                minorMax,
                minorCount,
                majorAxisIsX ? stepZ : stepX);
            float majorStart = GetCenteredStart(majorMin, majorMax, majorCount, majorStep);
            float minorStart = GetMinorStart(boxType, minorMin, minorMax, minorCount, majorAxisIsX, minorStepMagnitude);
            float minorStep = GetOutsideMinorStep(boxType, majorAxisIsX, minorStepMagnitude);

            return new SlotGrid
            {
                MajorCount = majorCount,
                MinorCount = minorCount,
                Layers = CalculateSlotCount(boxBounds.size.y, stepY, verticalCenterInset),
                MajorAxisIsX = majorAxisIsX,
                MajorStart = majorStart,
                MinorStart = minorStart,
                MajorStep = majorStep,
                MinorStep = minorStep,
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

        private float GetHorizontalCenterInset(BoxType boxType)
        {
            float extraPadding = boxType == BoxType.Normal
                ? normalExtraHorizontalPadding
                : boxType == BoxType.Abnormal
                    ? abnormalExtraHorizontalPadding
                    : 0f;
            return Mathf.Max(
                horizontalPadding + extraPadding,
                productSize * 0.5f + sideClearance);
        }

        private float GetVerticalCenterInset()
        {
            return Mathf.Max(verticalPadding, productSize * 0.5f + bottomClearance);
        }

        private static float GetCenteredStart(float min, float max, int count, float step)
        {
            float span = Mathf.Max(0f, (Mathf.Max(1, count) - 1) * step);
            return (min + max - span) * 0.5f;
        }

        private static float GetFittedStep(float min, float max, int count, float desiredStep)
        {
            int clampedCount = Mathf.Max(1, count);
            if (clampedCount <= 1)
            {
                return 0f;
            }

            float usableSpan = Mathf.Max(0f, max - min);
            if (usableSpan <= Mathf.Epsilon)
            {
                return Mathf.Max(MinSlotSpacing, desiredStep);
            }

            return Mathf.Max(
                MinSlotSpacing,
                Mathf.Min(Mathf.Max(MinSlotSpacing, desiredStep), usableSpan / (clampedCount - 1)));
        }

        private static Vector3 GetGridPosition(SlotGrid grid, int slotIndex)
        {
            int footprintCount = Mathf.Max(1, grid.MajorCount * grid.MinorCount);
            int footprintIndex = slotIndex % footprintCount;
            int layerIndex = slotIndex / footprintCount;
            int majorIndex = footprintIndex % grid.MajorCount;
            int minorIndex = footprintIndex / grid.MajorCount;
            float major = grid.MajorStart + (majorIndex * grid.MajorStep);
            float minor = grid.MinorStart + (minorIndex * grid.MinorStep);
            float y = grid.LayerStartY + (layerIndex * grid.StepY);

            return grid.MajorAxisIsX
                ? new Vector3(major, y, minor)
                : new Vector3(minor, y, major);
        }

        private float GetMinorStart(
            BoxType boxType,
            float min,
            float max,
            int count,
            bool majorAxisIsX,
            float stepMagnitude)
        {
            if (boxType == BoxType.Abnormal)
            {
                return GetCenteredStart(min, max, count, stepMagnitude);
            }

            if (boxType == BoxType.Normal)
            {
                float centeredStart = GetCenteredStart(min, max, count, stepMagnitude);
                return majorAxisIsX
                    ? centeredStart + ((Mathf.Max(1, count) - 1) * stepMagnitude)
                    : centeredStart;
            }

            return min;
        }

        private static float GetOutsideMinorStep(
            BoxType boxType,
            bool majorAxisIsX,
            float stepMagnitude)
        {
            if (boxType == BoxType.Normal && majorAxisIsX)
            {
                return -stepMagnitude;
            }

            return stepMagnitude;
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
            return slotIndex < capacity;
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
