using UnityEngine;
using CPS.Lab11.MobileManipulator;

/// <summary>
/// 컨베이어에서 spawn 되는 실제 픽업 대상 제품.
/// Awake 시 PickableObject + BoxCollider + kinematic Rigidbody 자동 부착.
/// SuctionGripper 가 candidate 로 인식, 픽업 시 dynamic 으로 전환되어 자연 낙하.
/// </summary>
public class RealProduct : MonoBehaviour
{
    public bool isNormal;

    private void Awake()
    {
        // 부모에 BoxCollider — PickableObject [RequireComponent(Collider)] 만족, 자식 cube 크기 일치.
        if (GetComponent<Collider>() == null)
        {
            var bc = gameObject.AddComponent<BoxCollider>();
            BoxCollider childBc = null;
            foreach (var c in GetComponentsInChildren<BoxCollider>(true))
            {
                if (c.gameObject != gameObject) { childBc = c; break; }
            }
            if (childBc != null)
            {
                bc.size = Vector3.Scale(childBc.size, childBc.transform.localScale);
                bc.center = childBc.transform.localPosition + Vector3.Scale(childBc.center, childBc.transform.localScale);
            }
            else
            {
                bc.size = Vector3.one;
            }
        }

        // 부모 Rigidbody kinematic — spawn 후 conveyor 에 정적 정렬 (안 떨어짐).
        var rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        rb.useGravity = false;
        rb.isKinematic = true;

        // 자식 Rigidbody 제거 — 부모-자식 둘 다 Rigidbody 면 분리. 부모만 유지 (compound collider 패턴).
        foreach (var childRb in GetComponentsInChildren<Rigidbody>(true))
        {
            if (childRb != null && childRb.gameObject != gameObject)
            {
                Destroy(childRb);
            }
        }

        // PickableObject — Lab 11 SuctionGripper 가 candidate 로 인식하기 위해 필수.
        if (GetComponent<PickableObject>() == null)
            gameObject.AddComponent<PickableObject>();
    }
}
