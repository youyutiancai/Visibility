using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using System.IO;

public class UDPBroadcastClient : MonoBehaviour
{
    public int port = 5005;
    private UdpClient udpClient;
    // Define header size (3 ints: bundleId, totalChunks, chunkIndex)
    private const int HEADER_SIZE = 12;

    // A helper class to store data for a bundle transmission
    private class BundleTransmission
    {
        public int totalChunks;
        public Dictionary<int, byte[]> chunks = new Dictionary<int, byte[]>();
        public double firstChunkTime;
    }

    // Dictionary mapping a bundleId to its transmission data
    private Dictionary<int, BundleTransmission> activeTransmissions = new Dictionary<int, BundleTransmission>();

    #region

    public event Action<string> OnReceivedServerData;

    #endregion

    void Start()
    {
        try
        {
            udpClient = new UdpClient(port);
            udpClient.BeginReceive(new AsyncCallback(ReceiveMessage), null);
            Debug.Log($"UDP client initialized on port {port}");
        }
        catch (Exception ex)
        {
            Debug.LogError("UDP client initialization failed: " + ex.Message);
        }
    }

    private void ReceiveMessage(IAsyncResult ar)
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, port);
        byte[] packet = udpClient.EndReceive(ar, ref remoteEP);

        // Validate packet size
        if (packet.Length < HEADER_SIZE)
        {
            Debug.LogWarning("Received packet too small to contain header.");
            udpClient.BeginReceive(new AsyncCallback(ReceiveMessage), null);
            return;
        }

        // Parse header values
        int bundleId = BitConverter.ToInt32(packet, 0);
        int totalChunks = BitConverter.ToInt32(packet, 4);
        int chunkIndex = BitConverter.ToInt32(packet, 8);

        int dataSize = packet.Length - HEADER_SIZE;
        byte[] chunkData = new byte[dataSize];
        Array.Copy(packet, HEADER_SIZE, chunkData, 0, dataSize);

        // Store the chunk in the appropriate transmission buffer
        if (!activeTransmissions.ContainsKey(bundleId))
        {
            activeTransmissions[bundleId] = new BundleTransmission { totalChunks = totalChunks, firstChunkTime = GetCurrentTime() };
            Debug.Log($"Started receiving bundleId {bundleId} with {totalChunks} chunks.");
        }

        BundleTransmission transmission = activeTransmissions[bundleId];

        // Avoid storing duplicate chunks
        if (!transmission.chunks.ContainsKey(chunkIndex))
        {
            transmission.chunks[chunkIndex] = chunkData;
            Debug.Log($"Received chunk {chunkIndex + 1}/{totalChunks} for bundleId {bundleId}");
        }

        // Check if all chunks are received.
        if (transmission.chunks.Count == transmission.totalChunks)
        {
            Debug.Log("11111");

            // reassamble asset bundle from chunks
            Debug.Log($"All chunks received for bundleId {bundleId}. Reassembling asset bundle...");
            int totalSize = 0;
            for (int i = 0; i < transmission.totalChunks; i++)
            {
                totalSize += transmission.chunks[i].Length;
            }

            byte[] assetBundleData = new byte[totalSize];
            int offset = 0;
            for (int i = 0; i < transmission.totalChunks; i++)
            {
                byte[] chunk = transmission.chunks[i];
                Buffer.BlockCopy(chunk, 0, assetBundleData, offset, chunk.Length);
                offset += chunk.Length;
            }

            // write to the disk
            string path = Application.dataPath + "net_asset_bundle";
            File.WriteAllBytes(path, assetBundleData);

            // dispatch the event to the listener
            OnReceivedServerData?.Invoke(path);

            activeTransmissions.Remove(bundleId);
        }
        else if (GetCurrentTime() - transmission.firstChunkTime > 0.5)
        {
            Debug.Log("2222");
            // After 2 seconds, check for missing chunks and request retransmission.
            List<int> missingChunks = new List<int>();
            for (int i = 0; i < transmission.totalChunks; i++)
            {
                if (!transmission.chunks.ContainsKey(i))
                {
                    missingChunks.Add(i);
                }
            }

            if (missingChunks.Count > 0)
            {
                RetransmissionRequest req = new RetransmissionRequest
                {
                    //bundleId = bundleId,
                    missingChunks = missingChunks.ToArray()
                };
                string jsonReq = JsonUtility.ToJson(req);
                byte[] reqData = Encoding.UTF8.GetBytes(jsonReq);
                // Send request to the server (assuming server IP is the one you received data from)
                UdpClient requestClient = new UdpClient();
                requestClient.Send(reqData, reqData.Length, remoteEP.Address.ToString(), port);
                requestClient.Close();
                Debug.Log("Requested retransmission for missing chunks: " + string.Join(",", missingChunks));
                // Reset timer so you don't spam requests
                transmission.firstChunkTime = GetCurrentTime();
            }
        }
        else
        {
            Debug.Log($"{GetCurrentTime()} - {transmission.firstChunkTime}");
        }

        // Continue listening for incoming packets
        udpClient.BeginReceive(new AsyncCallback(ReceiveMessage), null);
    }

    
    private double GetCurrentTime()
    {
        return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
    }

    

    void OnDestroy()
    {
        if (udpClient != null)
        {
            udpClient.Close();
        }
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


//private void ReceiveMessage(IAsyncResult ar)
//{
//    try
//    {
//        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, portUDP);
//        byte[] packet = udpClient.EndReceive(ar, ref remoteEP);

//        if (packet.Length < HEADER_SIZE)
//        {
//            Debug.LogWarning("Received packet too small to contain header.");
//            udpClient.BeginReceive(new AsyncCallback(ReceiveMessage), null);
//            return;
//        }

//        // Parse the header.
//        int bundleId = BitConverter.ToInt32(packet, 0);
//        int totalChunks = BitConverter.ToInt32(packet, 4);
//        int chunkIndex = BitConverter.ToInt32(packet, 8);

//        int dataSize = packet.Length - HEADER_SIZE;
//        byte[] chunkData = new byte[dataSize];
//        Array.Copy(packet, HEADER_SIZE, chunkData, 0, dataSize);

//        // Create or update the transmission record.
//        if (!activeTransmissions.ContainsKey(bundleId))
//        {
//            activeTransmissions[bundleId] = new BundleTransmission
//            {
//                totalChunks = totalChunks,
//                firstChunkTime = DateTime.UtcNow,
//                remoteEP = remoteEP   // store the sender's endpoint
//            };
//            //Debug.Log($"Started receiving bundleId {bundleId} with {totalChunks} chunks from {remoteEP.Address}");
//        }
//        BundleTransmission transmission = activeTransmissions[bundleId];

//        if (!transmission.chunks.ContainsKey(chunkIndex))
//        {
//            transmission.chunks[chunkIndex] = chunkData;
//            //Debug.Log($"Received chunk {chunkIndex + 1}/{totalChunks} for bundleId {bundleId}");
//        }

//        // If all chunks have been received, schedule reassembly and loading on the main thread.
//        if (transmission.chunks.Count == transmission.totalChunks)
//        {
//            UnityDispatcher.Instance.Enqueue(() =>
//            {
//                AssembleAndLoadBundle(bundleId, transmission);
//            });
//            activeTransmissions.Remove(bundleId);
//        }
//    }
//    catch (Exception ex)
//    {
//        Debug.LogError("ReceiveMessage error: " + ex.Message);
//    }

//    // Continue listening for packets.
//    udpClient.BeginReceive(new AsyncCallback(ReceiveMessage), null);
//}
//// Coroutine to periodically check for missing chunks and request retransmissions.
//private IEnumerator CheckRetransmissions()
//{
//    while (true)
//    {
//        yield return new WaitForSeconds(0.5f);

//        // Iterate over a copy of the keys so that modifications to activeTransmissions don't throw exceptions.
//        foreach (var kvp in new Dictionary<int, BundleTransmission>(activeTransmissions))
//        {
//            int bundleId = kvp.Key;
//            BundleTransmission transmission = kvp.Value;

//            // Only process incomplete transmissions.
//            if (transmission.chunks.Count < transmission.totalChunks)
//            {
//                TimeSpan elapsed = DateTime.UtcNow - transmission.firstChunkTime;
//                if (elapsed.TotalSeconds > 2f)
//                {
//                    List<int> missingChunks = new List<int>();
//                    for (int i = 0; i < transmission.totalChunks; i++)
//                    {
//                        if (!transmission.chunks.ContainsKey(i))
//                        {
//                            missingChunks.Add(i);
//                        }
//                    }

//                    if (missingChunks.Count > 0)
//                    {
//                        RetransmissionRequest req = new RetransmissionRequest
//                        {
//                            bundleId = bundleId,
//                            missingChunks = missingChunks.ToArray()
//                        };
//                        string jsonReq = JsonUtility.ToJson(req);
//                        byte[] reqData = Encoding.UTF8.GetBytes(jsonReq);

//                        UdpClient requestClient = new UdpClient();
//                        // Use the stored remoteEP address for the retransmission request.
//                        requestClient.Send(reqData, reqData.Length, transmission.remoteEP.Address.ToString(), portTCP);
//                        requestClient.Close();
//                        //Debug.Log("Requested retransmission for bundleId " + bundleId + " for missing chunks: " + string.Join(",", missingChunks));

//                        // Reset the timer for retransmission requests.
//                        transmission.firstChunkTime = DateTime.UtcNow;
//                    }
//                }
//            }
//        }
//    }
//}

//private void UpdateUI(int objectId, int meshReceivedN, bool isAppending)
//{
//    //int trunksPerObject = m_TCPClient.objectHolders[objectId].totalTriChunkNum + m_TCPClient.objectHolders[objectId].totalVertChunkNum;

//    if (isAppending)
//    {
//        // new object log added.

//        logContext = m_TextLog.text;

//        m_TextLog.text += $"Starting Receiving Object-[{objectId}] with total chunks-[xxx]\n";
//    }
//    else
//    {
//        // update the chunks received for the current object
//        m_TextLog.text = logContext + $"Object-[{objectId}]: {meshReceivedN} / xxx \n";
//    }
//}
//private void AssembleLoadObjectSubMeshes(int objectId, MeshTransmission transmission)
//{
//    Debug.Log($"All chunks received for objectId {objectId}. Initializing the object...");

//    List<List<int>> triangles = new List<List<int>>();
//    int subMeshCount = 12;  // TODO: will change later 
//    for (int i = 0; i < subMeshCount; i++)
//    {
//        triangles.Add(new List<int>());
//    }

//    int numVerticesPerChunk = 57; //TODO: might change later
//    int totalVertexNum = 20706; // TODO: will change later
//    Vector3[] vertices = new Vector3[totalVertexNum];
//    Vector3[] normals = new Vector3[totalVertexNum];

//    for (int i = 0; i < transmission.totalMeshChunks; i++)
//    {
//        Chunk chunk = transmission.chunks[i];
//        byte[] chunk_data = chunk.data;
//        char vorT = chunk.type;
//        int objectID = chunk.objectID;
//        int chunkID = i;

//        if (vorT == 'V')
//        {
//            int numVerticesInChunk = chunk_data.Length / (sizeof(float) * 6);  // 6: pos and rot
//            float[] floatArray = new float[numVerticesInChunk * 6];
//            Buffer.BlockCopy(chunk_data, 0, floatArray, 0, floatArray.Length * sizeof(float));
//            for (int j = 0; j < numVerticesInChunk; j++)
//            {
//                vertices[chunkID * numVerticesPerChunk + j] = new Vector3(floatArray[j * 6], floatArray[j * 6 + 1], floatArray[j * 6 + 2]);
//                normals[chunkID * numVerticesPerChunk + j] = new Vector3(floatArray[j * 6 + 3], floatArray[j * 6 + 4], floatArray[j * 6 + 5]);
//            }
//        }
//        else if (vorT == 'T')
//        {
//            int subMeshIdx = chunk.subMeshIdx;
//            int numVerticesInChunk = (chunk_data.Length) / sizeof(int);
//            int[] intArray = new int[numVerticesInChunk];
//            Buffer.BlockCopy(chunk_data, 0, intArray, 0, intArray.Length * sizeof(int));
//            triangles[subMeshIdx].AddRange(intArray);
//        }
//    }

//    GameObject newObject = new GameObject();
//    newObject.AddComponent<MeshFilter>();

//    Mesh newMesh = new Mesh();
//    newMesh.vertices = vertices;
//    newMesh.normals = normals;
//    newMesh.subMeshCount = subMeshCount;
//    for (int i = 0; i < subMeshCount; i++)
//    {
//        newMesh.SetTriangles(triangles[i].ToArray(), i);
//    }
//    newObject.GetComponent<MeshFilter>().mesh = newMesh;
//    newObject.AddComponent<MeshRenderer>();
//    newObject.GetComponent<MeshRenderer>().materials = VisibilityCheck.Instance.testObject.GetComponent<MeshRenderer>().materials;

//}

//private void AssembleAndLoadBundle(int bundleId, BundleTransmission transmission)
//{
//    Debug.Log($"All chunks received for bundleId {bundleId}. Reassembling asset bundle...");
//    int totalSize = 0;
//    for (int i = 0; i < transmission.totalChunks; i++)
//    {
//        totalSize += transmission.chunks[i].Length;
//    }

//    byte[] assetBundleData = new byte[totalSize];
//    int offset = 0;
//    for (int i = 0; i < transmission.totalChunks; i++)
//    {
//        byte[] chunk = transmission.chunks[i];
//        Buffer.BlockCopy(chunk, 0, assetBundleData, offset, chunk.Length);
//        offset += chunk.Length;
//    }

//    AssetBundle bundle = AssetBundle.LoadFromMemory(assetBundleData);
//    if (bundle == null)
//    {
//        Debug.LogError("Failed to load AssetBundle.");
//        return;
//    }
//    Debug.Log("AssetBundle loaded successfully.");

//    // Attempt to load and instantiate the prefab. Adjust the asset name as necessary.
//    GameObject prefab = bundle.LoadAsset<GameObject>("balcony_1");
//    if (prefab != null)
//    {
//        Instantiate(prefab);
//    }
//    else
//    {
//        Debug.LogError("Prefab 'balcony_1' not found in AssetBundle.");
//    }
//}