using UnityEngine;
using UnityEngine.XR;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using System;
using Newtonsoft.Json.Linq;
using TMPro;

[RequireComponent(typeof(ObjectChunkManager))]
public class SimulatorManager : MonoBehaviour
{
    public Transform cameraRig;
    public string jsonlFilePath = "Assets/Data/ClientLogData/with_interrupt.jsonl";
    public TMP_Text logTimePerFrame;
    private bool startSimulating = false;
    private List<LogEntry> logEntries;
    private int currentEntryIndex = 0;
    private float startTime;
    private float nextFrameTime;
    private ObjectChunkManager chunkManager;
    private Dictionary<int, bool[]> receivedChunks; // objectID -> array of received status for each chunk
    private Dictionary<int, GameObject> visualizedObjects = new Dictionary<int, GameObject>();
    private Dictionary<int, Vector3[]> verticesDict = new Dictionary<int, Vector3[]>();
    private Dictionary<int, Vector3[]> normalsDict = new Dictionary<int, Vector3[]>();
    private Dictionary<int, List<List<int>>> trianglesDict = new Dictionary<int, List<List<int>>>();
    private float[] reusableFloatBuffer = new float[57 * 6];
    private int[] reusableIntBuffer = new int[1024];

    [Serializable]
    private class ChunkData
    {
        [JsonProperty("objectID")]
        public int objectID;
        [JsonProperty("chunkID")]
        public int chunkID;
        [JsonProperty("type")]
        public string type;
        [JsonProperty("chunkRecvTime")]
        public string chunkRecvTime;
        [JsonProperty("subMeshIdx")]
        public int subMeshIdx; // nullable, only present for some chunks
    }

    [Serializable]
    private class LogEntry
    {
        [JsonProperty("time")]
        private DateTime _time;
        private string _originalTimeString;
        
        public float time => (float)(_time - DateTime.UnixEpoch).TotalSeconds;
        public string originalTime => _originalTimeString;
        public HeadsetData headset;
        public List<ChunkData> chunks;

        public LogEntry(DateTime time, string originalTimeStr, HeadsetData headsetData, List<ChunkData> chunkList)
        {
            _time = time;
            _originalTimeString = originalTimeStr;
            headset = headsetData;
            chunks = chunkList;
        }
    }

    [Serializable]
    private class HeadsetData
    {
        [JsonProperty("position")]
        private float[] _positionArray;
        
        [JsonProperty("rotationEuler")]
        private float[] _rotationArray;

        [JsonIgnore]
        public Vector3 position => new Vector3(_positionArray[0], _positionArray[1], _positionArray[2]);
        
        [JsonIgnore]
        public Vector3 rotationEuler => new Vector3(_rotationArray[0], _rotationArray[1], _rotationArray[2]);
    }

    void Start()
    {
        chunkManager = GetComponent<ObjectChunkManager>();
        if (chunkManager == null)
        {
            Debug.LogError("ObjectChunkManager component not found!");
            return;
        }
        receivedChunks = new Dictionary<int, bool[]>();
        LoadJsonlData();
    }

    void Update()
    {
        if (!startSimulating || logEntries == null || logEntries.Count == 0)
        {
            currentEntryIndex = 0;
            cameraRig.position = new Vector3(-114f, 1.7f, -100f);
            cameraRig.rotation = Quaternion.Euler(0f, 0f, 0f);
            return;
        }

        float currentTime = Time.time - startTime;

        // Check if it's time to show the next frame
        if (currentTime >= nextFrameTime && currentEntryIndex < logEntries.Count)
        {
            LogEntry entry = logEntries[currentEntryIndex];

            // Update head position and rotation
            cameraRig.position = entry.headset.position;
            cameraRig.rotation = Quaternion.Euler(entry.headset.rotationEuler);

            // Process received chunks
            ProcessReceivedChunks(entry.chunks);

            // Update UI
            logTimePerFrame.text = entry.originalTime;

            // Calculate time until next frame
            if (currentEntryIndex < logEntries.Count - 1)
            {
                float currentFrameTime = entry.time;
                float nextFrameTimeInLog = logEntries[currentEntryIndex + 1].time;
                nextFrameTime = currentTime + (nextFrameTimeInLog - currentFrameTime);
            }

            currentEntryIndex++;
        }
    }

