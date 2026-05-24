namespace CPS.ICPBL.Common
{
    /// <summary>
    /// 환경(컨베이어 큐·박스·시간) 상태를 조회하는 인터페이스.
    /// 양품/불량품 판단은 카메라 센서를 통해서만 가능 — 본 인터페이스로 직접 노출하지 않음.
    /// </summary>
    public interface IEnvironmentInfo
    {
        int GetQueueLength(int conveyorId);
        int GetBoxOccupancy(BoxType box);

        float CurrentTime { get; }
        float ProductionEndTime { get; }            // 생산 종료 시각 (초)
        float NextProductionAt(int conveyorId);     // 다음 생산 절대 시각
    }
}
