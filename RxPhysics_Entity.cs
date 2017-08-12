using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Simple physics engine by N7RX.
/// Supports networking.
/// Last modified: 2017-08
/// 
/// This class defines basic physics attributes and controls over physics entity.
/// This script should be attached to single physics object.
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkIdentity))]
public class RxPhysics_Entity : NetworkBehaviour {
    
    // Physics attributes
    [Header("Physics Attributes")]
    [SyncVar] public float Mass = 1;
    [SyncVar] public float Gravity = 9.8f;
    [SyncVar] public bool  EnableGravity;
    [SyncVar] public bool  IsObstacle; // If true, the entity's position won't be affected by collision

    [Header("Damping")]
    [Range(0, 1)] public float Friction = 0;
    [Range(0, 1)] public float AngularDamping = 0;

    // Velocity used to move per fixed update
    [HideInInspector]
    public Vector3 TranslateVelocity = Vector3.zero;

    [Header("Collision Parameters")]
    [SyncVar] [Range(0, 1)] public float Bounciness = 1f;
    [Range(0, 1)] public float VerticalDamping = 0.8f; // Value is suggested to be less than 1,
                                                       // otherwise the object might never settle on the ground 
                                                       // if it's bounciness is greater than 1 with a zero friction.
    [SerializeField] private float _groundDetectionBias = 0.02f;
    [SerializeField] [Range(0, 0.99f)] private float _penetrateDetectionBias = 0.1f;
    
    [Header("Network")]
    [SerializeField] private bool _localSimulationOnly = false;

    // Reference to physics manager (for entity on server)
    private RxPhysics_Judge   _judge;
    // Reference to physics broker  (for entity on client)
    private RxPhysics_Broker  _broker;
    // Physics predictor
    private RxPhysics_Predict _physicsPredictor;

    // Unique ID for each entity
    private int  _entityID = -1;
    private bool _idSynced = false;

    // Velocity used to calculate collision
    private Vector3  _mathematicalVelocity = Vector3.zero;
    private Vector3  _lastPos = Vector3.zero;

    // Collider component
    private Collider _collider;
    // Collision detector
    private Ray _ray = new Ray();
    private RaycastHit _rayHit = new RaycastHit();
    // Raycast detection radius
    private float _detectionRadius = 0;
    private float _penetrateRadius = 0;

    // Gravity smoother
    private float _gravityScalar = 0;
    // Disable control in the air
    private bool  _isAboveGround = false;

    // Force that is currently applied to this entity
    private Vector3 _currentlyAppliedForce = Vector3.zero;
    private Vector3 _acceleration = Vector3.zero;
    // Length of duration that the entity can't be added force on after a collision
    private float _refractoryTime = 0;
    private bool  _isRefractory = false;

    // Cut off velocity that belows certain threshold
    private float _velocityCutoff = 0;
    // Actual friction factor
    private float _frictionFactor = 0;

    // Translation compensation
    private float _compensateDuration = 0.3f;
    [SyncVar] private Vector3 _translateCompensation = Vector3.zero;
    private int _remainingCompensationStep = 0;

    // Network transform synchronization
    private NetworkTransform _transformSync = null;
    [SyncVar(hook = "SwapTransformSyncMode")] private bool _blockSync;
    private readonly float _reSyncTimeout     = 0.5f;
    private float          _initSyncInterval  = 0.05f;
    private readonly float _blockSyncInterval = 65536;


    /// <summary>
    /// Initialization
    /// </summary>
    private void Start()
    {
        _collider = this.GetComponent<Collider>();
        _broker = GameObject.FindObjectOfType<RxPhysics_Broker>();
        _physicsPredictor = _broker.gameObject.GetComponent<RxPhysics_Predict>();
        _transformSync = this.GetComponent<NetworkTransform>();

        _detectionRadius = _collider.bounds.extents.y * (1 + _groundDetectionBias);
        _penetrateRadius = _collider.bounds.extents.y * (1 - _penetrateDetectionBias);
        _lastPos = transform.position;
        _gravityScalar = Gravity * Mass * 0.002f; // Empirical value
        _refractoryTime = _broker.GetRefractoryTime();
        _velocityCutoff = _physicsPredictor.GetVelocityCutoff();
        _frictionFactor = Friction * Friction;

        AdjustComponentSettings();
        RegisterEntity();
    }

