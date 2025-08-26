using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework.Constraints;
using UnityEngine;

public class SimulatorVisibility
{
    private List<GameObject> objectsInScene;
    private long[] objectFootprintInfo;
    private Dictionary<string, int[]> diffInfoAdd, objectFootprintsInGrid;
    private int numInUnitX = 10, numInUnitZ = 10;
    private GridDivide gd;
    private GameObject sceneRoot;
    private ObjectChunkManager chunkManager;
    private ObjectTableManager objectTableManager;
    private ResourceLoader resourceLoader;

    private Dictionary<int, GameObject> visualizedObjects = new Dictionary<int, GameObject>();
    private Dictionary<int, Vector3[]> verticesDict = new Dictionary<int, Vector3[]>();
    private Dictionary<int, Vector3[]> normalsDict = new Dictionary<int, Vector3[]>();
    private Dictionary<int, List<List<int>>> trianglesDict = new Dictionary<int, List<List<int>>>();
    private GameObject chunkVisGroundTruthRoot;
    private Dictionary<string, int[]> chunkFootprintsAtCorner = new Dictionary<string, int[]>();
    private Dictionary<int, HashSet<int>> receivedChunksPerObject = new Dictionary<int, HashSet<int>>();
    private Dictionary<int, long[]> chunkFootprintInfo = new Dictionary<int, long[]>();

    // public variables for total objects and chunks sent based on the visibility check
    public int totalObjectsSentByChunk = 0;
    public int totalChunksSentByChunk = 0;
    public int totalObjectsSentByObject = 0;
    public int totalChunksSentByObject = 0;

    // Dynamic Visibility Check Per User
    private int preX = 0, preZ = 0;

    public enum VisibilityType
    {
        Chunk,
        Object
    }

    public void ShowVisibilityGroundTruthByType(VisibilityType type, bool active)
    {
        if (chunkVisGroundTruthRoot != null)
            chunkVisGroundTruthRoot.SetActive(type == VisibilityType.Chunk && active);
        if (sceneRoot != null)
            sceneRoot.SetActive(type == VisibilityType.Object && active);
    }

    public (int, int) GetTotalObjectsAndChunksSent(VisibilityType type)
    {
        if (type == VisibilityType.Chunk)
        {
            return (totalObjectsSentByChunk, totalChunksSentByChunk);
        }
        else if (type == VisibilityType.Object)
        {
            return (totalObjectsSentByObject, totalChunksSentByObject);
        }

        Debug.LogError($"Invalid visibility type: {type}");
        return (0, 0);
    }

    public Dictionary<int, long[]> GetChunkFootprintInfo()
    {
        if (chunkFootprintInfo.Count == 0)
        {
            Debug.LogError("Chunk footprint info is empty");
            return new Dictionary<int, long[]>();
        }
        return chunkFootprintInfo;
    }

    public void ResetVisibility()
    {
        totalObjectsSentByChunk = 0;
        totalChunksSentByChunk = 0;
        totalObjectsSentByObject = 0;
        totalChunksSentByObject = 0;

        preX = 0;
        preZ = 0;


        // Reset visualized chunks
        foreach (var obj in visualizedObjects.Values)
        {
            if (obj != null)
            {
                GameObject.Destroy(obj);
            }
        }
        visualizedObjects.Clear();
        verticesDict.Clear();
        normalsDict.Clear();
        trianglesDict.Clear();

        // reset chunk footprint info
        chunkFootprintInfo.Clear();
        receivedChunksPerObject.Clear();
    }

    // Archived: for static visibility check
    public void ComputeVisibility(Vector3 position, float radius)
    {
        SetVisibilityChunksInRegion(position, radius);
        //SetVisibilityObjectsInRegion(position, radius);
    }
    

