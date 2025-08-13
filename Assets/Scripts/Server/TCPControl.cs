using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityTCPClient.Assets.Scripts;

public class TCPControl : Singleton<TCPControl>
{
    public IPAddress[] headsetIDs = new IPAddress[] { IPAddress.Parse("192.168.1.173") };
    private IPAddress iP4Address;
    private int listenerport = 13000;
    private CancellationToken ct;
    private TcpListener tcpListener;
    private Task listenerTask;
    private static object _lock = new object();
    public static Dictionary<IPAddress, TcpClient> clients;
    private static Dictionary<IPAddress, Task> clientTasks;
    private Dispatcher dispatcher;
    private VisibilityCheck vc;
    private ClusterControl cc;
    private NetworkControl nc;
    public Dictionary<IPAddress, RealUser> addressToUser;
    private int userIDOrder;

    private void Start()
    {
        dispatcher = Dispatcher.Instance;
        vc = VisibilityCheck.Instance;
        cc = ClusterControl.Instance;
        nc = NetworkControl.Instance;
        ct = nc.cts.Token;
        iP4Address = IPAddress.Any;
        clients = new Dictionary<IPAddress, TcpClient>();
        clientTasks = new Dictionary<IPAddress, Task>();
        addressToUser = new Dictionary<IPAddress, RealUser>();
        userIDOrder = 0;
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
                    clients[ep.Address] = newClient;
                    clientTasks[ep.Address] = HandleClientConnectionAsync(ep, ct);
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
            client = clients[ep.Address];
        }

        dispatcher.Enqueue(() => SendTable(client));
        Debug.Log($"ClientID {ep} is connected and added to our clients...");

        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[1000000];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Await data only if available (non-blocking), else yield
                //if (!stream.DataAvailable)
                //{
                //    await Task.Delay(10, ct);
                //    continue;
                //}

                int byteCount = await stream.ReadAsync(buffer, 0, buffer.Length, ct);

                // Client disconnected
                if (byteCount == 0)
                    break;

