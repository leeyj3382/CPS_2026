using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProductSpawner : MonoBehaviour
{
    public int proudctionPeriod = 0;
    public int proudctionAmount = 0;

    [Header("Fake products for animation")]
    public GameObject f_normalProduct;
    public GameObject f_abnormalProduct;

    [Header("Real products")]
    public GameObject normalProduct;
    public GameObject abnormalProduct;

    private GameObject tempProduct;

    private Transform spawnStart;
    private Transform spawnEnd;

    [SerializeField]
    private List<GameObject> spawnPoints;

    [SerializeField]
    private int queueCount = 0;
    public int QueueCount => queueCount;   // EnvironmentInfo·학생 코드에서 read-only 조회
    [SerializeField]
    private bool isNormal;
    public bool localProductionDone = false;

    [Header("Queue shift delay")]
    [Tooltip("Point 1 에서 cube 가 제거되고 다음 cube 가 그 자리로 이동하기까지 지연 시간 (초).")]
    [SerializeField] private float queueShiftDelay = 1.0f;
    private bool queueShiftPending = false;
    private int debugConveyorId = -1;

    void Awake()
    {
        spawnStart = gameObject.transform.Find("SpawnStart");
        spawnEnd = gameObject.transform.Find("SpawnEnd");
    }

    public void SetDebugConveyorId(int conveyorId)
    {
        debugConveyorId = conveyorId;
    }

    void Update()
    {
        // conveyor animation done -> instantiate real product
        if (tempProduct != null && tempProduct.GetComponent<FakeProduct>().done)
        {
            Destroy(tempProduct);

            // queue 가득(queueCount=3) 시 새 cube 는 마지막 spawn point 에 dynamic 으로 → 바닥 자연 낙하 (overflow).
            bool isOverflow = (queueCount == 3);
            int spawnIdx = Mathf.Min(queueCount, spawnPoints.Count - 1);

            GameObject temp;
            if (isNormal)
            {
                temp = Instantiate(normalProduct, spawnPoints[spawnIdx].transform.position, spawnPoints[spawnIdx].transform.rotation);
                temp.GetComponent<RealProduct>().isNormal = true;
            }
            else
            {
                temp = Instantiate(abnormalProduct, spawnPoints[spawnIdx].transform.position, spawnPoints[spawnIdx].transform.rotation);
                temp.GetComponent<RealProduct>().isNormal = false;
            }

            if (isOverflow)
            {
                StartCoroutine(MakeOverflowCubeDynamic(temp));
            }
            else
            {
                queueCount++;
            }
        }

        // queue poped → pull (2026: queueShiftDelay 후 이동, 즉시 이동 시 너무 빨리 보임)
        SpawnPointTrigger frontTrigger = GetFrontTrigger();
        if (frontTrigger != null && frontTrigger.triggerExited && !queueShiftPending)
        {
            GameObject expectedFront = frontTrigger.triggerEnteredProduct;
            GameObject exitObject = frontTrigger.lastExitedProduct;
            Debug.Log($"[Debug][ConveyorQueue] conveyor={GetConveyorId()} triggerExit object={FormatObject(exitObject)} expectedFront={FormatObject(expectedFront)} match={exitObject == expectedFront} queueBefore={queueCount} delay={queueShiftDelay:0.00}s.");
            queueShiftPending = true;
            StartCoroutine(QueueShiftAfterDelay(queueShiftDelay, expectedFront, exitObject));
        }
    }

    public GameObject GetQueueFrontObject()
    {
        SpawnPointTrigger frontTrigger = GetFrontTrigger();
        return frontTrigger != null ? frontTrigger.triggerEnteredProduct : null;
    }

    /// <summary>Queue 가 가득(4개) 찬 상태에서 새로 spawn 된 cube 를 dynamic 으로 풀어
    /// 컨베이어 끝에서 자연 낙하 → bottom (floor) 로 떨어짐. unplaced 카운트로 평가 반영.</summary>
    private IEnumerator MakeOverflowCubeDynamic(GameObject overflowCube)
    {
        if (overflowCube == null) yield break;
        // RealProduct.Awake 가 isKinematic=true 로 정적화 한 직후 dynamic 으로 풀어줌
        yield return null;
        yield return null;
        if (overflowCube == null) yield break;
        foreach (var rb in overflowCube.GetComponentsInChildren<Rigidbody>(true))
        {
            if (rb == null) continue;
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        Debug.Log($"[ProductSpawner {name}] Queue full — overflow cube {overflowCube.name} dropped (dynamic).");
    }

    private IEnumerator QueueShiftAfterDelay(float delay, GameObject expectedFront, GameObject exitObject)
    {
        yield return new WaitForSeconds(delay);

        bool matchesExpectedFront = expectedFront != null && exitObject == expectedFront;
        if (!matchesExpectedFront)
        {
            SpawnPointTrigger frontTrigger = GetFrontTrigger();
            if (frontTrigger != null)
            {
                frontTrigger.triggerExited = false;
            }

            Debug.LogWarning($"[Debug][ConveyorQueue] conveyor={GetConveyorId()} queueDecrement blocked object={FormatObject(exitObject)} expectedFront={FormatObject(expectedFront)} match=False queueBefore={queueCount}.");
            queueShiftPending = false;
            yield break;
        }

        if (queueCount == 2)
        {
            var p1 = spawnPoints[1].GetComponent<SpawnPointTrigger>().triggerEnteredProduct;
            if (p1 != null) p1.transform.position = spawnPoints[0].transform.position;
        }
        else if (queueCount == 3)
        {
            var p1 = spawnPoints[1].GetComponent<SpawnPointTrigger>().triggerEnteredProduct;
            var p2 = spawnPoints[2].GetComponent<SpawnPointTrigger>().triggerEnteredProduct;
            if (p1 != null) p1.transform.position = spawnPoints[0].transform.position;
            if (p2 != null) p2.transform.position = spawnPoints[1].transform.position;
        }
        spawnPoints[0].GetComponent<SpawnPointTrigger>().triggerExited = false;
        if (queueCount >= 1)
        {
            int before = queueCount;
            queueCount--;
            Debug.Log($"[Debug][ConveyorQueue] conveyor={GetConveyorId()} queueDecrement object={FormatObject(exitObject)} expectedFront={FormatObject(expectedFront)} match=True before={before} after={queueCount}.");
        }
        else
        {
            Debug.Log($"[Debug][ConveyorQueue] conveyor={GetConveyorId()} queueDecrement skipped object={FormatObject(exitObject)} expectedFront={FormatObject(expectedFront)} match=True queueCount={queueCount}.");
        }
        queueShiftPending = false;
    }

    private SpawnPointTrigger GetFrontTrigger()
    {
        if (spawnPoints == null || spawnPoints.Count == 0 || spawnPoints[0] == null)
        {
            return null;
        }

        return spawnPoints[0].GetComponent<SpawnPointTrigger>();
    }

    private int GetConveyorId()
    {
        return debugConveyorId;
    }

    private static string FormatObject(GameObject obj)
    {
        return obj != null
            ? $"{obj.name}#{obj.GetInstanceID()}"
            : "null";
    }

    public void spawn(bool normal)
    {
        if (normal)
        {
            tempProduct = Instantiate(f_normalProduct, spawnStart.position, spawnStart.rotation);
            isNormal = true;
        }
        else
        {
            tempProduct = Instantiate(f_abnormalProduct, spawnStart.position, spawnStart.rotation);
            isNormal=false;
        }
        tempProduct.GetComponent<FakeProduct>().moveTarget = spawnEnd.position;

    }
}
