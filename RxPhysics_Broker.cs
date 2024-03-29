﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Simple physics engine by N7RX.
/// Supports networking.
/// Last modified: 2017-08
/// 
/// This class handles info request between sever and client.
/// This script should be attached to physics broker object.
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(RxPhysics_Predict))]
public class RxPhysics_Broker : NetworkBehaviour {

    [Header("Connection Info")]
    public string ServerIP = "10.20.73.58"; /* Test host IP address inside SEASUN group*/
    public int    ServerPort = 7777;        /* Test host port */

    [Header("Performance")]
    public float TimeGapUpdateInterval = 1f;
    
    [HideInInspector]
    public readonly int DefaultTimeGap = -65536;

    // Length of duration that the entity can't be added force on after a collision
    private float _refractoryTime = 0.3f;
    // Broker client   (on client only)
    private NetworkClient   _brokerClient;

    // Judge reference (on server only)
    private RxPhysics_Judge _judgeRef;

    /// <summary>
    /// Client side
    /// </summary>
    // Client entities pending to send register request
    //private Queue<NetworkInstanceId> _pendingSendRegEntitiesID = new Queue<NetworkInstanceId>(); /* Remote entity registration is currently disabled */
    // Client entities pending to send remove request
    private Queue<int>                     _pendingSendRemoveEntitiesID = new Queue<int>();
    // Queued entities pending to sync entity ID
    private Queue<NetworkInstanceId>       _pendingSyncEntitiesID       = new Queue<NetworkInstanceId>();
    // Queued entity IDs to be distributed
    private Queue<RxPhysics_IDSync>        _pendingDistribID            = new Queue<RxPhysics_IDSync>();
    // Queued collision data
    private Queue<RxPhysics_CollisionData> _pendingSendCollisionData    = new Queue<RxPhysics_CollisionData>();

    // Time gap between server and client
    private float _clientSeverTimeGap = 0;
    private float _lastGapUpdateTime  = 0;
    private bool  _gapNeedsUpdate     = false;

    /// <summary>
    /// Server side
    /// </summary>
    // Client entities pending to register
    //private Queue<NetworkInstanceId> _pendingRegEntitiesID   = new Queue<NetworkInstanceId>();
    // Client entities pending to remove
    private Queue<int>                     _pendingRemoveEntitiesID = new Queue<int>();
    // Client collision pending to be processed
    private Queue<RxPhysics_CollisionData> _pendingCollision        = new Queue<RxPhysics_CollisionData>();


    /// <summary>
    /// Initialization
    /// </summary>
    private void Start()
    {
        // Server broker
        if (isServer)
        {
            //NetworkServer.RegisterHandler(RxMsgType.RegisterReq, OnRegisterReqMsgReceived);
            NetworkServer.RegisterHandler(RxMsgType.CollisionData, OnCollisionDataReceived);
            NetworkServer.RegisterHandler(RxMsgType.RemoveReg, OnRemoveRegMsgReceived);
            NetworkServer.RegisterHandler(RxMsgType.CalibrateTime, OnTimeCalibReqReceived);
            NetworkServer.RegisterHandler(RxMsgType.IDSync, OnSyncIDReqRequest);

            _judgeRef = GameObject.FindObjectOfType<RxPhysics_Judge>();
            _clientSeverTimeGap = 0;
        }
        // Client broker
        else
        {
            _brokerClient = new NetworkClient();
            _brokerClient.Connect(ServerIP, ServerPort);
            NetworkManager.singleton.client.RegisterHandler(RxMsgType.CalibrateTime, OnTimeCalibRepReceived);
            NetworkManager.singleton.client.RegisterHandler(RxMsgType.IDSync, OnSyncIDRepReceived);
            _clientSeverTimeGap = DefaultTimeGap;
            _gapNeedsUpdate = true;
        }
    }