    /// <summary>
    /// Adjust related components' settings to suit RxPhyscis' working mechanism
    /// </summary>
    private void AdjustComponentSettings()
    {
        _collider.isTrigger = true;
     
        if (IsObstacle)
        {
            this.GetComponent<Rigidbody>().isKinematic = true;
        }
        this.GetComponent<Rigidbody>().useGravity = false;

        if (_transformSync != null)
        {
            _initSyncInterval = _transformSync.sendInterval;
        }
    }

    /// <summary>
    /// Register current entity to physics manager
    /// </summary>
    private void RegisterEntity()
    {
        // Server
        if (isServer)
        {         
            _judge = GameObject.FindObjectOfType<RxPhysics_Judge>();

            // Register all spawned entities on server
            _judge.RequestRegister(this.GetComponent<NetworkIdentity>().netId);
        }
        // Client
        else
        {
            // Sync ID from server
            _broker.RequestSyncID(this.GetComponent<NetworkIdentity>().netId);
        }
    }

    /// <summary>
    /// Assign current entity's ID; Should only be called from physics manager
    /// </summary>
    /// <param name="id">Assigned ID</param>
    public void AssignEntityID(int id)
    {
        _entityID = id;
        _idSynced = true;
    }

    /// <summary>
    /// Get current entity's ID
    /// </summary>
    /// <returns>This entity's ID</returns>
    public int GetEntityID()
    {
        if (_idSynced)
        {
            return _entityID;
        }
        else
        {
            return -1; // Temporary, not a good solution
        }
    }

    /// <summary>
    /// Get approximate entity bound scale
    /// </summary>
    /// <returns>Bound scale on x axis</returns>
    public float GetColliderRadius()
    {
        return _collider.bounds.extents.x;
    }

    /// <summary>
    /// Calculate physics every fixed interval
    /// </summary>
    private void FixedUpdate()
    {
        if (_idSynced) // Entity's ID needs to be assigned before participating in collision
        {
            ImplementGravity();
            ApplyAccleration();
            ApplyVelocity();
            ApplyDamping();
            CompensatePosition();
            PenetrationAvoidance();
            CalibrateVelocity();
        }
    }

    /// <summary>
    /// Apply accleration based on currently applied force
    /// </summary>
    private void ApplyAccleration()
    {
        _acceleration = _currentlyAppliedForce / Mass;
        TranslateVelocity += _acceleration;
        _currentlyAppliedForce = Vector3.zero;
        _acceleration = Vector3.zero;
    }

    /// <summary>
    /// Simulate gravity
    /// </summary>
    private void ImplementGravity()
    {
        if (!EnableGravity)
        {
            return;
        }

        _ray.origin = this.transform.position;
        // Preserved for gravity inversion
        if (Gravity >= 0)
        {
            _ray.direction = Vector3.down;
        }
        else
        {
            _ray.direction = Vector3.up;
        }

        if (!Physics.Raycast(_ray, _detectionRadius))
        {         
            AddForce(_ray.direction * _gravityScalar);
            _isAboveGround = true;
        }
        else
        {
            _isAboveGround = false;
        }
    }

    /// <summary>
    /// Prevent object from penetrating other objects
    /// </summary>
    private void PenetrationAvoidance()
    {
        if (!IsObstacle)
        {
            _ray.origin = this.transform.position;
            _ray.direction = TranslateVelocity;

            if (Physics.Raycast(_ray, out _rayHit, _penetrateRadius))
            {
                transform.Translate(-_ray.direction.normalized * (_penetrateRadius - _rayHit.distance) * 1.05f);
            }
        }
    }

    /// <summary>
    /// Apply entity movement
    /// </summary>
    private void ApplyVelocity()
    {
        transform.Translate(TranslateVelocity);
    }

