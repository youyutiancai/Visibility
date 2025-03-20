using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using Random = UnityEngine.Random;

public class TCPControl : MonoBehaviour
{
    private IPAddress iP4Address;
    private int listenerport = 13000;
    private Thread tcpThread;
    private TcpListener tcpListener;
    private CancellationToken ct;
    private static object _lock = new object();
    private static Dictionary<IPEndPoint, TcpClient> clients;
    private Dispatcher dispatcher;
    private VisibilityCheck visibilityCheck;
    private ClusterControl cc;

    public TCPControl(CancellationToken _ct, Dispatcher _dispatcher, VisibilityCheck _visibilityCheck, ClusterControl _clusterControl)
    {
        ct = _ct;
        dispatcher = _dispatcher;
        iP4Address = IPAddress.Any;
        clients = new Dictionary<IPEndPoint, TcpClient>();
        tcpThread = new Thread(() => ListenTCP());
        tcpThread.Start();
        visibilityCheck = _visibilityCheck;
        cc = _clusterControl;
    }

    private void ListenTCP()
    {
        IPEndPoint localEndPoint = new IPEndPoint(iP4Address, listenerport);
        tcpListener = new TcpListener(localEndPoint);
        tcpListener.Start();
        try
        {
            while(!ct.IsCancellationRequested)
            {
                Debug.Log($"Waiting for client connections...");
                TcpClient newClient = tcpListener.AcceptTcpClient();
                IPEndPoint ep = newClient.Client.RemoteEndPoint as IPEndPoint;
                lock (_lock)
                {
                    clients.Add(ep, newClient);
                }
                Thread thread = new Thread(() => HandleClientConnection(ep, ct));
                thread.Start();
            }
            tcpListener.Stop();
        } catch (SocketException e)
        {
            Debug.Log($"socketException: {e}");
            tcpListener.Stop();
        }
    }

    public void HandleClientConnection(IPEndPoint ep, CancellationToken ct)
    {
        TcpClient client = null;
        lock (_lock)
        {
            client = clients[ep];
        }
        dispatcher.Enqueue(() => SendTable(client));
        Debug.Log($"ClientID {ep} is connected and added to our clients...");
        while (!ct.IsCancellationRequested)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1000000];
            int byteCount = 0;
            try
            {
                byteCount = stream.Read(buffer, 0, buffer.Length);
            } catch (Exception e)
            {
                Debug.Log(e);
                break;
            }

            if (byteCount == 0)
            {
                break;
            }

            byte[] data = new byte[byteCount];
            Buffer.BlockCopy(buffer, 0, data, 0, byteCount);
            dispatcher.Enqueue(() => HandleMessageTCP(client, data));
        }

        lock (_lock)
        {
            clients.Remove(ep);
            Debug.Log($"Client {ep} has been removed");
        }
        client.Close();
    }

    private void HandleMessageTCP(TcpClient client, byte[] data)
    {
        TCPMessageType mt = (TCPMessageType) BitConverter.ToInt32(data, 0);
        Debug.Log($"Client {client.Client.RemoteEndPoint} sends message: {mt}");

        switch (mt) {
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
            Debug.LogError($"The current simulation strategy is not real user!");
        lock (_lock)
        {
            GameObject newUser = Instantiate(cc.realUserPrefab);
            newUser.transform.position = cc.initialClusterCenterPos +
                new Vector3(Random.Range(-cc.epsilon / 4.0f, cc.epsilon / 4.0f), 1.3f,
                Random.Range(-cc.epsilon / 4.0f, cc.epsilon / 4.0f));
            newUser.transform.parent = cc.transform;
            cc.users.Add(newUser.GetComponent<RealUser>());
            //Buffer.BlockCopy(BitConverter.GetBytes(newUser.transform.position.x), 0, visibilityCheck.objectTable, sizeof(int) * 3, sizeof(float));
            //Buffer.BlockCopy(BitConverter.GetBytes(newUser.transform.position.y), 0, visibilityCheck.objectTable, sizeof(int) * 3 + sizeof(float), sizeof(float));
            //Buffer.BlockCopy(BitConverter.GetBytes(newUser.transform.position.y), 0, visibilityCheck.objectTable, sizeof(int) * 3 + sizeof(float) * 2, sizeof(float));
            client.GetStream().Write(visibilityCheck.objectTable);
        }
    }
}