    private void FixedUpdate()
    {
        // On server
        if (isServer)
        {
            // Fix manager reference
            if (_judgeRef == null)
            {
                _judgeRef = GameObject.FindObjectOfType<RxPhysics_Judge>();
            }
            else
            {
                //RegisterEntities();
                RemoveEntities();
                ProcessCollision();
            }
        }
        // On client
        else
        {
            // Fix server connection
            if (!_brokerClient.isConnected)
            {
                _brokerClient.Connect(ServerIP, ServerPort);
            }
            else
            {
                //SendRegisterData();
                SendSyncRequest();
                DistributeSyncedID();
                SendCollisionData();
                SendRemoveReq();               

                // Update client-server time gap
                if (_clientSeverTimeGap < DefaultTimeGap + 1)
                {
                    ReqTimeCalibration();
                }
                else
                {
                    if (Time.realtimeSinceStartup - _lastGapUpdateTime >= TimeGapUpdateInterval)
                    {
                        _gapNeedsUpdate = true;
                        ReqTimeCalibration();
                    }
                }
            }
        }

    }

    /// <summary>
    /// Get current time gap between client and server
    /// </summary>
    /// <returns>Client time gap</returns>
    public float GetClientServerTimeGap()
    {
        return _clientSeverTimeGap;
    }

    /// <summary>
    /// Queried by local entities
    /// </summary>
    /// <returns>Refractory time after collision</returns>
    public float GetRefractoryTime()
    {
        return _refractoryTime;
    }

    /// <summary>
    /// Add client entity netID to pending register list
    /// </summary>
    /// <param name="id">Entity netID</param>
    //[Client]
    //public void RequestRegister(NetworkInstanceId id)
    //{
    //    _pendingSendRegEntitiesID.Enqueue(id);
    //}

    /// <summary>
    /// Add client entity netID to pending sync ID list
    /// </summary>
    /// <param name="id">Entity netID</param>
    [Client]
    public void RequestSyncID(NetworkInstanceId id)
    {
        _pendingSyncEntitiesID.Enqueue(id);
    }

    /// <summary>
    /// Add collision data to pending send list
    /// </summary>
    /// <param name="data">Collision data</param>
    [Client]
    public void RequestCollisionJudge(RxPhysics_CollisionData data)
    {
        _pendingSendCollisionData.Enqueue(data);
    }

    /// <summary>
    /// Add client entity ID to pending remove list
    /// </summary>
    /// <param name="id">Entity iD</param>
    [Client]
    public void RequestRemove(int id)
    {
        _pendingSendRemoveEntitiesID.Enqueue(id);
    }

    /// <summary>
    /// Send time calibration request
    /// </summary>
    [Client]
    private void ReqTimeCalibration()
    {
        RxPhysics_TimeGapMsg msg = new RxPhysics_TimeGapMsg();
        msg.conID = _brokerClient.connection.connectionId;
        msg.clientTime = Time.realtimeSinceStartup;
        _brokerClient.Send(RxMsgType.CalibrateTime, msg);
    }

    /// <summary>
    /// Send register data to server broker
    /// </summary>
    //[Client]
    //private void SendRegisterData()
    //{
    //    while (_pendingSendRegEntitiesID.Count > 0)
    //    {
    //        RxPhysics_RegMessage msg = new RxPhysics_RegMessage();
    //        msg.conID = _brokerClient.connection.connectionId;
    //        msg.isRemove = false;
    //        msg.netID = _pendingSendRegEntitiesID.Dequeue();
    //        _brokerClient.Send(RxMsgType.RegisterReq, msg);
    //    }
    //}

    /// <summary>
    /// Send collision data to server broker
    /// </summary>
    [Client]
    private void SendCollisionData()
    {
        while (_pendingSendCollisionData.Count > 0)
        {
            RxPhysics_CollisionMsg msg = new RxPhysics_CollisionMsg();
            msg.conID = _brokerClient.connection.connectionId;
            msg.colData = _pendingSendCollisionData.Dequeue();
            _brokerClient.Send(RxMsgType.CollisionData, msg);
        }

    }

