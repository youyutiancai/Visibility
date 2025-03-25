using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TMPro;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
// Removed "using Android.Net.Wifi;" since it’s not available.
using UnityEngine.Android;
#endif

[Serializable]
public class RetransmissionRequest
{
    public int bundleId;
    public int[] missingChunks;
}

public class UDPBroadcastClientNew : MonoBehaviour
{
    public GameObject m_TestObject;
    public TextMeshProUGUI debugText;
    [SerializeField] private TCPClient m_TCPClient;
    [SerializeField] private ResourceLoader m_ResourceLoader;

    public int port1 = 5005;  // broadcast port
    public int port2 = 5006;  // retransmit port
    private UdpClient udpClient;
    private const int HEADER_SIZE = 12; // archived: 3 ints

    public int numVerticesPerChunk = 57; // constant 

    private Dictionary<int, GameObject> recGameObjects = new Dictionary<int, GameObject>();
    private Dictionary<int, Vector3[]> verticesDict = new Dictionary<int, Vector3[]>();
    private Dictionary<int, List<List<int>>> trianglesDict = new Dictionary<int, List<List<int>>>();
    private Dictionary<int, Vector3[]> normalsDict = new Dictionary<int, Vector3[]>();
    private Dictionary<int, string[]> matNamesDict = new Dictionary<int, string[]>();

    // Represents an in-progress asset BUNDLE transmission.
    private class BundleTransmission
    {
        public int totalChunks;
        public Dictionary<int, byte[]> chunks = new Dictionary<int, byte[]>();
        public DateTime firstChunkTime;
        public IPEndPoint remoteEP;
    }
    private Dictionary<int, BundleTransmission> activeTransmissions = new Dictionary<int, BundleTransmission>();

    // Represent a Mesh transmission.
    private class Chunk
    {
        public int id;
        public char type;
        public int objectID;
        public int subMeshIdx;
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

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject multicastLock;
#endif

    //private void OnEnable()
    //{
    //    debugText.text += "Enabled\n";
    //    m_TCPClient.OnReceivedServerTable += OnRecevTable;
    //}

    //private void OnDisable()
    //{
    //    m_TCPClient.OnReceivedServerTable -= OnRecevTable;
    //}

    private void OnRecevTable()
    {
        
    }

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AcquireMulticastLock();
#endif
        StartListenToBroadcast();
    }

    private void StartListenToBroadcast()
    {
        debugText.text += "StartListenToBroadcast\n";
        try
        {
            udpClient = new UdpClient(port1);
            udpClient.EnableBroadcast = true;
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
            Debug.Log($"UDP client listening on port {port1}");
        }
        catch (Exception ex)
        {
            debugText.text += $"UDP client initialization failed: {ex.Message}\n";
            Debug.LogError("UDP client initialization failed: " + ex.Message);
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void AcquireMulticastLock()
    {
        // Get the current activity.
        AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

        // Get the WiFi service.
        AndroidJavaObject wifiManager = currentActivity.Call<AndroidJavaObject>("getSystemService", "wifi");
        // Create and acquire the multicast lock.
        multicastLock = wifiManager.Call<AndroidJavaObject>("createMulticastLock", "UDPBroadcastLock");
        multicastLock.Call("setReferenceCounted", true);
        multicastLock.Call("acquire");
        Debug.Log("MulticastLock acquired");
    }
#endif

    private void ReceiveMeshChunks(IAsyncResult ar)
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

            char submeshType = BitConverter.ToChar(packet, 0);
            int objectId = -1, chunkId = -1, submeshId = -1, headerSize = -1;
            int totalMeshTrunks = 364; // TODO: update with actual value.

            if (submeshType == 'V')
            {
                objectId = BitConverter.ToInt32(packet, 2);
                chunkId = BitConverter.ToInt32(packet, 6);
                headerSize = 10;
            }
            else if (submeshType == 'T')
            {
                objectId = BitConverter.ToInt32(packet, 2);
                chunkId = BitConverter.ToInt32(packet, 6);
                submeshId = BitConverter.ToInt32(packet, 10);
                headerSize = 14;
            }
            else
            {
                Debug.LogError("Error: Packet parser is incorrect.");
            }

            int dataSize = packet.Length - headerSize;
            byte[] chunkData = new byte[dataSize];
            Array.Copy(packet, headerSize, chunkData, 0, dataSize);

            if (!activeMeshTransmissions.ContainsKey(objectId))
            {
                activeMeshTransmissions[objectId] = new MeshTransmission
                {
                    totalMeshChunks = totalMeshTrunks,
                    firstChunkTime = DateTime.UtcNow,
                    remoteEP = remoteEP
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
                //Debug.Log($"Received chunk {chunkId + 1}/{totalMeshTrunks} of {transmission.chunks[chunkId].type} for objectId {objectId}");

                UnityDispatcher.Instance.Enqueue(() =>
                {
                    UpdateObjectSubMeshes(transmission.chunks[chunkId]);
                });
            }
        }
        catch (Exception ex)
        {
            debugText.text += $"{ex}\n";
            Debug.LogError("ReceiveMeshChunks error: " + ex.Message);
        }

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

            int bundleId = BitConverter.ToInt32(packet, 0);
            int totalChunks = BitConverter.ToInt32(packet, 4);
            int chunkIndex = BitConverter.ToInt32(packet, 8);

            int dataSize = packet.Length - HEADER_SIZE;
            byte[] chunkData = new byte[dataSize];
            Array.Copy(packet, HEADER_SIZE, chunkData, 0, dataSize);

            if (!activeTransmissions.ContainsKey(bundleId))
            {
                activeTransmissions[bundleId] = new BundleTransmission
                {
                    totalChunks = totalChunks,
                    firstChunkTime = DateTime.UtcNow,
                    remoteEP = remoteEP
                };
                //Debug.Log($"Started receiving bundleId {bundleId} with {totalChunks} chunks from {remoteEP.Address}");
            }
            BundleTransmission transmission = activeTransmissions[bundleId];

            if (!transmission.chunks.ContainsKey(chunkIndex))
            {
                transmission.chunks[chunkIndex] = chunkData;
                //Debug.Log($"Received chunk {chunkIndex + 1}/{totalChunks} for bundleId {bundleId}");
            }

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
            debugText.text += $"{ex}\n";
            Debug.LogError("ReceiveMessage error: " + ex.Message);
        }

        udpClient.BeginReceive(new AsyncCallback(ReceiveMessage), null);
    }

