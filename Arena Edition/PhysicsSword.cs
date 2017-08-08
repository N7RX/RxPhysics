using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Data structure that stores the ID of the collided entity pair
/// </summary>
public struct IDPair
{
    public IDPair(ushort id_1, ushort id_2)
    {
        this.id_1 = id_1;
        this.id_2 = id_2;
    }

    // Overriden operators
    public static bool operator ==(IDPair pair_1, IDPair pair_2)
    {
        return  (pair_1.id_1 == pair_2.id_1 && pair_1.id_2 == pair_2.id_2) ? true : false;
    }
    public static bool operator !=(IDPair pair_1, IDPair pair_2)
    {
        return !(pair_1.id_1 == pair_2.id_1 && pair_1.id_2 == pair_2.id_2) ? true : false;
    }
    public override bool Equals(object obj)
    {
        return base.Equals(obj);
    }
    public override int GetHashCode()
    {
        return base.GetHashCode();
    }

    public ushort id_1;
    public ushort id_2;
}


/// <summary>
/// Physics Manager Class
///
/// Bind only to server manager object; Server only authority
/// </summary>
[RequireComponent(typeof(Mathematician))]
[RequireComponent(typeof(NetworkIdentity))]
public class PhysicsSword : NetworkBehaviour {

    private ushort _idToAssign = 0;
    private Dictionary<ushort, ApproxSensor> _listOfPlayers = new Dictionary<ushort, ApproxSensor>();
    private Stack<IDPair>  _twinCollision = new Stack<IDPair>(); // List of collision info waiting to be paired
    private Stack<IDPair>    _casualStack = new Stack<IDPair>(); // Temporary stack used to transfer data in _twinCollision
    private Queue<IDPair> _solidCollision = new Queue<IDPair>(); // List of collision info waiting to be processed

    private Mathematician _calculator; // Reference to physics calculator


    public override void OnStartServer()
    {
        _calculator = this.GetComponent<Mathematician>();
    }

    private void Update()
    {
        ProcessCollision();
    }

    /// <summary>
    /// Assign internal ID to sensor instance and record it's reference
    /// </summary>
    public ushort BegForID(ApproxSensor bribe)
    {
        _listOfPlayers.Add(_idToAssign, bribe);
        return _idToAssign++;
    }

    /// <summary>
    /// Receive collision information
    /// </summary>
    public void ReportCollision(IDPair couple)
    {
        // Paired info on top of the stack
        if(_twinCollision.Count > 0 && _twinCollision.Peek() == couple)
        {
            _solidCollision.Enqueue(_twinCollision.Pop());
        }
        // Info paired, but not on the top
        else if (_twinCollision.Contains(couple))
        {
            do
            {
                _casualStack.Push(_twinCollision.Pop());
            } while (_twinCollision.Peek() != couple);

            _solidCollision.Enqueue(_twinCollision.Pop());

            do
            {
                _twinCollision.Push(_casualStack.Pop());
            } while (_casualStack.Count > 0);

        }
        // No matching info
        else
        {
            _twinCollision.Push(couple);
        }
    }

    private void ProcessCollision()
    {
        while (_solidCollision.Count > 0)
        {
            IDPair pair = _solidCollision.Dequeue();
            StartCoroutine(_calculator.SolveProblem(_listOfPlayers[pair.id_1], _listOfPlayers[pair.id_2]));
        }
    }

}