    /// <summary>
    /// Send ID sync request data to server
    /// </summary>
    [Client]
    private void SendSyncRequest()
    {
        while (_pendingSyncEntitiesID.Count > 0)
        {
            RxPhysics_IDSyncMsg msg = new RxPhysics_IDSyncMsg();
            msg.conID = _brokerClient.connection.connectionId;
            msg.syncData = new RxPhysics_IDSync();
            msg.syncData.netID = _pendingSyncEntitiesID.Dequeue();
            _brokerClient.Send(RxMsgType.IDSync, msg);
        }
    }

    /// <summary>
    /// Send remove entity request to server
    /// </summary>
    [Client]
    private void SendRemoveReq()
    {
        while (_pendingSendRemoveEntitiesID.Count > 0)
        {
            RxPhysics_RegisterMsg msg = new RxPhysics_RegisterMsg();
            msg.conID = _brokerClient.connection.connectionId;
            msg.isRemove = true;
            msg.entID = _pendingSendRemoveEntitiesID.Dequeue();
            _brokerClient.Send(RxMsgType.RemoveReg, msg);
        }
    }

    /// <summary>
    /// Register physics entity (on server)
    /// </summary>
    //[Server]
    //private void RegisterEntities()
    //{
    //    while (_pendingRegEntitiesID.Count > 0)
    //    {
    //        _judgeRef.RequestRegister(_pendingRegEntitiesID.Dequeue());
    //    }
    //}

    /// <summary>
    /// Distribute synced netID and entID data
    /// </summary>
    [Client]
    private void DistributeSyncedID()
    {
        while (_pendingDistribID.Count > 0)
        {
            RxPhysics_IDSync data = _pendingDistribID.Dequeue();
            ClientScene.FindLocalObject(data.netID).GetComponent<RxPhysics_Entity>().AssignEntityID(data.entID);
        }
    }

    /// <summary>
    /// Remove physics entity registration (on server)
    /// </summary>
    [Server]
    private void RemoveEntities()
    {
        while (_pendingRemoveEntitiesID.Count > 0)
        {
            _judgeRef.RemoveEntity(_pendingRemoveEntitiesID.Dequeue());
        }
    }

    /// <summary>
    /// Process queued collision data
    /// </summary>
    [Server]
    private void ProcessCollision()
    {
        while (_pendingCollision.Count > 0)
        {
            _judgeRef.CallCollisonJudge(_pendingCollision.Dequeue());
        }
    }

    /// <summary>
    /// Process received collision data
    /// </summary>
    /// <param name="netMsg"></param>
    [Server]
    private void OnCollisionDataReceived(NetworkMessage netMsg)
    {
        RxPhysics_CollisionMsg msg = netMsg.ReadMessage<RxPhysics_CollisionMsg>();

        _pendingCollision.Enqueue(msg.colData);
    }

    /// <summary>
    /// Add requested entity registration to queue
    /// </summary>
    /// <param name="netMsg">Network message</param>
    //[Server]
    //private void OnRegisterReqMsgReceived(NetworkMessage netMsg)
    //{
    //    RxPhysics_RegMessage msg = netMsg.ReadMessage<RxPhysics_RegMessage>();

    //    if (!msg.isRemove)
    //    {
    //        _pendingRegEntitiesID.Enqueue(msg.netID);
    //    }
    //}

    /// <summary>
    /// Remove assigned entity from physics manager
    /// </summary>
    /// <param name="netMsg">Network message</param>
    [Server]
    private void OnRemoveRegMsgReceived(NetworkMessage netMsg)
    {
        RxPhysics_RegisterMsg msg = netMsg.ReadMessage<RxPhysics_RegisterMsg>();

        if (msg.isRemove)
        {
            _pendingRemoveEntitiesID.Enqueue(msg.entID);
        }
    }

    /// <summary>
    /// Calculate and return the time gap between server and client
    /// </summary>
    /// <param name="netMsg">Network message</param>
    [Server]
    private void OnTimeCalibReqReceived(NetworkMessage netMsg)
    {
        RxPhysics_TimeGapMsg msg = netMsg.ReadMessage<RxPhysics_TimeGapMsg>();
        msg.serverTime = Time.realtimeSinceStartup;
        NetworkServer.SendToClient(msg.conID, RxMsgType.CalibrateTime, msg);
    }

