using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimpleGrabber : MonoBehaviour
{
    private Collider _otherCollider;
    public Collider otherCollider { get { return _otherCollider; } }
    public GameObject indicator;

    private LinkedList<Collider> colliders = new LinkedList<Collider>();

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
    }

    private Collider findClosestCollider()
    {
        float minDistance = float.MaxValue;
        Collider closestCollider = null;
        foreach (var collider in colliders)
        {
            var distance = Vector3.Distance(collider.transform.position, transform.position);
            if (distance < minDistance)
            {
                closestCollider = collider;
                minDistance = distance;
            }
        }

        return closestCollider;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<SimpleGrabbable>() == null)
            return;

        colliders.AddLast(other);
        _otherCollider = findClosestCollider();

        if (indicator != null)
            indicator.GetComponent<MeshRenderer>().material.color = Color.blue;
    }

    private void OnTriggerExit(Collider other)
    {
        colliders.Remove(other);
        _otherCollider = findClosestCollider();

        if ((indicator != null) && (colliders.Count == 0))
            indicator.GetComponent<MeshRenderer>().material.color = Color.white;
    }
}
