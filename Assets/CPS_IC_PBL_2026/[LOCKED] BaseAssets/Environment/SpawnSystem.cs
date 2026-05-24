using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpawnSystem : MonoBehaviour
{
    public int seed = 1221;
    public bool globalProductionDone = false;
    private int doneCount = 0;

    [Header("Info")]
    public int totalProductNumber = 64;
    public int timeLimit = 180;
    public List<int> productionPeriod = new List<int>{15, 18, 20, 20, 30, 36, 45, 45, 60, 90};

    public ProductSpawner[] conveyors;

    // Deferred init. 시드 확정 후 Initialize()에서 셔플 + 코루틴 시작.
    private bool initialized = false;
    public bool IsInitialized => initialized;

    void Start()
    {
        // 의도적 공란. UISystem.afterSeedInput() → Initialize(seed) 호출 흐름.
    }

    /// <summary>
    /// 시드 확정 후 호출. Random.InitState → 셔플 → 코루틴 시작. 중복 호출 시 무시.
    /// </summary>
    public void Initialize(int seedValue)
    {
        if (initialized)
        {
            Debug.LogWarning("[SpawnSystem] Already initialized. Initialize() call ignored.");
            return;
        }

        seed = seedValue;
        Random.InitState(seed);   // 시드 기반 셔플 결정성 보장

        int abnormalNumber = (int)(totalProductNumber * 0.2);
        bool[] productArray = new bool[totalProductNumber];

        for (int i = 0; i < abnormalNumber; i++)
        {
            productArray[i] = false;
        }
        for (int i = abnormalNumber; i < totalProductNumber; i++)
        {
            productArray[i] = true;
        }
        productArray = ShuffleArray(productArray);

        Queue<bool> productQueue = new Queue<bool>(productArray);

        for (int i = 0; i < conveyors.Length; i++)
        {
            int productCount = timeLimit / productionPeriod[i];
            bool[] tempArray = new bool[productCount];
            for (int j = 0; j < productCount; j++)
            {
                tempArray[j] = productQueue.Dequeue();
            }

            StartCoroutine(StartProduction(i, productionPeriod[i], tempArray));
        }

        initialized = true;
        Debug.Log($"[SpawnSystem] Initialized — seed={seed}, total={totalProductNumber}, abnormal={abnormalNumber}, conveyors={conveyors.Length}");
    }

    void Update()
    {
        if (!initialized) return;   // deferred init 이전에는 비활성

        if (!globalProductionDone)
        {
            foreach (ProductSpawner conveyor in conveyors)
            {
                if (!conveyor.localProductionDone)
                {
                    break;
                }
                doneCount++;
            }
            if (doneCount == 10)
            {
                globalProductionDone = true;
            }
            else
            {
                doneCount = 0;
            }
        }
    }

    IEnumerator StartProduction(int conveyor_no, int period, bool[] productArray)
    {
        int productionAmount = timeLimit / period;
        for (int i = 0; i < productionAmount; i++)
        {
            yield return new WaitForSeconds(period);
            conveyors[conveyor_no].spawn(productArray[i]);
        }
        conveyors[conveyor_no].localProductionDone = true;
    }

    // Fisher-Yates
    public List<bool> ShuffleList(List<bool> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            bool temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
        return list;
    }
    public bool[] ShuffleArray(bool[] array)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            bool temp = array[i];
            array[i] = array[j];
            array[j] = temp;
        }
        return array;
    }
}
