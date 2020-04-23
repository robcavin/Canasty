using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ResetButton : MonoBehaviour
{
    public Collider selector;
    public CanastyController canastyController;

    private bool buttonEnabled = false;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (OVRInput.GetDown(OVRInput.RawButton.X) && buttonEnabled)
        {
            canastyController.OnReset();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        gameObject.GetComponent<Renderer>().material.color = Color.red;
        buttonEnabled = true;
    }

    private void OnTriggerExit(Collider other)
    {
        gameObject.GetComponent<Renderer>().material.color = Color.gray;
        buttonEnabled = false;
    }
}
