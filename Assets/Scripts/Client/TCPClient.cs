using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;
using UnityEditor.Experimental.GraphView;
using UnityEditor.VersionControl;
using UnityEngine;

public class TCPClient : MonoBehaviour
{
    [HideInInspector]
    public static TCPClient instance;


    //[HideInInspector]
    private string serverIPAddress = "192.168.0.82";
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

    #region

    public event Action OnReceivedServerTable;

    #endregion

    // create Singleton object before threads are created. 
    void Awake() {
        
    }
    void Start()
    {
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

    private void Message(int clientId, string message) {
        //Debug.Log($"{clientId} {message}");
        //debugText.text += $"{message} \n";
    }



    public int GetReceivestatus() {
        return messagestatus;
    }
    public void ResetMessagestatus() {
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
                
                while(true) {
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
            catch(Exception e)
            {
                Debug.Log($"Exception : {e}");
            }

            Console.Read();
        }

    private void HandleMessage(byte[] message)
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
                objectHolders[i].position = new Vector3(BitConverter.ToSingle(message, cursor), BitConverter.ToSingle(message, cursor += sizeof(float)),
                    BitConverter.ToSingle(message, cursor += sizeof(float)));
                objectHolders[i].eulerAngles = new Vector3(BitConverter.ToSingle(message, cursor += sizeof(float)), BitConverter.ToSingle(message, cursor += sizeof(float)),
                    BitConverter.ToSingle(message, cursor += sizeof(float)));
                objectHolders[i].scale = new Vector3(BitConverter.ToSingle(message, cursor += sizeof(float)), BitConverter.ToSingle(message, cursor += sizeof(float)),
                    BitConverter.ToSingle(message, cursor += sizeof(float)));
                objectHolders[i].totalVertChunkNum = BitConverter.ToInt32(message, cursor += sizeof(float));
                objectHolders[i].totalTriChunkNum = BitConverter.ToInt32(message, cursor += sizeof(int));
                objectHolders[i].totalVertNum = BitConverter.ToInt32(message, cursor += sizeof(int));
                objectHolders[i].submeshCount = BitConverter.ToInt32(message, cursor += sizeof(int));
                cursor += sizeof(int);

                // TODO: Currently not used for the isvisible and isowned

                // Debug.Log($"{objectHolders[i].position} - {objectHolders[i].eulerAngles} - {objectHolders[i].scale}");

                objectHolders[i].materialNames = new string[objectHolders[i].submeshCount];
                //Transform transform = objectsInScene[i].transform;
                for (int j = 0; j < objectHolders[i].submeshCount; j++)
                {
                    int materialNameLength = BitConverter.ToInt32(message, cursor);
                    objectHolders[i].materialNames[j] = Encoding.ASCII.GetString(message, cursor += sizeof(int), materialNameLength);
                    cursor += materialNameLength;

                    //Debug.Log($"ObjectID{i} - {objectHolders[i].materialNames[j]}");
                }
            }

            OnReceivedServerTable?.Invoke();
        }

        
    }

    public void SendString(string message) {
        Debug.Log(message);
        byte[] startmessage = System.Text.Encoding.ASCII.GetBytes(message);
        client.GetStream().Write(startmessage, 0, startmessage.Length);
    }

    public void SendMessage(byte[] message) {
        try
        {
            client.GetStream().Write(message, 0, message.Length);
        } catch (Exception e)
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
