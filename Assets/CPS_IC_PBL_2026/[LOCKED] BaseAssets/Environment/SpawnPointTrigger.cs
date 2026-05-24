using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpawnPointTrigger : MonoBehaviour
{
    public GameObject triggerEnteredProduct;
    public bool triggerExited = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.tag == "Product")
        {
            triggerEnteredProduct = other.gameObject;        
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.tag == "Product")
        {
            triggerExited = true;
        }
    }
}
