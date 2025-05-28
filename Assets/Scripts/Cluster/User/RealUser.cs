using System.Net.Sockets;
using System.Net;
using UnityEngine;

public class RealUser : User
{
    public bool isPuppet;
    public IPEndPoint tcpEndPoint;
    public Vector3 latestPosition;
    public Quaternion latestRotation;
    public TcpClient tcpClient;

    public RealUser(Vector3 initialPos) : base(initialPos)
    {
        latestPosition = initialPos;
        latestRotation = Quaternion.identity;
        isPuppet = false;
    }
}
