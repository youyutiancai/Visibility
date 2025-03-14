using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RandomOrderUnreliableNet : MonoBehaviour
{
    public GameObject testObject;
    [Range(0, 1)]
    public float packageLossPossibility;
    private float prePackageLossPossibility;
    private Mesh oriMesh;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        RandomizeMesh(testObject);
        oriMesh = testObject.GetComponent<MeshFilter>().mesh;
        packageLossPossibility = 0;
        prePackageLossPossibility = packageLossPossibility;
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


    private void Update()
    {
        if (packageLossPossibility != prePackageLossPossibility)
        {
            SimulateMeshTransmission(testObject, oriMesh, packageLossPossibility);
            prePackageLossPossibility = packageLossPossibility;
        }
    }

    public static void SimulateMeshTransmission(GameObject obj, Mesh oriMesh, float packetLossProbability, int vertexChunkSize = 100, int triangleChunkSize = 300)
    {
        MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.mesh == null)
        {
            Debug.LogError("No MeshFilter found on the object!");
            return;
        }

        Mesh mesh = oriMesh;

        Vector3[] originalVertices = mesh.vertices;
        int vertexCount = originalVertices.Length;
        int subMeshCount = mesh.subMeshCount;

        System.Random rng = new System.Random();

        // Step 1: Simulate sending vertex data in chunks
        Vector3[] receivedVertices = new Vector3[vertexCount];

        for (int i = 0; i < vertexCount; i += vertexChunkSize)
        {
            bool packetLost = rng.NextDouble() < packetLossProbability;

            for (int j = 0; j < vertexChunkSize && (i + j) < vertexCount; j++)
            {
                if (packetLost)
                    receivedVertices[i + j] = Vector3.zero; // Placeholder for missing vertices
                else
                    receivedVertices[i + j] = originalVertices[i + j];
            }
        }

        // Step 2: Simulate sending triangle data in chunks for each submesh
        List<int[]> receivedSubMeshes = new List<int[]>(subMeshCount);

        for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
        {
            int[] subTriangles = mesh.GetTriangles(subMesh);
            List<int> receivedTriangles = new List<int>();

            for (int i = 0; i < subTriangles.Length; i += triangleChunkSize * 3) // Iterate over triangle chunks
            {
                bool packetLost = rng.NextDouble() < packetLossProbability;

                if (!packetLost)
                {
                    for (int j = 0; j < triangleChunkSize * 3 && (i + j) < subTriangles.Length; j += 3) // Individual triangles
                    {
                        int v0 = subTriangles[i + j];
                        int v1 = subTriangles[i + j + 1];
                        int v2 = subTriangles[i + j + 2];

                        // Only add the triangle if none of its vertices are Vector3.zero
                        if (receivedVertices[v0] != Vector3.zero &&
                            receivedVertices[v1] != Vector3.zero &&
                            receivedVertices[v2] != Vector3.zero)
                        {
                            receivedTriangles.Add(v0);
                            receivedTriangles.Add(v1);
                            receivedTriangles.Add(v2);
                        }
                    }
                }
            }

            receivedSubMeshes.Add(receivedTriangles.ToArray());
        }

        // Step 3: Construct the new mesh
        Mesh receivedMesh = new Mesh();
        receivedMesh.vertices = receivedVertices;
        receivedMesh.subMeshCount = subMeshCount;

        for (int i = 0; i < subMeshCount; i++)
        {
            receivedMesh.SetTriangles(receivedSubMeshes[i], i);
        }

        receivedMesh.RecalculateNormals();
        receivedMesh.RecalculateBounds();

        // Step 4: Apply the new mesh to the GameObject
        meshFilter.mesh = receivedMesh;
    }
}
