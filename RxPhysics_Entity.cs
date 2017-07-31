using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Simple physics engine by N7RX.
/// Supports networking.
/// Last modified: 2017-07
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
    public float Mass = 1;
    public float Gravity = 9.8f;
    public bool EnableGravity;
    public bool IsObstacle; // Obstacle's position won't be affected by collision

    [Header("Damping")]
    [Range(0, 1)] public float Friction = 0;
    [Range(0, 1)] public float AngularDamping = 0;

    // Velocity used to move per fixed update
    [HideInInspector]
    public Vector3 TranslateVelocity = Vector3.zero;

    [Header("Collision Parameters")]
    [Range(0, 1)] public float Bounciness = 1f;
    [Range(0, 1)] public float VerticalDamping = 0.8f; // Value is suggested to be less than 1,
                                                       // otherwise the object might never settle on the ground 
                                                       // if it's bounciness is greater than 1 with a zero friction.
    [SerializeField] private float _groundDetectionBias = 0.02f;
    [SerializeField] [Range(0, 0.99f)] private float _penetrateDetectionBias = 0.1f;

    [Header("Network")]
    [SerializeField] private bool _localSimulationOnly = false;

    // Reference to physics manager (for entity on server)
    private RxPhysics_Judge _judge;
    // Reference to physics broker  (for entity on client)
    private RxPhysics_Broker _broker;
    // Physics predictor
    private RxPhysics_Predict _physicsPredictor;

    // Unique ID for each entity
    private int _entityID;
    // Velocity used to calculate collision
    private Vector3 _mathematicalVelocity;
    private Vector3 _lastPos;
    // Collider component
    private Collider _collider;
    // Collision detector
    private Ray _ray = new Ray();
    private RaycastHit _rayHit = new RaycastHit();
    private float _detectionRadius;
    private float _penetrateRadius;
    // Gravity smoother
    private float _gravityScalar;
    // Disable control in the air
    private bool  _isAboveGround;
    // Force that is currently applied to this entity
    private Vector3 _currentlyAppliedForce;
    private Vector3 _acceleration;
    // Length of duration that the entity can't be added force on after a collision
    private float _refractoryTime = 0;
    private bool  _isRefractory = false;
    // Cut off velocity that belows certain threshold
    private float _velocityCutoff;
    // Actual friction factor
    private float _frictionFactor;


    /// <summary>
    /// Initialization
    /// </summary>
    private void Start()
    {
        _collider = this.GetComponent<Collider>();
        _detectionRadius = _collider.bounds.extents.y * (1 + _groundDetectionBias);
        _penetrateRadius = _collider.bounds.extents.y * (1 - _penetrateDetectionBias); // Empirical value
        _lastPos = transform.position;
        _currentlyAppliedForce = Vector3.zero;
        _acceleration = Vector3.zero;
        _gravityScalar = Gravity * Mass * 0.002f; // Empirical value
        _broker = GameObject.FindObjectOfType<RxPhysics_Broker>();
        _physicsPredictor = _broker.gameObject.GetComponent<RxPhysics_Predict>();
        _refractoryTime = _broker.GetRefractoryTime();
        _velocityCutoff = _physicsPredictor.GetVelocityCutoff();
        _frictionFactor = Friction * Friction;

        // Force correct component settings
        _collider.isTrigger = true;
        this.GetComponent<Rigidbody>().useGravity = false;
        if (IsObstacle)
        {
            this.GetComponent<Rigidbody>().isKinematic = true;
        }

        RegisterEntity();
    }

    /// <summary>
    /// Register current entity to physics manager
    /// </summary>
    private void RegisterEntity()
    {
        if (isServer)
        {
            // Directly get reference
            _judge = GameObject.FindObjectOfType<RxPhysics_Judge>();
            _judge.RequestRegister(this.GetComponent<NetworkIdentity>().netId);

        }
        else
        {
            // Request reference from broker
            _broker.RequestRegister(this.GetComponent<NetworkIdentity>().netId);
        }
    }

    /// <summary>
    /// Assign current entity's ID; Should only be called from physics manager
    /// </summary>
    /// <param name="id">Assigned ID</param>
    [ClientRpc]
    public void RpcAssignEntityID(int id)
    {
        _entityID = id;
    }

    /// <summary>
    /// Get current entity's ID
    /// </summary>
    /// <returns>This entity's ID</returns>
    public int GetEntityID()
    {
        return _entityID;
    }

    public float GetColliderRadius()
    {
        return _collider.bounds.extents.x;
    }

    /// <summary>
    /// Calculate physics every fixed interval
    /// </summary>
    private void FixedUpdate()
    {      
        ImplementGravity();
        ApplyAccleration();
        ApplyVelocity();
        ApplyDamping();
        PenetrationAvoidance();
        CalibrateVelocity();
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
                transform.Translate(-_ray.direction * (_penetrateRadius - _rayHit.distance));
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
        if (isLocalPlayer && !IsObstacle)
        {
            if (other.gameObject.GetComponent<RxPhysics_Entity>() != null)
            {
                RxPhysics_Entity otherEntity = other.gameObject.GetComponent<RxPhysics_Entity>();

                Vector2 idPair; // (larger.ID, smaller.ID)
                idPair.x = _entityID;
                idPair.y = otherEntity.GetEntityID();

                // Construct collision data pack
                RxPhysics_CollisionData data = new RxPhysics_CollisionData();

                data.CollisionTime = Time.realtimeSinceStartup + _broker.GetClientServerTimeGap();
                data.CollisionVelocity_1 = new Vector4(_entityID, _mathematicalVelocity.x, _mathematicalVelocity.y, _mathematicalVelocity.z);
                data.CollisionVelocity_2 = new Vector4(idPair.y, otherEntity.GetVelocity().x, otherEntity.GetVelocity().y, otherEntity.GetVelocity().z);
                data.CollisionPosition_1 = new Vector4(_entityID, transform.position.x, transform.position.y, transform.position.z);
                data.CollisionPosition_2 = new Vector4(idPair.y, otherEntity.transform.position.x, otherEntity.transform.position.y, otherEntity.transform.position.z);
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
                if (!_localSimulationOnly)
                {
                    if (isServer)
                    {
                        _judge.CallCollisonJudge(data);
                    }
                    else
                    {
                        _broker.RequestCollisionJudge(data);
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
        if (isLocalPlayer)
        {
            this.TranslateVelocity = velocity;
        }
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
        if (isLocalPlayer)
        {
            //this.transform.position = position;
            this.transform.Translate(position - this.transform.position);
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
            _broker.RequestRemove(this._entityID);
        }
    }
}
