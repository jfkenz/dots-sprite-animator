using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Culling settings singleton.</summary>
    public struct SpriteCullSettings : IComponentData
    {
        public float MarginUnits;     // expand camera rect by this
        public float MaxDistanceSq;   // 0 = distance cull off
    }

    /// <summary>
    /// Toggles SpriteAnimEnabled from the main camera's view rect (top-down
    /// ortho) plus optional distance. Disabled sprites skip animation ticking.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(InvertLab.Sprites.DOTS.SpriteAnimPlayerSystem))]
    public partial struct SpriteAnimCullingSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.EntityManager.AddComponentData(
                state.EntityManager.CreateEntity(),
                new SpriteCullSettings { MarginUnits = 8f, MaxDistanceSq = 0f });
        }

        public void OnUpdate(ref SystemState state)
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (!SystemAPI.TryGetSingleton(out SpriteCullSettings s)) return;

            float halfH = cam.orthographicSize + s.MarginUnits;
            float halfW = halfH * cam.aspect + s.MarginUnits;
            float2 c = new float2(cam.transform.position.x, cam.transform.position.z);
            bool distOn = s.MaxDistanceSq > 0f;

            // Burst job writes the enableable bit directly — no per-entity
            // managed calls, no array copies (those cost ~50 ms at 100k).
            var job = new CullJob
            {
                Center = c,
                HalfW = halfW,
                HalfH = halfH,
                DistOn = distOn,
                MaxDistSq = s.MaxDistanceSq,
            };
            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithOptions(Unity.Entities.EntityQueryOptions.IgnoreComponentEnabledState)]
        [WithAll(typeof(SpriteAnimEnabled))]
        partial struct CullJob : IJobEntity
        {
            public float2 Center;
            public float HalfW;
            public float HalfH;
            public bool DistOn;
            public float MaxDistSq;

            void Execute(in LocalTransform lt, EnabledRefRW<SpriteAnimEnabled> enabled)
            {
                float2 p = new float2(lt.Position.x, lt.Position.z);
                bool vis = math.abs(p.x - Center.x) <= HalfW && math.abs(p.y - Center.y) <= HalfH;
                if (vis && DistOn)
                    vis = math.distancesq(p, Center) <= MaxDistSq;
                enabled.ValueRW = vis;
            }
        }
    }
}
