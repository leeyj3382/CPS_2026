namespace CPS.ICPBL.Student
{
    public enum RobotRuntimeState
    {
        Idle = 0,
        Reserved = 1,
        MovingToConveyor = 2,
        Picking = 3,
        Retracting = 4,
        Inspecting = 5,
        MovingToBox = 6,
        Placing = 7,
        Releasing = 8,
        Completed = 9,
        Failed = 10,
        WaitingForLock = 11,
        Stuck = 12
    }

    public enum TaskStatus
    {
        Pending = 0,
        Reserved = 1,
        Running = 2,
        Completed = 3,
        Failed = 4,
        Cancelled = 5
    }

    /// <summary>
    /// Student-side lock resource type extended with CentralZone and RobotArmZone.
    /// This does not replace the locked CPS.ICPBL.Common.ResourceType API type.
    /// </summary>
    public enum LockResourceType
    {
        Conveyor = 0,
        NormalBox = 1,
        AbnormalBox = 2,
        CentralZone = 3,
        RobotArmZone = 4
    }

    public enum MissionFailureReason
    {
        None = 0,
        QueueEmpty = 1,
        MoveTimeout = 2,
        GripFailed = 3,
        ClassificationFailed = 4,
        BoxLockFailed = 5,
        PlaceFailed = 6,
        CollisionRisk = 7,
        Unknown = 99
    }
}
