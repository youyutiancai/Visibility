using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;

public class RandomizedMesh : MeshVariant
{
    public override List<byte[]> RequestChunks(int objectID, int chunkSize)
    {
        List<byte[]> chunks = new List<byte[]>();
        //GameObject testObject = VisibilityCheck.Instance.testObject;
        GameObject testObject = VisibilityCheck.Instance.objectsInScene[objectID];
        //RandomizeMesh(testObject);
        GetChunks(testObject, objectID, chunkSize, ref chunks);
        //Debug.Log($"number of chunks {chunks.Count}");
        return chunks;
    }

    public static void RandomizeMesh(GameObject obj)
    {
        MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.mesh == null)
        {
            Debug.LogError("No MeshFilter found on the object!");
            return;
        }

        Mesh mesh = meshFilter.mesh;

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Vector3[] normals = mesh.normals;
        Vector2[] uv = mesh.uv;

        int vertexCount = vertices.Length;
        int subMeshCount = mesh.subMeshCount;

        // Create a mapping of old vertex index to new shuffled index
        List<int> newOrder = Enumerable.Range(0, vertexCount).ToList();
        System.Random rng = new System.Random();
        newOrder = newOrder.OrderBy(x => rng.Next()).ToList(); // Shuffle indices

        // Create new vertex lists
        Vector3[] newVertices = new Vector3[vertexCount];
        Vector3[] newNormals = new Vector3[vertexCount];
        Vector2[] newUVs = new Vector2[vertexCount];

        Dictionary<int, int> indexMap = new Dictionary<int, int>();

        for (int i = 0; i < vertexCount; i++)
        {
            newVertices[i] = vertices[newOrder[i]];
            newNormals[i] = normals[newOrder[i]];
            newUVs[i] = uv[newOrder[i]];
            indexMap[newOrder[i]] = i; // Store new index mapping
        }

        // Create new submesh triangle lists
        List<int[]> newSubMeshes = new List<int[]>(subMeshCount);

        for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
        {
            int[] subTriangles = mesh.GetTriangles(subMesh);
            int triangleCount = subTriangles.Length / 3;

            // Adjust triangle indices based on new vertex order
            int[] updatedTriangles = new int[subTriangles.Length];
            for (int i = 0; i < subTriangles.Length; i++)
            {
                updatedTriangles[i] = indexMap[subTriangles[i]];
            }

            // Shuffle triangles in groups of 3
            List<int> triangleList = updatedTriangles.ToList();
            List<int> shuffledTriangles = new List<int>();

            List<int> triangleGroups = Enumerable.Range(0, triangleCount).ToList();
            triangleGroups = triangleGroups.OrderBy(x => rng.Next()).ToList(); // Shuffle triangles

            foreach (int index in triangleGroups)
            {
                shuffledTriangles.Add(triangleList[index * 3]);
                shuffledTriangles.Add(triangleList[index * 3 + 1]);
                shuffledTriangles.Add(triangleList[index * 3 + 2]);
            }

            newSubMeshes.Add(shuffledTriangles.ToArray());
        }

        // Assign the new mesh data
        mesh.vertices = newVertices;
        mesh.normals = newNormals;
        mesh.uv = newUVs;
        mesh.subMeshCount = subMeshCount;

        for (int i = 0; i < subMeshCount; i++)
        {
            mesh.SetTriangles(newSubMeshes[i], i);
        }

        mesh.RecalculateBounds();
        meshFilter.mesh = mesh;
    }

    private void GetChunks(GameObject testObject, int objectID, int chunkSize, ref List<byte[]> chunks)
    {
        MeshFilter meshFilter = testObject.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.mesh == null)
        {
            Debug.LogError("No MeshFilter found on the object!");
            return;
        }

        Mesh mesh = meshFilter.mesh;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int subMeshCount = mesh.subMeshCount;

        System.Random rng = new System.Random();

        // ===========================
        // 1. Encode Vertex Data
        // ===========================
        int numVerticesPerChunk = (chunkSize - sizeof(char) - sizeof(int) * 2) / (sizeof(float) * 6);
        int numVertexChunks = Mathf.CeilToInt((float)vertices.Length / numVerticesPerChunk);

        for (int i = 0; i < numVertexChunks; i++)
        {
            int startIdx = i * numVerticesPerChunk;
            int endIdx = Mathf.Min((i + 1) * numVerticesPerChunk, vertices.Length);

            List<byte> chunkData = new List<byte>();

            // Header: [char('V'), int(objectID), int(chunkID)]
            chunkData.AddRange(BitConverter.GetBytes('V'));
            chunkData.AddRange(BitConverter.GetBytes(objectID));
            chunkData.AddRange(BitConverter.GetBytes(i));

            // Encode vertex data (position + normal)
            for (int j = startIdx; j < endIdx; j++)
            {
                chunkData.AddRange(BitConverter.GetBytes(vertices[j].x));
                chunkData.AddRange(BitConverter.GetBytes(vertices[j].y));
                chunkData.AddRange(BitConverter.GetBytes(vertices[j].z));
                chunkData.AddRange(BitConverter.GetBytes(normals[j].x));
                chunkData.AddRange(BitConverter.GetBytes(normals[j].y));
                chunkData.AddRange(BitConverter.GetBytes(normals[j].z));
            }

            chunks.Add(chunkData.ToArray());
        }

        // ===========================
        // 2. Encode Triangle Data
        // ===========================
        int totalTriangleChunks = 0;
        for (int subMeshID = 0; subMeshID < subMeshCount; subMeshID++)
        {
            int[] triangles = mesh.GetTriangles(subMeshID);

            int numTrianglesPerChunk = (chunkSize - sizeof(char) - sizeof(int) * 3) / (sizeof(int) * 3);
            int numTriangleChunks = Mathf.CeilToInt((float)triangles.Length / (numTrianglesPerChunk * 3));
            

            for (int i = 0; i < numTriangleChunks; i++)
            {
                int startIdx = i * numTrianglesPerChunk * 3;
                int endIdx = Mathf.Min((i + 1) * numTrianglesPerChunk * 3, triangles.Length);

                List<byte> chunkData = new List<byte>();

                // Header: [char('T'), int(objectID), int(chunkID), int(subMeshID)]
                chunkData.AddRange(BitConverter.GetBytes('T'));
                chunkData.AddRange(BitConverter.GetBytes(objectID));
                chunkData.AddRange(BitConverter.GetBytes(i + numVertexChunks + totalTriangleChunks));
                chunkData.AddRange(BitConverter.GetBytes(subMeshID));

                // Encode triangle data
                for (int j = startIdx; j < endIdx; j += 3)
                {
                    chunkData.AddRange(BitConverter.GetBytes(triangles[j]));
                    chunkData.AddRange(BitConverter.GetBytes(triangles[j + 1]));
                    chunkData.AddRange(BitConverter.GetBytes(triangles[j + 2]));
                }

                chunks.Add(chunkData.ToArray());
            }
            totalTriangleChunks += numTriangleChunks;
        }
        //Debug.Log($"# of vertex chunks: {numVertexChunks}, # of triangle chunks: {totalTriangleChunks}");
    }
}
