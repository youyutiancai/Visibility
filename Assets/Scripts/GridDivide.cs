using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityTCPClient.Assets.Scripts;

public class GridDivide : Singleton<GridDivide>
{
    public GameObject city, ground, gridCornerPrefab, gridCornerParent, gridBoundPrefab, gridBoundParent;
    public GameObject testCube;
    public float gridSize = 3, lowBound = 1, highBound = 2;
    [HideInInspector]
    public int numGridX, numGridZ;

    private int[] cornerisOff, gridisOff;
    public bool hasDividedGrids;
    // Start is called before the first frame update
    void Start()
    {
        AddBoxCollider(city.transform);
        InitializeGrid();
        //hasDividedGrids = false;
    }

    private void InitializeGrid()
    {
        Bounds groundBoundingBox = ground.GetComponent<MeshRenderer>().bounds;
        gridCornerParent.transform.position = groundBoundingBox.center - groundBoundingBox.size / 2;
        gridCornerParent.transform.position = new Vector3(gridCornerParent.transform.position.x,
            -0.00003314018f, gridCornerParent.transform.position.z);
        numGridX = Mathf.CeilToInt(groundBoundingBox.size.x / gridSize);
        numGridZ = Mathf.CeilToInt(groundBoundingBox.size.z / gridSize);
    }

