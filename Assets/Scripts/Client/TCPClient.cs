using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class TCPClient : MonoBehaviour
{
    [HideInInspector]
    public static TCPClient instance;


    //[HideInInspector]
    private string serverIPAddress = "192.168.1.188";  // 192.168.1.2
    //public string serverIPAddress;

    [SerializeField]
    private int port = 13000;

    //[SerializeField]
    //private TextMeshProUGUI debugText;

    public TcpClient client;
    private int messagestatus;
    private Thread listenerThread;
    //private byte[] data;

    // global variables for the table
    [HideInInspector]
    public ObjectHolder[] objectHolders;
    private int currentBytes = 0;
    private List<byte> table_data; // the initial length is 0
    private int tcpType;
    private int totalBytes;
    private int totalObjectNum;
    private bool parsingTable;
    public GameObject centerEye;
    public bool isPuppet = false;

    #region

    public event Action OnReceivedServerTable;

    #endregion

    // create Singleton object before threads are created. 
    void Awake()
    {

    }
    void Start()
    {
        parsingTable = false;
        try
        {
            client = new TcpClient(serverIPAddress, port);
            Debug.Log($"TCP client listening on port {port}");
        }
        catch (Exception e)
        {
            Debug.Log(e);
        }
        listenerThread = new Thread(() =>
        {
            Thread.CurrentThread.IsBackground = true;
            ListenToServer(client);
        });
        listenerThread.Start();
    }

    public string GetServerIP()
    {
        return serverIPAddress;
    }

    private void Message(int clientId, string message)
    {
        //Debug.Log($"{clientId} {message}");
        //debugText.text += $"{message} \n";
    }



    public int GetReceivestatus()
    {
        return messagestatus;
    }
    public void ResetMessagestatus()
    {
    }

    private void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.Three))
        {
            isPuppet = !isPuppet;
            SendPuppetStateChange(isPuppet);
            Debug.Log($"Puppet mode toggled: {isPuppet}");
        }

        if (!isPuppet && client != null && client.Connected && centerEye != null)
        {
            byte[] poseMessage = CreatePoseMessage(centerEye.transform.position, centerEye.transform.rotation);
            SendMessage(poseMessage);
        }
    }

    private void SendPuppetStateChange(bool isPuppet)
    {
        List<byte> buffer = new List<byte>();
        buffer.AddRange(BitConverter.GetBytes((int)TCPMessageType.PUPPET_TOGGLE));
        buffer.AddRange(BitConverter.GetBytes(isPuppet ? 1 : 0));
        SendMessage(buffer.ToArray());
    }

    private byte[] CreatePoseMessage(Vector3 pos, Quaternion rot)
    {
        List<byte> buffer = new List<byte>();
        buffer.AddRange(BitConverter.GetBytes((int)TCPMessageType.POSE_UPDATE));  // Use your enum
        buffer.AddRange(BitConverter.GetBytes(pos.x));
        buffer.AddRange(BitConverter.GetBytes(pos.y));
        buffer.AddRange(BitConverter.GetBytes(pos.z));
        buffer.AddRange(BitConverter.GetBytes(rot.x));
        buffer.AddRange(BitConverter.GetBytes(rot.y));
        buffer.AddRange(BitConverter.GetBytes(rot.z));
        buffer.AddRange(BitConverter.GetBytes(rot.w));
        return buffer.ToArray();
    }


    /*** 
     * Received Message format:
     * 
     * TCP message type - int
     * object count - int
     * total num of bytes of the table - int
     * table data
     * 
     * ***/
    private void ListenToServer(TcpClient server)
    {
        try
        {
            NetworkStream stream = server.GetStream();

            while (true)
            {
                byte[] buffer = new byte[1024 * 1024]; // 1MB buffer
                int byteCount = stream.Read(buffer, 0, buffer.Length);
                byte[] dataTemp = new byte[byteCount];
                Buffer.BlockCopy(buffer, 0, dataTemp, 0, byteCount);

                // string response = System.Text.Encoding.ASCII.GetString(dataTemp, 0, bytes);
                UnityDispatcher.Instance.Enqueue(() => HandleMessage(dataTemp));

                //Dispatcher.Instance.Enqueue(() => Message(clientId, $"Recieved : {byteCount}"));
                //dispatcher.Enqueue(() => Message(clientId, $"Message : " +
                //    $"{System.Text.Encoding.ASCII.GetString(dataTemp, 0, MessageType.MESSAGELENGTH)}"));
                //nfClient.readyForNextData = false;
            }
            // Dispatcher.Instance.Enqueue(() => Message(clientId, $"Disconnects"));
            // stream.Close();
            // client.Close();
        }
        catch (Exception e)
        {
            Debug.Log($"Exception : {e}");
        }

        Console.Read();
    }

    private void HandleMessage(byte[] message)
    {
        int cursor = 0;

        TCPMessageType mt = (TCPMessageType)BitConverter.ToInt32(message, cursor);
        Debug.Log($"[+++++++++++++++] message type: {mt}");

        if (mt == TCPMessageType.TABLE && !parsingTable)
        {
            parsingTable = true;
        }

        if (mt == TCPMessageType.POSE_FROM_SERVER && isPuppet)
        {
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

            if (centerEye != null)
            {
                centerEye.transform.SetPositionAndRotation(receivedPos, receivedRot);
            }
            return;
        }


        if (parsingTable)
        {
            ParseTable(message);
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