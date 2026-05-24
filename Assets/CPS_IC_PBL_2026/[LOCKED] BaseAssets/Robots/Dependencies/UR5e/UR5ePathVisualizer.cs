using UnityEngine;

namespace CPS.Lab10.UR5e
{
    [DisallowMultipleComponent]
    public class UR5ePathVisualizer : MonoBehaviour
    {
        [Header("Path Source")]
        [SerializeField] private Transform tcpTransform;
        [SerializeField] private LineRenderer lineRenderer;

        [Header("Recording")]
        [SerializeField] private bool recordOnStart = true;
        [SerializeField] private float minimumPointDistance = 0.01f;
        [SerializeField] private int maxPoints = 500;

        private Vector3[] points;
        private int pointCount;
        private bool isRecording;

        public bool IsRecording => isRecording;
        public int PointCount => pointCount;

        private void Awake()
        {
            points = new Vector3[Mathf.Max(2, maxPoints)];
        }

        private void Start()
        {
            if (recordOnStart)
            {
                StartRecording();
            }
        }

        private void LateUpdate()
        {
            if (!isRecording || tcpTransform == null)
            {
                return;
            }

            RecordPosition(tcpTransform.position);
        }

        public void StartRecording()
        {
            isRecording = true;

            if (tcpTransform != null && pointCount == 0)
            {
                RecordPosition(tcpTransform.position);
            }
        }

        public void StopRecording()
        {
            isRecording = false;
        }

        public void ClearPath()
        {
            pointCount = 0;
            UpdateLineRenderer();
        }

        private void RecordPosition(Vector3 position)
        {
            if (pointCount > 0 && Vector3.Distance(points[pointCount - 1], position) < minimumPointDistance)
            {
                return;
            }

            if (pointCount >= points.Length)
            {
                ShiftPointsLeft();
            }

            points[pointCount] = position;
            pointCount++;
            UpdateLineRenderer();
        }

        private void ShiftPointsLeft()
        {
            for (int i = 1; i < points.Length; i++)
            {
                points[i - 1] = points[i];
            }

            pointCount = points.Length - 1;
        }

        private void UpdateLineRenderer()
        {
            if (lineRenderer == null)
            {
                return;
            }

            lineRenderer.positionCount = pointCount;

            for (int i = 0; i < pointCount; i++)
            {
                lineRenderer.SetPosition(i, points[i]);
            }
        }
    }
}
