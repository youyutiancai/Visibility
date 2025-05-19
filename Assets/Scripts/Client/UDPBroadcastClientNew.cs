using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TMPro;
using UnityEngine;


[Serializable]
public class RetransmissionRequest
{
    public int bundleId;
    public int[] missingChunks;
}

public class ObjectHolder
{
    public Vector3 position, eulerAngles, scale;
    public string prefabName;
    public string[] materialNames;
    public int totalVertChunkNum, totalTriChunkNum, totalVertNum, submeshCount;
    public bool ifVisible, ifOwned;
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


    public int portUDP = 5005;  // broadcast port
    public int portTCP = 5006;  // retransmit port
    private UdpClient udpClient;
    private IPEndPoint remoteEP;
    private IPEndPoint localEP;
    private string multicastAddr = "230.0.0.1";
    private IPAddress multicastAddress;  // multicast
    private const int HEADER_SIZE = 12; // [archived] 3 ints: bundleId, totalChunks, chunkIndex.
    private int bufferSize = 1024 * 1024;

    // an gameObject triangle mesh data
    //GameObject recGameObject;
    //int numVerticesPerChunk = 57; // constant for all the chunk
    //int totalVertexNum; // ObjectHolder -> totalVertNum
    //int subMeshCount;  // ObjectHolder -> submeshCount
    //Vector3 position, eulerAngles, scale;
    //string[] materialNames;  // per object
    //List<Material> materials;
    //List<List<int>> triangles;
    //Vector3[] vertices;
    //Vector3[] normals;

    // multiple scene objects data
    public int numVerticesPerChunk = 57; // constant 
    private Dictionary<int, GameObject> recGameObjects = new Dictionary<int, GameObject>();
    private Dictionary<int, Vector3[]> verticesDict = new Dictionary<int, Vector3[]>();
    private Dictionary<int, List<List<int>>> trianglesDict = new Dictionary<int, List<List<int>>>();
    private Dictionary<int, Vector3[]> normalsDict = new Dictionary<int, Vector3[]>();
    private int recevTotalChunkPerObject = 0;
    private int recevTotalChunkN = 0;


    // Represents an in-progress asset BUNDLE transmission.
    private class BundleTransmission
    {
        public int totalChunks;
        public Dictionary<int, byte[]> chunks = new Dictionary<int, byte[]>();
        public DateTime firstChunkTime;
        public IPEndPoint remoteEP;  // Save the sender's endpoint for retransmission requests.
    }

    // Active transmissions mapped by bundleId.
    private Dictionary<int, BundleTransmission> activeTransmissions = new Dictionary<int, BundleTransmission>();


    // Represent an Mesh transmission
    // Chunk format
    //char vorT = BitConverter.ToChar(chunk, cursor);
    //int objectID = BitConverter.ToInt32(chunk, cursor += sizeof(char));
    //int chunkID = BitConverter.ToInt32(chunk, cursor += sizeof(int));
    private class Chunk  // Noted: chunk id is set as the index in dictionary
    {
        public int id;
        public char type;
        public int objectID;
        public int subMeshIdx;  // only has when the chunk type is triangle
        public byte[] data;
    }
    private class MeshTransmission  // one object one mesh transmission, with multiple chunks
    {
        public int totalMeshChunks;
        public Dictionary<int, Chunk> chunks = new Dictionary<int, Chunk>();
        public DateTime firstChunkTime;
        public IPEndPoint remoteEP;
    }
    private Dictionary<int, MeshTransmission> activeMeshTransmissions = new Dictionary<int, MeshTransmission>();


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


    void Start()
    {

    }

    void Update()
    {
        if (isShowLog)
        {
            m_TextLog.text = $"Received Chunks Numbers - [{recevTotalChunkN}]";
        }
    }

