using UnityEngine;

public class ColorArea : MonoBehaviour
{
    [Header("State")]
    [Tooltip("현재 감지한 색상")]
    public Color color = Color.white;

    [Tooltip("감지 대상이 없을 때의 기본 색상")]
    public Color defaultColor = Color.white;

    [Header("Filter")]
    [Tooltip("로봇 자신의 collider를 무시할지 여부")]
    public bool ignoreSameRoot = true;

    [Tooltip("비워두면 모든 태그를 감지")]
    public string requiredTag = "";

    [Tooltip("감지 가능한 레이어")]
    public LayerMask detectableLayers = ~0;

    [Tooltip("collider 자신에게 renderer가 없으면 parent에서 renderer를 찾음")]
    public bool searchRendererInParents = true;

    private Transform ownerRoot;
    private Renderer currentRenderer;
    private int detectedCount = 0;

    private void Awake()
    {
        ownerRoot = transform.root;
        ResetColor();
    }

    private void OnDisable()
    {
        ResetColor();
    }

    public void ResetColor()
    {
        detectedCount = 0;
        currentRenderer = null;
        color = defaultColor;
    }

    private bool IsValidTarget(Collider other)
    {
        if (other == null)
        {
            return false;
        }

        if (((1 << other.gameObject.layer) & detectableLayers.value) == 0)
        {
            return false;
        }

        if (ignoreSameRoot && other.transform.root == ownerRoot)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requiredTag) && !other.CompareTag(requiredTag))
        {
            return false;
        }

        return true;
    }

    private Renderer FindRenderer(Collider other)
    {
        if (other == null)
        {
            return null;
        }

        Renderer rendererOnSelf = other.GetComponent<Renderer>();
        if (rendererOnSelf != null)
        {
            return rendererOnSelf;
        }

        if (searchRendererInParents)
        {
            return other.GetComponentInParent<Renderer>();
        }

        return null;
    }

    private void UpdateColorFromRenderer(Renderer targetRenderer)
    {
        if (targetRenderer == null)
        {
            return;
        }

        currentRenderer = targetRenderer;
        color = currentRenderer.material.color;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsValidTarget(other))
        {
            return;
        }

        detectedCount++;
        Renderer targetRenderer = FindRenderer(other);
        UpdateColorFromRenderer(targetRenderer);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsValidTarget(other))
        {
            return;
        }

        Renderer targetRenderer = FindRenderer(other);
        UpdateColorFromRenderer(targetRenderer);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsValidTarget(other))
        {
            return;
        }

        detectedCount = Mathf.Max(0, detectedCount - 1);

        if (detectedCount == 0)
        {
            ResetColor();
        }
    }
}