                byte[] data = new byte[byteCount];
                Buffer.BlockCopy(buffer, 0, data, 0, byteCount);
                dispatcher.Enqueue(() => HandleMessageTCP(client, data));
            }
        }
        catch (OperationCanceledException)
        {
            Debug.Log($"Client {ep} read canceled");
        }
        catch (Exception e)
        {
            Debug.Log($"Client {ep} read error: {e.Message}");
        }
        finally
        {
            lock (_lock)
            {
                clients.Remove(ep.Address);
                clientTasks.Remove(ep.Address);
                GameObject userParent = addressToUser[ep.Address].gameObject;
                userParent.transform.SetParent(null);
                Destroy(userParent);
                addressToUser.Remove(ep.Address);
                Debug.Log($"Client {ep} has been removed");
            }

            // Forcefully close socket to ensure ReadAsync exits
            try { client.Client?.Shutdown(SocketShutdown.Both); } catch { }
            try { stream?.Close(); } catch { }
            try { client?.Close(); } catch { }
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
                    if (cursor + sizeof(float) * 7 > data.Length) {
                        Debug.Log($"not enough data: {data.Length}");
                        return;
                    }
                        
                    if (addressToUser.TryGetValue(ep.Address, out RealUser realUser))
                    {
                        float px = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                        float py = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                        float pz = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                        float rx = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                        float ry = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                        float rz = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                        float rw = BitConverter.ToSingle(data, cursor); cursor += sizeof(float);
                        TestPhase testPhase = (TestPhase)BitConverter.ToInt32(data, cursor); cursor += sizeof(int);

                        realUser.latestPosition = new Vector3(px, py, pz);
                        realUser.latestRotation = new Quaternion(rx, ry, rz, rw);
                        if (realUser.testPhase != testPhase)
                        {
                            realUser.testPhaseChanged = true;
                        }
                        realUser.testPhase = testPhase;

                        if (!realUser.isPuppet)
                        {
                            realUser.simulatedPosition = realUser.latestPosition;
                            realUser.simulatedRotation = realUser.latestRotation;
                        }

                        //Debug.Log($"{realUser.simulatedPosition}, {realUser.simulatedRotation}, {realUser.latestPosition}, {realUser.latestRotation}");
                        if (realUser.transform != null)
                            realUser.transform.SetPositionAndRotation(realUser.latestPosition, realUser.latestRotation);
                    }
                    break;

                case TCPMessageType.PUPPET_TOGGLE:
                    if (cursor + sizeof(int) > data.Length) return; // not enough data
                    if (addressToUser.TryGetValue(ep.Address, out realUser))
                    {
                        bool newState = BitConverter.ToInt32(data, cursor) == 1;
                        cursor += sizeof(int);
                        realUser.isPuppet = newState;
                        //realUser.simulatedPosition = transform.position;
                        //realUser.simulatedRotation = transform.rotation;
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
        if (!(cc.SimulationStrategy == SimulationStrategyDropDown.RealUserCluster || cc.SimulationStrategy == SimulationStrategyDropDown.RealUserIndi))
        {
            Debug.LogError($"The current simulation strategy is not real user!");
            return;
        }

        lock (_lock)
        {
            GameObject newUser = Instantiate(cc.realUserPrefab);
            newUser.transform.position = cc.initialClusterCenterPos + new Vector3(0, 1.3f, 0);
            //newUser.transform.position = cc.initialClusterCenterPos +
            //    new Vector3(Random.Range(-cc.epsilon / 4.0f, cc.epsilon / 4.0f), 1.3f,
            //    Random.Range(-cc.epsilon / 4.0f, cc.epsilon / 4.0f));
            newUser.transform.parent = cc.transform;
            cc.users.Add(newUser.GetComponent<RealUser>());
            var realUser = newUser.GetComponent<RealUser>();
            realUser.tcpClient = client;
            realUser.tcpEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
            addressToUser[realUser.tcpEndPoint.Address] = realUser;
            realUser.InformStartPath(userIDOrder % 2);
            userIDOrder++;
            client.GetStream().Write(nc.objectTable);
            Debug.Log($"table size: {nc.objectTable.Length}");

            Vector3 position = realUser.transform.position;
            Quaternion rotation = realUser.transform.rotation;
            try
            {
                List<byte> buffer = new List<byte>();
                buffer.AddRange(BitConverter.GetBytes(0));
                buffer.AddRange(BitConverter.GetBytes((int)TCPMessageType.POSE_FROM_SERVER));
                buffer.AddRange(BitConverter.GetBytes(position.x));
                buffer.AddRange(BitConverter.GetBytes(position.y));
                buffer.AddRange(BitConverter.GetBytes(position.z));
                buffer.AddRange(BitConverter.GetBytes(rotation.x));
                buffer.AddRange(BitConverter.GetBytes(rotation.y));
                buffer.AddRange(BitConverter.GetBytes(rotation.z));
                buffer.AddRange(BitConverter.GetBytes(rotation.w));
                byte[] message = buffer.ToArray();
                Buffer.BlockCopy(BitConverter.GetBytes(buffer.Count - sizeof(int)), 0, message, 0, sizeof(int));
                NetworkStream stream = realUser.tcpClient.GetStream();
                stream.Write(message, 0, message.Length);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to send puppet pose to {realUser.tcpEndPoint}: {e.Message}");
            }
        }
    }

    public void SendChunkToUser(RealUser user, byte[] message)
    {
        byte[] new_message = new byte[message.Length + sizeof(int) * 3];
        Buffer.BlockCopy(BitConverter.GetBytes(new_message.Length - sizeof(int)), 0, new_message, 0, sizeof(int));
        Buffer.BlockCopy(BitConverter.GetBytes((int)TCPMessageType.CHUNK), 0, new_message, sizeof(int), sizeof(int));
        Buffer.BlockCopy(BitConverter.GetBytes((int)nc.cc.meshDecodeMethod), 0, new_message, sizeof(int) * 2, sizeof(int));
        Buffer.BlockCopy(message, 0, new_message, sizeof(int) * 3, message.Length);
        SendMessageToClient(user.tcpEndPoint.Address, new_message);
    }

    public void InformQuestionStart(RealUser user)
    {
        byte[] new_message = new byte[sizeof(int) * 2];
        Buffer.BlockCopy(BitConverter.GetBytes(sizeof(int)), 0, new_message, 0, sizeof(int));
        Buffer.BlockCopy(BitConverter.GetBytes((int) TCPMessageType.QUESTIONSTART), 0, new_message, sizeof(int), sizeof(int));
        SendMessageToClient(user.tcpEndPoint.Address, new_message);
    }

    public void SendMessageToUser(RealUser user, byte[] message)
    {
        byte[] new_message = new byte[message.Length + sizeof(int)];
        Buffer.BlockCopy(BitConverter.GetBytes(message.Length), 0, new_message, 0, sizeof(int));
        Buffer.BlockCopy(message, 0, new_message, sizeof(int), message.Length);
        SendMessageToClient(user.tcpEndPoint.Address, new_message);
    }

    public void SendMessageToClient(IPAddress clientEndPoint, byte[] message)
    {
        //Debug.Log($"Sending message to client {clients[clientEndPoint].Available} of size {BitConverter.ToInt32(message, 0)}");
        if (!clients.ContainsKey(clientEndPoint))
        {
            return;
        }
        clients[clientEndPoint].GetStream().Write(message, 0, message.Length);
    }

    public async Task OnQuit()
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
                try
                {
                    kvp.Value.Client?.Shutdown(SocketShutdown.Both);
                    kvp.Value?.GetStream().Close();
                    kvp.Value?.Close();
                }
                catch (Exception e)
                {
                    Debug.Log($"Error closing client {kvp.Key}: {e.Message}");
                }
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