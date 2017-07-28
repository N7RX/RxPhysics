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
    [Server]
    public void ComputeCollision(RxPhysics_CollisionData data, RxPhysics_Entity entity_1, RxPhysics_Entity entity_2)
    {
        Vector3 v1_ori = new Vector3(data.CollisionVelocity_1.y, data.CollisionVelocity_1.z, data.CollisionVelocity_1.w);
        Vector3 v2_ori = new Vector3(data.CollisionVelocity_2.y, data.CollisionVelocity_2.z, data.CollisionVelocity_2.w);

        Vector3 pos1 = new Vector3(data.CollisionPosition_1.y, data.CollisionPosition_1.z, data.CollisionPosition_1.w);
        Vector3 pos2 = new Vector3(data.CollisionPosition_2.y, data.CollisionPosition_2.z, data.CollisionPosition_2.w);

        float v1_res_mag = 0, v2_res_mag = 0;
        if (!entity_1.IsObstacle && !entity_2.IsObstacle)
        {
            v1_res_mag = (((entity_1.Mass - entity_2.Mass) * v1_ori + 2 * entity_2.Mass * v2_ori) / (entity_1.Mass + entity_2.Mass)).magnitude
                * entity_1.CollideBounciness;
            v2_res_mag = (((entity_2.Mass - entity_1.Mass) * v2_ori + 2 * entity_1.Mass * v1_ori) / (entity_1.Mass + entity_2.Mass)).magnitude
                * entity_2.CollideBounciness;
        }
        else if (entity_1.IsObstacle && !entity_2.IsObstacle)
        {
            v2_res_mag = v2_ori.magnitude * entity_2.CollideBounciness;
        }
        else if (!entity_1.IsObstacle && entity_2.IsObstacle)
        {
            v1_res_mag = v1_ori.magnitude * entity_1.CollideBounciness;
        }

        Vector3 collisionCenter = (pos1 + pos2) / 2;

        Vector3 v1_res = (pos1 - collisionCenter).normalized * v1_res_mag;
        Vector3 v2_res = (pos2 - collisionCenter).normalized * v2_res_mag;

        float elapsedTime = Time.realtimeSinceStartup - data.CollisionTime;

        // Sample calculation, without friction

        if (!entity_1.IsObstacle)
        {
            entity_1.RpcSetPosition(pos1 + v1_res * ((elapsedTime + 2 * data.Delay) / Time.fixedDeltaTime));
            entity_1.RpcSetVelocity(v1_res);
        }
        if (!entity_2.IsObstacle)
        {
            entity_2.RpcSetPosition(pos2 + v2_res * ((elapsedTime + 2 * data.Delay) / Time.fixedDeltaTime));
            entity_2.RpcSetVelocity(v2_res);
        }
    }

}