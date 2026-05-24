using UnityEngine;

public class ColorSensor : MonoBehaviour
{
    [Header("Sensing Area")]
    [Tooltip("컬러 센서의 Trigger Area")]
    public ColorArea area;

    [Header("Area Size")]
    public float width = 0.1f;
    public float height = 0.05f;
    public float length = 0.6f;

    [Header("Area Position")]
    public float lateralOffset = 0f;
    public float verticalOffset = 0f;
    public float distance = 0.4f;

    [Header("Area Rotation")]
    public Vector3 localEulerAngles = Vector3.zero;

    [Header("Setup")]
    public bool applyOnStart = true;
    public bool applyOnValidate = true;

    private void Start()
    {
        if (applyOnStart)
        {
            ApplyAreaTransform();
        }
    }

    private void OnValidate()
    {
        if (applyOnValidate)
        {
            ApplyAreaTransform();
        }
    }

    public void ApplyAreaTransform()
    {
        if (area == null)
        {
            return;
        }

        Transform areaTransform = area.transform;
        areaTransform.localScale = new Vector3(width, height, length);
        areaTransform.localPosition = new Vector3(lateralOffset, verticalOffset, distance);
        areaTransform.localEulerAngles = localEulerAngles;
    }
}
