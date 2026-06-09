using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CPS.ICPBL.Student
{
    [DisallowMultipleComponent]
    public sealed class WorldGridOverlay : MonoBehaviour
    {
        private const float DefaultCellSize = 3f;
        private const int DefaultLabelFontSize = 32;
        private const float DefaultLabelCharacterSize = 0.12f;
        private static readonly Vector2 DefaultXRange = new Vector2(-12f, 12f);
        private static readonly Vector2 DefaultZRange = new Vector2(-9f, 12f);
        private static readonly Color DefaultLineColor = new Color(0.85f, 0.85f, 0.85f, 0.5f);
        private static readonly Color DefaultLabelColor = new Color(1f, 0.92f, 0.15f, 0.95f);

        [Header("Grid")]
        [SerializeField] private bool showGrid = true;
        [SerializeField, Min(0.1f)] private float cellSize = DefaultCellSize;
        [SerializeField] private Vector2 xRange = DefaultXRange;
        [SerializeField] private Vector2 zRange = DefaultZRange;
        [SerializeField, Min(0f)] private float lineHeight = 0.03f;
        [SerializeField, Min(0f)] private float labelHeight = 0.08f;

        [Header("Style")]
        [SerializeField, Min(0.001f)] private float lineWidth = 0.025f;
        [SerializeField] private Color lineColor = DefaultLineColor;
        [SerializeField] private Color labelColor = DefaultLabelColor;
        [SerializeField, Min(1)] private int labelFontSize = DefaultLabelFontSize;
        [SerializeField, Min(0.01f)] private float labelCharacterSize = DefaultLabelCharacterSize;
        [SerializeField] private bool billboardLabels = true;

        private readonly List<Transform> labels = new List<Transform>(128);
        private readonly List<GameObject> generatedObjects = new List<GameObject>(256);
        private Material lineMaterial;
        private bool built;

        private void Start()
        {
            if (!built)
            {
                Rebuild();
            }
        }

        private void LateUpdate()
        {
            if (!billboardLabels || labels.Count == 0)
            {
                return;
            }

            Camera targetCamera = Camera.main;
            if (targetCamera == null)
            {
                return;
            }

            for (int i = 0; i < labels.Count; i++)
            {
                Transform label = labels[i];
                if (label == null)
                {
                    continue;
                }

                Vector3 direction = label.position - targetCamera.transform.position;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    label.rotation = Quaternion.LookRotation(direction, Vector3.up);
                }
            }
        }

        private void OnDestroy()
        {
            ClearGeneratedObjects();
            if (lineMaterial != null)
            {
                Destroy(lineMaterial);
                lineMaterial = null;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGrid || Application.isPlaying)
            {
                return;
            }

            DrawEditorGrid(xRange, zRange, cellSize, lineHeight, lineColor, labelColor);
        }

        public static void DrawDefaultEditorGrid()
        {
            DrawEditorGrid(
                DefaultXRange,
                DefaultZRange,
                DefaultCellSize,
                0.03f,
                DefaultLineColor,
                DefaultLabelColor);
        }

        private static void DrawEditorGrid(
            Vector2 xRange,
            Vector2 zRange,
            float cellSize,
            float height,
            Color lineColor,
            Color labelColor)
        {
            float minX = Mathf.Min(xRange.x, xRange.y);
            float maxX = Mathf.Max(xRange.x, xRange.y);
            float minZ = Mathf.Min(zRange.x, zRange.y);
            float maxZ = Mathf.Max(zRange.x, zRange.y);
            float safeCellSize = Mathf.Max(0.1f, cellSize);
            int columnCount = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / safeCellSize));
            int rowCount = Mathf.Max(1, Mathf.CeilToInt((maxZ - minZ) / safeCellSize));

            maxX = minX + columnCount * safeCellSize;
            maxZ = minZ + rowCount * safeCellSize;

            Color previousColor = Gizmos.color;
            Gizmos.color = lineColor;
            for (int column = 0; column <= columnCount; column++)
            {
                float x = minX + column * safeCellSize;
                Gizmos.DrawLine(
                    new Vector3(x, height, minZ),
                    new Vector3(x, height, maxZ));
            }

            for (int row = 0; row <= rowCount; row++)
            {
                float z = minZ + row * safeCellSize;
                Gizmos.DrawLine(
                    new Vector3(minX, height, z),
                    new Vector3(maxX, height, z));
            }

            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12
            };
            labelStyle.normal.textColor = labelColor;

            int cellNumber = 1;
            for (int row = 0; row < rowCount; row++)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    Vector3 center = new Vector3(
                        minX + (column + 0.5f) * safeCellSize,
                        height + 0.05f,
                        minZ + (row + 0.5f) * safeCellSize);
                    Handles.Label(center, cellNumber.ToString(), labelStyle);
                    cellNumber++;
                }
            }

            Gizmos.color = previousColor;
        }
