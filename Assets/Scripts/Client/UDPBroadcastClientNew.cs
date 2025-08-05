using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TMPro;
//using Unity.Android.Gradle;
using UnityEngine;
using UnityEngine.XR;

[Serializable]
public class RetransmissionRequest
{
    public int objectID;
    public int[] missingChunks;
}
public class Chunk
{
    public MeshDecodeMethod meshDecodeMethod;
    public int id;
    public char type;
    public int objectID;
    public int subMeshIdx;
    public DateTime chunkRecvTime;
    public byte[] data;
}

public enum MeshDecodeMethod
{
    VTSeparate,
    VTGrouped
}

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

public class UDPBroadcastClientNew : MonoBehaviour
{
    public TextMeshProUGUI m_TextLog;
    public TextMeshProUGUI m_TextLog2;
    private string logContext = "";
    public bool isShowLog = false;
    public bool isMulticast = true;

    [SerializeField] private TCPClient m_TCPClient;
    [SerializeField] private ResourceLoader m_ResourceLoader;

    public int portUDP = 5005;
    public int portTCP = 5006;
    public Camera lightCamera;
    private UdpClient udpClient;
    private IPEndPoint localEP;
    private string multicastAddr = "230.0.0.1";
    private IPAddress multicastAddress;
    private const int HEADER_SIZE = 12;
    private int bufferSize = 1024 * 1024;

    public int numVerticesPerChunk = 57;
    private Dictionary<int, GameObject> recGameObjects = new Dictionary<int, GameObject>();
    private Dictionary<int, Vector3[]> verticesDict = new Dictionary<int, Vector3[]>();
    private Dictionary<int, List<List<int>>> trianglesDict = new Dictionary<int, List<List<int>>>();
    private Dictionary<int, Vector3[]> normalsDict = new Dictionary<int, Vector3[]>();
    private int recevTotalChunkPerObject = 0;
    private int recevTotalChunkN = 0;

    private float[] reusableFloatBuffer = new float[57 * 6];
    private int[] reusableIntBuffer = new int[1024];

    private readonly Queue<Chunk> chunkQueue = new Queue<Chunk>();
    private readonly object chunkQueueLock = new object();
    private int maxChunksPerFrame = 50, currentColliderToUpdate, colliderToUpdateEachFrame; // You can tweak this
    private float lastAdjustTime = 0f, adjustCooldown = 0.3f, lastColliderUpdateTime, colliderUpdateInterval;
    private bool isShuttingDown = false;

    private Matrix4x4 lightViewProjMatrix;
    private Texture2D shadowMap;
    
    private static UdpClient retransmissionClient = new UdpClient();

    private class MeshTransmission
    {
        public int totalMeshChunks;
        public Dictionary<int, Chunk> chunks = new Dictionary<int, Chunk>();
        public DateTime firstChunkTime;
        public IPEndPoint remoteEP;
    }
    private Dictionary<int, MeshTransmission> activeMeshTransmissions = new Dictionary<int, MeshTransmission>();

    [SerializeField] private Transform headsetTransform;

    private StreamWriter logWriter;
    private List<string> chunksThisFrame = new List<string>();
    private string logFilePath;

    private void Awake()
    {
        retransmissionClient = new UdpClient();
    }
    private void Start()
    {
        shadowMap = Resources.Load<Texture2D>("Materials/Textures/shadowMap_new");
        lightViewProjMatrix = lightCamera.projectionMatrix * lightCamera.worldToCameraMatrix;
        //Shader.SetGlobalTexture("_CustomShadowMap", shadowMap);
        //Shader.SetGlobalMatrix("_LightViewProjection", lightViewProjMatrix);
        colliderUpdateInterval = 2f;
        colliderToUpdateEachFrame = 10;
        currentColliderToUpdate = 0;
        lastColliderUpdateTime = Time.time;
        string filename = $"ClientRuntimeLog_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jsonl";
        logFilePath = Path.Combine(Application.persistentDataPath, filename);
        logWriter = new StreamWriter(logFilePath, append: false);
    }

    private void OnEnable()
    {
        m_TCPClient.OnReceivedServerTable += OnRecevTable;
    }

    private void OnDisable()
    {
        m_TCPClient.OnReceivedServerTable -= OnRecevTable;
    }

    private void OnRecevTable()
    {
        Debug.Log($"[The first object info]: name-{m_TCPClient.objectHolders[0].prefabName}, vertN-{m_TCPClient.objectHolders[0].totalVertNum}, submeshN-{m_TCPClient.objectHolders[0].submeshCount}");
        StartListenToServer();
    }