    public void ComputeDynamicVisibility(Vector3 position, float radius, string testPhase)
    {
        if (testPhase != TestPhase.StandPhase.ToString() && 
            testPhase != TestPhase.MovingPhase.ToString() && 
            testPhase != TestPhase.QuestionPhase.ToString()
        ) return;

        Vector3 userPosition = position;
        int xStartIndex = Mathf.FloorToInt((userPosition.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
        int zStartIndex = Mathf.FloorToInt((userPosition.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
        
        if (xStartIndex == preX && zStartIndex == preZ) return;

        // update the visibility if user located in a new grid unit
        preX = xStartIndex; preZ = zStartIndex;
        UpdateVisibilityChunksInRegion(position, radius);

        // Debug.Log($"position: {position}, radius: {radius}, xStartIndex: {xStartIndex}, zStartIndex: {zStartIndex}");
    }

    public SimulatorVisibility(GameObject sceneRoot, GridDivide gridDivide, ObjectChunkManager chunkManager, ObjectTableManager objectTableManager, ResourceLoader resourceLoader)
    {
        this.sceneRoot = sceneRoot;
        this.gd = gridDivide;
        this.chunkManager = chunkManager;
        this.objectTableManager = objectTableManager;
        this.resourceLoader = resourceLoader;
        objectsInScene = new List<GameObject>();
        diffInfoAdd = new Dictionary<string, int[]>();
        objectFootprintsInGrid = new Dictionary<string, int[]>();
        AddAllObjects(sceneRoot.transform);
        chunkVisGroundTruthRoot = new GameObject("ChunkVisGroundTruthRoot");
        // chunkVisGroundTruthRoot.SetActive(false);  // change to use layer instead
        Debug.Log($"SimulatorVisibility initialized with {objectsInScene.Count} objects in scene");
    }

    private void AddAllObjects(Transform child)
    {
        if (!child.gameObject.activeSelf)
            return;
        MeshRenderer mr = child.GetComponent<MeshRenderer>();
        if (mr != null)
            objectsInScene.Add(child.gameObject);
        for (int i = 0; i < child.childCount; i++)
            AddAllObjects(child.GetChild(i));
    }

    public void SetVisibilityObjectsInRegion(Vector3 head_position, float radius)
    {
        totalObjectsSentByObject = 0;
        totalChunksSentByObject = 0;
        
        objectFootprintInfo = new long[objectsInScene.Count];
        // Archived
        //GetVisibleObjectsInRegionProg_Internal(head_position, radius, ref visibleObjects);
        ReadFootprintByObjectInRegion(head_position, radius, ref objectFootprintInfo);

        Debug.Log($"objectFootprintInfo.Length: {objectFootprintInfo.Length}");

        for (int i = 0; i < objectFootprintInfo.Length; i++)
        {
            if (objectFootprintInfo[i] > 0)
            {
                totalObjectsSentByObject++;
                totalChunksSentByObject += chunkManager.GetChunkCount(i);
            }
            
            objectsInScene[i].SetActive(objectFootprintInfo[i] > 0 || objectsInScene[i].tag == "Terrain");
        }

        Debug.Log($"Total objects sent: {totalObjectsSentByObject}, total chunks sent: {totalChunksSentByObject}");
    }

    private void UpdateVisibilityChunksInRegion(Vector3 position, float radius)
    {
        if (chunkFootprintInfo.Count > 0)
            chunkFootprintInfo.Clear();
        // Incremental update: preserve existing visibility state
        // Don't reset totalObjectsSentByChunk and totalChunksSentByChunk
        // Don't clear visualizedObjects and related dictionaries
        
        Debug.Log($"Updating visibility chunks in region at {position}");
        
        ReadFootprintByChunkInRegion(position, radius, ref chunkFootprintInfo);
        // Debug.Log($"chunkFootprintInfo.Count: {chunkFootprintInfo.Count}");
        
        foreach (var objectID in chunkFootprintInfo.Keys)
        {
            long[] footprints = chunkFootprintInfo[objectID];
            bool firstChunkOfObjectSent = true;
            
            // Check if this object is already being visualized
            bool objectAlreadyVisible = visualizedObjects.ContainsKey(objectID);
            
            // Initialize tracking for this object if needed
            if (!receivedChunksPerObject.ContainsKey(objectID))
            {
                receivedChunksPerObject[objectID] = new HashSet<int>();
            }
            
            for (int chunkID = 0; chunkID < footprints.Length; chunkID++)
            {
                if (footprints[chunkID] > 0)
                {
                    // Check if this specific chunk has already been received
                    bool chunkAlreadyReceived = receivedChunksPerObject[objectID].Contains(chunkID);
                    
                    if (firstChunkOfObjectSent && !objectAlreadyVisible)
                    {
                        totalObjectsSentByChunk++;
                        firstChunkOfObjectSent = false;
                    }

                    if (!chunkAlreadyReceived)
                    {
                        totalChunksSentByChunk++;
                        receivedChunksPerObject[objectID].Add(chunkID);
                    }
                    
                    VisualizeChunk(objectID, chunkID);
                }
            }
        }
        // Debug.Log($"Updated totals - Objects: {totalObjectsSentByChunk}, Chunks: {totalChunksSentByChunk}");
    }

    public void SetVisibilityChunksInRegion(Vector3 position, float radius)
    {
        totalObjectsSentByChunk = 0;
        totalChunksSentByChunk = 0;

        // Reset visualized chunks
        foreach (var obj in visualizedObjects.Values)
        {
            if (obj != null)
            {
                GameObject.Destroy(obj);
            }
        }
        visualizedObjects.Clear();
        verticesDict.Clear();
        normalsDict.Clear();
        trianglesDict.Clear();

        Debug.Log($"Setting visibility chunks in region at {position}, {radius}");
        
        Dictionary<int, long[]> chunkFootprintInfo = new Dictionary<int, long[]>();
        ReadFootprintByChunkInRegion(position, radius, ref chunkFootprintInfo);
        Debug.Log($"chunkFootprintInfo.Count: {chunkFootprintInfo.Count}");
        
        foreach (var objectID in chunkFootprintInfo.Keys)
        {
            long[] footprints = chunkFootprintInfo[objectID];
            bool firstChunkOfObjectSent = true;
            for (int chunkID = 0; chunkID < footprints.Length; chunkID++)
            {
                if (footprints[chunkID] > 0)
                {
                    if (firstChunkOfObjectSent)
                    {
                        totalObjectsSentByChunk++;
                        firstChunkOfObjectSent = false;
                    }

                    totalChunksSentByChunk++;
                    VisualizeChunk(objectID, chunkID);
                }
            }
        }
        Debug.Log($"Total objects sent: {totalObjectsSentByChunk}, total chunks sent: {totalChunksSentByChunk}");
    }

    public long[] GetVisibleObjectInRegion()
    {
        return objectFootprintInfo;
    }

    // Archived
    private void GetVisibleObjectsInRegionProg_Internal(Vector3 position, float radius, ref int[] objectVisibility)
    {
        int xStartIndex = Mathf.FloorToInt((position.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
        int zStartIndex = Mathf.FloorToInt((position.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
        int[] footprints = ReadFootprintGridUnit(xStartIndex, zStartIndex);
        for (int i = 0; i < footprints.Length; i++)
        {
            objectVisibility[footprints[i]] = 1;
        }
        int gridNumToInclude = Mathf.FloorToInt(radius / gd.gridSize);
        UpdateVisOnLine(xStartIndex, zStartIndex, xStartIndex - gridNumToInclude, zStartIndex, -1, 0, ref objectVisibility);
        UpdateVisOnLine(xStartIndex, zStartIndex, xStartIndex + gridNumToInclude, zStartIndex, 1, 0, ref objectVisibility);
        for (int i = xStartIndex - gridNumToInclude; i < xStartIndex + gridNumToInclude + 1; i++)
        {
            UpdateVisOnLine(i, zStartIndex, i, zStartIndex - gridNumToInclude, 0, -1, ref objectVisibility);
            UpdateVisOnLine(i, zStartIndex, i, zStartIndex + gridNumToInclude, 0, 1, ref objectVisibility);
        }
    }

    private void UpdateVisOnLine(int fromX, int fromZ, int toX, int toZ, int xStep, int zStep, ref int[] visibility)
    {
        if (fromX == toX && fromZ == toZ) { return; }
        UpdateVis(fromX, fromZ, fromX + xStep, fromZ + zStep, ref visibility);
        UpdateVisOnLine(fromX + xStep, fromZ + zStep, toX, toZ, xStep, zStep, ref visibility);
    }

    private void UpdateVis(int fromX, int fromZ, int toX, int toZ, ref int[] visibility)
    {
        int[] updatedVis = ReadFootprintsDiffUnit(fromX, fromZ, toX, toZ);
        int count = updatedVis[0];
        if (count != 0)
        {
            for (int i = 1; i < count + 1; i++)
            {
                visibility[updatedVis[i]] = 1;
            }
        }
    }


    private int[] ReadFootprintGridUnit(int x, int z)
    {
        string indiGrid = $"{x}_{z}";
        if (objectFootprintsInGrid.ContainsKey(indiGrid))
        {
            return objectFootprintsInGrid[indiGrid];
        }
        string filePath = "./Assets/Data/GridLevelFootprintsUnit/";
        int unitX = x / (int)numInUnitX, unitZ = z / (int)numInUnitZ;
        string fileName = $"{filePath}{unitX}_{unitZ}.bin";
        if (!File.Exists(fileName))
        {
            Debug.LogError($"{fileName} does not exist");
            return new int[objectsInScene.Count];
        }
        byte[] bytes_read = File.ReadAllBytes(fileName);
        int[] visInfo = ConvertByteArrayToIntArrayNoHeader(bytes_read);
        for (int k = 0; k < numInUnitX; k++)
        {
            for (int l = 0; l < numInUnitZ; l++)
            {
                int gridX = unitX * (int)numInUnitX + k, gridZ = unitZ * (int)numInUnitZ + l;
                int objectCount = objectsInScene.Count;
                int[] newGridLevelFootprintInfo = new int[objectCount];
                Array.Copy(visInfo, objectCount * (k * (int)numInUnitX + l), newGridLevelFootprintInfo, 0, objectCount);
                objectFootprintsInGrid.Add($"{gridX}_{gridZ}", newGridLevelFootprintInfo);
            }
        }
        return objectFootprintsInGrid[indiGrid];
    }

    private static int[] ConvertByteArrayToIntArrayNoHeader(byte[] byteArray)
    {
        int[] ints = new int[byteArray.Length / sizeof(int)];
        Buffer.BlockCopy(byteArray, 0, ints, 0, byteArray.Length);
        return ints;
    }

    // Archived
    // private int[] ReadFootprintGridUnit(int x, int z)
    // {
    //     string indiGrid = $"{x}_{z}";
    //     if (visibleObjectsInGrid.ContainsKey(indiGrid))
    //     {
    //         return visibleObjectsInGrid[indiGrid];
    //     }
    //     string filePath = "./Assets/Data/GridLevelVis_Unit/";
    //     int unitX = x / (int)numInUnitX, unitZ = z / (int)numInUnitZ;
    //     string fileName = $"{filePath}{unitX}_{unitZ}.bin";
    //     if (!File.Exists(fileName))
    //     {
    //         Debug.LogError($"{fileName} does not exist");
    //         return new int[objectsInScene.Count];
    //     }
    //     byte[] bytes_read = File.ReadAllBytes(fileName);
    //     int[] visInfo = ConvertByteArrayToIntArray(bytes_read);
    //     int cursor = 0;
    //     for (int k = 0; k < numInUnitX; k++)
    //     {
    //         for (int l = 0; l < numInUnitZ; l++)
    //         {
    //             int gridX = unitX * (int)numInUnitX + k, gridZ = unitZ * (int)numInUnitZ + l;
    //             int[] visibleObjects = new int[visInfo[cursor]];
    //             Array.Copy(visInfo, cursor + 1, visibleObjects, 0, visInfo[cursor]);
    //             visibleObjectsInGrid.Add($"{gridX}_{gridZ}", visibleObjects);
    //             cursor += 1 + visInfo[cursor];
    //         }
    //     }
    //     return visibleObjectsInGrid[indiGrid];
    // }

    // Archived
    private int[] ReadFootprintsDiffUnit(int fromX, int fromZ, int toX, int toZ)
    {
        string indiDiff = $"{fromX}_{fromZ}_{toX}_{toZ}";
        if (diffInfoAdd.ContainsKey(indiDiff))
        {
            return diffInfoAdd[indiDiff];
        }
        string filePath = "./Assets/Data/GridDiff_Unit/";
        int unitX = fromX / (int)numInUnitX, unitZ = fromZ / (int)numInUnitZ;
        string fileName = $"{filePath}{unitX}_{unitZ}.bin";
        if (!File.Exists(fileName))
        {
            Debug.LogError($"{fileName} does not exist");
            return new int[objectsInScene.Count];
        }
        byte[] bytes_read = File.ReadAllBytes(fileName);
        int[] diffInfo = ConvertByteArrayToIntArray(bytes_read);
        int cursor = 0;
        for (int k = 0; k < numInUnitX; k++)
        {
            for (int l = 0; l < numInUnitZ; l++)
            {
                int gridX = unitX * (int)numInUnitX + k, gridZ = unitZ * (int)numInUnitZ + l;
                ReadUnit($"{gridX}_{gridZ}_{gridX + 1}_{gridZ}", ref cursor, diffInfo);
                ReadUnit($"{gridX}_{gridZ}_{gridX - 1}_{gridZ}", ref cursor, diffInfo);
                ReadUnit($"{gridX}_{gridZ}_{gridX}_{gridZ + 1}", ref cursor, diffInfo);
                ReadUnit($"{gridX}_{gridZ}_{gridX}_{gridZ - 1}", ref cursor, diffInfo);
            }
        }
        indiDiff = $"{fromX}_{fromZ}_{toX}_{toZ}";
        return diffInfoAdd[indiDiff];
    }

    private void ReadUnit(string indiDiff, ref int cursor, int[] diffInfo)
    {
        int numToAdd = diffInfo[cursor];
        int[] objectsAdd = new int[1 + numToAdd];
        cursor++;
        objectsAdd[0] = numToAdd;
        if (numToAdd != 0)
        {
            Array.Copy(diffInfo, cursor, objectsAdd, 1, numToAdd);
        }
        diffInfoAdd.Add(indiDiff, objectsAdd);
        cursor += numToAdd;
        int numToRemove = diffInfo[cursor];
        cursor++;
        cursor += numToRemove;
    }

    private static int[] ConvertByteArrayToIntArray(byte[] byteArray)
    {
        int length = BitConverter.ToInt32(byteArray, 0);
        int[] array = new int[length];
        Buffer.BlockCopy(byteArray, 4, array, 0, length * 4);
        return array;
    }

    private void ReadFootprintByObjectInRegion(Vector3 position, float radius, ref long[] objectFootprints)
    {
        int xStartIndex = Mathf.FloorToInt((position.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
        int zStartIndex = Mathf.FloorToInt((position.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);

        int gridNumToInclude = Mathf.FloorToInt(radius / gd.gridSize);

        for (int i = xStartIndex - gridNumToInclude; i < xStartIndex + gridNumToInclude + 1; i++)
        {
            for (int j = zStartIndex - gridNumToInclude; j < zStartIndex + gridNumToInclude + 1; j++)
            {
                int[] footprints = ReadFootprintGridUnit(i, j);
                for (int k = 0; k < objectFootprints.Length; k++)
                {
                    objectFootprints[k] += footprints[k];
                }
            }
        }
    }

    
    private void ReadFootprintByChunkInRegion(Vector3 position, float radius, ref Dictionary<int, long[]> result)
    {
        int xStartIndex = Mathf.FloorToInt((position.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
        int zStartIndex = Mathf.FloorToInt((position.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
        int gridNumToInclude = Mathf.FloorToInt(radius / gd.gridSize);
        
        for (int i = xStartIndex - gridNumToInclude; i <= xStartIndex + gridNumToInclude + 1; i++)
        {
            for (int j = zStartIndex - gridNumToInclude; j <= zStartIndex + gridNumToInclude + 1; j++)
            {
                // Debug.Log($"Reading footprint by chunk at {i}, {j}");

                int[] intData = new int[0];
                ReadFootprintByChunk(i, j, ref intData);
                // Debug.Log($"intData.Length: {intData.Length}");
                if (intData == null)
                {
                    continue;
                }
                int cursor = 0;
                while (cursor < intData.Length)
                {
                    int objectID = intData[cursor++];
                    int chunkCount = GetChunkCountForObject(objectID);
                    if (!result.ContainsKey(objectID))
                    {
                        result[objectID] = new long[chunkCount];
                    }
                    for (int k = 0; k < chunkCount; k++)
                    {
                        result[objectID][k] += intData[cursor++];
                        //Debug.Log($"Object {objectID} chunk {k} footprint: {result[objectID][k]}");
                    }
                }
            }
        }
        // Debug.Log($"object number: {result.Count}");
    }


    
    private void ReadFootprintByChunk(int x, int z, ref int[] visibleChunksAtCorner)
    {
        string fileName = $"{x}_{z}";
        if (chunkFootprintsAtCorner.ContainsKey(fileName))
        {
            visibleChunksAtCorner = chunkFootprintsAtCorner[fileName];
            return;
        }

        int unitX = x / numInUnitX, unitZ = z / numInUnitZ;
        // TODO: think about loading the file at the start of the simulation
        string path = $"Assets/Data/CornerLevelFootprintsByChunkUnit/{unitX}_{unitZ}.bin";
        byte[] bytes_read = File.ReadAllBytes(path);
        int cursor = 0;
        for (int k = 0; k < numInUnitX; k++)
        {
            for (int l = 0; l < numInUnitZ; l++)
            {
                int gridX = unitX * numInUnitX + k, gridZ = unitZ * numInUnitZ + l;
                int length = BitConverter.ToInt32(bytes_read, cursor);
                int[] intData = new int[length / sizeof(int)]; cursor += sizeof(int);
                Buffer.BlockCopy(bytes_read, cursor, intData, 0, length); cursor += length;
                chunkFootprintsAtCorner.Add($"{gridX}_{gridZ}", intData);
                visibleChunksAtCorner = intData;
            }
        }
        visibleChunksAtCorner = chunkFootprintsAtCorner[fileName];

        // string fileName = $"{x}_{z}";
        // string path = $"Assets/Data/CornerLevelFootprintsByChunk/{x}_{z}.bin";
        // if (!File.Exists(path))
        // {
        //     Debug.Log($"File {path} does not exist");
        //     visibleChunksAtCorner = null;
        //     return;
        // }
        // byte[] byteData = File.ReadAllBytes(path);
        // int[] intData = new int[byteData.Length / sizeof(int)];
        // Buffer.BlockCopy(byteData, 0, intData, 0, byteData.Length);
        // visibleChunksAtCorner = intData;
    }

   
    private int GetChunkCountForObject(int objectID)
    {
        // Try to infer chunk count from the chunk file in Assets/Data/objectChunksGrouped/object_{objectID}.bin
        string chunkFilePath = $"Assets/Data/objectChunksGrouped/object_{objectID}.bin";
        if (!File.Exists(chunkFilePath))
            return 0;
        using (BinaryReader reader = new BinaryReader(File.Open(chunkFilePath, FileMode.Open)))
        {
            int chunkCount = reader.ReadInt32();
            return chunkCount;
        }
    }


    private void VisualizeChunk(int objectID, int chunkID)
    {
        // Get chunk data
        if (!chunkManager.HasChunk(objectID, chunkID))
            return;

        byte[] chunk = chunkManager.GetChunk(objectID, chunkID);
        if (chunk == null) return;

        // Get mesh metadata from ObjectTableManager
        var holder = objectTableManager.GetObjectInfo(objectID);
        if (holder == null) return;
        int totalVertNum = holder.totalVertNum;
        int submeshCount = holder.submeshCount;

        // Initialize mesh data structures if needed
        if (!verticesDict.ContainsKey(objectID))
        {
            verticesDict[objectID] = new Vector3[totalVertNum];
            normalsDict[objectID] = new Vector3[totalVertNum];
            trianglesDict[objectID] = new List<List<int>>();
            for (int i = 0; i < submeshCount; i++)
                trianglesDict[objectID].Add(new List<int>());
        }

        var verticesArr = verticesDict[objectID];
        var normalsArr = normalsDict[objectID];
        var trianglesArr = trianglesDict[objectID];

        // Parse header (grouped format)
        int cursor = 0;
        char submeshType = BitConverter.ToChar(chunk, cursor);
        int objectId = BitConverter.ToInt32(chunk, cursor += 2);
        int chId = BitConverter.ToInt32(chunk, cursor += sizeof(int));
        int submeshId = BitConverter.ToInt32(chunk, cursor += sizeof(int));
        int headerSize = cursor += sizeof(int);

        // Parse data
        int dataSize = chunk.Length - headerSize;
        byte[] chunkData = new byte[dataSize];
        Buffer.BlockCopy(chunk, headerSize, chunkData, 0, dataSize);

        cursor = 0;
        // Vertices
        int vertexCount = BitConverter.ToInt32(chunkData, cursor); cursor += sizeof(int);
        for (int i = 0; i < vertexCount; i++)
        {
            int index = BitConverter.ToInt32(chunkData, cursor); cursor += sizeof(int);
            // Only update if not already filled
            if (verticesArr[index] != Vector3.zero)
            {
                cursor += 6 * sizeof(float);
                continue;
            }
            float x = BitConverter.ToSingle(chunkData, cursor); cursor += sizeof(float);
            float y = BitConverter.ToSingle(chunkData, cursor); cursor += sizeof(float);
            float z = BitConverter.ToSingle(chunkData, cursor); cursor += sizeof(float);
            verticesArr[index] = new Vector3(x, y, z);

            float nx = BitConverter.ToSingle(chunkData, cursor); cursor += sizeof(float);
            float ny = BitConverter.ToSingle(chunkData, cursor); cursor += sizeof(float);
            float nz = BitConverter.ToSingle(chunkData, cursor); cursor += sizeof(float);
            normalsArr[index] = new Vector3(nx, ny, nz);
        }
        // Triangles
        int triangleCount = BitConverter.ToInt32(chunkData, cursor); cursor += sizeof(int);
        for (int i = 0; i < triangleCount; i++)
        {
            int tri = BitConverter.ToInt32(chunkData, cursor); cursor += sizeof(int);
            trianglesArr[submeshId].Add(tri);
        }

        // Update or create GameObject
        if (!visualizedObjects.ContainsKey(objectID))
        {
            // Create new object if it doesn't exist
            GameObject newObject = new GameObject($"Object_{objectID}");
            newObject.layer = LayerMask.NameToLayer(Commons.GROUND_TRUTH_LAYER_NAME);
            newObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = newObject.AddComponent<MeshRenderer>();

            // Create new mesh
            Mesh newMesh = new Mesh();
            newMesh.vertices = verticesArr;
            newMesh.normals = normalsArr;
            newMesh.subMeshCount = holder.submeshCount;
            for (int i = 0; i < holder.submeshCount; i++)
            {
                newMesh.SetTriangles(trianglesArr[i], i);
            }
            newMesh.RecalculateBounds();
            newObject.GetComponent<MeshFilter>().mesh = newMesh;

            // Set up materials using ResourceLoader
            List<Material> materials = new List<Material>();
            foreach (string matName in holder.materialNames)
            {
                materials.Add(resourceLoader.LoadMaterialByName(matName));
            }
            renderer.materials = materials.ToArray();

            // Set transform
            newObject.transform.position = holder.position;
            newObject.transform.eulerAngles = holder.eulerAngles;
            newObject.transform.localScale = holder.scale;
            // Parent to chunkVisualizationRoot
            newObject.transform.SetParent(chunkVisGroundTruthRoot.transform);

            visualizedObjects[objectID] = newObject;
        }
        else
        {
            // Update existing mesh
            GameObject obj = visualizedObjects[objectID];
            Mesh mesh = obj.GetComponent<MeshFilter>().mesh;
            mesh.vertices = verticesArr;
            mesh.normals = normalsArr;
            for (int i = 0; i < holder.submeshCount; i++)
            {
                mesh.SetTriangles(trianglesArr[i], i);
            }
            mesh.RecalculateBounds();
        }
    }
}
