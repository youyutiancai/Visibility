using UnityEngine;
using UnityEngine.XR;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using System;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine.UI;
using System.Globalization;

[RequireComponent(typeof(ObjectChunkManager))]
[RequireComponent(typeof(ObjectTableManager))]
[RequireComponent(typeof(ResourceLoader))]
public class SimulatorManager : MonoBehaviour
{
    public Transform cameraRig;
    public GameObject completeSceneObject;
    public string jsonlFilePath = "Assets/Data/ClientLogData/with_interrupt.jsonl";
    public TMP_Text logTimePerFrame;
    public Button toggleSimulateButton;
    public TMP_Dropdown fileSelectionDropdown;
    public TMP_Dropdown modeDropdown;
    public CameraSetupManager cameraSetupManager;
    public TMP_Text packetLossText;
    private bool startSimulating = false;
    private List<LogEntry> logEntries;
    private int currentEntryIndex = 0;
    private double startTime;
    private double nextFrameTime;
    private double deltaTime;
    private double logDeltaTime;
    private double logBaseTime; // reference timestamp from first entry
    private ObjectChunkManager chunkManager;
    private ObjectTableManager objectTableManager;
    private ResourceLoader resourceLoader;
    private Dictionary<int, bool[]> receivedChunks;
    private Dictionary<int, GameObject> visualizedObjects = new Dictionary<int, GameObject>();
    private Dictionary<int, Vector3[]> verticesDict = new Dictionary<int, Vector3[]>();
    private Dictionary<int, Vector3[]> normalsDict = new Dictionary<int, Vector3[]>();
    private Dictionary<int, List<List<int>>> trianglesDict = new Dictionary<int, List<List<int>>>();
    private float[] reusableFloatBuffer = new float[57 * 6];
    private int[] reusableIntBuffer = new int[1024];
    private int captureFrameCount = 0;
    private Dictionary<int, int> totalExpectedChunks = new Dictionary<int, int>();
    private Dictionary<int, int> totalReceivedChunks = new Dictionary<int, int>();

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
        [JsonIgnore]
        public double time;  // Use double for high-precision relative time

        private string _originalTimeString;
        public string originalTime => _originalTimeString;

        public HeadsetData headset;
        public List<ChunkData> chunks;

        public LogEntry(double relativeTime, string originalTimeStr, HeadsetData headsetData, List<ChunkData> chunkList)
        {
            time = relativeTime;
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
        objectTableManager = GetComponent<ObjectTableManager>();
        if (objectTableManager == null)
        {
            Debug.LogError("ObjectTableManager component not found!");
            return;
        }
        resourceLoader = GetComponent<ResourceLoader>();
        if (resourceLoader == null)
        {
            Debug.LogError("ResourceLoader component not found!");
            return;
        }
        receivedChunks = new Dictionary<int, bool[]>();
        
        // Initialize the dropdown
        InitializeFileDropdown();
        InitializeModeDropdown();

        // load default jsonl file
        jsonlFilePath = Path.Combine("Assets/Data/ClientLogData", fileSelectionDropdown.options[0].text);
        LoadJsonlData();
    }

    private void InitializeModeDropdown()
    {
        if (modeDropdown == null)
        {
            Debug.LogError("Mode Dropdown not assigned!");
            return;
        }

        modeDropdown.ClearOptions();
        modeDropdown.AddOptions(new List<TMP_Dropdown.OptionData>
        {
            new TMP_Dropdown.OptionData("Replay"),
            new TMP_Dropdown.OptionData("Capture Frames - Ground Truth"),
            new TMP_Dropdown.OptionData("Capture Frames - Recevied")
        });

        modeDropdown.onValueChanged.AddListener(OnModeSelected);
    }

    private void OnModeSelected(int index)
    {
        Debug.Log($"Mode selected: {modeDropdown.options[index].text}");
        if (index == 0)
        {
            // Replay
            completeSceneObject.SetActive(false);

        }
        else if (index == 1)
        {
            // Capture Frames - Ground Truth
            completeSceneObject.SetActive(true);
            
        }
        else if (index == 2)
        {
            // Capture Frames - Recevied
            completeSceneObject.SetActive(false);
        }
    }

