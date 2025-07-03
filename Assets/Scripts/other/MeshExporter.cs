using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class MeshExporter: MonoBehaviour
{
    public GameObject targetRoot;

    private void Start()
    {
        StartCoroutine(ExportMeshesCoroutine(targetRoot, "C:\\Users\\zhou1168\\VRAR\\Visibility\\Assets\\Data\\ObjectInfo"));
    }

    private IEnumerator ExportMeshesCoroutine(GameObject root, string outputPath)
    {
        if (root == null)
        {
            Debug.LogWarning("No GameObject assigned for mesh export.");
            yield break;
        }

        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);

        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>();
        int meshIndex = 0;

        foreach (MeshFilter mf in meshFilters)
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) continue;

            string filename = Path.Combine(outputPath, $"{root.name}_mesh_{meshIndex}.bin");

            using (BinaryWriter writer = new BinaryWriter(File.Open(filename, FileMode.Create)))
            {
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                int[] triangles = mesh.triangles;

                writer.Write(vertices.Length);
                writer.Write(triangles.Length);

                for (int i = 0; i < vertices.Length; i++)
                {
                    writer.Write(vertices[i].x);
                    writer.Write(vertices[i].y);
                    writer.Write(vertices[i].z);

                    Vector3 normal = (normals != null && normals.Length == vertices.Length)
                        ? normals[i]
                        : Vector3.zero;

                    writer.Write(normal.x);
                    writer.Write(normal.y);
                    writer.Write(normal.z);
                }

                foreach (int index in triangles)
                    writer.Write(index);
            }

            meshIndex++;
            Debug.Log($"Exported mesh {meshIndex} to: {filename}");

            yield return null; // Wait for one frame before processing the next mesh
        }

        Debug.Log($"Finished exporting {meshIndex} mesh(es).");
    }
}
