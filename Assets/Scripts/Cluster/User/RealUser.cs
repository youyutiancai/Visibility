using System.Net.Sockets;
using System.Net;
using UnityEngine;

public class RealUser : User
{
    public bool isPuppet;
    public TestPhase testPhase;
    public IPEndPoint tcpEndPoint;
    public Vector3 latestPosition, simulatedPosition;
    public Quaternion latestRotation, simulatedRotation;
    public TcpClient tcpClient;

    public RealUser(Vector3 initialPos) : base(initialPos)
    {
        latestPosition = initialPos;
        latestRotation = Quaternion.identity;
        simulatedPosition = initialPos;
        simulatedRotation = Quaternion.identity;
        isPuppet = false;
        testPhase = TestPhase.InitialPhase;
    }
}
