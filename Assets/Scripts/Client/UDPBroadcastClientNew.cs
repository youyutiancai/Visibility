using System;
using System.Collections;
using System.Collections.Generic;
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

    public int portUDP = 5005;
    public int portTCP = 5006;
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

    private class BundleTransmission
    {
        public int totalChunks;
        public Dictionary<int, byte[]> chunks = new Dictionary<int, byte[]>();
        public DateTime firstChunkTime;
        public IPEndPoint remoteEP;
    }

    private Dictionary<int, BundleTransmission> activeTransmissions = new Dictionary<int, BundleTransmission>();

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
                udpClient = new UdpClient(AddressFamily.InterNetwork);
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                localEP = new IPEndPoint(IPAddress.Any, portUDP);
                udpClient.Client.Bind(localEP);
                udpClient.JoinMulticastGroup(multicastAddress);
            }
            else
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

            if (packet.Length < HEADER_SIZE)
            {
                Debug.LogWarning("Received packet too small to contain header.");
                udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
                return;
            }

            char submeshType = BitConverter.ToChar(packet, 0);
            int objectId = -1, chunkId = -1, submeshId = -1, headerSize = -1;
            int totalMeshTrunks = 10000;

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
                Debug.LogError("Unknown packet type.");
                udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
                return;
            }

            int dataSize = packet.Length - headerSize;
            byte[] chunkData = new byte[dataSize];
            Buffer.BlockCopy(packet, headerSize, chunkData, 0, dataSize);

            if (!activeMeshTransmissions.ContainsKey(objectId))
            {
                recevTotalChunkPerObject = 0;
                activeMeshTransmissions[objectId] = new MeshTransmission
                {
                    totalMeshChunks = totalMeshTrunks,
                    firstChunkTime = DateTime.UtcNow,
                    remoteEP = remoteEP
                };
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

                UnityDispatcher.Instance.Enqueue(() =>
                {
                    UpdateObjectSubMeshes(transmission.chunks[chunkId]);
                });
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("ReceiveMeshChunks error: " + ex.Message);
        }

        udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
    }

    private void UpdateObjectSubMeshes(Chunk chunk)
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
        }
        else if (vorT == 'T')
        {
            int count = chunk_data.Length / sizeof(int);
            Buffer.BlockCopy(chunk_data, 0, reusableIntBuffer, 0, chunk_data.Length);
            trianglesArr[chunk.subMeshIdx].AddRange(new ArraySegment<int>(reusableIntBuffer, 0, count));
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
        }
    }

    void OnDestroy()
    {
        if (udpClient != null)
        {
            if (isMulticast)
            {
                udpClient.DropMulticastGroup(multicastAddress);
            }
            udpClient.Close();
        }
    }
}
