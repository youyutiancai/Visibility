#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System;
using System.Collections;
using System.Drawing.Imaging;
using Random = UnityEngine.Random;
using static log4net.Appender.ColoredConsoleAppender;

public class PrefabCreator
{
    [MenuItem("Tools/Clear Progress Bar")]
    public static void ClearProgressBar()
    {
        EditorUtility.ClearProgressBar();
        UnityEngine.Debug.Log("Progress bar cleared.");
    }

    [MenuItem("Tools/Create And Save New GameObject Prefab")]
    public static void CreateAndSavePrefab()
    {
        GameObject root = GameObject.Find("THE WHOLE CITY"); // Assuming "Root" is the name of your scene's root object
        if (root == null)
        {
            Debug.LogError("Root GameObject not found in the scene.");
            return;
        }
        List<GameObject> objectsInScene = new List<GameObject>();
        AddAllObjects(root.transform, ref objectsInScene);
        Debug.Log($"Found {objectsInScene.Count} objects in the scene.");

        Dictionary<int, List<byte[]>> objectChunksVTGrouped = new Dictionary<int, List<byte[]>>();
        LoadAllChunks("Assets/Data/objectChunksGrouped", ref objectChunksVTGrouped);
        Debug.Log($"Loaded {objectChunksVTGrouped.Count} objects from grouped chunks. The first has {objectChunksVTGrouped[0].Count} chunks.");

        GameObject chunkObjectsRoot = GameObject.Find("ChunkObjectsRoot");
        CopyObjectsPerChunk(objectsInScene, objectChunksVTGrouped, chunkObjectsRoot);
    }

