using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

public class SplitTrGen : MonoBehaviour
{
    [Tooltip("The GameObject to slice.")]
    public GameObject targetObject;

    [Tooltip("Number of slices to create.")]
    public int sliceCount = 8;

    [Tooltip("Parent object to hold the sliced pieces.")]
    public GameObject parentObject;

    [Tooltip("Maximum vertices or triangles allowed in a single partition.")]
    public int sizeLimit = 500; // Adjust based on network packet size

    void Start()
    {
        //TestDuplicatedVertices();
        SliceAndRemoveDuplicateTriangles();
    }

    private void TestDuplicatedVertices()
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

    public void SliceAndRemoveDuplicateTriangles()
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

        // Precompute angle step
        float angleStep = 360f / sliceCount;
        Vector3 center = targetObject.transform.position;

        // Use world Vector3.up as the rotation axis
        Vector3 rotationAxis = Vector3.up;

        // Create a parent object if not provided
        if (parentObject == null)
        {
            parentObject = new GameObject($"{targetObject.name}_Slices");
            parentObject.transform.position = targetObject.transform.position;
            parentObject.transform.rotation = targetObject.transform.rotation;
        }

        // Dictionary to track which slice each vertex belongs to
        Dictionary<int, int> vertexToSlice = new Dictionary<int, int>();
            
        SortedSet<int>[] sliceVertIndices = new SortedSet<int>[sliceCount];
        SortedSet<int> initialVertices = new SortedSet<int>();
        //List<Vector3> sliceVertices = new List<Vector3>();
        //List<Vector3> sliceNormals = new List<Vector3>();
        //List<Vector4> sliceTangents = new List<Vector4>();
        //List<Color> sliceColors = new List<Color>();
        //List<Vector2> sliceUVs = new List<Vector2>();
        List<List<int>>[] sliceSubTriangles = new List<List<int>>[sliceCount];
        Dictionary<int, int> vertexMap = new Dictionary<int, int>();

        // Compute vertex ownership
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

