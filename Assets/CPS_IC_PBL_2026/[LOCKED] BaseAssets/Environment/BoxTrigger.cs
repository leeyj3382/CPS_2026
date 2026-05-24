using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CPS.ICPBL.Environment;

public class BoxTrigger : MonoBehaviour
{
    public bool isNormalBox;
    public int correctCount = 0;
    public int wrongCount = 0;

    // 팔레타이징 슬롯 점유 추적. slotIdx 의 의미·좌표는 학생이 정의.
    [Header("Slot Occupancy")]
    [Tooltip("이 박스가 받아낼 cube 수 한도. Normal Box=52, Abnormal Box=12 권장 (spawn 수와 일치).")]
    [SerializeField] private int slotCount = 52;

    private int[] slotOccupancy;   // 0 = 비어있음, 1 = 점유

    public int SlotCount => slotCount;
    public int OccupiedSlotCount { get; private set; } = 0;

    // 박스 측벽 collider — base infrastructure.
    // 박스 prefab visual mesh 는 collider 없어 cube 가 통과하므로 Awake 에서 4면 BoxCollider 자동 부착.
    // 측벽 두께가 너무 얇으면 depenetration 시 cube 가 측벽 통과 가능.
    private const bool autoCreateWalls = true;
    private const float wallThickness = 0.15f;

    void Awake()
    {
        slotOccupancy = new int[slotCount];
        if (autoCreateWalls) CreateBoxWalls();
    }

    /// <summary>박스 4면 측벽 자동 생성 (world-space BoxCollider). BoxTrigger 의 trigger BoxCollider bounds 를 기준으로 측정.</summary>
    private void CreateBoxWalls()
    {
        var trigger = GetComponent<BoxCollider>();
        if (trigger == null)
        {
            Debug.LogWarning($"[BoxTrigger {name}] No BoxCollider — wall auto-create skipped.");
            return;
        }

        Bounds b = trigger.bounds;
        float hx = b.extents.x, hy = b.extents.y, hz = b.extents.z;
        float t = wallThickness;
        Vector3 c = b.center;

        // 4 면 측벽 — world-axis aligned. (박스 prefab 의 X 축 -90° 회전을 고려해 world bounds 사용)
        CreateWallAt("Wall_-X", c + new Vector3(-hx - t * 0.5f, 0f, 0f), new Vector3(t, 2f * hy, 2f * (hz + t)));
        CreateWallAt("Wall_+X", c + new Vector3( hx + t * 0.5f, 0f, 0f), new Vector3(t, 2f * hy, 2f * (hz + t)));
        CreateWallAt("Wall_-Z", c + new Vector3(0f, 0f, -hz - t * 0.5f), new Vector3(2f * hx, 2f * hy, t));
        CreateWallAt("Wall_+Z", c + new Vector3(0f, 0f,  hz + t * 0.5f), new Vector3(2f * hx, 2f * hy, t));

        Debug.Log($"[BoxTrigger {name}] Auto-created 4 walls around bounds center={c} size=(±{hx:F2}, ±{hy:F2}, ±{hz:F2}).");
    }

    private void CreateWallAt(string wallName, Vector3 worldPos, Vector3 worldSize)
    {
        var go = new GameObject($"{name}_{wallName}");
        // Scene root 의 자식 — lossyScale 영향 없음
        go.transform.position = worldPos;
        var bc = go.AddComponent<BoxCollider>();
        bc.size = worldSize;
        bc.isTrigger = false;
    }

    // 같은 RealProduct 가 부모·자식 두 collider 로 두 번 enter/exit 호출되는 것을 dedup.
    // 한 cube 가 진입 = +1 (correct or wrong) 만, 나갈 때 -1.
    private readonly System.Collections.Generic.HashSet<int> trackedProductIds = new System.Collections.Generic.HashSet<int>();

    private void OnTriggerEnter(Collider other)
    {
        if (other.tag != "Product") return;
        var product = other.GetComponentInParent<RealProduct>();
        if (product == null) return;
        int id = product.GetInstanceID();
        if (!trackedProductIds.Add(id)) return;   // 이미 카운트됨

        if (isNormalBox == product.isNormal) correctCount++;
        else wrongCount++;
        // 카운트만 갱신. cube 는 자기 dynamic Rigidbody 로 자연 낙하해 박스 안 안착.
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.tag != "Product") return;
        var product = other.GetComponentInParent<RealProduct>();
        if (product == null) return;
        int id = product.GetInstanceID();
        if (!trackedProductIds.Remove(id)) return;   // 이미 정리됨

        if (isNormalBox == product.isNormal && correctCount >= 1) correctCount--;
        else if (isNormalBox != product.isNormal && wrongCount >= 1) wrongCount--;
    }

    /// <summary>
    /// RobotController.PlaceAtBoxSlot(box, slotIndex) 등 베이스 코드가 슬롯에 적재할 때 호출.
    /// 중복 점유 또는 범위 밖 인덱스는 false 반환 + 경고 로그.
    /// </summary>
    public bool RegisterSlotPlacement(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slotCount)
        {
            Debug.LogWarning($"[BoxTrigger] Invalid slot index {slotIndex} (range: 0..{slotCount - 1}).");
            return false;
        }
        if (slotOccupancy[slotIndex] == 1)
        {
            Debug.LogWarning($"[BoxTrigger] Slot {slotIndex} already occupied.");
            return false;
        }
        slotOccupancy[slotIndex] = 1;
        OccupiedSlotCount++;
        return true;
    }

    public bool IsSlotOccupied(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slotCount) return false;
        return slotOccupancy[slotIndex] == 1;
    }

    /// <summary>슬롯 점유 해제 (Restart·디버그용).</summary>
    public void ClearSlotPlacement(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slotCount) return;
        if (slotOccupancy[slotIndex] == 1)
        {
            slotOccupancy[slotIndex] = 0;
            OccupiedSlotCount--;
        }
    }

}
