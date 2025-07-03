using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class SimulatorVisibility
{
    private List<GameObject> objectsInScene;
    private Dictionary<string, int[]> diffInfoAdd, visibleObjectsInGrid;
    private float numInUnitX = 10f, numInUnitZ = 10f;
    private GridDivide gd;
    private GameObject sceneRoot;

    public SimulatorVisibility(GameObject sceneRoot, GridDivide gridDivide)
    {
        this.sceneRoot = sceneRoot;
        this.gd = gridDivide;
        objectsInScene = new List<GameObject>();
        diffInfoAdd = new Dictionary<string, int[]>();
        visibleObjectsInGrid = new Dictionary<string, int[]>();
        AddAllObjects(sceneRoot.transform);

        Debug.Log($"SimulatorVisibility initialized with {objectsInScene.Count} objects in scene");
    }

    private void AddAllObjects(Transform child)
    {
        if (!child.gameObject.activeSelf)
            return;
        MeshRenderer mr = child.GetComponent<MeshRenderer>();
        if (mr != null)
            objectsInScene.Add(child.gameObject);
        for (int i = 0; i < child.childCount; i++)
            AddAllObjects(child.GetChild(i));
    }

    public void SetVisibilityObjectsInScene(Vector3 head_position, float radius)
    {
        int[] visibleObjects = new int[objectsInScene.Count];
        GetVisibleObjectsInRegionProg_Internal(head_position, radius, ref visibleObjects);
        
        for (int i = 0; i < objectsInScene.Count; i++)
        {
            objectsInScene[i].SetActive(visibleObjects[i] > 0 || objectsInScene[i].tag == "Terrain");
        }
    }

    private void GetVisibleObjectsInRegionProg_Internal(Vector3 position, float radius, ref int[] objectVisibility)
    {
        int xStartIndex = Mathf.FloorToInt((position.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
        int zStartIndex = Mathf.FloorToInt((position.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
        int[] footprints = ReadFootprintGridUnit(xStartIndex, zStartIndex);
        for (int i = 0; i < footprints.Length; i++)
        {
            objectVisibility[footprints[i]] = 1;
        }
        int gridNumToInclude = Mathf.FloorToInt(radius / gd.gridSize);
        UpdateVisOnLine(xStartIndex, zStartIndex, xStartIndex - gridNumToInclude, zStartIndex, -1, 0, ref objectVisibility);
        UpdateVisOnLine(xStartIndex, zStartIndex, xStartIndex + gridNumToInclude, zStartIndex, 1, 0, ref objectVisibility);
        for (int i = xStartIndex - gridNumToInclude; i < xStartIndex + gridNumToInclude + 1; i++)
        {
            UpdateVisOnLine(i, zStartIndex, i, zStartIndex - gridNumToInclude, 0, -1, ref objectVisibility);
            UpdateVisOnLine(i, zStartIndex, i, zStartIndex + gridNumToInclude, 0, 1, ref objectVisibility);
        }
    }

    private void UpdateVisOnLine(int fromX, int fromZ, int toX, int toZ, int xStep, int zStep, ref int[] visibility)
    {
        if (fromX == toX && fromZ == toZ) { return; }
        UpdateVis(fromX, fromZ, fromX + xStep, fromZ + zStep, ref visibility);
        UpdateVisOnLine(fromX + xStep, fromZ + zStep, toX, toZ, xStep, zStep, ref visibility);
    }

    private void UpdateVis(int fromX, int fromZ, int toX, int toZ, ref int[] visibility)
    {
        int[] updatedVis = ReadFootprintsDiffUnit(fromX, fromZ, toX, toZ);
        int count = updatedVis[0];
        if (count != 0)
        {
            for (int i = 1; i < count + 1; i++)
            {
                visibility[updatedVis[i]] = 1;
            }
        }
    }

    private int[] ReadFootprintGridUnit(int x, int z)
    {
        string indiGrid = $"{x}_{z}";
        if (visibleObjectsInGrid.ContainsKey(indiGrid))
        {
            return visibleObjectsInGrid[indiGrid];
        }
        string filePath = "./Assets/Data/GridLevelVis_Unit/";
        int unitX = x / (int)numInUnitX, unitZ = z / (int)numInUnitZ;
        string fileName = $"{filePath}{unitX}_{unitZ}.bin";
        if (!File.Exists(fileName))
        {
            Debug.LogError($"{fileName} does not exist");
            return new int[objectsInScene.Count];
        }
        byte[] bytes_read = File.ReadAllBytes(fileName);
        int[] visInfo = ConvertByteArrayToIntArray(bytes_read);
        int cursor = 0;
        for (int k = 0; k < numInUnitX; k++)
        {
            for (int l = 0; l < numInUnitZ; l++)
            {
                int gridX = unitX * (int)numInUnitX + k, gridZ = unitZ * (int)numInUnitZ + l;
                int[] visibleObjects = new int[visInfo[cursor]];
                Array.Copy(visInfo, cursor + 1, visibleObjects, 0, visInfo[cursor]);
                visibleObjectsInGrid.Add($"{gridX}_{gridZ}", visibleObjects);
                cursor += 1 + visInfo[cursor];
            }
        }
        return visibleObjectsInGrid[indiGrid];
    }

    private int[] ReadFootprintsDiffUnit(int fromX, int fromZ, int toX, int toZ)
    {
        string indiDiff = $"{fromX}_{fromZ}_{toX}_{toZ}";
        if (diffInfoAdd.ContainsKey(indiDiff))
        {
            return diffInfoAdd[indiDiff];
        }
        string filePath = "./Assets/Data/GridDiff_Unit/";
        int unitX = fromX / (int)numInUnitX, unitZ = fromZ / (int)numInUnitZ;
        string fileName = $"{filePath}{unitX}_{unitZ}.bin";
        if (!File.Exists(fileName))
        {
            Debug.LogError($"{fileName} does not exist");
            return new int[objectsInScene.Count];
        }
        byte[] bytes_read = File.ReadAllBytes(fileName);
        int[] diffInfo = ConvertByteArrayToIntArray(bytes_read);
        int cursor = 0;
        for (int k = 0; k < numInUnitX; k++)
        {
            for (int l = 0; l < numInUnitZ; l++)
            {
                int gridX = unitX * (int)numInUnitX + k, gridZ = unitZ * (int)numInUnitZ + l;
                ReadUnit($"{gridX}_{gridZ}_{gridX + 1}_{gridZ}", ref cursor, diffInfo);
                ReadUnit($"{gridX}_{gridZ}_{gridX - 1}_{gridZ}", ref cursor, diffInfo);
                ReadUnit($"{gridX}_{gridZ}_{gridX}_{gridZ + 1}", ref cursor, diffInfo);
                ReadUnit($"{gridX}_{gridZ}_{gridX}_{gridZ - 1}", ref cursor, diffInfo);
            }
        }
        indiDiff = $"{fromX}_{fromZ}_{toX}_{toZ}";
        return diffInfoAdd[indiDiff];
    }

    private void ReadUnit(string indiDiff, ref int cursor, int[] diffInfo)
    {
        int numToAdd = diffInfo[cursor];
        int[] objectsAdd = new int[1 + numToAdd];
        cursor++;
        objectsAdd[0] = numToAdd;
        if (numToAdd != 0)
        {
            Array.Copy(diffInfo, cursor, objectsAdd, 1, numToAdd);
        }
        diffInfoAdd.Add(indiDiff, objectsAdd);
        cursor += numToAdd;
        int numToRemove = diffInfo[cursor];
        cursor++;
        cursor += numToRemove;
    }

    private static int[] ConvertByteArrayToIntArray(byte[] byteArray)
    {
        int length = BitConverter.ToInt32(byteArray, 0);
        int[] array = new int[length];
        Buffer.BlockCopy(byteArray, 4, array, 0, length * 4);
        return array;
    }
}
