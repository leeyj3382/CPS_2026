using System;
using CPS.ICPBL.Common;
using NUnit.Framework;
using UnityEngine;

namespace CPS.ICPBL.Student.Tests
{
    public sealed class PathPlannerTests
    {
        private GameObject plannerObject;
        private PathPlanner planner;
        private FakeRobotController robotA;
        private FakeRobotController robotB;

        [SetUp]
        public void SetUp()
        {
            plannerObject = new GameObject("PathPlannerTests");
            planner = plannerObject.AddComponent<PathPlanner>();
            robotA = new FakeRobotController(
                StudentConstants.RobotAId,
                new Vector3(-4f, 0f, 0f));
            robotB = new FakeRobotController(
                StudentConstants.RobotBId,
                new Vector3(0f, 0f, -4f));
            planner.ConfigureRobots(robotA, robotB);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(plannerObject);
        }

        [Test]
        public void CrossingPaths_GiveRobotAPriorityAndRequestRobotBDetour()
        {
            Vector3 robotATarget = new Vector3(4f, 0f, 0f);
            Vector3 robotBTarget = new Vector3(0f, 0f, 4f);

            planner.RegisterActiveBasePath(
                StudentConstants.RobotAId,
                1,
                robotA.Position,
                robotATarget,
                true);
            planner.RegisterActiveBasePath(
                StudentConstants.RobotBId,
                2,
                robotB.Position,
                robotBTarget,
                false);

            bool robotABlocked = planner.IsBasePathBlocked(
                StudentConstants.RobotAId,
                robotA.Position,
                robotATarget,
                out int robotABlocker);
            bool robotBBlocked = planner.IsBasePathBlocked(
                StudentConstants.RobotBId,
                StudentConstants.NoStationId,
                robotB.Position,
                robotBTarget,
                out int robotBBlocker,
                out bool waitForSameBox,
                out bool preferDetour);

            Assert.That(robotABlocked, Is.False);
            Assert.That(robotABlocker, Is.EqualTo(StudentConstants.UnassignedRobotId));
            Assert.That(robotBBlocked, Is.True);
            Assert.That(robotBBlocker, Is.EqualTo(StudentConstants.RobotAId));
            Assert.That(waitForSameBox, Is.False);
            Assert.That(preferDetour, Is.True);
        }

        [TestCase(StudentConstants.RobotAId)]
        [TestCase(StudentConstants.RobotBId)]
        public void BuildBaseRoute_ReturnsConnectedWaypoints(int robotId)
        {
            Vector3 start = new Vector3(-4f, 0f, -3f);
            Vector3 target = new Vector3(5f, 0f, 6f);

            var route = planner.BuildBaseRoute(
                robotId,
                StudentConstants.NoStationId,
                5,
                start,
                target);

            Assert.That(route, Is.Not.Empty);
            Vector3 segmentStart = start;
            for (int i = 0; i < route.Count; i++)
            {
                Vector3 segmentEnd = route[i];
                Assert.That(
                    Vector3.Distance(segmentStart, segmentEnd),
                    Is.GreaterThan(0.05f),
                    string.Format(
                        "Segment {0} has no length: {1} -> {2}.",
                        i,
                        segmentStart,
                        segmentEnd));
                segmentStart = segmentEnd;
            }

            Assert.That(segmentStart, Is.EqualTo(target));
        }

        [Test]
        public void BuildBaseRoute_ConveyorOneToNormalUsesBoxAlignedGridEdge()
        {
            Vector3 start = new Vector3(-8f, 0f, -7f);
            Vector3 target = new Vector3(0f, 0f, -6f);

            var route = planner.BuildBaseRoute(
                StudentConstants.RobotAId,
                1,
                StudentConstants.NormalBoxStationId,
                start,
                target);

            Assert.That(route.Count, Is.EqualTo(3));
            Assert.That(route[0], Is.EqualTo(new Vector3(-6.5f, 0f, -7f)));
            Assert.That(route[1], Is.EqualTo(new Vector3(-6.5f, 0f, -6f)));
            Assert.That(route[2], Is.EqualTo(target));
        }

