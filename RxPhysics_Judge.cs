using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Simple physics engine by N7RX.
/// Supports networking.
/// Last modified: 2017-07
/// 
/// This class provides arbitration of physics calculation.
/// This script should be attached to physics manager.
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(RxPhysics_Compute))]
public class RxPhysics_Judge : NetworkBehaviour {

    // After which period the pending collision would be calculated directly without waiting for opponent's confirmation
    public float CollisionPendingTimeout = 0.3f;

    // Count used to assign entity ID
    private int _entityNum = 0;
    // Reference to physics calculator
    private RxPhysics_Compute _physicsCalculator;
    // List of registered physics entity
    private Dictionary<int, RxPhysics_Entity> _listOfEntities = new Dictionary<int, RxPhysics_Entity>();
    // List of collision waiting to be processed
    private Dictionary<Vector2, RxPhysics_CollisionData> _listOfCollisionRequest = new Dictionary<Vector2, RxPhysics_CollisionData>();
    private Queue<RxPhysics_CollisionData> _listOfCollision = new Queue<RxPhysics_CollisionData>();

    /// <summary>
    /// Initialization
    /// </summary>
    public override void OnStartServer()
    {
        base.OnStartServer();

        DontDestroyOnLoad(transform.gameObject);
        _physicsCalculator = this.GetComponent<RxPhysics_Compute>();
    }

    /// <summary>
    /// Register new physics entity
    /// </summary>
    /// <param name="entity">New entity to add</param>
    [Server]
    public void RequestRegister(NetworkInstanceId identity)
    {
        // Find entity reference
        RxPhysics_Entity entity = NetworkServer.FindLocalObject(identity).GetComponent<RxPhysics_Entity>();
        entity.AssignEntityID(_entityNum);
        _listOfEntities.Add(_entityNum, entity);
        // Next assigned ID
        _entityNum++;
    }

    /// <summary>
    /// Remove a entity from current list
    /// </summary>
    /// <param name="id">ID of the entity desired to remove</param>
    [Server]
    public void RemoveEntity(int id)
    {
        if (_listOfEntities.ContainsKey(id))
        {
            _listOfEntities.Remove(id);
        }
    }

    /// <summary>
    /// Call to the physics manager when collison happened
    /// </summary>
    /// <param name="idPair">IDs of the entities collided</param>
    [Server]
    public void CallCollisonJudge(RxPhysics_CollisionData data)
    {
        data.Delay = Time.realtimeSinceStartup - data.CollisionTime;

        // Collision pending
        if (!_listOfCollisionRequest.ContainsKey(data.IDPair))
        {           
            data.StartPendingTime = Time.realtimeSinceStartup;
            _listOfCollisionRequest.Add(data.IDPair, data);
        }
        // Collision confirmed
        else
        {
            _listOfCollision.Enqueue(InterpolateColData(_listOfCollisionRequest[data.IDPair], data));
            _listOfCollisionRequest.Remove(data.IDPair);
        }
    }

    /// <summary>
    /// Interpolate values between two reported collision data
    /// </summary>
    /// <param name="data_1">Data 1</param>
    /// <param name="data_2">Data 2</param>
    /// <returns>Interpolated data</returns>
    [Server]
    private RxPhysics_CollisionData InterpolateColData(RxPhysics_CollisionData data_1, RxPhysics_CollisionData data_2)
    {
        RxPhysics_CollisionData result = new RxPhysics_CollisionData();
        result.IDPair = data_1.IDPair;

        // Assign data weight based on client delay
        float weight_1 = data_2.Delay / (data_1.Delay + data_2.Delay);
        float weight_2 = 1 - weight_1;

        result.CollisionTime = data_1.CollisionTime * weight_1 + data_2.CollisionTime * weight_2;
        result.Delay = Mathf.Max(data_1.Delay, data_2.Delay);
        result.StartPendingTime = Mathf.Min(data_1.StartPendingTime, data_2.StartPendingTime);

        result.CollisionVelocity_1 = data_1.CollisionVelocity_1 * weight_1 + data_2.CollisionVelocity_2 * weight_2;
        result.CollisionVelocity_1.x = data_1.CollisionVelocity_1.x;
        result.CollisionVelocity_2 = data_1.CollisionVelocity_2 * weight_1 + data_2.CollisionVelocity_1 * weight_2;
        result.CollisionVelocity_2.x = data_1.CollisionVelocity_2.x;
        result.CollisionPosition_1 = data_1.CollisionPosition_1 * weight_1 + data_2.CollisionPosition_2 * weight_2;
        result.CollisionPosition_1.x = data_1.CollisionPosition_1.x;
        result.CollisionPosition_2 = data_1.CollisionPosition_2 * weight_1 + data_2.CollisionPosition_1 * weight_2;
        result.CollisionPosition_2.x = data_1.CollisionPosition_2.x;
        result.CollisionPoint = data_1.CollisionPoint * weight_1 + data_2.CollisionPoint * weight_2;

        return result;
    }

    private void Update()
    {
        if (!isServer)
        {
            return;
        }

        ProcessCollision();
    }

    private void FixedUpdate()
    {
        if (!isServer)
        {
            return;
        }

        //StartCoroutine(CheckCollisionPendingTimeout());
        CheckCollisionPendingTimeout();
    }

    /// <summary>
    /// Clear time-out collisoin pendings
    /// </summary>
    [Server]
    //private IEnumerator CheckCollisionPendingTimeout()
    private void CheckCollisionPendingTimeout()
    {
        List<Vector2> buffer = new List<Vector2>(_listOfCollisionRequest.Keys);
        foreach(Vector2 key in buffer)
        {
            if (Time.realtimeSinceStartup - _listOfCollisionRequest[key].StartPendingTime >= CollisionPendingTimeout)
            {
                _listOfCollision.Enqueue(_listOfCollisionRequest[key]);
                _listOfCollisionRequest.Remove(key);

                //yield return null;
            }
        }
    }

    /// <summary>
    /// Process queued collision
    /// </summary>
    [Server]
    private void ProcessCollision()
    {
        while (_listOfCollision.Count > 0)
        {
            RxPhysics_CollisionData data = _listOfCollision.Dequeue();
            _physicsCalculator.ComputeCollision(data,
                _listOfEntities[(int)data.CollisionVelocity_1.x],
                _listOfEntities[(int)data.CollisionVelocity_2.x]);
        }
    }

    private void OnDestroy()
    {
        if (isServer)
        {
            NetworkServer.Shutdown();
        }
    }

}
