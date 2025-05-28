using UnityEngine;
using System.IO;
using System.Text;
using System;

public class ObjectTableManager : MonoBehaviour
{
    public static ObjectTableManager Instance { get; private set; }
    private ObjectHolder[] objectHolders;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        LoadObjectTable();
    }

    private void LoadObjectTable()
    {
        string filePath = Path.Combine(Application.dataPath, "Data", "object_table.bin");
        if (!File.Exists(filePath))
        {
            Debug.LogError($"Object table file not found at: {filePath}");
            return;
        }

        byte[] data = File.ReadAllBytes(filePath);
        ParseObjectTable(data);
    }

    private void ParseObjectTable(byte[] data)
    {
        int cursor = 0;
        
        // Read header information
        int tcpType = BitConverter.ToInt32(data, cursor);
        cursor += sizeof(int);
        int totalBytes = BitConverter.ToInt32(data, cursor);
        cursor += sizeof(int);
        int totalObjectNum = BitConverter.ToInt32(data, cursor);
        cursor += sizeof(int);

        Debug.Log($"HEADER: type: {tcpType}, object_num: {totalObjectNum}, total_bytes: {totalBytes}");

        // Initialize object holders array
        objectHolders = new ObjectHolder[totalObjectNum];

        // Parse each object's data
        for (int i = 0; i < totalObjectNum; i++)
        {
            objectHolders[i] = new ObjectHolder();
            
            // Parse position
            objectHolders[i].position = new Vector3(
                BitConverter.ToSingle(data, cursor),
                BitConverter.ToSingle(data, cursor += sizeof(float)),
                BitConverter.ToSingle(data, cursor += sizeof(float))
            );

            // Parse rotation
            objectHolders[i].eulerAngles = new Vector3(
                BitConverter.ToSingle(data, cursor += sizeof(float)),
                BitConverter.ToSingle(data, cursor += sizeof(float)),
                BitConverter.ToSingle(data, cursor += sizeof(float))
            );

            // Parse scale
            objectHolders[i].scale = new Vector3(
                BitConverter.ToSingle(data, cursor += sizeof(float)),
                BitConverter.ToSingle(data, cursor += sizeof(float)),
                BitConverter.ToSingle(data, cursor += sizeof(float))
            );

            // Parse mesh information
            objectHolders[i].totalVertChunkNum = BitConverter.ToInt32(data, cursor += sizeof(float));
            objectHolders[i].totalTriChunkNum = BitConverter.ToInt32(data, cursor += sizeof(int));
            objectHolders[i].totalVertNum = BitConverter.ToInt32(data, cursor += sizeof(int));
            objectHolders[i].submeshCount = BitConverter.ToInt32(data, cursor += sizeof(int));
            cursor += sizeof(int);

            // Parse material names
            objectHolders[i].materialNames = new string[objectHolders[i].submeshCount];
            for (int j = 0; j < objectHolders[i].submeshCount; j++)
            {
                int materialNameLength = BitConverter.ToInt32(data, cursor);
                objectHolders[i].materialNames[j] = Encoding.ASCII.GetString(data, cursor += sizeof(int), materialNameLength);
                cursor += materialNameLength;
            }
        }
    }

    // Get object information by ID
    public ObjectHolder GetObjectInfo(int objectID)
    {
        if (objectHolders == null || objectID < 0 || objectID >= objectHolders.Length)
        {
            Debug.LogError($"Invalid object ID: {objectID}");
            return null;
        }
        return objectHolders[objectID];
    }

    // Get total number of objects
    public int GetTotalObjectCount()
    {
        return objectHolders?.Length ?? 0;
    }

    // Get all object holders
    public ObjectHolder[] GetAllObjectHolders()
    {
        return objectHolders;
    }
} 