    /// <summary>
    /// Apply damping
    /// </summary>
    private void ApplyDamping()
    {
        TranslateVelocity *= (1 - _frictionFactor);

        if (Mathf.Abs(TranslateVelocity.x) <= float.Epsilon)
        {
            TranslateVelocity.x = 0;
        }
        if (Mathf.Abs(TranslateVelocity.y) <= _velocityCutoff)
        {
            TranslateVelocity.y = 0;
        }
        if (Mathf.Abs(TranslateVelocity.z) <= float.Epsilon)
        {
            TranslateVelocity.z = 0;
        }

        //...
    }

    /// <summary>
    /// Calculate mathematical velocity based on fixed interval
    /// </summary>
    private void CalibrateVelocity()
    {
        _mathematicalVelocity = transform.position - _lastPos;
        _lastPos = transform.position;
    }

    /// <summary>
    /// Add force to the physics entity
    /// </summary>
    /// <param name="force">Applied force</param>
    public void AddForce(Vector3 force)
    {
        if (!_isRefractory && !_isAboveGround)
        {
            _currentlyAppliedForce += force;
        }
        else
        {
            // Gravity only
            _currentlyAppliedForce.y += force.y;
        }
    }

    /// <summary>
    /// Invoke custom collision simulation
    /// </summary>
    /// <param name="other">The other collider</param>
    private void OnTriggerEnter(Collider other)
    {
        //if (!IsObstacle && hasAuthority)
        if (!IsObstacle)
        {
            if (other.gameObject.GetComponent<RxPhysics_Entity>() != null)
            {
                RxPhysics_Entity otherEntity = other.gameObject.GetComponent<RxPhysics_Entity>();

                Vector2 idPair; // (larger.ID, smaller.ID)
                idPair.x = _entityID;
                idPair.y = otherEntity.GetEntityID();

                if (idPair.x < 0 || idPair.y <0)
                {
                    return;
                }

                // Construct collision data pack
                RxPhysics_CollisionData data = new RxPhysics_CollisionData();
                data.DontWait = false;

                data.CollisionTime = Time.realtimeSinceStartup + _broker.GetClientServerTimeGap();
                data.CollisionVelocity_1 = new Vector4(_entityID, 
                    _mathematicalVelocity.x, 
                    _mathematicalVelocity.y, 
                    _mathematicalVelocity.z);
                data.CollisionVelocity_2 = new Vector4(idPair.y, 
                    otherEntity.GetVelocity().x, 
                    otherEntity.GetVelocity().y, 
                    otherEntity.GetVelocity().z);
                data.CollisionPosition_1 = new Vector4(_entityID, 
                    transform.position.x, 
                    transform.position.y, 
                    transform.position.z);
                data.CollisionPosition_2 = new Vector4(idPair.y, 
                    otherEntity.transform.position.x, 
                    otherEntity.transform.position.y, 
                    otherEntity.transform.position.z);
                data.CollisionPoint = other.ClosestPoint(this.transform.position); // Approximate collision point

                // Assert pair format
                if (idPair.x < idPair.y)
                {
                    float temp = idPair.x;
                    idPair.x = idPair.y;
                    idPair.y = temp;
                }

                data.IDPair = idPair;

                // Request server arbitration
                if (!_localSimulationOnly && hasAuthority)
                {
                    if (!(Mathf.Abs(data.CollisionPoint.y - this.transform.position.y) >= _penetrateRadius)) // Vertical collison should be calculated locally,
                                                                                                             // otherwise small vibrations may cause nasty results.
                    {
                        if (otherEntity.IsObstacle)
                        {
                            data.DontWait = true;
                        }
                        if (isServer)
                        {
                            _judge.CallCollisonJudge(data);
                        }
                        else
                        {
                            _broker.RequestCollisionJudge(data);
                        }

                        _blockSync = true;
                        if (!isServer)
                        {
                            CmdSwapTransformSyncMode();
                        }
                        if (_transformSync != null)
                        {
                            _transformSync.sendInterval = _blockSyncInterval;
                            Invoke("ReSyncTransform", _reSyncTimeout);
                        }
                    }
                }

                // Perform local prediction
                _physicsPredictor.PreComputeCollision(data, this, otherEntity);

                _isRefractory = true;
                Invoke("RemoveRefractory", _refractoryTime);
            }
        }
    }

