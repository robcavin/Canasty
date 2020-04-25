using System.Collections;
using System.Collections.Generic;
using Unity.Cloud.UserReporting.Plugin;
using UnityEngine;

public class CanastyDebug : MonoBehaviour
{
    bool usageLogSent = false;

    // Start is called before the first frame update
    void Start()
    {
        DebugUIBuilder.instance.AddButton("Send usage log", LogButtonPressed);
        DebugUIBuilder.instance.Show();
    }

    void LogButtonPressed()
    {
        if (!usageLogSent)
            UnityUserReporting.CurrentClient.CreateUserReport((report) => {
                UnityUserReporting.CurrentClient.SendUserReport(report, (success, new_report) => {
                    DebugUIBuilder.instance.Hide();
                });
            });

        usageLogSent = true;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
