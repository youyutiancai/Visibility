using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class MeshExporter : MonoBehaviour
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

            string filename = Path.Combine(outputPath, $"{mf.gameObject.name}_mesh_{meshIndex}.bin");

            using (BinaryWriter writer = new BinaryWriter(File.Open(filename, FileMode.Create)))
            {
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;

                writer.Write(vertices.Length);
                writer.Write(mesh.subMeshCount); // Write number of submeshes

                // Write vertex data
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

                // Get the Renderer and materials
                Renderer renderer = mf.GetComponent<Renderer>();
                Material[] materials = renderer ? renderer.sharedMaterials : new Material[0];

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] subTriangles = mesh.GetTriangles(sub);
                    writer.Write(subTriangles.Length);

                    foreach (int index in subTriangles)
                        writer.Write(index);

                    // Write color from material
                    Color color = Color.white;
                    if (sub < materials.Length && materials[sub] != null && materials[sub].HasProperty("_Color"))
                        color = materials[sub].GetColor("_Color");

                    if (color == Color.white)
                    {
                        Debug.LogWarning($"Material color not found for submesh {sub} of {mf.gameObject.name}. Using default white color.");
                    }
                    writer.Write(color.r);
                    writer.Write(color.g);
                    writer.Write(color.b);
                    writer.Write(color.a);
                }
                // Write global transform info
                Transform t = mf.transform;
                Vector3 pos = t.position;
                Quaternion rot = t.rotation;
                Vector3 scale = t.lossyScale;

                writer.Write(pos.x);
                writer.Write(pos.y);
                writer.Write(pos.z);

                writer.Write(rot.x);
                writer.Write(rot.y);
                writer.Write(rot.z);
                writer.Write(rot.w);

                writer.Write(scale.x);
                writer.Write(scale.y);
                writer.Write(scale.z);
            }

            meshIndex++;
            Debug.Log($"Exported mesh {meshIndex} to: {filename}");

            yield return null;
        }

        Debug.Log($"Finished exporting {meshIndex} mesh(es).");
    }
}