    private IEnumerator CheckRetransmissions()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f);
            foreach (var kvp in new Dictionary<int, BundleTransmission>(activeTransmissions))
            {
                int bundleId = kvp.Key;
                BundleTransmission transmission = kvp.Value;
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
                            requestClient.Send(reqData, reqData.Length, transmission.remoteEP.Address.ToString(), port2);
                            requestClient.Close();
                            //Debug.Log("Requested retransmission for bundleId " + bundleId + " for missing chunks: " + string.Join(",", missingChunks));
                            transmission.firstChunkTime = DateTime.UtcNow;
                        }
                    }
                }
            }
        }
    }

    private void UpdateObjectSubMeshes(Chunk chunk)
    {
        if (recGameObjects == null)
        {
            recGameObjects = new Dictionary<int, GameObject>();
        }
        int chunkID = chunk.id;
        char vorT = chunk.type;
        int objectID = chunk.objectID;
        byte[] chunk_data = chunk.data;
        int totalVertexNum = m_TCPClient.objectHolders[objectID].totalVertNum;
        int subMeshCount = m_TCPClient.objectHolders[objectID].submeshCount;
        string[] materialNames = m_TCPClient.objectHolders[objectID].materialNames;
        Vector3 position = m_TCPClient.objectHolders[objectID].position;
        Vector3 eulerAngles = m_TCPClient.objectHolders[objectID].eulerAngles;
        Vector3 scale = m_TCPClient.objectHolders[objectID].scale;
        
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

        if (vorT == 'V')
        {
            int numVerticesInChunk = chunk_data.Length / (sizeof(float) * 6);
            float[] floatArray = new float[numVerticesInChunk * 6];
            Buffer.BlockCopy(chunk_data, 0, floatArray, 0, floatArray.Length * sizeof(float));
            for (int j = 0; j < numVerticesInChunk; j++)
            {
                verticesDict[objectID][chunkID * numVerticesPerChunk + j] = new Vector3(
                    floatArray[j * 6], floatArray[j * 6 + 1], floatArray[j * 6 + 2]);
                normalsDict[objectID][chunkID * numVerticesPerChunk + j] = new Vector3(
                    floatArray[j * 6 + 3], floatArray[j * 6 + 4], floatArray[j * 6 + 5]);
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

            List<Material> materials = new List<Material>();
            foreach (string matName in materialNames)
            {
                Material mat = m_ResourceLoader.LoadMaterialByName(matName);
                materials.Add(mat);
            }
            recGameObject.GetComponent<MeshRenderer>().materials = materials.ToArray();

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
        int subMeshCount = 12;
        for (int i = 0; i < subMeshCount; i++)
        {
            triangles.Add(new List<int>());
        }

        int numVerticesPerChunk = 57;
        int totalVertexNum = 20706;
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
                int numVerticesInChunk = chunk_data.Length / (sizeof(float) * 6);
                float[] floatArray = new float[numVerticesInChunk * 6];
                Buffer.BlockCopy(chunk_data, 0, floatArray, 0, floatArray.Length * sizeof(float));
                for (int j = 0; j < numVerticesInChunk; j++)
                {
                    vertices[chunkID * numVerticesPerChunk + j] = new Vector3(
                        floatArray[j * 6], floatArray[j * 6 + 1], floatArray[j * 6 + 2]);
                    normals[chunkID * numVerticesPerChunk + j] = new Vector3(
                        floatArray[j * 6 + 3], floatArray[j * 6 + 4], floatArray[j * 6 + 5]);
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
#if UNITY_ANDROID && !UNITY_EDITOR
        if (multicastLock != null)
        {
            multicastLock.Call("release");
            Debug.Log("MulticastLock released");
        }
#endif
    }
}
