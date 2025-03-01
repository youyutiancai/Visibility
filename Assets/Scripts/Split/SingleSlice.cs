using UnityEditor;
using UnityEngine;

using System.Collections.Generic;
using Unity.VisualScripting;
using NUnit.Framework.Internal;
using System.Linq;
using System;

public class SingleSlice : MonoBehaviour
{
    public Vector3[] vertices, normals;
    public Vector4[] tangents;
    public Color[] colors;
    public Vector2[] uvs;

    private List<StepChangeDataInd> stepChangeData;

    public List<int>[] currentTriangles;
    private Dictionary<int, List<int>>[] trianglesVert;

    public int selfVertStartInd, subMeshCount, currentStep, targetStep;

    private GameObject testObject;
    private bool noDuplicate;
    private Dictionary<int, int> repeatedVertices;

    public SingleSlice(Vector3[] newVertices, Vector3[] newNormals, Vector4[] newTangents, Color[] newColors, Vector2[] newUVs,
        int uniqueVertStartInd, List<int>[] triangles, GameObject tobject, bool noDuplicate)
    {
        stepChangeData = new List<StepChangeDataInd>();
        //currentVertices = vertices.ToList();
        selfVertStartInd = uniqueVertStartInd;
        vertices = newVertices;
        normals = newNormals;
        tangents = newTangents;
        colors = newColors;
        uvs = newUVs;
        currentTriangles = triangles;
        subMeshCount = currentTriangles.Length;
        testObject = tobject;
        PrecomputeSimplifications();
        this.noDuplicate = noDuplicate;
    }

    private void PrecomputeSimplifications()
    {
        //Debug.Log("Precomputing simplifications...");
        currentStep = vertices.Length - 1;
        if (!noDuplicate)
        {
            CheckRepeatVertices();
        }
        GenerateTriDict();
        while (currentStep >= selfVertStartInd)
        {
            StepChangeDataInd stepData = SimplifyOneStep();
            if (stepData == null)
            {
                break;
            }
            stepChangeData.Add(stepData);
        }
        stepChangeData.Reverse();
        currentStep = vertices.Length - 1;
        GenerateTriDict();
        for (int submeshInd = 0; submeshInd < subMeshCount; submeshInd++)
        {
            currentTriangles[submeshInd] = new List<int>();
            for (int j = 0; j < vertices.Length; j++)
            {
                if (trianglesVert[submeshInd].ContainsKey(j))
                {
                    for (int k = 0; k < trianglesVert[submeshInd][j].Count; k++)
                    {
                        currentTriangles[submeshInd].Add(trianglesVert[submeshInd][j][k]);
                    }
                }
            }
        }
        targetStep = vertices.Length - 1;
        UpdateMesh();
    }

