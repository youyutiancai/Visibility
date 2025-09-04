using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityTCPClient.Assets.Scripts;

public class NetworkControl : Singleton<NetworkControl>
{
    public BroadcastControl bcc;
    public TCPControl tc;
    [HideInInspector]
    public CancellationTokenSource cts;
    public ClusterControl cc;
    public VisibilityCheck vc;
    [HideInInspector]
    public bool readyForNextObject;
    public int totalChunkSent, totalBytesSent;
    public float timeStartSendingChunks, timePassedForSendingChunks;
    public bool isBroadcast;
    [HideInInspector]
    public float timeSinceLastChunkRequest;
    [HideInInspector]
    public byte[] objectTable;
    public SendingMode sendingMode;

    private void Awake()
    {
        cts = new CancellationTokenSource();
    }

    void Start()
    {
        cc = ClusterControl.Instance;
        vc = VisibilityCheck.Instance;
        Dispatcher dispatcher = Dispatcher.Instance;
        readyForNextObject = true;
        totalChunkSent = 0;

        LoadObjectTable();
    }

    private void LoadObjectTable()
    {
        string filePath = Path.Combine(Application.dataPath, "Data", "ObjectTable.bin");
        if (!File.Exists(filePath))
        {
            Debug.LogError($"Object table file not found at: {filePath}");
            return;
        }

        objectTable = File.ReadAllBytes(filePath);
    }

    private void Update()
    {
    }

    public void BroadcastChunk(byte[] chunk)
    {
        if (totalChunkSent == 0)
        {
            timeStartSendingChunks = Time.time;
        }
        totalChunkSent++;
        totalBytesSent += chunk.Length;
        timePassedForSendingChunks = Time.time - timeStartSendingChunks;
        bcc.BroadcastChunk(chunk);
    }

    public void SendChunkTCP(RealUser user, byte[] chunk)
    {
        if (totalChunkSent == 0)
        {
            timeStartSendingChunks = Time.time;
        }
        totalChunkSent++;
        totalBytesSent += chunk.Length;
        timePassedForSendingChunks = Time.time - timeStartSendingChunks;
        try
        {
            tc.SendChunkToUser(user, chunk);
        }
        catch (Exception e)
        {
            Debug.Log($"{tc is null}, {user is null}, {chunk is null}");
        }
        
    }
    
    public void ResetTotalSentLog()
    {
        totalChunkSent = 0;
        totalBytesSent = 0;
    }

    private async void OnApplicationQuit()
    {
        Debug.Log($"Cancelling the ct OnApplicationQuit");

        if (cts != null)
        {
            cts.Cancel();
        }

        if (tc != null)
        {
            await tc.OnQuit(); // Create a public cleanup method instead of relying on OnApplicationQuit
        }
    }

}

public enum TCPMessageType
{
    TABLE,
    POSE_UPDATE,
    POSE_FROM_SERVER,
    PUPPET_TOGGLE,
    CHUNK,
    QUESTIONSTART,
    RESETALL,
    PATHORDER
}

public enum SendingMode
{
    BROADCAST,
    MULTICAST,
    UNICAST_TCP,
    UNICAST_UDP
}
