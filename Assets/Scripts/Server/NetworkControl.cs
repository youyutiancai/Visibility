using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityTCPClient.Assets.Scripts;

public class NetworkControl : Singleton<NetworkControl>
{
    BroadcastControl bcc;
    TCPControl tc;
    private CancellationTokenSource cts;
    ClusterControl cc;
    [HideInInspector]
    public bool readyForNextObject;

    void Start()
    {
        cc = ClusterControl.Instance;
        if (cc.SimulationStrategy == SimulationStrategyDropDown.RealUser)
        {
            Dispatcher dispatcher = Dispatcher.Instance;
            VisibilityCheck visibilityCheck = VisibilityCheck.Instance;
            cts = new CancellationTokenSource();
            bcc = new BroadcastControl(cts.Token);
            tc = new TCPControl(cts.Token, dispatcher, visibilityCheck, cc);
        }
        readyForNextObject = true;
    }

    private void Update()
    {
        if (cc.SimulationStrategy == SimulationStrategyDropDown.RealUser)
        {
            bcc.UpdateTime();
            //if (Input.GetKeyDown(KeyCode.B))
            //{
            //    bcc.BroadcastObjectData(1);
            //}
            bcc.UpdateChunkSending();
        }
    }

    public void BroadcastObjectData(int objectID, float _sleepTime = 0.01f)
    {
        bcc.BroadcastObjectData(objectID, _sleepTime);
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
