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

    public TCPControl(CancellationToken _ct, Dispatcher _dispatcher, VisibilityCheck _visibilityCheck, ClusterControl _clusterControl)
    {
        ct = _ct;
        dispatcher = _dispatcher;
        visibilityCheck = _visibilityCheck;
        cc = _clusterControl;
        iP4Address = IPAddress.Any;
        clients = new Dictionary<IPEndPoint, TcpClient>();
        clientTasks = new Dictionary<IPEndPoint, Task>();

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
                Debug.Log($"Client {ep} has been removed");
            }
            client.Close();
        }
    }

    private void HandleMessageTCP(TcpClient client, byte[] data)
    {
        TCPMessageType mt = (TCPMessageType)BitConverter.ToInt32(data, 0);
        Debug.Log($"Client {client.Client.RemoteEndPoint} sends message: {mt}");

        switch (mt)
        {
            case TCPMessageType.TABLE:
                SendTable(client);
                break;

            default:
                break;
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
