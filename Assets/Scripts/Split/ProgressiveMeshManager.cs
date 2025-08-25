using UnityEditor;
using UnityEngine;

using System.Collections.Generic;
using Unity.VisualScripting;
using NUnit.Framework.Internal;
using System.Linq;
using System;

public class ProgressiveMeshManager : MonoBehaviour
{
    public GameObject targetObject; // Input object to simplify

    [Range(2, 30000)] // Slider in the Inspector
    public int targetStep = 0; // Current step of simplification/recovery

    private MeshFilter meshFilter;

    private List<StepChangeData> stepChangeData; // Stores changes between steps
    private List<Vector3> currentVertices, currentNormals; // Current vertices in the mesh
    private List<Vector4> currentTangents;
    private List<Color> currentColors;
    private List<Vector2> currentUVs;
    private List<int>[] currentTriangles; // Current triangles in the mesh

    private Mesh originalMesh;
    private Dictionary<int, List<int>>[] trianglesVert;

    private int maxSimplificationSteps, currentStep;

    void Start()
    {
        TestDuplicatedVertices();
        if (targetObject == null)
        {
            Debug.LogError("Target object is not assigned!");
            return;
        }

        meshFilter = targetObject.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            Debug.LogError("Target object must have a MeshFilter component!");
            return;
        }

        originalMesh = meshFilter.mesh;
        stepChangeData = new List<StepChangeData>();

        // Clone the original mesh data
        currentVertices = new List<Vector3>(originalMesh.vertices);
        currentNormals = new List<Vector3>(originalMesh.normals);
        currentTangents = new List<Vector4>(originalMesh.tangents);
        currentColors = new List<Color>(originalMesh.colors);
        currentUVs = new List<Vector2>(originalMesh.uv);
        currentTriangles = new List<int>[originalMesh.subMeshCount];
        for (int submesh = 0; submesh < originalMesh.subMeshCount; submesh++) {
            currentTriangles[submesh] = new List<int>( originalMesh.GetTriangles(submesh));    
        }
        //currentTriangles = new List<int>(originalMesh.triangles);

