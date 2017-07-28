using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Simple physics engine by N7RX.
/// Supports networking.
/// Last modified: 2017-07
/// 
/// This class implements necessary physics prediction and is replaceable.
/// This script should be attached with physics entity.
/// </summary>
public class RxPhysics_Predict : NetworkBehaviour {

    /// <summary>
    /// PreCalculate collision before server provides arbitration
    /// </summary>
    /// <param name="data">Collision data</param>
    public void PreComputeCollision(RxPhysics_CollisionData data, RxPhysics_Entity entity_1, RxPhysics_Entity entity_2)
    {
        /* Test fragment */
        Vector3 v2 = new Vector3(data.CollisionVelocity_2.y, data.CollisionVelocity_2.z, data.CollisionVelocity_2.w);
        entity_1.TranslateVelocity = v2; 
        
    }

}
