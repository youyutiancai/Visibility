using System.Collections.Generic;
using System.IO;
using UnityEngine;

public abstract class User : MonoBehaviour
{
    [HideInInspector]
    public Camera userCamera;
    [HideInInspector]
    public int[] clusterReceived, indiReceived, preindiReceived;
    [HideInInspector]
    public PriorityQueue<int, long, float, (int, int)> ChunksWaitToSend;

    public SyntheticPathNode[] path;
    public Vector3 offset;
    protected List<SyntheticPathNode> possibleNodes;
    public float speed;
    public int currentNodeIndex = 0, preX, preZ;
    private Dictionary<int, long[]> chunkPlanned;
    private Dictionary<int, int[]> chunkSentTimes;

    public int ClusterId { get; set; } = -1;  // -1 indicates unvisited

    protected VisibilityCheck vc;
    protected ClusterControl cc;

    public User(Vector3 initialPos)
    {
    }

    private void Start()
    {
        vc = VisibilityCheck.Instance;
        cc = ClusterControl.Instance;
        userCamera = GetComponent<Camera>();
        currentNodeIndex = 0;
        clusterReceived = new int[vc.objectsInScene.Count];
        indiReceived = new int[vc.objectsInScene.Count];
        preindiReceived = new int[vc.objectsInScene.Count];
        chunkPlanned = new Dictionary<int, long[]>();
        chunkSentTimes = new Dictionary<int, int[]>();
        preX = 0;
        preZ = 0;
    }

    public void UpdateVisibleObjects(long[] visibleObjects, ref long[] newObjectsCount)
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
                newObjectsCount[i] += visibleObjects[i];
            }
        }
    }

    public void UpdateVisibleChunks(Dictionary<int, long[]> visibleChunks, ref Dictionary<int, long[]> newChunksToSend)
    {
        if (vc == null)
        {
            Start();
        }
        foreach (int objectID in visibleChunks.Keys)
        {
            long[] allChunksFootprint = visibleChunks[objectID];
            for (int j = 0; j < allChunksFootprint.Length; j++)
            {
                if (allChunksFootprint[j] > 0)
                {
                    if (!chunkPlanned.ContainsKey(objectID))
                    {
                        chunkPlanned[objectID] = new long[allChunksFootprint.Length];
                    }
                    if (chunkPlanned[objectID][j] == 0)
                    {
                        chunkPlanned[objectID][j] = allChunksFootprint[j];
                        if (!newChunksToSend.ContainsKey(objectID))
                        {
                            newChunksToSend[objectID] = new long[allChunksFootprint.Length];
                        }
                        newChunksToSend[objectID][j] += allChunksFootprint[j];
                    }
                }
            }
        }
    }

    public void CleanChunksWaitToSend()
    {
        ChunksWaitToSend = new PriorityQueue<int, long, float, (int, int)>();
    }

    public void UpdateChunkToSend(Dictionary<int, long[]> visibleChunks, long[] objectFootprints, bool ifUseChunkFootprint)
    {
        if (vc == null)
        {
            Start();
        }
        foreach (int objectID in visibleChunks.Keys)
        {
            long[] allChunksFootprint = visibleChunks[objectID];
            for (int j = 0; j < allChunksFootprint.Length; j++)
            {
                if (allChunksFootprint[j] > 0)
                {
                    if (!chunkSentTimes.ContainsKey(objectID))
                    {
                        chunkSentTimes[objectID] = new int[allChunksFootprint.Length];
                    }
                    if (chunkSentTimes[objectID][j] < cc.numChunkRepeat && !ChunksWaitToSend.Contains((objectID, j)))
                    {
                        long priority = ifUseChunkFootprint ? allChunksFootprint[j] : objectFootprints[objectID];
                        ChunksWaitToSend.Enqueue(0, (objectID, j), priority, cc.numChunkRepeat - chunkSentTimes[objectID][j], 0);
                    }
                }
            }
        }
    }

    public void MarkAsSent(int objectID, int ChunkID, int chunkCountForObject, int sentCounts)
    {
        if (!chunkSentTimes.ContainsKey(objectID))
        {
            chunkSentTimes[objectID] = new int[chunkCountForObject];
        }
        chunkSentTimes[objectID][ChunkID]++;
        if (ChunksWaitToSend.Contains((objectID, ChunkID)))
        {
            ChunksWaitToSend.DecreaseCount((objectID, ChunkID), sentCounts);
        }
    }

    public void MarkAsSentMaxCount(int objectID, int ChunkID, int chunkCountForObject)
    {
        if (!chunkSentTimes.ContainsKey(objectID))
        {
            chunkSentTimes[objectID] = new int[chunkCountForObject];
        }
        chunkSentTimes[objectID][ChunkID] = cc.numChunkRepeat;
        if (ChunksWaitToSend.Contains((objectID, ChunkID)))
        {
            ChunksWaitToSend.Remove((objectID, ChunkID));
        }
    }

    public void UpdateChunksPlanned(Dictionary<int, long[]> visibleChunks)
    {
        foreach (int objectID in visibleChunks.Keys)
        {
            long[] allChunksFootprint = visibleChunks[objectID];
            for (int j = 0; j < allChunksFootprint.Length; j++)
            {
                if (allChunksFootprint[j] > 0)
                {
                    if (!chunkPlanned.ContainsKey(objectID))
                    {
                        chunkPlanned[objectID] = new long[allChunksFootprint.Length];
                    }
                    if (chunkPlanned[objectID][j] == 0)
                    {
                        chunkPlanned[objectID][j] = allChunksFootprint[j];
                    }
                }
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
