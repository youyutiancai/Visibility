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
    public PixelErrorEvaluator pixelErrorEvaluator;
    public TMP_Text m_DepthErrorLog;
    public Transform cameraRig;
    public GameObject sceneRoot;
    public GridDivide gd;
    public string jsonlFilePath = "Assets/Data/ClientLogData/with_interrupt.jsonl";
    public TMP_Text logTimePerFrame;
    public TMP_Text pathText;
    public TMP_Text testPhaseText;
    public Button toggleSimulateButton;
    public TMP_Dropdown fileSelectionDropdown;
    public TMP_Dropdown modeDropdown;
    public TMP_Dropdown visibilityTypeDropdown;
    public CameraSetupManager cameraSetupManager;
    public TMP_Text packetLossText;
    public TMP_Text elapsedTimeText;

    [Header("Chunk Set Log File Name")]
    public string logFileNameForRecvChunk = "chunkset_x";

    [Header("Server Send Head Position")]
    public Vector3 serverSendHeadPosition = new Vector3(0f, 0f, 0f);


    private bool startSimulating = false;
    private List<LogEntry> logEntries;
    private int currentEntryIndex = 0;
    private double deltaTime;
    private double logDeltaTime;
    private double logBaseTime; // reference timestamp from first entry
    private ObjectChunkManager chunkManager;
    // private int totalObjectsSent;
    // private int totalChunksSent;
    private ObjectTableManager objectTableManager;
    private ResourceLoader resourceLoader;
    private GameObject chunksVisReceivedRoot;
    private Dictionary<int, GameObject> visualizedObjects = new Dictionary<int, GameObject>();
    private Dictionary<int, Vector3[]> verticesDict = new Dictionary<int, Vector3[]>();
    private Dictionary<int, Vector3[]> normalsDict = new Dictionary<int, Vector3[]>();
    private Dictionary<int, List<List<int>>> trianglesDict = new Dictionary<int, List<List<int>>>();
    private float[] reusableFloatBuffer = new float[57 * 6];
    private int[] reusableIntBuffer = new int[1024];
    private int captureFrameCount = 0;
    //private Dictionary<int, int> totalExpectedChunks = new Dictionary<int, int>();
    private Dictionary<int, int> totalReceivedChunks = new Dictionary<int, int>();
    private Dictionary<int, bool[]> isReceivedChunks;  // record the chunk i whehter received in object j

    private SimulatorVisibility simulatorVisibility;
    
    // Elapsed time tracking
    private double firstChunkTime = -1.0;
    private double currentElapsedTime = 0.0;
    
    // Path tracking for reset detection
    private string currentPath = "";

    // CSV logging for packet loss
    private StreamWriter packetLossCsvWriter;
    private string packetLossCsvPath;

    // CSV logging for percentage pixel of depth error
    private StreamWriter depthPixelErrorCsvWriter;
    private string depthPixelErrorCsvPath; 

    // visibility check variables
    private float epsilon = 1f;    // Radius for clustering

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

        [JsonProperty("path")]
        public string path;

        [JsonProperty("test phase")]
        public string testPhase;

        public HeadsetData headset;
        public List<ChunkData> chunks;

        public LogEntry(double relativeTime, string originalTimeStr, string pathValue, string testPhaseValue, HeadsetData headsetData, List<ChunkData> chunkList)
        {
            time = relativeTime;
            _originalTimeString = originalTimeStr;
            path = pathValue;
            testPhase = testPhaseValue;
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

    // Add this field to track the current visibility type
    private SimulatorVisibility.VisibilityType visibilityType = SimulatorVisibility.VisibilityType.Chunk;

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
        isReceivedChunks = new Dictionary<int, bool[]>();

        chunksVisReceivedRoot = new GameObject("ChunksVisReceivedRoot");
        
        // Initialize SimulatorVisibility FIRST
        if (sceneRoot != null && gd != null)
        {
            sceneRoot.SetActive(true);
            simulatorVisibility = new SimulatorVisibility(sceneRoot, gd, chunkManager, objectTableManager, resourceLoader);
            sceneRoot.SetActive(false);
        }
        else
            Debug.LogError("sceneRoot or gd not assigned! SimulatorVisibility cannot be initialized.");

        // Initialize the dropdowns
        InitializeFileDropdown();
        InitializeModeDropdown();
        InitializeVisibilityTypeDropdown();

        // Initialize UI displays
        if (elapsedTimeText != null)
        {
            elapsedTimeText.text = "Elapsed Time: --";
        }
        if (pathText != null)
        {
            pathText.text = "Path: --";
        }
        if (testPhaseText != null)
        {
            testPhaseText.text = "Test Phase: --";
        }

        // load default jsonl file
        jsonlFilePath = Path.Combine("Assets/Data/ClientLogData", fileSelectionDropdown.options[0].text);
        LoadUserLogJsonlData();
    }

    private void InitializePacketLossCsv(string pathId = "null_path")
    {
        try
        {
            // Close existing CSV if any
            if (packetLossCsvWriter != null)
            {
                packetLossCsvWriter.Close();
                packetLossCsvWriter.Dispose();
            }

            string dataDir = Path.Combine(Application.dataPath, "Data/PacketLossLog");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            // Get the current log file name without extension
            string logFileName = Path.GetFileNameWithoutExtension(jsonlFilePath);
            string pathSuffix = string.IsNullOrEmpty(pathId) ? "" : $"_{pathId}";
            packetLossCsvPath = Path.Combine(dataDir, $"packet_loss_{logFileName}{pathSuffix}.csv");
            
            packetLossCsvWriter = new StreamWriter(packetLossCsvPath, false, System.Text.Encoding.UTF8);
            packetLossCsvWriter.WriteLine("ElapsedTime,PacketLossRate");
            
            Debug.Log($"Packet loss CSV initialized for path '{pathId}' at: {packetLossCsvPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to initialize packet loss CSV: {e.Message}");
        }
    }

    private void InitializeDepthPixelErrorCsv(string pathId = "null_path")
    {
        try
        {
            // Close existing CSV if any
            if (depthPixelErrorCsvWriter != null)
            {
                depthPixelErrorCsvWriter.Close();
                depthPixelErrorCsvWriter.Dispose();
            }

            string dataDir = Path.Combine(Application.dataPath, "Data/DepthPixelErrorLog");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            // Get the current log file name without extension
            string logFileName = Path.GetFileNameWithoutExtension(jsonlFilePath);
            string pathSuffix = string.IsNullOrEmpty(pathId) ? "" : $"_{pathId}";
            depthPixelErrorCsvPath = Path.Combine(dataDir, $"depth_pixel_error_{logFileName}{pathSuffix}.csv");
            
            depthPixelErrorCsvWriter = new StreamWriter(depthPixelErrorCsvPath, false, System.Text.Encoding.UTF8);
            depthPixelErrorCsvWriter.WriteLine("ElapsedTime,PixelErrorPercent");
            
            Debug.Log($"Pixel error CSV initialized for path '{pathId}' at: {depthPixelErrorCsvPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to initialize pixel error CSV: {e.Message}");
        }
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
            new TMP_Dropdown.OptionData("Capture Frames - Recevied"),
            new TMP_Dropdown.OptionData("Depth Error Analysis")
        });

        modeDropdown.onValueChanged.AddListener(OnModeSelected);
    }

    private void OnModeSelected(int index)
    {
        Debug.Log($"Mode selected: {modeDropdown.options[index].text}");
        if (index == 0)
        {
            // Replay
            //sceneRoot.SetActive(false);
            simulatorVisibility.ShowVisibilityGroundTruthByType(visibilityType, false);

        }
        else if (index == 1)
        {
            // Capture Frames - Ground Truth (noted: the current ground truth is based on the visibility check table)
            // sceneRoot.SetActive(true);
            // simulatorVisibility.SetVisibilityObjectsInScene(cameraRig.position, epsilon);
            simulatorVisibility.ShowVisibilityGroundTruthByType(visibilityType, true);
        }
        else if (index == 2)
        {
            // Capture Frames - Recevied
            //sceneRoot.SetActive(false);
            simulatorVisibility.ShowVisibilityGroundTruthByType(visibilityType, false);
        }
        else if (index == 3)
        {
            // Optimized for Computing depth error by depth map between ground truth and received
            //sceneRoot.SetActive(false);
            simulatorVisibility.ShowVisibilityGroundTruthByType(visibilityType, false);
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
        
        LoadUserLogJsonlData();
    }


    private void InitializeVisibilityTypeDropdown()
    {
        if (visibilityTypeDropdown == null)
        {
            Debug.LogError("Visibility Type Dropdown not assigned!");
            return;
        }
        visibilityTypeDropdown.ClearOptions();
        visibilityTypeDropdown.AddOptions(new List<TMP_Dropdown.OptionData>
        {
            new TMP_Dropdown.OptionData("Chunk"),
            new TMP_Dropdown.OptionData("Object")
        });
        visibilityTypeDropdown.value = (int)visibilityType;
        visibilityTypeDropdown.onValueChanged.AddListener(OnVisibilityTypeSelected);
    }

    private void OnVisibilityTypeSelected(int index)
    {
        visibilityType = (SimulatorVisibility.VisibilityType)index;
        Debug.Log($"Visibility type changed to: {visibilityType}");
    }

    private void DepthErrorAnalaysisPerFrame()
    {
        
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
            3 => "Screenshots_Depth_Analysis",
            _ => "Screenshots" // Default folder for replay mode
        };

        // Add output type subfolder
        string outputType = cameraSetupManager.currentOutputMode == CameraSetupManager.OutputMode.RGB ? "RGB" : "Depth";
        string selectedFile = Path.GetFileNameWithoutExtension(fileSelectionDropdown.options[fileSelectionDropdown.value].text);
        string screenshotDir = Path.Combine(Application.dataPath, "Data", modeFolder, outputType, selectedFile);
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
            // noted: the current user position is hardcoded (fixed), not updatd per frame
            currentEntryIndex = 0;
            cameraRig.position = serverSendHeadPosition;
            cameraRig.rotation = Quaternion.Euler(0f, 0f, 0f);
            if (packetLossText != null)
            {
                packetLossText.text = "Packet Loss Rate: --";
            }
            return;
        }

        deltaTime += Time.deltaTime;

        // Check if it's time to show the next frame
        if ((deltaTime >= logDeltaTime || deltaTime >= 1.0) && currentEntryIndex < logEntries.Count)
        {
            //Debug.Log($"Current Entry Index: {currentEntryIndex}, currentTime: {currentTime}, nextFrameTime: {nextFrameTime}");

            LogEntry entry = logEntries[currentEntryIndex];

            // Update head position and rotation
            cameraRig.position = entry.headset.position;
            cameraRig.rotation = Quaternion.Euler(entry.headset.rotationEuler);

            // Check if path has changed and reset if necessary
            if (currentPath != "" && currentPath != entry.path)
            {
                ResetForPathChange();
            }
            else if (currentPath == "")
            {
                currentPath = entry.path;
                InitializePacketLossCsv(entry.path);
                InitializeDepthPixelErrorCsv(entry.path);
            }

            // Dynamic Visibility Check
            DynamicVisibilityCheck(entry.headset.position);

            // Simulate based on mode
            if (modeDropdown.value == 0) // Replay
            {
                ProcessReceivedChunksGrouped(entry.chunks);
            }
            else if (modeDropdown.value == 1) // Capture Frames - Ground Truth
            {
                CaptureFrame();
            }
            else if (modeDropdown.value == 2) // Capture Frames - Recevied
            {
                ProcessReceivedChunksGrouped(entry.chunks);
                CaptureFrame();
            }
            else if (modeDropdown.value == 3) // Depth Error Analysis
            {
                ProcessReceivedChunksGrouped(entry.chunks);
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
            if (pathText != null)
                pathText.text = $"Path: {entry.path}";
            if (testPhaseText != null)
                testPhaseText.text = $"Test Phase: {entry.testPhase}";
            UpdateElapsedTimeDisplay(entry.time);
            UpdatePacketLossDisplay();

            currentEntryIndex++;
        }
    }

    private void ProcessReceivedChunksGrouped(List<ChunkData> chunks)
    {
        if (chunks == null) return;

        // Track the first chunk received time
        if (firstChunkTime < 0 && chunks.Count > 0)
        {
            firstChunkTime = logEntries[currentEntryIndex].time;
            Debug.Log($"First chunk received at time: {firstChunkTime:F3} seconds");
        }
        
        foreach (var chunk in chunks)
        {
            ObjectHolder holder = objectTableManager.GetObjectInfo(chunk.objectID);
            if (holder == null)
            {
                Debug.LogError($"Object {chunk.objectID} not found in object table!");
                continue;
            }

            if (!totalReceivedChunks.ContainsKey(chunk.objectID))
            {
                totalReceivedChunks[chunk.objectID] = 0;
                isReceivedChunks[chunk.objectID] = new bool[chunkManager.GetChunkCount(chunk.objectID)];
            }

            // Get the chunk data from ObjectChunkManager
            if (chunkManager.HasChunk(chunk.objectID, chunk.chunkID))
            {
                // update the pacekt received status
                totalReceivedChunks[chunk.objectID]++;
                isReceivedChunks[chunk.objectID][chunk.chunkID] = true;
                
                byte[] packet = chunkManager.GetChunk(chunk.objectID, chunk.chunkID);
                
                // Parse the packet header in the grouped format
                int cursor = 0;
                char submeshType = BitConverter.ToChar(packet, cursor);
                int objectId = BitConverter.ToInt32(packet, cursor += 2);
                int chunkId = BitConverter.ToInt32(packet, cursor += sizeof(int));
                int submeshId = BitConverter.ToInt32(packet, cursor += sizeof(int));
                int headerSize = cursor += sizeof(int);

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

                cursor = 0;
                int vertexCount = BitConverter.ToInt32(chunkData, cursor); cursor += sizeof(int);
                for (int i = 0; i < vertexCount; i++)
                {
                    int index = BitConverter.ToInt32(chunkData, cursor); cursor += sizeof(int);

                    // Skip decoding if already filled
                    if (verticesArr[index] != Vector3.zero)
                    {
                        cursor += 6 * sizeof(float); // skip position + normal
                        continue;
                    }

                    float x = BitConverter.ToSingle(chunkData, cursor); cursor += sizeof(float);
                    float y = BitConverter.ToSingle(chunkData, cursor); cursor += sizeof(float);
                    float z = BitConverter.ToSingle(chunkData, cursor); cursor += sizeof(float);
                    verticesArr[index] = new Vector3(x, y, z);

                    float nx = BitConverter.ToSingle(chunkData, cursor); cursor += sizeof(float);
                    float ny = BitConverter.ToSingle(chunkData, cursor); cursor += sizeof(float);
                    float nz = BitConverter.ToSingle(chunkData, cursor); cursor += sizeof(float);
                    normalsArr[index] = new Vector3(nx, ny, nz);
                }


                int triangleCount = BitConverter.ToInt32(chunkData, cursor); cursor += sizeof(int);
                for (int i = 0; i < triangleCount; i++)
                {
                    int tri = BitConverter.ToInt32(chunkData, cursor); cursor += sizeof(int);
                    trianglesArr[submeshId].Add(tri);  // [chunk.subMeshIdx] temporary: replace back if log include submeshIdx
                }

                // Update mesh visualization
                UpdateMeshVisualization(objectId, holder);
            }
            else
            {
                Debug.Log($"Chunk {chunk.objectID}, {chunk.chunkID} not found in ObjectChunkManager!");
            }
        }
    }

    // Archived function for ProcessReceivedChunks, Now use ProcessReceivedChunksGrouped instead
    private void ProcessReceivedChunks(List<ChunkData> chunks)
    {
        if (chunks == null) return;
        
        // Track the first chunk received time
        if (firstChunkTime < 0 && chunks.Count > 0)
        {
            firstChunkTime = logEntries[currentEntryIndex].time;
            Debug.Log($"First chunk received at time: {firstChunkTime:F3} seconds");
        }
        
        foreach (var chunk in chunks)
        {
            // Get object information from ObjectTableManager
            ObjectHolder holder = objectTableManager.GetObjectInfo(chunk.objectID);
            if (holder == null)
            {
                Debug.LogError($"Object {chunk.objectID} not found in object table!");
                continue;
            }

            if (!totalReceivedChunks.ContainsKey(chunk.objectID))
            {
                totalReceivedChunks[chunk.objectID] = 0;
                isReceivedChunks[chunk.objectID] = new bool[chunkManager.GetChunkCount(chunk.objectID)];
            }

            // Get the chunk data from ObjectChunkManager
            if (chunkManager.HasChunk(chunk.objectID, chunk.chunkID))
            {
                // update the pacekt received status
                totalReceivedChunks[chunk.objectID]++;
                isReceivedChunks[chunk.objectID][chunk.chunkID] = true;
                
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
            newObject.layer = LayerMask.NameToLayer(Commons.RECEIVED_LAYER_NAME);
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

            // IMPORTANT: Set the recompute the mesh bounds to avoid camera culling issues
            newMesh.RecalculateBounds();
            
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
            newObject.transform.SetParent(chunksVisReceivedRoot.transform);

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

            // IMPORTANT: Set the recompute the mesh bounds to avoid camera culling issues
            mesh.RecalculateBounds();
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

    private void LoadUserLogJsonlData()
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
                
                // Extract new fields
                string pathValue = jsonObj["path"]?.ToString() ?? "";
                string testPhaseValue = jsonObj["test phase"]?.ToString() ?? "";
                
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

                logEntries.Add(new LogEntry(relativeTime, timeStr, pathValue, testPhaseValue, headsetData, chunkList));
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

    public void CalculatePacketLog()
    {   
        int totalObjectsSent = simulatorVisibility.GetTotalObjectsAndChunksSent(visibilityType).Item1;
        int totalChunksSent = simulatorVisibility.GetTotalObjectsAndChunksSent(visibilityType).Item2;
        
        if (totalObjectsSent > 0 && totalChunksSent > 0)
        {
            // Calculate total received chunks
            int totalReceivedChunksCount = 0;
            foreach (var receivedCount in totalReceivedChunks.Values)
            {
                totalReceivedChunksCount += receivedCount;
            }
            
            // Calculate received objects (objects that have received at least one chunk)
            int receivedObjects = totalReceivedChunks.Count;
            
            // Calculate packet loss rate based on total chunks vs received chunks
            float packetLossRate = 1f - ((float)totalReceivedChunksCount / totalChunksSent);
            
            // Log the results
            Debug.Log("=== Packet Loss Analysis ===");
            Debug.Log($"Total Objects: {totalObjectsSent}");
            Debug.Log($"Received Objects: {receivedObjects}");
            Debug.Log($"Object Reception Rate: {(float)receivedObjects / totalObjectsSent:P2}");
            Debug.Log($"Total Available Chunks: {totalChunksSent}");
            Debug.Log($"Total Loss Chunks: {totalChunksSent - totalReceivedChunksCount}");
            Debug.Log($"Packet Loss Rate: {packetLossRate:P2}");
            Debug.Log($"Chunk Reception Rate: {(float)totalReceivedChunksCount / totalChunksSent:P2}");

            // output the received chunks to a file
            chunkManager.SaveReceivedChunksData(isReceivedChunks, logFileNameForRecvChunk);
        }
        else
        {
            Debug.LogWarning("No objects or chunks available for packet loss analysis");
        }
    }

    private void LogDepthPixelErrorData()
    {
        if (pixelErrorEvaluator == null || depthPixelErrorCsvWriter == null) return;
        
        try
        {
            var pixelErrorHistory = pixelErrorEvaluator.GetPixelErrorHistory();
            
            foreach (var (time, pixelError) in pixelErrorHistory)
            {
                string csvLine = $"{time:F3},{pixelError:F4}";
                depthPixelErrorCsvWriter.WriteLine(csvLine);
            }
            
            depthPixelErrorCsvWriter.Flush();
            Debug.Log($"Logged {pixelErrorHistory.Count} pixel error measurements to CSV");
            
            // Clear the history after logging
            pixelErrorEvaluator.ClearHistory();
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to log pixel error data: {e.Message}");
        }
    }

    private void UpdatePacketLossDisplay()
    {
        if (packetLossText == null) return;

        if (totalReceivedChunks.Count == 0)
        {
            packetLossText.text = "Packet Loss Rate: --";
            return;
        }

        int totalObjectsReceived = 0;
        int totalChunksReceived = 0;
        int totalObjectsSent = 0;
        int totalChunksSent = 0;

        Dictionary<int, long[]> chunkFootprintInfo = simulatorVisibility.GetChunkFootprintInfo();
        totalObjectsSent = chunkFootprintInfo.Count;

        foreach (var objectFootprints in chunkFootprintInfo)
        {
            bool isObjectReceived = false;
            for (int i = 0; i < objectFootprints.Value.Length; i++)
            {
                if (objectFootprints.Value[i] > 0)
                {
                    // ground truth chunk number
                    totalChunksSent++;

                    if (isReceivedChunks.ContainsKey(objectFootprints.Key))
                    {
                        if (!isObjectReceived)
                        {
                            totalObjectsReceived++;
                            isObjectReceived = true;
                        }
                        if (isReceivedChunks[objectFootprints.Key][i])
                            totalChunksReceived++;
                    }
                }
            }
        }

        float overallLossRate = 1f - ((float)totalChunksReceived / totalChunksSent);
        packetLossText.text = $"Packet Loss Rate: {overallLossRate:P2}\n" +
                             $"Received Objects: {totalObjectsReceived}/{totalObjectsSent}\n" +
                             $"Received Chunks: {totalChunksReceived}/{totalChunksSent}";

        
        // Temporary: Update the pixel error display
        m_DepthErrorLog.text = $"Pixel Error: {pixelErrorEvaluator.GetCurrentPixelError():F4}";

        // Write to CSV
        WritePacketLossToCsv(overallLossRate);
    }

    private void WritePacketLossToCsv(float lossRate)
    {
        if (packetLossCsvWriter == null) return;

        try
        {
            string csvLine = $"{currentElapsedTime:F3},{lossRate:F4}";
            packetLossCsvWriter.WriteLine(csvLine);
            packetLossCsvWriter.Flush(); // Flush immediately to ensure data is written
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to write to CSV: {e.Message}");
        }
    }



    private void UpdateElapsedTimeDisplay(double currentTime)
    {
        if (elapsedTimeText == null) return;

        if (firstChunkTime < 0)
        {
            elapsedTimeText.text = "Elapsed Time: --";
            return;
        }

        currentElapsedTime = currentTime - firstChunkTime;
        elapsedTimeText.text = $"Elapsed Time: {currentElapsedTime:F3}s";
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

            // Archived: trick to get the total objects and chunks sent
            // sceneRoot.SetActive(true);
            // simulatorVisibility.SetVisibilityObjectsInScene(serverSendHeadPosition, epsilon);
            // int[] visibility = simulatorVisibility.GetVisibleObjectInRegion();
            // ComputeChunkSentByVisibility(visibility);
            // sceneRoot.SetActive(false);

            // totalObjectsSent = simulatorVisibility.GetTotalObjectsAndChunksSent(visibilityType).Item1;
            // totalChunksSent = simulatorVisibility.GetTotalObjectsAndChunksSent(visibilityType).Item2;

            // Reset packet loss tracking
            totalReceivedChunks.Clear();
            isReceivedChunks.Clear();

            // Reset elapsed time tracking
            firstChunkTime = -1.0;
            currentElapsedTime = 0.0;
            
            // Reset path tracking
            currentPath = "";

            // Start depth pixel error logging
            if (pixelErrorEvaluator != null)
            {
                pixelErrorEvaluator.StartLogging();
            }
                 
            // Reset packet loss display
            if (packetLossText != null)
            {
                packetLossText.text = "Packet Loss Rate: --";
            }

            // Reset elapsed time display
            if (elapsedTimeText != null)
            {
                elapsedTimeText.text = "Elapsed Time: --";
            }

            toggleSimulateButton.GetComponentInChildren<TMP_Text>().text = "Reset";
        }
        else
        {
            // Calculate and log packet loss rates before resetting
            CalculatePacketLog();

            // Log pixel error data before resetting
            LogDepthPixelErrorData();

            // Reset simulation
            startSimulating = false;
            currentEntryIndex = 0;

            // Reset visibility
            simulatorVisibility.ResetVisibility();

            // Stop pixel error logging
            if (pixelErrorEvaluator != null)
            {
                pixelErrorEvaluator.StopLogging();
            }
            
            // Reset camera position
            if (cameraRig != null)
            {
                cameraRig.position = serverSendHeadPosition;
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
            
            // Reset path tracking
            currentPath = "";

            // Reset UI
            if (logTimePerFrame != null)
            {
                logTimePerFrame.text = "Time log......";
                toggleSimulateButton.GetComponentInChildren<TMP_Text>().text = "Simulate";
            }
            if (pathText != null)
            {
                pathText.text = "Path: --";
            }
            if (testPhaseText != null)
            {
                testPhaseText.text = "Test Phase: --";
            }
            if (packetLossText != null)
            {
                packetLossText.text = "Packet Loss Rate: --";
            }
            if (elapsedTimeText != null)
            {
                elapsedTimeText.text = "Elapsed Time: --";
            }
        }
    }

    // Get current path and test phase information
    public (string path, string testPhase) GetCurrentPathAndTestPhase()
    {
        if (logEntries != null && currentEntryIndex < logEntries.Count && currentEntryIndex >= 0)
        {
            return (logEntries[currentEntryIndex].path, logEntries[currentEntryIndex].testPhase);
        }
        return ("--", "--");
    }

    // Reset packet loss and visualization data when path changes
    private void ResetForPathChange()
    {
        Debug.Log($"Path changed from '{currentPath}' to '{logEntries[currentEntryIndex].path}'. Resetting packet loss and visualization data.");
        
        // Reset packet loss tracking
        totalReceivedChunks.Clear();
        isReceivedChunks.Clear();
        
        // Get the new path and create a new CSV file for it
        string newPath = logEntries[currentEntryIndex].path;
        InitializePacketLossCsv(newPath);
        
        LogDepthPixelErrorData();
        InitializeDepthPixelErrorCsv(newPath);
        
        // Reset elapsed time tracking
        firstChunkTime = -1.0;
        currentElapsedTime = 0.0;

        // Reset visibility
        simulatorVisibility.ResetVisibility();
        
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
        
        // Reset UI displays
        if (packetLossText != null)
        {
            packetLossText.text = "Packet Loss Rate: --";
        }
        if (elapsedTimeText != null)
        {
            elapsedTimeText.text = "Elapsed Time: --";
        }
        
        // Update current path
        currentPath = logEntries[currentEntryIndex].path;
    }

    // Might need to be refactored if users/clusters are dynamic
    public void ComputeAllVisibility()
    {
        simulatorVisibility.ComputeVisibility(serverSendHeadPosition, epsilon);
    }

    // Dynamic Visibility Check (for Grouth Truth and Packet Loss Monitoring)
    private void DynamicVisibilityCheck(Vector3 currentHeadPosition)
    {
        simulatorVisibility.ComputeDynamicVisibility(currentHeadPosition, epsilon, logEntries[currentEntryIndex].testPhase);
    }

    private void OnDestroy()
    {
        if (packetLossCsvWriter != null)
        {
            packetLossCsvWriter.Close();
            packetLossCsvWriter.Dispose();
        }

        if (depthPixelErrorCsvWriter != null)
        {
            depthPixelErrorCsvWriter.Close();
            depthPixelErrorCsvWriter.Dispose();
        }
    }

    // Public method to get current CSV file info
    public string GetCurrentCsvInfo()
    {
        string packetLossInfo = packetLossCsvWriter != null ? 
            $"Packet Loss: {packetLossCsvPath}" : "No packet loss CSV active";
        string pixelErrorInfo = depthPixelErrorCsvWriter != null ? 
            $"Pixel Error: {depthPixelErrorCsvPath}" : "No pixel error CSV active";
        return $"{packetLossInfo}\n{pixelErrorInfo}";
    }

    public double GetCurrentElapsedTime()
    {
        return currentElapsedTime;
    }
}
