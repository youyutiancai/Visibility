using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
public class ObjectHolder
{
    public int objectID;
    public Vector3 position, eulerAngles, scale;
    public string prefabName;
    public string[] materialNames;
    public int totalVertChunkNum, totalTriChunkNum, totalVertNum, submeshCount;
    public bool ifVisible, ifOwned, needUpdateCollider;
    public Dictionary<int, Chunk> chunks_VTSeparate = new Dictionary<int, Chunk>();
    public Dictionary<int, Chunk> chunks_VTGrouped = new Dictionary<int, Chunk>();
    public DateTime firstChunkTime, latestChunkTime;
    public IPEndPoint remoteEP;
}
public class TCPClient : MonoBehaviour
{
    [HideInInspector]
    public static TCPClient instance;
    [SerializeField] private int port = 13000;

    public TcpClient client;
    private Thread listenerThread;

    [HideInInspector] public ObjectHolder[] objectHolders;
    private List<byte> table_data;
    private int tcpType, totalBytes, totalObjectNum;
    private bool parsingTable;

    public GameObject head, centerEye;
    public bool isPuppet = false, receivedInitPos;
    public UDPBroadcastClientNew udpClient;
    public TestClient testClient;

    public event Action OnReceivedServerTable;

    private byte[] receiveBuffer = new byte[1024 * 1024];
    private int readPos = 0, writePos = 0;
    private float poseSendingGap, lastPoseSentTime;
    private volatile bool isRunning = true;

    void Start()
    {
        parsingTable = false;
        receivedInitPos = false;
        isRunning = true;
        poseSendingGap = 0.1f;
        lastPoseSentTime = Time.time;
        try
        {
            client = new TcpClient(Commons.SERVER_IP_ADDR, port);
            Debug.Log($"TCP client listening on port {port}");
        }
        catch (Exception e) { Debug.Log(e); }

        listenerThread = new Thread(() => ListenToServer(client)) { IsBackground = true };
        listenerThread.Start();
    }
    public void ResetAll()
    {
        for (int i = 0; i < objectHolders.Length; i++)
        {
            objectHolders[i].ifVisible = false;
            objectHolders[i].ifOwned = false;
            objectHolders[i].needUpdateCollider = false;
            objectHolders[i].chunks_VTSeparate.Clear();
            objectHolders[i].chunks_VTGrouped.Clear();
        }
    }

