namespace CPS.ICPBL.Common
{
    /// <summary>카메라 색 분류 결과. Unknown은 임계 모호 영역에 대한 재검사 신호.</summary>
    public enum ClassificationResult
    {
        Normal,
        Abnormal,
        Unknown
    }
}
