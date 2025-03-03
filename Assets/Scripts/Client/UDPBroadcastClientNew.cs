using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;



[Serializable]
public class RetransmissionRequest
{
    public int bundleId;
    public int[] missingChunks;
}

public class UDPBroadcastClientNew : MonoBehaviour
{
    public int port1 = 5005;  // broadcast port
    public int port2 = 5006;  // retransmit port
    private UdpClient udpClient;
    private const int HEADER_SIZE = 12; // 3 ints: bundleId, totalChunks, chunkIndex.

    // Represents an in-progress asset bundle transmission.
    private class BundleTransmission
    {
        public int totalChunks;
        public Dictionary<int, byte[]> chunks = new Dictionary<int, byte[]>();
        public DateTime firstChunkTime;
    }

    // Active transmissions mapped by bundleId.
    private Dictionary<int, BundleTransmission> activeTransmissions = new Dictionary<int, BundleTransmission>();

    void Start()
    {
        try
        {
            udpClient = new UdpClient(port1);
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpClient.BeginReceive(new AsyncCallback(ReceiveMessage), null);

            Debug.Log($"UDP client listening on port {port1}");
        }
        catch (Exception ex)
        {
            Debug.LogError("UDP client initialization failed: " + ex.Message);
        }
    }

    private void ReceiveMessage(IAsyncResult ar)
    {
        try
        {
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, port1);
            byte[] packet = udpClient.EndReceive(ar, ref remoteEP);

            if (packet.Length < HEADER_SIZE)
            {
                Debug.LogWarning("Received packet too small to contain header.");
                udpClient.BeginReceive(new AsyncCallback(ReceiveMessage), null);
                return;
            }

            // Parse the header.
            int bundleId = BitConverter.ToInt32(packet, 0);
            int totalChunks = BitConverter.ToInt32(packet, 4);
            int chunkIndex = BitConverter.ToInt32(packet, 8);

            int dataSize = packet.Length - HEADER_SIZE;
            byte[] chunkData = new byte[dataSize];
            Array.Copy(packet, HEADER_SIZE, chunkData, 0, dataSize);

            // Create or update the transmission record.
            if (!activeTransmissions.ContainsKey(bundleId))
            {
                activeTransmissions[bundleId] = new BundleTransmission { totalChunks = totalChunks, firstChunkTime = DateTime.UtcNow };
                Debug.Log($"Started receiving bundleId {bundleId} with {totalChunks} chunks.");
            }
            BundleTransmission transmission = activeTransmissions[bundleId];

            if (!transmission.chunks.ContainsKey(chunkIndex))
            {
                transmission.chunks[chunkIndex] = chunkData;
                Debug.Log($"Received chunk {chunkIndex + 1}/{totalChunks} for bundleId {bundleId}");
            }

            // If all chunks have been received, schedule reassembly and loading on the main thread.
            if (transmission.chunks.Count == transmission.totalChunks)
            {
                UnityDispatcher.Instance.Enqueue(() =>
                {
                    AssembleAndLoadBundle(bundleId, transmission);
                });
                activeTransmissions.Remove(bundleId);
            }
            else
            {
                TimeSpan currentTime = DateTime.UtcNow - transmission.firstChunkTime;
                Debug.Log($"waiting...{currentTime.TotalSeconds}");

                if (currentTime.TotalSeconds < 2f)
                {
                    return;
                }
                else
                {

                }
                    

                while (currentTime.TotalSeconds < 2f)
                {
                    
                    List<int> missingChunks = new List<int>();
                    for (int i = 0; i < transmission.totalChunks; i++)
                    {
                        if (!transmission.chunks.ContainsKey(i))
                        {
                            missingChunks.Add(i);
                        }
                    }

                    if (missingChunks.Count > 0)
                    {
                        RetransmissionRequest req = new RetransmissionRequest
                        {
                            bundleId = bundleId,
                            missingChunks = missingChunks.ToArray()
                        };
                        string jsonReq = JsonUtility.ToJson(req);
                        byte[] reqData = Encoding.UTF8.GetBytes(jsonReq);
                        // Send request to the server (assuming server IP is the one you received data from)
                        UdpClient requestClient = new UdpClient();
                        requestClient.Send(reqData, reqData.Length, remoteEP.Address.ToString(), port2);
                        requestClient.Close();
                        Debug.Log("Requested retransmission for missing chunks: " + string.Join(",", missingChunks));

                        // refresh the chunk first send time 
                        transmission.firstChunkTime = DateTime.UtcNow;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("ReceiveMessage error: " + ex.Message);
        }
        
        // keep the listening to the server
        udpClient.BeginReceive(new AsyncCallback(ReceiveMessage), null);
    }

    private void AssembleAndLoadBundle(int bundleId, BundleTransmission transmission)
    {
        Debug.Log($"All chunks received for bundleId {bundleId}. Reassembling asset bundle...");
        int totalSize = 0;
        for (int i = 0; i < transmission.totalChunks; i++)
        {
            totalSize += transmission.chunks[i].Length;
        }

        byte[] assetBundleData = new byte[totalSize];
        int offset = 0;
        for (int i = 0; i < transmission.totalChunks; i++)
        {
            byte[] chunk = transmission.chunks[i];
            Buffer.BlockCopy(chunk, 0, assetBundleData, offset, chunk.Length);
            offset += chunk.Length;
        }

        AssetBundle bundle = AssetBundle.LoadFromMemory(assetBundleData);
        if (bundle == null)
        {
            Debug.LogError("Failed to load AssetBundle.");
            return;
        }
        Debug.Log("AssetBundle loaded successfully.");

        // Attempt to load and instantiate the prefab. Adjust the asset name as necessary.
        GameObject prefab = bundle.LoadAsset<GameObject>("balcony_1");
        if (prefab != null)
        {
            Instantiate(prefab);
        }
        else
        {
            Debug.LogError("Prefab 'balcony_1' not found in AssetBundle.");
        }
    }

    void OnDestroy()
    {
        if (udpClient != null)
        {
            udpClient.Close();
        }
    }
}