    private void GenerateTriDict()
    {
        trianglesVert = new Dictionary<int, List<int>>[subMeshCount];
        for (int submeshInd = 0; submeshInd < subMeshCount; submeshInd++)
        {
            List<int> triangles = currentTriangles[submeshInd];
            trianglesVert[submeshInd] = new Dictionary<int, List<int>>();
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int lastInd = Mathf.Max(triangles[i], triangles[i + 1], triangles[i + 2]);
                if (!trianglesVert[submeshInd].ContainsKey(lastInd))
                {
                    trianglesVert[submeshInd][lastInd] = new List<int>();
                }
                trianglesVert[submeshInd][lastInd].Add(triangles[i]);
                trianglesVert[submeshInd][lastInd].Add(triangles[i + 1]);
                trianglesVert[submeshInd][lastInd].Add(triangles[i + 2]);
            }
        }
    }

    private void CheckRepeatVertices()
    {
        repeatedVertices = new Dictionary<int, int>();
        Dictionary<int, int> newVerticesMap = new Dictionary<int, int>();
        Dictionary<int, int> repeatedVerticesCopy = new Dictionary<int, int>();
        int[] newVerticeArray = new int[vertices.Length];
        int count = newVerticeArray.Length - 1;
        for (int submeshInd = 0; submeshInd < subMeshCount; submeshInd++)
        {
            List<int> triangles = currentTriangles[submeshInd];
            SortedSet<int> subMeshVertsSorted = new SortedSet<int>();
            for (int subMeshVertInd = 0; subMeshVertInd < triangles.Count; subMeshVertInd++)
            {
                subMeshVertsSorted.Add(triangles[subMeshVertInd]);
            }
            int[] subMeshVerts = subMeshVertsSorted.ToArray();

            for (int i = subMeshVerts.Length - 1; i >= 0; i--)
            {
                for (int j = 0; j < i; j++)
                {
                    int largerV = subMeshVerts[i];
                    int smallerV = subMeshVerts[j];
                    if (Vector3.Distance(vertices[largerV], vertices[smallerV]) < 0.01f)
                    {
                        if (!repeatedVerticesCopy.ContainsKey(largerV))
                        {
                            repeatedVerticesCopy.Add(largerV, smallerV);
                            newVerticeArray[count] = largerV;
                            newVerticesMap.Add(largerV, count);
                            count--;
                            break;
                        }
                    }
                }
            }
        }

        count = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (!repeatedVerticesCopy.ContainsKey(i))
            {
                newVerticeArray[count] = i;
                newVerticesMap.Add(i, count);
                count++;
            }
        }

        Vector3[] newVertices = new Vector3[vertices.Length];
        Vector3[] newNormals = new Vector3[vertices.Length];
        Vector4[] newTangents = new Vector4[vertices.Length];
        Color[] newColors = new Color[vertices.Length];
        Vector2[] newUVs = new Vector2[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            int oriInd = newVerticeArray[i];
            newVertices[i] = vertices[oriInd];
            newNormals[i] = normals[oriInd];
            newTangents[i] = tangents[oriInd];
            newColors[i] = colors[oriInd];
            newUVs[i] = uvs[oriInd];
        }

        vertices = newVertices;
        normals = newNormals;
        tangents = newTangents;
        colors = newColors;
        uvs = newUVs;

        for (int submeshInd = 0; submeshInd < subMeshCount; submeshInd++)
        {
            List<int> triangles = currentTriangles[submeshInd];
            for (int subMeshVertInd = 0; subMeshVertInd < triangles.Count; subMeshVertInd++)
            {
                triangles[subMeshVertInd] = newVerticesMap[triangles[subMeshVertInd]];
            }
            currentTriangles[submeshInd] = triangles;
        }

        foreach (int key in repeatedVerticesCopy.Keys)
        {
            repeatedVertices.Add(newVerticesMap[key], newVerticesMap[repeatedVerticesCopy[key]]);
        }
    }

    private StepChangeDataInd SimplifyOneStep()
    {
        StepChangeDataInd newStepData = new StepChangeDataInd();
        int vertexToRemove = currentStep;

        //if (!noDuplicate)
        //{
        //    if (repeatedVertices.Count > 0) {
        //        vertexToRemove = repeatedVertices.Keys.First();
        //        newStepData.removedVertices.Add(vertexToRemove);
        //        newStepData.addedVertices.Add(repeatedVertices[vertexToRemove]);
        //        repeatedVertices.Remove(vertexToRemove);
        //        return newStepData;
        //    } else
        //    {
        //        return null;
        //    }
        //}
        newStepData.removedVertices.Add(vertexToRemove);
        newStepData.addedVertices.Add(vertexToRemove);
        newStepData.removedTriangles = new List<int>[subMeshCount];
        newStepData.addedTriPoses = new List<int>[subMeshCount];
        newStepData.addedTriangles = new List<int>[subMeshCount];
        for (int submeshInd = 0; submeshInd < subMeshCount; submeshInd++)
        {
            newStepData.removedTriangles[submeshInd] = new List<int>();
            newStepData.addedTriPoses[submeshInd] = new List<int>();
            newStepData.addedTriangles[submeshInd] = new List<int>();
        }

        for (int submeshInd = 0; submeshInd < subMeshCount; submeshInd++)
        {
            if (!trianglesVert[submeshInd].ContainsKey(vertexToRemove))
                continue;

            List<int> newTriangles = new List<int>();
            int nextMaxVert = -1;
            //if (trianglesVert[submeshInd][vertexToRemove].Count > 0)
            //{
            //    nextMaxVert = trianglesVert[submeshInd][vertexToRemove][0];
            //}
            for (int i = 0; i < trianglesVert[submeshInd][vertexToRemove].Count; i++)
            {
                if (trianglesVert[submeshInd][vertexToRemove][i] < vertexToRemove && trianglesVert[submeshInd][vertexToRemove][i] > nextMaxVert)
                {
                    nextMaxVert = trianglesVert[submeshInd][vertexToRemove][i];
                }
            }
            if (!noDuplicate && repeatedVertices.ContainsKey(vertexToRemove))
            {
                nextMaxVert = repeatedVertices[vertexToRemove];
                //Debug.Log($"{submeshInd}.. {vertexToRemove}.. {nextMaxVert}.. {Vector3.Distance(vertices[vertexToRemove], vertices[nextMaxVert])}");
            }
            if (nextMaxVert < 0)
            {
                currentStep--;
                return newStepData;
            }

            for (int i = 0; i < trianglesVert[submeshInd][vertexToRemove].Count; i += 3)
            {
                if (trianglesVert[submeshInd][vertexToRemove][i] != nextMaxVert && trianglesVert[submeshInd][vertexToRemove][i + 1] != nextMaxVert &&
                    trianglesVert[submeshInd][vertexToRemove][i + 2] != nextMaxVert)
                {
                    newTriangles.Add(trianglesVert[submeshInd][vertexToRemove][i] != vertexToRemove ? trianglesVert[submeshInd][vertexToRemove][i] : nextMaxVert);
                    newTriangles.Add(trianglesVert[submeshInd][vertexToRemove][i + 1] != vertexToRemove ? trianglesVert[submeshInd][vertexToRemove][i + 1] : nextMaxVert);
                    newTriangles.Add(trianglesVert[submeshInd][vertexToRemove][i + 2] != vertexToRemove ? trianglesVert[submeshInd][vertexToRemove][i + 2] : nextMaxVert);
                }
            }

            string log = $"removed vertex: {vertexToRemove}.. submeshInd: {submeshInd}.. triangles it has: {string.Join(',', trianglesVert[submeshInd][vertexToRemove])}.. " +
                $"next max vertex: {nextMaxVert}";
            if (trianglesVert[submeshInd].ContainsKey(nextMaxVert))
            {
                log += $".. old triangles of next: {string.Join(',', trianglesVert[submeshInd][nextMaxVert])}";
            }
            else
            {
                trianglesVert[submeshInd][nextMaxVert] = new List<int>();
                log += $".. no old triangles but creating";
            }

            newTriangles = SortTriangles(newTriangles);
            for (int t = 0; t < newTriangles.Count / 3; t++)
            {
                int t1 = newTriangles[t * 3];
                int t2 = newTriangles[t * 3 + 1];
                int t3 = newTriangles[t * 3 + 2];
                int insertInd = Mathf.Max(t1, t2, t3);
                int newTrisPos = 0;
                for (int i = 0; i <= insertInd; i++)
                {
                    if (trianglesVert[submeshInd].ContainsKey(i))
                    {
                        log += $".. {i}, {trianglesVert[submeshInd][i].Count / 3}";
                        newTrisPos += trianglesVert[submeshInd][i].Count / 3;
                    }
                }
                if (!trianglesVert[submeshInd].ContainsKey(insertInd))
                {
                    trianglesVert[submeshInd][insertInd] = new List<int>();
                }
                trianglesVert[submeshInd][insertInd].Add(t1);
                trianglesVert[submeshInd][insertInd].Add(t2);
                trianglesVert[submeshInd][insertInd].Add(t3);
                newStepData.addedTriPoses[submeshInd].Add(newTrisPos);
            }

            //int newTrisPos = 0;
            //for (int i = 0; i <= nextMaxVert; i++)
            //{
            //    if (trianglesVert[submeshInd].ContainsKey(i))
            //    {
            //        log += $".. {i}, {trianglesVert[submeshInd][i].Count / 3}";
            //        newTrisPos += trianglesVert[submeshInd][i].Count / 3;
            //    }
            //}
            //trianglesVert[submeshInd][nextMaxVert].AddRange(newTriangles);
            //for (int i = 0; i < newTriangles.Count / 3; i++)
            //{
            //    newStepData.addedTriPoses[submeshInd].Add(i + newTrisPos);
            //}
            newStepData.removedTriangles[submeshInd] = trianglesVert[submeshInd][vertexToRemove];
            newStepData.addedTriangles[submeshInd] = newTriangles;
            log += $".. added triposes: {string.Join(',', newStepData.addedTriPoses[submeshInd])}" +
                $".. added triangles: {string.Join(',', newStepData.addedTriangles[submeshInd])}";
            //Debug.Log(log);
        }
        currentStep--;
        return newStepData;
    }

    public static List<int> SortTriangles(List<int> triangleList)
    {
        // Ensure the input list is a multiple of 3 (valid triangle list)
        if (triangleList.Count % 3 != 0)
        {
            throw new ArgumentException("The input list must contain a multiple of 3 elements.");
        }

        // Create a list of tuples (max value, triangle indices)
        List<(int maxIndex, int a, int b, int c)> triangles = new List<(int, int, int, int)>();

        for (int i = 0; i < triangleList.Count / 3; i++)
        {
            int a = triangleList[i * 3];
            int b = triangleList[i * 3 + 1];
            int c = triangleList[i * 3 + 2];

            int maxIndex = Math.Max(a, Math.Max(b, c));

            triangles.Add((maxIndex, a, b, c));
        }

        // Sort by max index in ascending order
        triangles = triangles.OrderBy(t => t.maxIndex).ToList();

        // Reconstruct the sorted triangle list
        List<int> sortedList = new List<int>();
        foreach (var (maxIndex, a, b, c) in triangles)
        {
            sortedList.Add(a);
            sortedList.Add(b);
            sortedList.Add(c);
        }

        return sortedList;
    }

    public void UpdateStep()
    {
        targetStep = Mathf.Clamp(targetStep, selfVertStartInd, vertices.Length - 1);

        if (currentStep < targetStep)
        {
            ApplyStepChange(stepChangeData[currentStep + 1 - selfVertStartInd], false);
            UpdateMesh();
        }
        else if (currentStep > targetStep)
        {
            ApplyStepChange(stepChangeData[currentStep - selfVertStartInd], true);
            UpdateMesh();
        }
    }

    private void ApplyStepChange(StepChangeDataInd stepData, bool simplify)
    {
        //Debug.Log($"{stepData.removedVertices[0]}.. {string.Join(',', stepData.removedTriangles[11])}");
        if (simplify)
        {
            for (int submeshInd = 0; submeshInd < subMeshCount; submeshInd++)
            {
                //Debug.Log($"{submeshInd}, {currentTriangles[submeshInd].Count}");
                string log = "";
                log += $"removed vertex: {stepData.removedVertices[0]}.. submeshInd: {submeshInd}.. tri count: {currentTriangles[submeshInd].Count / 3}";
                List<int> trianglesRemoved = new List<int>();
                for (int i = currentTriangles[submeshInd].Count - stepData.removedTriangles[submeshInd].Count; i < stepData.removedTriangles[submeshInd].Count; i++)
                {
                    trianglesRemoved.Add(currentTriangles[submeshInd][i]);
                }
                log += $".. removed triangles {string.Join(',', trianglesRemoved)}";

                currentTriangles[submeshInd].RemoveRange(currentTriangles[submeshInd].Count - stepData.removedTriangles[submeshInd].Count, stepData.removedTriangles[submeshInd].Count);
                log += $".. supposely removed triangles {string.Join(',', stepData.removedTriangles[submeshInd])}.. " +
                    $"tri count: {currentTriangles[submeshInd].Count / 3}";
                List<int> newTriangles = new List<int>();
                int count = 0;
                for (int i = 0; i < currentTriangles[submeshInd].Count / 3 + stepData.addedTriPoses[submeshInd].Count; i++)
                {
                    if (count < stepData.addedTriPoses[submeshInd].Count && i == stepData.addedTriPoses[submeshInd][count])
                    {
                        newTriangles.Add(stepData.addedTriangles[submeshInd][count * 3]);
                        newTriangles.Add(stepData.addedTriangles[submeshInd][count * 3 + 1]);
                        newTriangles.Add(stepData.addedTriangles[submeshInd][count * 3 + 2]);
                        count++;
                    }
                    else
                    {
                        newTriangles.Add(currentTriangles[submeshInd][(i - count) * 3]);
                        newTriangles.Add(currentTriangles[submeshInd][(i - count) * 3 + 1]);
                        newTriangles.Add(currentTriangles[submeshInd][(i - count) * 3 + 2]);
                    }
                }
                currentTriangles[submeshInd] = newTriangles;
                log += $".. supposed added tri pos: {string.Join(',', stepData.addedTriPoses[submeshInd])}.. " +
                    $"supposed added tri: {string.Join(',', stepData.addedTriangles[submeshInd])}.. " +
                    $"tri count: {currentTriangles[submeshInd].Count / 3}";
                //Debug.Log(log);
                //Debug.Log($"{submeshInd}, {currentTriangles[submeshInd].Count}");
            }
            
            currentStep--;
        }
        else
        {
            for (int submeshInd = 0; submeshInd < subMeshCount; submeshInd++)
            {
                //Debug.Log($"{submeshInd}, {currentTriangles[submeshInd].Count}");
                List<int> newTriangles = new List<int>();
                int count = 0;
                for (int i = 0; i < currentTriangles[submeshInd].Count / 3; i++)
                {
                    if (count < stepData.addedTriPoses[submeshInd].Count && i == stepData.addedTriPoses[submeshInd][count])
                    {
                        count++;
                    }
                    else
                    {
                        newTriangles.Add(currentTriangles[submeshInd][i * 3]);
                        newTriangles.Add(currentTriangles[submeshInd][i * 3 + 1]);
                        newTriangles.Add(currentTriangles[submeshInd][i * 3 + 2]);
                    }
                }
                currentTriangles[submeshInd] = newTriangles;

                currentTriangles[submeshInd].AddRange(stepData.removedTriangles[submeshInd]);
                //Debug.Log($"{submeshInd}, {currentTriangles[submeshInd].Count}");
            }
            currentStep++;
        }
    }

    private void UpdateMesh()
    {
        Mesh sliceMesh = new Mesh
        {
            vertices = vertices,
            normals = normals,
            tangents = tangents,
            colors = colors,
            uv = uvs
        };

        sliceMesh.subMeshCount = subMeshCount;
        for (int subMeshInd = 0; subMeshInd < subMeshCount; subMeshInd++)
        {
            sliceMesh.SetTriangles(currentTriangles[subMeshInd], subMeshInd);
            //if (subMeshInd == 11)
            //{
            //    Debug.Log($"{subMeshInd}, {string.Join(',', currentTriangles[subMeshInd])}");
            //}
        }

        sliceMesh.RecalculateNormals();
        sliceMesh.RecalculateBounds();

        testObject.GetComponent<MeshFilter>().mesh = sliceMesh;
    }
}

[System.Serializable]
public class StepChangeDataInd
{
    public List<int> removedVertices; // Indices of vertices removed at this step
    public List<int>[] removedTriangles; // Indices of triangles removed at this step
    public int removeIndex, removeCount;

    public List<int> addedVertices;
    public List<int>[] addedTriangles; // Triangles added back at this step (during recovery)
    public List<int>[] addedTriPoses;

    public StepChangeDataInd()
    {
        removedVertices = new List<int>();
        removedTriangles = new List<int>[1];
        addedVertices = new List<int>();
        addedTriangles = new List<int>[1];
        addedTriPoses = new List<int>[1];
    }
}