    private void ProcessReceivedChunks(List<ChunkData> chunks)
    {
        if (chunks == null) return;

        foreach (var chunk in chunks)
        {
            // Get the chunk data from ObjectChunkManager
            if (chunkManager.HasChunk(chunk.objectID, chunk.chunkID))
            {
                byte[] packet = chunkManager.GetChunk(chunk.objectID, chunk.chunkID);
                
                // Parse the packet header
                char submeshType = BitConverter.ToChar(packet, 0);
                int objectId = BitConverter.ToInt32(packet, 2);
                int chunkId = BitConverter.ToInt32(packet, 6);
                int headerSize = (submeshType == 'V') ? 10 : 14;
                int submeshId = (submeshType == 'T') ? BitConverter.ToInt32(packet, 10) : -1;

                // Parse the packet data
                int dataSize = packet.Length - headerSize;
                byte[] chunkData = new byte[dataSize];
                Buffer.BlockCopy(packet, headerSize, chunkData, 0, dataSize);

                // Initialize data structures if needed
                if (!verticesDict.ContainsKey(objectId))
                {
                    verticesDict[objectId] = new Vector3[1000]; // Assuming max vertices
                    normalsDict[objectId] = new Vector3[1000];
                    trianglesDict[objectId] = new List<List<int>>();
                    trianglesDict[objectId].Add(new List<int>());
                }

                var verticesArr = verticesDict[objectId];
                var normalsArr = normalsDict[objectId];
                var trianglesArr = trianglesDict[objectId];

                if (submeshType == 'V')
                {
                    // Process vertex data
                    int count = chunkData.Length / sizeof(float);
                    Buffer.BlockCopy(chunkData, 0, reusableFloatBuffer, 0, chunkData.Length);

                    for (int j = 0; j < count / 6; j++)
                    {
                        int baseIdx = chunkId * 57 + j;
                        if (baseIdx >= verticesArr.Length)
                        {
                            Debug.LogError($"Vertex index {baseIdx} out of bounds for array size {verticesArr.Length}");
                            continue;
                        }

                        verticesArr[baseIdx] = new Vector3(
                            reusableFloatBuffer[j * 6],
                            reusableFloatBuffer[j * 6 + 1],
                            reusableFloatBuffer[j * 6 + 2]
                        );
                        normalsArr[baseIdx] = new Vector3(
                            reusableFloatBuffer[j * 6 + 3],
                            reusableFloatBuffer[j * 6 + 4],
                            reusableFloatBuffer[j * 6 + 5]
                        );
                    }
                }
                else if (submeshType == 'T')
                {
                    // Process triangle data
                    int count = chunkData.Length / sizeof(int);
                    Buffer.BlockCopy(chunkData, 0, reusableIntBuffer, 0, chunkData.Length);
                    
                    // Ensure we have enough submesh lists
                    while (trianglesArr.Count <= submeshId)
                    {
                        trianglesArr.Add(new List<int>());
                    }
                    
                    trianglesArr[submeshId].AddRange(new ArraySegment<int>(reusableIntBuffer, 0, count));
                }

                // Update mesh visualization
                UpdateMeshVisualization(objectId);
            }
        }
    }

    private void UpdateMeshVisualization(int objectID)
    {
        if (!visualizedObjects.ContainsKey(objectID))
        {
            GameObject newObject = new GameObject($"Object_{objectID}");
            newObject.AddComponent<MeshFilter>();
            newObject.AddComponent<MeshRenderer>();
            visualizedObjects[objectID] = newObject;
            newObject.GetComponent<MeshRenderer>().material = new Material(Shader.Find("Standard"));
        }

        GameObject obj = visualizedObjects[objectID];
        Mesh mesh = obj.GetComponent<MeshFilter>().mesh;
        if (mesh == null)
        {
            mesh = new Mesh();
            obj.GetComponent<MeshFilter>().mesh = mesh;
        }

        mesh.vertices = verticesDict[objectID];
        mesh.normals = normalsDict[objectID];
        mesh.subMeshCount = trianglesDict[objectID].Count;
        
        for (int i = 0; i < trianglesDict[objectID].Count; i++)
        {
            mesh.SetTriangles(trianglesDict[objectID][i], i);
        }

        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
    }

    private void OnObjectComplete(int objectID)
    {
        // Get all chunks for the complete object
        var vertexChunks = chunkManager.GetVertexChunks(objectID);
        var triangleChunks = chunkManager.GetTriangleChunks(objectID);

        if (vertexChunks != null && triangleChunks != null)
        {
            Debug.Log($"Object {objectID} is complete! Received {vertexChunks.Count} vertex chunks and {triangleChunks.Count} triangle chunks");
            // TODO: Reconstruct or visualize the complete object
        }
    }

    public bool HasReceivedAllChunks(int objectID)
    {
        if (!receivedChunks.ContainsKey(objectID)) return false;
        
        // Check if all chunks have been received
        bool[] receivedStatus = receivedChunks[objectID];
        for (int i = 0; i < receivedStatus.Length; i++)
        {
            if (!receivedStatus[i]) return false;
        }
        return true;
    }

    public void ResetReceivedChunks()
    {
        foreach (var objectID in receivedChunks.Keys)
        {
            Array.Clear(receivedChunks[objectID], 0, receivedChunks[objectID].Length);
        }
    }

    private void LoadJsonlData()
    {
        logEntries = new List<LogEntry>();
        
        if (!File.Exists(jsonlFilePath))
        {
            Debug.LogError($"JSONL file not found at path: {jsonlFilePath}");
            return;
        }

        try
        {
            string[] lines = File.ReadAllLines(jsonlFilePath);
            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;

                // Parse the JSON object
                var jsonObj = JObject.Parse(line);
                
                // Extract headset data
                var headsetData = jsonObj["headset"].ToObject<HeadsetData>();

                string timeStr = jsonObj["time"].ToString();

                // Extract chunks data
                List<ChunkData> chunkList = null;
                if (jsonObj["chunks"] != null)
                {
                    chunkList = jsonObj["chunks"].ToObject<List<ChunkData>>();
                }
                else
                {
                    chunkList = new List<ChunkData>();
                }

                // Create and add log entry
                logEntries.Add(new LogEntry(
                    DateTime.Parse(timeStr),
                    timeStr,
                    headsetData,
                    chunkList
                ));
            }

            if (logEntries.Count > 0)
            {
                Debug.Log($"Successfully loaded {logEntries.Count} log entries");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading JSONL file: {e.Message}");
        }
    }

    public void StartSimulation()
    {
        startSimulating = true;
        startTime = Time.time;
        currentEntryIndex = 0;
        nextFrameTime = 0f; // First frame will be shown immediately
    }
}
