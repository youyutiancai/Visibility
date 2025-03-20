using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;



[Serializable]
public class RetransmissionRequest
{
    public int bundleId;
    public int[] missingChunks;
}

public class UDPBroadcastClientNew : MonoBehaviour
{
    // TODO: will change later
    public GameObject m_TestObject;

    [SerializeField] private TCPClient m_TCPClient;
    [SerializeField] private ResourceLoader m_ResourceLoader;


    public int port1 = 5005;  // broadcast port
    public int port2 = 5006;  // retransmit port
    private UdpClient udpClient;
    private const int HEADER_SIZE = 12; // [archived] 3 ints: bundleId, totalChunks, chunkIndex.

    // an gameObject triangle mesh data
    GameObject recGameObject;
    int numVerticesPerChunk = 57; // constant for all the chunk
    int totalVertexNum; // ObjectHolder -> totalVertNum
    int subMeshCount;  // ObjectHolder -> submeshCount
    Vector3 position, eulerAngles, scale;
    string[] materialNames;  // per object
    List<Material> materials;
    List<List<int>> triangles;
    Vector3[] vertices;
    Vector3[] normals;


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
    private class MeshTransmission
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

        StartListenToBroadcast();
    }


    void Start()
    {
        // init the mesh list
        triangles = new List<List<int>>();
        materials = new List<Material>();
    }

    private void StartListenToBroadcast()
    {
        try
        {
            udpClient = new UdpClient(port1);
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);

            Debug.Log($"UDP client listening on port {port1}");

            // Start the coroutine to periodically check for retransmissions.
            //StartCoroutine(CheckRetransmissions());   //TODO
        }
        catch (Exception ex)
        {
            Debug.LogError("UDP client initialization failed: " + ex.Message);
        }
    }

    private void ReceiveMeshChunks(IAsyncResult ar)
    {
        try
        {
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, port1);
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
            int totalMeshTrunks = 364; // TODO: get from the initial user object tables by tcp

            if (submeshType == 'V')
            {
                Debug.Log($"Received package type V...");
                objectId = BitConverter.ToInt32(packet, 2);
                chunkId = BitConverter.ToInt32(packet, 6);
                headerSize = 10;
            }
            else if (submeshType == 'T')
            {
                Debug.Log($"Received package type T...");
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
                activeMeshTransmissions[objectId] = new MeshTransmission
                {
                    totalMeshChunks = totalMeshTrunks,  // TODO: need to use the table to record
                    firstChunkTime = DateTime.UtcNow,
                    remoteEP = remoteEP   // store the sender's endpoint
                };
                Debug.Log($"Started receiving objectId {objectId} with {totalMeshTrunks} chunks from {remoteEP.Address}");
            }
            MeshTransmission transmission = activeMeshTransmissions[objectId];

            if (!transmission.chunks.ContainsKey(chunkId))
            {
                transmission.chunks[chunkId] = new Chunk
                {
                    id = chunkId,
                    type = submeshType,
                    objectID = objectId,
                    subMeshIdx = submeshId,
                    data = chunkData
                };
                Debug.Log($"Received chunk {chunkId + 1}/{totalMeshTrunks} of {transmission.chunks[chunkId].type} for objectId {objectId}");

                // update the triangles, vertices and normals list
                UnityDispatcher.Instance.Enqueue(() =>
                {
                    UpdateObjectSubMeshes(transmission.chunks[chunkId]);
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
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, port1);
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
                Debug.Log($"Started receiving bundleId {bundleId} with {totalChunks} chunks from {remoteEP.Address}");
            }
            BundleTransmission transmission = activeTransmissions[bundleId];

            if (!transmission.chunks.ContainsKey(chunkIndex))
            {
                transmission.chunks[chunkIndex] = chunkData;
                Debug.Log($"Received chunk {chunkIndex + 1}/{totalChunks} for bundleId {bundleId}");
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
                            requestClient.Send(reqData, reqData.Length, transmission.remoteEP.Address.ToString(), port2);
                            requestClient.Close();
                            Debug.Log("Requested retransmission for bundleId " + bundleId + " for missing chunks: " + string.Join(",", missingChunks));

                            // Reset the timer for retransmission requests.
                            transmission.firstChunkTime = DateTime.UtcNow;
                        }
                    }
                }
            }
        }
    }

    private void UpdateObjectSubMeshes(Chunk chunk)
    {
        int chunkID = chunk.id;
        char vorT = chunk.type;
        int objectID = chunk.objectID;
        byte[] chunk_data = chunk.data;

        if (vertices == null || vertices.Length == 0)
        {
            // get the table information
            totalVertexNum = m_TCPClient.objectHolders[objectID].totalVertNum;
            subMeshCount = m_TCPClient.objectHolders[objectID].submeshCount;
            materialNames = m_TCPClient.objectHolders[objectID].materialNames;
            position = m_TCPClient.objectHolders[objectID].position;
            eulerAngles = m_TCPClient.objectHolders[objectID].eulerAngles;
            scale = m_TCPClient.objectHolders[objectID].scale;
            
            Debug.Log($"vertexNum: {totalVertexNum}, SubMeshCount: {subMeshCount}, MatNum: {materialNames.Length}");

            // init the mesh arrays
            vertices = new Vector3[totalVertexNum];
            normals = new Vector3[totalVertexNum];

            for (int i = 0; i < subMeshCount; i++)
            {
                triangles.Add(new List<int>());
            }
        }

        if (vorT == 'V')
        {
            int numVerticesInChunk = chunk_data.Length / (sizeof(float) * 6);  // 6: pos and normal
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

        if (recGameObject == null)
        {
            recGameObject = new GameObject();
            recGameObject.AddComponent<MeshFilter>();

            Mesh newMesh = new Mesh();
            newMesh.vertices = vertices;
            newMesh.normals = normals;
            newMesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
            {
                newMesh.SetTriangles(triangles[i].ToArray(), i);
            }
            recGameObject.GetComponent<MeshFilter>().mesh = newMesh;
            recGameObject.AddComponent<MeshRenderer>();
            
            // set up the material list
            foreach (string matName in materialNames)
            {
                Debug.Log(matName);
                Material mat = m_ResourceLoader.LoadMaterialByName(matName);
                materials.Add(mat);
            }
            recGameObject.GetComponent<MeshRenderer>().materials = materials.ToArray();

            // set up the pos, rot, scale
            recGameObject.transform.position = position;
            recGameObject.transform.eulerAngles = eulerAngles;
            recGameObject.transform.localScale = scale;
        }
        else
        {
            recGameObject.GetComponent<MeshFilter>().mesh.vertices = vertices;
            recGameObject.GetComponent<MeshFilter>().mesh.normals = normals;
            for (int i = 0; i < subMeshCount; i++)
            {
                recGameObject.GetComponent<MeshFilter>().mesh.SetTriangles(triangles[i].ToArray(), i);
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
            udpClient.Close();
        }
    }
}

