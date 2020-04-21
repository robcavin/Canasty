using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class CardController : MonoBehaviour
{
    public CanastyController canastyController = null;
    public SimpleGrabbable grabbable = null;

    public bool shouldReOrientOnPlayerProximity = false;
    float prevClosestDistance = float.MaxValue;

    // Start is called before the first frame update
    void Start()
    {
        if (canastyController == null)
            canastyController = GameObject.Find("CanastyController").GetComponent<CanastyController>();
    }

    // Update is called once per frame
    void Update()
    {
        if (grabbable && grabbable.isGrabbed && transform.hasChanged)
        {
            if (shouldReOrientOnPlayerProximity)
            {
                float distance = (canastyController.transform.position - transform.position).magnitude;
                if (distance < prevClosestDistance)
                {
                    var newAngle = distance > 0.2 ? 0 : distance < 0.1 ? 90 : 90 * (1 - 10 * (distance - 0.1f));
                    transform.localEulerAngles = new Vector3(newAngle, 0, 0);
                    prevClosestDistance = distance;
                    if (distance < 0.1f) shouldReOrientOnPlayerProximity = false;
                }
            }

            canastyController.OnTrackedObjectUpdate(gameObject);
            transform.hasChanged = false;
        }
        
    }
}
