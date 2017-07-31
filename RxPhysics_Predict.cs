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

    // Vertical velocity under this threshold will be cut off to prevent vibrating
    [SerializeField] private float _verticalVelocityCutoff = 0.01f;

    public float GetVelocityCutoff()
    {
        return _verticalVelocityCutoff;
    }

    /// <summary>
    /// PreCalculate collision before server provides arbitration
    /// </summary>
    /// <param name="data">Collision data</param>
    public void PreComputeCollision(RxPhysics_CollisionData data, RxPhysics_Entity entity_1, RxPhysics_Entity entity_2)
    {
        if (!entity_1.IsObstacle) // Entity 1 should be the local player itself
        {
            Vector3 v1_ori = new Vector3(data.CollisionVelocity_1.y, data.CollisionVelocity_1.z, data.CollisionVelocity_1.w);
            Vector3 v2_ori = new Vector3(data.CollisionVelocity_2.y, data.CollisionVelocity_2.z, data.CollisionVelocity_2.w);

            Vector3 pos1 = new Vector3(data.CollisionPosition_1.y, data.CollisionPosition_1.z, data.CollisionPosition_1.w);
            Vector3 pos2 = new Vector3(data.CollisionPosition_2.y, data.CollisionPosition_2.z, data.CollisionPosition_2.w);

            // Sample calculation using momentum physics

            float v1_res_mag = 0, v2_res_mag = 0;
            if (!entity_1.IsObstacle && !entity_2.IsObstacle)
            {
                v1_res_mag = (((entity_1.Mass - entity_2.Mass) * v1_ori + 2 * entity_2.Mass * v2_ori) / (entity_1.Mass + entity_2.Mass)).magnitude
                    * entity_1.Bounciness;
                v2_res_mag = (((entity_2.Mass - entity_1.Mass) * v2_ori + 2 * entity_1.Mass * v1_ori) / (entity_1.Mass + entity_2.Mass)).magnitude
                    * entity_2.Bounciness;
            }
            else if (entity_1.IsObstacle && !entity_2.IsObstacle)
            {
                v2_res_mag = v2_ori.magnitude * entity_2.Bounciness;
            }
            else if (!entity_1.IsObstacle && entity_2.IsObstacle)
            {
                v1_res_mag = v1_ori.magnitude * entity_1.Bounciness;
            }

            Vector3 v1_res = (v1_ori - 2 * Vector3.Project(v1_ori, data.CollisionPoint - pos1)).normalized * v1_res_mag;
            v1_res.y *= entity_1.VerticalDamping;
            if (Mathf.Abs(v1_res.y) < _verticalVelocityCutoff)
            {
                v1_res.y = 0;
            }

            Vector3 v2_res = (v2_ori - 2 * Vector3.Project(v2_ori, data.CollisionPoint - pos2)).normalized * v2_res_mag;
            v2_res.y *= entity_2.VerticalDamping;
            if (Mathf.Abs(v2_res.y) < _verticalVelocityCutoff)
            {
                v2_res.y = 0;
            }

            if (!entity_1.IsObstacle)
            {
                entity_1.TranslateVelocity = v1_res;
            }
            if (!entity_2.IsObstacle)
            {
                entity_2.PredictSetVelocity(v2_res);
            }
        }
    }

}
