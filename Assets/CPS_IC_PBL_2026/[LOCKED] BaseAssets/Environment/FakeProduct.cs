using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FakeProduct : MonoBehaviour
{
    public Vector3 moveTarget;
    public bool done = false;
    private float speed = 0.5f;
    
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        gameObject.transform.position = Vector3.MoveTowards(transform.position, moveTarget, Time.deltaTime* speed);
        if (Vector3.Distance(transform.position, moveTarget) < 0.001f)
        {
            done = true;
        }
    }
}
