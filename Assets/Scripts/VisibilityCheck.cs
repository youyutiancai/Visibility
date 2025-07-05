using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Random = UnityEngine.Random;
using UnityTCPClient.Assets.Scripts;
using System.Collections;
using System.Text;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using System.Linq;

public class VisibilityCheck : Singleton<VisibilityCheck>
{
    public Camera mainCamera;
    public GameObject sceneRoot, footprintCameras, grids, pathStart, pathEnd, testObject;
    public bool showAll, progBased, autoMove;
    public GridDivide gd;
    [HideInInspector]
    public bool captureAndCount;
    [HideInInspector]
    public Dictionary<Color, int> colorCount, colorRecordID, newColorCount;
    [HideInInspector]
    public List<GameObject> objectsInScene;
    //[HideInInspector]
    //public byte[] objectTable;
    [HideInInspector]
    public List<float> objectDataSize;
    private List<int> colors;
    private int colorID, cameraPosIDX, cameraPosIDZ, preGridX, preGridZ, stepCount;
    private bool[] fpcameraFinished;
    private bool preAutoMove, preShowAll, preProgBased;
    private Texture2D destinationTexture;
    private Rect regionToReadFrom;
    private Color color;
    private string meshInfo;
    private GameObject visTarget, preVisTarget;
    private Dictionary<string, int[]> diffInfoAdd, diffInfoRemove, visibleObjectsInGrid, objectFootprintsInGrid;

    public float frameUpdatePace;
    public int numInUnitX = 10, numInUnitZ = 10;
    public Vector3 pathStartPos, pathEndPos;

    private RenderTexture reusableRenderTexture;
    private Texture2D reusableTexture;

    void Start()
    {
        InitialValues();
        AddAllObjects(sceneRoot.transform);
        StartCoroutine(Test());
        //objectTable = CreateObjectTable();
        //TestObjectTable();
        //Debug.Log(objectsInScene.Count);
        //GenerateMeshInfo();
        //ColorObjects();
    }

