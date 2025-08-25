using System.Collections.Generic;
using UnityEngine;

public class SplitDemo : MonoBehaviour
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
        SliceAndRemoveDuplicateTriangles();
        //foreach (Transform child in parentObject.transform)
        //{
        //    PartitionMesh(child.gameObject);
        //}
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

        // Slice the mesh and create GameObjects
        for (int i = 0; i < sliceCount; i++)
        {
            float minAngle = i * angleStep;
            float maxAngle = minAngle + angleStep;

            // Create a new GameObject for the slice
            GameObject sliceObject = new GameObject($"{targetObject.name}_Slice_{i}");
            sliceObject.transform.position = targetObject.transform.position;
            sliceObject.transform.rotation = targetObject.transform.rotation;
            sliceObject.transform.SetParent(parentObject.transform);

            List<Vector3> sliceVertices = new List<Vector3>();
            List<Vector3> sliceNormals = new List<Vector3>();
            List<Vector4> sliceTangents = new List<Vector4>();
            List<Color> sliceColors = new List<Color>();
            List<Vector2> sliceUVs = new List<Vector2>();
            List<List<int>> sliceSubTriangles = new List<List<int>>();
            Dictionary<int, int> vertexMap = new Dictionary<int, int>();

            // Initialize sub-mesh triangle lists
            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                sliceSubTriangles.Add(new List<int>());
            }

            // Iterate over all submeshes
            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                int[] triangles = sourceMesh.GetTriangles(subMeshIndex);

                for (int t = 0; t < triangles.Length; t += 3)
                {
                    int v0 = triangles[t];
                    int v1 = triangles[t + 1];
                    int v2 = triangles[t + 2];

                    // Determine slices the vertices belong to
                    int sliceV0 = vertexToSlice[v0];
                    int sliceV1 = vertexToSlice[v1];
                    int sliceV2 = vertexToSlice[v2];

                    // Determine the highest slice order among the vertices
                    int highestSlice = Mathf.Min(sliceV0, sliceV1, sliceV2);

                    // Keep the triangle only if this slice is the highest slice among the vertices
                    if (i == highestSlice)
                    {
                        int newV0 = AddVertexToSlice(v0, sliceVertices, sliceNormals, sliceTangents, sliceColors, sliceUVs, vertexMap, vertices, normals, tangents, colors, uvs);
                        int newV1 = AddVertexToSlice(v1, sliceVertices, sliceNormals, sliceTangents, sliceColors, sliceUVs, vertexMap, vertices, normals, tangents, colors, uvs);
                        int newV2 = AddVertexToSlice(v2, sliceVertices, sliceNormals, sliceTangents, sliceColors, sliceUVs, vertexMap, vertices, normals, tangents, colors, uvs);

                        sliceSubTriangles[subMeshIndex].Add(newV0);
                        sliceSubTriangles[subMeshIndex].Add(newV1);
                        sliceSubTriangles[subMeshIndex].Add(newV2);
                    }
                }
            }

            // Create the sliced mesh
            Mesh sliceMesh = new Mesh
            {
                vertices = sliceVertices.ToArray(),
                normals = sliceNormals.ToArray(),
                tangents = sliceTangents.ToArray(),
                colors = sliceColors.ToArray(),
                uv = sliceUVs.ToArray()
            };
            //Debug.Log($"{i}, {sliceVertices.Count}");

            // Assign triangles to sub-meshes
            sliceMesh.subMeshCount = subMeshCount;
            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                sliceMesh.SetTriangles(sliceSubTriangles[subMeshIndex], subMeshIndex);
            }

            sliceMesh.RecalculateNormals();
            sliceMesh.RecalculateBounds();

            // Add MeshFilter and MeshRenderer to the slice object
            MeshFilter sliceMeshFilter = sliceObject.AddComponent<MeshFilter>();
            MeshRenderer sliceMeshRenderer = sliceObject.AddComponent<MeshRenderer>();

            sliceMeshFilter.mesh = sliceMesh;
            sliceMeshRenderer.materials = meshRenderer.materials;
        }
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


    public void PartitionMesh(GameObject gameObject)
    {
        MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            Debug.LogError($"GameObject {gameObject.name} does not have a MeshFilter.");
            return;
        }

        Mesh sourceMesh = meshFilter.mesh;

        // Use the updated BuildAdjacency to create adjacency data based on world coordinates
        Dictionary<int, List<int>> adjacency = BuildAdjacency(sourceMesh, gameObject.transform);

        // Find connected components using the updated adjacency data
        List<List<int>> components = FindConnectedComponents(adjacency, sourceMesh.triangles);

        int componentIndex = 0;
        foreach (var component in components)
        {
            // Partition each connected component
            PartitionConnectedComponent(component, sourceMesh, gameObject, componentIndex);
            componentIndex++;
        }
    }


    private Dictionary<int, List<int>> BuildAdjacency(Mesh mesh, Transform transform, float tolerance = 0.001f)
    {
        var adjacency = new Dictionary<int, List<int>>();
        var triangles = mesh.triangles;
        var vertices = mesh.vertices;

        // Global vertex map to find nearby vertices
        var globalVertexMap = new Dictionary<Vector3, int>(new Vector3EqualityComparer(tolerance));

        // Populate global vertex map with world positions
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldVertex = transform.TransformPoint(vertices[i]);
            if (!globalVertexMap.ContainsKey(worldVertex))
            {
                globalVertexMap[worldVertex] = i;
            }
        }

        // Build adjacency
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int v0 = globalVertexMap[transform.TransformPoint(vertices[triangles[i]])];
            int v1 = globalVertexMap[transform.TransformPoint(vertices[triangles[i + 1]])];
            int v2 = globalVertexMap[transform.TransformPoint(vertices[triangles[i + 2]])];

            AddEdge(adjacency, v0, v1, i / 3);
            AddEdge(adjacency, v1, v2, i / 3);
            AddEdge(adjacency, v2, v0, i / 3);
        }

        return adjacency;
    }

    private class Vector3EqualityComparer : IEqualityComparer<Vector3>
    {
        private readonly float tolerance;

        public Vector3EqualityComparer(float tolerance)
        {
            this.tolerance = tolerance;
        }

        public bool Equals(Vector3 v1, Vector3 v2)
        {
            return Vector3.SqrMagnitude(v1 - v2) < tolerance * tolerance;
        }

        public int GetHashCode(Vector3 obj)
        {
            return obj.GetHashCode();
        }
    }


    private void AddEdge(Dictionary<int, List<int>> adjacency, int v0, int v1, int triangleIndex)
    {
        if (!adjacency.ContainsKey(v0)) adjacency[v0] = new List<int>();
        if (!adjacency.ContainsKey(v1)) adjacency[v1] = new List<int>();

        if (!adjacency[v0].Contains(triangleIndex)) adjacency[v0].Add(triangleIndex);
        if (!adjacency[v1].Contains(triangleIndex)) adjacency[v1].Add(triangleIndex);
    }

    private List<List<int>> FindConnectedComponents(Dictionary<int, List<int>> adjacency, int[] triangles)
    {
        var visited = new HashSet<int>();
        var components = new List<List<int>>();

        for (int i = 0; i < triangles.Length / 3; i++)
        {
            if (!visited.Contains(i))
            {
                var component = new List<int>();
                var stack = new Stack<int>();
                stack.Push(i);

                while (stack.Count > 0)
                {
                    int current = stack.Pop();
                    if (visited.Add(current))
                    {
                        component.Add(current);

                        // Add neighboring triangles
                        if (adjacency.ContainsKey(current))
                        {
                            foreach (int neighbor in adjacency[current])
                            {
                                stack.Push(neighbor);
                            }
                        }
                    }
                }

                components.Add(component);
            }
        }

        return components;
    }


    private void PartitionConnectedComponent(
    List<int> component,
    Mesh sourceMesh,
    GameObject parent,
    int componentIndex)
    {
        List<int> currentTriangles = new List<int>();
        Dictionary<int, int> vertexMap = new Dictionary<int, int>();
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector4> tangents = new List<Vector4>();
        List<Color> colors = new List<Color>();
        List<Vector2> uvs = new List<Vector2>();
        List<List<int>> subMeshTriangles = new List<List<int>>();

        for (int i = 0; i < sourceMesh.subMeshCount; i++)
        {
            subMeshTriangles.Add(new List<int>());
        }

        int partitionIndex = 0;
        int triangleCount = 0;

        foreach (int triangleIndex in component)
        {
            if (triangleCount >= sizeLimit)
            {
                CreateMeshWithSubMeshes(parent, componentIndex, partitionIndex++, vertices, normals, tangents, colors, uvs, subMeshTriangles);
                foreach (var subTriangles in subMeshTriangles) subTriangles.Clear();
                currentTriangles.Clear();
                vertexMap.Clear();
                triangleCount = 0;
            }

            // Add the triangle and its sub-mesh
            for (int subMeshIndex = 0; subMeshIndex < sourceMesh.subMeshCount; subMeshIndex++)
            {
                int[] subTriangles = sourceMesh.GetTriangles(subMeshIndex);
                int subStartIndex = triangleIndex * 3;

                if (subStartIndex + 2 < subTriangles.Length)
                {
                    int v0 = subTriangles[subStartIndex];
                    int v1 = subTriangles[subStartIndex + 1];
                    int v2 = subTriangles[subStartIndex + 2];

                    int newV0 = AddVertex(v0, sourceMesh, vertices, normals, tangents, colors, uvs, vertexMap, parent.transform);
                    int newV1 = AddVertex(v1, sourceMesh, vertices, normals, tangents, colors, uvs, vertexMap, parent.transform);
                    int newV2 = AddVertex(v2, sourceMesh, vertices, normals, tangents, colors, uvs, vertexMap, parent.transform);

                    subMeshTriangles[subMeshIndex].Add(newV0);
                    subMeshTriangles[subMeshIndex].Add(newV1);
                    subMeshTriangles[subMeshIndex].Add(newV2);
                }
            }

            triangleCount++;
        }

        // Final partition
        if (triangleCount > 0)
        {
            CreateMeshWithSubMeshes(parent, componentIndex, partitionIndex, vertices, normals, tangents, colors, uvs, subMeshTriangles);
        }
    }


    /// <summary>
    /// Extract the vertex indices of a triangle.
    /// </summary>
    private int[] GetTriangleVertices(Mesh mesh, int triangleIndex)
    {
        int startIndex = triangleIndex * 3;
        return new int[] { mesh.triangles[startIndex], mesh.triangles[startIndex + 1], mesh.triangles[startIndex + 2] };
    }

    private int AddVertex(
     int originalIndex,
     Mesh mesh,
     List<Vector3> vertices,
     List<Vector3> normals,
     List<Vector4> tangents,
     List<Color> colors,
     List<Vector2> uvs,
     Dictionary<int, int> vertexMap,
     Transform parentTransform)
    {
        if (!vertexMap.ContainsKey(originalIndex))
        {
            vertexMap[originalIndex] = vertices.Count;

            // Transform vertex and normal to world coordinates
            Vector3 worldVertex = parentTransform.TransformPoint(mesh.vertices[originalIndex]);
            Vector3 worldNormal = parentTransform.TransformDirection(mesh.normals[originalIndex]);

            vertices.Add(worldVertex);
            normals.Add(worldNormal);
            tangents.Add(mesh.tangents[originalIndex]);
            colors.Add(mesh.colors.Length > 0 ? mesh.colors[originalIndex] : Color.white);
            uvs.Add(mesh.uv[originalIndex]);
        }

        return vertexMap[originalIndex];
    }


    private void CreateMeshWithSubMeshes(
    GameObject parent,
    int componentIndex,
    int partitionIndex,
    List<Vector3> vertices,
    List<Vector3> normals,
    List<Vector4> tangents,
    List<Color> colors,
    List<Vector2> uvs,
    List<List<int>> subMeshTriangles)
    {
        Mesh newMesh = new Mesh
        {
            vertices = vertices.ToArray(),
            normals = normals.ToArray(),
            tangents = tangents.ToArray(),
            colors = colors.ToArray(),
            uv = uvs.ToArray(),
            subMeshCount = subMeshTriangles.Count
        };

        for (int subMeshIndex = 0; subMeshIndex < subMeshTriangles.Count; subMeshIndex++)
        {
            newMesh.SetTriangles(subMeshTriangles[subMeshIndex].ToArray(), subMeshIndex);
        }

        newMesh.RecalculateBounds();

        GameObject partitionObject = new GameObject($"Component_{componentIndex}_Partition_{partitionIndex}");
        partitionObject.transform.SetParent(parent.transform);

        MeshFilter meshFilter = partitionObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = partitionObject.AddComponent<MeshRenderer>();
        meshFilter.mesh = newMesh;

        MeshRenderer parentRenderer = parent.GetComponent<MeshRenderer>();
        if (parentRenderer != null)
        {
            meshRenderer.materials = parentRenderer.materials;
        }
    }


}
