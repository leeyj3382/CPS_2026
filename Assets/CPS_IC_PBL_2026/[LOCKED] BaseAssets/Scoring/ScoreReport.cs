namespace CPS.ICPBL.Scoring
{
    /// <summary>시뮬레이션 종료 시 ICPBLScorer.FinalizeAndReport() 가 반환하는 최종 채점 데이터.</summary>
    public struct ScoreReport
    {
        public int FinalScore;
        public float CompletionTime;

        public int CorrectCount;
        public int WrongCount;
        public int UnplacedCount;

        public float IdleA;
        public float IdleB;
        public float BothIdle;

        public int ResourceConflicts;
        public int NearCollisions;

        public int TimePenalty;
        public int IdlePenalty;
        public int ConflictPenalty;

        public override string ToString()
        {
            return
                $"[ScoreReport] Final={FinalScore}/15  time={CompletionTime:F2}s  " +
                $"correct={CorrectCount}  wrong={WrongCount}  unplaced={UnplacedCount}  " +
                $"idleA={IdleA:F1}s idleB={IdleB:F1}s bothIdle={BothIdle:F1}s  " +
                $"resConf={ResourceConflicts}  nearCol={NearCollisions}  " +
                $"penalties: time=-{TimePenalty} idle=-{IdlePenalty} conflict=-{ConflictPenalty}";
        }
    }
}
