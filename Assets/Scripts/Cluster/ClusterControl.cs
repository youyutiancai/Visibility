using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityTCPClient.Assets.Scripts;
using System.IO;
using System;
using Random = UnityEngine.Random;
using UnityEngine.InputSystem;
using System.Net.Sockets;
using System.Collections;

public enum MeshDecodeMethod
{
    VTSeparate,
    VTGrouped
}

public enum TestPhase
{
    InitialPhase, StandPhase, MovingPhase, QuestionPhase, WaitPhase, EndPhase
}

public class ClusterControl : Singleton<ClusterControl>
{
    private const int CHUNK_SIZE = 1400;

    public GameObject randomMovingUserPrefab, followUserPrefab, realUserPrefab, clusterPrefab, initialClusterCenter, footprintCalculateStartPos,
        footprintCalculateEndPos;  // clusterPrefab is used to represent a cluster visually
    //public SyntheticPathNode[] clusterInitPoses;
    //public SyntheticPathNode[] paths;
    public TextMeshProUGUI userStatusInfo;

    public float epsilon = 10;  // Radius for clustering
    public int numChunkRepeat;
    public int minPts = 2;      // Minimum points to form a cluster
    [SerializeField]
    public int pathNum;
    public float updateInterval, newObjectInterval, newChunkInterval, chunkCoolDownInterval, timegapForSwapUsers;  // How often to update (in seconds)
    public bool regularlySwapUsers, regularlySwapLeader, writeToData;

    //[HideInInspector]
    public List<User> users = new List<User>();
    [HideInInspector]
    public List<List<SyntheticPathNode>> allPathNodes;
    [HideInInspector]
    public Vector3 initialClusterCenterPos;
    [HideInInspector]
    //public List<int> objectsWaitToBeSent;
    private MeshVariant mv, mv1;
    [HideInInspector]
    public PriorityQueue<int, long, float, int> objectsWaitToBeSent;
    [HideInInspector]
    public PriorityQueue<byte[], float, long, (int, int)> chunksCoolingDown;
    [HideInInspector]
    public PriorityQueue<byte[], long, float, (int, int)> chunksToSend;
    public Dictionary<int, List<byte[]>> objectChunksVTSeparate, objectChunksVTGrouped;
    public int chunksSentEachTime;
    private float timeSinceLastUpdate = 0f, timeSinceLastChunksent = 0f;
    public SimulationStrategyDropDown SimulationStrategy;
    private SimulationStrategy ss;
    private Dictionary<int, Color> clusterColors = new Dictionary<int, Color>();  // Cluster ID to color mapping
    private Dictionary<int, GameObject> clusterGameObjects = new Dictionary<int, GameObject>(); // Cluster ID to GameObject mapping
    private VisibilityCheck vc;
    private TCPControl tc;
    private int[] visibleObjectsInRegion;
    private long[] objectFootPrintsInRegion, newObjectCount;
    private GridDivide gd;
    private int objectSentIndi, userIDToSend;
    private GameObject pathNodesRoot;
    private NetworkControl nc;
    private bool canSendObjects;
    public MeshDecodeMethod meshDecodeMethod;
    public bool onlySendVisibleChunks, useChunkFootAsPriority, ifHaveChunkCoolingDown;
    private Dictionary<int, long[]> newChunksToSend = new Dictionary<int, long[]>(), chunkFootprintInfo;

    private StreamWriter writer;
    private string filePath;

    void Start()
    {
        InitialValues();
        CreateClusters();
        if (writeToData)
        {
            InitializeIndividualUserDataWriter();
        }
    }

    private void InitialValues()
    {
        vc = VisibilityCheck.Instance;
        tc = TCPControl.Instance;
        nc = NetworkControl.Instance;
        gd = GridDivide.Instance;
        visibleObjectsInRegion = new int[vc.objectsInScene.Count];
        objectFootPrintsInRegion = new long[vc.objectsInScene.Count];
        objectSentIndi = 0;
        userIDToSend = 0;
        pathNum = 0;
        pathNodesRoot = GameObject.Find("PathNodes");
        initialClusterCenterPos = initialClusterCenter.transform.position;
        objectsWaitToBeSent = new PriorityQueue<int, long, float, int>();
        chunksToSend = new PriorityQueue<byte[], long, float, (int, int)>();
        chunksCoolingDown = new PriorityQueue<byte[], float, long, (int, int)>();
        chunksCoolingDown.descending = false;
        chunksCoolingDown.ifHaveChunkCoolingDown = true;
        canSendObjects = false;
        mv = new RandomizedMesh();
        mv1 = new GroupedMesh();
        //List<byte[]> chunks_mv = mv.RequestChunks(objectToSerialize, CHUNK_SIZE);
        //List<byte[]> chunks_mv1 = mv1.RequestChunks(objectToSerialize, CHUNK_SIZE);
        
        objectChunksVTSeparate = new Dictionary<int, List<byte[]>>();
        objectChunksVTGrouped = new Dictionary<int, List<byte[]>>();
        //objectChunksVTGroupedTest = new Dictionary<int, List<byte[]>>();
        if (vc.step == CityPreprocessSteps.NoPreProcessStep || vc.step == CityPreprocessSteps.CalculateFootprintChunk)
        {
            //StartCoroutine(SaveAll()); // Save all chunks to files
            LoadAllChunks("Assets/Data/objectChunksGrouped", ref objectChunksVTGrouped);
            //LoadAllChunks("Assets/Data/objectChunksGrouped", ref objectChunksVTGroupedTest);
            //StartCoroutine(LoadAll());
            //LoadAllChunks("Assets/Data/ObjectChunks", ref objectChunksVTSeparate);
        }
        //Dictionary<int, long[]> result = new Dictionary<int, long[]>();
        //vc.ReadFootprintByChunkInRegion(initialClusterCenterPos + new Vector3(0, 0, -1), epsilon, ref result);
        //int count = 0;
        //foreach (int objectID in result.Keys)
        //{
        //    long[] footprints = result[objectID];
        //    for (int i = 0; i < footprints.Length; i++)
        //    {
        //        if (footprints[i] > 0)
        //        {
        //            count++;
        //        }
        //    }
        //}
        //Debug.Log($"Total number of chunks in region: {count}");
        //int objectToSerialize = 0;
        //ReconstructFromChunks(vc.objectsInScene[objectToSerialize], objectChunksVTGrouped[objectToSerialize]);
    }

