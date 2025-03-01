using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class UDPBroadcastClient : MonoBehaviour
{

    public int port = 5005;
    private UdpClient udpClient;

    void Start()
    {
        // Bind the UDP client to listen on the designated port.
        udpClient = new UdpClient(port);
        udpClient.BeginReceive(new AsyncCallback(ReceiveCallback), null);
    }

    private void ReceiveCallback(IAsyncResult ar)
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, port);
        byte[] data = udpClient.EndReceive(ar, ref remoteEP);
        string message = Encoding.UTF8.GetString(data);

        Debug.Log("Received: " + message + " from " + remoteEP);

        // Continue listening for incoming messages.
        udpClient.BeginReceive(new AsyncCallback(ReceiveCallback), null);
    }

    void OnDestroy()
    {
        udpClient.Close();
    }


    //public int port = 5005;

    //private UdpClient client;
    //private IPEndPoint remoteEndPoint;
    //private Thread receiveThread;
    //private bool isRunning = false;

    //// Data tracking dictionaries
    //private Dictionary<string, int> objectSizes = new Dictionary<string, int>();
    //private Dictionary<string, int> receivedData = new Dictionary<string, int>();

    //// Start is called before the first frame update
    //void Start()
    //{
    //    client = new UdpClient(port);
    //    remoteEndPoint = new IPEndPoint(IPAddress.Any, port);
    //    isRunning = true;

    //    // Start the background thread for receiving UDP messages.
    //    receiveThread = new Thread(ReceiveData);
    //    receiveThread.IsBackground = true;
    //    receiveThread.Start();

    //    Debug.Log("[Client] Listening for UDP broadcast messages...");
    //}

    //void ReceiveData()
    //{
    //    while (isRunning)
    //    {
    //        try
    //        {
    //            // Blocks until a message is received.
    //            byte[] data = client.Receive(ref remoteEndPoint);
    //            string message = Encoding.UTF8.GetString(data);

    //            // Check if the message contains JSON metadata.
    //            Debug.Log($"received message from server: {message}");
    //            if (message.StartsWith("{"))
    //            {
    //                Debug.Log("[Client] Received object table");
    //                objectSizes = ParseObjectTable(message);
    //                receivedData.Clear(); // Reset tracking for a fresh session
    //            }
    //            else
    //            {
    //                // Process actual object data in the format "objectName:payload"
    //                string[] parts = message.Split(':');
    //                if (parts.Length == 2)
    //                {
    //                    string objName = parts[0];
    //                    int dataSize = parts[1].Length;

    //                    if (objectSizes.ContainsKey(objName))
    //                    {
    //                        if (!receivedData.ContainsKey(objName))
    //                        {
    //                            receivedData[objName] = 0;
    //                        }

    //                        receivedData[objName] += dataSize;

    //                        if (receivedData[objName] >= objectSizes[objName])
    //                        {
    //                            Debug.Log($"[Client] {objName} fully received ({objectSizes[objName]} bytes).");
    //                            receivedData.Remove(objName); // Reset tracking for this object
    //                        }
    //                        else
    //                        {
    //                            Debug.Log($"[Client] Receiving {objName}: {receivedData[objName]}/{objectSizes[objName]} bytes.");
    //                        }
    //                    }
    //                }
    //            }
    //        }
    //        catch (SocketException ex)
    //        {
    //            // When the client is closed, a SocketException is expected.
    //            if (isRunning)
    //            {
    //                Debug.LogError("SocketException: " + ex);
    //            }
    //            break;
    //        }
    //        catch (Exception ex)
    //        {
    //            Debug.LogError("Exception in ReceiveData: " + ex);
    //        }
    //    }
    //}

    //// Parses the JSON metadata using Unity's JsonUtility.
    //Dictionary<string, int> ParseObjectTable(string jsonData)
    //{
    //    Dictionary<string, int> objTable = new Dictionary<string, int>();
    //    try
    //    {
    //        ObjectTable table = JsonUtility.FromJson<ObjectTable>(jsonData);
    //        if (table != null && table.objects != null)
    //        {
    //            foreach (var obj in table.objects)
    //            {
    //                objTable[obj.name] = obj.size;
    //            }
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        Debug.LogError("Error parsing JSON: " + ex);
    //    }
    //    return objTable;
    //}

    //// Cleanup when the application quits.
    //void OnApplicationQuit()
    //{
    //    isRunning = false;
    //    if (client != null)
    //    {
    //        client.Close();
    //    }
    //    if (receiveThread != null && receiveThread.IsAlive)
    //    {
    //        // Closing the UdpClient will break the blocking Receive call.
    //        receiveThread.Join();
    //    }
    //}
}

// Serializable class to hold individual object data from the JSON.
[Serializable]
public class ObjectData
{
    public string name;
    public int size;
}

// Serializable class to represent the JSON structure.
[Serializable]
public class ObjectTable
{
    public ObjectData[] objects;
}
