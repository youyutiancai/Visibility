using System.Net.Sockets;
using System.Net;
using UnityEngine;
using System;
using Unity.VisualScripting;

public class RealUser : User
{
    public bool isPuppet, testPhaseChanged;
    public TestPhase testPhase;
    public IPEndPoint tcpEndPoint;
    public Vector3 latestPosition, simulatedPosition;
    public Quaternion latestRotation, simulatedRotation;
    public TcpClient tcpClient;

    public RealUser(Vector3 initialPos) : base(initialPos)
    {
        latestPosition = initialPos;
        latestRotation = Quaternion.identity;
        simulatedPosition = initialPos;
        simulatedRotation = Quaternion.identity;
        isPuppet = false;
        testPhaseChanged = false;
        testPhase = TestPhase.InitialPhase;
    }


    public void MarkAsSent(int objectID, int ChunkID, int chunkCountForObject, int sentCounts)
    {
        if (!chunkSentTimes.ContainsKey(objectID))
        {
            chunkSentTimes[objectID] = new int[chunkCountForObject];
        }
        chunkSentTimes[objectID][ChunkID]++;
        int chunksLeftBeforeRemoving = ChunksWaitToSend.Count;
        if (ChunksWaitToSend.Contains((objectID, ChunkID)))
        {
            ChunksWaitToSend.DecreaseCount((objectID, ChunkID), sentCounts);
        }
        int chunksLeftAfterRemoving = ChunksWaitToSend.Count;
        if (testPhase == TestPhase.StandPhase && chunksLeftBeforeRemoving != 0 && chunksLeftAfterRemoving == 0)
        {
            InformQuestionStart();
        }
    }

    public void MarkAsSentMaxCount(int objectID, int ChunkID, int chunkCountForObject)
    {
        if (!chunkSentTimes.ContainsKey(objectID))
        {
            chunkSentTimes[objectID] = new int[chunkCountForObject];
        }
        chunkSentTimes[objectID][ChunkID] = cc.numChunkRepeat;
        int chunksLeftBeforeRemoving = ChunksWaitToSend.Count;
        if (ChunksWaitToSend.Contains((objectID, ChunkID)))
        {
            ChunksWaitToSend.Remove((objectID, ChunkID));
        }
        int chunksLeftAfterRemoving = ChunksWaitToSend.Count;
        if (testPhase == TestPhase.StandPhase && chunksLeftBeforeRemoving != 0 && chunksLeftAfterRemoving == 0)
        {
            InformQuestionStart();
        }
    }

    public void InformQuestionStart()
    {
        Debug.Log($"inform question start");
        byte[] message = new byte[sizeof(int) * 2];
        Buffer.BlockCopy(BitConverter.GetBytes(sizeof(int)), 0, message, 0, sizeof(int));
        Buffer.BlockCopy(BitConverter.GetBytes((int)TCPMessageType.QUESTIONSTART), 0, message, sizeof(int), sizeof(int));
        tcpClient.GetStream().Write(message, 0, message.Length);
    }

    public void InformResetAll()
    {
        InitializeUser();
        Debug.Log($"resetting all");
        byte[] message = new byte[sizeof(int) * 2];
        Buffer.BlockCopy(BitConverter.GetBytes(sizeof(int)), 0, message, 0, sizeof(int));
        Buffer.BlockCopy(BitConverter.GetBytes((int)TCPMessageType.RESETALL), 0, message, sizeof(int), sizeof(int));
        tcpClient.GetStream().Write(message, 0, message.Length);
    }
}
