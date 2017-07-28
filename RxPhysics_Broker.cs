using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Simple physics engine by N7RX.
/// Supports networking.
/// Last modified: 2017-07
/// 
/// This class handles info request between sever and client.
/// This script should be attached to physics broker prefab.
/// </summary>
public class RxPhysics_Broker : NetworkBehaviour {

    public string ServerIP = "10.20.73.58";
    public int  ServerPort = 7777;

    private NetworkClient _brokerClient = new NetworkClient();
    private RxPhysics_Judge _reference;
    private Queue<RxPhysics_Entity> _subscribedEntities = new Queue<RxPhysics_Entity>();

    private void Start()
    {
        _brokerClient.Connect(ServerIP, ServerPort);
        _brokerClient.RegisterHandler((short)RxMsgType.JudgeIDRep, OnPhysicsManagerReplyed);
        _brokerClient.RegisterHandler((short)RxMsgType.JudgeIDReq, OnClientBrokerMessageReceived);

        if (!isServer)
        {
            RxPhysicsMessage msg = new RxPhysicsMessage();
            msg.netID = this.GetComponent<NetworkIdentity>().netId;
            msg.reqType = (short)RxMsgType.JudgeIDReq;
            msg.conID = _brokerClient.connection.connectionId;
            _brokerClient.Send((short)RxMsgType.JudgeIDReq, msg);
        }
        else
        {
            _reference = GameObject.FindObjectOfType<RxPhysics_Judge>();
        }
    }

    public RxPhysics_Judge GetPhysicsManagerReference()
    {
        return _reference;
    }

    public void Subscribe(RxPhysics_Entity entity)
    {
        _subscribedEntities.Enqueue(entity);
    }

    private void FixedUpdate()
    {
        if (_reference != null)
        {
            while (_subscribedEntities.Count > 0)
            {
                _subscribedEntities.Dequeue().UpdateJudgeReference(_reference);
            }
        }
    }

    private void OnPhysicsManagerReplyed(NetworkMessage netMsg)
    {
        RxPhysicsMessage msg = netMsg.ReadMessage<RxPhysicsMessage>();

        if (msg.reqType == (short)RxMsgType.JudgeIDRep)
        {
            _reference = NetworkServer.FindLocalObject(msg.netID).GetComponent<RxPhysics_Judge>();
        }
    }

    private void OnClientBrokerMessageReceived(NetworkMessage netMsg)
    {
        RxPhysicsMessage msg = netMsg.ReadMessage<RxPhysicsMessage>();

        if (msg.reqType == (short)RxMsgType.JudgeIDReq)
        {
            RxPhysicsMessage reply = new RxPhysicsMessage();
            reply.reqType = (short)RxMsgType.JudgeIDRep;
            reply.netID = _reference.GetComponent<NetworkIdentity>().netId;
            reply.conID = msg.conID;
            NetworkServer.SendToClient(msg.conID, (short)RxMsgType.JudgeIDRep, reply);
        }
    }

}

/// <summary>
/// Collison information data struct
/// </summary>
public struct RxPhysics_CollisionData
{
    public float CollisionTime;         // At what time the client percieved the collision happened
    public float Delay;                 // Time required to transmit this struct. Calculated in server
    public float StartPendingTime;      // Pending start time. Calculated in server
    public Vector4 CollisionVelocity_1; // (int ID, Vec3 Velocity)
    public Vector4 CollisionVelocity_2;
    public Vector4 CollisionPosition_1; // (int ID, Vec3 Position)
    public Vector4 CollisionPosition_2;
}

/// <summary>
/// Message transmitted between physics server and client
/// </summary>
public class RxPhysicsMessage : MessageBase
{
    public short reqType;
    public int conID;
    public NetworkInstanceId netID;
}

/// <summary>
/// Message type be
/// </summary>
public enum RxMsgType
{
    JudgeIDReq,
    JudgeIDRep
}