    //private IEnumerator LoadAll()
    //{
    //    for (int i = 0; i < vc.objectsInScene.Count; i++)
    //    {
    //        List<Byte[]> chunks = mv1.RequestChunks(i, CHUNK_SIZE);
    //        objectChunksVTGroupedTest[i] = chunks;
    //        Debug.Log($"{i}: old chunk num: {objectChunksVTGrouped[i].Count}, new chunk num: {chunks.Count}");
    //        yield return null;
    //    }
    //}

    private IEnumerator SaveAll()
    {
        string targetPath = "Assets/Data/ObjectChunksGroupedCorrect";

        for (int i = 0; i < vc.objectsInScene.Count; i++)
        {
            string objectFilePath = Path.Combine(targetPath, $"object_{i}.bin");
            List<Byte[]> chunks = mv1.RequestChunks(i, CHUNK_SIZE);
            SaveChunksToFile(objectFilePath, chunks);
            Debug.Log($"Saved {chunks.Count} chunks for object {i} to {objectFilePath}");
            yield return null;
        }
    }
    private void SaveChunksToFile(string objectFilePath, List<byte[]> chunks)
    {
        try
        {
            using (BinaryWriter writer = new BinaryWriter(File.Open(objectFilePath, FileMode.Create)))
            {
                // First write number of chunks
                writer.Write(chunks.Count);

                // Then write each chunk
                foreach (byte[] chunk in chunks)
                {
                    writer.Write(chunk.Length); // chunk size
                    writer.Write(chunk);        // chunk data
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error writing chunks to {objectFilePath}: {e.Message}");
        }
    }

    private void LoadAllChunks(string chunksDirectory, ref Dictionary<int, List<byte[]>> chunkDic)
    {
        //string chunksDirectory = "Assets/Data/ObjectChunks";
        //objectChunksVTSeparate = new Dictionary<int, List<byte[]>>();
        chunkDic = new Dictionary<int, List<byte[]>>();

        if (!Directory.Exists(chunksDirectory))
        {
            Debug.LogError($"Chunks directory not found at: {chunksDirectory}");
            return;
        }

        try
        {
            // Get all files in the chunks directory
            string[] files = Directory.GetFiles(chunksDirectory, "object_*.bin");

            foreach (string file in files)
            {
                // Parse filename to get objectID
                string fileName = Path.GetFileNameWithoutExtension(file);
                string[] parts = fileName.Split('_');

                if (parts.Length >= 2 && int.TryParse(parts[1], out int objectID))
                {
                    // Load chunks for this object
                    List<byte[]> chunks = LoadChunksFromFile(file);
                    if (chunks != null && chunks.Count > 0)
                    {
                        chunkDic[objectID] = chunks;
                    }
                }
            }

            Debug.Log($"Successfully loaded {chunkDic.Count} objects");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading chunks: {e.Message}");
        }
    }

    private List<byte[]> LoadChunksFromFile(string objectFilePath)
    {
        List<byte[]> chunks = new List<byte[]>();

        try
        {
            using (BinaryReader reader = new BinaryReader(File.Open(objectFilePath, FileMode.Open)))
            {
                int chunkCount = reader.ReadInt32(); // First read number of chunks

                for (int i = 0; i < chunkCount; i++)
                {
                    int chunkSize = reader.ReadInt32();          // Read chunk size
                    byte[] chunk = reader.ReadBytes(chunkSize);  // Read chunk data
                    chunks.Add(chunk);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error reading chunks from {objectFilePath}: {e.Message}");
            return null;
        }

        return chunks;
    }

    public static void ReconstructFromChunks(GameObject original, List<byte[]> chunks)
    {
        Mesh originalMesh = original.GetComponent<MeshFilter>()?.sharedMesh;
        if (originalMesh == null)
        {
            Debug.LogError("Original object has no mesh.");
        }

        int vertexCapacity = originalMesh.vertexCount;
        int submeshCount = originalMesh.subMeshCount;

        Vector3[] vertices = new Vector3[vertexCapacity];
        Vector3[] normals = new Vector3[vertexCapacity];
        List<int>[] submeshTriangles = new List<int>[submeshCount];
        for (int i = 0; i < submeshCount; i++)
            submeshTriangles[i] = new List<int>();

        //foreach (var chunk in chunks)
        for (int k = 0; k < chunks.Count / 2; k++)
        {
            byte[] chunk = chunks[k];
            int offset = 0;
            char type = BitConverter.ToChar(chunk, offset); offset += sizeof(char);
            int objectID = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);
            int chunkID = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);
            int submeshID = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);

            int vertexCount = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);
            for (int i = 0; i < vertexCount; i++)
            {
                int index = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);

                // Skip decoding if already filled
                if (vertices[index] != Vector3.zero)
                {
                    offset += 6 * sizeof(float); // skip position + normal
                    continue;
                }

                float x = BitConverter.ToSingle(chunk, offset); offset += sizeof(float);
                float y = BitConverter.ToSingle(chunk, offset); offset += sizeof(float);
                float z = BitConverter.ToSingle(chunk, offset); offset += sizeof(float);
                vertices[index] = new Vector3(x, y, z);

                float nx = BitConverter.ToSingle(chunk, offset); offset += sizeof(float);
                float ny = BitConverter.ToSingle(chunk, offset); offset += sizeof(float);
                float nz = BitConverter.ToSingle(chunk, offset); offset += sizeof(float);
                normals[index] = new Vector3(nx, ny, nz);
            }


            int triangleCount = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);
            for (int i = 0; i < triangleCount; i++)
            {
                int tri = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);
                submeshTriangles[submeshID].Add(tri);
            }

            // Rebuild mesh
            Mesh newMesh = new Mesh();
            newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            newMesh.vertices = vertices;
            newMesh.normals = normals;
            newMesh.subMeshCount = submeshCount;

            for (int i = 0; i < submeshCount; i++)
                newMesh.SetTriangles(submeshTriangles[i], i);

            newMesh.RecalculateBounds();

            // Create new object
            GameObject copy = Instantiate(original);
            copy.name = $"{original.name}_Reconstructed_{k}";
            copy.transform.position = original.transform.position;
            copy.transform.rotation = original.transform.rotation;
            copy.transform.localScale = original.transform.localScale;

            MeshFilter filter = copy.GetComponent<MeshFilter>();
            if (filter == null) filter = copy.AddComponent<MeshFilter>();
            filter.sharedMesh = newMesh;

            MeshRenderer renderer = copy.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = copy.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = original.GetComponent<MeshRenderer>().sharedMaterials;
        }
    }



    private void InitializeIndividualUserDataWriter()
    {
        filePath = $"C:\\Users\\zhou1168\\VRAR\\Visibility\\Assets\\GeneratedData\\IndividualUserUpdateData\\{0}.csv";
        writer = new StreamWriter(filePath, false);
        //if (new FileInfo(filePath).Length == 0)
        //{
        writer.WriteLine("Timestamp,UserID,ObjectID,UserX,UserY,UserZ,ObjectX,ObjectY,ObjectZ,Object_dis,Object_size");
        //}
        Debug.Log("CSV file created or opened at: " + filePath);
    }

    private void CreateClusters()
    {
        allPathNodes = new List<List<SyntheticPathNode>>();
        for (int i = 0; i < pathNodesRoot.transform.childCount; i++)
        {
            Transform path = pathNodesRoot.transform.GetChild(i);
            allPathNodes.Add(new List<SyntheticPathNode>() {});
            for (int j = 0; j < path.childCount; j++)
            {
                allPathNodes[i].Add(path.GetChild(j).GetComponent<SyntheticPathNode>());
            }

        }

        switch (SimulationStrategy)
        {
            case SimulationStrategyDropDown.FollowStrategy:
                ss = new FollowStrategy();
                ss.CreateUsers(5, followUserPrefab);
                break;

            case SimulationStrategyDropDown.IndiUserRandomSpawn:
                ss = new IndiUserRandomSpawn();
                ss.CreateUsers(100, randomMovingUserPrefab);
                break;

            case SimulationStrategyDropDown.RealUserCluster:
                ss = new RealUserClusterStrategy();
                break;

            case SimulationStrategyDropDown.RealUserIndi:
                ss = new RealUserIndiStrategy();
                break;
        }    
    }

    void Update()
    {
        timeSinceLastUpdate += Time.deltaTime;
        nc.timeSinceLastChunkRequest += Time.deltaTime;
        chunksToSend.ifHaveChunkCoolingDown = ifHaveChunkCoolingDown;

        if ((SimulationStrategy == SimulationStrategyDropDown.RealUserCluster || SimulationStrategy == SimulationStrategyDropDown.RealUserIndi) && Keyboard.current.bKey.wasPressedThisFrame)
        {
            if (canSendObjects)
            {
                canSendObjects = false;
            } else
            {
                bool allUsersStandby = true;
                foreach (RealUser user in tc.addressToUser.Values)
                {
                    if (user.testPhase != TestPhase.StandPhase)
                    {
                        allUsersStandby = false; break;
                    }
                }
                if (allUsersStandby)
                {
                    canSendObjects = true;
                }
            }
            Debug.Log($"canSendObject changed to {canSendObjects}");
        }

        if (Keyboard.current.rKey.wasPressedThisFrame && tc.addressToUser.Count != 0)
        {
            bool canResetAll = true;
            foreach (RealUser user in tc.addressToUser.Values)
            {
                if (user.testPhase != TestPhase.WaitPhase)
                {
                    canResetAll = false; break;
                }
            }
            if (canResetAll)
            {
                pathNum++;
                canSendObjects = false;
                nc.sendingMode = pathNum % 2 == 0 ? SendingMode.UNICAST_TCP : SendingMode.MULTICAST;
                foreach (RealUser user in tc.addressToUser.Values)
                {
                    user.InformResetAll();
                }
            }
        }

        UpdateUserStatus();

        if (users.Count == 0)
            return;

        switch (ss)
        {
            case IndiUserRandomSpawn:
                if (timeSinceLastUpdate >= updateInterval)
                {
                    timeSinceLastUpdate = 0f;
                    RunDBSCAN(); // Re-run DBSCAN clustering
                    ApplyClusterColors(); // Apply colors based on clustering
                    UpdateClusterParents(); // Update parents and scale clusters
                    SendObjectsToClusters();
                    SendObjectsToUsers();
                }
                break;


            case RealUserClusterStrategy:
                if (timeSinceLastUpdate >= updateInterval)
                {
                    timeSinceLastUpdate = 0f;
                    RunDBSCAN();
                    ApplyClusterColors();
                    UpdateClusterParents();
                    if (meshDecodeMethod == MeshDecodeMethod.VTGrouped && onlySendVisibleChunks)
                    {
                        SendObjectsToClustersByChunk();
                        foreach (int objectID in newChunksToSend.Keys)
                        {
                            long[] footprints = newChunksToSend[objectID];
                            for (int i = 0; i < footprints.Length; i++)
                            {
                                byte[] newChunk = objectChunksVTGrouped[objectID][i];
                                if (footprints[i] > 0 && !chunksToSend.Contains((objectID, i)))
                                {
                                    chunksToSend.Enqueue(newChunk, (objectID, i), footprints[i], numChunkRepeat, 0);
                                }
                            }
                        }
                        Debug.Log($"chunks left to send: {chunksToSend.Count}");
                    } else
                    {
                        SendObjectsToClusters();

                        //foreach (var user in users)
                        //{
                        //    user.UpdateVisibleObjectsIndi(newObjectsToSend, ref objectSentIndi, null);
                        //}

                        for (int i = 0; i < newObjectCount.Length; i++)
                        {
                            if (newObjectCount[i] > 0 && !objectsWaitToBeSent.Contains(i))
                            {
                                objectsWaitToBeSent.Enqueue(i, i, newObjectCount[i], 1, 0);
                            }
                        }
                    }
                }
                SimulateAndSendPuppetPoses();
                break;

            case RealUserIndiStrategy:
                if (timeSinceLastUpdate >= updateInterval)
                {
                    timeSinceLastUpdate = 0f;
                    // everyone votes for the order of objects
                    //SendObjectsToIndisByChunk();
                    //CountObjectFootprintIndi();
                    //foreach (int objectID in newChunksToSend.Keys)
                    //{
                    //    long[] footprints = newChunksToSend[objectID];
                    //    for (int i = 0; i < footprints.Length; i++)
                    //    {
                    //        byte[] newChunk = objectChunksVTGrouped[objectID][i];
                    //        if (footprints[i] > 0 && !chunksToSend.Contains((objectID, i)))
                    //        {
                    //            if (useChunkFootAsPriority)
                    //            {
                    //                chunksToSend.Enqueue(newChunk, (objectID, i), footprints[i], numChunkRepeat, 0);
                    //            }
                    //            else
                    //            {
                    //                chunksToSend.Enqueue(newChunk, (objectID, i), newObjectCount[objectID], numChunkRepeat, 0);
                    //            }
                    //        }
                    //    }
                    //}

                    // send one object for users in turn
                    for (int i = 0; i < transform.childCount; i++)
                    {
                        RealUser user = transform.GetChild(i).GetComponent<RealUser>();
                        if (user.testPhase != TestPhase.StandPhase && user.testPhase != TestPhase.MovingPhase)
                            continue;
                        Vector3 userPosition = user.transform.position;
                        int xStartIndex = Mathf.FloorToInt((userPosition.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
                        int zStartIndex = Mathf.FloorToInt((userPosition.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
                        if (xStartIndex == user.preX && zStartIndex == user.preZ) { continue; }
                        user.preX = xStartIndex; user.preZ = zStartIndex;
                        chunkFootprintInfo = new Dictionary<int, long[]>();
                        vc.ReadFootprintByChunkInRegion(userPosition, epsilon, ref chunkFootprintInfo);
                        newObjectCount = new long[vc.objectsInScene.Count];
                        if (!useChunkFootAsPriority)
                        {
                            vc.GetFootprintsInRegion(userPosition, epsilon, ref newObjectCount);
                        }
                        user.CleanChunksWaitToSend();
                        user.UpdateChunkToSend(chunkFootprintInfo, newObjectCount, useChunkFootAsPriority);

                        Debug.Log($"RealUserIndiStrategy chunks left to send: {user.ChunksWaitToSend.Count}");
                    }
                    
                }
                //SimulateAndSendPuppetPoses();
                break;
        }

        if (nc.timeSinceLastChunkRequest >= newObjectInterval && nc.readyForNextObject && objectsWaitToBeSent.Count > 0)
        {
            long priority = objectsWaitToBeSent.GetPriority(objectsWaitToBeSent.Peek());
            int sendingObjectIdx = objectsWaitToBeSent.Dequeue();
            List<byte[]> chunks = null;
            switch (meshDecodeMethod)
            {
                case MeshDecodeMethod.VTSeparate:
                    if (!objectChunksVTSeparate.ContainsKey(sendingObjectIdx))
                    {
                        objectChunksVTSeparate.Add(sendingObjectIdx, mv.RequestChunks(sendingObjectIdx, CHUNK_SIZE));
                    }
                    chunks = objectChunksVTSeparate[sendingObjectIdx];
                    break;

                case MeshDecodeMethod.VTGrouped:
                    if (!objectChunksVTGrouped.ContainsKey(sendingObjectIdx))
                    {
                        objectChunksVTGrouped.Add(sendingObjectIdx, mv1.RequestChunks(sendingObjectIdx, CHUNK_SIZE));
                    }
                    chunks = objectChunksVTGrouped[sendingObjectIdx];

                    break;
            }
            for (int i = 0; i < chunks.Count; i++)
            {
                if (!chunksToSend.Contains((sendingObjectIdx, i)))
                {
                    chunksToSend.Enqueue(chunks[i], (sendingObjectIdx, i), priority, numChunkRepeat, 0);
                }
            }
            nc.timeSinceLastChunkRequest = 0;
        }

        timeSinceLastChunksent += Time.deltaTime;
        if (canSendObjects && timeSinceLastChunksent >= newChunkInterval) //  && chunksToSend.Count > 0
        {
            //int maxToSend = Mathf.Max(0, Mathf.Min(chunksToSend.Count, chunksSentEachTime));
            //float now = Time.time;
            //for (int i = 0; i < maxToSend; i++)
            //{
            // (int, int) id = chunksToSend.Peek();
            //    if (ifHaveChunkCoolingDown)
            //    {
            //        (int, int) id = chunksToSend.Peek();
            //        byte[] chunk = chunksToSend.GetElement(id);
            //        int countLeft = chunksToSend.GetCount(id);
            //        long priority = chunksToSend.GetPriority(id);
            //        if (countLeft > 1)
            //        {
            //            chunksCoolingDown.Enqueue(chunk, id, now + chunkCoolDownInterval, countLeft - 1, priority);
            //        }
            //    }
            //    Debug.Log($"{Time.time}: {id.Item1}, {id.Item2}");
            //    nc.BroadcastChunk(chunksToSend.Dequeue());
            //}
            //if (ifHaveChunkCoolingDown)
            //{
            //    while (chunksCoolingDown.Count > 0 && chunksCoolingDown.GetPriority(chunksCoolingDown.Peek()) < now)
            //    {
            //        (int, int) id = chunksCoolingDown.Peek();
            //        byte[] chunk = chunksCoolingDown.GetElement(id);
            //        int countLeft = chunksCoolingDown.GetCount(id);
            //        long priority = chunksCoolingDown.GetAdditionalElement(id);
            //        chunksToSend.Enqueue(chunk, id, priority, countLeft, 0);
            //        chunksCoolingDown.Dequeue();
            //    }
            //}
            int count = 0;
            RealUser[] allUsers = new RealUser[transform.childCount];
            int totalChunksWaitToSend = 0;
            for (int i = 0; i < transform.childCount; i++)
            {
                allUsers[i] = transform.GetChild(i).GetComponent<RealUser>();
                if (allUsers[i].ChunksWaitToSend is not null)
                {
                    totalChunksWaitToSend += allUsers[i].ChunksWaitToSend.Count;
                }
            }
            if (totalChunksWaitToSend > 0)
            {
                chunksSentEachTime = nc.sendingMode == SendingMode.UNICAST_TCP ? 50 : 30;
                while (count < chunksSentEachTime)
                {
                    userIDToSend = userIDToSend % allUsers.Length;
                    if (allUsers[userIDToSend].ChunksWaitToSend.Count == 0)
                    {
                        userIDToSend = (userIDToSend + 1) % allUsers.Length;
                        continue;
                    }
                    (int, int) id = allUsers[userIDToSend].ChunksWaitToSend.Peek();
                    if (nc.sendingMode == SendingMode.UNICAST_TCP)
                    {
                        allUsers[userIDToSend].MarkAsSentMaxCount(id.Item1, id.Item2, objectChunksVTGrouped[id.Item1].Count);
                        //nc.SendChunkTCP(allUsers[userIDToSend], objectChunksVTGrouped[id.Item1][id.Item2]);
                        nc.SendChunkTCP(allUsers[userIDToSend], objectChunksVTGrouped[id.Item1][id.Item2]);
                    }
                    else
                    {
                        for (int i = 0; i < allUsers.Length; i++)
                        {
                            allUsers[i].MarkAsSent(id.Item1, id.Item2, objectChunksVTGrouped[id.Item1].Count, 1);
                        }
                        nc.BroadcastChunk(objectChunksVTGrouped[id.Item1][id.Item2]);
                        //nc.SendChunkTCP(allUsers[userIDToSend], objectChunksVTGrouped[id.Item1][id.Item2]);
                    }
                    int nextUserID = userIDToSend;
                    do
                    {
                        nextUserID = (nextUserID + 1) % allUsers.Length;
                        if (nextUserID == userIDToSend)
                            break;
                    }
                    while (allUsers[nextUserID].ChunksWaitToSend.Count == 0);
                    if (nextUserID == userIDToSend && allUsers[nextUserID].ChunksWaitToSend.Count == 0)
                        break;
                    count++;
                    userIDToSend = nextUserID;
                }
            }
            timeSinceLastChunksent = 0;
        }
        //if (regularlySwapUsers || regularlySwapLeader)
        //{
        //    ss.UpdateRegularly();
        //}
    }

    private void UpdateUserStatus()
    {
        userStatusInfo.text = "";
        for (int i = 0; i < tc.headsetIDs.Length; i++)
        {
            if (tc.addressToUser.TryGetValue(tc.headsetIDs[i], out RealUser user)) {
                userStatusInfo.text += $"User {i} ({tc.headsetIDs[i]}): {user.testPhase}\n";
            }
            else
            {
                userStatusInfo.text += $"User {i} ({tc.headsetIDs[i]}): Not connected\n";
            } 
        }
    }

    private void SimulateAndSendPuppetPoses()
    {
        foreach (var user in users)
        {
            if (user is RealUser realUser && realUser.isPuppet && realUser.tcpClient?.Connected == true)
            {
                float t = Time.time;
                float speed = 30f;
                float angle = t * speed;

                realUser.simulatedPosition = initialClusterCenterPos;
                realUser.simulatedRotation = Quaternion.Euler(new Vector3(0, angle, 0));
                //Debug.Log($"{realUser.simulatedPosition}, {realUser.simulatedRotation.eulerAngles}");
                
                try
                {
                    List<byte> buffer = new List<byte>();
                    buffer.AddRange(BitConverter.GetBytes(0));
                    buffer.AddRange(BitConverter.GetBytes((int)TCPMessageType.POSE_FROM_SERVER));
                    buffer.AddRange(BitConverter.GetBytes(realUser.simulatedPosition.x));
                    buffer.AddRange(BitConverter.GetBytes(realUser.simulatedPosition.y));
                    buffer.AddRange(BitConverter.GetBytes(realUser.simulatedPosition.z));
                    buffer.AddRange(BitConverter.GetBytes(realUser.simulatedRotation.x));
                    buffer.AddRange(BitConverter.GetBytes(realUser.simulatedRotation.y));
                    buffer.AddRange(BitConverter.GetBytes(realUser.simulatedRotation.z));
                    buffer.AddRange(BitConverter.GetBytes(realUser.simulatedRotation.w));
                    byte[] message = buffer.ToArray();
                    Buffer.BlockCopy(BitConverter.GetBytes(buffer.Count - sizeof(int)), 0, message, 0, sizeof(int));
                    NetworkStream stream = realUser.tcpClient.GetStream();
                    stream.Write(message, 0, message.Length);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to send puppet pose to {realUser.tcpEndPoint}: {e.Message}");
                }
            }
        }
    }


    void RunDBSCAN()
    {
        // Reset cluster IDs and clear existing cluster colors and clusters before starting
        foreach (var user in users)
        {
            user.ClusterId = -1;
            user.transform.parent = transform;  // Set default parent to ClusterControl
        }
        clusterColors.Clear();

        // Destroy all previous cluster GameObjects
        foreach (var clusterObj in clusterGameObjects.Values)
        {
            Destroy(clusterObj);
        }
        clusterGameObjects.Clear();

        int clusterId = 0;

        foreach (var point in users)
        {
            if (point.ClusterId != -1) continue;  // Skip if already visited

            var neighbors = GetNeighbors(point);

            if (neighbors.Count < minPts)
            {
                // Treat individual users as their own, but don't create a separate cluster GameObject
                clusterId++;
                point.ClusterId = clusterId;
                clusterColors[clusterId] = GetRandomColor();  // Assign a random color to the single-user cluster
            }
            else
            {
                clusterId++;
                clusterColors[clusterId] = GetRandomColor();  // Assign a random color to the new cluster

                // Instantiate clusterPrefab for multi-user clusters
                GameObject newClusterObj = Instantiate(clusterPrefab, transform);
                newClusterObj.name = "Cluster_" + clusterId;  // Name the cluster for clarity
                newClusterObj.tag = "Cluster";
                clusterGameObjects[clusterId] = newClusterObj;

                ExpandCluster(point, neighbors, clusterId);
            }
        }
    }

    void ExpandCluster(User point, List<User> neighbors, int clusterId)
    {
        // Start the cluster with the initial point
        List<User> clusterPoints = new List<User> { point };
        point.ClusterId = clusterId;

        int index = 0;
        while (index < neighbors.Count)
        {
            var neighbor = neighbors[index];
            index++;

            // Skip if already visited or assigned to another cluster
            if (neighbor.ClusterId != -1 && neighbor.ClusterId != 0)
                continue;

            // Check if adding this neighbor maintains the 2 * epsilon distance constraint
            bool canAddToCluster = true;
            foreach (var existingPoint in clusterPoints)
            {
                if (Vector3.Distance(existingPoint.transform.position, neighbor.transform.position) > epsilon)
                {
                    canAddToCluster = false;
                    break;
                }
            }

            if (canAddToCluster)
            {
                // Assign cluster ID only after passing the distance check
                neighbor.ClusterId = clusterId;
                clusterPoints.Add(neighbor);  // Add to the cluster

                // Get neighbors of this valid neighbor
                var neighborNeighbors = GetNeighbors(neighbor);

                // If the neighbor has enough nearby points, add them to the list to be checked
                if (neighborNeighbors.Count >= minPts)
                {
                    foreach (var newNeighbor in neighborNeighbors)
                    {
                        if (newNeighbor.ClusterId == -1 || newNeighbor.ClusterId == 0)
                        {
                            neighbors.Add(newNeighbor);  // Add to neighbors list for future checks
                        }
                    }
                }
            }
        }
    }

    List<User> GetNeighbors(User point)
    {
        List<User> neighbors = new List<User>();

        foreach (var p in users)
        {
            if (Distance(point, p) <= epsilon/2)
            {
                neighbors.Add(p);
            }
        }

        return neighbors;
    }

    double Distance(User p1, User p2)
    {
        return Vector3.Distance(p1.transform.position, p2.transform.position);
    }

    void ApplyClusterColors()
    {
        foreach (var user in users)
        {
            int clusterId = user.ClusterId;
            if (clusterColors.ContainsKey(clusterId))
            {
                // Set the user's color based on the cluster color
                Renderer renderer = user.GetComponent<Renderer>();

                if (renderer != null)
                {
                    renderer.material.color = clusterColors[clusterId];
                }
            }
        }
    }

    void UpdateClusterParents()
    {
        // Calculate and update each cluster's position and scale
        foreach (var clusterId in clusterGameObjects.Keys)
        {
            GameObject clusterObj = clusterGameObjects[clusterId];
            if (clusterObj == null) continue;

            // Initialize variables for finding the two farthest points
            Vector3 pointA = Vector3.zero;
            Vector3 pointB = Vector3.zero;
            float maxDistance = 0f;

            // Find the two farthest points within the cluster
            foreach (var user1 in users)
            {
                if (user1.ClusterId != clusterId) continue;

                foreach (var user2 in users)
                {
                    if (user2.ClusterId != clusterId || user1 == user2) continue;

                    float distance = Vector3.Distance(user1.transform.position, user2.transform.position);
                    if (distance > maxDistance)
                    {
                        maxDistance = distance;
                        pointA = user1.transform.position;
                        pointB = user2.transform.position;
                    }
                }
            }

            if (maxDistance > 0f)
            {
                // Set the cluster center to the midpoint between the two farthest points
                Vector3 clusterCenter = (pointA + pointB) / 2;

                // Set the final radius based on maxDistance, ensuring it is at least epsilon
                float finalRadius = Mathf.Max(maxDistance / 2, epsilon);

                // Set the position and scale of the cluster to ensure it circles all users
                clusterObj.transform.position = clusterCenter;
                Debug.Log(clusterObj.transform.position);
                clusterObj.transform.localScale = new Vector3(finalRadius * 2 * 0.007f, 0.1f, finalRadius * 2 * 0.007f);

                // Set the color of the cluster to match the cluster color
                Renderer renderer = clusterObj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = clusterColors[clusterId];
                }
            }
        }

        // Update each user's parent based on their cluster ID
        foreach (var user in users)
        {
            int clusterId = user.ClusterId;

            if (clusterGameObjects.ContainsKey(clusterId) && clusterGameObjects[clusterId] != null)
            {
                // If part of a multi-user cluster, set parent to the cluster GameObject
                user.transform.parent = clusterGameObjects[clusterId].transform;
            }
            else
            {
                // Otherwise, set parent to ClusterControl (for individual users or unclustered users)
                user.transform.parent = transform;
            }
        }
    }

    Color GetRandomColor()
    {
        return new Color(Random.value, Random.value, Random.value);
    }

    private void SendObjectsToClusters()
    {
        newObjectCount = new long[vc.objectsInScene.Count];
        for (int i = 0; i < transform.childCount; i++) {
            if (transform.GetChild(i).tag == "Cluster")
            {
                Transform child = transform.GetChild(i);
                objectFootPrintsInRegion = new long[vc.objectsInScene.Count];
                //vc.GetVisibleObjectsInRegion(child.position, epsilon, ref visibleObjectsInRegion);
                vc.GetFootprintsInRegion(initialClusterCenterPos, epsilon, ref objectFootPrintsInRegion);
                for (int j = 0; j < child.childCount; j++)
                {
                    child.GetChild(j).GetComponent<User>().UpdateVisibleObjects(objectFootPrintsInRegion, ref newObjectCount);
                }
            }

            if (transform.GetChild(i).tag == "User")
            {
                User user = transform.GetChild(i).GetComponent<User>();
                Vector3 position = user.transform.position;
                int xStartIndex = Mathf.FloorToInt((position.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
                int zStartIndex = Mathf.FloorToInt((position.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
                if (xStartIndex == user.preX && zStartIndex == user.preZ) { continue; }
                user.preX = xStartIndex;
                user.preZ = zStartIndex;
                objectFootPrintsInRegion = new long[vc.objectsInScene.Count];
                vc.GetFootprintsInRegion(initialClusterCenterPos, epsilon, ref objectFootPrintsInRegion);
                user.UpdateVisibleObjects(objectFootPrintsInRegion, ref newObjectCount);
            }
        }
    }

    private void SendObjectsToIndi()
    {
        newObjectCount = new long[vc.objectsInScene.Count];
        for (int i = 0; i < transform.childCount; i++)
        {
            User user = transform.GetChild(i).GetComponent<User>();
            Vector3 position = user.transform.position;
            int xStartIndex = Mathf.FloorToInt((position.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
            int zStartIndex = Mathf.FloorToInt((position.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
            if (xStartIndex == user.preX && zStartIndex == user.preZ) { continue; }
            user.preX = xStartIndex;
            user.preZ = zStartIndex;
            objectFootPrintsInRegion = new long[vc.objectsInScene.Count];
            vc.GetFootprintsInRegion(initialClusterCenterPos, epsilon, ref objectFootPrintsInRegion);
            user.UpdateVisibleObjects(objectFootPrintsInRegion, ref newObjectCount);
        }
    }

    private void CountObjectFootprintIndi()
    {
        newObjectCount = new long[vc.objectsInScene.Count];
        for (int i = 0; i < transform.childCount; i++)
        {
            User user = transform.GetChild(i).GetComponent<User>();
            Vector3 position = user.transform.position;
            vc.GetFootprintsInRegion(position, epsilon, ref newObjectCount);
        }
    }

    private void SendObjectsToUsers()
    {
        for (int i = 0; i < users.Count; i++) {
            UpdateVisIndi(users[i], writer);
        }
    }

    private void SendObjectsToClustersByChunk()
    {
        newChunksToSend = new Dictionary<int, long[]>();
        for (int i = 0; i < transform.childCount; i++)
        {
            if (transform.GetChild(i).tag == "Cluster")
            {
                Transform child = transform.GetChild(i);
                Dictionary<int, long[]> chunkFootprintInfo = new Dictionary<int, long[]>();
                vc.ReadFootprintByChunkInRegion(initialClusterCenterPos, epsilon, ref chunkFootprintInfo);
                for (int j = 0; j < child.childCount; j++)
                {
                    child.GetChild(j).GetComponent<User>().UpdateVisibleChunks(chunkFootprintInfo, ref newChunksToSend);
                }
            }

            if (transform.GetChild(i).tag == "User")
            {
                User user = transform.GetChild(i).GetComponent<User>();
                Vector3 userPosition = user.transform.position;
                int xStartIndex = Mathf.FloorToInt((userPosition.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
                int zStartIndex = Mathf.FloorToInt((userPosition.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
                if (xStartIndex == user.preX && zStartIndex == user.preZ) { continue; }
                user.preX = xStartIndex;
                user.preZ = zStartIndex;
                chunkFootprintInfo = new Dictionary<int, long[]>();
                vc.ReadFootprintByChunkInRegion(initialClusterCenterPos, epsilon, ref chunkFootprintInfo);
                user.UpdateVisibleChunks(chunkFootprintInfo, ref newChunksToSend);
            }
        }
    }

    private void SendObjectsToIndisByChunk()
    {
        newChunksToSend = new Dictionary<int, long[]>();
        for (int i = 0; i < transform.childCount; i++)
        {
            User user = transform.GetChild(i).GetComponent<User>();
            Vector3 userPosition = user.transform.position;
            int xStartIndex = Mathf.FloorToInt((userPosition.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
            int zStartIndex = Mathf.FloorToInt((userPosition.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
            if (xStartIndex == user.preX && zStartIndex == user.preZ) { continue; }
            user.preX = xStartIndex;
            user.preZ = zStartIndex;
            chunkFootprintInfo = new Dictionary<int, long[]>();
            vc.ReadFootprintByChunkInRegion(userPosition, epsilon, ref chunkFootprintInfo);
            user.UpdateVisibleChunks(chunkFootprintInfo, ref newChunksToSend);
        }

        for (int i = 0; i < transform.childCount; i++)
        {
            User user = transform.GetChild(i).GetComponent<User>();
            user.UpdateChunksPlanned(newChunksToSend);
        }
    }

    private void UpdateVisIndi(User user, StreamWriter writer)
    {
        Vector3 position = user.transform.position;
        int xStartIndex = Mathf.FloorToInt((position.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
        int zStartIndex = Mathf.FloorToInt((position.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
        if (xStartIndex == user.preX && zStartIndex == user.preZ) { return; }
        visibleObjectsInRegion = new int[vc.objectsInScene.Count];
        vc.GetVisibleObjectsInRegion(user.transform.position, epsilon, ref visibleObjectsInRegion);
        user.UpdateVisibleObjectsIndi(visibleObjectsInRegion, ref objectSentIndi, writer);
        user.preX = xStartIndex;
        user.preZ = zStartIndex;
    }

    void OnApplicationQuit()
    {
        // Close the file when the application quits
        if (writer != null)
        {
            writer.Close();
            writer.Dispose();
            Debug.Log("CSV file closed.");
        }
    }
}

public class PriorityQueue<TElement, TPriority, AElement, IDELement> where TPriority : IComparable<TPriority>
{
    private List<(TElement Element, IDELement id, TPriority Priority, int Count, AElement AdditionalElement)> _heap = new();
    private Dictionary<IDELement, int> _indexMap = new();
    public bool descending = true, ifHaveChunkCoolingDown = false;

    public int Count => _heap.Count;

    public void Enqueue(TElement element, IDELement id, TPriority priority, int count, AElement additionalElement)
    {
        int index = 0;
        if (_indexMap.TryGetValue(id, out index))
        {
            _heap[index] = (element, id, _heap[index].Priority, count, additionalElement);
        }
        else
        {
            _heap.Add((element, id, priority, count, additionalElement));
            index = _heap.Count - 1;
            _indexMap[id] = index;
        }
        HeapifyUp(index);
    }

    //public void AddTimes(TElement element, TPriority priority, int count)
    //{
    //    int index = 0;
    //    if (_indexMap.TryGetValue(element, out index))
    //    {
    //        _heap[index] = (element, _heap[index].Priority, _heap[index].Count + count);
    //    }
    //    else
    //    {
    //        _heap.Add((element, priority, count));
    //        index = _heap.Count - 1;
    //        _indexMap[element] = index;
    //    }
    //    HeapifyUp(index);
    //}

    public TElement Dequeue()
    {
        if (_heap.Count == 0)
            throw new InvalidOperationException("PriorityQueue is empty.");

        var root = _heap[0];

        if (root.Count > 1 && !ifHaveChunkCoolingDown)
        {
            _heap[0] = (root.Element, root.id, root.Priority, root.Count - 1, root.AdditionalElement);
            HeapifyDown(0);
            return root.Element;
        }

        Swap(0, _heap.Count - 1);
        _heap.RemoveAt(_heap.Count - 1);
        _indexMap.Remove(root.id);

        if (_heap.Count > 0)
            HeapifyDown(0);

        return root.Element;
    }

    public bool Remove(IDELement id)
    {
        if (!_indexMap.TryGetValue(id, out int index))
            return false;

        int lastIndex = _heap.Count - 1;

        // Swap with the last element
        Swap(index, lastIndex);

        // Remove from list and map
        _heap.RemoveAt(lastIndex);
        _indexMap.Remove(id);

        // Restore heap only if we didn't remove the last element directly
        if (index < _heap.Count)
        {
            // Determine if we should heapify up or down
            if (index > 0 && IsHigherPriority(_heap[index], _heap[(index - 1) / 2]))
                HeapifyUp(index);
            else
                HeapifyDown(index);
        }

        return true;
    }

    public bool DecreaseCount(IDELement id, int amount)
    {
        if (!_indexMap.TryGetValue(id, out int index))
            return false;

        var entry = _heap[index];
        int newCount = entry.Count - amount;

        if (newCount <= 0)
        {
            // Remove if count drops to 0 or below
            return Remove(id);
        }
        else
        {
            _heap[index] = (entry.Element, id, entry.Priority, newCount, entry.AdditionalElement);

            // Reorder heap depending on priority change
            if (index > 0 && IsHigherPriority(_heap[index], _heap[(index - 1) / 2]))
                HeapifyUp(index);
            else
                HeapifyDown(index);
        }

        return true;
    }

    public IDELement Peek()
    {
        if (_heap.Count == 0)
            throw new InvalidOperationException("PriorityQueue is empty.");
        return _heap[0].id;
    }

    public void UpdatePriority(IDELement id, TPriority newPriority)
    {
        if (!_indexMap.TryGetValue(id, out int index))
            throw new KeyNotFoundException("Element not found in priority queue.");

        var current = _heap[index];
        var updated = (current.Element, current.id, newPriority, current.Count, current.AdditionalElement);
        bool moveUp = IsHigherPriority(updated, current);

        _heap[index] = updated;

        if (moveUp)
            HeapifyUp(index);
        else
            HeapifyDown(index);

    }

    public bool Contains(IDELement id) => _indexMap.ContainsKey(id);

    public void Clear()
    {
        _heap.Clear();
        _indexMap.Clear();
    }

    private void HeapifyUp(int i)
    {
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (!IsHigherPriority(_heap[i], _heap[parent]))
                break;

            Swap(i, parent);
            i = parent;
        }
    }

    private bool IsHigherPriority((TElement Element, IDELement id, TPriority Priority, int Count, AElement AdditionalElement) a,
                              (TElement Element, IDELement id, TPriority Priority, int Count, AElement AdditionalElement) b)
    {
        if (a.Count != b.Count && !ifHaveChunkCoolingDown)
            return a.Count > b.Count; // Higher count = higher priority

        if (descending)
            return a.Priority.CompareTo(b.Priority) > 0; // Higher priority value = higher priority
        else
            return a.Priority.CompareTo(b.Priority) < 0; // Lower priority value = higher priority
    }

    private void HeapifyDown(int i)
    {
        int last = _heap.Count - 1;
        while (true)
        {
            int left = 2 * i + 1;
            int right = 2 * i + 2;
            int best = i;

            if (left <= last && IsHigherPriority(_heap[left], _heap[best]))
                best = left;
            if (right <= last && IsHigherPriority(_heap[right], _heap[best]))
                best = right;

            if (best == i) break;

            Swap(i, best);
            i = best;
        }
    }


    private void Swap(int i, int j)
    {
        var temp = _heap[i];
        _heap[i] = _heap[j];
        _heap[j] = temp;

        _indexMap[_heap[i].id] = i;
        _indexMap[_heap[j].id] = j;
    }


    public TPriority GetPriority(IDELement id)
    {
        if (!_indexMap.TryGetValue(id, out int index))
            throw new KeyNotFoundException("Element not found in priority queue.");

        return _heap[index].Priority;
    }

    public int GetCount(IDELement id)
    {
        if (!_indexMap.TryGetValue(id, out int index))
            throw new KeyNotFoundException("Element not found in priority queue.");

        return _heap[index].Count;
    }

    public TElement GetElement(IDELement id)
    {
        if (!_indexMap.TryGetValue(id, out int index))
            throw new KeyNotFoundException("Element not found in priority queue.");

        return _heap[index].Element;
    }

    public AElement GetAdditionalElement(IDELement id)
    {
        if (!_indexMap.TryGetValue(id, out int index))
            throw new KeyNotFoundException("Element not found in priority queue.");

        return _heap[index].AdditionalElement;
    }
}