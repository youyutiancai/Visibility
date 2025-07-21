using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class SimulatorVisibility
{
    private List<GameObject> objectsInScene;
    private int[] visibleObjects;
    private Dictionary<string, int[]> diffInfoAdd, visibleObjectsInGrid;
    private float numInUnitX = 10f, numInUnitZ = 10f;
    private GridDivide gd;
    private GameObject sceneRoot;
    private ObjectChunkManager chunkManager;
    private ObjectTableManager objectTableManager;

    private Dictionary<int, GameObject> visualizedObjects = new Dictionary<int, GameObject>();
    private Dictionary<int, Vector3[]> verticesDict = new Dictionary<int, Vector3[]>();
    private Dictionary<int, Vector3[]> normalsDict = new Dictionary<int, Vector3[]>();
    private Dictionary<int, List<List<int>>> trianglesDict = new Dictionary<int, List<List<int>>>();

    public SimulatorVisibility(GameObject sceneRoot, GridDivide gridDivide, ObjectChunkManager chunkManager, ObjectTableManager objectTableManager)
    {
        this.sceneRoot = sceneRoot;
        this.gd = gridDivide;
        this.chunkManager = chunkManager;
        this.objectTableManager = objectTableManager;
        objectsInScene = new List<GameObject>();
        diffInfoAdd = new Dictionary<string, int[]>();
        visibleObjectsInGrid = new Dictionary<string, int[]>();
        AddAllObjects(sceneRoot.transform);

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

    public void SetVisibilityObjectsInScene(Vector3 head_position, float radius)
    {
        visibleObjects = new int[objectsInScene.Count];
        GetVisibleObjectsInRegionProg_Internal(head_position, radius, ref visibleObjects);
        
        for (int i = 0; i < objectsInScene.Count; i++)
        {
            objectsInScene[i].SetActive(visibleObjects[i] > 0 || objectsInScene[i].tag == "Terrain");
        }
    }

    public void SetVisibilityChunksInRegion(Vector3 position, float radius)
    {
        Dictionary<int, long[]> chunkFootprintInfo = new Dictionary<int, long[]>();
        ReadFootprintByChunkInRegion(position, radius, ref chunkFootprintInfo);
        Debug.Log($"chunkFootprintInfo.Count: {chunkFootprintInfo.Count}");
        
        foreach (var objectID in chunkFootprintInfo.Keys)
        {
            long[] footprints = chunkFootprintInfo[objectID];
            for (int chunkID = 0; chunkID < footprints.Length; chunkID++)
            {
                if (footprints[chunkID] > 0)
                {
                    VisualizeChunk(objectID, chunkID);
                }
            }
        }
    }

    public int[] GetVisibleObjectInRegion()
    {
        return visibleObjects;
    }

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
        if (visibleObjectsInGrid.ContainsKey(indiGrid))
        {
            return visibleObjectsInGrid[indiGrid];
        }
        string filePath = "./Assets/Data/GridLevelVis_Unit/";
        int unitX = x / (int)numInUnitX, unitZ = z / (int)numInUnitZ;
        string fileName = $"{filePath}{unitX}_{unitZ}.bin";
        if (!File.Exists(fileName))
        {
            Debug.LogError($"{fileName} does not exist");
            return new int[objectsInScene.Count];
        }
        byte[] bytes_read = File.ReadAllBytes(fileName);
        int[] visInfo = ConvertByteArrayToIntArray(bytes_read);
        int cursor = 0;
        for (int k = 0; k < numInUnitX; k++)
        {
            for (int l = 0; l < numInUnitZ; l++)
            {
                int gridX = unitX * (int)numInUnitX + k, gridZ = unitZ * (int)numInUnitZ + l;
                int[] visibleObjects = new int[visInfo[cursor]];
                Array.Copy(visInfo, cursor + 1, visibleObjects, 0, visInfo[cursor]);
                visibleObjectsInGrid.Add($"{gridX}_{gridZ}", visibleObjects);
                cursor += 1 + visInfo[cursor];
            }
        }
        return visibleObjectsInGrid[indiGrid];
    }

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

    /// <summary>
    /// Aggregates chunk-level footprint data for all objects within a region centered at 'position' with the given 'radius'.
    /// The result is a dictionary mapping objectID to an array of chunk footprint counts (long[]).
    /// </summary>
    public void ReadFootprintByChunkInRegion(Vector3 position, float radius, ref Dictionary<int, long[]> result)
    {
        int xStartIndex = Mathf.FloorToInt((position.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
        int zStartIndex = Mathf.FloorToInt((position.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
        int gridNumToInclude = Mathf.FloorToInt(radius / gd.gridSize);
        result = new Dictionary<int, long[]>();
        for (int i = xStartIndex - gridNumToInclude; i <= xStartIndex + gridNumToInclude + 1; i++)
        {
            for (int j = zStartIndex - gridNumToInclude; j <= zStartIndex + gridNumToInclude + 1; j++)
            {
                Debug.Log($"Reading footprint by chunk at {i}, {j}");

                int[] intData = new int[0];
                ReadFootprintByChunk(i, j, ref intData);
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
                        Debug.Log($"Object {objectID} chunk {k} footprint: {result[objectID][k]}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Reads the chunk-level footprint data for a specific grid cell (i, j) into intData.
    /// </summary>
    private void ReadFootprintByChunk(int x, int z, ref int[] visibleChunksAtCorner)
    {
        string fileName = $"{x}_{z}";
        string path = $"Assets/Data/CornerLevelFootprintsByChunk/{x}_{z}.bin";
        if (!File.Exists(path))
        {
            Debug.Log($"File {path} does not exist");
            visibleChunksAtCorner = null;
            return;
        }
        byte[] byteData = File.ReadAllBytes(path);
        int[] intData = new int[byteData.Length / sizeof(int)];
        Buffer.BlockCopy(byteData, 0, intData, 0, byteData.Length);
        visibleChunksAtCorner = intData;
    }

    /// <summary>
    /// Returns the number of chunks for a given objectID by scanning the chunk file directory.
    /// This is a minimal replacement for cc.objectChunksVTGrouped[objectID].Count.
    /// </summary>
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
        string[] materialNames = holder.materialNames;

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

            // Set up materials using Resources.Load
            List<Material> materials = new List<Material>();
            if (materialNames != null)
            {
                foreach (string matName in materialNames)
                {
                    var mat = Resources.Load<Material>(matName);
                    if (mat != null)
                        materials.Add(mat);
                    else
                        materials.Add(new Material(Shader.Find("Standard")));
                }
            }
            renderer.materials = materials.ToArray();

            // Set transform
            newObject.transform.position = holder.position;
            newObject.transform.eulerAngles = holder.eulerAngles;
            newObject.transform.localScale = holder.scale;

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
