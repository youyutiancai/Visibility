using System.Net.Sockets;
using System.Net;
using UnityEngine;
using System;

public class RealUser : User
{
    public bool isPuppet, testPhaseChanged;
    public TestPhase testPhase;
    public IPEndPoint tcpEndPoint;
    public Vector3 latestPosition, simulatedPosition;
    public Quaternion latestRotation, simulatedRotation;
    public TcpClient tcpClient;
    private byte[] receiveBuffer = new byte[1024 * 1024];
    private int readPos = 0, writePos = 0, accumulatedPacketReceived;

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

    public void InitializeRealUser()
    {
        InitializeUser();
        isPuppet = false;
        testPhaseChanged = false;
        testPhase = TestPhase.InitialPhase;
        accumulatedPacketReceived = 0;
        receiveBuffer = new byte[1024 * 1024];
        readPos = 0;
        writePos = 0;
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
        //if (testPhase == TestPhase.StandPhase && chunksLeftBeforeRemoving != 0 && chunksLeftAfterRemoving == 0)
        if (ChunksWaitToSend.Count > 0)
        {
            (int, int) nextChunk = ChunksWaitToSend.Peek();
            if (testPhase == TestPhase.StandPhase && ChunksWaitToSend.GetCount(nextChunk) <= cc.numChunkRepeat - 3)
            {
                InformQuestionStart();
            }
        }
    }

    public void MarkAsSentMaxCount(int objectID, int ChunkID, int chunkCountForObject)
    {
        if (vc == null)
        {
            InitializeUser();
        }
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

    public void InformStartPath(int pathOrder)
    {
        byte[] message = new byte[sizeof(int) * 3];
        Buffer.BlockCopy(BitConverter.GetBytes(sizeof(int) * 2), 0, message, 0, sizeof(int));
        Buffer.BlockCopy(BitConverter.GetBytes((int)TCPMessageType.PATHORDER), 0, message, sizeof(int), sizeof(int));
        Buffer.BlockCopy(BitConverter.GetBytes(pathOrder), 0, message, sizeof(int) * 2, sizeof(int));
        tcpClient.GetStream().Write(message, 0, message.Length);
    }

    public void StackReadMessageRealUser(byte[] data)
    {
        Array.Copy(data, 0, receiveBuffer, writePos, data.Length);
        writePos += data.Length;

        while (writePos - readPos >= 4)
        {
            int messageLength = BitConverter.ToInt32(receiveBuffer, readPos);
            if (writePos - readPos < 4 + messageLength) break;

            byte[] dataToHandle = new byte[messageLength];
            Buffer.BlockCopy(receiveBuffer, readPos + 4, dataToHandle, 0, messageLength);
            HandleMessageRealUser(dataToHandle);

            readPos += 4 + messageLength;
        }

        if (readPos > 0 && (writePos == receiveBuffer.Length || readPos > receiveBuffer.Length / 2))
        {
            Buffer.BlockCopy(receiveBuffer, readPos, receiveBuffer, 0, writePos - readPos);
            writePos -= readPos;
            readPos = 0;
        }
    }

    private void HandleMessageRealUser(byte[] data)
    {
        int cursor = 0;
        while (cursor + sizeof(int) <= data.Length)
        {
            TCPMessageType mt = (TCPMessageType)BitConverter.ToInt32(data, cursor);
            cursor += sizeof(int);

            switch (mt)
            {
                case TCPMessageType.POSE_UPDATE:
                    float px = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                    float py = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                    float pz = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                    float rx = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                    float ry = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                    float rz = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                    float rw = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                    TestPhase currentTestPhase = (TestPhase)BitConverter.ToInt32(data, cursor); cursor += sizeof(int);
                    if (nc.sendingMode == SendingMode.MULTICAST && data.Length > cursor)
                    {
                        int receivedThisFrame = 0;
                        int totalPacketsToMarkAsReceived = (data.Length - cursor) / (sizeof(int) * 2);
                        for (int _ = 0; _ < totalPacketsToMarkAsReceived; _++)
                        {
                            int objectID = BitConverter.ToInt32(data, cursor); cursor += sizeof(int);
                            int chunkID = BitConverter.ToInt32(data, cursor); cursor += sizeof(int);
                            MarkAsSentMaxCount(objectID, chunkID, cc.objectChunksVTGrouped[objectID].Count);
                            receivedThisFrame++;
                            accumulatedPacketReceived++;
                        }
                        Debug.Log($"received this frame: {receivedThisFrame}, {(data.Length - cursor) / (sizeof(int) * 2)}, received total: {accumulatedPacketReceived}");
                    }
                    cursor = data.Length;

                    latestPosition = new Vector3(px, py, pz);
                    latestRotation = new Quaternion(rx, ry, rz, rw);
                    if (testPhase != currentTestPhase)
                    {
                        testPhaseChanged = true;
                    }
                    testPhase = currentTestPhase;

                    if (!isPuppet)
                    {
                        simulatedPosition = latestPosition;
                        simulatedRotation = latestRotation;
                    }
                    transform.SetPositionAndRotation(latestPosition, latestRotation);
                    break;

                case TCPMessageType.PUPPET_TOGGLE:
                    bool newState = BitConverter.ToInt32(data, cursor) == 1;
                    cursor += sizeof(int);
                    isPuppet = newState;
                    Debug.LogError($"{name} is becoming puppet!");
                    break;

                default:
                    Debug.LogError($"Unknown TCPMessageType: {mt}, remaining bytes: {data.Length - cursor}");
                    return; // Avoid infinite loop
            }
        }
    }
}
