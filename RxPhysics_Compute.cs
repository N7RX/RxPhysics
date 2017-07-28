using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Simple physics engine by N7RX.
/// Supports networking.
/// Last modified: 2017-07
/// 
/// This class implements necessary physics calculations and is replaceable.
/// This script should be attached to physics manager.
/// </summary>
public class RxPhysics_Compute : NetworkBehaviour {

    /// <summary>
    /// Collison physics calculated on server
    /// </summary>
    /// <param name="data">Collision data</param>
    public void ComputeCollision(RxPhysics_CollisionData data, RxPhysics_Entity entity_1, RxPhysics_Entity entity_2)
    {
        if (!isServer)
        {
            return;
        }

        Vector3 v1 = new Vector3(data.CollisionVelocity_1.y, data.CollisionVelocity_1.z, data.CollisionVelocity_1.w);
        Vector3 v2 = new Vector3(data.CollisionVelocity_2.y, data.CollisionVelocity_2.z, data.CollisionVelocity_2.w);

        Vector3 pos1 = new Vector3(data.CollisionPosition_1.y, data.CollisionPosition_1.z, data.CollisionPosition_1.w);
        Vector3 pos2 = new Vector3(data.CollisionPosition_2.y, data.CollisionPosition_2.z, data.CollisionPosition_2.w);

        float elapsedTime = Time.time - data.CollisionTime;

        //Vector3 collisionCenter = (entity_1.transform.position + entity_2.transform.position) / 2;

        /* Test fragment */
        entity_1.RpcSetPosition(pos1 + v2 * ((elapsedTime + 2 * data.Delay) / Time.fixedDeltaTime));
        entity_2.RpcSetPosition(pos2 + v1 * ((elapsedTime + 2 * data.Delay) / Time.fixedDeltaTime));

        entity_1.RpcSetVelocity(v2);
        entity_2.RpcSetVelocity(v1);

        //Debug.Log("PredictPos1:" + entity_1.transform.position);
        //Debug.Log("CalculatedPos1:" + (pos1 + v2 * ((elapsedTime + 2 * data.Delay) / Time.fixedDeltaTime)));
        //Debug.Log("PredictPos2:" + entity_2.transform.position);
        //Debug.Log("CalculatedPos2:" + (pos2 + v1 * ((elapsedTime + 2 * data.Delay) / Time.fixedDeltaTime)));
    }

}