                if (sliceV0 != sliceV1 || sliceV0 != sliceV2) { 
                    initialVertices.Add(v0);
                    initialVertices.Add(v1);
                    initialVertices.Add(v2);
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
        //Debug.Log(string.Join(',', sliceVertIndices[0]));
        //Debug.Log(string.Join(',', initialVertices));
        for (int i = 0; i < sliceCount; i++)
        {
            foreach (int ind in sliceVertIndices[i].ToList())
            {
                if (initialVertices.Contains(ind))
                {
                    sliceVertIndices[i].Remove(ind);
                }
            }
        }

        int[][] sliceVertIndexLists = sliceVertIndices.Select(x => x.ToArray()).ToArray();
        //Debug.Log(string.Join(',', sliceVertIndexLists[0]));
        
        
        //Debug.Log(uniqueVerts.Count);
        int[] initialVertArray = initialVertices.ToArray();

        int total_with_share = 0;
        int total_no_share = 0;
        int[][] checkRepeat = new int[sliceCount][];
        for (int i = 0; i < sliceCount; i++) {
            checkRepeat[i] = new int[vertices.Length];
        }
        // Slice the mesh and create GameObjects
        for (int sliceNum = 0; sliceNum < sliceCount; sliceNum++)
        {
            SortedSet<int> uniqueVerts = new SortedSet<int>();
            for (int i = 0; i < subMeshCount; i++)
            {
                for (int j = 0; j < sliceSubTriangles[sliceNum][i].Count; j++)
                {
                    uniqueVerts.Add(sliceSubTriangles[sliceNum][i][j]);
                    checkRepeat[sliceNum][sliceSubTriangles[sliceNum][i][j]] = 1;
                }
            }
            Debug.Log($"{initialVertices.Count}, {sliceVertIndexLists[sliceNum].Length}, {uniqueVerts.Count}");
            total_with_share += sliceVertIndexLists[sliceNum].Length;
            total_no_share += uniqueVerts.Count;
            Dictionary<int, int> newMeshVertMap = new Dictionary<int, int>();
            Vector3[] newMeshVerts = new Vector3[initialVertArray.Length + sliceVertIndexLists[sliceNum].Length];
            Vector3[] newMeshNorms = new Vector3[initialVertArray.Length + sliceVertIndexLists[sliceNum].Length];
            Vector4[] newMeshTengents = new Vector4[initialVertArray.Length + sliceVertIndexLists[sliceNum].Length];
            Color[] newMeshColors = new Color[initialVertArray.Length + sliceVertIndexLists[sliceNum].Length];
            Vector2[] newMeshUVs = new Vector2[initialVertArray.Length + sliceVertIndexLists[sliceNum].Length];
        
            int cursor = 0;

            for (int j = 0; j < initialVertArray.Length; j++)
            {
                newMeshVerts[cursor] = vertices[initialVertArray[j]];
                newMeshNorms[cursor] = normals[initialVertArray[j]];
                newMeshTengents[cursor] = tangents[initialVertArray[j]];
                newMeshColors[cursor] = colors[initialVertArray[j]];
                newMeshUVs[cursor] = uvs[initialVertArray[j]];
                
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

            // Create a new GameObject for the slice
            GameObject sliceObject = new GameObject($"{targetObject.name}_Slice_{sliceNum}");
            sliceObject.transform.position = targetObject.transform.position;
            sliceObject.transform.rotation = targetObject.transform.rotation;
            sliceObject.transform.SetParent(parentObject.transform);

                // Create the sliced mesh
            Mesh sliceMesh = new Mesh
            {
                vertices = newMeshVerts,
                normals = newMeshNorms,
                tangents = newMeshTengents,
                colors = newMeshColors,
                uv = newMeshUVs
            };

            //// Assign triangles to sub-meshes
            sliceMesh.subMeshCount = subMeshCount;
            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                int[] newMeshTri = new int[sliceSubTriangles[sliceNum][subMeshIndex].Count];
                for (int j = 0; j < sliceSubTriangles[sliceNum][subMeshIndex].Count; j++)
                {
                    newMeshTri[j] = newMeshVertMap[sliceSubTriangles[sliceNum][subMeshIndex][j]];
                }
                sliceMesh.SetTriangles(newMeshTri, subMeshIndex);
            }

            sliceMesh.RecalculateNormals();
            sliceMesh.RecalculateBounds();

            //// Add MeshFilter and MeshRenderer to the slice object
            MeshFilter sliceMeshFilter = sliceObject.AddComponent<MeshFilter>();
            MeshRenderer sliceMeshRenderer = sliceObject.AddComponent<MeshRenderer>();

            sliceMeshFilter.mesh = sliceMesh;
            sliceMeshRenderer.materials = meshRenderer.materials;
        }
        Debug.Log($"total with share: {initialVertArray.Length + total_with_share}, total no share: {total_no_share}");
        int[] checkRepeats = new int[vertices.Length];
        int repeatedVert = 0;
        for (int i = 0; i < sliceCount; i++)
        {
            for (int j = 0; j < vertices.Length; j++)
            {
                if (checkRepeats[j] > 0 && checkRepeat[i][j] > 0)
                    repeatedVert++;
                if (checkRepeat[i][j] > 0)
                {
                    checkRepeats[j] = 1;
                }
            }
        }
        Debug.Log(repeatedVert);
    }

    private static int AddVertexToSlice(
        int originalIndex,
        List<Vector3> sliceVertices,
        List<Vector3> sliceNormals,
        List<Vector4> sliceTangents,
        List<Color> sliceColors,
        List<Vector2> sliceUVs,
        Dictionary<int, int> vertexMap,
        Vector3[] originalVertices,
        Vector3[] originalNormals,
        Vector4[] originalTangents,
        Color[] originalColors,
        Vector2[] originalUVs)
    {
        if (!vertexMap.ContainsKey(originalIndex))
        {
            vertexMap[originalIndex] = sliceVertices.Count;
            sliceVertices.Add(originalVertices[originalIndex]);
            sliceNormals.Add(originalNormals[originalIndex]);
            sliceTangents.Add(originalTangents[originalIndex]);
            sliceColors.Add(originalColors.Length > 0 ? originalColors[originalIndex] : Color.white);
            sliceUVs.Add(originalUVs[originalIndex]);
        }
        return vertexMap[originalIndex];
    }
}