        [Test]
        public void BuildBaseRoute_ConveyorTenToAbnormalUsesBoxAlignedGridEdge()
        {
            Vector3 start = new Vector3(9.5f, 0f, 10.5f);
            Vector3 target = new Vector3(8.5f, 0f, 2.5f);

            var route = planner.BuildBaseRoute(
                StudentConstants.RobotBId,
                10,
                StudentConstants.AbnormalBoxStationId,
                start,
                target);

            Assert.That(route.Count, Is.EqualTo(3));
            Assert.That(route[0], Is.EqualTo(new Vector3(9.5f, 0f, 9f)));
            Assert.That(route[1], Is.EqualTo(new Vector3(8.5f, 0f, 9f)));
            Assert.That(route[2], Is.EqualTo(target));
        }

        [Test]
        public void BuildYieldCandidates_UsesOnlyAxisAlignedMoves()
        {
            Vector3 start = new Vector3(1f, 0f, 2.5f);

            var candidates = planner.BuildYieldCandidates(
                StudentConstants.RobotBId,
                start,
                new Vector3(5f, 0f, 7f));

            Assert.That(candidates, Is.Not.Empty);
            for (int i = 0; i < candidates.Count; i++)
            {
                bool xUnchanged = Mathf.Approximately(start.x, candidates[i].x);
                bool zUnchanged = Mathf.Approximately(start.z, candidates[i].z);
                Assert.That(
                    xUnchanged || zUnchanged,
                    Is.True,
                    string.Format("Yield candidate {0} is diagonal.", candidates[i]));
            }
        }

        [Test]
        public void ConfigureRobots_DrawsRobotSizedSparseGuideGrid()
        {
            Transform guideNetwork = plannerObject.transform.Find("VirtualGuideNetwork");

            Assert.That(guideNetwork, Is.Not.Null);
            Assert.That(guideNetwork.childCount, Is.EqualTo(24));

            int stationLineCount = 0;
            int connectorLineCount = 0;
            int horizontalGridLineCount = 0;
            int verticalGridLineCount = 0;
            LineRenderer firstStationLine = null;
            LineRenderer firstConnectorLine = null;
            for (int i = 0; i < guideNetwork.childCount; i++)
            {
                Transform child = guideNetwork.GetChild(i);
                LineRenderer line = child.GetComponent<LineRenderer>();
                Assert.That(line, Is.Not.Null, child.name);

                if (child.name.StartsWith("Station_"))
                {
                    stationLineCount++;
                    firstStationLine = firstStationLine ?? line;
                }
                else if (child.name.StartsWith("Connector_"))
                {
                    connectorLineCount++;
                    firstConnectorLine = firstConnectorLine ?? line;
                    if (child.name.Contains("Grid_H_"))
                    {
                        horizontalGridLineCount++;
                    }
                    else if (child.name.Contains("Grid_V_"))
                    {
                        verticalGridLineCount++;
                    }
                }
            }

            Assert.That(stationLineCount, Is.EqualTo(10));
            Assert.That(connectorLineCount, Is.EqualTo(14));
            Assert.That(horizontalGridLineCount, Is.EqualTo(7));
            Assert.That(verticalGridLineCount, Is.EqualTo(7));
            Assert.That(
                guideNetwork.Find("Station_Normal"),
                Is.Null);
            Assert.That(
                guideNetwork.Find("Station_Abnormal"),
                Is.Null);
            Assert.That(firstStationLine, Is.Not.Null);
            Assert.That(firstConnectorLine, Is.Not.Null);
            Assert.That(
                firstStationLine.startWidth,
                Is.GreaterThan(firstConnectorLine.startWidth));
        }

