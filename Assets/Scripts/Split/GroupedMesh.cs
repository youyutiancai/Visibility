using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GroupedMesh : MeshVariant
{
    public override List<byte[]> RequestChunks(int objectID, int chunkSize)
    {
        GameObject obj = VisibilityCheck.Instance.objectsInScene[objectID];
        List<byte[]> chunks = new List<byte[]>();
        EncodeGroupedChunks(obj, objectID, chunkSize, chunks);
        return chunks;
    }

    private List<byte[]> EncodeGroupedChunks(GameObject obj, int objectID, int chunkSize, List<byte[]> chunks)
    {
        Mesh mesh = obj.GetComponent<MeshFilter>()?.mesh;
        if (mesh == null)
        {
            Debug.LogError("No mesh found!");
            return null;
        }

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int subMeshCount = mesh.subMeshCount;

        int chunkID = 0;

        for (int subMeshID = 0; subMeshID < subMeshCount; subMeshID++)
        {
            int[] triangles = mesh.GetTriangles(subMeshID);
            HashSet<int> visited = new HashSet<int>();
            HashSet<int> addedVertexIndices = new HashSet<int>();

            while (visited.Count < triangles.Length / 3)
            {
                List<Vector3> chunkVertices = new List<Vector3>();
                List<Vector3> chunkNormals = new List<Vector3>();
                List<int> chunkVertexIndices = new List<int>();
                List<int> chunkTriangles = new List<int>();
                int estimatedSize = sizeof(char) + sizeof(int) * 3;

                for (int i = 0; i < triangles.Length; i += 3)
                {
                    if (!visited.Contains(i / 3))
                    {
                        AddTriangleWithOriginalIndices(i, triangles, vertices, normals, addedVertexIndices, chunkVertices, chunkNormals, chunkVertexIndices, chunkTriangles);
                        visited.Add(i / 3);
                        estimatedSize += 3 * sizeof(int);
                        estimatedSize += 3 * (sizeof(int) + 6 * sizeof(float));
                        break;
                    }
                }

                while (true)
                {
                    int bestTriIndex = -1;
                    int bestSharedVertices = -1;
                    int bestAdditionalSize = int.MaxValue;

                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        if (visited.Contains(i / 3)) continue;

                        int v0 = triangles[i], v1 = triangles[i + 1], v2 = triangles[i + 2];
                        int shared = 0;
                        if (addedVertexIndices.Contains(v0)) shared++;
                        if (addedVertexIndices.Contains(v1)) shared++;
                        if (addedVertexIndices.Contains(v2)) shared++;

                        int triangleSize = 3 * sizeof(int) + (3 - shared) * (sizeof(int) + 6 * sizeof(float));

                        if (estimatedSize + triangleSize > chunkSize) continue;

                        if (shared > bestSharedVertices || (shared == bestSharedVertices && triangleSize < bestAdditionalSize))
                        {
                            bestTriIndex = i;
                            bestSharedVertices = shared;
                            bestAdditionalSize = triangleSize;
                        }
                    }

                    if (bestTriIndex == -1)
                        break;

                    AddTriangleWithOriginalIndices(bestTriIndex, triangles, vertices, normals, addedVertexIndices, chunkVertices, chunkNormals, chunkVertexIndices, chunkTriangles);
                    visited.Add(bestTriIndex / 3);
                    estimatedSize += bestAdditionalSize;
                }

                List<byte> chunkData = new List<byte>();
                chunkData.AddRange(BitConverter.GetBytes('G'));
                chunkData.AddRange(BitConverter.GetBytes(objectID));
                chunkData.AddRange(BitConverter.GetBytes(chunkID++));
                chunkData.AddRange(BitConverter.GetBytes(subMeshID));

                chunkData.AddRange(BitConverter.GetBytes(chunkVertices.Count));
                for (int i = 0; i < chunkVertices.Count; i++)
                {
                    chunkData.AddRange(BitConverter.GetBytes(chunkVertexIndices[i]));
                    chunkData.AddRange(BitConverter.GetBytes(chunkVertices[i].x));
                    chunkData.AddRange(BitConverter.GetBytes(chunkVertices[i].y));
                    chunkData.AddRange(BitConverter.GetBytes(chunkVertices[i].z));
                    chunkData.AddRange(BitConverter.GetBytes(chunkNormals[i].x));
                    chunkData.AddRange(BitConverter.GetBytes(chunkNormals[i].y));
                    chunkData.AddRange(BitConverter.GetBytes(chunkNormals[i].z));
                }

                chunkData.AddRange(BitConverter.GetBytes(chunkTriangles.Count));
                foreach (int t in chunkTriangles)
                    chunkData.AddRange(BitConverter.GetBytes(t));

                chunks.Add(chunkData.ToArray());
            }
        }

        return chunks;
    }

    private void AddTriangleWithOriginalIndices(int triIndex, int[] triangles, Vector3[] vertices, Vector3[] normals,
        HashSet<int> addedVertexIndices, List<Vector3> chunkVertices, List<Vector3> chunkNormals, List<int> chunkVertexIndices, List<int> chunkTriangles)
    {
        for (int j = 0; j < 3; j++)
        {
            int v = triangles[triIndex + j];
            if (!addedVertexIndices.Contains(v))
            {
                addedVertexIndices.Add(v);
                chunkVertexIndices.Add(v);
                chunkVertices.Add(vertices[v]);
                chunkNormals.Add(normals[v]);
            }
            chunkTriangles.Add(v);
        }
    }
}