    /// <summary>
    /// Process server's reply on time calibration
    /// </summary>
    /// <param name="netMsg">Network message</param>
    [Client]
    private void OnTimeCalibRepReceived(NetworkMessage netMsg)
    {
        RxPhysics_TimeGapMsg msg = netMsg.ReadMessage<RxPhysics_TimeGapMsg>();
      
        if (_gapNeedsUpdate)
        {
            _gapNeedsUpdate = false;
            _clientSeverTimeGap = msg.serverTime - msg.clientTime;
            _lastGapUpdateTime = Time.realtimeSinceStartup;
        }
    }

    /// <summary>
    /// Find and return corresponding entity ID
    /// </summary>
    /// <param name="netMsg">Network message</param>
    [Server]
    private void OnSyncIDReqRequest(NetworkMessage netMsg)
    {
        RxPhysics_IDSyncMsg msg = netMsg.ReadMessage<RxPhysics_IDSyncMsg>();
        msg.syncData.entID = NetworkServer.FindLocalObject(msg.syncData.netID).GetComponent<RxPhysics_Entity>().GetEntityID();
        NetworkServer.SendToClient(msg.conID, RxMsgType.IDSync, msg);
    }

    /// <summary>
    /// Enque received ID sync data
    /// </summary>
    /// <param name="netMsg">Network message</param>
    [Client]
    private void OnSyncIDRepReceived(NetworkMessage netMsg)
    {
        RxPhysics_IDSyncMsg msg = netMsg.ReadMessage<RxPhysics_IDSyncMsg>();
        _pendingDistribID.Enqueue(msg.syncData);
    }

    private void OnDestroy()
    {
        if (!isServer)
        {
            if (_brokerClient != null)
            {
                _brokerClient.Shutdown();
            }
            if (NetworkManager.singleton.client != null)
            {
                NetworkManager.singleton.client.Shutdown();
            }
        }
    }
}


/// <summary>
/// ID sync data
/// </summary>
public struct RxPhysics_IDSync
{
    public NetworkInstanceId netID;
    public int entID;
}

/// <summary>
/// Collison information data struct
/// </summary>
public struct RxPhysics_CollisionData
{
    public Vector2 IDPair;              // Collison ID pair. Format:(largerID, smallerID)
    public float   CollisionTime;       // At what time the client percieved the collision happened
    public float   Delay;               // Time required to transmit this struct. Calculated in server
    public float   StartPendingTime;    // Pending start time. Calculated in server
    public Vector4 CollisionVelocity_1; // (int ID, Vec3 Velocity)
    public Vector4 CollisionVelocity_2;
    public Vector4 CollisionPosition_1; // (int ID, Vec3 Position)
    public Vector4 CollisionPosition_2;
    public Vector3 CollisionPoint;
    public bool    DontWait;
}

/// <summary>
/// Internal message types
/// </summary>
public class RxMsgType
{
    public static short RegisterReq   = MsgType.Highest + 1;
    public static short CollisionData = MsgType.Highest + 2;
    public static short RemoveReg     = MsgType.Highest + 3;
    public static short CalibrateTime = MsgType.Highest + 4;
    public static short IDSync        = MsgType.Highest + 5;
}

/// <summary>
/// Register/remove entity message
/// </summary>
public class RxPhysics_RegisterMsg : MessageBase
{
    public int  conID;    // Connection ID
    public int  entID;    // Physics entity ID
    public bool isRemove; // Mark request as remove
    public NetworkInstanceId netID;
}

/// <summary>
/// Collision message
/// </summary>
public class RxPhysics_CollisionMsg : MessageBase
{
    public int conID;
    public RxPhysics_CollisionData colData;
}

/// <summary>
/// Time calibration message
/// </summary>
public class RxPhysics_TimeGapMsg : MessageBase
{
    public int   conID;
    public float clientTime;
    public float serverTime;
}

/// <summary>
/// ID sync message
/// </summary>
public class RxPhysics_IDSyncMsg : MessageBase
{
    public int conID;
    public RxPhysics_IDSync syncData;
}
