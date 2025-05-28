using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System;

public class ObjectChunkManager : MonoBehaviour
{
    private Dictionary<int, List<byte[]>> objectChunks; // objectID -> list of chunks
    private string chunksDirectory = "Assets/Data/ObjectChunks";

    // Chunk type identifiers
    private const char VERTEX_CHUNK = 'V';
    private const char TRIANGLE_CHUNK = 'T';

    public struct ChunkHeader
    {
        public char type;        // 'V' for vertex data, 'T' for triangle data
        public int objectID;     // ID of the object this chunk belongs to
        public int chunkID;      // Sequential ID of this chunk
        public int subMeshID;    // Only present in triangle chunks
    }

    void Awake()
    {
        LoadAllChunks();
    }

    private void LoadAllChunks()
    {
        objectChunks = new Dictionary<int, List<byte[]>>();

        if (!Directory.Exists(chunksDirectory))
        {
            Debug.LogError($"Chunks directory not found at: {chunksDirectory}");
            return;
        }

        try
        {
            // Get all files in the chunks directory
            string[] files = Directory.GetFiles(chunksDirectory, "object_*.bin");
            
            foreach (string file in files)
            {
                // Parse filename to get objectID
                string fileName = Path.GetFileNameWithoutExtension(file);
                string[] parts = fileName.Split('_');
                
                if (parts.Length >= 2 && int.TryParse(parts[1], out int objectID))
                {
                    // Load chunks for this object
                    List<byte[]> chunks = LoadChunksFromFile(file);
                    if (chunks != null && chunks.Count > 0)
                    {
                        objectChunks[objectID] = chunks;
                        Debug.Log($"Loaded {chunks.Count} chunks for object {objectID}");
                    }
                }
            }

            Debug.Log($"Successfully loaded {objectChunks.Count} objects");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading chunks: {e.Message}");
        }
    }

    private List<byte[]> LoadChunksFromFile(string objectFilePath)
    {
        List<byte[]> chunks = new List<byte[]>();

        try
        {
            using (BinaryReader reader = new BinaryReader(File.Open(objectFilePath, FileMode.Open)))
            {
                int chunkCount = reader.ReadInt32(); // First read number of chunks

                for (int i = 0; i < chunkCount; i++)
                {
                    int chunkSize = reader.ReadInt32();          // Read chunk size
                    byte[] chunk = reader.ReadBytes(chunkSize);  // Read chunk data
                    chunks.Add(chunk);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error reading chunks from {objectFilePath}: {e.Message}");
            return null;
        }

        return chunks;
    }

    public ChunkHeader? GetChunkHeader(int objectID, int chunkID)
    {
        if (!HasChunk(objectID, chunkID)) return null;

        byte[] chunk = objectChunks[objectID][chunkID];
        if (chunk.Length < 9) return null; // Minimum size for header (1 char + 2 ints)

        using (MemoryStream ms = new MemoryStream(chunk))
        using (BinaryReader reader = new BinaryReader(ms))
        {
            ChunkHeader header = new ChunkHeader();
            header.type = reader.ReadChar();
            header.objectID = reader.ReadInt32();
            header.chunkID = reader.ReadInt32();

            // Triangle chunks have an additional subMeshID
            if (header.type == TRIANGLE_CHUNK && chunk.Length >= 13)
            {
                header.subMeshID = reader.ReadInt32();
            }

            return header;
        }
    }

    public bool IsVertexChunk(int objectID, int chunkID)
    {
        var header = GetChunkHeader(objectID, chunkID);
        return header.HasValue && header.Value.type == VERTEX_CHUNK;
    }

    public bool IsTriangleChunk(int objectID, int chunkID)
    {
        var header = GetChunkHeader(objectID, chunkID);
        return header.HasValue && header.Value.type == TRIANGLE_CHUNK;
    }

    public bool HasChunk(int objectID, int chunkID)
    {
        return objectChunks.ContainsKey(objectID) && 
               chunkID >= 0 && 
               chunkID < objectChunks[objectID].Count;
    }

    public byte[] GetChunk(int objectID, int chunkID)
    {
        if (HasChunk(objectID, chunkID))
        {
            return objectChunks[objectID][chunkID];
        }
        return null;
    }

    public bool HasAllChunks(int objectID, List<int> chunkIDs)
    {
        if (!objectChunks.ContainsKey(objectID)) return false;
        
        foreach (int chunkID in chunkIDs)
        {
            if (chunkID < 0 || chunkID >= objectChunks[objectID].Count)
            {
                return false;
            }
        }
        return true;
    }

    public List<byte[]> GetAllChunksForObject(int objectID)
    {
        if (objectChunks.ContainsKey(objectID))
        {
            return new List<byte[]>(objectChunks[objectID]);
        }
        return null;
    }

    public int GetChunkCount(int objectID)
    {
        if (objectChunks.ContainsKey(objectID))
        {
            return objectChunks[objectID].Count;
        }
        return 0;
    }

    // Helper method to get all vertex chunks for an object
    public List<byte[]> GetVertexChunks(int objectID)
    {
        if (!objectChunks.ContainsKey(objectID)) return null;

        List<byte[]> vertexChunks = new List<byte[]>();
        foreach (var chunk in objectChunks[objectID])
        {
            if (chunk.Length > 0 && chunk[0] == VERTEX_CHUNK)
            {
                vertexChunks.Add(chunk);
            }
        }
        return vertexChunks;
    }

    // Helper method to get all triangle chunks for an object
    public List<byte[]> GetTriangleChunks(int objectID)
    {
        if (!objectChunks.ContainsKey(objectID)) return null;

        List<byte[]> triangleChunks = new List<byte[]>();
        foreach (var chunk in objectChunks[objectID])
        {
            if (chunk.Length > 0 && chunk[0] == TRIANGLE_CHUNK)
            {
                triangleChunks.Add(chunk);
            }
        }
        return triangleChunks;
    }
} 