    private void InitializeFileDropdown()
    {
        if (fileSelectionDropdown == null)
        {
            Debug.LogError("File Selection Dropdown not assigned!");
            return;
        }

        // Clear existing options
        fileSelectionDropdown.ClearOptions();

        // Get all JSONL files from the ClientLogData directory
        string dataPath = Path.Combine(Application.dataPath, "Data", "ClientLogData");
        string[] files = Directory.GetFiles(dataPath, "*.jsonl");

        // Create dropdown options
        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
        foreach (string file in files)
        {
            string fileName = Path.GetFileName(file);
            options.Add(new TMP_Dropdown.OptionData(fileName));
        }

        // Add options to dropdown
        fileSelectionDropdown.AddOptions(options);

        // Add listener for dropdown value change
        fileSelectionDropdown.onValueChanged.AddListener(OnFileSelected);
    }

    private void OnFileSelected(int index)
    {
        string selectedFile = fileSelectionDropdown.options[index].text;
        jsonlFilePath = Path.Combine("Assets/Data/ClientLogData", selectedFile);
        LoadJsonlData();
    }

    private void CaptureFrame()
    {
        if (cameraSetupManager == null)
        {
            Debug.LogError("CameraSetupManager not assigned!");
            return;
        }

        // Determine screenshot directory based on mode and output type
        string modeFolder = modeDropdown.value switch
        {
            1 => "Screenshots_Ground_Truth",
            2 => "Screenshots_Received",
            _ => "Screenshots" // Default folder for replay mode
        };

        // Add output type subfolder
        string outputType = cameraSetupManager.currentOutputMode == CameraSetupManager.OutputMode.RGB ? "RGB" : "Depth";
        string screenshotDir = Path.Combine(Application.dataPath, "Data", modeFolder, outputType);
        if (!Directory.Exists(screenshotDir))
        {
            Directory.CreateDirectory(screenshotDir);
        }

        // Get current timestamp from log entry and format it safely for filenames
        string timestamp = logEntries[currentEntryIndex].originalTime;
        string safeTimestamp = timestamp.Replace("/", "-").Replace(":", "-").Replace(" ", "_");
        string filename = $"frame_{captureFrameCount:D4}_{safeTimestamp}.png";
        string filepath = Path.Combine(screenshotDir, filename);

        // Capture frame from the render texture
        byte[] frameData = cameraSetupManager.CaptureFrameToBytes();
        File.WriteAllBytes(filepath, frameData);
        captureFrameCount++;
    }

    void Update()
    {
        if (!startSimulating || logEntries == null || logEntries.Count == 0)
        {
            currentEntryIndex = 0;
            cameraRig.position = new Vector3(-114f, 1.7f, -100f);
            cameraRig.rotation = Quaternion.Euler(0f, 0f, 0f);
            if (packetLossText != null)
            {
                packetLossText.text = "Packet Loss Rate: --";
            }
            return;
        }

        deltaTime += Time.deltaTime;

        // Check if it's time to show the next frame
        if (deltaTime >= logDeltaTime && currentEntryIndex < logEntries.Count)
        {
            //Debug.Log($"Current Entry Index: {currentEntryIndex}, currentTime: {currentTime}, nextFrameTime: {nextFrameTime}");

            LogEntry entry = logEntries[currentEntryIndex];

            // Update head position and rotation
            cameraRig.position = entry.headset.position;
            cameraRig.rotation = Quaternion.Euler(entry.headset.rotationEuler);

            // Simulate based on mode
            if (modeDropdown.value == 0) // Replay
            {
                ProcessReceivedChunks(entry.chunks);
            }
            else if (modeDropdown.value == 1) // Capture Frames - Ground Truth
            {
                CaptureFrame();
            }
            else if (modeDropdown.value == 2) // Capture Frames - Recevied
            {
                ProcessReceivedChunks(entry.chunks);
                CaptureFrame();
            }

            // Calculate time until next frame
            if (currentEntryIndex < logEntries.Count - 1)
            {
                // double currentFrameTime = entry.time;
                // double nextFrameTimeInLog = logEntries[currentEntryIndex + 1].time;
                // nextFrameTime = currentTime + (nextFrameTimeInLog - currentFrameTime);
                logDeltaTime = logEntries[currentEntryIndex + 1].time - entry.time;
                deltaTime = 0.0;
            }

            // Update UI
            logTimePerFrame.text = entry.originalTime;
            UpdatePacketLossDisplay();

            currentEntryIndex++;
        }
    }

