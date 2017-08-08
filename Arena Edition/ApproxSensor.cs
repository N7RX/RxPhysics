using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Sensor Response Class
///
/// Bind with player object; Used with player trigger volume
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(NetworkTransform))]
[RequireComponent(typeof(Rigidbody))]
public class ApproxSensor : NetworkBehaviour {

    // Unique ID assigned within this calculation system
    private ushort _id = 65535;
    public  ushort SensorID
    {
        get { return _id; }
    }

    private Rigidbody _rigidbody;
    private bool _isStunned = false; // Mark this entity as unmovable

    private PhysicsSword _jimmyBar;  // Reference to physics manager


    private void Start()
    {
        _rigidbody = this.GetComponent<Rigidbody>();

        // Only entities on server can communicate directly with the physics manager
        if (isServer)
        {
            _jimmyBar = GameObject.FindObjectOfType<PhysicsSword>();
            _id = _jimmyBar.BegForID(this);
        }
    }

    /// <summary>
    /// Event triggered when two players' trigger volume overlaped
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        // Process event only on server instances
        if (!isServer)
        {
            return;
        }

        if (other.CompareTag("Player"))
        {
            // Send collision info
            _jimmyBar.ReportCollision(PackIDPair(_id, other.GetComponent<ApproxSensor>().SensorID));
        }
    }

    /// <summary>
    /// Apply collision force to client instance(s)
    /// </summary>
    [ClientRpc]
    public void RpcBlastForce(Vector3 force)
    {
        _isStunned = true;
        _rigidbody.AddForce(force);

        Invoke("Recover", 0.5f);
    }

    /// <summary>
    /// Construct and return a standarized collision ID pair
    /// </summary>
    private IDPair PackIDPair(ushort id_1, ushort id_2)
    {
        return new IDPair((ushort)Mathf.Max(id_1, id_2), (ushort)Mathf.Min(id_1, id_2));
    }

    public Rigidbody GetRigidbody()
    {
        return _rigidbody;
    }

    public bool IsControllable()
    {
        return !_isStunned;
    }

    /// <summary>
    /// Mark the entity as controllable again
    /// </summary>
    private void Recover()
    {
        _isStunned = false;
    }

}