        [Test]
        public void BuildBaseRoute_RobotBUsesAlternateGridColumnAroundRobotAPath()
        {
            const float innerLeftX = -1.5f;
            const float centerX = 1f;
            const float lowerZ = -3.5f;
            const float crossingZ = 1.5f;
            const float upperZ = 6.5f;

            robotA = new FakeRobotController(
                StudentConstants.RobotAId,
                new Vector3(innerLeftX, 0f, crossingZ));
            robotB = new FakeRobotController(
                StudentConstants.RobotBId,
                new Vector3(centerX, 0f, lowerZ));
            planner.ConfigureRobots(robotA, robotB);

            planner.RegisterActiveBasePath(
                StudentConstants.RobotAId,
                1,
                robotA.Position,
                new Vector3(centerX, 0f, crossingZ),
                false);

            var route = planner.BuildBaseRoute(
                StudentConstants.RobotBId,
                StudentConstants.NoStationId,
                StudentConstants.NoStationId,
                robotB.Position,
                new Vector3(centerX, 0f, upperZ));

            Assert.That(route, Is.Not.Empty);
            Assert.That(
                route,
                Has.Some.Matches<Vector3>(
                    point => Mathf.Abs(point.x - centerX) > 0.5f));
            Assert.That(
                route[route.Count - 1],
                Is.EqualTo(new Vector3(centerX, 0f, upperZ)));
        }

        [Test]
        public void HeadOnPaths_KeepRobotAMovingAndMakeRobotBYield()
        {
            robotA = new FakeRobotController(
                StudentConstants.RobotAId,
                new Vector3(-2f, 0f, 0f));
            robotB = new FakeRobotController(
                StudentConstants.RobotBId,
                new Vector3(2f, 0f, 0f));
            planner.ConfigureRobots(robotA, robotB);

            Vector3 robotATarget = new Vector3(4f, 0f, 0f);
            Vector3 robotBTarget = new Vector3(-4f, 0f, 0f);
            planner.RegisterActiveBasePath(
                StudentConstants.RobotAId,
                1,
                robotA.Position,
                robotATarget,
                false);
            planner.RegisterActiveBasePath(
                StudentConstants.RobotBId,
                2,
                robotB.Position,
                robotBTarget,
                false);

            bool robotABlocked = planner.IsBasePathBlocked(
                StudentConstants.RobotAId,
                StudentConstants.NoStationId,
                robotA.Position,
                robotATarget,
                out int robotABlocker,
                out _,
                out _);
            bool robotBBlocked = planner.IsBasePathBlocked(
                StudentConstants.RobotBId,
                StudentConstants.NoStationId,
                robotB.Position,
                robotBTarget,
                out int robotBBlocker,
                out _,
                out bool robotBShouldYield);

            Assert.That(robotABlocked, Is.False);
            Assert.That(
                robotABlocker,
                Is.EqualTo(StudentConstants.UnassignedRobotId));
            Assert.That(robotBBlocked, Is.True);
            Assert.That(robotBBlocker, Is.EqualTo(StudentConstants.RobotAId));
            Assert.That(robotBShouldYield, Is.True);
        }