    private void ProcessReceivedChunks(List<ChunkData> chunks)
    {
        if (chunks == null) return;
        
        foreach (var chunk in chunks)
        {
            // Get object information from ObjectTableManager
            ObjectHolder holder = objectTableManager.GetObjectInfo(chunk.objectID);
            if (holder == null)
            {
                Debug.LogError($"Object {chunk.objectID} not found in object table!");
                continue;
            }

            // Initialize tracking for this object if not already done
            if (!totalExpectedChunks.ContainsKey(chunk.objectID))
            {
                var vertexChunks = chunkManager.GetVertexChunks(chunk.objectID);
                var triangleChunks = chunkManager.GetTriangleChunks(chunk.objectID);
                totalExpectedChunks[chunk.objectID] = (vertexChunks?.Count ?? 0) + (triangleChunks?.Count ?? 0);
                totalReceivedChunks[chunk.objectID] = 0;
            }

            // Get the chunk data from ObjectChunkManager
            if (chunkManager.HasChunk(chunk.objectID, chunk.chunkID))
            {
                totalReceivedChunks[chunk.objectID]++;
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
                    verticesDict[objectId] = new Vector3[holder.totalVertNum];
                    normalsDict[objectId] = new Vector3[holder.totalVertNum];
                    trianglesDict[objectId] = new List<List<int>>();
                    for (int i = 0; i < holder.submeshCount; i++)
                    {
                        trianglesDict[objectId].Add(new List<int>());
                    }
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
                        int baseIdx = chunkId * 57 + j; // Using 57 vertices per chunk as in the original
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
                    trianglesArr[submeshId].AddRange(new ArraySegment<int>(reusableIntBuffer, 0, count));
                }

                // Update mesh visualization
                UpdateMeshVisualization(objectId, holder);
            }
        }
    }

    private void UpdateMeshVisualization(int objectID, ObjectHolder holder)
    {
        if (!visualizedObjects.ContainsKey(objectID))
        {
            // Create new object if it doesn't exist
            GameObject newObject = new GameObject($"Object_{objectID}");
            newObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = newObject.AddComponent<MeshRenderer>();

            // Create new mesh
            Mesh newMesh = new Mesh();
            newMesh.vertices = verticesDict[objectID];
            newMesh.normals = normalsDict[objectID];
            newMesh.subMeshCount = holder.submeshCount;
            
            // Set triangles for each submesh
            for (int i = 0; i < holder.submeshCount; i++)
            {
                newMesh.SetTriangles(trianglesDict[objectID][i], i);
            }
            
            newObject.GetComponent<MeshFilter>().mesh = newMesh;

            // Set up materials using ResourceLoader
            List<Material> materials = new List<Material>();
            foreach (string matName in holder.materialNames)
            {
                materials.Add(resourceLoader.LoadMaterialByName(matName));
            }
            renderer.materials = materials.ToArray();

            // Set transform
            newObject.transform.position = holder.position;
            newObject.transform.eulerAngles = holder.eulerAngles;
            newObject.transform.localScale = holder.scale;

            visualizedObjects[objectID] = newObject;
        }
        else
        {
            // Update existing mesh
            GameObject obj = visualizedObjects[objectID];
            Mesh mesh = obj.GetComponent<MeshFilter>().mesh;
            
            mesh.vertices = verticesDict[objectID];
            mesh.normals = normalsDict[objectID];
            
            for (int i = 0; i < holder.submeshCount; i++)
            {
                mesh.SetTriangles(trianglesDict[objectID][i], i);
            }
        }
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
        if (logEntries == null)
            logEntries = new List<LogEntry>();

        logEntries.Clear();

        if (!File.Exists(jsonlFilePath))
        {
            Debug.LogError($"JSONL file not found at path: {jsonlFilePath}");
            return;
        }

        try
        {
            string[] lines = File.ReadAllLines(jsonlFilePath);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;

                var jsonObj = JObject.Parse(line);
                var headsetData = jsonObj["headset"].ToObject<HeadsetData>();
                string timeStr = ExtractRawTimeField(line);
                
                if (!DateTimeOffset.TryParseExact(
                        timeStr,
                        "yyyy-MM-ddTHH:mm:ss.fffffffZ",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out DateTimeOffset parsedTime))
                {
                    Debug.LogError($"Failed to parse: {timeStr}");
                    continue;
                }

                // calculate the absolute time of the log entry
                double absTime = (parsedTime - DateTimeOffset.UnixEpoch).TotalSeconds;
                if (i == 0)
                    logBaseTime = absTime;
                double relativeTime = absTime - logBaseTime;

                List<ChunkData> chunkList = jsonObj["chunks"]?.ToObject<List<ChunkData>>() ?? new List<ChunkData>();

                logEntries.Add(new LogEntry(relativeTime, timeStr, headsetData, chunkList));
            }

            Debug.Log($"Successfully loaded {logEntries.Count} log entries");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading JSONL file: {e.Message}");
        }
    }

    // helper function to extract the raw time field from the json line
    private string ExtractRawTimeField(string jsonLine)
        {
            int timeStart = jsonLine.IndexOf("\"time\":\"") + 8;
            int timeEnd = jsonLine.IndexOf("\"", timeStart);
            return jsonLine.Substring(timeStart, timeEnd - timeStart);
        }

    public void CalculateAndLogPacketLossRates()
    {
        float totalLossRate = 0f;
        int totalObjects = totalExpectedChunks.Count;
        
        Debug.Log("=== Packet Loss Rate Analysis ===");
        foreach (var objectId in totalExpectedChunks.Keys)
        {
            int expected = totalExpectedChunks[objectId];
            int received = totalReceivedChunks[objectId];
            float lossRate = 1f - ((float)received / expected);
            
            // Debug.Log($"Object {objectId}:");
            // Debug.Log($"  Expected chunks: {expected}");
            // Debug.Log($"  Received chunks: {received}");
            // Debug.Log($"  Loss rate: {lossRate:P2}");
            
            totalLossRate += lossRate;
        }
        
        if (totalObjects > 0)
        {
            float averageLossRate = totalLossRate / totalObjects;
            Debug.Log($"=== Summary ===");
            Debug.Log($"Total objects: {totalObjects}");
            Debug.Log($"Average packet loss rate: {averageLossRate:P2}");
        }
    }

    private void UpdatePacketLossDisplay()
    {
        if (packetLossText == null) return;

        if (totalExpectedChunks.Count == 0)
        {
            packetLossText.text = "Packet Loss Rate: --";
            return;
        }

        float totalLossRate = 0f;
        int totalObjects = totalExpectedChunks.Count;
        int totalExpected = 0;
        int totalReceived = 0;

        foreach (var objectId in totalExpectedChunks.Keys)
        {
            totalExpected += totalExpectedChunks[objectId];
            totalReceived += totalReceivedChunks[objectId];
        }

        float overallLossRate = 1f - ((float)totalReceived / totalExpected);
        packetLossText.text = $"Packet Loss Rate: {overallLossRate:P2}\n" +
                             $"Objects: {totalObjects}\n" +
                             $"Received: {totalReceived}/{totalExpected} chunks";
    }

    public void StartSimulation()
    {
        if (!startSimulating)
        {
            // Start simulation
            startSimulating = true;
            currentEntryIndex = 0;
            deltaTime = 0.0;
            logDeltaTime = 0.0;
            captureFrameCount = 0; // Reset frame counter

            // Reset packet loss tracking
            totalExpectedChunks.Clear();
            totalReceivedChunks.Clear();

            // Reset packet loss display
            if (packetLossText != null)
            {
                packetLossText.text = "Packet Loss Rate: --";
            }

            toggleSimulateButton.GetComponentInChildren<TMP_Text>().text = "Reset";
        }
        else
        {
            // Calculate and log packet loss rates before resetting
            CalculateAndLogPacketLossRates();

            // Reset simulation
            startSimulating = false;
            currentEntryIndex = 0;
            
            // Reset camera position
            if (cameraRig != null)
            {
                cameraRig.position = new Vector3(-114f, 1.7f, -100f);
                cameraRig.rotation = Quaternion.Euler(0f, 0f, 0f);
            }

            // Clear all visualized objects
            foreach (var obj in visualizedObjects.Values)
            {
                if (obj != null)
                {
                    Destroy(obj);
                }
            }
            visualizedObjects.Clear();

            // Clear all data structures
            verticesDict.Clear();
            normalsDict.Clear();
            trianglesDict.Clear();
            receivedChunks.Clear();

            // Reset UI
            if (logTimePerFrame != null)
            {
                logTimePerFrame.text = "Time log......";
                toggleSimulateButton.GetComponentInChildren<TMP_Text>().text = "Simulate";
            }
            if (packetLossText != null)
            {
                packetLossText.text = "Packet Loss Rate: --";
            }
        }
    }
}