        // Precompute simplification steps
        PrecomputeSimplifications();
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
                    }
                }
            }
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
        sourceMesh.RecalculateBounds();

        meshFilter.mesh = sourceMesh;
    }

    private void PrecomputeSimplifications()
    {
        Debug.Log("Precomputing simplifications...");
        GenerateTriDict();
        while (currentVertices.Count > 0)
        {
            var stepData = SimplifyOneStep();
            stepChangeData.Add(stepData);
        }
        stepChangeData.Reverse();
        currentTriangles = new List<int>[originalMesh.subMeshCount];
        for (int submesh = 0; submesh < originalMesh.subMeshCount; submesh++)
        {
            currentTriangles[submesh] = new List<int>();
        }
        currentVertices = new List<Vector3>();
        currentNormals = new List<Vector3>();
        currentTangents = new List<Vector4>();
        currentColors = new List<Color>();
        currentUVs = new List<Vector2>();
        for (int i = 0; i < 3; i++)
        {
            ApplyStepChange(stepChangeData[i], false);
        }
        maxSimplificationSteps = stepChangeData.Count;
        Debug.Log($"Precomputed {maxSimplificationSteps} simplification steps.");
        for (int submesh = 0; submesh < originalMesh.subMeshCount; submesh++)
        {
            meshFilter.mesh.SetTriangles(new int[0], submesh);
        }
        UpdateMesh(false);
    }

    private void GenerateTriDict()
    {   
        trianglesVert = new Dictionary<int, List<int>>[originalMesh.subMeshCount];
        for (int submesh = 0; submesh < originalMesh.subMeshCount; submesh++)
        {
            List<int> triangles = new List<int>(currentTriangles[submesh]);
            trianglesVert[submesh] = new Dictionary<int, List<int>>();
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int lastInd = Mathf.Max(triangles[i], triangles[i + 1], triangles[i + 2]);
                if (!trianglesVert[submesh].ContainsKey(lastInd))
                {
                    trianglesVert[submesh][lastInd] = new List<int>();
                }
                trianglesVert[submesh][lastInd].Add(triangles[i]);
                trianglesVert[submesh][lastInd].Add(triangles[i + 1]);
                trianglesVert[submesh][lastInd].Add(triangles[i + 2]);
            }
        }
    }

    private StepChangeData SimplifyOneStep()
    {
        StepChangeData newStepData = new StepChangeData();

        int vertexToRemove = currentVertices.Count - 1;
        newStepData.removedVertices.Add(vertexToRemove);
        newStepData.addedVertices.Add(currentVertices[vertexToRemove]);
        newStepData.addedNormals.Add(currentNormals[vertexToRemove]);
        newStepData.addedTangents.Add(currentTangents[vertexToRemove]);
        newStepData.addedColors.Add(currentColors[vertexToRemove]);
        newStepData.addedUV.Add(currentUVs[vertexToRemove]);
        currentVertices.RemoveAt(vertexToRemove);
        currentNormals.RemoveAt(vertexToRemove);
        currentTangents.RemoveAt(vertexToRemove);
        currentColors.RemoveAt(vertexToRemove);
        currentUVs.RemoveAt(vertexToRemove);
        newStepData.removedTriangles = new List<int>[originalMesh.subMeshCount];
        newStepData.addedTriPoses = new List<int>[originalMesh.subMeshCount];
        newStepData.addedTriangles = new List<int>[originalMesh.subMeshCount];
        for (int submesh = 0; submesh < originalMesh.subMeshCount; submesh++)
        {
            newStepData.removedTriangles[submesh] = new List<int>();
            newStepData.addedTriPoses[submesh] = new List<int>();
            newStepData.addedTriangles[submesh] = new List<int>();
        }

        for (int submesh = 0; submesh < originalMesh.subMeshCount; submesh++)
        {
            if (!trianglesVert[submesh].ContainsKey(vertexToRemove))
                continue;

            List<int> newTriangles = new List<int>();
            int nextMaxVert = 0;
            for (int i = 0; i < trianglesVert[submesh][vertexToRemove].Count; i++)
            {
                if (trianglesVert[submesh][vertexToRemove][i] != vertexToRemove && trianglesVert[submesh][vertexToRemove][i] > nextMaxVert)
                {
                    nextMaxVert = trianglesVert[submesh][vertexToRemove][i];
                }
            }

            for (int i = 0; i < trianglesVert[submesh][vertexToRemove].Count; i += 3)
            {
                if (trianglesVert[submesh][vertexToRemove][i] != nextMaxVert && trianglesVert[submesh][vertexToRemove][i + 1] != nextMaxVert &&
                    trianglesVert[submesh][vertexToRemove][i + 2] != nextMaxVert)
                {
                    newTriangles.Add(trianglesVert[submesh][vertexToRemove][i] != vertexToRemove ? trianglesVert[submesh][vertexToRemove][i] : nextMaxVert);
                    newTriangles.Add(trianglesVert[submesh][vertexToRemove][i + 1] != vertexToRemove ? trianglesVert[submesh][vertexToRemove][i + 1] : nextMaxVert);
                    newTriangles.Add(trianglesVert[submesh][vertexToRemove][i + 2] != vertexToRemove ? trianglesVert[submesh][vertexToRemove][i + 2] : nextMaxVert);
                }
            }

            string log = $"removed vertex: {vertexToRemove}.. submesh: {submesh}.. triangles it has: {string.Join(',', trianglesVert[submesh][vertexToRemove])}.. " +
                $"next max vertex: {nextMaxVert}";
            if (trianglesVert[submesh].ContainsKey(nextMaxVert))
            {
                log += $".. old triangles of next: {string.Join(',', trianglesVert[submesh][nextMaxVert])}";
            }
            else
            {
                trianglesVert[submesh][nextMaxVert] = new List<int>();
                log += $".. no old triangles but creating";
            }


            List<int> newTriPoses = new List<int>();
            int newTrisPos = 0;
            for (int i = 0; i <= nextMaxVert; i++)
            {
                if (trianglesVert[submesh].ContainsKey(i))
                {
                    newTrisPos += trianglesVert[submesh][i].Count / 3;
                }
            }
            trianglesVert[submesh][nextMaxVert].AddRange(newTriangles);
            for (int i = 0; i < newTriangles.Count / 3; i++)
            {
                newStepData.addedTriPoses[submesh].Add(i + newTrisPos);
            }

            newStepData.removedTriangles[submesh] = trianglesVert[submesh][vertexToRemove];
            newStepData.addedTriangles[submesh] = newTriangles;
            log += $".. added triposes: {string.Join(',', newStepData.addedTriPoses[submesh])}" +
                $".. added triangles: {string.Join(',', newStepData.addedTriangles[submesh])}";
            //Debug.Log(log);
        }

        return newStepData;
    }
    public void Update()
    {
        UpdateMeshToStep();
    }

    public void UpdateMeshToStep()
    {
        if (stepChangeData == null || stepChangeData.Count == 0)
        {
            Debug.LogWarning("Step change data is not initialized. Ensure PrecomputeSimplifications has been called.");
            return;
        }

        // Clamp the step to valid range
        targetStep = Mathf.Clamp(targetStep, 0, stepChangeData.Count - 1);

        if (currentStep < targetStep)
        {
            while(currentStep < targetStep)
            {
                ApplyStepChange(stepChangeData[currentStep], false);
            }
            UpdateMesh(false);
        }
        else if (currentStep > targetStep)
        {
            while (currentStep > targetStep)
            {
                ApplyStepChange(stepChangeData[currentStep - 1], true);
            }
            UpdateMesh(true);
        }
    }

    private void ApplyStepChange(StepChangeData stepData, bool simplify)
    {
        if (simplify)
        {
            
            foreach (int vertexIndex in stepData.removedVertices)
            {
                currentVertices.RemoveAt(vertexIndex);
                currentNormals.RemoveAt(vertexIndex);
                currentTangents.RemoveAt(vertexIndex);
                currentColors.RemoveAt(vertexIndex);
                currentUVs.RemoveAt(vertexIndex);
            }
            for (int submesh = 0; submesh < originalMesh.subMeshCount; submesh++)
            {
                string log = "";
                log += $"removed vertex: {stepData.removedVertices[0]}.. submesh: {submesh}.. tri count: {currentTriangles[submesh].Count / 3}";
                List<int> trianglesRemoved = new List<int>();
                for (int i = currentTriangles[submesh].Count - stepData.removedTriangles[submesh].Count; i < stepData.removedTriangles[submesh].Count; i++)
                {
                    trianglesRemoved.Add(currentTriangles[submesh][i]);
                }
                log += $".. removed triangles {string.Join(',', trianglesRemoved)}";

                currentTriangles[submesh].RemoveRange(currentTriangles[submesh].Count - stepData.removedTriangles[submesh].Count, stepData.removedTriangles[submesh].Count);
                log += $".. supposely removed triangles {string.Join(',', stepData.removedTriangles[submesh])}.. " +
                    $"tri count: {currentTriangles[submesh].Count / 3}";
                List<int> newTriangles = new List<int>();
                int count = 0;
                for (int i = 0; i < currentTriangles[submesh].Count / 3 + stepData.addedTriPoses[submesh].Count; i++)
                {
                    if (count < stepData.addedTriPoses[submesh].Count && i == stepData.addedTriPoses[submesh][count])
                    {
                        newTriangles.Add(stepData.addedTriangles[submesh][count * 3]);
                        newTriangles.Add(stepData.addedTriangles[submesh][count * 3 + 1]);
                        newTriangles.Add(stepData.addedTriangles[submesh][count * 3 + 2]);
                        count++;
                    }
                    else
                    {
                        newTriangles.Add(currentTriangles[submesh][(i - count) * 3]);
                        newTriangles.Add(currentTriangles[submesh][(i - count) * 3 + 1]);
                        newTriangles.Add(currentTriangles[submesh][(i - count) * 3 + 2]);
                    }
                }
                currentTriangles[submesh] = newTriangles;
                log += $".. supposed added tri pos: {string.Join(',', stepData.addedTriPoses[submesh])}.. " +
                    $"supposed added tri: {string.Join(',', stepData.addedTriangles[submesh])}.. " +
                    $"tri count: {currentTriangles[submesh].Count / 3}";
                //Debug.Log(log);
            }

            currentStep--;
        } else
        {
            for(int i = 0; i < stepData.addedVertices.Count; i++)
            {
                currentVertices.Add(stepData.addedVertices[i]);
                currentNormals.Add(stepData.addedNormals[i]);
                currentTangents.Add(stepData.addedTangents[i]);
                currentColors.Add(stepData.addedColors[i]);
                currentUVs.Add(stepData.addedUV[i]);
            }

            for (int submesh = 0; submesh < originalMesh.subMeshCount; submesh++)
            {
                List<int> newTriangles = new List<int>();
                int count = 0;
                for (int i = 0; i < currentTriangles[submesh].Count / 3; i++)
                {
                    if (count < stepData.addedTriPoses[submesh].Count && i == stepData.addedTriPoses[submesh][count])
                    {
                        count++;
                    }
                    else
                    {
                        newTriangles.Add(currentTriangles[submesh][i * 3]);
                        newTriangles.Add(currentTriangles[submesh][i * 3 + 1]);
                        newTriangles.Add(currentTriangles[submesh][i * 3 + 2]);
                    }
                }
                currentTriangles[submesh] = newTriangles;

                currentTriangles[submesh].AddRange(stepData.removedTriangles[submesh]);
            }
            currentStep++;
        }
    }

    private void UpdateMesh(bool simplfy)
    {
        if (simplfy)
        {
            StepChangeData stepData = stepChangeData[currentStep];
            for (int submesh = 0; submesh < originalMesh.subMeshCount; submesh++)
            {
                meshFilter.mesh.SetTriangles(currentTriangles[submesh].ToArray(), submesh);
            }
            meshFilter.mesh.vertices = currentVertices.ToArray();
            meshFilter.mesh.normals = currentNormals.ToArray();
            meshFilter.mesh.tangents = currentTangents.ToArray();
            meshFilter.mesh.colors = currentColors.ToArray();
            meshFilter.mesh.uv = currentUVs.ToArray();


        }
        else
        {
            meshFilter.mesh.vertices = currentVertices.ToArray();
            meshFilter.mesh.normals = currentNormals.ToArray();
            meshFilter.mesh.tangents = currentTangents.ToArray();
            meshFilter.mesh.colors = currentColors.ToArray();
            meshFilter.mesh.uv = currentUVs.ToArray();
            for (int submesh = 0; submesh < originalMesh.subMeshCount; submesh++)
            {
                meshFilter.mesh.SetTriangles(currentTriangles[submesh].ToArray(), submesh);
            }
        }
    }
}




[System.Serializable]
public class StepChangeData
{
    public List<int> removedVertices; // Indices of vertices removed at this step
    public List<int>[] removedTriangles; // Indices of triangles removed at this step
    public int removeIndex, removeCount;

    public List<Vector3> addedVertices, addedNormals;
    public List<Vector4> addedTangents;
    public List<Color> addedColors;
    public List<Vector2> addedUV; // Vertices added back at this step (during recovery)
    public List<int>[] addedTriangles; // Triangles added back at this step (during recovery)
    public List<int>[] addedTriPoses;

    public StepChangeData()
    {
        removedVertices = new List<int>();
        removedTriangles = new List<int>[1];
        addedVertices = new List<Vector3>();
        addedNormals = new List<Vector3>();
        addedTangents = new List<Vector4>();
        addedColors = new List<Color>();
        addedUV = new List<Vector2>();
        addedTriangles = new List<int>[1];
        addedTriPoses = new List<int>[1];
    }
}
