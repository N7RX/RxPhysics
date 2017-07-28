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

    // Reference to physics calculator
    private RxPhysics_Compute _physicsCalculator;

    // Count used to assign entity ID
    private int _entityNum = 0;

    // List of registered physics entity
    private Dictionary<int, RxPhysics_Entity> _listOfEntities = new Dictionary<int, RxPhysics_Entity>();

    // List of collision waiting to be processed
    private Dictionary<Vector2, RxPhysics_CollisionData> _listOfCollisionRequest = new Dictionary<Vector2, RxPhysics_CollisionData>();
    private Queue<RxPhysics_CollisionData> _listOfCollision = new Queue<RxPhysics_CollisionData>();

    private void Start()
    {
        if (!isServer)
        {
            return;
        }

        DontDestroyOnLoad(transform.gameObject);
        _physicsCalculator = this.GetComponent<RxPhysics_Compute>();
    }

    /// <summary>
    /// Register new physics entity
    /// </summary>
    /// <param name="entity">New entity to add</param>
    [Command]
    public void CmdRequestRegister(NetworkInstanceId identity)
    {
        if (!isServer)
        {
            return;
        }

        // Find entity reference
        RxPhysics_Entity entity = NetworkServer.FindLocalObject(identity).GetComponent<RxPhysics_Entity>();
        entity.RpcAssignEntityID(_entityNum);
        _listOfEntities.Add(_entityNum, entity);

        // Increse ID count
        _entityNum++;
    }

    /// <summary>
    /// Remove a entity from current list
    /// </summary>
    /// <param name="id">ID of the entity desired to remove</param>
    [Command]
    public void CmdRemoveEntity(int id)
    {
        if (!isServer)
        {
            return;
        }

        if (_listOfEntities.ContainsKey(id))
        {
            _listOfEntities.Remove(id);
        }
    }

    /// <summary>
    /// Call to the physics manager when collison happened
    /// </summary>
    /// <param name="idPair">IDs of the entities collided</param>
    [Command]
    public void CmdCallCollisonJudge(Vector2 idPair, RxPhysics_CollisionData data)
    {
        if (!isServer)
        {
            return;
        }

        // Collision pending
        if (!_listOfCollisionRequest.ContainsKey(idPair))
        {
            data.Delay = Time.time - data.CollisionTime;
            data.StartPendingTime = Time.time;
            _listOfCollisionRequest.Add(idPair, data);
        }
        // Collision confirmed
        else
        {
            _listOfCollision.Enqueue(_listOfCollisionRequest[idPair]); // Take the first arrived data as criteria
            _listOfCollisionRequest.Remove(idPair);
        }
    }

    /// <summary>
    /// Functions called per frame
    /// </summary>
    private void Update()
    {
        if (!isServer)
        {
            return;
        }

        ProcessCollision();
    }

    /// <summary>
    /// Functions called per fixed inverval
    /// </summary>
    private void FixedUpdate()
    {
        if (!isServer)
        {
            return;
        }

        CheckCollisionPendingTimeout();
    }

    /// <summary>
    /// Timeout collision pendings
    /// </summary>
    private void CheckCollisionPendingTimeout()
    {
        foreach (Vector2 key in _listOfCollisionRequest.Keys)
        {
            if (Time.time - _listOfCollisionRequest[key].StartPendingTime >= CollisionPendingTimeout)
            {
                _listOfCollision.Enqueue(_listOfCollisionRequest[key]);
                _listOfCollisionRequest.Remove(key);
            }
        }
    }

    /// <summary>
    /// Predict if collision would happen between two entities
    /// </summary>
    /// <param name="entity_1">Entity one</param>
    /// <param name="entity_2">Entity two</param>
    /// <returns></returns>
    //private bool PredictCollision(RxPhysics_Entity entity_1, RxPhysics_Entity entity_2)
    //{
    //    if ( ((entity_1.transform.position + entity_1.TranslateVelocity)
    //        - (entity_2.transform.position + entity_2.TranslateVelocity)).magnitude
    //        <= entity_1.GetCollideRadius() + entity_2.GetCollideRadius()) // Predict one step ahead
    //    {
    //        return true;
    //    }
    //    else
    //    {
    //        return false;
    //    }
    //}

    /// <summary>
    /// Process queued collision
    /// </summary>
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

}