    private static void AddAllObjects(Transform child, ref List<GameObject> objectsInScene)
    {
        if (child.gameObject.activeSelf == false)
        {
            return;
        }
        MeshRenderer mr = child.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            objectsInScene.Add(child.gameObject);
        }
        for (int i = 0; i < child.childCount; i++)
        {
            AddAllObjects(child.GetChild(i), ref objectsInScene);
        }
    }

    private static void LoadAllChunks(string chunksDirectory, ref Dictionary<int, List<byte[]>> chunkDic)
    {
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
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading chunks: {e.Message}");
        }
    }

    private static List<byte[]> LoadChunksFromFile(string objectFilePath)
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


    private static void CopyObjectsPerChunk(List<GameObject> objectsInScene, Dictionary<int, List<byte[]>> objectChunksVTGrouped, GameObject chunkObjectsRoot)
    {
        int numCopy = 500000;
        float colorMax = Mathf.Pow(100, 3);
        HashSet<int> colorsHash = new HashSet<int>();
        while (colorsHash.Count < numCopy)
        {
            colorsHash.Add((int)Mathf.Ceil(Random.Range(0, colorMax)));
        }
        List<int> colors = new List<int>(colorsHash);
        int colorIndex = -1;
        Debug.Log($"Generated {colors.Count} unique colors for objects.");
        int startIndex = 0, endIndex = objectsInScene.Count;
        for (int l = startIndex; l < endIndex; l++)
        {
            int objectID = l;
            GameObject original = objectsInScene[objectID];
            List<byte[]> chunks = objectChunksVTGrouped[objectID];
            Mesh originalMesh = original.GetComponent<MeshFilter>()?.sharedMesh;
            if (originalMesh == null)
            {
                Debug.LogError("Original object has no mesh.");
            }

            int vertexCapacity = originalMesh.vertexCount;
            int submeshCount = originalMesh.subMeshCount;
            // Rebuild mesh
            Mesh newMesh = new Mesh();
            newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            newMesh.vertices = originalMesh.vertices;
            newMesh.normals = originalMesh.normals;
            newMesh.subMeshCount = chunks.Count;
            Material[] newMaterials = new Material[chunks.Count];
            Material[] oldMaterials = original.GetComponent<MeshRenderer>().sharedMaterials;

            //foreach (var chunk in chunks)
            for (int k = 0; k < chunks.Count; k++)
            {
                //EditorUtility.DisplayProgressBar($"Processing Mesh {l + 1}/{objectsInScene.Count}", $"Processing Chunk {k + 1}/{chunks.Count})",
                //    (float)(l - startIndex) / (endIndex - startIndex)
                //);
                byte[] chunk = chunks[k];
                int offset = 0;
                char type = BitConverter.ToChar(chunk, offset); offset += sizeof(char);
                objectID = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);
                int chunkID = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);
                int submeshID = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);
                int vertexCount = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);
                offset += sizeof(int) * 7 * vertexCount;

                int triangleCount = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);
                int[] triangles = new int[triangleCount];
                for (int i = 0; i < triangleCount; i++)
                {
                    int tri = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);
                    triangles[i] = tri;
                }
                newMesh.SetTriangles(triangles, k);
                if (oldMaterials[submeshID] == null)
                {
                    Debug.Log($"Material for submesh {submeshID} is null in object {objectID}.");
                    continue;
                }
                //int mode = oldMaterials[submeshID].GetInt("_Mode");
                //Material newMaterial = new Material(Shader.Find("Custom/Cover_check"));
                //if (mode == 1)
                //{
                //    newMaterial.SetFloat("_Cutoff", oldMaterials[submeshID].GetFloat("_Cutoff"));
                //}
                //else
                //{
                //    newMaterial.SetFloat("_Cutoff", 0);
                //}
                //newMaterial.SetTexture("_MainTex", oldMaterials[submeshID].GetTexture("_MainTex"));
                //newMaterial.SetInt("_IsMorph", 0);
                //newMaterial.SetFloat("_IsColor", 1);
                //colorIndex++;
                //Color newColor = new Color(colors[colorIndex] % 100 / 100f, colors[colorIndex] / 100 % 100 / 100f,
                //    colors[colorIndex] / 100 / 100 % 100 / 100f, 1);
                //newMaterial.SetColor("_Color", newColor);
                //newMaterials[k] = newMaterial;
                int mode = oldMaterials[submeshID].GetInt("_Mode");
                Material newMaterial = new Material(Shader.Find("Custom/Pure_color"));
                if (mode == 1)
                {
                    newMaterial.SetFloat("_Cutoff", oldMaterials[submeshID].GetFloat("_Cutoff"));
                }
                else
                {
                    newMaterial.SetFloat("_Cutoff", 0);
                }
                colorIndex++;
                Color newColor = new Color(colors[colorIndex] % 100 / 100f, colors[colorIndex] / 100 % 100 / 100f,
                    colors[colorIndex] / 100 / 100 % 100 / 100f, 1);
                newMaterial.SetColor("_Color", newColor);
                newMaterials[k] = newMaterial;
                //newMaterials[k] = oldMaterials[submeshID];
            }
            newMesh.RecalculateBounds();
            GameObject copy = UnityEngine.Object.Instantiate(original);
            copy.name = $"Object_{objectID}";
            copy.transform.SetParent(chunkObjectsRoot.transform, false);
            copy.transform.position = original.transform.position;
            copy.transform.rotation = original.transform.rotation;
            copy.transform.localScale = original.transform.localScale;

            MeshFilter filter = copy.GetComponent<MeshFilter>();
            if (filter == null) filter = copy.AddComponent<MeshFilter>();
            filter.sharedMesh = newMesh;

            MeshRenderer renderer = copy.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = copy.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = newMaterials;
            //SaveMeshAndPrefab(copy, objectID);
            copy.SetActive(false);
        }
        EditorUtility.ClearProgressBar();
    }

    //private static void CopyObjectsPerChunk(List<GameObject> objectsInScene, Dictionary<int, List<byte[]>> objectChunksVTGrouped, GameObject chunkObjectsRoot)
    //{
    //    int startIndex = 0, endIndex = 200;
    //    for (int l = startIndex; l < endIndex; l++)
    //    {
    //        int objectID = l;
    //        Transform parentTransform = chunkObjectsRoot.transform.Find($"Object_{objectID}");
    //        if (parentTransform == null)
    //        {
    //            GameObject childObject = new GameObject($"Object_{objectID}");
    //            childObject.transform.SetParent(chunkObjectsRoot.transform, false);
    //            parentTransform = childObject.transform;
    //        }
    //        GameObject original = objectsInScene[objectID];
    //        List<byte[]> chunks = objectChunksVTGrouped[objectID];
    //        Mesh originalMesh = original.GetComponent<MeshFilter>()?.sharedMesh;
    //        if (originalMesh == null)
    //        {
    //            Debug.LogError("Original object has no mesh.");
    //        }

    //        int vertexCapacity = originalMesh.vertexCount;
    //        int submeshCount = originalMesh.subMeshCount;

    //        Vector3[] vertices = new Vector3[vertexCapacity];
    //        Vector3[] normals = new Vector3[vertexCapacity];
    //        List<int>[] submeshTriangles = new List<int>[submeshCount];
    //        for (int i = 0; i < submeshCount; i++)
    //            submeshTriangles[i] = new List<int>();

    //        //foreach (var chunk in chunks)
    //        for (int k = 0; k < chunks.Count; k++)
    //        {
    //            EditorUtility.DisplayProgressBar($"Processing Mesh {l + 1}/{objectsInScene.Count}", $"Processing Chunk {k + 1}/{chunks.Count})",
    //                (float)(l - startIndex) / (endIndex - startIndex)
    //            );
    //            byte[] chunk = chunks[k];
    //            int offset = 0;
    //            char type = BitConverter.ToChar(chunk, offset); offset += sizeof(char);
    //            objectID = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);
    //            int chunkID = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);
    //            int submeshID = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);

    //            int vertexCount = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);
    //            for (int i = 0; i < vertexCount; i++)
    //            {
    //                int index = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);

    //                // Skip decoding if already filled
    //                if (vertices[index] != Vector3.zero)
    //                {
    //                    offset += 6 * sizeof(float); // skip position + normal
    //                    continue;
    //                }

    //                float x = BitConverter.ToSingle(chunk, offset); offset += sizeof(float);
    //                float y = BitConverter.ToSingle(chunk, offset); offset += sizeof(float);
    //                float z = BitConverter.ToSingle(chunk, offset); offset += sizeof(float);
    //                vertices[index] = new Vector3(x, y, z);

    //                float nx = BitConverter.ToSingle(chunk, offset); offset += sizeof(float);
    //                float ny = BitConverter.ToSingle(chunk, offset); offset += sizeof(float);
    //                float nz = BitConverter.ToSingle(chunk, offset); offset += sizeof(float);
    //                normals[index] = new Vector3(nx, ny, nz);
    //            }


    //            int triangleCount = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);
    //            for (int i = 0; i < triangleCount; i++)
    //            {
    //                int tri = BitConverter.ToInt32(chunk, offset); offset += sizeof(int);
    //                submeshTriangles[submeshID].Add(tri);
    //            }

    //            // Rebuild mesh
    //            Mesh newMesh = new Mesh();
    //            newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
    //            newMesh.vertices = vertices;
    //            newMesh.normals = normals;
    //            newMesh.subMeshCount = submeshCount;

    //            for (int i = 0; i < submeshCount; i++)
    //                newMesh.SetTriangles(submeshTriangles[i], i);

    //            newMesh.RecalculateBounds();

    //            // Create new object
    //            GameObject copy = UnityEngine.Object.Instantiate(original);
    //            copy.name = $"chunk_{k}";
    //            copy.transform.SetParent(parentTransform, false);
    //            copy.transform.position = original.transform.position;
    //            copy.transform.rotation = original.transform.rotation;
    //            copy.transform.localScale = original.transform.localScale;

    //            MeshFilter filter = copy.GetComponent<MeshFilter>();
    //            if (filter == null) filter = copy.AddComponent<MeshFilter>();
    //            filter.sharedMesh = newMesh;

    //            MeshRenderer renderer = copy.GetComponent<MeshRenderer>();
    //            if (renderer == null) renderer = copy.AddComponent<MeshRenderer>();
    //            renderer.sharedMaterials = original.GetComponent<MeshRenderer>().sharedMaterials;
    //            RemoveUnusedVertices(copy);
    //            SaveMeshAndPrefab(copy, objectID, k);
    //        }
    //        parentTransform.gameObject.SetActive(false);
    //    }
    //    EditorUtility.ClearProgressBar();
    //}

    public static void RemoveUnusedVertices(GameObject go)
    {
        if (go == null)
        {
            Debug.LogError("GameObject is null.");
            EditorUtility.ClearProgressBar();
            return;
        }

        MeshFilter mf = go.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogError("No MeshFilter or mesh found.");
            EditorUtility.ClearProgressBar();
            return;
        }

        Mesh originalMesh = mf.sharedMesh;
        Vector3[] vertices = originalMesh.vertices;
        Vector3[] normals = originalMesh.normals;
        int submeshCount = originalMesh.subMeshCount;

        // Prepare new lists
        List<Vector3> compactedVertices = new List<Vector3>();
        List<Vector3> compactedNormals = new List<Vector3>();
        List<int>[] compactedSubmeshTriangles = new List<int>[submeshCount];
        Dictionary<int, int> oldToNewIndex = new Dictionary<int, int>();

        for (int i = 0; i < submeshCount; i++)
        {
            compactedSubmeshTriangles[i] = new List<int>();
            int[] triangles = originalMesh.GetTriangles(i);

            foreach (int oldIndex in triangles)
            {
                if (!oldToNewIndex.ContainsKey(oldIndex))
                {
                    int newIndex = compactedVertices.Count;
                    oldToNewIndex[oldIndex] = newIndex;
                    compactedVertices.Add(vertices[oldIndex]);
                    if (normals != null && normals.Length == vertices.Length)
                        compactedNormals.Add(normals[oldIndex]);
                }
                compactedSubmeshTriangles[i].Add(oldToNewIndex[oldIndex]);
            }
        }

        Mesh optimizedMesh = new Mesh();
        optimizedMesh.indexFormat = originalMesh.indexFormat; 
        optimizedMesh.SetVertices(compactedVertices);
        if (compactedNormals.Count == compactedVertices.Count)
            optimizedMesh.SetNormals(compactedNormals);

        optimizedMesh.subMeshCount = submeshCount;
        for (int i = 0; i < submeshCount; i++)
            optimizedMesh.SetTriangles(compactedSubmeshTriangles[i], i);

        optimizedMesh.RecalculateBounds();
        mf.sharedMesh = optimizedMesh;
    }

    private static void SaveMeshAndPrefab(GameObject obj, int objectID)
    {
        string meshFolder = "Assets/Resources/Meshes";
        string prefabFolder = "Assets/Resources/Prefabs";

        Directory.CreateDirectory(meshFolder);
        Directory.CreateDirectory(prefabFolder);

        MeshFilter mf = obj.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            string meshAssetPath = Path.Combine(meshFolder, $"mesh_{objectID}.asset");
            Mesh meshCopy = UnityEngine.Object.Instantiate(mf.sharedMesh);
            AssetDatabase.CreateAsset(meshCopy, meshAssetPath);
            AssetDatabase.SaveAssets();
            mf.sharedMesh = meshCopy;
        }

        string prefabPath = Path.Combine(prefabFolder, $"chunk_{objectID}.prefab");
        PrefabUtility.SaveAsPrefabAsset(obj, prefabPath);
        Debug.Log($"Saved prefab to {prefabPath}");
    }
}
#endif
