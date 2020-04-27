using System.Collections;
using System.Collections.Generic;
using Unity.Cloud.UserReporting.Plugin;
using UnityEngine;
using UnityEngine.UI;

public class CanastyDebug : MonoBehaviour
{
    bool usageLogSent = false;
    public CanastyController canastyController;
    List<Text> labelTexts = new List<Text>();

    // Start is called before the first frame update
    void Start()
    {
        DebugUIBuilder.instance.AddButton("Send usage log", LogButtonPressed);
        for (int i = 0; i < 6; i++)
        {
            var label = DebugUIBuilder.instance.AddLabel("");
            labelTexts.Add(label.GetComponent<Text>());
        }
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
        var connectionStates = canastyController.remoteConnectionStates;

        labelTexts[0].text = canastyController.ReadyToPlay() ? "Ready to Play" : "Waiting on users";

        int labelIndex = 1;
        foreach (var connection in connectionStates)
        {
            var c = connection.Value;

            if (c.username == null)
                c.username = canastyController.GetUsernameForID(connection.Key);

            string username = c.username != null ? c.username : connection.Key.ToString();

            string netAttempts =
                c.networkState == CanastyController.ConnectionState.Connecting ? "T" + c.netTimeouts.ToString() :
                c.networkState == CanastyController.ConnectionState.Reconnecting ? "R" + c.netReconnects.ToString() :
                "";

            string voipAttempts =
                c.voipState == CanastyController.ConnectionState.Connecting ? "T" + c.voipTimeouts.ToString() :
                c.voipState == CanastyController.ConnectionState.Reconnecting ? "R" + c.voipReconnects.ToString() :
                "";

            string ping = c.pingTimeout == 0 ? (c.ping / 1000).ToString() : "T" + c.pingTimeout.ToString();

            labelTexts[labelIndex].text = c.username + " " + 
                "Net:" + c.networkState.ToString() + " " + netAttempts + " " + 
                "Voice:"+ c.voipState.ToString() + " " + voipAttempts + " " + 
                ping;

            labelIndex++;

            if (labelIndex >= 6) break;
        }
        
    }
}