    private void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.Three))
        {
            isPuppet = !isPuppet;
            SendPuppetStateChange(isPuppet);
            Debug.Log($"Puppet mode toggled: {isPuppet}");
        }

        if (client != null && client.Connected && centerEye != null && receivedInitPos && Time.time - lastPoseSentTime > poseSendingGap)
        {
            byte[] poseMessage = CreatePoseMessage(centerEye.transform.position, centerEye.transform.rotation);
            SendMessage(poseMessage);
            lastPoseSentTime = Time.time;
        }
    }

    private void SendPuppetStateChange(bool isPuppet)
    {
        var buffer = new List<byte>();
        buffer.AddRange(BitConverter.GetBytes((int)TCPMessageType.PUPPET_TOGGLE));
        buffer.AddRange(BitConverter.GetBytes(isPuppet ? 1 : 0));
        SendMessage(buffer.ToArray());
    }

    private byte[] CreatePoseMessage(Vector3 pos, Quaternion rot)
    {
        var buffer = new List<byte>();
        buffer.AddRange(BitConverter.GetBytes((int)TCPMessageType.POSE_UPDATE));
        buffer.AddRange(BitConverter.GetBytes(pos.x));
        buffer.AddRange(BitConverter.GetBytes(pos.y));
        buffer.AddRange(BitConverter.GetBytes(pos.z));
        buffer.AddRange(BitConverter.GetBytes(rot.x));
        buffer.AddRange(BitConverter.GetBytes(rot.y));
        buffer.AddRange(BitConverter.GetBytes(rot.z));
        buffer.AddRange(BitConverter.GetBytes(rot.w));
        buffer.AddRange(BitConverter.GetBytes((int)testClient.testPhase));
        return buffer.ToArray();
    }
    private void ListenToServer(TcpClient server)
    {
        NetworkStream stream = server.GetStream();
        byte[] tempBuffer = new byte[8192];

        while (isRunning)
        {
            try
            {
                int bytesRead = stream.Read(tempBuffer, 0, tempBuffer.Length);
                if (bytesRead == 0) break;

                Array.Copy(tempBuffer, 0, receiveBuffer, writePos, bytesRead);
                writePos += bytesRead;

                while (writePos - readPos >= 4)
                {
                    int messageLength = BitConverter.ToInt32(receiveBuffer, readPos);
                    if (writePos - readPos < 4 + messageLength) break;

                    byte[] message = new byte[messageLength];
                    Buffer.BlockCopy(receiveBuffer, readPos + 4, message, 0, messageLength);
                    UnityDispatcher.Instance.Enqueue(() => HandleMessage(message));

                    readPos += 4 + messageLength;
                }

                if (readPos > 0 && (writePos == receiveBuffer.Length || readPos > receiveBuffer.Length / 2))
                {
                    Buffer.BlockCopy(receiveBuffer, readPos, receiveBuffer, 0, writePos - readPos);
                    writePos -= readPos;
                    readPos = 0;
                }
            }
            catch (IOException e)
            {
                if (isRunning) Debug.Log($"IOException: {e}");
                break;
            }
        }
    }

    private void HandleMessage(byte[] message)
    {
        int cursor = 0;
        TCPMessageType mt = (TCPMessageType)BitConverter.ToInt32(message, cursor);

        if (mt == TCPMessageType.TABLE && !parsingTable)
        {
            parsingTable = true;
        }

        if (mt == TCPMessageType.POSE_FROM_SERVER && (isPuppet || !receivedInitPos))
        {
            if (!receivedInitPos)
                receivedInitPos = true;
            //cursor = sizeof(int);
            //float px = BitConverter.ToSingle(message, cursor); cursor += sizeof(float);
            //float py = BitConverter.ToSingle(message, cursor); cursor += sizeof(float);
            //float pz = BitConverter.ToSingle(message, cursor); cursor += sizeof(float);
            //float rx = BitConverter.ToSingle(message, cursor); cursor += sizeof(float);
            //float ry = BitConverter.ToSingle(message, cursor); cursor += sizeof(float);
            //float rz = BitConverter.ToSingle(message, cursor); cursor += sizeof(float);
            //float rw = BitConverter.ToSingle(message, cursor);

            //Vector3 receivedPos = new Vector3(px, py, pz);
            //Quaternion receivedRot = new Quaternion(rx, ry, rz, rw);

            //if (head != null)
            //{
            //    head.transform.SetPositionAndRotation(receivedPos, receivedRot);
            //}
            return;
        }


        if (parsingTable)
        {
            ParseTable(message);
            return;
        }

        if (mt == TCPMessageType.CHUNK)
        {
            udpClient.ParseMessageForChunks(message);
        }

        if (mt == TCPMessageType.QUESTIONSTART)
        {
            testClient.testPhase = TestPhase.QuestionPhase;
            testClient.UpdateAll();
            return;
        }

        if (mt == TCPMessageType.RESETALL)
        {
            ResetAll();
            testClient.ResetAll();
            udpClient.ResetAll();
            testClient.UpdateAll();
        }

        if (mt == TCPMessageType.PATHORDER)
        {
            int pathOrder = BitConverter.ToInt32(message, sizeof(int));
            testClient.pathOrder = pathOrder;
            testClient.UpdatePathOrder();
        }
    }

    private void ParseTable(byte[] message)
    {
        int cursor = 0;
        if (table_data == null || table_data.Count == 0)
        {
            table_data = new List<byte>();
        }

        if (table_data.Count == 0)
        {
            tcpType = BitConverter.ToInt32(message, 0);
            totalBytes = BitConverter.ToInt32(message, cursor += sizeof(int));
            totalObjectNum = BitConverter.ToInt32(message, cursor += sizeof(int));
        }

        if (table_data.Count < totalBytes)
        {

            table_data.AddRange(message);
        }

        if (table_data.Count == totalBytes)
        {
            cursor = sizeof(int) * 3;

            objectHolders = new ObjectHolder[totalObjectNum];

            for (int i = 0; i < totalObjectNum; i++)
            {
                objectHolders[i] = new ObjectHolder();
                objectHolders[i].objectID = i;
                objectHolders[i].position = new Vector3(BitConverter.ToSingle(table_data.ToArray(), cursor), BitConverter.ToSingle(table_data.ToArray(), cursor += sizeof(float)),
                    BitConverter.ToSingle(table_data.ToArray(), cursor += sizeof(float)));
                objectHolders[i].eulerAngles = new Vector3(BitConverter.ToSingle(table_data.ToArray(), cursor += sizeof(float)), BitConverter.ToSingle(table_data.ToArray(), cursor += sizeof(float)),
                    BitConverter.ToSingle(table_data.ToArray(), cursor += sizeof(float)));
                objectHolders[i].scale = new Vector3(BitConverter.ToSingle(table_data.ToArray(), cursor += sizeof(float)), BitConverter.ToSingle(table_data.ToArray(), cursor += sizeof(float)),
                    BitConverter.ToSingle(table_data.ToArray(), cursor += sizeof(float)));
                objectHolders[i].totalVertChunkNum = BitConverter.ToInt32(table_data.ToArray(), cursor += sizeof(float));
                objectHolders[i].totalTriChunkNum = BitConverter.ToInt32(table_data.ToArray(), cursor += sizeof(int));
                objectHolders[i].totalVertNum = BitConverter.ToInt32(table_data.ToArray(), cursor += sizeof(int));
                objectHolders[i].submeshCount = BitConverter.ToInt32(table_data.ToArray(), cursor += sizeof(int));
                cursor += sizeof(int);

                objectHolders[i].materialNames = new string[objectHolders[i].submeshCount];
                for (int j = 0; j < objectHolders[i].submeshCount; j++)
                {
                    int materialNameLength = BitConverter.ToInt32(table_data.ToArray(), cursor);
                    objectHolders[i].materialNames[j] = Encoding.ASCII.GetString(table_data.ToArray(), cursor += sizeof(int), materialNameLength);
                    cursor += materialNameLength;
                }
            }
            parsingTable = false;
            OnReceivedServerTable?.Invoke();
            testClient.testPhase = TestPhase.StandPhase;
            testClient.UpdateAll();
            //testClient.TrapUser();
        }
    }

    public void SendString(string message)
    {
        byte[] startmessage = System.Text.Encoding.ASCII.GetBytes(message);
        client.GetStream().Write(startmessage, 0, startmessage.Length);
    }

    public void SendMessage(byte[] message)
    {
        try
        {
            client.GetStream().Write(message, 0, message.Length);
        }
        catch (Exception e)
        {
            Debug.Log(e);
        }

    }

    void OnApplicationQuit() => Disconnect();
    void OnDisable() => Disconnect();
    void OnDestroy() => Disconnect();

    private void Disconnect()
    {
        isRunning = false;
        try
        {
            if (client?.Connected == true)
            {
                client.Client.Shutdown(SocketShutdown.Both);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Shutdown failed: {e.Message}");
        }

        try { client?.Close(); } catch (Exception e) { Debug.LogWarning($"Close failed: {e.Message}"); }

        try { listenerThread?.Join(100); } catch { }
    }

}