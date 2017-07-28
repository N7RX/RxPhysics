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
    [Range(0, 1)]
    public float Friction = 0;
    [Range(0, 1)]
    public float AngularDamping = 0;

    // Velocity used to move per fixed update
    [HideInInspector]
    public Vector3 TranslateVelocity;

    // Effects parameters
    [Header("Effects Parameters")]
    [Range(0, 1)]
    public float VerticalBounciness = 0.8f;
    [Range(0, 1)]
    public float CollideBounciness = 1f;
    [SerializeField]
    private float _detectionBias = 0.02f;
    [SerializeField]
    [Range(0, 1)]
    private float _smoothGravity = 0.02f;

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
    private float _collideRadius;
    // Collision detector
    private Ray _ray;
    private RaycastHit _rayHit;
    private float _detectionRadius;
    private bool  _verticalImpact;
    // Gravity smoother
    private float _gravityScalar;
    // Threshold to cut-off vertical speed
    private float _verticalBounceThreshold = float.Epsilon;
    // Force that is currently applied to this entity
    private Vector3 _currentlyAppliedForce;
    private Vector3 _acceleration;

    /// <summary>
    /// Initialization
    /// </summary>
    private void Start()
    {
        _collider = this.GetComponent<Collider>();
        _detectionRadius = _collider.bounds.extents.y * (1 + _detectionBias);
        _collideRadius = (_collider.bounds.extents.x + _collider.bounds.extents.y + _collider.bounds.extents.z) / 3; // Take average value
        _lastPos = transform.position;
        _currentlyAppliedForce = Vector3.zero;
        _acceleration = Vector3.zero;
        _gravityScalar = Gravity * Mass * _smoothGravity * _smoothGravity * 5; // Empirical value
        _broker = GameObject.FindObjectOfType<RxPhysics_Broker>();
        _physicsPredictor = _broker.gameObject.GetComponent<RxPhysics_Predict>();

        Register();
    }

    /// <summary>
    /// Register current entity to physics manager
    /// </summary>
    private void Register()
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

    /// <summary>
    /// Get entity's collide radius
    /// </summary>
    /// <returns>Collide radius</returns>
    public float GetCollideRadius()
    {
        return _collideRadius;
    }

    /// <summary>
    /// Calculate physics every fixed interval
    /// </summary>
    private void FixedUpdate()
    {
        ApplyAccleration();
        ImplementGravity();
        VerticalBounce();
        ApplyVelocity();
        ApplyFriction();

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

        _ray.origin = transform.position;
        // Preserved for gravity inversion
        if (Gravity >= 0)
        {
            _ray.direction = Vector3.down;
        }
        else
        {
            _ray.direction = Vector3.up;
        }

        if (!Physics.Raycast(_ray, out _rayHit, _detectionRadius))
        {         
            _verticalImpact = false;
            AddForce(_ray.direction * _gravityScalar);
        }
        else
        {
            _verticalImpact = true;
        }
    }

    /// <summary>
    /// Simulate bounce effect when hitting the ground
    /// </summary>
    private void VerticalBounce()
    {
        if (!_verticalImpact)
        {
            return;
        }

        if (Mathf.Abs(TranslateVelocity.y) > _verticalBounceThreshold)
        {
            TranslateVelocity.y = -TranslateVelocity.y * VerticalBounciness;
        }
        else
        {
            TranslateVelocity.y = 0;
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
    private void ApplyFriction()
    {
        TranslateVelocity *= (1 - Friction);
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
        _currentlyAppliedForce += force;
    }

    /// <summary>
    /// Invoke custom collision simulation
    /// </summary>
    /// <param name="other">The other collider</param>
    private void OnTriggerEnter(Collider other)
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

            // Assert pair format
            if (idPair.x < idPair.y)
            {
                float temp = idPair.x;
                idPair.x = idPair.y;
                idPair.y = temp;
            }

            data.IDPair = idPair;

            // Request server arbitration
            if (isServer)
            {
                _judge.CallCollisonJudge(data);
            }
            else
            {
                _broker.RequestCollisionJudge(data);
            }

            // Perform local prediction
            _physicsPredictor.PreComputeCollision(data, this, otherEntity);
        }
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
    /// Set velocity after collision
    /// </summary>
    /// <param name="velocity">New velocity</param>
    [ClientRpc]
    public void RpcSetVelocity(Vector3 velocity)
    {
        this.TranslateVelocity = velocity;
    }

    /// <summary>
    /// Seperate entity after collision
    /// </summary>
    /// <param name="position">New position</param>
    [ClientRpc]
    public void RpcSetPosition(Vector3 position)
    {
        //this.transform.position = position;
        this.transform.Translate(position - this.transform.position);
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