    void Update()
    {
        if (isShowLog && m_TextLog != null)
        {
            m_TextLog.text = $"Received Chunks Numbers - [{recevTotalChunkN}]" +
            $"max chunks per frame: \n{maxChunksPerFrame}";
        }

        InputDevice rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        bool aPressed = false, bPressed = false;
        rightHand.TryGetFeatureValue(CommonUsages.primaryButton, out aPressed);
        rightHand.TryGetFeatureValue(CommonUsages.secondaryButton, out bPressed);

        if (Time.time - lastAdjustTime > adjustCooldown)
        {
            if (aPressed)
            {
                maxChunksPerFrame += 1;
                lastAdjustTime = Time.time;
            }
            else if (bPressed)
            {
                maxChunksPerFrame = Mathf.Max(1, maxChunksPerFrame - 1);
                lastAdjustTime = Time.time;
            }
        }

        int processed = 0;
        lock (chunkQueueLock)
        {
            while (chunkQueue.Count > 0 && processed < maxChunksPerFrame)
            {
                var chunk = chunkQueue.Dequeue();

                if (!activeMeshTransmissions.TryGetValue(chunk.objectID, out var transmission))
                {
                    transmission = new MeshTransmission  // lack of remoteEP
                    {
                        totalMeshChunks = 10000,
                        firstChunkTime = DateTime.UtcNow
                    };
                    activeMeshTransmissions[chunk.objectID] = transmission;
                }

                if (!transmission.chunks.ContainsKey(chunk.id))
                {
                    recevTotalChunkPerObject++;
                    recevTotalChunkN++;

                    transmission.chunks[chunk.id] = chunk;
                    switch (chunk.meshDecodeMethod)
                    {
                        case MeshDecodeMethod.VTSeparate:
                            DecodeChunkVRSeparate(chunk);
                            break;

                        case MeshDecodeMethod.VTGrouped:
                            DecodeChunkVRGrouped(chunk);
                            break;
                    }
                    
                }

                processed++;
            }
        }

        if (headsetTransform != null)
        {
            Vector3 pos = headsetTransform.position;
            Vector3 rot = headsetTransform.eulerAngles;

            string timeStamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            string frameEntry = $"{{\"time\":\"{timeStamp}\",\"headset\":{{\"position\":[{pos.x:F4},{pos.y:F4},{pos.z:F4}],\"rotationEuler\":[{rot.x:F1},{rot.y:F1},{rot.z:F1}]}},\"chunks\":[{string.Join(",", chunksThisFrame)}]}}";

            logWriter.WriteLine(frameEntry);
            logWriter.Flush();
            chunksThisFrame.Clear();
        }

        //if (Time.time - lastColliderUpdateTime > colliderUpdateInterval)
        //{
            
        //    foreach (int objectID in recGameObjects.Keys)
        //    {
        //        ObjectHolder holder = m_TCPClient.objectHolders[objectID];
        //        if (holder.needUpdateCollider && recGameObjects.TryGetValue(objectID, out GameObject go))
        //        {
        //            Mesh mesh = go.GetComponent<MeshFilter>().mesh;
        //            if (mesh != null)
        //            {
        //                MeshCollider collider = go.GetComponent<MeshCollider>();
        //                if (collider != null)
        //                {
        //                    collider.sharedMesh = null; // clear the old mesh
        //                    collider.sharedMesh = mesh; // assign the new mesh
        //                }
        //                holder.needUpdateCollider = false; // reset the flag
        //            }
        //        }
        //    }
        //    lastColliderUpdateTime = Time.time;
        //}

        int numCollidersUpdatedThisFrame = 0;
        int objectIDStarted = currentColliderToUpdate;
        while (numCollidersUpdatedThisFrame < colliderToUpdateEachFrame && m_TCPClient.objectHolders is not null)
        {
            currentColliderToUpdate = (currentColliderToUpdate + 1) % m_TCPClient.objectHolders.Length;
            if (currentColliderToUpdate == objectIDStarted)
                break;
            ObjectHolder holder = m_TCPClient.objectHolders[currentColliderToUpdate];
            if (holder.needUpdateCollider && recGameObjects.TryGetValue(currentColliderToUpdate, out GameObject go))
            {
                Mesh mesh = go.GetComponent<MeshFilter>().mesh;
                if (mesh != null)
                {
                    MeshCollider collider = go.GetComponent<MeshCollider>();
                    if (collider != null)
                    {
                        collider.sharedMesh = null; // clear the old mesh
                        collider.sharedMesh = mesh; // assign the new mesh
                    }
                }
                holder.needUpdateCollider = false;
                numCollidersUpdatedThisFrame++;
            }
        }
    }

