using System;
using CPS.ICPBL.Common;
using NUnit.Framework;
using UnityEngine;

namespace CPS.ICPBL.Student.Tests
{
    public sealed class FleetManagerTests
    {
        private GameObject host;
        private FleetManager fleetManager;
        private FakeEnvironmentInfo environment;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("FleetManagerTests");
            fleetManager = host.AddComponent<FleetManager>();
            environment = new FakeEnvironmentInfo();
            fleetManager.ConfigureEnvironment(environment);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(host);
        }

        [Test]
        public void RunSchedulingCycle_CreatesOnlyOnePendingTaskPerNonEmptyConveyor()
        {
            environment.SetQueueLength(3, 1);

            fleetManager.RunSchedulingCycle();
            fleetManager.RunSchedulingCycle();

            Assert.That(fleetManager.Tasks, Has.Count.EqualTo(1));
            Assert.That(fleetManager.Tasks[0].conveyorId, Is.EqualTo(3));
            Assert.That(fleetManager.Tasks[0].status, Is.EqualTo(TaskStatus.Pending));
        }

        [Test]
        public void RunSchedulingCycle_DispatchesFullQueueBeforeNonFullQueue()
        {
            var robot = new FakeRobotAgent(StudentConstants.RobotAId);
            environment.SetQueueLength(1, 1);
            environment.SetQueueLength(2, StudentConstants.ConveyorQueueCapacity);
            fleetManager.ConfigureRobotAgents(robot, null);

            fleetManager.RunSchedulingCycle();

            Assert.That(robot.DispatchCount, Is.EqualTo(1));
            Assert.That(robot.LastRequest.conveyorId, Is.EqualTo(2));
            Assert.That(fleetManager.ReservedConveyorIds, Does.Contain(2));
        }

        [Test]
        public void RunSchedulingCycle_DoesNotAssignOneConveyorToBothRobots()
        {
            var robotA = new FakeRobotAgent(StudentConstants.RobotAId);
            var robotB = new FakeRobotAgent(StudentConstants.RobotBId);
            environment.SetQueueLength(1, 2);
            fleetManager.ConfigureRobotAgents(robotA, robotB);

            fleetManager.RunSchedulingCycle();

            Assert.That(robotA.DispatchCount, Is.EqualTo(1));
            Assert.That(robotB.DispatchCount, Is.Zero);
            Assert.That(fleetManager.ReservedConveyorIds, Is.EquivalentTo(new[] { 1 }));
        }

        [Test]
        public void MissionCompletion_ReleasesReservationAndCompletesTask()
        {
            var robot = new FakeRobotAgent(StudentConstants.RobotAId);
            environment.SetQueueLength(4, 1);
            fleetManager.ConfigureRobotAgents(robot, null);
            fleetManager.RunSchedulingCycle();

            robot.FinishMission(true);

            Assert.That(fleetManager.Tasks[0].status, Is.EqualTo(TaskStatus.Completed));
            Assert.That(fleetManager.ReservedConveyorIds, Is.Empty);
        }

        [Test]
        public void FailedMission_RetriesOnceThenMarksTaskFailed()
        {
            var robot = new FakeRobotAgent(StudentConstants.RobotAId);
            environment.SetQueueLength(5, 1);
            fleetManager.ConfigureRobotAgents(robot, null);
            fleetManager.RunSchedulingCycle();

            robot.FinishMission(false);

            Assert.That(fleetManager.Tasks[0].status, Is.EqualTo(TaskStatus.Pending));
            Assert.That(fleetManager.Tasks[0].retryCount, Is.EqualTo(1));
            Assert.That(fleetManager.ReservedConveyorIds, Is.Empty);

            fleetManager.RunSchedulingCycle();
            robot.FinishMission(false);

            Assert.That(robot.DispatchCount, Is.EqualTo(2));
            Assert.That(fleetManager.Tasks[0].status, Is.EqualTo(TaskStatus.Failed));
            Assert.That(fleetManager.Tasks[0].retryCount, Is.EqualTo(2));
            Assert.That(fleetManager.ReservedConveyorIds, Is.Empty);
        }

        private sealed class FakeEnvironmentInfo : IEnvironmentInfo
        {
            private readonly int[] queueLengths =
                new int[StudentConstants.MaxConveyorId + 1];

            public float CurrentTime { get; set; }
            public float ProductionEndTime => 220f;

            public int GetQueueLength(int conveyorId)
            {
                return queueLengths[conveyorId];
            }

            public int GetBoxOccupancy(BoxType box)
            {
                return 0;
            }

            public float NextProductionAt(int conveyorId)
            {
                return -1f;
            }

            public void SetQueueLength(int conveyorId, int queueLength)
            {
                queueLengths[conveyorId] = queueLength;
            }
        }

        private sealed class FakeRobotAgent : IRobotAgent
        {
            private Action<MissionResult> onFinished;

            public FakeRobotAgent(int robotId)
            {
                RobotId = robotId;
            }

            public int RobotId { get; }
            public RobotRuntimeState State { get; private set; } = RobotRuntimeState.Idle;
            public bool CanAcceptTask { get; private set; } = true;
            public MissionRequest LastRequest { get; private set; }
            public int DispatchCount { get; private set; }

            public void StartMission(MissionRequest request, Action<MissionResult> callback)
            {
                LastRequest = request;
                onFinished = callback;
                CanAcceptTask = false;
                State = RobotRuntimeState.MovingToConveyor;
                DispatchCount++;
            }

            public void FinishMission(bool success)
            {
                Assert.That(onFinished, Is.Not.Null, "No mission is awaiting completion.");

                Action<MissionResult> callback = onFinished;
                onFinished = null;
                CanAcceptTask = true;
                State = RobotRuntimeState.Idle;
                callback(new MissionResult
                {
                    taskId = LastRequest.taskId,
                    robotId = RobotId,
                    conveyorId = LastRequest.conveyorId,
                    success = success,
                    failureReason = success ? MissionFailureReason.None : MissionFailureReason.Unknown
                });
            }
        }
    }
}
