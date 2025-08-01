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


public class BroadcastControl : Singleton<BroadcastControl>
{
    private IPAddress BROADCAST_IP = IPAddress.Broadcast;// IPAddress.Parse("192.168.1.255");//  //IPAddress.Parse("192.168.0.211");
    private const int PORT = 5005;
    // We use a separate port for retransmission requests.
    private const int RETRANSMISSION_PORT = 5006;

    private UdpClient udpClient;
    private bool appRunning;
    //Thread retransmissionThread;
    public CancellationToken ct;
    //private const int CHUNK_SIZE = 1400;
    private const int HEADER_SIZE = 12;
    private int bundleID, currentChunkToSend;
    // Dictionary to store each chunk's packet for possible retransmission.
    private Dictionary<int, byte[]> chunks;
    //private MeshVariant mv;
    private NetworkControl nc;
    private VisibilityCheck vc;
    private ClusterControl cc;

    void Start()
    {
        udpClient = new UdpClient();
        udpClient.Client.SendBufferSize = 1024 * 1024;
        udpClient.EnableBroadcast = true;
        appRunning = true;
        vc = VisibilityCheck.Instance; 
        cc = ClusterControl.Instance;
        nc = NetworkControl.Instance;
        bundleID = 0;
        chunks = new Dictionary<int, byte[]>();

        if (cc.SimulationStrategy == SimulationStrategyDropDown.RealUserCluster || cc.SimulationStrategy == SimulationStrategyDropDown.RealUserIndi)
        {
            Debug.Log($"nc cts null: {nc.cts is null}");
            ct = nc.cts.Token;
            // Start the thread that listens for retransmission requests.
            //retransmissionThread = new Thread(() => ListenForRetransmissionRequests());
            //retransmissionThread.Start();
        }
    }

    // Sends the provided message as a broadcast UDP packet.
    public void BroadcastChunk(byte[] message)
    {
        switch (nc.sendingMode) {

            case SendingMode.BROADCAST:
                udpClient.EnableBroadcast = true;
                IPEndPoint endPoint = new IPEndPoint(BROADCAST_IP, PORT);
                byte[] new_message = new byte[message.Length + sizeof(int)];
                Buffer.BlockCopy(BitConverter.GetBytes((int) nc.cc.meshDecodeMethod), 0, new_message, 0, sizeof(int));
                Buffer.BlockCopy(message, 0, new_message, sizeof(int), message.Length);
                udpClient.Send(new_message, new_message.Length, endPoint);
                break;

            case SendingMode.MULTICAST:
                udpClient.EnableBroadcast = false;
                IPAddress multicastAddress = IPAddress.Parse("230.0.0.1"); // pick any in 224.x.x.x - 239.x.x.x
                IPEndPoint multicastEndPoint = new IPEndPoint(multicastAddress, PORT);
                new_message = new byte[message.Length + sizeof(int)];
                Buffer.BlockCopy(BitConverter.GetBytes((int)nc.cc.meshDecodeMethod), 0, new_message, 0, sizeof(int));
                Buffer.BlockCopy(message, 0, new_message, sizeof(int), message.Length);
                udpClient.Send(new_message, new_message.Length, multicastEndPoint);
                break;

            case SendingMode.UNICAST_TCP:
                new_message = new byte[message.Length + sizeof(int) * 3];
                Buffer.BlockCopy(BitConverter.GetBytes(new_message.Length - sizeof(int)), 0, new_message, 0, sizeof(int));
                Buffer.BlockCopy(BitConverter.GetBytes((int)TCPMessageType.CHUNK), 0, new_message, sizeof(int), sizeof(int));
                Buffer.BlockCopy(BitConverter.GetBytes((int)nc.cc.meshDecodeMethod), 0, new_message, sizeof(int) * 2, sizeof(int));
                Buffer.BlockCopy(message, 0, new_message, sizeof(int) * 3, message.Length);
                nc.tc.SendMessageToClient(IPAddress.Parse("192.168.1.173"), new_message);
                nc.tc.SendMessageToClient(IPAddress.Parse("192.168.1.101"), new_message);
                nc.tc.SendMessageToClient(IPAddress.Parse("192.168.1.174"), new_message);
                nc.tc.SendMessageToClient(IPAddress.Parse("192.168.1.240"), new_message);
                break;

            case SendingMode.UNICAST_UDP:
                udpClient.EnableBroadcast = false;
                IPAddress unicastAddress = IPAddress.Parse("192.168.1.240");
                IPEndPoint unicastEndPoint = new IPEndPoint(unicastAddress, PORT);
                new_message = new byte[message.Length + sizeof(int)];
                Buffer.BlockCopy(BitConverter.GetBytes((int) nc.cc.meshDecodeMethod), 0, new_message, 0, sizeof(int));
                Buffer.BlockCopy(message, 0, new_message, sizeof(int), message.Length);
                udpClient.Send(new_message, new_message.Length, unicastEndPoint);
                break;

            default:
                break;
        }
    }

