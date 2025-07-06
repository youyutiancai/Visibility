using System.Collections.Generic;
using System.IO;
using UnityEngine;

public abstract class User : MonoBehaviour
{
    [HideInInspector]
    public Camera userCamera;
    [HideInInspector]
    public int[] clusterReceived, indiReceived, preindiReceived;

    public SyntheticPathNode[] path;
    public Vector3 offset;
    protected List<SyntheticPathNode> possibleNodes;
    public float speed;
    public int currentNodeIndex = 0, preX, preZ;

    public int ClusterId { get; set; } = -1;  // -1 indicates unvisited

    protected VisibilityCheck vc;

    public User(Vector3 initialPos)
    {
    }

    private void Start()
    {
        vc = VisibilityCheck.Instance;
        userCamera = GetComponent<Camera>();
        currentNodeIndex = 0;
        clusterReceived = new int[vc.objectsInScene.Count];
        indiReceived = new int[vc.objectsInScene.Count];
        preindiReceived = new int[vc.objectsInScene.Count];
        preX = 0;
        preZ = 0;
    }

    public void UpdateVisibleObjects(int[] visibleObjects, ref int[] newObjectsCount)
    {
        if (vc == null)
        {
            Start ();
        }
        for (int i = 0; i < vc.objectsInScene.Count; i++)
        {
            if (visibleObjects[i] > 0 && clusterReceived[i] == 0)
            {
                clusterReceived[i] = 1;
                newObjectsCount[i] = visibleObjects[i];
            }
        }
    }
    private string FormatValue(float value)
    {
        return value.ToString("G", System.Globalization.CultureInfo.InvariantCulture).Contains("E")
            ? "0"
            : value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
    }

    public List<int> UpdateVisibleObjectsIndi(int[] visibleObjects, ref int objectSentIndi, StreamWriter writer)
    {
        List<int> newObjects = new List<int>();
        for (int i = 0; i < visibleObjects.Length; i++)
        {
            if (visibleObjects[i] > 0 && indiReceived[i] == 0)
            {
                indiReceived[i] = 1;
                newObjects.Add(i);
            }

            if (writer != null && visibleObjects[i] == 1 && preindiReceived[i] == 0)
            {
                objectSentIndi++;
                Vector3 selfPos = transform.position;
                Vector3 objectPos = vc.objectsInScene[i].transform.position;
                float datasize = vc.objectDataSize[i];
                if (Vector3.Distance(selfPos, objectPos) > 50)
                {
                    datasize /= 5;
                } else if (Vector3.Distance(selfPos, objectPos) > 10)
                {
                    datasize /= 2;
                }
                writer.WriteLine($"{Time.time},{name},{i}," +
                    $"{FormatValue(selfPos.x)},{FormatValue(selfPos.y)},{FormatValue(selfPos.z)}," +
                    $"{FormatValue(objectPos.x)},{FormatValue(objectPos.y)},{FormatValue(objectPos.z)}," +
                    $"{FormatValue(Vector3.Distance(selfPos, objectPos))},{FormatValue(datasize)}");
                writer.Flush();
            }
        }
        preindiReceived = visibleObjects;
        return newObjects;
    }

    public void GenerateInitialPath(SyntheticPathNode initialNode)
    {
        path = new SyntheticPathNode[3];
        path[0] = initialNode;
        offset = transform.position - initialNode.transform.position;
        possibleNodes = path[0].connectedNodes;
        path[1] = possibleNodes[Random.Range(0, possibleNodes.Count)];
        possibleNodes = new List<SyntheticPathNode>(path[1].connectedNodes);
        possibleNodes.Remove(path[0]);
        path[2] = possibleNodes[Random.Range(0, possibleNodes.Count)];
    }
}