        [Test]
        public void RegisterActivePaths_DrawsRobotColorsAndCrossingPoint()
        {
            planner.RegisterActiveBasePath(
                StudentConstants.RobotAId,
                1,
                robotA.Position,
                new Vector3(4f, 0f, 0f),
                true);
            planner.RegisterActiveBasePath(
                StudentConstants.RobotBId,
                2,
                robotB.Position,
                new Vector3(0f, 0f, 4f),
                true);

            LineRenderer robotALine = FindLine("RobotAPath");
            LineRenderer robotBLine = FindLine("RobotBPath");
            Transform crossingPoint = plannerObject.transform.Find(
                "RobotPathCrossingPoint");

            Assert.That(robotALine, Is.Not.Null);
            Assert.That(robotBLine, Is.Not.Null);
            Assert.That(robotALine.startColor, Is.EqualTo(Color.red));
            Assert.That(robotBLine.startColor, Is.EqualTo(Color.blue));
            Assert.That(crossingPoint, Is.Not.Null);
            Assert.That(crossingPoint.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void RegisterActiveBaseRoute_DrawsEntireRemainingRoute()
        {
            Vector3[] route =
            {
                new Vector3(-4f, 0f, 4f),
                new Vector3(4f, 0f, 4f)
            };

            planner.RegisterActiveBaseRoute(
                StudentConstants.RobotAId,
                1,
                robotA.Position,
                route,
                0,
                false);

            LineRenderer line = FindLine("RobotAPath");
            Assert.That(line, Is.Not.Null);
            Assert.That(line.positionCount, Is.EqualTo(3));
            Assert.That(
                line.GetPosition(2).x,
                Is.EqualTo(route[1].x).Within(0.001f));
            Assert.That(
                line.GetPosition(2).z,
                Is.EqualTo(route[1].z).Within(0.001f));
        }

        [Test]
        public void UpdateActiveBasePathProgress_RemovesTravelledLine()
        {
            Vector3[] route =
            {
                new Vector3(-4f, 0f, 4f),
                new Vector3(4f, 0f, 4f)
            };
            planner.RegisterActiveBaseRoute(
                StudentConstants.RobotAId,
                1,
                robotA.Position,
                route,
                0,
                false);

            Vector3 current = new Vector3(-4f, 0f, 2f);
            planner.UpdateActiveBasePathProgress(
                StudentConstants.RobotAId,
                1,
                current);

            LineRenderer line = FindLine("RobotAPath");
            Assert.That(line, Is.Not.Null);
            Assert.That(
                line.GetPosition(0).x,
                Is.EqualTo(current.x).Within(0.001f));
            Assert.That(
                line.GetPosition(0).z,
                Is.EqualTo(current.z).Within(0.001f));
        }

        [Test]
        public void FutureRouteIntersection_GivesRobotAPriorityOverRobotB()
        {
            planner.RegisterActiveBaseRoute(
                StudentConstants.RobotAId,
                1,
                robotA.Position,
                new[]
                {
                    new Vector3(-4f, 0f, 4f),
                    new Vector3(4f, 0f, 4f)
                },
                0,
                false);
            planner.RegisterActiveBaseRoute(
                StudentConstants.RobotBId,
                2,
                robotB.Position,
                new[]
                {
                    new Vector3(0f, 0f, 2f),
                    new Vector3(0f, 0f, 6f)
                },
                0,
                false);

            bool blocked = planner.IsBasePathBlocked(
                StudentConstants.RobotBId,
                StudentConstants.NoStationId,
                robotB.Position,
                new Vector3(0f, 0f, 2f),
                out int blockingRobotId,
                out bool waitForSameBox,
                out bool preferDetour);

            Assert.That(blocked, Is.True);
            Assert.That(
                blockingRobotId,
                Is.EqualTo(StudentConstants.RobotAId));
            Assert.That(waitForSameBox, Is.False);
            Assert.That(preferDetour, Is.True);
        }

        private LineRenderer FindLine(string objectName)
        {
            Transform child = plannerObject.transform.Find(objectName);
            return child != null ? child.GetComponent<LineRenderer>() : null;
        }

        private sealed class FakeRobotController : IRobotController
        {
            public FakeRobotController(int robotId, Vector3 position)
            {
                RobotId = robotId;
                Position = position;
            }

            public int RobotId { get; }
            public Vector3 Position { get; private set; }
            public bool IsBusy { get; private set; } = true;

            public void GoToOperatingStation(int stationId)
            {
            }

            public void MoveBaseTo(Vector3 worldPos, Action onArrived = null)
            {
                Position = worldPos;
                IsBusy = false;
                onArrived?.Invoke();
            }

            public void MoveArmTo(
                Vector3 worldPos,
                Quaternion worldRot,
                float duration = 1f,
                Action onArrived = null)
            {
                onArrived?.Invoke();
            }
        }
    }
}
