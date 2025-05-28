using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Random = UnityEngine.Random;

public class TCPControl : MonoBehaviour
{
    private IPAddress iP4Address;
    private int listenerport = 13000;
    private CancellationToken ct;
    private TcpListener tcpListener;
    private Task listenerTask;
    private static object _lock = new object();
    private static Dictionary<IPEndPoint, TcpClient> clients;
    private static Dictionary<IPEndPoint, Task> clientTasks;
    private Dispatcher dispatcher;
    private VisibilityCheck visibilityCheck;
    private ClusterControl cc;
    private static Dictionary<IPEndPoint, RealUser> endpointToUser;

    public TCPControl(CancellationToken _ct, Dispatcher _dispatcher, VisibilityCheck _visibilityCheck, ClusterControl _clusterControl)
    {
        ct = _ct;
        dispatcher = _dispatcher;
        visibilityCheck = _visibilityCheck;
        cc = _clusterControl;
        iP4Address = IPAddress.Any;
        clients = new Dictionary<IPEndPoint, TcpClient>();
        clientTasks = new Dictionary<IPEndPoint, Task>();
        endpointToUser = new Dictionary<IPEndPoint, RealUser>();
        listenerTask = ListenTCPAsync();
    }

    private async Task ListenTCPAsync()
    {
        IPEndPoint localEndPoint = new IPEndPoint(iP4Address, listenerport);
        tcpListener = new TcpListener(localEndPoint);
        tcpListener.Start();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!tcpListener.Pending())
                {
                    await Task.Delay(100, ct);
                    continue;
                }

                TcpClient newClient = await tcpListener.AcceptTcpClientAsync();
                IPEndPoint ep = newClient.Client.RemoteEndPoint as IPEndPoint;

                lock (_lock)
                {
                    clients[ep] = newClient;
                    clientTasks[ep] = HandleClientConnectionAsync(ep, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.Log($"ListenTCPAsync error: {e.Message}");
        }
        finally
        {
            tcpListener.Stop();
        }
    }

    public async Task HandleClientConnectionAsync(IPEndPoint ep, CancellationToken ct)
    {
        TcpClient client;
        lock (_lock)
        {
            client = clients[ep];
        }

        dispatcher.Enqueue(() => SendTable(client));
        Debug.Log($"ClientID {ep} is connected and added to our clients...");

        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[1000000];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!stream.DataAvailable)
                {
                    await Task.Delay(10, ct);
                    continue;
                }

                int byteCount = await stream.ReadAsync(buffer, 0, buffer.Length, ct);

                if (byteCount == 0)
                    break;

                byte[] data = new byte[byteCount];
                Buffer.BlockCopy(buffer, 0, data, 0, byteCount);
                dispatcher.Enqueue(() => HandleMessageTCP(client, data));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.Log($"Client {ep} read error: {e.Message}");
        }
        finally
        {
            lock (_lock)
            {
                clients.Remove(ep);
                clientTasks.Remove(ep);
                endpointToUser.Remove(ep);
                Debug.Log($"Client {ep} has been removed");
            }
            client.Close();
        }
    }

    private void HandleMessageTCP(TcpClient client, byte[] data)
    {
        int cursor = 0;
        IPEndPoint ep = client.Client.RemoteEndPoint as IPEndPoint;

        while (cursor + sizeof(int) <= data.Length)
        {
            TCPMessageType mt = (TCPMessageType)BitConverter.ToInt32(data, cursor);
            cursor += sizeof(int);

            switch (mt)
            {
                case TCPMessageType.POSE_UPDATE:
                    if (cursor + sizeof(float) * 7 > data.Length) return; // not enough data
                    if (endpointToUser.TryGetValue(ep, out RealUser realUser) && !realUser.isPuppet)
                    {
                        float px = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                        float py = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                        float pz = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                        float rx = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                        float ry = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                        float rz = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                        float rw = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);

                        realUser.latestPosition = new Vector3(px, py, pz);
                        realUser.latestRotation = new Quaternion(rx, ry, rz, rw);

                        if (realUser.transform != null)
                            realUser.transform.SetPositionAndRotation(realUser.latestPosition, realUser.latestRotation);
                    }
                    break;

                case TCPMessageType.PUPPET_TOGGLE:
                    if (cursor + sizeof(int) > data.Length) return; // not enough data
                    if (endpointToUser.TryGetValue(ep, out realUser))
                    {
                        bool newState = BitConverter.ToInt32(data, cursor) == 1;
                        cursor += sizeof(int);
                        realUser.isPuppet = newState;
                        Debug.Log($"User {ep} puppet mode set to: {newState}");
                    }
                    break;

                default:
                    Debug.LogWarning($"Unknown TCPMessageType: {mt}, remaining bytes: {data.Length - cursor}");
                    return; // Avoid infinite loop
            }
        }
    }


    private void SendTable(TcpClient client)
    {
        if (cc.SimulationStrategy != SimulationStrategyDropDown.RealUser)
        {
            Debug.LogError($"The current simulation strategy is not real user!");
            return;
        }

        lock (_lock)
        {
            GameObject newUser = Instantiate(cc.realUserPrefab);
            newUser.transform.position = cc.initialClusterCenterPos +
                new Vector3(Random.Range(-cc.epsilon / 4.0f, cc.epsilon / 4.0f), 1.3f,
                Random.Range(-cc.epsilon / 4.0f, cc.epsilon / 4.0f));
            newUser.transform.parent = cc.transform;
            cc.users.Add(newUser.GetComponent<RealUser>());
            var realUser = newUser.GetComponent<RealUser>();
            realUser.tcpClient = client;
            realUser.tcpEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
            endpointToUser[realUser.tcpEndPoint] = realUser;
            client.GetStream().Write(visibilityCheck.objectTable);
            Debug.Log($"table size: {visibilityCheck.objectTable.Length}");
        }
    }

    public async void OnQuit()
    {
        Debug.Log("Shutting down TCPControl...");

        try
        {
            tcpListener?.Stop();
        }
        catch (Exception e)
        {
            Debug.Log($"Error stopping tcpListener: {e.Message}");
        }

        lock (_lock)
        {
            foreach (var kvp in clients)
            {
                try { kvp.Value?.Close(); }
                catch (Exception e) { Debug.Log($"Error closing client {kvp.Key}: {e.Message}"); }
            }
            clients.Clear();
        }

        try
        {
            await Task.WhenAll(clientTasks.Values);
        }
        catch (Exception e)
        {
            Debug.Log($"Error waiting for client tasks: {e.Message}");
        }

        clientTasks.Clear();

        Debug.Log("TCPControl shutdown complete.");
    }
}