    private void StartListenToServer()
    {
        try
        {
            if (isMulticast)
            {
                multicastAddress = IPAddress.Parse(multicastAddr);
                Debug.Log($"Parsed MCast addr = {multicastAddress}");

                udpClient = new UdpClient(AddressFamily.InterNetwork);
                udpClient.Client.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress, true);

                localEP = new IPEndPoint(IPAddress.Any, portUDP);
                udpClient.Client.Bind(localEP);
                Debug.Log($"Bound UDP client to {localEP}");

                udpClient.JoinMulticastGroup(multicastAddress);
                Debug.Log($"Joined multicast group {multicastAddress}");

                udpClient.Client.ReceiveBufferSize = bufferSize;
                Debug.Log($"Set ReceiveBufferSize = {bufferSize}");

                udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
            }
            else
            {
                udpClient = new UdpClient(portUDP);
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpClient.Client.ReceiveBufferSize = bufferSize;
                udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
            }

            int recvBuf = (int)udpClient.Client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer);
            int sendBuf = (int)udpClient.Client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer);

            Debug.Log($"[+++++++] UDP client listening on the server, recvBuf-{recvBuf}, sendBuf-{sendBuf}");
            m_TextLog2.text = $"[+++++++] UDP client listening on the server, recvBuf-{recvBuf}, sendBuf-{sendBuf}";

            // Start the coroutine to periodically check for retransmissions.
            //StartCoroutine(CheckRetransmissions());   //TODO
        }
        catch (Exception ex)
        {
            Debug.Log("UDP client initialization failed: " + ex.Message);
        }
    }


    private void ReceiveMeshChunks(IAsyncResult ar)
    {
        try
        {
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, portUDP);
            byte[] packet = udpClient.EndReceive(ar, ref remoteEP);

            if (packet.Length < HEADER_SIZE) // archived
            {
                Debug.LogWarning("Received packet too small to contain header.");
                udpClient.BeginReceive(new AsyncCallback(ReceiveMessage), null);
                return;
            }

            // Parse the header. (Object ID to determine whether continue the logic)
            char submeshType = BitConverter.ToChar(packet, 0);
            int objectId = -1, chunkId = -1, submeshId = -1, headerSize = -1;
            int totalMeshTrunks = 10000;  // placeholder

            if (submeshType == 'V')
            {
                //Debug.Log($"Received package type V...");
                objectId = BitConverter.ToInt32(packet, 2);
                chunkId = BitConverter.ToInt32(packet, 6);
                headerSize = 10;
            }
            else if (submeshType == 'T')
            {
                //Debug.Log($"Received package type T...");
                objectId = BitConverter.ToInt32(packet, 2);
                chunkId = BitConverter.ToInt32(packet, 6);
                submeshId = BitConverter.ToInt32(packet, 10);
                headerSize = 14;
            }
            else
            {
                Debug.LogError("Err: Please ensure the packet parser is correct");
            }


            int dataSize = packet.Length - headerSize;
            byte[] chunkData = new byte[dataSize];
            Array.Copy(packet, headerSize, chunkData, 0, dataSize);

            // Create or update the transmission record.
            if (!activeMeshTransmissions.ContainsKey(objectId))
            {
                recevTotalChunkPerObject = 0;  // refresh the chunk N
                
                activeMeshTransmissions[objectId] = new MeshTransmission
                {
                    totalMeshChunks = totalMeshTrunks,
                    firstChunkTime = DateTime.UtcNow,
                    remoteEP = remoteEP   // store the sender's endpoint
                };
                //Debug.Log($"Starting received Object {objectId}");
                //UnityDispatcher.Instance.Enqueue(() =>
                //{
                //    //UpdateUI(objectId, recevTotalChunkPerObject, true);
                //});
            }
            MeshTransmission transmission = activeMeshTransmissions[objectId];

            if (!transmission.chunks.ContainsKey(chunkId))
            {
                recevTotalChunkPerObject++;
                recevTotalChunkN++;

                
                transmission.chunks[chunkId] = new Chunk
                {
                    id = chunkId,
                    type = submeshType,
                    objectID = objectId,
                    subMeshIdx = submeshId,
                    data = chunkData
                };
                //Debug.Log($"Received chunk {chunkId + 1}/{totalMeshTrunks} of {transmission.chunks[chunkId].type} for objectId {objectId}");

                // update the triangles, vertices and normals list
                UnityDispatcher.Instance.Enqueue(() =>
                {
                    UpdateObjectSubMeshes(transmission.chunks[chunkId]);
                    //UpdateUI(objectId, recevTotalChunkPerObject, false);
                });
            }

            // If all chunks have been received, schedule reassembly and loading on the main thread.
            //if (transmission.chunks.Count == transmission.totalMeshChunks)
            //{
            //    UnityDispatcher.Instance.Enqueue(() =>
            //    {
            //        AssembleLoadObjectSubMeshes(objectId, transmission);
            //    });
            //    activeMeshTransmissions.Remove(objectId);
            //}
        }
        catch (Exception ex)
        {
            Debug.LogError("ReceiveMessage error: " + ex.Message);
        }

        // Continue listening for packets.
        udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
    }

    private void ReceiveMessage(IAsyncResult ar)
    {
        try
        {
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, portUDP);
            byte[] packet = udpClient.EndReceive(ar, ref remoteEP);

            if (packet.Length < HEADER_SIZE)
            {
                Debug.LogWarning("Received packet too small to contain header.");
                udpClient.BeginReceive(new AsyncCallback(ReceiveMessage), null);
                return;
            }

            // Parse the header.
            int bundleId = BitConverter.ToInt32(packet, 0);
            int totalChunks = BitConverter.ToInt32(packet, 4);
            int chunkIndex = BitConverter.ToInt32(packet, 8);

            int dataSize = packet.Length - HEADER_SIZE;
            byte[] chunkData = new byte[dataSize];
            Array.Copy(packet, HEADER_SIZE, chunkData, 0, dataSize);

            // Create or update the transmission record.
            if (!activeTransmissions.ContainsKey(bundleId))
            {
                activeTransmissions[bundleId] = new BundleTransmission
                {
                    totalChunks = totalChunks,
                    firstChunkTime = DateTime.UtcNow,
                    remoteEP = remoteEP   // store the sender's endpoint
                };
                //Debug.Log($"Started receiving bundleId {bundleId} with {totalChunks} chunks from {remoteEP.Address}");
            }
            BundleTransmission transmission = activeTransmissions[bundleId];

            if (!transmission.chunks.ContainsKey(chunkIndex))
            {
                transmission.chunks[chunkIndex] = chunkData;
                //Debug.Log($"Received chunk {chunkIndex + 1}/{totalChunks} for bundleId {bundleId}");
            }

            // If all chunks have been received, schedule reassembly and loading on the main thread.
            if (transmission.chunks.Count == transmission.totalChunks)
            {
                UnityDispatcher.Instance.Enqueue(() =>
                {
                    AssembleAndLoadBundle(bundleId, transmission);
                });
                activeTransmissions.Remove(bundleId);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("ReceiveMessage error: " + ex.Message);
        }

        // Continue listening for packets.
        udpClient.BeginReceive(new AsyncCallback(ReceiveMessage), null);
    }

    // Coroutine to periodically check for missing chunks and request retransmissions.
    private IEnumerator CheckRetransmissions()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f);

            // Iterate over a copy of the keys so that modifications to activeTransmissions don't throw exceptions.
            foreach (var kvp in new Dictionary<int, BundleTransmission>(activeTransmissions))
            {
                int bundleId = kvp.Key;
                BundleTransmission transmission = kvp.Value;

                // Only process incomplete transmissions.
                if (transmission.chunks.Count < transmission.totalChunks)
                {
                    TimeSpan elapsed = DateTime.UtcNow - transmission.firstChunkTime;
                    if (elapsed.TotalSeconds > 2f)
                    {
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
                                bundleId = bundleId,
                                missingChunks = missingChunks.ToArray()
                            };
                            string jsonReq = JsonUtility.ToJson(req);
                            byte[] reqData = Encoding.UTF8.GetBytes(jsonReq);

                            UdpClient requestClient = new UdpClient();
                            // Use the stored remoteEP address for the retransmission request.
                            requestClient.Send(reqData, reqData.Length, transmission.remoteEP.Address.ToString(), portTCP);
                            requestClient.Close();
                            //Debug.Log("Requested retransmission for bundleId " + bundleId + " for missing chunks: " + string.Join(",", missingChunks));

                            // Reset the timer for retransmission requests.
                            transmission.firstChunkTime = DateTime.UtcNow;
                        }
                    }
                }
            }
        }
    }

    private void UpdateUI(int objectId, int meshReceivedN, bool isAppending)
    {
        //int trunksPerObject = m_TCPClient.objectHolders[objectId].totalTriChunkNum + m_TCPClient.objectHolders[objectId].totalVertChunkNum;

        if (isAppending)
        {
            // new object log added.

            logContext = m_TextLog.text;

            m_TextLog.text += $"Starting Receiving Object-[{objectId}] with total chunks-[xxx]\n";
        }
        else
        {
            // update the chunks received for the current object
            m_TextLog.text = logContext + $"Object-[{objectId}]: {meshReceivedN} / xxx \n";
        }
    }

    private void UpdateObjectSubMeshes(Chunk chunk)
    {

        int chunkID = chunk.id;
        char vorT = chunk.type;
        int objectID = chunk.objectID;
        byte[] chunk_data = chunk.data;

        Debug.Log($"[+++++++]: chunkid:{chunkID} - type:{vorT} - objectid:{objectID} - chunk_data_size:{chunk.data.Length}");


        // get the object info from the table
        int totalVertexNum = m_TCPClient.objectHolders[objectID].totalVertNum;
        int subMeshCount = m_TCPClient.objectHolders[objectID].submeshCount;
        string[] materialNames = m_TCPClient.objectHolders[objectID].materialNames;
        Vector3 position = m_TCPClient.objectHolders[objectID].position;
        Vector3 eulerAngles = m_TCPClient.objectHolders[objectID].eulerAngles;
        Vector3 scale = m_TCPClient.objectHolders[objectID].scale;

        //Debug.Log($"[+++++++] {m_TCPClient}");
        //Debug.Log($"[+++++++] {m_TCPClient.objectHolders}");
        //Debug.Log($"[+++++++] object holder len: {m_TCPClient.objectHolders.Length}");
        //Debug.Log($"[+++++++] total vertext num: {totalVertexNum}");

        //Debug.Log($"gameObjectID: {objectID}, vertexNum: {totalVertexNum}, SubMeshCount: {subMeshCount}, MatNum: {materialNames.Length}");

        if (!recGameObjects.ContainsKey(objectID))
        {
            //int numVerticesPerChunk = 57; // constant for all the chunk
            //int totalVertexNum; // ObjectHolder -> totalVertNum
            //int subMeshCount;  // ObjectHolder -> submeshCount
            //Vector3 position, eulerAngles, scale;

            // init the mesh arrays
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

        if (vorT == 'V')
        {
            int numVerticesInChunk = chunk_data.Length / (sizeof(float) * 6);  // 6: pos and normal
            float[] floatArray = new float[numVerticesInChunk * 6];
            Buffer.BlockCopy(chunk_data, 0, floatArray, 0, floatArray.Length * sizeof(float));
            for (int j = 0; j < numVerticesInChunk; j++)
            {
                verticesDict[objectID][chunkID * numVerticesPerChunk + j] = new Vector3(floatArray[j * 6], floatArray[j * 6 + 1], floatArray[j * 6 + 2]);
                normalsDict[objectID][chunkID * numVerticesPerChunk + j] = new Vector3(floatArray[j * 6 + 3], floatArray[j * 6 + 4], floatArray[j * 6 + 5]);
            }
        }
        else if (vorT == 'T')
        {
            int subMeshIdx = chunk.subMeshIdx;
            int numVerticesInChunk = (chunk_data.Length) / sizeof(int);
            int[] intArray = new int[numVerticesInChunk];
            Buffer.BlockCopy(chunk_data, 0, intArray, 0, intArray.Length * sizeof(int));
            trianglesDict[objectID][subMeshIdx].AddRange(intArray);
        }

        if (!recGameObjects.ContainsKey(objectID))
        {
            GameObject recGameObject = new GameObject();
            recGameObject.AddComponent<MeshFilter>();

            Mesh newMesh = new Mesh();
            newMesh.vertices = verticesDict[objectID];
            newMesh.normals = normalsDict[objectID];
            newMesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
            {
                newMesh.SetTriangles(trianglesDict[objectID][i].ToArray(), i);
            }
            recGameObject.GetComponent<MeshFilter>().mesh = newMesh;
            recGameObject.AddComponent<MeshRenderer>();

            // set up the material list
            List<Material> materials = new List<Material>();
            foreach (string matName in materialNames)
            {
                //Debug.Log(matName);
                Material mat = m_ResourceLoader.LoadMaterialByName(matName);
                materials.Add(mat);
            }
            recGameObject.GetComponent<MeshRenderer>().materials = materials.ToArray();

            // set up the pos, rot, scale
            recGameObject.transform.position = position;
            recGameObject.transform.eulerAngles = eulerAngles;
            recGameObject.transform.localScale = scale;

            recGameObjects[objectID] = recGameObject;
        }
        else
        {
            recGameObjects[objectID].GetComponent<MeshFilter>().mesh.vertices = verticesDict[objectID];
            recGameObjects[objectID].GetComponent<MeshFilter>().mesh.normals = normalsDict[objectID];
            for (int i = 0; i < subMeshCount; i++)
            {
                recGameObjects[objectID].GetComponent<MeshFilter>().mesh.SetTriangles(trianglesDict[objectID][i].ToArray(), i);
            }
        }
    }

    private void AssembleLoadObjectSubMeshes(int objectId, MeshTransmission transmission)
    {
        Debug.Log($"All chunks received for objectId {objectId}. Initializing the object...");

        List<List<int>> triangles = new List<List<int>>();
        int subMeshCount = 12;  // TODO: will change later 
        for (int i = 0; i < subMeshCount; i++)
        {
            triangles.Add(new List<int>());
        }

        int numVerticesPerChunk = 57; //TODO: might change later
        int totalVertexNum = 20706; // TODO: will change later
        Vector3[] vertices = new Vector3[totalVertexNum];
        Vector3[] normals = new Vector3[totalVertexNum];

        for (int i = 0; i < transmission.totalMeshChunks; i++)
        {
            Chunk chunk = transmission.chunks[i];
            byte[] chunk_data = chunk.data;
            char vorT = chunk.type;
            int objectID = chunk.objectID;
            int chunkID = i;

            if (vorT == 'V')
            {
                int numVerticesInChunk = chunk_data.Length / (sizeof(float) * 6);  // 6: pos and rot
                float[] floatArray = new float[numVerticesInChunk * 6];
                Buffer.BlockCopy(chunk_data, 0, floatArray, 0, floatArray.Length * sizeof(float));
                for (int j = 0; j < numVerticesInChunk; j++)
                {
                    vertices[chunkID * numVerticesPerChunk + j] = new Vector3(floatArray[j * 6], floatArray[j * 6 + 1], floatArray[j * 6 + 2]);
                    normals[chunkID * numVerticesPerChunk + j] = new Vector3(floatArray[j * 6 + 3], floatArray[j * 6 + 4], floatArray[j * 6 + 5]);
                }
            }
            else if (vorT == 'T')
            {
                int subMeshIdx = chunk.subMeshIdx;
                int numVerticesInChunk = (chunk_data.Length) / sizeof(int);
                int[] intArray = new int[numVerticesInChunk];
                Buffer.BlockCopy(chunk_data, 0, intArray, 0, intArray.Length * sizeof(int));
                triangles[subMeshIdx].AddRange(intArray);
            }
        }

        GameObject newObject = new GameObject();
        newObject.AddComponent<MeshFilter>();

        Mesh newMesh = new Mesh();
        newMesh.vertices = vertices;
        newMesh.normals = normals;
        newMesh.subMeshCount = subMeshCount;
        for (int i = 0; i < subMeshCount; i++)
        {
            newMesh.SetTriangles(triangles[i].ToArray(), i);
        }
        newObject.GetComponent<MeshFilter>().mesh = newMesh;
        newObject.AddComponent<MeshRenderer>();
        newObject.GetComponent<MeshRenderer>().materials = VisibilityCheck.Instance.testObject.GetComponent<MeshRenderer>().materials;

    }

    private void AssembleAndLoadBundle(int bundleId, BundleTransmission transmission)
    {
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

        AssetBundle bundle = AssetBundle.LoadFromMemory(assetBundleData);
        if (bundle == null)
        {
            Debug.LogError("Failed to load AssetBundle.");
            return;
        }
        Debug.Log("AssetBundle loaded successfully.");

        // Attempt to load and instantiate the prefab. Adjust the asset name as necessary.
        GameObject prefab = bundle.LoadAsset<GameObject>("balcony_1");
        if (prefab != null)
        {
            Instantiate(prefab);
        }
        else
        {
            Debug.LogError("Prefab 'balcony_1' not found in AssetBundle.");
        }
    }

    void OnDestroy()
    {
        if (udpClient != null)
        {
            if (isMulticast)
            {
                udpClient.DropMulticastGroup(multicastAddress);
                udpClient.Close();
            }
            else
            {
                udpClient.Close();
            }
        }
    }

}
