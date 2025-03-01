using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using TMPro;
using UnityEngine;

public class TCPClient : MonoBehaviour
{
    [HideInInspector]
    public static TCPClient instance;

    //[HideInInspector]
    private string serverIPAddress = "192.168.137.1";
    //private string serverIPAddress = "10.161.33.74";
    //private string serverIPAddress = "10.0.0.28";
    //public string serverIPAddress;

    [SerializeField]
    private int port = 13000;

    [SerializeField]
    private TextMeshProUGUI debugText;

    public TcpClient client;
    private int messagestatus;
    private Thread listenerThread;
    private Dispatcher dispatcher;
    //private byte[] data;

    // create Singleton object before threads are created. 
    void Awake() {
        dispatcher = Dispatcher.Instance;
        if (instance == null)
            instance = this;
    }
    void Start()
    {
        try
        {
            client = new TcpClient(serverIPAddress, port);
        } catch (Exception e)
        {
            debugText.text += e + "\n";
        }
        listenerThread = new Thread(() =>
        {
            Thread.CurrentThread.IsBackground = true;
            ConnectClient(client, 1);
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

    private void ConnectClient(TcpClient client, int clientId)
        {
            // string message = $"ClientId {clientId} sending a message...";
            try
            {
                NetworkStream stream = client.GetStream();

            while(true) {
                byte[] buffer = new byte[10000000];
                int byteCount = stream.Read(buffer, 0, buffer.Length);
                byte[] dataTemp = new byte[byteCount];
                Buffer.BlockCopy(buffer, 0, dataTemp, 0, byteCount);
                // string response = System.Text.Encoding.ASCII.GetString(dataTemp, 0, bytes);
                dispatcher.Enqueue(() => HandleMessagenfClient(dataTemp));
                    
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
                dispatcher.Enqueue(() => Message(clientId, $"Exception : {e}"));
            }

            Console.Read();
        }

    private void HandleMessagenfClient(byte[] dataTemp)
    {
        
            
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
            debugText.text = "";
            debugText.text += e;
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