#endif

        public void Rebuild()
        {
            ClearGeneratedObjects();
            if (!showGrid)
            {
                built = false;
                return;
            }

            float minX = Mathf.Min(xRange.x, xRange.y);
            float maxX = Mathf.Max(xRange.x, xRange.y);
            float minZ = Mathf.Min(zRange.x, zRange.y);
            float maxZ = Mathf.Max(zRange.x, zRange.y);
            float safeCellSize = Mathf.Max(0.1f, cellSize);
            int columnCount = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / safeCellSize));
            int rowCount = Mathf.Max(1, Mathf.CeilToInt((maxZ - minZ) / safeCellSize));

            maxX = minX + columnCount * safeCellSize;
            maxZ = minZ + rowCount * safeCellSize;

            EnsureLineMaterial();
            for (int column = 0; column <= columnCount; column++)
            {
                float x = minX + column * safeCellSize;
                CreateLine(
                    string.Format("GridLine_X_{0}", column),
                    new Vector3(x, lineHeight, minZ),
                    new Vector3(x, lineHeight, maxZ));
            }

            for (int row = 0; row <= rowCount; row++)
            {
                float z = minZ + row * safeCellSize;
                CreateLine(
                    string.Format("GridLine_Z_{0}", row),
                    new Vector3(minX, lineHeight, z),
                    new Vector3(maxX, lineHeight, z));
            }

            int cellNumber = 1;
            for (int row = 0; row < rowCount; row++)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    Vector3 center = new Vector3(
                        minX + (column + 0.5f) * safeCellSize,
                        labelHeight,
                        minZ + (row + 0.5f) * safeCellSize);
                    CreateLabel(cellNumber.ToString(), center);
                    cellNumber++;
                }
            }

            built = true;
        }

        public int GetCellNumber(Vector3 worldPosition)
        {
            float minX = Mathf.Min(xRange.x, xRange.y);
            float minZ = Mathf.Min(zRange.x, zRange.y);
            float maxX = Mathf.Max(xRange.x, xRange.y);
            float maxZ = Mathf.Max(zRange.x, zRange.y);
            float safeCellSize = Mathf.Max(0.1f, cellSize);

            if (worldPosition.x < minX
                || worldPosition.x >= maxX
                || worldPosition.z < minZ
                || worldPosition.z >= maxZ)
            {
                return StudentConstants.NoStationId;
            }

            int columnCount = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / safeCellSize));
            int column = Mathf.FloorToInt((worldPosition.x - minX) / safeCellSize);
            int row = Mathf.FloorToInt((worldPosition.z - minZ) / safeCellSize);
            return row * columnCount + column + 1;
        }

        private void CreateLine(string objectName, Vector3 from, Vector3 to)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(transform, false);
            generatedObjects.Add(lineObject);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;
            line.startColor = lineColor;
            line.endColor = lineColor;
            line.material = lineMaterial;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = 50;
        }

        private void CreateLabel(string text, Vector3 position)
        {
            GameObject labelObject = new GameObject(string.Format("GridCell_{0}", text));
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.position = position;
            generatedObjects.Add(labelObject);
            labels.Add(labelObject.transform);

            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = labelFontSize;
            label.characterSize = labelCharacterSize;
            label.fontStyle = FontStyle.Bold;
            label.color = labelColor;

            MeshRenderer renderer = labelObject.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sortingOrder = 60;
            }
        }

        private void EnsureLineMaterial()
        {
            if (lineMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                return;
            }

            lineMaterial = new Material(shader)
            {
                name = "Student Grid Overlay Line Material"
            };
            lineMaterial.color = lineColor;
        }

        private void ClearGeneratedObjects()
        {
            labels.Clear();
            for (int i = 0; i < generatedObjects.Count; i++)
            {
                GameObject generatedObject = generatedObjects[i];
                if (generatedObject != null)
                {
                    Destroy(generatedObject);
                }
            }

            generatedObjects.Clear();
        }
    }
}