    // Listens for retransmission requests on RETRANSMISSION_PORT.
    private void ListenForRetransmissionRequests()
    {
        UdpClient requestListener = new UdpClient(RETRANSMISSION_PORT);
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, RETRANSMISSION_PORT);
        Debug.Log("[Server] Retransmission listener started on port " + RETRANSMISSION_PORT);
        while (appRunning)
        {
            byte[] reqData = requestListener.Receive(ref remoteEP);
            string jsonReq = Encoding.UTF8.GetString(reqData);
            RetransmissionRequest req = JsonUtility.FromJson<RetransmissionRequest>(jsonReq);
            Debug.Log($"[Server] Received retransmission request for bundleId {req.objectID} for missing chunks: {string.Join(",", req.missingChunks)}");
            Dispatcher.Instance.Enqueue(() => AddMissingChunks(req, remoteEP));

            // Only process requests for the current bundle.
            //if (req.objectID != bundleID)
            //{
            //    Debug.Log($"[Server] Retransmission request bundleId {req.objectID} does not match current bundleId {bundleID}. Ignoring.");
            //    continue;
            //}
            // Resend each missing chunk.
            //foreach (int chunkIndex in req.missingChunks)
            //{
            //    if (chunks.ContainsKey(chunkIndex))
            //    {
            //        byte[] packet = chunks[chunkIndex];
            //        Broadcast(packet);
            //        Debug.Log($"[Server] Retransmitted chunk {chunkIndex}.");
            //        Thread.Sleep(10); // Brief delay.
            //    }
            //    else
            //    {
            //        Debug.LogWarning($"[Server] Chunk {chunkIndex} not found for retransmission.");
            //    }
            //}
        }
        requestListener.Close();
    }

    public void AddMissingChunks(RetransmissionRequest req, IPEndPoint remoteEP)
    {
        float distance = Vector3.Distance(nc.vc.objectsInScene[req.objectID].transform.position, nc.tc.addressToUser[remoteEP.Address].transform.position);
        for (int i = 0; i < req.missingChunks.Length; i++)
        {
            byte[] missingChunk = nc.cc.objectChunksVTSeparate[req.objectID][req.missingChunks[i]];
            if (!nc.cc.chunksToSend.Contains((req.objectID, i)))
            {
            }
        }
    }

    void OnApplicationQuit()
    {
        appRunning = false;
        //if (retransmissionThread != null && retransmissionThread.IsAlive)
        //{
        //    retransmissionThread.Join();
        //}
        udpClient.Close();
        udpClient.Dispose();
    }

    private IEnumerator WaitOneSecond()
    {
        yield return new WaitForSeconds(1);
    }
}

