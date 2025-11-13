using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.XR;

public class UDPBroadcastTest : MonoBehaviour
{
    public bool isMulticast = true;
    public int portUDP = 5005;
    private UdpClient udpClient;
    private IPEndPoint localEP;
    private string multicastAddr = "230.0.0.1";
    private IPAddress multicastAddress;
    private const int HEADER_SIZE = 12;
    private int bufferSize = 1024 * 1024;
    private bool isShuttingDown = false;
    private static UdpClient retransmissionClient = new UdpClient();

    private class MeshTransmission
    {
        public int totalMeshChunks;
        public Dictionary<int, Chunk> chunks = new Dictionary<int, Chunk>();
        public DateTime firstChunkTime;
        public IPEndPoint remoteEP;
    }

    private StreamWriter logWriter;
    private string logFilePath;

    private void Awake()
    {
        retransmissionClient = new UdpClient();
    }
    private void Start()
    {
        string filename = $"ClientRuntimeLog_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jsonl";
        logFilePath = Path.Combine(Application.persistentDataPath, filename);
        logWriter = new StreamWriter(logFilePath, append: false);
        logWriter.AutoFlush = true;
        StartListenToServer();
    }

    void Update()
    {
    }

    private void StartListenToServer()
    {
        try
        {
            if (isMulticast) // multicast
            {
                multicastAddress = IPAddress.Parse(multicastAddr);
                udpClient = new UdpClient(AddressFamily.InterNetwork);
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                localEP = new IPEndPoint(IPAddress.Any, portUDP);
                udpClient.Client.Bind(localEP);
                udpClient.JoinMulticastGroup(multicastAddress);
            }
            else // broadcast
            {
                udpClient = new UdpClient(portUDP);
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }

            udpClient.Client.ReceiveBufferSize = bufferSize;

            int recvBuf = (int)udpClient.Client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer);
            int sendBuf = (int)udpClient.Client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer);
            Debug.Log($"[+++++++] UDP client listening on the server, recvBuf-{recvBuf}, sendBuf-{sendBuf}");

            udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
        }
        catch (Exception ex)
        {
            Debug.Log("UDP client initialization failed: " + ex.Message);
        }
    }
    
    private void ReceiveMeshChunks(IAsyncResult ar)
    {
        // Check if shutting down or socket already closed
        if (isShuttingDown || udpClient == null)
        {
            Debug.Log($"{isShuttingDown}, {udpClient == null}");
            return;
        }

        try
        {
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, portUDP);
            Debug.Log($"waiting on packets");
            byte[] packet = udpClient.EndReceive(ar, ref remoteEP);
            if (packet.Length < HEADER_SIZE)
            {
                Debug.LogWarning("Received packet too small to contain header.");
            }
            else
            {
                Debug.Log($"received packet of size {packet.Length}");
                // Todo: Decode function
                //string timeStamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                //string frameEntry = $"{{\"time\":\"{timeStamp}\"}}";
                //logWriter.WriteLine(frameEntry);
                //logWriter.Flush();
                //chunksThisFrame.Clear();
            }
        }
        catch (ObjectDisposedException)
        {
            // Expected when shutting down ? safe to ignore
        }
        catch (Exception ex)
        {
            if (!isShuttingDown) // suppress if already quitting
                Debug.LogError("ReceiveMeshChunks error: " + ex.Message);
        }

        // Re-register only if still running
        if (!isShuttingDown && udpClient != null)
        {
            try
            {
                udpClient.BeginReceive(new AsyncCallback(ReceiveMeshChunks), null);
            }
            catch (ObjectDisposedException)
            {
                // Also safe to ignore if shutdown race
            }
        }
    }

    void OnDestroy()
    {
        isShuttingDown = true;
        if (udpClient != null)
        {
            if (isMulticast)
            {
                udpClient.DropMulticastGroup(multicastAddress);
            }
            udpClient.Close();
        }
        if (logWriter != null)
        {
            logWriter.Flush();
            logWriter.Close();
        }
        retransmissionClient?.Close();
        retransmissionClient = null;
    }
}