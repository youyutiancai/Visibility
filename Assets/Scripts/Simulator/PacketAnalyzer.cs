using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using Newtonsoft.Json;

public class PacketAnalyzer : MonoBehaviour
{
    [Header("JSON File Names")]
    public string jsonFile1 = "fix_multicast_1.json";
    public string jsonFile2 = "fix_multicast_2.json";
    public string jsonFile3 = "fix_multicast_3.json";

    [Header("Analysis Results")]
    public bool showAnalysisResults = true;

    // Data structures to store loaded chunk reception data
    private Dictionary<int, bool[]> chunkReceptionData1;
    private Dictionary<int, bool[]> chunkReceptionData2;
    private Dictionary<int, bool[]> chunkReceptionData3;

    // Reference to ObjectChunkManager
    private ObjectChunkManager objectChunkManager;

    void Start()
    {
        // Get reference to ObjectChunkManager
        objectChunkManager = FindObjectOfType<ObjectChunkManager>();
        if (objectChunkManager == null)
        {
            Debug.LogError("ObjectChunkManager not found in scene!");
            return;
        }
        
        LoadAllJsonFiles();

        ComputeCommonChunksLoss();
    }

    void Update()
    {
        
    }

    /// <summary>
    /// Loads all three JSON files containing chunk reception data
    /// </summary>
    private void LoadAllJsonFiles()
    {
        chunkReceptionData1 = LoadJsonFile(jsonFile1);
        chunkReceptionData2 = LoadJsonFile(jsonFile2);
        chunkReceptionData3 = LoadJsonFile(jsonFile3);
    }

    /// <summary>
    /// Loads a single JSON file using ObjectChunkManager's LoadReceivedChunksData function
    /// </summary>
    /// <param name="filename">Name of the JSON file to load</param>
    /// <returns>Dictionary mapping object IDs to their chunk reception status arrays</returns>
    private Dictionary<int, bool[]> LoadJsonFile(string filename)
    {
        var result = new Dictionary<int, bool[]>();
        objectChunkManager.LoadReceivedChunksData(ref result, filename);
        return result;
    }

    /// <summary>
    /// Computes common chunk loss patterns across all three datasets
    /// </summary>
    private void ComputeCommonChunksLoss()
    {
        Debug.Log("=== COMPUTING COMMON CHUNK LOSS PATTERNS ===");

        // Get all unique object IDs across all datasets
        var allObjectIds = new HashSet<int>();
        if (chunkReceptionData1 != null) allObjectIds.UnionWith(chunkReceptionData1.Keys);
        if (chunkReceptionData2 != null) allObjectIds.UnionWith(chunkReceptionData2.Keys);
        if (chunkReceptionData3 != null) allObjectIds.UnionWith(chunkReceptionData3.Keys);

        Debug.Log($"Total unique objects across all datasets: {allObjectIds.Count}");

        // Find objects that exist in all three datasets
        var commonObjects = new List<int>();
        foreach (int objectId in allObjectIds)
        {
            bool inAllDatasets = (chunkReceptionData1?.ContainsKey(objectId) ?? false) &&
                                (chunkReceptionData2?.ContainsKey(objectId) ?? false) &&
                                (chunkReceptionData3?.ContainsKey(objectId) ?? false);
            
            if (inAllDatasets)
            {
                commonObjects.Add(objectId);
            }
        }

        Debug.Log($"Objects present in all three datasets: {commonObjects.Count}");


        // Find chunks that were lost in all three datasets for the same object
        FindCommonlyLostChunks(commonObjects);
    }

   
    /// <summary>
    /// Finds chunks that were commonly lost across all datasets
    /// </summary>
    /// <param name="commonObjects">List of objects present in all datasets</param>
    private void FindCommonlyLostChunks(List<int> commonObjects)
    {
        Debug.Log("\n=== COMMONLY LOST CHUNKS ANALYSIS ===");

        var commonlyLostChunks = new Dictionary<int, List<int>>(); // objectId -> list of commonly lost chunk IDs

        foreach (int objectId in commonObjects)
        {
            var dataset1 = chunkReceptionData1[objectId];
            var dataset2 = chunkReceptionData2[objectId];
            var dataset3 = chunkReceptionData3[objectId];

            int chunkCount = dataset1.Length;
            var lostChunkIds = new List<int>();

            // Find chunks lost in all three datasets
            for (int chunkId = 0; chunkId < chunkCount; chunkId++)
            {
                if (!dataset1[chunkId] && !dataset2[chunkId] && !dataset3[chunkId])
                {
                    lostChunkIds.Add(chunkId);
                }
            }

            if (lostChunkIds.Count > 0)
            {
                commonlyLostChunks[objectId] = lostChunkIds;
                // Debug.Log($"Object {objectId}: {lostChunkIds.Count} chunks lost in all datasets");
                // Debug.Log($"  Lost chunk IDs: {string.Join(", ", lostChunkIds)}");
            }
        }

        // Summary statistics
        int totalCommonlyLostChunks = 0;
        foreach (var kvp in commonlyLostChunks)
        {
            totalCommonlyLostChunks += kvp.Value.Count;
        }

        Debug.Log($"  Total commonly lost chunks: {totalCommonlyLostChunks}");
        Debug.Log($"  Objects with commonly lost chunks: {commonlyLostChunks.Count}");

        // Find the most problematic objects (most commonly lost chunks)
        // var sortedObjects = commonlyLostChunks.OrderByDescending(kvp => kvp.Value.Count).ToList();
        // if (sortedObjects.Count > 0)
        // {
        //     Debug.Log($"\nTop 5 objects with most commonly lost chunks:");
        //     for (int i = 0; i < Mathf.Min(5, sortedObjects.Count); i++)
        //     {
        //         var kvp = sortedObjects[i];
        //         Debug.Log($"  Object {kvp.Key}: {kvp.Value.Count} commonly lost chunks");
        //     }
        // }
    }
}
