namespace CPS.ICPBL.Common
{
    /// <summary>한 로봇이 수행할 단일 픽-드롭 작업 단위.</summary>
    public struct TaskAssignment
    {
        public int ConveyorId;          // 1~10 (픽업할 컨베이어)
        public BoxType DropBox;         // Normal 또는 Abnormal (드롭할 박스)
        public int PalletSlotIndex;     // 박스 안의 슬롯 인덱스 (0-based)
    }
}
