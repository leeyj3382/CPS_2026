using CPS.ICPBL.Common;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    [DisallowMultipleComponent]
    public sealed class SimulatedCameraSensor : MonoBehaviour
    {
        [SerializeField] private global::ColorSensor colorSensor;
        [SerializeField] private global::ColorArea colorArea;
        [SerializeField] private Color defaultColor = Color.white;

        public global::ColorSensor ColorSensor => colorSensor;
        public global::ColorArea ColorArea => ResolveArea();

        public Color ReadColor()
        {
            global::ColorArea area = ResolveArea();
            return area != null ? area.color : defaultColor;
        }

        public bool TryReadColor(out Color color)
        {
            global::ColorArea area = ResolveArea();
            color = area != null ? area.color : defaultColor;
            return area != null;
        }

        public ColorClassificationResult Inspect(IColorClassifier classifier)
        {
            Color sensedColor = ReadColor();
            if (classifier != null)
            {
                return classifier.Classify(sensedColor);
            }

            return new ColorClassificationResult
            {
                result = ClassificationResult.Unknown,
                sensedColor = sensedColor,
                reliable = false,
                message = "IColorClassifier reference is missing."
            };
        }

        private void Reset()
        {
            ResolveSerializedReferences();
        }

        private void OnValidate()
        {
            ResolveSerializedReferences();
        }

        private global::ColorArea ResolveArea()
        {
            if (colorArea != null)
            {
                return colorArea;
            }

            return colorSensor != null ? colorSensor.area : null;
        }

        private void ResolveSerializedReferences()
        {
            if (colorSensor == null)
            {
                colorSensor = GetComponent<global::ColorSensor>();
            }

            if (colorArea == null)
            {
                global::ColorSensor sensor = colorSensor != null
                    ? colorSensor
                    : GetComponent<global::ColorSensor>();
                colorArea = sensor != null
                    ? sensor.area
                    : GetComponentInChildren<global::ColorArea>(true);
            }
        }
    }
}
