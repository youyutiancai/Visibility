using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
public class ProgressiveMeshMultiPath : MonoBehaviour
{
    public GameObject targetObject;
    public GameObject parentObject;
    public int sliceCount = 8;
    public bool ifSimplify;

    [Range(0, 30000)] // Slider in the Inspector
    public int targetStep = 0; // Current step of simplification/recovery
    [HideInInspector]
    public int[] minValue, maxValue;
    public int[] targetSteps = new int[8];

    private Vector3[] initialVertices, initialNormals;
    private Vector4[] initialTangents;
    private Color[] initialColors;
    private Vector2[] initialUVs;

    private SingleSlice[] slices;

    void OnValidate()
    {
        // Ensure arrays are initialized
        if (minValue == null || minValue.Length != sliceCount)
            minValue = new int[sliceCount];

        if (maxValue == null || maxValue.Length != sliceCount)
            maxValue = new int[sliceCount];

        if (targetSteps == null || targetSteps.Length != sliceCount)
            targetSteps = new int[sliceCount];

        // Ensure values stay within the dynamically changing range
        for (int i = 0; i < sliceCount; i++)
        {
            minValue[i] = 0;
            maxValue[i] = 20000;
            targetSteps[i] = Mathf.Clamp(targetSteps[i], minValue[i], maxValue[i]); // Clamp targetSteps
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (ifSimplify)
        {
            SimplifyObject();
        }
        DivideMeshIntoSubMeshes();
    }

    private void SimplifyObject()
    {
        MeshFilter meshFilter = targetObject.GetComponent<MeshFilter>();
        Mesh sourceMesh = meshFilter.mesh;
        Vector3[] vertices = sourceMesh.vertices;
        Vector3[] normals = sourceMesh.normals;
        int vertexRemoved = 0;
        SortedSet<int> newVerticesIndSorted = new SortedSet<int>();
        for (int submeshInd = 0; submeshInd < sourceMesh.subMeshCount; submeshInd++)
        {
            int[] triangles = sourceMesh.GetTriangles(submeshInd);
            SortedSet<int> subMeshVertsSorted = new SortedSet<int>();
            for (int subMeshVertInd = 0; subMeshVertInd < triangles.Length; subMeshVertInd++)
            {
                subMeshVertsSorted.Add(triangles[subMeshVertInd]);
            }
            int[] subMeshVerts = subMeshVertsSorted.ToArray();
            Dictionary<int, int> repeatedVertices = new Dictionary<int, int>();
            //Dictionary<int, List<int>> vertexMerge = new Dictionary<int, List<int>>();

            for (int i = subMeshVerts.Length - 1; i >= 0; i--)
            {
                for (int j = i - 1; j >= 0; j--)
                {
                    int largerV = subMeshVerts[i];
                    int smallerV = subMeshVerts[j];
                    if (Vector3.Distance(vertices[largerV], vertices[smallerV]) < 0.01f)
                    {
                        if (!repeatedVertices.ContainsKey(smallerV))
                        {
                            repeatedVertices.Add(smallerV, largerV);
                            vertexRemoved++;
                        }
                        //if (!vertexMerge.ContainsKey(largerV))
                        //{
                        //    vertexMerge.Add(largerV, new List<int>());
                        //}
                        //vertexMerge[largerV].Add(smallerV);
                        //Debug.Log($"{submeshInd}, {largerV}, {smallerV}, {vertices[largerV]}, {vertices[smallerV]}," +
                        //    $"{normals[largerV]}, {normals[smallerV]}");
                    }
                }
            }
            //for (int i = 0; i < vertices.Length; i++)
            //{
            //    if (vertexMerge.ContainsKey(i))
            //    {
            //        Vector3 newNorm = normals[i];
            //        List<int> samePosVerts = vertexMerge[i];
            //        for (int j = 0; j < samePosVerts.Count; j++)
            //        {
            //            newNorm += normals[samePosVerts[j]];
            //        }
            //        normals[i] = newNorm.normalized;
            //    }
            //}
            ////sourceMesh.normals = normals;
            int[] newMeshTri = sourceMesh.GetTriangles(submeshInd);
            for (int i = 0; i < newMeshTri.Length; i++)
            {
                if (repeatedVertices.ContainsKey(newMeshTri[i]))
                {
                    newMeshTri[i] = repeatedVertices[newMeshTri[i]];
                }
                newVerticesIndSorted.Add(newMeshTri[i]);
            }
            sourceMesh.SetTriangles(newMeshTri, submeshInd);

        }
        Debug.Log($"vertex removed {vertexRemoved}, {newVerticesIndSorted.Count}");
        Vector3[] oldVertices = sourceMesh.vertices;
        Vector3[] newVertices = new Vector3[newVerticesIndSorted.Count];
        Vector3[] oldNormals = sourceMesh.normals;
        Vector3[] newNormals = new Vector3[newVerticesIndSorted.Count];
        Vector4[] oldTangents = sourceMesh.tangents;
        Vector4[] newTangents = new Vector4[newVerticesIndSorted.Count];
        Color[] oldColors = sourceMesh.colors;
        Color[] newColors = new Color[newVerticesIndSorted.Count];
        Vector2[] oldUVs = sourceMesh.uv;
        Vector2[] newUVs = new Vector2[newVerticesIndSorted.Count];

        int[] newVerticesInd = newVerticesIndSorted.ToArray();
        Dictionary<int, int> vertexMapping = new Dictionary<int, int>();

        for (int i = 0; i < newVerticesIndSorted.Count; i++)
        {
            newVertices[i] = oldVertices[newVerticesInd[i]];
            newNormals[i] = oldNormals[newVerticesInd[i]];
            newTangents[i] = oldTangents[newVerticesInd[i]];
            newColors[i] = oldColors[newVerticesInd[i]];
            newUVs[i] = oldUVs[newVerticesInd[i]];
            vertexMapping.Add(newVerticesInd[i], i);
        }

        for (int subMeshInd = 0; subMeshInd < sourceMesh.subMeshCount; subMeshInd++)
        {
            int[] subTriangles = sourceMesh.GetTriangles(subMeshInd);
            for (int i = 0; i < subTriangles.Length; i++)
            {
                subTriangles[i] = vertexMapping[subTriangles[i]];
            }
            sourceMesh.SetTriangles(subTriangles, subMeshInd);
        }

        sourceMesh.vertices = newVertices;
        sourceMesh.normals = newNormals;
        sourceMesh.tangents = newTangents;
        sourceMesh.colors = newColors;
        sourceMesh.uv = newUVs;
        sourceMesh.RecalculateNormals();
        //sourceMesh.RecalculateBounds();

        meshFilter.mesh = sourceMesh;
    }

    public void DivideMeshIntoSubMeshes()
    {
        if (targetObject == null)
        {
            Debug.LogError("Target object is not set.");
            return;
        }

        if (sliceCount <= 0)
        {
            Debug.LogError("Slice count must be greater than 0.");
            return;
        }

        MeshFilter meshFilter = targetObject.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = targetObject.GetComponent<MeshRenderer>();

        if (meshFilter == null || meshRenderer == null)
        {
            Debug.LogError("Target object must have MeshFilter and MeshRenderer components.");
            return;
        }

        Mesh sourceMesh = meshFilter.mesh;
        Vector3[] vertices = sourceMesh.vertices;
        Vector3[] normals = sourceMesh.normals;
        Vector4[] tangents = sourceMesh.tangents;
        Color[] colors = sourceMesh.colors;
        Vector2[] uvs = sourceMesh.uv;
        int subMeshCount = sourceMesh.subMeshCount;

        float angleStep = 360f / sliceCount;
        Vector3 center = targetObject.transform.position;
        Vector3 rotationAxis = Vector3.up;

        Dictionary<int, int> vertexToSlice = new Dictionary<int, int>();

        SortedSet<int>[] sliceVertIndices = new SortedSet<int>[sliceCount];
        SortedSet<int> initialVertsSorted = new SortedSet<int>();
        List<List<int>>[] sliceSubTriangles = new List<List<int>>[sliceCount];
        Dictionary<int, int> vertexMap = new Dictionary<int, int>();

        for (int v = 0; v < vertices.Length; v++)
        {
            Vector3 worldVertex = targetObject.transform.TransformPoint(vertices[v]);
            Vector3 dir = worldVertex - center;
            Vector3 projDir = Vector3.ProjectOnPlane(dir, rotationAxis);
            float angle = Vector3.SignedAngle(Vector3.right, projDir, rotationAxis);
            angle = (angle + 360f) % 360f;

            int sliceIndex = Mathf.FloorToInt(angle / angleStep);
            vertexToSlice[v] = sliceIndex;
        }

        for (int i = 0; i < sliceCount; i++)
        {
            sliceVertIndices[i] = new SortedSet<int>();
            sliceSubTriangles[i] = new List<List<int>>();
            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                sliceSubTriangles[i].Add(new List<int>());
            }
        }

        for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
        {
            int[] triangles = sourceMesh.GetTriangles(subMeshIndex);

            for (int t = 0; t < triangles.Length; t += 3)
            {
                int v0 = triangles[t];
                int v1 = triangles[t + 1];
                int v2 = triangles[t + 2];

                int sliceV0 = vertexToSlice[v0];
                int sliceV1 = vertexToSlice[v1];
                int sliceV2 = vertexToSlice[v2];

                if (sliceV0 != sliceV1 || sliceV0 != sliceV2)
                {
                    initialVertsSorted.Add(v0);
                    initialVertsSorted.Add(v1);
                    initialVertsSorted.Add(v2);
                }

                int lowestSlice = Mathf.Min(sliceV0, sliceV1, sliceV2);

                sliceVertIndices[lowestSlice].Add(v0);
                sliceVertIndices[lowestSlice].Add(v1);
                sliceVertIndices[lowestSlice].Add(v2);

                sliceSubTriangles[lowestSlice][subMeshIndex].Add(v0);
                sliceSubTriangles[lowestSlice][subMeshIndex].Add(v1);
                sliceSubTriangles[lowestSlice][subMeshIndex].Add(v2);
            }
        }

        int[][] slicesContainVerts = new int[sliceCount][];
        for (int i = 0; i < sliceCount; i++)
        {
            slicesContainVerts[i] = new int[vertices.Length];
        }

        for (int sliceNum = 0; sliceNum < sliceCount; sliceNum++)
        {
            for (int i = 0; i < subMeshCount; i++)
            {
                for (int j = 0; j < sliceSubTriangles[sliceNum][i].Count; j++)
                {
                    slicesContainVerts[sliceNum][sliceSubTriangles[sliceNum][i][j]] = 1;
                }
            }
        }

        int[] numVertsContains = new int[vertices.Length];
        for (int i = 0; i < sliceCount; i++)
        {
            for (int j = 0; j < vertices.Length; j++)
            {
                if (slicesContainVerts[i][j] > 0)
                {
                    numVertsContains[j] += 1;
                }
            }
        }
        foreach(int ind in initialVertsSorted.ToList())
        {
            if (numVertsContains[ind] <= 1)
            {
                initialVertsSorted.Remove(ind);
            }
        }

        for (int i = 0; i < sliceCount; i++)
        {
            foreach (int ind in sliceVertIndices[i].ToList())
            {
                if (initialVertsSorted.Contains(ind))
                {
                    sliceVertIndices[i].Remove(ind);
                }
            }
        }

        int[][] sliceVertIndexLists = sliceVertIndices.Select(x => x.ToArray()).ToArray();
        int[] initialVertArray = initialVertsSorted.ToArray();

        initialVertices = new Vector3[initialVertArray.Length];
        initialNormals = new Vector3[initialVertArray.Length];
        initialTangents = new Vector4[initialVertArray.Length];
        initialColors = new Color[initialVertArray.Length];
        initialUVs = new Vector2[initialVertArray.Length];
        for (int i = 0; i < initialVertArray.Length; i++)
        {
            initialVertices[i] = vertices[initialVertArray[i]];
            initialNormals[i] = normals[initialVertArray[i]];
            initialTangents[i] = tangents[initialVertArray[i]];
            initialColors[i] = colors[initialVertArray[i]];
            initialUVs[i] = uvs[initialVertArray[i]];
        }

        //int total_with_share = 0;
        //int total_no_share = 0;
        //int[][] checkRepeat = new int[sliceCount][];
        //for (int i = 0; i < sliceCount; i++)
        //{
        //    checkRepeat[i] = new int[vertices.Length];
        //}

        //for (int sliceNum = 0; sliceNum < sliceCount; sliceNum++)
        //{
        //    SortedSet<int> uniqueVerts = new SortedSet<int>();
        //    for (int i = 0; i < subMeshCount; i++)
        //    {
        //        for (int j = 0; j < sliceSubTriangles[sliceNum][i].Count; j++)
        //        {
        //            uniqueVerts.Add(sliceSubTriangles[sliceNum][i][j]);
        //            checkRepeat[sliceNum][sliceSubTriangles[sliceNum][i][j]] = 1;
        //        }
        //    }
        //    Debug.Log($"{initialVertices.Count}, {sliceVertIndexLists[sliceNum].Length}, {uniqueVerts.Count}");
        //    total_with_share += sliceVertIndexLists[sliceNum].Length;
        //    total_no_share += uniqueVerts.Count;
        //}
        //Debug.Log($"total with share: {initialVertArray.Length + total_with_share}, total no share: {total_no_share}");

        //int[] checkRepeats = new int[vertices.Length];
        //for (int j = 0; j < vertices.Length; j++)
        //{
        //    for (int i = 0; i < sliceCount; i++)
        //    {
        //        if (checkRepeat[i][j] > 0)
        //        {
        //            checkRepeats[j] += 1;
        //        }
        //    }
        //}
        //int repeatedVerts = 0;
        //for (int i = 0; i < vertices.Length; i++)
        //{
        //    if (checkRepeats[i] > 4)
        //    {
        //        repeatedVerts++;
        //    }
        //}
        //Debug.Log(repeatedVerts);

        slices = new SingleSlice[sliceCount];
        for (int sliceNum = 0; sliceNum < sliceCount; sliceNum++)
        {
            Dictionary<int, int> newMeshVertMap = new Dictionary<int, int>();
            Vector3[] newMeshVerts = new Vector3[initialVertArray.Length + sliceVertIndexLists[sliceNum].Length];
            Vector3[] newMeshNorms = new Vector3[initialVertArray.Length + sliceVertIndexLists[sliceNum].Length];
            Vector4[] newMeshTengents = new Vector4[initialVertArray.Length + sliceVertIndexLists[sliceNum].Length];
            Color[] newMeshColors = new Color[initialVertArray.Length + sliceVertIndexLists[sliceNum].Length];
            Vector2[] newMeshUVs = new Vector2[initialVertArray.Length + sliceVertIndexLists[sliceNum].Length];

            Array.Copy(initialVertices, newMeshVerts, initialVertices.Length);
            Array.Copy(initialNormals, newMeshNorms, initialNormals.Length);
            Array.Copy(initialTangents, newMeshTengents, initialTangents.Length);
            Array.Copy(initialColors, newMeshColors, initialColors.Length);
            Array.Copy(initialUVs, newMeshUVs, initialUVs.Length);

            int cursor = 0;

            for (int j = 0; j < initialVertArray.Length; j++)
            {
                newMeshVertMap[initialVertArray[j]] = cursor;
                cursor++;
            }

            for (int j = 0; j < sliceVertIndexLists[sliceNum].Length; j++)
            {
                newMeshVerts[cursor] = vertices[sliceVertIndexLists[sliceNum][j]];
                newMeshNorms[cursor] = normals[sliceVertIndexLists[sliceNum][j]];
                newMeshTengents[cursor] = tangents[sliceVertIndexLists[sliceNum][j]];
                newMeshColors[cursor] = colors[sliceVertIndexLists[sliceNum][j]];
                newMeshUVs[cursor] = uvs[sliceVertIndexLists[sliceNum][j]];
                if (newMeshVertMap.ContainsKey(sliceVertIndexLists[sliceNum][j]))
                    Debug.Log($"has key {sliceVertIndexLists[sliceNum][j]}");
                newMeshVertMap[sliceVertIndexLists[sliceNum][j]] = cursor;
                cursor++;
            }

            List<int>[] sliceSubTrianglesRemap = new List<int>[subMeshCount];
            for (int subMeshInd = 0; subMeshInd < subMeshCount; subMeshInd++)
            {
                sliceSubTrianglesRemap[subMeshInd] = new List<int>();
                for (int j = 0; j < sliceSubTriangles[sliceNum][subMeshInd].Count; j++)
                {
                    sliceSubTrianglesRemap[subMeshInd].Add(newMeshVertMap[sliceSubTriangles[sliceNum][subMeshInd][j]]);
                }
            }

            GameObject sliceObject = new GameObject($"{targetObject.name}_Slice_{sliceNum}");
            sliceObject.transform.position = targetObject.transform.position;
            sliceObject.transform.rotation = targetObject.transform.rotation;
            sliceObject.transform.SetParent(parentObject.transform);
            MeshFilter sliceMeshFilter = sliceObject.AddComponent<MeshFilter>();
            MeshRenderer sliceMeshRenderer = sliceObject.AddComponent<MeshRenderer>();
            sliceMeshRenderer.materials = meshRenderer.materials;

            slices[sliceNum] = new SingleSlice(newMeshVerts, newMeshNorms, newMeshTengents, newMeshColors, newMeshUVs, initialVertices.Length,
                sliceSubTrianglesRemap, sliceObject, ifSimplify);
        }
    }
    void Update()
    {
        if (targetStep < initialVertices.Length)
        {
            targetStep = initialVertices.Length;
        }

        for (int i = 0; i < sliceCount; i++)
        {
            minValue[i] = initialVertices.Length;
            maxValue[i] = slices[i].vertices.Length - 1;
            targetSteps[i] = Mathf.Clamp(targetSteps[i], minValue[i], maxValue[i]);
            slices[i].targetStep = targetSteps[i];
            slices[i].UpdateStep();
        }
    }
}
