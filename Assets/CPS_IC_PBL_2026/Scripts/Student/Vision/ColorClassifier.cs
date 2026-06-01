using CPS.ICPBL.Common;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    [DisallowMultipleComponent]
    public sealed class ColorClassifier : MonoBehaviour, IColorClassifier
    {
        [Header("Reference Colors")]
        [SerializeField] private Color normalColor = StudentConstants.NormalBlue;
        [SerializeField] private Color abnormalColor = StudentConstants.AbnormalRed;
        [SerializeField] private Color defaultSensorColor = StudentConstants.DefaultSensorColor;

        [Header("Thresholds")]
        [SerializeField, Min(0f)] private float defaultColorThreshold =
            StudentConstants.ColorReliableThreshold;
        [SerializeField, Min(0f)] private float ambiguousDistanceDelta =
            StudentConstants.ColorAmbiguousDistanceDelta;

        public ColorClassificationResult Classify(Color sensedColor)
        {
            float blueDistance = CalculateRgbDistance(sensedColor, normalColor);
            float redDistance = CalculateRgbDistance(sensedColor, abnormalColor);

            if (IsCloseToDefaultColor(sensedColor) || sensedColor.a <= 0f)
            {
                return BuildResult(
                    sensedColor,
                    blueDistance,
                    redDistance,
                    ClassificationResult.Unknown,
                    false,
                    "No reliable sensed color.");
            }

            float distanceDelta = Mathf.Abs(blueDistance - redDistance);
            if (distanceDelta <= ambiguousDistanceDelta)
            {
                return BuildResult(
                    sensedColor,
                    blueDistance,
                    redDistance,
                    ClassificationResult.Unknown,
                    false,
                    "Sensed color is ambiguous.");
            }

            if (blueDistance < redDistance)
            {
                return BuildResult(
                    sensedColor,
                    blueDistance,
                    redDistance,
                    ClassificationResult.Normal,
                    true,
                    "Classified as Normal.");
            }

            return BuildResult(
                sensedColor,
                blueDistance,
                redDistance,
                ClassificationResult.Abnormal,
                true,
                "Classified as Abnormal.");
        }

        private void OnValidate()
        {
            defaultColorThreshold = Mathf.Max(0f, defaultColorThreshold);
            ambiguousDistanceDelta = Mathf.Max(0f, ambiguousDistanceDelta);
        }

        private bool IsCloseToDefaultColor(Color sensedColor)
        {
            return CalculateRgbDistance(sensedColor, defaultSensorColor) <= defaultColorThreshold;
        }

        private static float CalculateRgbDistance(Color a, Color b)
        {
            float dr = a.r - b.r;
            float dg = a.g - b.g;
            float db = a.b - b.b;
            return Mathf.Sqrt((dr * dr) + (dg * dg) + (db * db));
        }

        private static ColorClassificationResult BuildResult(
            Color sensedColor,
            float blueDistance,
            float redDistance,
            ClassificationResult result,
            bool reliable,
            string message)
        {
            return new ColorClassificationResult
            {
                result = result,
                sensedColor = sensedColor,
                blueDistance = blueDistance,
                redDistance = redDistance,
                reliable = reliable,
                message = message
            };
        }
    }
}
