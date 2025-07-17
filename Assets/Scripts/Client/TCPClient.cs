using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Oculus.Platform;
using UnityEngine;

public class TCPClient : MonoBehaviour
{
    [HideInInspector]
    public static TCPClient instance;

    private string serverIPAddress = "192.168.1.188";
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

    public event Action OnReceivedServerTable;

    private byte[] receiveBuffer = new byte[1024 * 1024];
    private int readPos = 0, writePos = 0;
    private float poseSendingGap, lastPoseSentTime;

    void Start()
    {
        parsingTable = false;
        receivedInitPos = false;
        poseSendingGap = 0.1f;
        lastPoseSentTime = Time.time;
        try
        {
            client = new TcpClient(serverIPAddress, port);
            Debug.Log($"TCP client listening on port {port}");
        }
        catch (Exception e) { Debug.Log(e); }

        listenerThread = new Thread(() => ListenToServer(client)) { IsBackground = true };
        listenerThread.Start();
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
        return buffer.ToArray();
    }
    private void ListenToServer(TcpClient server)
    {
        try
        {
            NetworkStream stream = server.GetStream();
            byte[] tempBuffer = new byte[8192];

            while (true)
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
        }
        catch (Exception e) { Debug.Log($"Exception : {e}"); }
    }

    private void HandleMessage(byte[] message)
    {
        int cursor = 0;
        TCPMessageType mt = (TCPMessageType)BitConverter.ToInt32(message, cursor);
        //Debug.Log($"[+++++++++++++++] message type: {mt}");

        if (mt == TCPMessageType.TABLE && !parsingTable)
        {
            parsingTable = true;
        }

        if (mt == TCPMessageType.POSE_FROM_SERVER && (isPuppet || !receivedInitPos))
        {
            if (!receivedInitPos)
                receivedInitPos = true;
            cursor = sizeof(int);
            float px = BitConverter.ToSingle(message, cursor); cursor += sizeof(float);
            float py = BitConverter.ToSingle(message, cursor); cursor += sizeof(float);
            float pz = BitConverter.ToSingle(message, cursor); cursor += sizeof(float);
            float rx = BitConverter.ToSingle(message, cursor); cursor += sizeof(float);
            float ry = BitConverter.ToSingle(message, cursor); cursor += sizeof(float);
            float rz = BitConverter.ToSingle(message, cursor); cursor += sizeof(float);
            float rw = BitConverter.ToSingle(message, cursor);

            Vector3 receivedPos = new Vector3(px, py, pz);
            Quaternion receivedRot = new Quaternion(rx, ry, rz, rw);
            Debug.Log($"[+++++++++++++++] {receivedPos}, {receivedRot}");

            if (head != null)
            {
                head.transform.SetPositionAndRotation(receivedPos, receivedRot);
            }
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
            Debug.Log($"HEADER: type: {tcpType}, object_num: {totalObjectNum}, total_bytes: {totalBytes}");
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

                // TODO: Currently not used for the isvisible and isowned

                // Debug.Log($"{objectHolders[i].position} - {objectHolders[i].eulerAngles} - {objectHolders[i].scale}");

                objectHolders[i].materialNames = new string[objectHolders[i].submeshCount];
                //Transform transform = objectsInScene[i].transform;
                for (int j = 0; j < objectHolders[i].submeshCount; j++)
                {
                    int materialNameLength = BitConverter.ToInt32(table_data.ToArray(), cursor);
                    objectHolders[i].materialNames[j] = Encoding.ASCII.GetString(table_data.ToArray(), cursor += sizeof(int), materialNameLength);
                    cursor += materialNameLength;

                    //Debug.Log($"ObjectID{i} - {objectHolders[i].materialNames[j]}");
                }
            }
            parsingTable = false;
            OnReceivedServerTable?.Invoke();
        }
    }

    public void SendString(string message)
    {
        Debug.Log(message);
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

    public void OnApplicationQuit()
    {
        Debug.Log("onapplicationquit");
        client.Close();
        listenerThread.Abort();
    }

    public void OnDisable()
    {
        Debug.Log("onDisable");
        client.Close();
        listenerThread.Abort();
    }

    public void OnDestroy()
    {
        Debug.Log("onDestroy");
        client.Close();
        listenerThread.Abort();
    }
}