    private IEnumerator Test()
    {
        yield return new WaitForSeconds(1f);
        Vector3 pos = ClusterControl.Instance.initialClusterCenter.transform.position;
        int xStartIndex = Mathf.FloorToInt((pos.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
        int zStartIndex = Mathf.FloorToInt((pos.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
        //Debug.Log($"{pos}, {gd.gridCornerParent.transform.position}, {xStartIndex}, {zStartIndex}");
        //int[] footprints = ReadFootprintGridUnit(xStartIndex, zStartIndex);
        ////int[] footprints = ReadFootprintsGrid(xStartIndex, zStartIndex);
        //Debug.Log($"current: {string.Join(", ", footprints)}");
        //int xoffset = 0, zoffset = 0;
        //int[] corner0 = ReadFootprintsGrid(xStartIndex + xoffset, zStartIndex + zoffset);
        //int[] corner1 = ReadFootprintsGrid(xStartIndex + xoffset + 1, zStartIndex + zoffset);
        //int[] corner2 = ReadFootprintsGrid(xStartIndex + xoffset, zStartIndex + zoffset + 1);
        //int[] corner3 = ReadFootprintsGrid(xStartIndex + xoffset + 1, zStartIndex + zoffset + 1);
        //List<int> unit = new List<int>();
        //for (int k = 0; k < corner0.Length; k++)
        //{
        //    unit.Add(corner0[k] + corner1[k] + corner2[k] + corner3[k]);
        //}
        //int[] unitRead = ReadFootprintGridUnit(xStartIndex, zStartIndex);
        int[] unitRead = new int[objectsInScene.Count];
        GetVisibleObjectsInRegionProg(pos, 10f, ref unitRead);
        int totalChunkCount = 0, count = 0;
        for (int k = 0; k < objectsInScene.Count; k++)
        {
            if (unitRead[k] > 0)
            {
                totalChunkCount += ClusterControl.Instance.objectChunksVTSeparate[k].Count;
                count++;
            }
            objectsInScene[k].SetActive(unitRead[k] > 0 || objectsInScene[k].tag == "Terrain" || showAll);
        }
        Debug.Log($"total object num: {count}, total chunk count: {totalChunkCount}");
    }

    private void GenerateMeshInfo()
    {
        string path = "C:\\Users\\zhou1168\\VRAR\\Visibility\\Assets\\GridData";

        string fname = "data.csv";
        using StreamWriter writer = new StreamWriter(Path.Combine(path, fname));

        meshInfo = "";
        for (int i = 0; i < objectsInScene.Count; i++)
        {
            GameObject go = objectsInScene[i];
            MeshFilter mf = go.GetComponent<MeshFilter>();
            string name = go.name;
            if (name.Split(",").Length != 1)
            {
                name = string.Join('_', name.Split(","));
            }
            if (mf is not null)
            {
                writer.WriteLine($"{name},{mf.sharedMesh.vertices.Length},{mf.sharedMesh.triangles.Length}," +
                    $"{48 * mf.sharedMesh.vertices.Length + 2 * mf.sharedMesh.triangles.Length}");
            }
            else
            {
                writer.WriteLine($"{name},0,0,0");
            }
        }

        writer.Close();
    }

    private void InitialValues()
    {
        objectsInScene = new List<GameObject>();
        objectDataSize = new List<float>();
        preGridX = 0;
        preGridZ = 0;
        pathStartPos = pathStart.transform.position;
        pathEndPos = pathEnd.transform.position;
        preAutoMove = autoMove;
        preShowAll = showAll;
        preProgBased = progBased;
        visTarget = mainCamera.gameObject;
        preVisTarget = visTarget;
        diffInfoAdd = new Dictionary<string, int[]>();
        visibleObjectsInGrid = new Dictionary<string, int[]>();
        objectFootprintsInGrid = new Dictionary<string, int[]>();
    }

    private void AddAllObjects(Transform child)
    {

        if (child.gameObject.activeSelf == false)
        {
            return;
        }
        MeshRenderer mr = child.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            objectsInScene.Add(child.gameObject);
            objectDataSize.Add(EstimateGameObjectSizeMB(child.gameObject));
        }
        for (int i = 0; i < child.childCount; i++)
        {
            AddAllObjects(child.GetChild(i));
        }
    }

    public byte[] CreateObjectTable()
    {
        int numVerticesPerChunk = 57;
        List<byte> objectTable = new List<byte>();
        objectTable.AddRange(BitConverter.GetBytes(0));
        objectTable.AddRange(BitConverter.GetBytes((int)TCPMessageType.TABLE));
        objectTable.AddRange(BitConverter.GetBytes(0));
        objectTable.AddRange(BitConverter.GetBytes(objectsInScene.Count));
        //objectTable.AddRange(BitConverter.GetBytes(0.1f));
        //objectTable.AddRange(BitConverter.GetBytes(0.1f));
        //objectTable.AddRange(BitConverter.GetBytes(0.1f));

        for (int i = 0; i < objectsInScene.Count; i++)
        {
            Transform transform = objectsInScene[i].transform;
            Mesh mesh = transform.gameObject.GetComponent<MeshFilter>().mesh;
            MeshRenderer mf = transform.gameObject.GetComponent<MeshRenderer>();
            Vector3[] vertices = mesh.vertices;

            // pose
            objectTable.AddRange(BitConverter.GetBytes(transform.position.x));
            objectTable.AddRange(BitConverter.GetBytes(transform.position.y));
            objectTable.AddRange(BitConverter.GetBytes(transform.position.z));
            objectTable.AddRange(BitConverter.GetBytes(transform.eulerAngles.x));
            objectTable.AddRange(BitConverter.GetBytes(transform.eulerAngles.y));
            objectTable.AddRange(BitConverter.GetBytes(transform.eulerAngles.z));
            objectTable.AddRange(BitConverter.GetBytes(transform.lossyScale.x));
            objectTable.AddRange(BitConverter.GetBytes(transform.lossyScale.y));
            objectTable.AddRange(BitConverter.GetBytes(transform.lossyScale.z));

            // chunk objectTable
            int numVertexChunks = Mathf.CeilToInt((float)vertices.Length / numVerticesPerChunk);
            objectTable.AddRange(BitConverter.GetBytes(numVertexChunks));
            objectTable.AddRange(BitConverter.GetBytes(CalculateTriChunks(mesh)));

            // mesh objectTable
            objectTable.AddRange(BitConverter.GetBytes(mesh.vertices.Length));
            objectTable.AddRange(BitConverter.GetBytes(mesh.subMeshCount));

            // put together the materials;
            for (int j = 0; j < mesh.subMeshCount; j++)
            {
                Material material = mf.sharedMaterials[j];
                string materialName = "null";
                if (material != null)
                {
                    materialName = material.name;
                }
                byte[] materialNameBytes = Encoding.ASCII.GetBytes(materialName);
                objectTable.AddRange(BitConverter.GetBytes(materialNameBytes.Length));
                objectTable.AddRange(materialNameBytes);
            }
            //Debug.Log($"total number of bytes {objectTable.Count}");
        }
        byte[] result = objectTable.ToArray();
        Buffer.BlockCopy(BitConverter.GetBytes(objectTable.Count - sizeof(int)), 0, result, sizeof(int) * 2, sizeof(int));
        Buffer.BlockCopy(BitConverter.GetBytes(objectTable.Count - sizeof(int)), 0, result, 0, sizeof(int));

        //Save the object table to a file
        //string dataPath = Path.Combine(Application.dataPath, "Data");
        //if (!Directory.Exists(dataPath))
        //{
        //    Directory.CreateDirectory(dataPath);
        //}
        //string filePath = Path.Combine(dataPath, "ObjectTable.bin");
        //File.WriteAllBytes(filePath, result);
        //Debug.Log($"Object table saved to: {filePath}");

        return result;
    }

    public int CalculateTriChunks(Mesh mesh)
    {
        int totalTriangleChunks = 0;
        int subMeshCount = mesh.subMeshCount;
        for (int subMeshID = 0; subMeshID < subMeshCount; subMeshID++)
        {
            int[] triangles = mesh.GetTriangles(subMeshID);

            int numTrianglesPerChunk = (1400 - sizeof(char) - sizeof(int) * 3) / (sizeof(int) * 3);
            int numTriangleChunks = Mathf.CeilToInt((float)triangles.Length / (numTrianglesPerChunk * 3));
            totalTriangleChunks += numTriangleChunks;
        }
        return totalTriangleChunks;
    }


    public static float EstimateGameObjectSizeMB(GameObject obj)
    {
        if (obj == null)
            return 0;

        long totalSize = 0;

        MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
        if (meshFilter && meshFilter.sharedMesh)
        {
            Mesh mesh = meshFilter.sharedMesh;
            totalSize += mesh.vertexCount * sizeof(float) * 3; // Vertices
            totalSize += mesh.triangles.Length * sizeof(int); // Triangles
            totalSize += mesh.uv.Length * sizeof(float) * 2; // UVs
            totalSize += mesh.normals.Length * sizeof(float) * 3; // Normals
            totalSize += mesh.colors.Length * sizeof(float) * 4; // Colors (RGBA)
        }

        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer && renderer.sharedMaterial)
        {
            totalSize += renderer.sharedMaterials.Length * 1000; // Approximate material size
        }

        Texture2D texture = null;
        if (renderer && renderer.sharedMaterial && renderer.sharedMaterial.mainTexture)
        {
            texture = renderer.sharedMaterial.mainTexture as Texture2D;
        }

        if (texture)
        {
            int bytesPerPixel = (texture.format == TextureFormat.RGBA32 || texture.format == TextureFormat.ARGB32) ? 4 : 3; // RGBA32 = 4 bytes per pixel, RGB24 = 3 bytes
            totalSize += texture.width * texture.height * bytesPerPixel;
        }

        Component[] components = obj.GetComponents<Component>();
        totalSize += components.Length * 500; // Approximate each component size

        return totalSize / (1024f * 1024f);
    }

    private void ColorObjects()
    {
        InitiateColorMap();
        Camera.onPostRender += CountFootprints;
        colorCount = new Dictionary<Color, int>();
        newColorCount = new Dictionary<Color, int>();
        CheckAllObjects(sceneRoot.transform);
    }

    private void InitiateColorMap()
    {
        colorRecordID = new Dictionary<Color, int>();
        int numCopy = 10000;
        float colorMax = Mathf.Pow(100, 3);
        HashSet<int> colorsHash = new HashSet<int>();
        while (colorsHash.Count < numCopy)
        {
            colorsHash.Add((int)Mathf.Ceil(Random.Range(0, colorMax)));
        }
        colors = new List<int>(colorsHash);
        colorID = 0;
    }

    private void CheckAllObjects(Transform child)
    {
        if (child.gameObject.activeSelf == false)
        {
            return;
        }
        MeshRenderer mr = child.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            SetColor(child.gameObject);
        }
        for (int i = 0; i < child.childCount; i++)
        {
            CheckAllObjects(child.GetChild(i));
        }
    }

    private void SetColor(GameObject target)
    {
        Color newColor = new Color(colors[colorID] % 100 / 100f, colors[colorID] / 100 % 100 / 100f,
                    colors[colorID] / 100 / 100 % 100 / 100f, 1);
        colorRecordID.Add(newColor, colorID);
        MeshRenderer renderer = target.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                    continue;
                int mode = material.GetInt("_Mode");
                Material newMaterial = new Material(Shader.Find("Custom/Cover_check"));
                if (mode == 1)
                {
                    //newMaterial.SetFloat("_IsCutout", 1);
                    newMaterial.SetFloat("_Cutoff", material.GetFloat("_Cutoff"));
                }
                else
                {
                    newMaterial.SetFloat("_Cutoff", 0);
                }
                newMaterial.SetTexture("_MainTex", material.GetTexture("_MainTex"));
                newMaterial.SetInt("_IsMorph", 0);
                newMaterial.SetFloat("_IsColor", 1);
                newMaterial.SetColor("_Color", newColor);
                materials[i] = newMaterial;
            }
            renderer.materials = materials;
        }
        colorID++;
    }

    private void UpdateVisibleObjects()
    {
        if (visTarget.tag == "Cluster")
        {
            //VisUpdateCluster();
            int[] visibleObjects = new int[objectsInScene.Count];
            GetVisibleObjectsInRegion(visTarget.transform.position, ClusterControl.Instance.epsilon, ref visibleObjects);
            for (int i = 0; i < objectsInScene.Count; i++)
            {
                objectsInScene[i].SetActive(visibleObjects[i] > 0 || objectsInScene[i].tag == "Terrain" || showAll);
            }
        }
        else if (visTarget.GetComponent<Camera>() != null)
        {
            //Debug.Log(visTarget.name);
            User user = visTarget.GetComponent<User>();
            if (user != null)
            {
                int[] visibileObjects = visTarget.GetComponent<User>().indiReceived;
                for (int i = 0; i < objectsInScene.Count; i++)
                {
                    objectsInScene[i].SetActive(visibileObjects[i] == 1 || objectsInScene[i].tag == "Terrain" || showAll);
                }
            }
            else
            {
                int[] visibleObjects = new int[objectsInScene.Count];
                GetVisibleObjectsInRegion(visTarget.transform.position, ClusterControl.Instance.epsilon / 2, ref visibleObjects);
                for (int i = 0; i < objectsInScene.Count; i++)
                {
                    objectsInScene[i].SetActive(visibleObjects[i] > 0 || objectsInScene[i].tag == "Terrain" || showAll);
                }
            }
        }
        preVisTarget = visTarget;
        preShowAll = showAll;
        preProgBased = progBased;
    }

    private void FixedUpdate()
    {
        if (Keyboard.current.aKey.wasPressedThisFrame || (!preAutoMove && autoMove))
        {
            float oriY = visTarget.transform.position.y;
            visTarget.transform.position = pathStartPos;
            visTarget.transform.position = new Vector3(visTarget.transform.position.x, oriY, visTarget.transform.position.z);
            frameUpdatePace = Mathf.Abs(frameUpdatePace);
            autoMove = true;
            stepCount = 0;
        }
        //if (autoMove)
        //{
        //    UpdateCameraPosOnPath();
        //}
        //CheckObjectSelection();
        //CheckGridsVis();
        //UpdateGrids();
    }

    private void UpdateCameraPosOnPath()
    {
        Vector3 cameraPos = visTarget.transform.position;
        float ratioOnPath = (cameraPos.x - pathStartPos.x) / (pathEndPos.x - pathStartPos.x);
        if (frameUpdatePace > 0)
        {
            if (ratioOnPath >= 1)
            {
                frameUpdatePace = -frameUpdatePace;
            }
        }
        if (ratioOnPath >= 0)
        {
            ratioOnPath += frameUpdatePace;
            float oriY = visTarget.transform.position.y;
            visTarget.transform.position = (pathEndPos - pathStartPos) * ratioOnPath + pathStartPos;
            visTarget.transform.position = new Vector3(visTarget.transform.position.x, oriY, visTarget.transform.position.z);
        }
        else
        {
            autoMove = false;
        }
        preAutoMove = autoMove;
        //CaptureImage();
    }

    public void CaptureImage()
    {
        int imageWidth = 1920;
        int imageHeight = 1080;
        RenderTexture renderTexture = new RenderTexture(imageWidth, imageHeight, 24);
        mainCamera.targetTexture = renderTexture;
        mainCamera.Render();
        Texture2D texture = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);
        RenderTexture.active = renderTexture;
        texture.ReadPixels(new Rect(0, 0, imageWidth, imageHeight), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        mainCamera.targetTexture = null;
        byte[] imageBytes = texture.EncodeToPNG();

        string path = "C:/Users/zhouy/OneDrive - purdue.edu/Desktop/VRAR/morphing/frames";
        if (showAll)
        {
            path += "/noVis";
        }
        else
        {
            path += "/Vis";
        }
        string filePath = Path.Combine(path, $"{stepCount}.png");
        File.WriteAllBytes(filePath, imageBytes);

        Debug.Log("Image saved to: " + filePath);
        Destroy(renderTexture);
        Destroy(texture);
        stepCount++;
    }

    private void CheckObjectSelection()
    {
        GameObject selectedObject = null;
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            selectedObject = Selection.activeGameObject;
        }
