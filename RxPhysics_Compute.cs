﻿using System.Collections;
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

    [Header("Bias")]
    // To what extend the server weights in the result
    [SerializeField] [Range(0, 1)] private float _serverWeight = 0.6f;
    // To what extend the perceived delay weights in the result
    [SerializeField] [Range(0, 2)] private float  _delayWeight = 1f;

    [Header("Calibrations")]
    // If the computed position and current position is less than this threshold, no position update will be made 
    [SerializeField] private float _ignoreDistance = 0.1f;
    // Vertical velocity under this threshold will be cut off to prevent vibrating
    [SerializeField] private float _verticalVelocityCutoff = 0.01f;

    // Heuristic collison detection
    private Ray _ray = new Ray();
    private RaycastHit _rayHit = new RaycastHit(); 

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

        // Time perceived to have elapsed after the collision
        float elpasedTime = (Time.realtimeSinceStartup - data.CollisionTime + data.Delay) * _delayWeight;

        if (!entity_1.IsObstacle && !entity_1.IsLocalSimOnly())
        {
            Vector3 serverPos = pos1 + TranslationCalculus(ref v1_res, entity_1.Friction, elpasedTime);
            if ((entity_1.transform.position - serverPos).magnitude > _ignoreDistance)
            {
                // Take median value
                Vector3 pos1_res = serverPos * _serverWeight + entity_1.transform.position * (1 - _serverWeight);
                HeuristicCollisionDetection(entity_1.GetColliderRadius(), entity_1.transform.position, ref pos1_res);
                entity_1.RpcSetPosition(pos1_res);
            }

            v1_res = v1_res * _serverWeight + entity_1.TranslateVelocity * (1 - _serverWeight);
            entity_1.RpcSetVelocity(v1_res);       
        }
        if (!entity_2.IsObstacle && !entity_2.IsLocalSimOnly())
        {
            Vector3 serverPos = pos2 + TranslationCalculus(ref v2_res, entity_2.Friction, elpasedTime);
            if ((entity_2.transform.position - serverPos).magnitude > _ignoreDistance)
            {
                Vector3 pos2_res = serverPos * _serverWeight + entity_2.transform.position * (1 - _serverWeight);
                HeuristicCollisionDetection(entity_2.GetColliderRadius(), entity_2.transform.position, ref pos2_res);
                entity_2.RpcSetPosition(pos2_res);
            }

            v2_res = v2_res * _serverWeight + entity_2.TranslateVelocity * (1 - _serverWeight);
            entity_2.RpcSetVelocity(v2_res);
        }
    }

    /// <summary>
    /// Calculate translation distance with friction. Note that velocity will also be modifed after the calculation
    /// </summary>
    /// <param name="initVelocity">Instant velocity</param>
    /// <param name="friction">Entity's friction</param>
    /// <param name="time">Duration</param>
    /// <returns>Distance translated</returns>
    private Vector3 TranslationCalculus(ref Vector3 initVelocity, float friction, float time)
    {
        Vector3 result = Vector3.zero;
        float frictionFactor = (1 - friction * friction);

        int steps = (int)(time / Time.fixedDeltaTime);

        for (int i = 0; i < steps; i++)
        {
            result += initVelocity;
            initVelocity *= frictionFactor;
        }

        return result;
    }


    /// <summary>
    /// Check and adjust if the entity would collide with another object when it moves to the calcuated position
    /// </summary>
    /// <param name="radius">Entity radius</param>
    /// <param name="oriPos">Start</param>
    /// <param name="destPos">Destination</param>
    private void HeuristicCollisionDetection(float radius, Vector3 oriPos,ref Vector3 destPos)
    {
        _ray.origin = oriPos;
        _ray.direction = destPos - oriPos;

        if (Physics.Raycast(_ray, out _rayHit, (destPos - oriPos).magnitude))
        {
            destPos = _rayHit.point - radius * _ray.direction.normalized;
        }
    }
}