    private void RemoveRefractory()
    {
        _isRefractory = false;
    }

    /// <summary>
    /// Get mathematical velocity
    /// </summary>
    /// <returns>Computed velocity</returns>
    public Vector3 GetVelocity()
    {
        return _mathematicalVelocity;
    }

    /// <summary>
    /// Check whether this entity is marked local physcis simulation only
    /// </summary>
    /// <returns>Local simulation mark</returns>
    public bool IsLocalSimOnly()
    {
        return _localSimulationOnly;
    }

    /// <summary>
    /// Set velocity after collision
    /// </summary>
    /// <param name="velocity">New velocity</param>
    [ClientRpc]
    public void RpcSetVelocity(Vector3 velocity)
    {
        this.TranslateVelocity = velocity;
    }

    /// <summary>
    /// Set velocity for non-local player entity after prediction
    /// </summary>
    /// <param name="velocity">Predicted velocity</param>
    public void PredictSetVelocity(Vector3 velocity)
    {
        if (!isLocalPlayer)
        {
            this.TranslateVelocity = velocity;
        }
    }

    /// <summary>
    /// Seperate entity after collision
    /// </summary>
    /// <param name="position">New position</param>
    [ClientRpc]
    public void RpcSetPosition(Vector3 position)
    {
        _blockSync = false;
        if (_transformSync != null)
        {
            _transformSync.sendInterval = _initSyncInterval;
        }

        int factor = (int)(_compensateDuration / Time.fixedDeltaTime);
        if (_remainingCompensationStep < 1)
        {
            _translateCompensation = (position - this.transform.position) / factor;
            _remainingCompensationStep = factor;
        }
        else
        {   // Instantly complete previous compensation
            this.transform.Translate(_translateCompensation * _remainingCompensationStep);
            _translateCompensation = (position - this.transform.position) / factor;
            _remainingCompensationStep = factor;
        }
    }

    /// <summary>
    /// Interpolate position calibration
    /// </summary>
    private void CompensatePosition()
    {
        if (_remainingCompensationStep > 0)
        {
            transform.Translate(_translateCompensation);
            _remainingCompensationStep--;
        }
    }

    /// <summary>
    /// Sync transform sync state from server to client
    /// </summary>
    private void SwapTransformSyncMode(bool sync)
    {
        _blockSync = sync; // Hook function will override auto-sync

        if (_transformSync != null)
        {
            if (!_blockSync)
            {
                _transformSync.sendInterval = _initSyncInterval;
            }
            else
            {
                _transformSync.sendInterval = _blockSyncInterval;
                Invoke("ReSyncTransform", _reSyncTimeout);

                if (!hasAuthority)
                {
                    this.TranslateVelocity = _mathematicalVelocity;
                }
            }
        }
    }

    /// <summary>
    /// Sync transform sync state from client to server
    /// </summary>
    [Command]
    private void CmdSwapTransformSyncMode()
    {
        _blockSync = !_blockSync;

        if (_transformSync != null)
        {
            if (!_blockSync)
            {
                _transformSync.sendInterval = _initSyncInterval;
            }
            else
            {
                _transformSync.sendInterval = _blockSyncInterval;
                Invoke("ReSyncTransform", _reSyncTimeout);

                if (!hasAuthority)
                {
                    this.TranslateVelocity = _mathematicalVelocity;
                }
            }
        }
    }

    /// <summary>
    /// ReSync entity transform between client and server
    /// </summary>
    private void ReSyncTransform()
    {
        _blockSync = false;
        if (_transformSync != null)
        {
            _transformSync.sendInterval = _initSyncInterval;
        }
    }

    /// <summary>
    /// Unregister current entity
    /// </summary>
    private void OnDestroy()
    {
        if (isServer)
        {
            _judge.RemoveEntity(this._entityID);
        }
        else
        {
            if (isLocalPlayer)
            {
                _broker.RequestRemove(this._entityID);
            }
        }
    }
}