#endif
        if (selectedObject != null &&
            (selectedObject.tag == "Cluster" || selectedObject.tag == "User") && selectedObject != preVisTarget)
        {
            if (visTarget != null && visTarget.GetComponent<Camera>() != null)
            {
                visTarget.GetComponent<Camera>().enabled = false;
            }
            visTarget = selectedObject;
            if (visTarget.GetComponent<Camera>() != null)
            {
                visTarget.GetComponent<Camera>().enabled = true;
            }
        }
    }

    private void CheckGridsVis()
    {
        if (visTarget == null)
            return;
        int xStartIndex = Mathf.FloorToInt((visTarget.transform.position.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
        int zStartIndex = Mathf.FloorToInt((visTarget.transform.position.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
        if (xStartIndex > 0 && zStartIndex > 0 && xStartIndex < gd.numGridX - 2 && zStartIndex < gd.numGridZ - 2 &&
            (xStartIndex + 1) * gd.numGridZ + zStartIndex + 1 < gd.numGridX * gd.numGridZ &&
                (xStartIndex != preGridX || zStartIndex != preGridZ) || preShowAll != showAll || preVisTarget != visTarget
                || preProgBased != progBased)
        {
            UpdateVisibleObjects();
            preGridX = xStartIndex;
            preGridZ = zStartIndex;
        }
    }

    private void UpdateGrids()
    {
        if (visTarget == null) return;
        Vector3 posDiff = visTarget.transform.position - gd.gridCornerParent.transform.position;
        grids.transform.position = new Vector3(Mathf.FloorToInt(posDiff.x), posDiff.y, Mathf.FloorToInt(posDiff.z)) +
            gd.gridCornerParent.transform.position;
    }


    public void GetVisibleObjectsInRegion(Vector3 position, float radius, ref int[] objectVisibility)
    {
        //if (progBased)
        //{
        GetVisibleObjectsInRegionProg(position, radius, ref objectVisibility);
        //} else
        //{
        //    GetVisibleObjectsInRegionCorner(position, radius, ref objectVisibility);
        //}
    }

    public void GetVisibleObjectsInRegionProg(Vector3 position, float radius, ref int[] objectVisibility)
    {

        int xStartIndex = Mathf.FloorToInt((position.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
        int zStartIndex = Mathf.FloorToInt((position.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
        //Debug.Log($"{position}, {gd.gridCornerParent.transform.position}, {xStartIndex}, {zStartIndex}");
        //int[] footprints = ReadFootprintsGrid(xStartIndex, zStartIndex);
        //List<int> visibleObjects = new List<int>();
        //for (int i = 0; i < objectsInScene.Count; i++)
        //{
        //    if (footprints[i] > 0)
        //    {
        //        objectVisibility[i] = 1;
        //        visibleObjects.Add(i);
        //    }
        //}
        //Debug.Log($"old: {string.Join(',', visibleObjects)}");
        int[] footprints = ReadVisibilityGridUnit(xStartIndex, zStartIndex);
        //Debug.Log($"unit: {string.Join(", ", footprints)}");
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

    public void GetFootprintsInRegion(Vector3 position, float radius, ref int[] objectFootprints)
    {
        int xStartIndex = Mathf.FloorToInt((position.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
        int zStartIndex = Mathf.FloorToInt((position.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);

        objectFootprints = new int[objectsInScene.Count];
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
    void Update()
    {
        if (captureAndCount)
        {
            captureAndCount = false;
            foreach (bool fpCameraReady in fpcameraFinished)
            {
                if (!fpCameraReady)
                    captureAndCount = true;
            }
            if (!captureAndCount)
                Serialize();
        }
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            //Debug.Log($"{gd.numGridX}, {gd.numGridZ}");
            cameraPosIDX = 120;
            cameraPosIDZ = 439;
            ResetFootprintCount();
            //StartCoroutine(WriteFootPrintsFromCornerToGridUnit());
            //WriteFromCornerToGrid();
            //WriteGridDifferences();
            //StartCoroutine(CombineGridDifferences());
            //StartCoroutine(ShrinkGridLevelVis());
            //StartCoroutine(CombineGrids());
        }
        else if (Keyboard.current.qKey.wasPressedThisFrame)
        {
            //CreateUI();
        }
    }



    private void ResetFootprintCount()
    {
        long totalAllocatedMemory = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        Debug.Log(totalAllocatedMemory);

        // Check if memory usage has reached half of the specified limit
        if (totalAllocatedMemory >= 8589934592)
        {
            Debug.Log("garbage collection...");
            GC.Collect();
        }
        captureAndCount = true;
        //newColorCount.Clear();
        foreach (Color key in colorRecordID.Keys)
        {
            //newColorCount.Add(key, 0);
            colorCount[key] = 0;
        }
        //colorCount = newColorCount;
        fpcameraFinished = new bool[6];
        //footprintCameras.transform.position = gd.gridCornerParent.transform.GetChild(cameraPosID).position;
        Vector3 cameraPos = gd.gridCornerParent.transform.position + new Vector3(cameraPosIDX * gd.gridSize, 0, cameraPosIDZ * gd.gridSize);
        footprintCameras.transform.position = new Vector3(cameraPos.x, 1, cameraPos.z);
        foreach (Color c in colorRecordID.Keys)
        {
            objectsInScene[colorRecordID[c]].SetActive(true);
        }
    }

    private void CountFootprints(Camera cam)
    {
        if (!captureAndCount)
            return;

        if (cam.tag == "FootprintCamera" && !fpcameraFinished[int.Parse(cam.name.Split('_')[1])])
        {
            int xPosToWriteTo = 0, yPosToWriteTo = 0;
            bool updateMipMapsAutomatically = false;

            int screenWidth = cam.activeTexture.width, screenHeight = cam.activeTexture.height;
            destinationTexture = new Texture2D(screenWidth, screenHeight, TextureFormat.RGB24, false);

            RenderTexture.active = cam.targetTexture;
            regionToReadFrom = new Rect(0, 0, screenWidth, screenHeight);
            destinationTexture.ReadPixels(regionToReadFrom, xPosToWriteTo, yPosToWriteTo, updateMipMapsAutomatically);
            destinationTexture.Apply();

            for (int i = 0; i < destinationTexture.width; i++)
            {
                for (int j = 0; j < destinationTexture.height; j++)
                {
                    color = destinationTexture.GetPixel(i, j);
                    color = new Color((float)Math.Round(color.r, 2), (float)Math.Round(color.g, 2), (float)Math.Round(color.b, 2), 1);
                    if (color == cam.backgroundColor || !colorCount.ContainsKey(color))
                        continue;
                    else
                        colorCount[color]++;
                }
            }

            RenderTexture.active = null;
            Destroy(destinationTexture);
            fpcameraFinished[int.Parse(cam.name.Split('_')[1])] = true;
        }
    }

    private void Serialize()
    {
        Debug.Log($"serialized {cameraPosIDX}, {cameraPosIDZ}");
        foreach (Color c in colorRecordID.Keys)
        {
            objectsInScene[colorRecordID[c]].SetActive(colorCount[c] > 0);
        }
        WriteFootprints();
        cameraPosIDZ++;
        if (cameraPosIDZ > gd.numGridZ) //
        {
            cameraPosIDX++;
            cameraPosIDZ = 0;
        }
        if (cameraPosIDX <= gd.numGridX)
        {
            ResetFootprintCount();
        }
    }

    private void WriteFootprints()
    {
        int[] footprintCount = new int[objectsInScene.Count];
        foreach (Color c in colorRecordID.Keys)
        {
            footprintCount[colorRecordID[c]] = colorCount[c];
        }
        string filePath = "C:\\Users\\zhou1168\\VRAR\\Data\\CornerLevelFootprints\\";
        //Vector3 pos = footprintCameras.transform.position;
        string fileName = $"{filePath}{cameraPosIDX}_{cameraPosIDZ}.bin";
        byte[] bytes = ConvertIntArrayToByteArray(footprintCount);
        File.WriteAllBytes(fileName, bytes);
    }


    static byte[] ConvertIntArrayToByteArray(int[] array)
    {
        int length = array.Length;
        byte[] result = new byte[4 + length * 4];
        Buffer.BlockCopy(BitConverter.GetBytes(length), 0, result, 0, 4);
        Buffer.BlockCopy(array, 0, result, 4, length * 4);
        return result;
    }

    static byte[] ConvertIntArrayToByteArrayNoHeader(int[] ints)
    {
        byte[] bytes = new byte[ints.Length * sizeof(int)];
        Buffer.BlockCopy(ints, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private int[] ReadVisibilityGridUnit(int x, int z)
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
                //int[] visibilityInfo = ReadFootprintsGrid(gridX, gridZ);
                //List<int> ints = new List<int>();
                //for (int t = 0; t < visibilityInfo.Length; t++)
                //{
                //    if (visibilityInfo[t] > 0)
                //    {
                //        ints.Add(t);
                //    }
                //}
                cursor += 1 + visInfo[cursor];
            }
        }
        return visibleObjectsInGrid[indiGrid];
    }

    private int[] ReadFootprintGridUnit(int x, int z)
    {
        string indiGrid = $"{x}_{z}";
        if (objectFootprintsInGrid.ContainsKey(indiGrid))
        {
            return objectFootprintsInGrid[indiGrid];
        }
        string filePath = "C:\\Users\\zhou1168\\VRAR\\Data\\GridLevelFootprintsUnit\\";
        int unitX = x / (int)numInUnitX, unitZ = z / (int)numInUnitZ;
        string fileName = $"{filePath}{unitX}_{unitZ}.bin";
        if (!File.Exists(fileName))
        {
            Debug.LogError($"{fileName} does not exist");
            return new int[objectsInScene.Count];
        }
        byte[] bytes_read = File.ReadAllBytes(fileName);
        int[] visInfo = ConvertByteArrayToIntArrayNoHeader(bytes_read);

        Debug.Log($"{bytes_read.Length}, {visInfo.Length}");
        for (int k = 0; k < numInUnitX; k++)
        {
            for (int l = 0; l < numInUnitZ; l++)
            {
                int gridX = unitX * (int)numInUnitX + k, gridZ = unitZ * (int)numInUnitZ + l;
                int objectCount = VisibilityCheck.Instance.objectsInScene.Count;
                int[] newGridLevelFootprintInfo = new int[objectCount];
                Array.Copy(visInfo, objectCount * (k * numInUnitX + l), newGridLevelFootprintInfo, 0, objectCount);
                objectFootprintsInGrid.Add($"{gridX}_{gridZ}", newGridLevelFootprintInfo);
            }
        }
        return objectFootprintsInGrid[indiGrid];
    }


    private int[] ReadFootprintsDiffUnit(int fromX, int fromZ, int toX, int toZ)
    {
        //int[] indiDiffArray = ReadFootprintsDiff(fromX, fromZ, toX, toZ);
        //Debug.Log($"indi: {string.Join(", ", indiDiffArray)}");
        string indiDiff = $"{fromX}_{fromZ}_{toX}_{toZ}";
        if (diffInfoAdd.ContainsKey(indiDiff))
        {
            //Debug.Log($"unit: {string.Join(',', diffInfoAdd[indiDiff])}");
            return diffInfoAdd[indiDiff];
        }
        string filePath = "./Assets/Data/GridDiff_Unit/";
        int unitX = fromX / (int)numInUnitX, unitZ = fromZ / (int)numInUnitZ;
        string fileName = $"{filePath}{unitX}_{unitZ}.bin";
        //Debug.Log(fileName);
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
        //Debug.Log($"unit: {string.Join(',', diffInfoAdd[indiDiff])}");
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

    private int[] ReadFootprintsGrid(int x, int z)
    {
        string filePath = "C:\\Users\\zhou1168\\VRAR\\Data\\CornerLevelFootprints\\";
        string fileName = $"{filePath}{x}_{z}.bin";
        if (!File.Exists(fileName))
        {
            Debug.Log($"{fileName} does not exist");
            return new int[objectsInScene.Count];
        }
        byte[] bytes_read = File.ReadAllBytes(fileName);
        int[] result = ConvertByteArrayToIntArray(bytes_read);
        if (result.Length == 0)
        {
            Debug.Log($"length 0 {x}, {z}");
        }
        return result;
    }

    private byte[] ReadFootprintsGridBytes(int x, int z)
    {
        string filePath = "C:\\Users\\zhou1168\\VRAR\\Data\\CornerLevelFootprints\\";
        string fileName = $"{filePath}{x}_{z}.bin";
        if (!File.Exists(fileName))
        {
            Debug.Log($"{fileName} does not exist");
            return new byte[objectsInScene.Count * sizeof(int)];
        }
        return File.ReadAllBytes(fileName);
    }

    private IEnumerator WriteFootPrintsFromCornerToGridUnit()
    {
        int numUnitX = Mathf.CeilToInt(gd.numGridX / numInUnitX);
        int numUnitZ = Mathf.CeilToInt(gd.numGridZ / numInUnitZ);
        string UnitFilePath = "C:\\Users\\zhou1168\\VRAR\\Data\\GridLevelFootprintsUnit\\";
        for (int i = 0; i < numUnitX; i++)
        {
            for (int j = 0; j < numUnitZ; j++)
            {
                List<byte> infoToWrite = new List<byte>();
                for (int k = 0; k < numInUnitX; k++)
                {
                    for (int l = 0; l < numInUnitZ; l++)
                    {
                        int gridX = i * (int)numInUnitX + k, gridZ = j * (int)numInUnitZ + l;
                        int[] corner0 = ReadFootprintsGrid(gridX, gridZ);
                        int[] corner1 = ReadFootprintsGrid(gridX + 1, gridZ);
                        int[] corner2 = ReadFootprintsGrid(gridX, gridZ + 1);
                        int[] corner3 = ReadFootprintsGrid(gridX + 1, gridZ + 1);
                        int[] sum = new int[objectsInScene.Count];
                        for (int d = 0; d < objectsInScene.Count; d++)
                        {
                            sum[d] = corner0[d] + corner1[d] + corner2[d] + corner3[d];
                        }
                        infoToWrite.AddRange(ConvertIntArrayToByteArrayNoHeader(sum));
                    }
                }
                string fileName = $"{UnitFilePath}{i}_{j}.bin";
                File.WriteAllBytes(fileName, infoToWrite.ToArray());
                Debug.Log($"finished {i}_{j}");
                yield return null;
            }
        }
    }

    static int[] ConvertByteArrayToIntArray(byte[] byteArray)
    {
        int length = BitConverter.ToInt32(byteArray, 0);
        int[] array = new int[length];
        Buffer.BlockCopy(byteArray, 4, array, 0, length * 4);
        return array;
    }

    static int[] ConvertByteArrayToIntArrayNoHeader(byte[] byteArray)
    {
        int[] ints = new int[byteArray.Length / sizeof(int)];
        Buffer.BlockCopy(byteArray, 0, ints, 0, byteArray.Length);
        return ints;
    }
}



//private void TestObjectTable()
//{
//    int totalObjectNum = BitConverter.ToInt32(objectTable, sizeof(int) * 2);
//    ObjectHolder[] objectHolders = new ObjectHolder[totalObjectNum];
//    Debug.Log($"total bytes num: {BitConverter.ToInt32(objectTable, sizeof(int))}, {totalObjectNum}");
//    int cursor = sizeof(int) * 3 + sizeof(float) * 3;
//    Debug.Log(objectHolders[0]);
//    for (int i = 0; i < totalObjectNum; i++)
//    {
//        objectHolders[i] = new ObjectHolder();
//        objectHolders[i].position = new Vector3(BitConverter.ToSingle(objectTable, cursor), BitConverter.ToSingle(objectTable, cursor += sizeof(float)),
//            BitConverter.ToSingle(objectTable, cursor += sizeof(float)));
//        objectHolders[i].eulerAngles = new Vector3(BitConverter.ToSingle(objectTable, cursor += sizeof(float)), BitConverter.ToSingle(objectTable, cursor += sizeof(float)),
//            BitConverter.ToSingle(objectTable, cursor += sizeof(float)));
//        objectHolders[i].scale = new Vector3(BitConverter.ToSingle(objectTable, cursor += sizeof(float)), BitConverter.ToSingle(objectTable, cursor += sizeof(float)),
//            BitConverter.ToSingle(objectTable, cursor += sizeof(float)));
//        objectHolders[i].totalVertChunkNum = BitConverter.ToInt32(objectTable, cursor += sizeof(float));
//        objectHolders[i].totalTriChunkNum = BitConverter.ToInt32(objectTable, cursor += sizeof(int));
//        objectHolders[i].totalVertNum = BitConverter.ToInt32(objectTable, cursor += sizeof(int));
//        objectHolders[i].submeshCount = BitConverter.ToInt32(objectTable, cursor += sizeof(int));
//        cursor += sizeof(int);

//        objectHolders[i].materialNames = new string[objectHolders[i].submeshCount];
//        Transform transform = objectsInScene[i].transform;
//        for (int j = 0; j < objectHolders[i].submeshCount; j++)
//        {
//            int materialNameLength = BitConverter.ToInt32(objectTable, cursor);
//            objectHolders[i].materialNames[j] = Encoding.ASCII.GetString(objectTable, cursor += sizeof(int), materialNameLength);
//            cursor += materialNameLength;
//            Debug.Log($"{j}, {materialNameLength}, {objectHolders[i].materialNames[j]}");
//        }


//        Debug.Log($"{cursor}, {transform.position}, {objectHolders[i].position}, " +
//            $"{transform.eulerAngles}, {objectHolders[i].eulerAngles}, " +
//            $"{transform.lossyScale}, {objectHolders[i].scale}");
//    }
//}

//private void VisUpdateCluster()
//{
//    int xStartIndex = Mathf.FloorToInt((visTarget.transform.position.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
//    int zStartIndex = Mathf.FloorToInt((visTarget.transform.position.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
//    List<int[]> finalSum = new List<int[]>();

//    for (int i = -1; i < 2; i++)
//    {
//        for (int j = -1; j < 2; j++)
//        {
//            //finalSum.Add(ReadFootprints(gd.gridCornerParent.transform.GetChild((xStartIndex + i) * gd.numGridZ + zStartIndex + j).position));
//            //finalSum.Add(ReadFootprints(gd.gridCornerParent.transform.GetChild((xStartIndex + i) * gd.numGridZ + zStartIndex + j + 1).position));
//            //finalSum.Add(ReadFootprints(gd.gridCornerParent.transform.GetChild((xStartIndex + i + 1) * gd.numGridZ + zStartIndex + j).position));
//            //finalSum.Add(ReadFootprints(gd.gridCornerParent.transform.GetChild((xStartIndex + i + 1) * gd.numGridZ + zStartIndex + j + 1).position));
//            finalSum.Add(ReadFootprints((xStartIndex + i) * gd.numGridZ + zStartIndex + j));
//            finalSum.Add(ReadFootprints((xStartIndex + i) * gd.numGridZ + zStartIndex + j + 1));
//            finalSum.Add(ReadFootprints((xStartIndex + i + 1) * gd.numGridZ + zStartIndex + j));
//            finalSum.Add(ReadFootprints((xStartIndex + i + 1) * gd.numGridZ + zStartIndex + j + 1));

//            for (int k = 0; k < objectsInScene.Count; k++)
//            {
//                int active = 0;
//                for (int d = 0; d < finalSum.Count; d++)
//                {
//                    active += finalSum[d][k];
//                }
//                objectsInScene[k].SetActive(active > 0 || objectsInScene[k].tag == "Terrain" || showAll);
//            }
//        }
//    }
//}

//private void VisUpdateIndiCell()
//{
//    int xStartIndex = Mathf.FloorToInt((visTarget.transform.position.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
//    int zStartIndex = Mathf.FloorToInt((visTarget.transform.position.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
//    //int[] cornerVis1 = ReadFootprints(gd.gridCornerParent.transform.GetChild(xStartIndex * gd.numGridZ + zStartIndex).position);
//    //int[] cornerVis2 = ReadFootprints(gd.gridCornerParent.transform.GetChild(xStartIndex * gd.numGridZ + zStartIndex + 1).position);
//    //int[] cornerVis3 = ReadFootprints(gd.gridCornerParent.transform.GetChild((xStartIndex + 1) * gd.numGridZ + zStartIndex).position);
//    //int[] cornerVis4 = ReadFootprints(gd.gridCornerParent.transform.GetChild((xStartIndex + 1) * gd.numGridZ + zStartIndex + 1).position);

//    int[] cornerVis1 = ReadFootprints(xStartIndex * gd.numGridZ + zStartIndex);
//    int[] cornerVis2 = ReadFootprints(xStartIndex * gd.numGridZ + zStartIndex + 1);
//    int[] cornerVis3 = ReadFootprints((xStartIndex + 1) * gd.numGridZ + zStartIndex);
//    int[] cornerVis4 = ReadFootprints((xStartIndex + 1) * gd.numGridZ + zStartIndex + 1);

//    for (int i = 0; i < cornerVis1.Length; i++)
//    {
//        objectsInScene[i].SetActive(cornerVis1[i] + cornerVis2[i] + cornerVis3[i] + cornerVis4[i] > 0 ||
//        objectsInScene[i].tag == "Terrain" || showAll);
//    }

//    preShowAll = showAll;
//    preProgBased = progBased;
//}

//public void GetVisibleObjectsInRegionCorner(Vector3 position, float radius, ref int[] objectVisibility)
//{
//    int xStartIndex = Mathf.FloorToInt((position.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
//    int zStartIndex = Mathf.FloorToInt((position.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);

//    for (int i = -Mathf.FloorToInt(radius / gd.gridSize); i < Mathf.FloorToInt(radius / gd.gridSize) + 1; i++)
//    {
//        for (int j = -Mathf.FloorToInt(radius / gd.gridSize); j < Mathf.FloorToInt(radius / gd.gridSize) + 1; j++)
//        {
//            //        for (int i = 0; i < 1; i++)
//            //{
//            //    for (int j = 0; j < 1; j++)
//            //    {
//            if (xStartIndex + i < 0 || zStartIndex + j < 0 || xStartIndex + i > gd.numGridX - 1 || zStartIndex + j > gd.numGridZ - 1)
//            { continue; }

//            //int[] footprints = ReadFootprints((xStartIndex + i) * gd.numGridZ + zStartIndex + j);
//            int[] footprints = ReadFootprintsGrid(xStartIndex + i, zStartIndex + j);

//            for (int k = 0; k < objectsInScene.Count; k++)
//            {
//                if (footprints[k] > 0)
//                {
//                    objectVisibility[k] = 1;
//                }
//            }
//        }
//    }
//}

//public class ObjectHolder
//{
//    public Vector3 position, eulerAngles, scale;
//    public string prefabName;
//    public string[] materialNames;
//    public int totalVertChunkNum, totalTriChunkNum, totalVertNum, submeshCount;
//    public bool ifVisible, ifOwned;
//}

//private void CreateUI()
//{
//    for (int i = -10; i < 10; i++)
//    {
//        for (int j = -10; j < 10; j++)
//        {
//            Vector3 pos = visTarget.transform.position + new Vector3(i, -1, j);
//            if (Vector3.Distance(pos, visTarget.transform.position) < 10)
//            {
//                GameObject newGridCorner = Instantiate(gd.gridCornerPrefab);
//                newGridCorner.layer = 5;
//                newGridCorner.transform.localScale *= 0.1f;
//                newGridCorner.transform.SetParent(transform);
//                newGridCorner.transform.position = pos;
//            }
//        }
//    }
//    int childCount = transform.childCount;
//    for (int i = 0; i < childCount - 1; i++)
//    {
//        for (int j = i + 1; j < childCount; j++)
//        {
//            Vector3 childiPos = transform.GetChild(i).position;
//            Vector3 childjPos = transform.GetChild(j).position;
//            if (Vector3.Distance(childiPos, childjPos) <= 1.1f)
//            {
//                CreateConnectingCuboid(transform.GetChild(i), transform.GetChild(j));
//            }
//        }
//    }
//}

//void CreateConnectingCuboid(Transform cube1, Transform cube2)
//{
//    Vector3 midPoint = (cube1.position + cube2.position) / 2.0f;
//    float distance = Vector3.Distance(cube1.position, cube2.position);
//    float sideLength = cube1.transform.localScale.x;
//    GameObject cuboid = GameObject.CreatePrimitive(PrimitiveType.Cube);
//    cuboid.transform.position = midPoint;
//    cuboid.transform.localScale = new Vector3(sideLength, sideLength, 0.8f * (distance - sideLength));
//    cuboid.transform.rotation = Quaternion.FromToRotation(Vector3.forward, cube2.transform.position - cube1.transform.position);
//    cuboid.GetComponent<Renderer>().material.color = Color.white;
//    cuboid.transform.parent = transform;
//    cuboid.layer = 5;
//}

//private int[] ReadFootprintsDiff(int fromX, int fromZ, int toX, int toZ)
//{
//    string filePath = "C:\\Users\\zhou1168\\VRAR\\Data\\GridDiff\\";
//    string fileName = $"{filePath}{fromX}_{fromZ}_{toX}_{toZ}.bin";
//    if (diffInfoAdd.ContainsKey(fileName))
//    {
//        return diffInfoAdd[fileName];
//    }
//    if (!File.Exists(fileName))
//    {
//        Debug.Log($"{fileName} does not exist");
//        return new int[objectsInScene.Count];
//    }
//    byte[] bytes_read = File.ReadAllBytes(fileName);
//    int[] result = new int[bytes_read.Length / 4];
//    Buffer.BlockCopy(bytes_read, 0, result, 0, bytes_read.Length);
//    diffInfoAdd.Add(fileName, result);

//    return result;
//}

//private int[] ReadFootprints(Vector3 pos)
//{
//    string filePath = "C:\\Users\\zhou1168\\VRAR\\Visibility\\Assets\\GridData\\ObjectVisibility/";
//    string fileName = $"{filePath}{pos.x}_{pos.y}_{pos.z}.bin";
//    byte[] bytes_read = File.ReadAllBytes(fileName);
//    return ConvertByteArrayToIntArray(bytes_read);
//}

//private int[] ReadFootprints(float index)
//{
//    string filePath = "C:\\Users\\zhou1168\\VRAR\\Visibility\\Assets\\GridData\\ObjectVisibility/";
//    string fileName = $"{filePath}{index}.bin";
//    if (!File.Exists(fileName)) {
//        Debug.Log($"{fileName} does not exist");
//        return new int[objectsInScene.Count];
//    }
//    byte[] bytes_read = File.ReadAllBytes(fileName);
//    return ConvertByteArrayToIntArray(bytes_read);
//}

//private int[] ReadFootprintsGrid(int x, int z)
//{
//    string filePath = "C:\\Users\\zhou1168\\VRAR\\Data\\GridLevelVis\\";
//    string fileName = $"{filePath}{x}_{z}.bin";
//    if (!File.Exists(fileName))
//    {
//        Debug.Log($"{fileName} does not exist");
//        return new int[objectsInScene.Count];
//    }
//    byte[] bytes_read = File.ReadAllBytes(fileName);
//    return ConvertByteArrayToIntArray(bytes_read);
//}


//private void WriteFromCornerToGrid()
//{
//    for (int i = 0; i < gd.numGridX - 1; i++) {
//        for (int j = 0; j < gd.numGridZ - 1; j++)
//        {
//            int[] visibilityAtGrid = new int[objectsInScene.Count];
//            UpdateVisibilityForGrid(i, j, ref visibilityAtGrid);
//            UpdateVisibilityForGrid(i + 1, j, ref visibilityAtGrid);
//            UpdateVisibilityForGrid(i, j + 1, ref visibilityAtGrid);
//            UpdateVisibilityForGrid(i + 1, j + 1, ref visibilityAtGrid);
//            string filePath = "C:\\Users\\zhou1168\\VRAR\\Data\\GridLevelVis\\";
//            string fileName = $"{filePath}{i}_{j}.bin";
//            byte[] bytes = ConvertIntArrayToByteArray(visibilityAtGrid);
//            File.WriteAllBytes(fileName, bytes);
//            Debug.Log($"Finished {i}, {j}");
//        }
//    }
//}

//private void UpdateVisibilityForGrid(int cornerX, int cornerZ, ref int[] visibility)
//{
//    int[] visAtCorner = ReadFootprints(cornerX * gd.numGridZ + cornerZ);
//    for (int i = 0; i < visibility.Length; i++) { 
//        if (visAtCorner[i] > 0)
//        {
//            visibility[i] = 1;
//        }
//    }   
//}

//private void WriteGridDifferences()
//{
//    for (int i = 0; i < gd.numGridX - 1; i++)
//    {
//        for (int j = 0; j < gd.numGridZ - 1; j++)
//        {
//            if (i > 0)
//            {
//                WriteGridDiffPair(i, j, i - 1, j);
//            }
//            if (j > 0)
//            {
//                WriteGridDiffPair(i, j, i, j - 1);
//            }
//            if (i < gd.numGridX - 2)
//            {
//                WriteGridDiffPair(i, j, i + 1, j);
//            }
//            if (j < gd.numGridZ - 2) {
//                WriteGridDiffPair(i, j, i, j + 1);
//            }
//        }
//    }
//}

//private void WriteGridDiffPair(int fromX, int fromZ, int toX, int toZ)
//{
//    int[] visFrom = ReadFootprintsGrid(fromX, fromZ);
//    int[] visTo = ReadFootprintsGrid(toX, toZ);

//    List<int> addVis = new List<int>();
//    List<int> removeVis = new List<int>();

//    for (int i = 0; i < visFrom.Length; i++)
//    {
//        if (visFrom[i] > 0 && visTo[i] == 0)
//        {
//            removeVis.Add(i);
//        }
//        else if (visFrom[i] == 0 && visTo[i] > 0)
//        {
//            addVis.Add(i);
//        }
//    }

//    byte[] toWrite = new byte[sizeof(int) * (1 + addVis.Count + removeVis.Count)];
//    Buffer.BlockCopy(BitConverter.GetBytes(addVis.Count), 0, toWrite, 0, sizeof(int));
//    Buffer.BlockCopy(addVis.ToArray(), 0, toWrite, sizeof(int), sizeof(int) * addVis.Count);
//    Buffer.BlockCopy(removeVis.ToArray(), 0, toWrite, sizeof(int) * (1 + addVis.Count), sizeof(int) * removeVis.Count);

//    string filePath = "C:\\Users\\zhou1168\\VRAR\\Data\\GridDiff\\";
//    string fileName = $"{filePath}{fromX}_{fromZ}_{toX}_{toZ}.bin";
//    File.WriteAllBytes(fileName, toWrite);
//}

//private IEnumerator ShrinkGridLevelVis()
//{
//    int numUnitX = Mathf.CeilToInt(gd.numGridX / numInUnitX);
//    int numUnitZ = Mathf.CeilToInt(gd.numGridZ / numInUnitZ);
//    string UnitFilePath = "C:\\Users\\zhou1168\\VRAR\\Data\\GridLevelVis_Unit\\";
//    for (int i = 8; i < numUnitX; i++)
//    {
//        for (int j = 23; j < numUnitZ; j++)
//        {
//            List<int> infoToWrite = new List<int>();
//            int cursor = 0;
//            for (int k = 0; k < numInUnitX; k++)
//            {
//                for (int l = 0; l < numInUnitZ; l++)
//                {
//                    infoToWrite.Add(0);
//                    int gridX = i * (int)numInUnitX + k, gridZ = j * (int)numInUnitZ + l;
//                    int[] visibilityInfo = ReadFootprintsGrid(gridX, gridZ);
//                    for (int t = 0; t < visibilityInfo.Length; t++)
//                    {
//                        if (visibilityInfo[t] > 0)
//                        {
//                            infoToWrite.Add(t);
//                            infoToWrite[cursor]++;
//                        }
//                    }
//                    cursor += infoToWrite[cursor] + 1;
//                }
//            }
//            cursor = 0;
//            for (int k = 0; k < numInUnitX; k++)
//            {
//                for (int l = 0; l < numInUnitZ; l++)
//                {
//                    int gridX = i * (int)numInUnitX + k, gridZ = j * (int)numInUnitZ + l;
//                    int[] visibilityInfo = ReadFootprintsGrid(gridX, gridZ);
//                    List<int> ints = new List<int>();
//                    for (int t = 0; t < visibilityInfo.Length; t++)
//                    {
//                        if (visibilityInfo[t] > 0)
//                        {
//                            ints.Add(t);
//                        }
//                    }
//                    Debug.Log($"old: {string.Join(',', ints)}");
//                    int[] visibleObjects = new int[infoToWrite[cursor]];
//                    Array.Copy(infoToWrite.ToArray(), cursor + 1, visibleObjects, 0, infoToWrite[cursor]);
//                    Debug.Log($"unit: {string.Join(", ", visibleObjects)}");
//                    cursor += infoToWrite[cursor] + 1;
//                }
//            }
//            string fileName = $"{UnitFilePath}{i}_{j}.bin";
//            byte[] toWrite = ConvertIntArrayToByteArray(infoToWrite.ToArray());
//            File.WriteAllBytes(fileName, toWrite);
//            Debug.Log($"finished {i}_{j}");
//            yield return null;
//        }
//    }
//}

//private IEnumerator CombineGrids()
//{
//    int numUnitX = Mathf.CeilToInt(gd.numGridX / numInUnitX);
//    int numUnitZ = Mathf.CeilToInt(gd.numGridZ / numInUnitZ);
//    string UnitFilePath = "C:\\Users\\zhou1168\\VRAR\\Data\\GridLevelVis\\";
//    for (int i = 0; i < numUnitX; i++)
//    {
//        for (int j = 0; j < numUnitZ; j++)
//        {
//            List<int> infoToWrite = new List<int>();
//            for (int k = 0; k < numInUnitX; k++)
//            {
//                for (int l = 0; l < numInUnitZ; l++)
//                {
//                    int gridX = i * (int)numInUnitX + k, gridZ = j * (int)numInUnitZ + l;
//                    CombineGridDiffSingle(gridX, gridZ, gridX + 1, gridZ, ref infoToWrite);
//                    CombineGridDiffSingle(gridX, gridZ, gridX - 1, gridZ, ref infoToWrite);
//                    CombineGridDiffSingle(gridX, gridZ, gridX, gridZ + 1, ref infoToWrite);
//                    CombineGridDiffSingle(gridX, gridZ, gridX, gridZ - 1, ref infoToWrite);
//                }
//            }
//            string fileName = $"{UnitFilePath}{i}_{j}.bin";
//            byte[] toWrite = ConvertIntArrayToByteArray(infoToWrite.ToArray());
//            File.WriteAllBytes(fileName, toWrite);
//            Debug.Log($"finished {i}_{j}");
//            yield return null;
//        }
//    }
//}

//private IEnumerator CombineGridDifferences()
//{
//    int numUnitX = Mathf.CeilToInt(gd.numGridX / numInUnitX);
//    int numUnitZ = Mathf.CeilToInt(gd.numGridZ / numInUnitZ);
//    string UnitFilePath = "C:\\Users\\zhou1168\\VRAR\\Data\\GridDiff_Unit\\";
//    for (int i = 0; i < numUnitX; i++) {
//        for (int j = 0; j < numUnitZ; j++)
//        {
//            List<int> infoToWrite = new List<int>();
//            for (int k = 0; k < numInUnitX; k++)
//            {
//                for (int l = 0; l < numInUnitZ; l++)
//                {
//                    int gridX = i * (int) numInUnitX + k, gridZ = j * (int) numInUnitZ + l;
//                    CombineGridDiffSingle(gridX, gridZ, gridX + 1, gridZ, ref infoToWrite);
//                    CombineGridDiffSingle(gridX, gridZ, gridX - 1, gridZ, ref infoToWrite);
//                    CombineGridDiffSingle(gridX, gridZ, gridX, gridZ + 1, ref infoToWrite);
//                    CombineGridDiffSingle(gridX, gridZ, gridX, gridZ - 1, ref infoToWrite);
//                }
//            }
//            string fileName = $"{UnitFilePath}{i}_{j}.bin";
//            byte[] toWrite = ConvertIntArrayToByteArray(infoToWrite.ToArray());
//            File.WriteAllBytes(fileName, toWrite);
//            Debug.Log($"finished {i}_{j}");
//            yield return null;
//        }
//    }
//}

//private void CombineGridDiffSingle(int fromX, int fromZ, int toX, int toZ, ref List<int> infoToWrite)
//{
//    string diffFilePath = "C:\\Users\\zhou1168\\VRAR\\Data\\GridDiff\\";
//    string fileName = $"{diffFilePath}{fromX}_{fromZ}_{toX}_{toZ}.bin";
//    if (!File.Exists(fileName))
//    {
//        infoToWrite.Add(0);
//        infoToWrite.Add(0);
//        return;
//    }
//    int[] data = ReadFootprintsDiffUnit(fromX, fromZ, toX, toZ);
//    infoToWrite.Add(data[0]);
//    for (int i = 1; i < data[0] + 1; i++)
//    {
//        infoToWrite.Add(data[i]);
//    }
//    infoToWrite.Add(data.Length - 1 - data[0]);
//    for (int i = 1 + data[0]; i < data.Length; i++)
//    {
//        infoToWrite.Add(data[i]);
//    }
//}