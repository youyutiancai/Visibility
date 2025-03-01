using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class NetworkControl : MonoBehaviour
{
    BroadcastControl bcc;
    TCPControl tc;
    private CancellationTokenSource cts;
    ClusterControl cc;

    void Start()
    {
        cc = ClusterControl.Instance;
        if (cc.SimulationStrategy == SimulationStrategyDropDown.RealUser)
        {
            Dispatcher dispatcher = Dispatcher.Instance;
            VisibilityCheck visibilityCheck = VisibilityCheck.Instance;
            cts = new CancellationTokenSource();
            bcc = new BroadcastControl(cts.Token);
            tc = new TCPControl(cts.Token, dispatcher, visibilityCheck);
        }
        
    }

    private void Update()
    {
        if (cc.SimulationStrategy == SimulationStrategyDropDown.RealUser)
        {
            bcc.UpdateTime();
        }
    }

    private void OnApplicationQuit()
    {
        Debug.Log($"Cancelling the ct OnApplicationQuit");
        if (cts != null)
        {
            cts.Cancel();
        }
    }
}

public enum TCPMessageType
{
    TABLE
}
