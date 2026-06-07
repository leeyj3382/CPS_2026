using System;
using System.Collections.Generic;
using CPS.ICPBL.Environment;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    internal sealed class VirtualGuideGraph
    {
        internal enum GuidePathKind
        {
            Conveyor,
            NormalBox,
            AbnormalBox,
            Connector
        }

        internal sealed class GuidePath
        {
            public string Name;
            public GuidePathKind Kind;
            public int StationId;
            public Vector3[] Points;
        }

        internal struct GuideSegment
        {
            public Vector3 From;
            public Vector3 To;
        }

        private struct GuideNode
        {
            public Vector3 Position;
        }

        private struct GuideEdge
        {
            public int From;
            public int To;
        }

        private struct LocalEdge
        {
            public int To;
            public float Distance;
        }

        private sealed class EdgeNodeComparer : IComparer<int>
        {
            private readonly List<GuideNode> nodes;
            private readonly Vector3 from;
            private readonly Vector3 direction;

            public EdgeNodeComparer(
                List<GuideNode> nodes,
                Vector3 from,
                Vector3 to)
            {
                this.nodes = nodes;
                this.from = from;
                direction = FlattenXZ(to - from);
            }

            public int Compare(int left, int right)
            {
                float leftDistance = Vector3.Dot(
                    FlattenXZ(nodes[left].Position - from),
                    direction);
                float rightDistance = Vector3.Dot(
                    FlattenXZ(nodes[right].Position - from),
                    direction);
                return leftDistance.CompareTo(rightDistance);
            }
        }

        private const float CoordinateTolerance = 0.05f;
        private const float InfiniteCost = float.MaxValue;

        private readonly List<GuideNode> nodes = new List<GuideNode>(160);
        private readonly List<GuideEdge> edges = new List<GuideEdge>(256);
        private readonly List<GuideSegment> segments =
            new List<GuideSegment>(256);
        private readonly List<GuidePath> paths = new List<GuidePath>(40);
        private readonly Dictionary<int, Vector3> stationPositions =
            new Dictionary<int, Vector3>();

        public IReadOnlyList<GuidePath> Paths => paths;
        public IReadOnlyList<GuideSegment> Segments => segments;
        public bool IsBuilt => nodes.Count > 0 && edges.Count > 0;

        public void Rebuild(
            OperatingStations operatingStations,
            float minimumGuideSpacing)
        {
            nodes.Clear();
            edges.Clear();
            segments.Clear();
            paths.Clear();
            stationPositions.Clear();

            LoadStationPositions(operatingStations);
            BuildConnectorPaths(
                Mathf.Max(CoordinateTolerance, minimumGuideSpacing),
                out float[] gridX,
                out float[] gridZ);
            BuildStationPaths(gridX, gridZ);
        }

        public bool TryBuildRoute(
            Vector3 from,
            Vector3 to,
            float waypointMergeDistance,
            float turnSlowdownPenalty,
            Func<Vector3, Vector3, float> dynamicEdgeCost,
            List<Vector3> result,
            out float totalCost)
        {
            result.Clear();
            totalCost = 0f;

            if (!IsBuilt)
            {
                return false;
            }

            if (DistanceXZ(from, to) <= CoordinateTolerance)
            {
                result.Add(to);
                return true;
            }

            var localNodes = new List<GuideNode>(nodes);
            int startNode = AddEndpoint(
                localNodes,
                from,
                waypointMergeDistance);
            int goalNode = AddEndpoint(
                localNodes,
                to,
                waypointMergeDistance);
            List<LocalEdge>[] adjacency = BuildLocalAdjacency(localNodes);

            if (!TryRunDijkstra(
                localNodes,
                adjacency,
                startNode,
                goalNode,
                Mathf.Max(0f, turnSlowdownPenalty),
                dynamicEdgeCost,
                out List<int> nodePath,
                out totalCost))
            {
                return false;
            }

            result.Add(from);
            for (int i = 1; i < nodePath.Count; i++)
            {
                Vector3 point = localNodes[nodePath[i]].Position;
                point.y = to.y;
                AddSimplifiedPoint(result, point, CoordinateTolerance);
            }

            if (result.Count == 0
                || DistanceXZ(result[result.Count - 1], to)
                > CoordinateTolerance)
            {
                AddSimplifiedPoint(result, to, CoordinateTolerance);
            }
            else
            {
                result[result.Count - 1] = to;
            }

            if (result.Count > 0)
            {
                result.RemoveAt(0);
            }

            return result.Count > 0;
        }

        public void AppendAdjacentGuidePoints(
            Vector3 position,
            float maxDistance,
            List<Vector3> result)
        {
            if (!IsBuilt)
            {
                return;
            }

            float bestDistance = float.MaxValue;
            for (int i = 0; i < segments.Count; i++)
            {
                Vector3 closest = ClosestPointXZ(
                    position,
                    segments[i].From,
                    segments[i].To);
                bestDistance = Mathf.Min(
                    bestDistance,
                    DistanceXZ(position, closest));
            }

            float allowedDistance = Mathf.Max(
                CoordinateTolerance,
                bestDistance + 0.1f);
            float candidateLimit = Mathf.Max(0.5f, maxDistance) * 1.75f;
            for (int i = 0; i < segments.Count; i++)
            {
                GuideSegment segment = segments[i];
                Vector3 closest = ClosestPointXZ(
                    position,
                    segment.From,
                    segment.To);
                if (DistanceXZ(position, closest) > allowedDistance)
                {
                    continue;
                }

                if (DistanceXZ(position, closest) > 0.2f)
                {
                    AddCandidate(
                        result,
                        closest,
                        position,
                        candidateLimit);
                    continue;
                }

                AddCandidate(
                    result,
                    segment.From,
                    position,
                    candidateLimit);
                AddCandidate(
                    result,
                    segment.To,
                    position,
                    candidateLimit);
            }
        }

        private void BuildStationPaths(float[] gridX, float[] gridZ)
        {
            Vector3 normalBox = GetStationPosition(
                StudentConstants.NormalBoxStationId);
            Vector3 abnormalBox = GetStationPosition(
                StudentConstants.AbnormalBoxStationId);

            float westEntryX = gridX[0];
            float eastEntryX = gridX[gridX.Length - 1];
            float southEntryZ = gridZ[0];
            float northEntryZ = gridZ[gridZ.Length - 1];

            for (int stationId = StudentConstants.MinConveyorId;
                stationId <= 5;
                stationId++)
            {
                Vector3 station = GetStationPosition(stationId);
                Vector3 gridEntry = new Vector3(
                    westEntryX,
                    0f,
                    Mathf.Clamp(station.z, southEntryZ, northEntryZ));
                AddGuidePath(
                    string.Format("Station_Conveyor_{0:00}", stationId),
                    GuidePathKind.Conveyor,
                    stationId,
                    station,
                    new Vector3(westEntryX, 0f, station.z),
                    gridEntry);
            }

            for (int stationId = 6;
                stationId <= StudentConstants.MaxConveyorId;
                stationId++)
            {
                Vector3 station = GetStationPosition(stationId);
                Vector3 gridEntry = new Vector3(
                    Mathf.Clamp(station.x, westEntryX, eastEntryX),
                    0f,
                    northEntryZ);
                AddGuidePath(
                    string.Format("Station_Conveyor_{0:00}", stationId),
                    GuidePathKind.Conveyor,
                    stationId,
                    station,
                    new Vector3(station.x, 0f, northEntryZ),
                    gridEntry);
            }

            AddGuidePath(
                "Station_Normal",
                GuidePathKind.NormalBox,
                StudentConstants.NormalBoxStationId,
                normalBox,
                new Vector3(normalBox.x, 0f, southEntryZ));

            AddGuidePath(
                "Station_Abnormal",
                GuidePathKind.AbnormalBox,
                StudentConstants.AbnormalBoxStationId,
                abnormalBox,
                new Vector3(eastEntryX, 0f, abnormalBox.z));
        }

        private void BuildConnectorPaths(
            float minimumGuideSpacing,
            out float[] gridX,
            out float[] gridZ)
        {
            float minX = GetStationPosition(6).x;
            float maxX = GetStationPosition(
                StudentConstants.AbnormalBoxStationId).x;
            float minZ = GetStationPosition(
                StudentConstants.NormalBoxStationId).z;
            float maxZ = GetStationPosition(5).z;

            gridX = BuildEvenCoordinates(
                minX,
                maxX,
                minimumGuideSpacing);
            gridZ = BuildEvenCoordinates(
                minZ,
                maxZ,
                minimumGuideSpacing);

            for (int row = 0; row < gridZ.Length; row++)
            {
                var points = new Vector3[gridX.Length];
                for (int column = 0; column < gridX.Length; column++)
                {
                    points[column] = new Vector3(
                        gridX[column],
                        0f,
                        gridZ[row]);
                }

                AddConnector(
                    row + 1,
                    string.Format("Grid_H_{0:00}", row + 1),
                    points);
            }

            for (int column = 0; column < gridX.Length; column++)
            {
                var points = new Vector3[gridZ.Length];
                for (int row = 0; row < gridZ.Length; row++)
                {
                    points[row] = new Vector3(
                        gridX[column],
                        0f,
                        gridZ[row]);
                }

                AddConnector(
                    gridZ.Length + column + 1,
                    string.Format("Grid_V_{0:00}", column + 1),
                    points);
            }
        }

        private static float[] BuildEvenCoordinates(
            float minimum,
            float maximum,
            float minimumSpacing)
        {
            if (maximum < minimum)
            {
                float swap = minimum;
                minimum = maximum;
                maximum = swap;
            }

            float range = maximum - minimum;
            if (range <= CoordinateTolerance)
            {
                return new[] { minimum, maximum + minimumSpacing };
            }

            int segmentCount = Mathf.Max(
                1,
                Mathf.FloorToInt(range / minimumSpacing));
            float actualSpacing = range / segmentCount;
            var coordinates = new float[segmentCount + 1];
            for (int i = 0; i <= segmentCount; i++)
            {
                coordinates[i] = i == segmentCount
                    ? maximum
                    : minimum + actualSpacing * i;
            }

            return coordinates;
        }

        private void AddConnector(
            int connectorNumber,
            string suffix,
            params Vector3[] points)
        {
            AddGuidePath(
                string.Format(
                    "Connector_{0:00}_{1}",
                    connectorNumber,
                    suffix),
                GuidePathKind.Connector,
                StudentConstants.NoStationId,
                points);
        }

        private void AddGuidePath(
            string pathName,
            GuidePathKind kind,
            int stationId,
            params Vector3[] sourcePoints)
        {
            var cleanPoints = new List<Vector3>(sourcePoints.Length);
            for (int i = 0; i < sourcePoints.Length; i++)
            {
                Vector3 point = sourcePoints[i];
                point.y = 0f;
                if (cleanPoints.Count > 0
                    && DistanceXZ(
                        cleanPoints[cleanPoints.Count - 1],
                        point) <= CoordinateTolerance)
                {
                    cleanPoints[cleanPoints.Count - 1] = point;
                    continue;
                }

                cleanPoints.Add(point);
            }

            if (cleanPoints.Count < 2)
            {
                return;
            }

            Vector3[] points = cleanPoints.ToArray();
            paths.Add(new GuidePath
            {
                Name = pathName,
                Kind = kind,
                StationId = stationId,
                Points = points
            });

            for (int i = 1; i < points.Length; i++)
            {
                int fromNode = AddNode(points[i - 1]);
                int toNode = AddNode(points[i]);
                AddEdge(fromNode, toNode);
                segments.Add(new GuideSegment
                {
                    From = points[i - 1],
                    To = points[i]
                });
            }
        }

        private int AddNode(Vector3 position)
        {
            position.y = 0f;
            int existing = FindNode(nodes, position, CoordinateTolerance);
            if (existing >= 0)
            {
                return existing;
            }

            nodes.Add(new GuideNode { Position = position });
            return nodes.Count - 1;
        }

        private void AddEdge(int fromNode, int toNode)
        {
            if (fromNode == toNode)
            {
                return;
            }

            long edgeKey = GetEdgeKey(fromNode, toNode);
            for (int i = 0; i < edges.Count; i++)
            {
                if (GetEdgeKey(edges[i].From, edges[i].To) == edgeKey)
                {
                    return;
                }
            }

            edges.Add(new GuideEdge
            {
                From = fromNode,
                To = toNode
            });
        }

        private int AddEndpoint(
            List<GuideNode> localNodes,
            Vector3 endpoint,
            float mergeDistance)
        {
            float endpointTolerance = Mathf.Min(
                0.15f,
                Mathf.Max(CoordinateTolerance, mergeDistance));
            int existing = FindNode(
                localNodes,
                endpoint,
                endpointTolerance);
            if (existing >= 0)
            {
                return existing;
            }

            Vector3 projection = FindNearestGuidePoint(endpoint);
            int projectionNode = FindNode(
                localNodes,
                projection,
                CoordinateTolerance);
            if (projectionNode < 0)
            {
                projectionNode = localNodes.Count;
                localNodes.Add(new GuideNode { Position = projection });
            }

            if (DistanceXZ(endpoint, projection) <= CoordinateTolerance)
            {
                return projectionNode;
            }

            int endpointNode = localNodes.Count;
            localNodes.Add(new GuideNode { Position = endpoint });
            return endpointNode;
        }

        private List<LocalEdge>[] BuildLocalAdjacency(
            List<GuideNode> localNodes)
        {
            var adjacency = new List<LocalEdge>[localNodes.Count];
            for (int i = 0; i < adjacency.Length; i++)
            {
                adjacency[i] = new List<LocalEdge>(4);
            }

            var edgeKeys = new HashSet<long>();
            for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
            {
                GuideEdge edge = edges[edgeIndex];
                Vector3 edgeFrom = nodes[edge.From].Position;
                Vector3 edgeTo = nodes[edge.To].Position;
                var edgeNodes = new List<int>();
                for (int nodeIndex = 0;
                    nodeIndex < localNodes.Count;
                    nodeIndex++)
                {
                    if (IsPointOnSegmentXZ(
                        localNodes[nodeIndex].Position,
                        edgeFrom,
                        edgeTo))
                    {
                        edgeNodes.Add(nodeIndex);
                    }
                }

                edgeNodes.Sort(new EdgeNodeComparer(
                    localNodes,
                    edgeFrom,
                    edgeTo));
                for (int i = 1; i < edgeNodes.Count; i++)
                {
                    AddUndirectedEdge(
                        adjacency,
                        edgeNodes[i - 1],
                        edgeNodes[i],
                        edgeKeys,
                        localNodes);
                }
            }

            for (int i = nodes.Count; i < localNodes.Count; i++)
            {
                if (IsOnGuideSegment(localNodes[i].Position))
                {
                    continue;
                }

                Vector3 projection = FindNearestGuidePoint(
                    localNodes[i].Position);
                int projectionNode = FindNode(
                    localNodes,
                    projection,
                    CoordinateTolerance);
                if (projectionNode >= 0)
                {
                    AddUndirectedEdge(
                        adjacency,
                        i,
                        projectionNode,
                        edgeKeys,
                        localNodes);
                }
            }

            return adjacency;
        }

        private bool TryRunDijkstra(
            List<GuideNode> localNodes,
            List<LocalEdge>[] adjacency,
            int startNode,
            int goalNode,
            float turnSlowdownPenalty,
            Func<Vector3, Vector3, float> dynamicEdgeCost,
            out List<int> nodePath,
            out float totalCost)
        {
            int nodeCount = localNodes.Count;
            int stateCount = nodeCount * nodeCount;
            var costs = new float[stateCount];
            var visited = new bool[stateCount];
            var previousStates = new int[stateCount];
            for (int i = 0; i < stateCount; i++)
            {
                costs[i] = InfiniteCost;
                previousStates[i] = -1;
            }

            int startState = startNode * nodeCount + startNode;
            costs[startState] = 0f;
            int goalState = -1;

            for (int iteration = 0; iteration < stateCount; iteration++)
            {
                int currentState = FindLowestCostState(costs, visited);
                if (currentState < 0)
                {
                    break;
                }

                visited[currentState] = true;
                int previousNode = currentState / nodeCount;
                int currentNode = currentState % nodeCount;
                if (currentNode == goalNode)
                {
                    goalState = currentState;
                    break;
                }

                List<LocalEdge> currentEdges = adjacency[currentNode];
                for (int edgeIndex = 0;
                    edgeIndex < currentEdges.Count;
                    edgeIndex++)
                {
                    LocalEdge edge = currentEdges[edgeIndex];
                    if (edge.To == previousNode
                        && previousNode != currentNode)
                    {
                        continue;
                    }

                    float edgeCost = edge.Distance;
                    if (previousNode != currentNode)
                    {
                        Vector3 incoming = FlattenXZ(
                            localNodes[currentNode].Position
                            - localNodes[previousNode].Position);
                        Vector3 outgoing = FlattenXZ(
                            localNodes[edge.To].Position
                            - localNodes[currentNode].Position);
                        edgeCost += turnSlowdownPenalty
                            * (Vector3.Angle(incoming, outgoing) / 90f);
                    }

                    if (dynamicEdgeCost != null)
                    {
                        edgeCost += Mathf.Max(
                            0f,
                            dynamicEdgeCost(
                                localNodes[currentNode].Position,
                                localNodes[edge.To].Position));
                    }

                    int nextState = currentNode * nodeCount + edge.To;
                    float candidateCost = costs[currentState] + edgeCost;
                    if (candidateCost + 0.0001f >= costs[nextState])
                    {
                        continue;
                    }

                    costs[nextState] = candidateCost;
                    previousStates[nextState] = currentState;
                }
            }

            nodePath = new List<int>();
            totalCost = 0f;
            if (goalState < 0)
            {
                return false;
            }

            totalCost = costs[goalState];
            for (int state = goalState;
                state >= 0;
                state = previousStates[state])
            {
                int node = state % nodeCount;
                if (nodePath.Count == 0
                    || nodePath[nodePath.Count - 1] != node)
                {
                    nodePath.Add(node);
                }

                if (state == startState)
                {
                    break;
                }
            }

            nodePath.Reverse();
            return nodePath.Count > 0
                && nodePath[0] == startNode
                && nodePath[nodePath.Count - 1] == goalNode;
        }

        private static int FindLowestCostState(
            float[] costs,
            bool[] visited)
        {
            int bestState = -1;
            float bestCost = InfiniteCost;
            for (int i = 0; i < costs.Length; i++)
            {
                if (visited[i] || costs[i] >= bestCost)
                {
                    continue;
                }

                bestCost = costs[i];
                bestState = i;
            }

            return bestState;
        }

        private Vector3 FindNearestGuidePoint(Vector3 point)
        {
            Vector3 nearest = nodes[0].Position;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < segments.Count; i++)
            {
                Vector3 candidate = ClosestPointXZ(
                    point,
                    segments[i].From,
                    segments[i].To);
                float distance = DistanceXZ(point, candidate);
                if (distance >= nearestDistance)
                {
                    continue;
                }

                nearest = candidate;
                nearestDistance = distance;
            }

            nearest.y = point.y;
            return nearest;
        }

        private bool IsOnGuideSegment(Vector3 point)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                if (IsPointOnSegmentXZ(
                    point,
                    segments[i].From,
                    segments[i].To))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPointOnSegmentXZ(
            Vector3 point,
            Vector3 segmentFrom,
            Vector3 segmentTo)
        {
            Vector3 closest = ClosestPointXZ(
                point,
                segmentFrom,
                segmentTo);
            return DistanceXZ(point, closest) <= CoordinateTolerance;
        }

        private static void AddUndirectedEdge(
            List<LocalEdge>[] adjacency,
            int left,
            int right,
            HashSet<long> edgeKeys,
            List<GuideNode> sourceNodes)
        {
            if (left == right)
            {
                return;
            }

            long edgeKey = GetEdgeKey(left, right);
            if (!edgeKeys.Add(edgeKey))
            {
                return;
            }

            float distance = DistanceXZ(
                sourceNodes[left].Position,
                sourceNodes[right].Position);
            if (distance <= CoordinateTolerance)
            {
                return;
            }

            adjacency[left].Add(new LocalEdge
            {
                To = right,
                Distance = distance
            });
            adjacency[right].Add(new LocalEdge
            {
                To = left,
                Distance = distance
            });
        }

        private static long GetEdgeKey(int left, int right)
        {
            int min = Mathf.Min(left, right);
            int max = Mathf.Max(left, right);
            return ((long)min << 32) | (uint)max;
        }

        private static int FindNode(
            List<GuideNode> sourceNodes,
            Vector3 position,
            float tolerance)
        {
            for (int i = 0; i < sourceNodes.Count; i++)
            {
                if (DistanceXZ(sourceNodes[i].Position, position)
                    <= tolerance)
                {
                    return i;
                }
            }

            return -1;
        }

        private void LoadStationPositions(
            OperatingStations operatingStations)
        {
            for (int stationId = StudentConstants.MinConveyorId;
                stationId <= StudentConstants.MaxConveyorId;
                stationId++)
            {
                if (TryGetStationPosition(
                    operatingStations,
                    stationId,
                    out Vector3 position))
                {
                    stationPositions[stationId] = position;
                }
            }

            if (TryGetStationPosition(
                operatingStations,
                StudentConstants.NormalBoxStationId,
                out Vector3 normalBox))
            {
                stationPositions[
                    StudentConstants.NormalBoxStationId] = normalBox;
            }

            if (TryGetStationPosition(
                operatingStations,
                StudentConstants.AbnormalBoxStationId,
                out Vector3 abnormalBox))
            {
                stationPositions[
                    StudentConstants.AbnormalBoxStationId] = abnormalBox;
            }

            AddFallbackStationPositions();
        }

        private static bool TryGetStationPosition(
            OperatingStations operatingStations,
            int stationId,
            out Vector3 position)
        {
            if (operatingStations != null
                && operatingStations.TryGetStation(
                    stationId,
                    out OperatingStations.Station station))
            {
                position = station.BasePosition;
                position.y = 0f;
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        private void AddFallbackStationPositions()
        {
            AddFallback(1, new Vector3(-8f, 0f, -7f));
            AddFallback(2, new Vector3(-8f, 0f, -3f));
            AddFallback(3, new Vector3(-8f, 0f, 1f));
            AddFallback(4, new Vector3(-8f, 0f, 5f));
            AddFallback(5, new Vector3(-8f, 0f, 9f));
            AddFallback(6, new Vector3(-6.5f, 0f, 10.5f));
            AddFallback(7, new Vector3(-2.5f, 0f, 10.5f));
            AddFallback(8, new Vector3(1.5f, 0f, 10.5f));
            AddFallback(9, new Vector3(5.5f, 0f, 10.5f));
            AddFallback(10, new Vector3(9.5f, 0f, 10.5f));
            AddFallback(
                StudentConstants.NormalBoxStationId,
                new Vector3(0f, 0f, -6f));
            AddFallback(
                StudentConstants.AbnormalBoxStationId,
                new Vector3(8.5f, 0f, 2.5f));
        }

        private void AddFallback(int stationId, Vector3 position)
        {
            if (!stationPositions.ContainsKey(stationId))
            {
                stationPositions.Add(stationId, position);
            }
        }

        private Vector3 GetStationPosition(int stationId)
        {
            return stationPositions.TryGetValue(
                stationId,
                out Vector3 position)
                ? position
                : Vector3.zero;
        }

        private static Vector3 ClosestPointXZ(
            Vector3 point,
            Vector3 segmentFrom,
            Vector3 segmentTo)
        {
            Vector2 point2 = new Vector2(point.x, point.z);
            Vector2 from2 = new Vector2(segmentFrom.x, segmentFrom.z);
            Vector2 to2 = new Vector2(segmentTo.x, segmentTo.z);
            Vector2 segment = to2 - from2;
            float lengthSquared = segment.sqrMagnitude;
            float t = lengthSquared <= Mathf.Epsilon
                ? 0f
                : Mathf.Clamp01(
                    Vector2.Dot(point2 - from2, segment)
                    / lengthSquared);
            Vector2 closest = from2 + segment * t;
            return new Vector3(closest.x, point.y, closest.y);
        }

        private static void AddCandidate(
            List<Vector3> result,
            Vector3 candidate,
            Vector3 origin,
            float maxDistance)
        {
            float distance = DistanceXZ(candidate, origin);
            if (distance <= 0.2f || distance > maxDistance)
            {
                return;
            }

            for (int i = 0; i < result.Count; i++)
            {
                if (DistanceXZ(result[i], candidate)
                    <= CoordinateTolerance)
                {
                    return;
                }
            }

            candidate.y = origin.y;
            result.Add(candidate);
        }

        private static void AddSimplifiedPoint(
            List<Vector3> result,
            Vector3 point,
            float mergeDistance)
        {
            float tolerance = Mathf.Max(
                CoordinateTolerance,
                mergeDistance);
            if (result.Count > 0
                && DistanceXZ(
                    result[result.Count - 1],
                    point) <= tolerance)
            {
                result[result.Count - 1] = point;
                return;
            }

            if (result.Count >= 2)
            {
                Vector3 previous = result[result.Count - 1];
                Vector3 beforePrevious = result[result.Count - 2];
                Vector3 incoming = FlattenXZ(
                    previous - beforePrevious);
                Vector3 outgoing = FlattenXZ(point - previous);
                if (incoming.sqrMagnitude > 0.0001f
                    && outgoing.sqrMagnitude > 0.0001f
                    && Vector3.Angle(incoming, outgoing) <= 0.1f)
                {
                    result[result.Count - 1] = point;
                    return;
                }
            }

            result.Add(point);
        }

        private static Vector3 FlattenXZ(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private static float DistanceXZ(Vector3 left, Vector3 right)
        {
            float dx = left.x - right.x;
            float dz = left.z - right.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
