using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Physics Calculator Class
///
/// Physics calculation implementation
/// </summary>
public class Mathematician : MonoBehaviour {

    /// <summary>
    /// Calculate collision physics
    /// </summary>
    public IEnumerator SolveProblem(ApproxSensor entity_1, ApproxSensor entity_2)
    {
        Rigidbody rb_1 = entity_1.GetRigidbody();
        Rigidbody rb_2 = entity_2.GetRigidbody();

        float blastMagnitude = (rb_1.velocity - rb_2.velocity).magnitude * 128;

        entity_1.RpcBlastForce((entity_1.transform.position - entity_2.transform.position).normalized 
            * (rb_2.velocity.magnitude / (rb_1.velocity.magnitude + rb_2.velocity.magnitude)) 
            * blastMagnitude);
        entity_2.RpcBlastForce((entity_2.transform.position - entity_1.transform.position).normalized 
            * (rb_1.velocity.magnitude / (rb_1.velocity.magnitude + rb_2.velocity.magnitude)) 
            * blastMagnitude);

        yield return null;
    }

}
