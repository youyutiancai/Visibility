using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.IO;

public class GenChunkFromObjects : MonoBehaviour
{
    private int totalObjectN = 0;
    private MeshVariant mv;

    // copy chunk information from the broadcast control
    private const int CHUNK_SIZE = 1400;
    private const int HEADER_SIZE = 12;

    void Start()
    {
        //mv = new RandomizedMesh();
        mv = new GroupedMesh();
    }

    void Update()
    {
        if (Keyboard.current.pKey.wasPressedThisFrame) 
        {
            ProcessSceneObjects();
        }
    }

    private void ProcessSceneObjects()
    {
        List<GameObject> sceneObjects = VisibilityCheck.Instance.objectsInScene;

        Debug.Log($"Scene Object N: {sceneObjects.Count}");

        //string baseFolder = Path.Combine(Application.dataPath, "Data/ObjectChunks");
        string baseFolder = Path.Combine(Application.dataPath, "Data/ObjectChunksGrouped");
        if (!Directory.Exists(baseFolder))
        {
            Directory.CreateDirectory(baseFolder);
        }

        for (int i = 0; i < sceneObjects.Count; i++)
        {
            List<byte[]> chunksOfAnObject = mv.RequestChunks(i, CHUNK_SIZE);
            string objectFilePath = Path.Combine(baseFolder, $"object_{i}.bin");

            using (BinaryWriter writer = new BinaryWriter(File.Open(objectFilePath, FileMode.Create)))
            {
                writer.Write(chunksOfAnObject.Count); // First, write the number of chunks

                foreach (byte[] chunk in chunksOfAnObject)
                {
                    writer.Write(chunk.Length);       // Write chunk size
                    writer.Write(chunk);              // Then write chunk data
                }
            }

            Debug.Log($"Saved {chunksOfAnObject.Count} chunks for object {i} in {objectFilePath}");
        }
    }

    private List<byte[]> LoadChunksFromFile(string objectFilePath)
    {
        List<byte[]> chunks = new List<byte[]>();

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

        return chunks;
    }
}