    private void StartListenToServer()
    {
        try
        {
            if (isMulticast) // multicast
            {
                multicastAddress = IPAddress.Parse(multicastAddr);
                udpClient = new UdpClient(AddressFamily.InterNetwork);
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                localEP = new IPEndPoint(IPAddress.Any, portUDP);
                udpClient.Client.Bind(localEP);
                udpClient.JoinMulticastGroup(multicastAddress);
            }
            else // broadcast
            {
                udpClient = new UdpClient(portUDP);
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }

            udpClient.Client.ReceiveBufferSize = bufferSize;

            int recvBuf = (int)udpClient.Client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer);
            int sendBuf = (int)udpClient.Client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer);
            Debug.Log($"[+++++++] UDP client listening on the server, recvBuf-{recvBuf}, sendBuf-{sendBuf}");
            m_TextLog2.text = $"[+++++++] UDP client listening on the server, recvBuf-{recvBuf}, sendBuf-{sendBuf}";

            udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
            //StartCoroutine(CheckRetransmissions());
        }
        catch (Exception ex)
        {
            Debug.Log("UDP client initialization failed: " + ex.Message);
        }
    }



    private void ReceiveMeshChunks(IAsyncResult ar)
    {
        // Check if shutting down or socket already closed
        if (isShuttingDown || udpClient == null)
            return;

        try
        {
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, portUDP);
            byte[] packet = udpClient.EndReceive(ar, ref remoteEP);

            if (packet.Length < HEADER_SIZE)
            {
                Debug.LogWarning("Received packet too small to contain header.");
            }
            else
            {
                MeshDecodeMethod method = (MeshDecodeMethod)BitConverter.ToInt32(packet, 0);
                switch (method)
                {
                    case MeshDecodeMethod.VTSeparate:
                        DecodePacketMeshSeparate(packet, remoteEP);
                        break;

                    case MeshDecodeMethod.VTGrouped:
                        DecodePacketMeshGrouped(packet, remoteEP);
                        break;
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Expected when shutting down — safe to ignore
        }
        catch (Exception ex)
        {
            if (!isShuttingDown) // suppress if already quitting
                Debug.LogError("ReceiveMeshChunks error: " + ex.Message);
        }

        // Re-register only if still running
        if (!isShuttingDown && udpClient != null)
        {
            try
            {
                udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
            }
            catch (ObjectDisposedException)
            {
                // Also safe to ignore if shutdown race
            }
        }
    }


    private void DecodePacketMeshSeparate(byte[] packet, IPEndPoint remoteEP)
    {
        int cursor = 0;
        MeshDecodeMethod method = (MeshDecodeMethod)BitConverter.ToInt32(packet, cursor);
        cursor += sizeof(int);

        char submeshType = BitConverter.ToChar(packet, cursor);
        int objectId = -1, chunkId = -1, submeshId = -1, headerSize = -1;

        if (submeshType == 'V')
        {
            objectId = BitConverter.ToInt32(packet, cursor += 2);
            chunkId = BitConverter.ToInt32(packet, cursor += sizeof(int));
            headerSize = cursor += sizeof(int);
        }
        else if (submeshType == 'T')
        {
            objectId = BitConverter.ToInt32(packet, cursor += 2);
            chunkId = BitConverter.ToInt32(packet, cursor += sizeof(int));
            submeshId = BitConverter.ToInt32(packet, cursor += sizeof(int));
            headerSize = cursor += sizeof(int);
        }
        else
        {
            Debug.LogError("Unknown packet type.");
            udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
            return;
        }

        // parse the packet data
        int dataSize = packet.Length - headerSize;
        byte[] chunkData = new byte[dataSize];
        Buffer.BlockCopy(packet, headerSize, chunkData, 0, dataSize);

        // init the chunk in the client
        var newChunk = new Chunk
        {
            meshDecodeMethod = method,
            id = chunkId,
            type = submeshType,
            objectID = objectId,
            subMeshIdx = submeshId,
            chunkRecvTime = DateTime.UtcNow,
            data = chunkData
        };

        ObjectHolder objectHolder = m_TCPClient.objectHolders[objectId];
        if (objectHolder.chunks_VTSeparate.Count == 0)
        {
            objectHolder.ifVisible = true;
            objectHolder.ifOwned = true;
            objectHolder.remoteEP = remoteEP;
            objectHolder.firstChunkTime = DateTime.UtcNow;
        }
        objectHolder.latestChunkTime = DateTime.UtcNow;
        if (!objectHolder.chunks_VTSeparate.ContainsKey(chunkId))
        {
            objectHolder.chunks_VTSeparate.Add(chunkId, newChunk);

            lock (chunkQueueLock)
            {
                chunkQueue.Enqueue(newChunk);
            }
        }
    }

    private void DecodePacketMeshGrouped(byte[] packet, IPEndPoint remoteEP)
    {
        //Debug.Log($"grouped");
        int cursor = 0;
        MeshDecodeMethod method = (MeshDecodeMethod)BitConverter.ToInt32(packet, cursor);
        cursor += sizeof(int);

        char submeshType = BitConverter.ToChar(packet, cursor);

        int objectId = BitConverter.ToInt32(packet, cursor += 2);
        int chunkId = BitConverter.ToInt32(packet, cursor += sizeof(int));
        int submeshId = BitConverter.ToInt32(packet, cursor += sizeof(int));
        int headerSize = cursor += sizeof(int);

        // parse the packet data
        int dataSize = packet.Length - headerSize;
        byte[] chunkData = new byte[dataSize];
        Buffer.BlockCopy(packet, headerSize, chunkData, 0, dataSize);

        //Debug.Log($"{chunkId}, {submeshType}, {objectId}, {submeshId}");
        // init the chunk in the client
        var newChunk = new Chunk
        {
            meshDecodeMethod = method,
            id = chunkId,
            type = submeshType,
            objectID = objectId,
            subMeshIdx = submeshId,
            chunkRecvTime = DateTime.UtcNow,
            data = chunkData
        };

        ObjectHolder objectHolder = m_TCPClient.objectHolders[objectId];
        if (objectHolder.chunks_VTGrouped.Count == 0)
        {
            objectHolder.ifVisible = true;
            objectHolder.ifOwned = true;
            objectHolder.remoteEP = remoteEP;
            objectHolder.firstChunkTime = DateTime.UtcNow;
        }
        objectHolder.latestChunkTime = DateTime.UtcNow;
        if (!objectHolder.chunks_VTGrouped.ContainsKey(chunkId))
        {
            objectHolder.chunks_VTGrouped.Add(chunkId, newChunk);

            lock (chunkQueueLock)
            {
                chunkQueue.Enqueue(newChunk);
            }
        }
    }

    public void ParseMessageForChunks(byte[] packet)
    {
        byte[] message = new byte[packet.Length - sizeof(int)];
        Buffer.BlockCopy(packet, sizeof(int), message, 0, message.Length);
        MeshDecodeMethod method = (MeshDecodeMethod)BitConverter.ToInt32(message, 0);
        switch (method)
        {
            case MeshDecodeMethod.VTSeparate:
                DecodePacketMeshSeparate(message, new IPEndPoint(IPAddress.Parse("192.168.1.188"), 13000));
                break;

            case MeshDecodeMethod.VTGrouped:
                DecodePacketMeshGrouped(message, new IPEndPoint(IPAddress.Parse("192.168.1.188"), 13000));
                break;
        }
        //Debug.Log($"packet size: {packet.Length}");
        // parse the packet header
        //char submeshType = BitConverter.ToChar(packet, 0);
        //int objectId = -1, chunkId = -1, submeshId = -1, headerSize = -1;

        //if (submeshType == 'V')
        //{
        //    objectId = BitConverter.ToInt32(packet, 2);
        //    chunkId = BitConverter.ToInt32(packet, 6);
        //    headerSize = 10;
        //}
        //else if (submeshType == 'T')
        //{
        //    objectId = BitConverter.ToInt32(packet, 2);
        //    chunkId = BitConverter.ToInt32(packet, 6);
        //    submeshId = BitConverter.ToInt32(packet, 10);
        //    headerSize = 14;
        //}
        //else
        //{
        //    Debug.LogError("Unknown packet type.");
        //    udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
        //    return;
        //}

        //// parse the packet data
        //int dataSize = packet.Length - headerSize;
        //byte[] chunkData = new byte[dataSize];
        //Buffer.BlockCopy(packet, headerSize, chunkData, 0, dataSize);

        //// init the chunk in the client
        //var newChunk = new Chunk
        //{
        //    id = chunkId,
        //    type = submeshType,
        //    objectID = objectId,
        //    subMeshIdx = submeshId,
        //    chunkRecvTime = DateTime.UtcNow,
        //    data = chunkData
        //};

        //lock (chunkQueueLock)
        //{
        //    chunkQueue.Enqueue(newChunk);
        //}
    }

    //private void DecodePacketMeshSeparate(byte[] packet, IPEndPoint remoteEP)
    //{
    //    int cursor = 0;
    //    MeshDecodeMethod method = (MeshDecodeMethod)BitConverter.ToInt32(packet, cursor);
    //    cursor += sizeof(int);

    //    char submeshType = BitConverter.ToChar(packet, cursor);
    //    int objectId = -1, chunkId = -1, submeshId = -1, headerSize = -1;

    //    if (submeshType == 'V')
    //    {
    //        objectId = BitConverter.ToInt32(packet, cursor += 2);
    //        chunkId = BitConverter.ToInt32(packet, cursor += sizeof(int));
    //        headerSize = cursor += sizeof(int);
    //    }
    //    else if (submeshType == 'T')
    //    {
    //        objectId = BitConverter.ToInt32(packet, cursor += 2);
    //        chunkId = BitConverter.ToInt32(packet, cursor += sizeof(int));
    //        submeshId = BitConverter.ToInt32(packet, cursor += sizeof(int));
    //        headerSize = cursor += sizeof(int);
    //    }
    //    else
    //    {
    //        Debug.LogError("Unknown packet type.");
    //        udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
    //        return;
    //    }

    //    // parse the packet data
    //    int dataSize = packet.Length - headerSize;
    //    byte[] chunkData = new byte[dataSize];
    //    Buffer.BlockCopy(packet, headerSize, chunkData, 0, dataSize);

    //    // init the chunk in the client
    //    var newChunk = new Chunk
    //    {
    //        meshDecodeMethod = method,
    //        id = chunkId,
    //        type = submeshType,
    //        objectID = objectId,
    //        subMeshIdx = submeshId,
    //        chunkRecvTime = DateTime.UtcNow,
    //        data = chunkData
    //    };

    //    ObjectHolder objectHolder = m_TCPClient.objectHolders[objectId];
    //    if (objectHolder.chunks_VTSeparate.Count == 0)
    //    {
    //        objectHolder.ifVisible = true;
    //        objectHolder.ifOwned = true;
    //        objectHolder.remoteEP = remoteEP;
    //        objectHolder.firstChunkTime = DateTime.UtcNow;
    //    }
    //    objectHolder.latestChunkTime = DateTime.UtcNow;
    //    if (!objectHolder.chunks_VTSeparate.ContainsKey(chunkId))
    //    {
    //        objectHolder.chunks_VTSeparate.Add(chunkId, newChunk);

    //        lock (chunkQueueLock)
    //        {
    //            chunkQueue.Enqueue(newChunk);
    //        }
    //    }
    //}

    private void DecodeChunkVRSeparate(Chunk chunk)
    {
        int chunkID = chunk.id;
        char vorT = chunk.type;
        int objectID = chunk.objectID;
        byte[] chunk_data = chunk.data;

        var holder = m_TCPClient.objectHolders[objectID];
        int totalVertexNum = holder.totalVertNum;
        int subMeshCount = holder.submeshCount;
        string[] materialNames = holder.materialNames;
        Vector3 position = holder.position;
        Vector3 eulerAngles = holder.eulerAngles;
        Vector3 scale = holder.scale;

        if (!recGameObjects.ContainsKey(objectID))
        {
            Vector3[] vertices = new Vector3[totalVertexNum];
            Vector3[] normals = new Vector3[totalVertexNum];
            verticesDict[objectID] = vertices;
            normalsDict[objectID] = normals;

            List<List<int>> triangles = new List<List<int>>();
            for (int i = 0; i < subMeshCount; i++)
            {
                triangles.Add(new List<int>());
            }
            trianglesDict[objectID] = triangles;
        }

        var verticesArr = verticesDict[objectID];
        var normalsArr = normalsDict[objectID];
        var trianglesArr = trianglesDict[objectID];

        if (vorT == 'V')
        {
            int count = chunk_data.Length / sizeof(float);
            Buffer.BlockCopy(chunk_data, 0, reusableFloatBuffer, 0, chunk_data.Length);

            for (int j = 0; j < count / 6; j++)
            {
                int baseIdx = chunkID * numVerticesPerChunk + j;
                verticesArr[baseIdx] = new Vector3(reusableFloatBuffer[j * 6], reusableFloatBuffer[j * 6 + 1], reusableFloatBuffer[j * 6 + 2]);
                normalsArr[baseIdx] = new Vector3(reusableFloatBuffer[j * 6 + 3], reusableFloatBuffer[j * 6 + 4], reusableFloatBuffer[j * 6 + 5]);
            }
            string vEntry = $"{{\"objectID\":{objectID},\"chunkID\":{chunkID},\"type\":\"V\",\"chunkRecvTime\":\"{chunk.chunkRecvTime}\"}}";
            chunksThisFrame.Add(vEntry);
        }
        else if (vorT == 'T')
        {
            int count = chunk_data.Length / sizeof(int);
            Buffer.BlockCopy(chunk_data, 0, reusableIntBuffer, 0, chunk_data.Length);
            trianglesArr[chunk.subMeshIdx].AddRange(new ArraySegment<int>(reusableIntBuffer, 0, count)); 
            string tEntry = $"{{\"objectID\":{objectID},\"chunkID\":{chunkID},\"type\":\"T\",\"subMeshIdx\":{chunk.subMeshIdx},\"chunkRecvTime\":\"{chunk.chunkRecvTime}\"}}";
            chunksThisFrame.Add(tEntry);
        }

        if (!recGameObjects.ContainsKey(objectID))
        {
            GameObject recGameObject = new GameObject();
            recGameObject.AddComponent<MeshFilter>();

            Mesh newMesh = new Mesh();
            newMesh.vertices = verticesArr;
            newMesh.normals = normalsArr;
            newMesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
            {
                newMesh.SetTriangles(trianglesArr[i], i);
            }

            // IMPORTANT: Set the recompute the mesh bounds to avoid camera culling issues
            newMesh.RecalculateBounds();

            recGameObject.GetComponent<MeshFilter>().mesh = newMesh;
            recGameObject.AddComponent<MeshRenderer>();

            List<Material> materials = new List<Material>();
            foreach (string matName in materialNames)
            {
                materials.Add(m_ResourceLoader.LoadMaterialByName(matName));
            }
            recGameObject.GetComponent<MeshRenderer>().materials = materials.ToArray();
            recGameObject.transform.position = position;
            recGameObject.transform.eulerAngles = eulerAngles;
            recGameObject.transform.localScale = scale;

            recGameObjects[objectID] = recGameObject;
        }
        else
        {
            Mesh mesh = recGameObjects[objectID].GetComponent<MeshFilter>().mesh;
            mesh.vertices = verticesArr;
            mesh.normals = normalsArr;
            for (int i = 0; i < subMeshCount; i++)
            {
                mesh.SetTriangles(trianglesArr[i], i);
            }

            // IMPORTANT: Set the recompute the mesh bounds to avoid camera culling issues
            mesh.RecalculateBounds();
        }
    }

    private void DecodeChunkVRGrouped(Chunk chunk)
    {
        int objectID = chunk.objectID;
        byte[] chunk_data = chunk.data;

        var holder = m_TCPClient.objectHolders[objectID];
        int totalVertexNum = holder.totalVertNum;
        int subMeshCount = holder.submeshCount;
        string[] materialNames = holder.materialNames;
        Vector3 position = holder.position;
        Vector3 eulerAngles = holder.eulerAngles;
        Vector3 scale = holder.scale;

        if (!recGameObjects.ContainsKey(objectID))
        {
            Vector3[] vertices = new Vector3[totalVertexNum];
            Vector3[] normals = new Vector3[totalVertexNum];
            verticesDict[objectID] = vertices;
            normalsDict[objectID] = normals;

            List<List<int>> triangles = new List<List<int>>();
            for (int i = 0; i < subMeshCount; i++)
            {
                triangles.Add(new List<int>());
            }
            trianglesDict[objectID] = triangles;
        }

        var verticesArr = verticesDict[objectID];
        var normalsArr = normalsDict[objectID];
        var trianglesArr = trianglesDict[objectID];
        string gEntry = $"{{\"objectID\":{objectID},\"chunkID\":{chunk.id},\"type\":\"G\",\"subMeshIdx\":{chunk.subMeshIdx},\"chunkRecvTime\":\"{chunk.chunkRecvTime}\"}}";
        chunksThisFrame.Add(gEntry);

        int cursor = 0;
        int vertexCount = BitConverter.ToInt32(chunk_data, cursor); cursor += sizeof(int);
        for (int i = 0; i < vertexCount; i++)
        {
            int index = BitConverter.ToInt32(chunk_data, cursor); cursor += sizeof(int);

            // Skip decoding if already filled
            if (verticesArr[index] != Vector3.zero)
            {
                cursor += 6 * sizeof(float); // skip position + normal
                continue;
            }

            float x = BitConverter.ToSingle(chunk_data, cursor); cursor += sizeof(float);
            float y = BitConverter.ToSingle(chunk_data, cursor); cursor += sizeof(float);
            float z = BitConverter.ToSingle(chunk_data, cursor); cursor += sizeof(float);
            verticesArr[index] = new Vector3(x, y, z);

            float nx = BitConverter.ToSingle(chunk_data, cursor); cursor += sizeof(float);
            float ny = BitConverter.ToSingle(chunk_data, cursor); cursor += sizeof(float);
            float nz = BitConverter.ToSingle(chunk_data, cursor); cursor += sizeof(float);
            normalsArr[index] = new Vector3(nx, ny, nz);
        }


        int triangleCount = BitConverter.ToInt32(chunk_data, cursor); cursor += sizeof(int);
        for (int i = 0; i < triangleCount; i++)
        {
            int tri = BitConverter.ToInt32(chunk_data, cursor); cursor += sizeof(int);
            trianglesArr[chunk.subMeshIdx].Add(tri);
        }

        if (!recGameObjects.ContainsKey(objectID))
        {
            GameObject recGameObject = new GameObject();
            recGameObject.AddComponent<MeshFilter>();

            Mesh newMesh = new Mesh();
            newMesh.vertices = verticesArr;
            newMesh.normals = normalsArr;
            newMesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
            {
                newMesh.SetTriangles(trianglesArr[i], i);
            }

            // IMPORTANT: Set the recompute the mesh bounds to avoid camera culling issues
            newMesh.RecalculateBounds();

            recGameObject.GetComponent<MeshFilter>().mesh = newMesh;
            recGameObject.AddComponent<MeshRenderer>();

            //List<Material> materials = new List<Material>();
            //foreach (string matName in materialNames)
            //{
            //    materials.Add(m_ResourceLoader.LoadMaterialByName(matName));
            //}
            //recGameObject.GetComponent<MeshRenderer>().materials = materials.ToArray();

            List<Material> materials = new List<Material>();
            foreach (string matName in materialNames)
            {
                if (matName == "null")
                {
                    materials.Add(m_ResourceLoader.LoadMaterialByName(matName));
                    continue;
                }
                //Debug.Log($"{matName}, {matName == "null"}, {matName is null}");
                Material oldMaterial = m_ResourceLoader.LoadMaterialByName(matName);
                int mode = oldMaterial.GetInt("_Mode");
                Material newMaterial = new Material(Shader.Find("Custom/Pure_Color_Shadow"));
                if (mode == 1)
                {
                    newMaterial.SetFloat("_Cutoff", oldMaterial.GetFloat("_Cutoff"));
                }
                else
                {
                    newMaterial.SetFloat("_Cutoff", 0);
                }
                newMaterial.SetColor("_Color", oldMaterial.GetColor("_Color"));
                materials.Add(newMaterial);
            }
            recGameObject.GetComponent<MeshRenderer>().materials = materials.ToArray();

            Shader.SetGlobalTexture("_CustomShadowMap", shadowMap);
            Shader.SetGlobalMatrix("_LightViewProjection", lightViewProjMatrix);
            recGameObject.AddComponent<MeshCollider>();
            recGameObject.layer = 1;

            recGameObject.transform.position = position;
            recGameObject.transform.eulerAngles = eulerAngles;
            recGameObject.transform.localScale = scale;

            recGameObjects[objectID] = recGameObject;
        }
        else
        {
            GameObject go = recGameObjects[objectID];
            Mesh mesh = go.GetComponent<MeshFilter>().mesh;

            mesh.vertices = verticesArr;
            mesh.normals = normalsArr;

            for (int i = 0; i < subMeshCount; i++)
            {
                mesh.SetTriangles(trianglesArr[i], i);
            }

            mesh.RecalculateBounds(); // important for rendering
            holder.needUpdateCollider = true;
        }
    }

    private IEnumerator CheckRetransmissions()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f);

            for (int i = 0; i < m_TCPClient.objectHolders.Length; i++)
            {
                ObjectHolder objectHolder = m_TCPClient.objectHolders[i];

                if (objectHolder.ifVisible && (DateTime.UtcNow - objectHolder.latestChunkTime).TotalSeconds > 1f && 
                    objectHolder.chunks_VTSeparate.Count < objectHolder.totalVertChunkNum + objectHolder.totalTriChunkNum)
                {
                    List<int> missingChunks = new List<int>();
                    for (int j = 0; j < objectHolder.totalVertChunkNum + objectHolder.totalTriChunkNum; j++)
                    {
                        if (!objectHolder.chunks_VTSeparate.ContainsKey(j))
                        {
                            missingChunks.Add(j);
                        }
                    }

                    if (missingChunks.Count > 0)
                    {
                        RetransmissionRequest req = new RetransmissionRequest
                        {
                            objectID = objectHolder.objectID,
                            missingChunks = missingChunks.ToArray()
                        };
                        Debug.Log($"objectID {req.objectID}, chunks {req.missingChunks}");
                        string jsonReq = JsonUtility.ToJson(req);
                        byte[] reqData = Encoding.UTF8.GetBytes(jsonReq);

                        retransmissionClient.Send(reqData, reqData.Length, objectHolder.remoteEP.Address.ToString(), portTCP);
                        objectHolder.latestChunkTime = DateTime.UtcNow;
                    }
                }
            }
        }
    }

    void OnDestroy()
    {
        isShuttingDown = true;
        if (udpClient != null)
        {
            if (isMulticast)
            {
                udpClient.DropMulticastGroup(multicastAddress);
            }
            udpClient.Close();
        }
        if (logWriter != null)
        {
            logWriter.Flush();
            logWriter.Close();
        }
        retransmissionClient?.Close();
        retransmissionClient = null;
    }
}

