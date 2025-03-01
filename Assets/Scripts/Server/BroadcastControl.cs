using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class BroadcastControl : MonoBehaviour
{
    private const string BROADCAST_IP = "255.255.255.255";
    private const int PORT = 5005;
    private UdpClient udpClient;
    private bool appRunning;
    private int currentTimeCheck, lastTimeCheck;
    Thread broadcastThread;
    private CancellationToken ct;

    // Object metadata
    private List<Dictionary<string, object>> objectTable = new List<Dictionary<string, object>>()
    {
        new Dictionary<string, object>() { { "name", "Cube" }, { "size", 1024 } },
        new Dictionary<string, object>() { { "name", "Sphere" }, { "size", 1024 } },
        new Dictionary<string, object>() { { "name", "Pyramid" }, { "size", 512 } }
    };

    public BroadcastControl(CancellationToken _ct) {
        ct = _ct;
        udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;
        appRunning = true;
        lastTimeCheck = 0;

        //StartCoroutine(BroadcastObjectMetadata());
        broadcastThread = new Thread(() => BroadcastObjectData());
        broadcastThread.Start();
    }

    //private IEnumerator BroadcastObjectMetadata()
    //{
    //    Debug.Log("[Server] Broadcasting object metadata...");

    //    // Convert metadata to JSON and broadcast
    //    var objectTableDict = new Dictionary<string, object> { { "objects", objectTable } };
    //    string metadataJson = JsonUtility.ToJson(new Wrapper { objects = objectTable });
    //    SendBroadcastMessage(metadataJson);

    //    yield return new WaitForSeconds(2); // Wait before sending object data

    //    Debug.Log("[Server] Broadcasting object data...");
    //    StartCoroutine(BroadcastObjectData());
    //}

    public void UpdateTime()
    {
        currentTimeCheck = (int)Time.time;
    }

    private void BroadcastObjectData()
    {
        var objectTableDict = new Dictionary<string, object> { { "objects", objectTable } };
        Debug.Log($"start broadcasting");
        while (appRunning)
        {
            if (currentTimeCheck > lastTimeCheck)
            {

                foreach (var obj in objectTable)
                {
                    string msg = $"{obj["name"]}:{new string('X', 3)}";
                    SendBroadcastMessage(msg);
                    lastTimeCheck = currentTimeCheck;
                }
            }
        }
    }

    private void SendBroadcastMessage(string message)
    {
        Debug.Log("[Server] Broadcasting object data...");
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, PORT);
            udpClient.Send(data, data.Length, endPoint);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Server] Error broadcasting message: {e.Message}");
        }
    }

    void OnApplicationQuit()
    {
        appRunning = false;
        udpClient.Close();
    }

    private IEnumerator WaitOneSecond()
    {
        yield return new WaitForSeconds(1);
    }
}