//private void DecodeMesh(List<byte[]> chunks)
//{
//    List<List<int>> triangles = new List<List<int>>();
//    int subMeshCount = VisibilityCheck.Instance.testObject.GetComponent<MeshFilter>().mesh.subMeshCount;
//    for (int i = 0; i < subMeshCount; i++) { 
//        triangles.Add(new List<int>());
//    }
//    int numVerticesPerChunk = 57;
//    int totalVertexNum = 20706;
//    Vector3[] vertices = new Vector3[totalVertexNum];
//    Vector3[] normals = new Vector3[totalVertexNum];
//    for (int i = 0; i < chunks.Count; i++)
//    {
//        byte[] chunk = chunks[i];
//        int cursor = 0;
//        char vorT = BitConverter.ToChar(chunk, cursor);
//        int objectID = BitConverter.ToInt32(chunk, cursor += sizeof(char));
//        int chunkID = BitConverter.ToInt32(chunk, cursor += sizeof(int));
//        if (vorT == 'V')
//        {
//            cursor += sizeof(int);
//            int numVerticesInChunk = (chunk.Length - cursor) / (sizeof(float) * 6);
//            float[] floatArray = new float[numVerticesInChunk * 6];
//            Buffer.BlockCopy(chunk, cursor, floatArray, 0, floatArray.Length * sizeof(float));
//            for (int j = 0; j < numVerticesInChunk; j++)
//            {
//                vertices[chunkID * numVerticesPerChunk + j] = new Vector3(floatArray[j * 6], floatArray[j * 6 + 1], floatArray[j * 6 + 2]);
//                normals[chunkID * numVerticesPerChunk + j] = new Vector3(floatArray[j * 6 + 3], floatArray[j * 6 + 4], floatArray[j * 6 + 5]);
//            }
//        } else if (vorT == 'T')
//        {
//            int subMeshIdx = BitConverter.ToInt32(chunk, cursor += sizeof(int));
//            cursor += sizeof(int);
//            int numVerticesInChunk = (chunk.Length - cursor) / sizeof(int);
//            int[] intArray = new int[numVerticesInChunk];
//            Buffer.BlockCopy(chunk, cursor, intArray, 0, intArray.Length * sizeof(int));
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

//private void SendBroadcastMessage(string message)
//{
//    Debug.Log("[Server] Broadcasting object data...");
//    try
//    {
//        byte[] data = Encoding.UTF8.GetBytes(message);
//        if (nc.isBroadcast)
//        {
//            udpClient.EnableBroadcast = true;
//            IPEndPoint endPoint = new IPEndPoint(BROADCAST_IP, PORT);
//            udpClient.Send(data, data.Length, endPoint);
//        } else
//        {
//            IPAddress multicastAddress = IPAddress.Parse("192.168.1.3"); // pick any in 224.x.x.x - 239.x.x.x
//            IPEndPoint multicastEndPoint = new IPEndPoint(multicastAddress, PORT);
//            udpClient.Send(data, data.Length, multicastEndPoint);
//        }
//    }
//    catch (Exception e)
//    {
//        Debug.LogError($"[Server] Error broadcasting message: {e.Message}");
//    }
//}

//private void BroadcastPrefab(string prefab)
//{
//    string assetBundlePath = $".\\Assets\\AssetBundles\\Prefabs\\Android/{prefab}";
//    byte[] assetBundleData = File.ReadAllBytes(assetBundlePath);
//    Debug.Log($"[Server] Broadcasting {prefab} {assetBundleData.Length} bytes...");
//    // Use a dispatcher to ensure that SendAssetBundleInChunks runs on the main thread if needed.
//    Dispatcher.Instance.Enqueue(() => SendAssetBundleInChunks(assetBundleData));
//}

//// Splits the asset bundle data into chunks, stores each chunk, and broadcasts them.
//private void SendAssetBundleInChunks(byte[] assetBundleData)
//{
//    bundleID++;
//    Debug.Log("Entered SendAssetBundleInChunks function.");
//    int totalChunks = (int)Math.Ceiling(assetBundleData.Length / (float)CHUNK_SIZE);

//    Debug.Log($"Sending bundleId {bundleID} in {totalChunks} chunks.");

//    // Clear any previous chunk data.
//    chunks.Clear();

//    for (int i = 0; i < totalChunks; i++)
//    {
//        int offset = i * CHUNK_SIZE;
//        int currentChunkSize = Math.Min(CHUNK_SIZE, assetBundleData.Length - offset);

//        // Create a packet: header (12 bytes) + chunk data.
//        byte[] packet = new byte[HEADER_SIZE + currentChunkSize];

//        // Header: bundleID, totalChunks, chunkIndex.
//        Array.Copy(BitConverter.GetBytes(bundleID), 0, packet, 0, 4);
//        Array.Copy(BitConverter.GetBytes(totalChunks), 0, packet, 4, 4);
//        Array.Copy(BitConverter.GetBytes(i), 0, packet, 8, 4);

//        // Copy the chunk data after the header.
//        Array.Copy(assetBundleData, offset, packet, HEADER_SIZE, currentChunkSize);
//        chunks[i] = packet;

//        Broadcast(packet);
//        Debug.Log($"Sent chunk {i + 1}/{totalChunks}, size: {packet.Length} bytes");
//        Thread.Sleep(10); // Brief pause to reduce network congestion.
//    }
//}