//private void ReceiveMeshChunks(IAsyncResult ar)
//{
//    try
//    {
//        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, portUDP);
//        byte[] packet = udpClient.EndReceive(ar, ref remoteEP);

//        if (packet.Length < HEADER_SIZE)
//        {
//            Debug.LogWarning("Received packet too small to contain header.");
//            udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
//            return;
//        }

//        char submeshType = BitConverter.ToChar(packet, 0);
//        int objectId = -1, chunkId = -1, submeshId = -1, headerSize = -1;
//        int totalMeshTrunks = 10000;

//        if (submeshType == 'V')
//        {
//            objectId = BitConverter.ToInt32(packet, 2);
//            chunkId = BitConverter.ToInt32(packet, 6);
//            headerSize = 10;
//        }
//        else if (submeshType == 'T')
//        {
//            objectId = BitConverter.ToInt32(packet, 2);
//            chunkId = BitConverter.ToInt32(packet, 6);
//            submeshId = BitConverter.ToInt32(packet, 10);
//            headerSize = 14;
//        }
//        else
//        {
//            Debug.LogError("Unknown packet type.");
//            udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
//            return;
//        }

//        int dataSize = packet.Length - headerSize;
//        byte[] chunkData = new byte[dataSize];
//        Buffer.BlockCopy(packet, headerSize, chunkData, 0, dataSize);

//        if (!activeMeshTransmissions.ContainsKey(objectId))
//        {
//            recevTotalChunkPerObject = 0;
//            activeMeshTransmissions[objectId] = new MeshTransmission
//            {
//                totalMeshChunks = totalMeshTrunks,
//                firstChunkTime = DateTime.UtcNow,
//                remoteEP = remoteEP
//            };
//        }

//        MeshTransmission transmission = activeMeshTransmissions[objectId];

//        if (!transmission.chunks.ContainsKey(chunkId))
//        {
//            recevTotalChunkPerObject++;
//            recevTotalChunkN++;

//            transmission.chunks[chunkId] = new Chunk
//            {
//                id = chunkId,
//                type = submeshType,
//                objectID = objectId,
//                subMeshIdx = submeshId,
//                data = chunkData
//            };

//            UnityDispatcher.Instance.Enqueue(() =>
//            {
//                DecodeChunkVRSeparate(transmission.chunks[chunkId]);
//            });
//        }
//    }
//    catch (Exception ex)
//    {
//        Debug.LogError("ReceiveMeshChunks error: " + ex.Message);
//    }

//    udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
//}