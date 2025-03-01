using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class TCPControl : MonoBehaviour
{
    private IPAddress iP4Address;
    private int listenerport = 5005;
    private Thread tcpThread;
    private TcpListener tcpListener;
    private CancellationToken ct;
    private static object _lock = new object();
    private static Dictionary<IPEndPoint, TcpClient> clients;
    private Dispatcher dispatcher;
    private VisibilityCheck visibilityCheck;

    public TCPControl(CancellationToken _ct, Dispatcher _dispatcher, VisibilityCheck _visibilityCheck)
    {
        ct = _ct;
        dispatcher = _dispatcher;
        iP4Address = IPAddress.Any;
        clients = new Dictionary<IPEndPoint, TcpClient>();
        tcpThread = new Thread(() => ListenTCP());
        tcpThread.Start();
        visibilityCheck = _visibilityCheck;
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
                Debug.Log($"ClientID {ep} is connected and added to our clients...");
                Thread thread = new Thread(() => HandleClientConnection(ep, ct));
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
        client.GetStream().Write(visibilityCheck.objectTable);
    }
}
