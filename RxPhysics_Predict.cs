using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple physics engine by N7RX.
/// Supports networking.
/// Last modified: 2017-07
/// 
/// This class implements necessary physics prediction and is replaceable.
/// This script should be attached with physics broker.
/// </summary>
public class RxPhysics_Predict : MonoBehaviour {

    /// <summary>
    /// PreCalculate collision before server provides arbitration
    /// </summary>
    /// <param name="data">Collision data</param>
    public void PreComputeCollision(RxPhysics_CollisionData data, RxPhysics_Entity entity_1, RxPhysics_Entity entity_2)
    {
        if (!entity_1.IsObstacle)
        {
            Vector3 v1_ori = new Vector3(data.CollisionVelocity_1.y, data.CollisionVelocity_1.z, data.CollisionVelocity_1.w);
            Vector3 v2_ori = new Vector3(data.CollisionVelocity_2.y, data.CollisionVelocity_2.z, data.CollisionVelocity_2.w);

            Vector3 pos1 = new Vector3(data.CollisionPosition_1.y, data.CollisionPosition_1.z, data.CollisionPosition_1.w);
            Vector3 pos2 = new Vector3(data.CollisionPosition_2.y, data.CollisionPosition_2.z, data.CollisionPosition_2.w);

            float v1_res_mag = (((entity_1.Mass - entity_2.Mass) * v1_ori + 2 * entity_2.Mass * v2_ori) / (entity_1.Mass + entity_2.Mass)).magnitude
                * entity_1.CollideBounciness;

            Vector3 collisionCenter = (pos1 + pos2) / 2;

            Vector3 v1_res = (pos1 - collisionCenter).normalized * v1_res_mag;

            entity_1.RpcSetVelocity(v1_res);
        }
    }

}
