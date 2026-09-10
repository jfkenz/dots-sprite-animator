using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Package API: ensure a kinematic Unity Physics hurtbox for an open-scene
    /// sprite (SubScene bakers do not run on those GameObjects). Call from Play
    /// when Method = UnityPhysics and no baked entity exists yet.
    /// </summary>
    public static class SpriteUnityPhysicsHurtbox
    {
        /// <summary>
        /// Create or refresh a runtime hurtbox entity for <paramref name="host"/>.
        /// Syncs LocalTransform to the host each call. Returns the entity
        /// (Null if world/profile unavailable).
        /// </summary>
        public static Entity Ensure(
            Transform host, SpriteAnimSetAuthoring set, Entity existing = default)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (host == null || world == null || !world.IsCreated)
                return Entity.Null;

            var em = world.EntityManager;
            var data = set != null && set.Profile != null ? set.Profile.Data : null;
            if (data == null)
                return Entity.Null;

            Entity entity = existing;
            if (entity == Entity.Null || !em.Exists(entity))
            {
                entity = em.CreateEntity();
                em.AddComponentData(entity, LocalTransform.FromPositionRotationScale(
                    host.position, quaternion.identity, 1f));
                em.AddComponentData(entity, new LocalToWorld
                {
                    Value = float4x4.TRS(host.position, quaternion.identity, new float3(1f)),
                });
                em.AddComponentData(entity, new SpriteHurtbox());
                em.AddSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });
            }

            // Build collider once — recreating the blob every Ensure call
            // can drop the body from the broadphase mid-attack.
            if (!em.HasComponent<PhysicsCollider>(entity))
            {
                var bodies = new List<FrameBoxDef>();
                SpriteUnityPhysicsShape.CollectCharacterBodyBoxes(data, bodies);
                var pivot = data.Pivot == default ? new Vector2(0.5f, 0f) : (Vector2)data.Pivot;
                float sizeUnits = set != null ? set.SizeUnits : 1f;
                var sheet = SpriteSocketWorld.DisplaySheet(data, null);
                var flipAxis = sheet != null
                    ? (float2)SpriteSocketWorld.PixelsFromPivotToMeshLocal(sheet, pivot, Vector2.zero)
                    : float2.zero;
                var blob = SpriteUnityPhysicsShape.CreateBodyCollider(
                    bodies, pivot, sizeUnits, false, false,
                    flipAxis: flipAxis);

                em.AddComponentData(entity, new PhysicsCollider { Value = blob });
                em.AddComponentData(entity, PhysicsMass.CreateKinematic(blob.Value.MassProperties));
                em.AddComponentData(entity, new PhysicsVelocity());
                em.AddComponentData(entity, new PhysicsGravityFactor { Value = 0f });
                em.SetSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });
            }
            else
            {
                em.SetSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });
            }

            SyncTransform(em, entity, host.position);
            return entity;
        }

        public static void SyncTransform(EntityManager em, Entity entity, Vector3 position)
        {
            if (entity == Entity.Null || !em.Exists(entity))
                return;
            var pos = (float3)position;
            if (em.HasComponent<LocalTransform>(entity))
            {
                var lt = em.GetComponentData<LocalTransform>(entity);
                lt.Position = pos;
                em.SetComponentData(entity, lt);
            }
            if (em.HasComponent<LocalToWorld>(entity))
            {
                em.SetComponentData(entity, new LocalToWorld
                {
                    Value = float4x4.TRS(pos, quaternion.identity, new float3(1f)),
                });
            }
        }

        public static void Destroy(Entity entity)
        {
            if (entity == Entity.Null)
                return;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;
            var em = world.EntityManager;
            if (em.Exists(entity))
                em.DestroyEntity(entity);
        }
    }
}
