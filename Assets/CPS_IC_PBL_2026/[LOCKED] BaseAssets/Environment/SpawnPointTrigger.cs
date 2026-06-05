using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpawnPointTrigger : MonoBehaviour
{
    public GameObject triggerEnteredProduct;
    public GameObject lastExitedProduct;
    public bool triggerExited = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.tag == "Product")
        {
            triggerEnteredProduct = ResolveProductRoot(other);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.tag == "Product")
        {
            lastExitedProduct = ResolveProductRoot(other);
            triggerExited = true;
        }
    }

    private static GameObject ResolveProductRoot(Collider other)
    {
        RealProduct product = other.GetComponentInParent<RealProduct>();
        return product != null ? product.gameObject : other.gameObject;
    }
}
