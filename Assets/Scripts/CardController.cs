using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class CardController : MonoBehaviour
{
    public CanastyController canastyController = null;
    public SimpleGrabbable grabbable = null;


    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        if (grabbable && grabbable.isGrabbed && transform.hasChanged)
        {
            canastyController.OnTrackedObjectUpdate(gameObject);
            transform.hasChanged = false;
        }
        
    }
}
