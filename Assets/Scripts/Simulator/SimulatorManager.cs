using UnityEngine;
using UnityEngine.XR;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using System;
using Newtonsoft.Json.Linq;

public class SimulatorManager : MonoBehaviour
{
    public Transform cameraRig;
    public string jsonlFilePath = "Assets/Data/ClientLogData/with_interrupt.jsonl";
    public bool startSimulating = false;
    
    private List<LogEntry> logEntries;
    private int currentEntryIndex = 0;
    private float startTime;
    private float nextFrameTime;

    [Serializable]
    private class LogEntry
    {
        [JsonProperty("time")]
        private DateTime _time;
        
        public float time => (float)(_time - DateTime.UnixEpoch).TotalSeconds;
        public HeadsetData headset;

        public LogEntry(DateTime time, HeadsetData headsetData)
        {
            _time = time;
            headset = headsetData;
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
            // Update cameraRig position and rotation
            LogEntry entry = logEntries[currentEntryIndex];
            cameraRig.position = entry.headset.position;
            cameraRig.rotation = Quaternion.Euler(entry.headset.rotationEuler);

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
                var jsonObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(line);
                
                // Extract headset data
                var headsetData = JsonConvert.DeserializeObject<HeadsetData>(
                    jsonObj["headset"].ToString()
                );

                // Create and add log entry
                logEntries.Add(new LogEntry(
                    DateTime.Parse(jsonObj["time"].ToString()),
                    headsetData
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