    private void AddBoxCollider(Transform child)
    {
        if (child.tag == "Terrain" || child.gameObject.activeSelf == false)
        {
            return;
        }
        MeshFilter mf = child.GetComponent<MeshFilter>();
        if (mf != null)
        {
            BoxCollider boxCollider = child.GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                child.gameObject.AddComponent<BoxCollider>();
            }
        }
        for (int i = 0; i < child.childCount; i++)
        {
            AddBoxCollider(child.GetChild(i));
        }
    }

    private void Update()
    {
        if (!hasDividedGrids)
        {
            Debug.Log("divide grids");
            CreateGridCorners();
            //CalculateCornerHeight();
            cornerisOff = new int[numGridX * numGridZ];
            RemoveCornersInObject(city.transform);
            RemoveGridWithCorner();
            //RemoveGridsWithObject();
            CreateGridBounds();
            hasDividedGrids = true;
        }
    }
    private void CreateGridCorners()
    {
        Bounds groundBoundingBox = ground.GetComponent<MeshRenderer>().bounds;
        for (int i = 0; i < numGridX; i++)
        {
            for (int j = 0; j < numGridZ; j++)
            {

                GameObject newGridCorner = GameObject.Instantiate(gridCornerPrefab);
                newGridCorner.transform.SetParent(gridCornerParent.transform);
                newGridCorner.transform.position = gridCornerParent.transform.position + 
                    new Vector3(gridSize * i, 0, gridSize * j);
            }
        }
        Debug.Log($"Finished creating grid corners, {gridCornerParent.transform.childCount}");
    }

    private void CalculateCornerHeight()
    {
        MeshFilter meshFilter = ground.GetComponent<MeshFilter>();
        Mesh mesh = meshFilter.mesh;
        Vector3[] localVertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Vector3[] worldVertices = new Vector3[localVertices.Length];
        for (int i = 0; i < localVertices.Length; i++)
        {
            worldVertices[i] = ground.transform.TransformPoint(localVertices[i]);
        }
    }

    private void RemoveCornersInObject(Transform child)
    {
        if (child.tag == "Terrain" || child.gameObject.activeSelf == false)
        {
            return;
        }
        MeshRenderer mr = child.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            BoxCollider boxCollider = child.GetComponent<BoxCollider>();
            Vector3[] corners = GetBoxColliderCorners(boxCollider);
            RemoveGridCornersWithinPoly(mr.bounds, corners);
        }
        for (int i = 0; i < child.childCount; i++)
        {
            RemoveCornersInObject(child.GetChild(i));
        }
    }

    Vector3[] GetBoxColliderCorners(BoxCollider boxCollider)
    {
        Vector3[] corners = new Vector3[4];
        Vector3 center = boxCollider.center;
        Vector3 extents = boxCollider.size * 0.5f;
        int index = 0;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                Vector3 localCorner = center + new Vector3(x * extents.x, y * x * extents.y, -extents.z);
                Vector3 worldCorner = boxCollider.transform.TransformPoint(localCorner);
                corners[index++] = worldCorner;
                //GameObject newGridCorner = GameObject.Instantiate(gridCornerPrefab);
                //newGridCorner.transform.SetParent(gridCornerParent.transform);
                //newGridCorner.transform.position = worldCorner;
            }
        }

        return corners;
    }

    private void RemoveGridCornersWithinPoly(Bounds objectBound, Vector3[] polyPoints)
    {
        //for (int i = 0; i < polyPoints.Length; i++)
        //{
        //    GameObject newGridBound = Instantiate(testCube);
        //    newGridBound.name = $"{i}";
        //    newGridBound.transform.SetParent(gridBoundParent.transform);
        //    newGridBound.transform.position = polyPoints[i];
        //}
        int xStartIndex = Mathf.FloorToInt((objectBound.center.x - objectBound.size.x / 2 - gridCornerParent.transform.position.x) / gridSize);
        int xEndIndex = Mathf.FloorToInt((objectBound.center.x + objectBound.size.x / 2 - gridCornerParent.transform.position.x) / gridSize);
        int zStartIndex = Mathf.FloorToInt((objectBound.center.z - objectBound.size.z / 2 - gridCornerParent.transform.position.z) / gridSize);
        int zEndIndex = Mathf.FloorToInt((objectBound.center.z + objectBound.size.z / 2 - gridCornerParent.transform.position.z) / gridSize);
        if (xStartIndex < numGridX && zStartIndex < numGridZ)
        {
            for (int i = Math.Max(0, xStartIndex); i < (xEndIndex + 1 > numGridX ? numGridX : xEndIndex + 1); i++)
            {
                for (int j = Math.Max(0, zStartIndex); j < (zEndIndex + 1 > numGridZ ? numGridZ : zEndIndex + 1); j++)
                {
                    int index = i * numGridZ + j;
                    Vector3 point = gridCornerParent.transform.position + new Vector3(i * gridSize, 0, j * gridSize);
                    //GameObject newGridBound = Instantiate(gridBoundPrefab);
                    //newGridBound.name = $"{i}_{j}";
                    //newGridBound.transform.SetParent(gridBoundParent.transform);
                    //newGridBound.transform.position = point;
                    if (IsPointInPolygon(point, polyPoints))
                    {
                        gridCornerParent.transform.GetChild(index).gameObject.SetActive(false);
                        cornerisOff[index] = 1;
                    }
                }
            }
        }
    }

    public static bool IsPointInPolygon(Vector3 point, Vector3[] polygon)
    {
        Vector2 point2D = new Vector2(point.x, point.z);
        Vector2[] polygon2D = new Vector2[polygon.Length];
        for (int i = 0; i < polygon.Length; i++)
        {
            polygon2D[i] = new Vector2(polygon[i].x, polygon[i].z);
        }
        return IsPointInPolygon2D(point2D, polygon2D);
    }

    private static bool IsPointInPolygon2D(Vector2 point, Vector2[] polygon)
    {
        int intersectCount = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 v1 = polygon[i];
            Vector2 v2 = polygon[(i + 1) % polygon.Length];
            if ((v1.y > point.y) != (v2.y > point.y))
            {
                float intersectX = (v2.x - v1.x) * (point.y - v1.y) / (v2.y - v1.y) + v1.x;
                if (point.x < intersectX)
                {
                    intersectCount++;
                }
            }
        }
        return (intersectCount % 2) == 1;
    }

    //private void RemoveCornersInObjectHelper(Transform child)
    //{
    //    if (child.tag == "Terrain" || child.gameObject.activeSelf == false)
    //    {
    //        return;
    //    }

    //    MeshFilter mf = child.GetComponent<MeshFilter>();
    //    if (mf != null)
    //    {
    //        Bounds bound = mf.sharedMesh.bounds;
    //        List<Vector3> boundCorners = new List<Vector3>();
    //        for (int i = 0; i < 2; i++)
    //        {
    //            for (int j = 0; j < 2; j++)
    //            {
    //                Vector3 boundCorner = new Vector3(bound.center.x + bound.size.x * i == 0 ? 1 : -1, 0,
    //                    bound.center.z + bound.size.z * j == 0 ? 1 : -1);
    //            }
    //        }

    //    }

    //    for (int i = 0; i < child.childCount; i++)
    //    {
    //        RemoveGridsWithObjectHelper(child.GetChild(i));
    //    }
    //}

    private void RemoveGridsWithObject()
    {
        gridisOff = new int[(numGridX - 1) * (numGridZ - 1)];
        for (int i = 0; i < city.transform.childCount; i++)
        {
            RemoveGridsWithObjectHelper(city.transform.GetChild(i));
        }
        Debug.Log("Finished removing grids with object");
    }

    private void RemoveGridsWithObjectHelper(Transform child)
    {
        if (child.tag == "Terrain" || child.gameObject.activeSelf == false)
        {
            return;
        }

        MeshRenderer mr = child.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            Bounds bound = mr.bounds;
            int xStartIndex = Mathf.FloorToInt((bound.center.x - bound.size.x / 2 - gridCornerParent.transform.position.x) / gridSize);
            int xEndIndex = Mathf.FloorToInt((bound.center.x + bound.size.x / 2 - gridCornerParent.transform.position.x) / gridSize);
            int zStartIndex = Mathf.FloorToInt((bound.center.z - bound.size.z / 2 - gridCornerParent.transform.position.z) / gridSize);
            int zEndIndex = Mathf.FloorToInt((bound.center.z + bound.size.z / 2 - gridCornerParent.transform.position.z) / gridSize);
            if (xStartIndex < numGridX - 1 && zStartIndex < numGridZ - 1)
            {
                for (int i = Math.Max(0, xStartIndex); i < (xEndIndex + 1 > numGridX - 1 ? numGridX - 1 : xEndIndex + 1); i++)
                {
                    for (int j = Math.Max(0, zStartIndex); j < (zEndIndex + 1 > numGridZ - 1 ? numGridZ - 1 : zEndIndex + 1); j++)
                    {
                        int index = j * (numGridX - 1) + i;
                        gridisOff[index] = 1;
                        //if (child.name == "civilian_house_32_d")
                        //{
                        //    Vector3 newPos = new Vector3(gridCornerParent.transform.position.x + gridSize * (index % (numGridX - 1) + 0.5f),
                        //    1.5f, gridCornerParent.transform.position.z + gridSize * (index / (numGridX - 1) + 0.5f));
                        //    Debug.Log($"{child.name}, {bound.center}, {bound.size}, {gridCornerParent.transform.position}" +
                        //    $"{xStartIndex}, {xEndIndex}, {zStartIndex}, {zEndIndex}, {numGridX}, {numGridZ}, {index}," +
                        //    $"{newPos}, {i}, {j}, {index % (numGridX - 1)}, {index / (numGridX - 1)}");
                        //}
                            
                    }
                }
            }
        }

        for (int i = 0; i < child.childCount; i++)
        {
            RemoveGridsWithObjectHelper(child.GetChild(i));
        }
    }

    private void RemoveGridWithCorner()
    {
        gridisOff = new int[(numGridX - 1) * (numGridZ - 1)];
        for (int i = 0; i < gridisOff.Length; i++)
        {
            int indexX = i / (numGridZ - 1);
            int indexZ = i % (numGridZ - 1);
            gridisOff[i] = cornerisOff[indexX * numGridZ + indexZ] == 0 && 
                cornerisOff[indexX * numGridZ + indexZ + 1] == 0 &&
                cornerisOff[(indexX + 1) * numGridZ + indexZ] == 0 &&
                cornerisOff[(indexX + 1) * numGridZ + indexZ + 1] == 0 ? 0 : 1;
        }
    }

    private void CreateGridBounds()
    {
        gridBoundParent.transform.position = gridCornerParent.transform.position;
        for (int i = 0; i < gridisOff.Length; i++)
        {
            if (gridisOff[i] == 0)
            {
                int indexX = i / (numGridZ - 1);
                int indexZ = i % (numGridZ - 1);
                Vector3 initPos = gridCornerParent.transform.position;
                //GameObject newGridBound = Instantiate(testCube);
                //newGridBound.name = $"{i}";
                //newGridBound.transform.SetParent(gridBoundParent.transform);
                //newGridBound.transform.position = new Vector3(initPos.x + gridSize * (indexX + 0.5f), 1.5f, initPos.z + gridSize * (indexZ + 0.5f));

                foreach (float bound in new float[] { lowBound, highBound })
                {
                    if (i - numGridZ + 1 >= 0 && gridisOff[i - numGridZ + 1] == 1)
                    {
                        DrawLine(new Vector3(initPos.x + gridSize * (indexX), bound, initPos.z + gridSize * (indexZ)),
                    new Vector3(initPos.x + gridSize * (indexX), bound, initPos.z + gridSize * (indexZ + 1)));
                    }
                    if (i - 1 >= 0 && gridisOff[i - 1] == 1)
                    {
                        DrawLine(new Vector3(initPos.x + gridSize * (indexX), bound, initPos.z + gridSize * (indexZ)),
                    new Vector3(initPos.x + gridSize * (indexX + 1), bound, initPos.z + gridSize * (indexZ)));
                    }
                    DrawLine(new Vector3(initPos.x + gridSize * (indexX), bound, initPos.z + gridSize * (indexZ + 1)),
                    new Vector3(initPos.x + gridSize * (indexX + 1), bound, initPos.z + gridSize * (indexZ + 1)));
                    DrawLine(new Vector3(initPos.x + gridSize * (indexX + 1), bound, initPos.z + gridSize * (indexZ)),
                    new Vector3(initPos.x + gridSize * (indexX + 1), bound, initPos.z + gridSize * (indexZ + 1)));
                }
                if ((i - numGridZ + 1 >= 0 && gridisOff[i - numGridZ + 1] == 1) && (i - 1 >= 0 && gridisOff[i - 1] == 1))
                {
                    DrawLine(new Vector3(initPos.x + gridSize * (indexX), lowBound, initPos.z + gridSize * (indexZ)),
                new Vector3(initPos.x + gridSize * (indexX), highBound, initPos.z + gridSize * (indexZ)));
                }
                if (i - numGridZ + 1 >= 0 && gridisOff[i - numGridZ + 1] == 1)
                {
                    DrawLine(new Vector3(initPos.x + gridSize * (indexX), lowBound, initPos.z + gridSize * (indexZ + 1)),
                new Vector3(initPos.x + gridSize * (indexX), highBound, initPos.z + gridSize * (indexZ + 1)));
                }
                if (i - 1 >= 0 && gridisOff[i - 1] == 1)
                {
                    DrawLine(new Vector3(initPos.x + gridSize * (indexX + 1), lowBound, initPos.z + gridSize * (indexZ)),
                new Vector3(initPos.x + gridSize * (indexX + 1), highBound, initPos.z + gridSize * (indexZ)));
                }
                DrawLine(new Vector3(initPos.x + gridSize * (indexX + 1), lowBound, initPos.z + gridSize * (indexZ + 1)),
                new Vector3(initPos.x + gridSize * (indexX + 1), highBound, initPos.z + gridSize * (indexZ + 1)));

            }
        }
        Debug.Log("Finished creating grid bounds");
    } 

    private void DrawLine(Vector3 startPoint, Vector3 endPoint)
    {
        GameObject lineObject = new GameObject("DynamicLine");
        lineObject.transform.SetParent(gridBoundParent.transform);
        LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();

        // Set LineRenderer properties
        lineRenderer.startWidth = 0.1f;
        lineRenderer.endWidth = 0.1f;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = Color.white;
        lineRenderer.endColor = Color.white;
        lineRenderer.positionCount = 2;

        // Set the positions of the LineRenderer
        lineRenderer.SetPosition(0, startPoint);
        lineRenderer.SetPosition(1, endPoint);
    }
}
