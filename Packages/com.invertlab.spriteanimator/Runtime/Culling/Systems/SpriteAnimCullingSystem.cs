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

            bool layoutXy = SpriteBatchSpawner.LayoutXy;
            var planes = new FixedList128Bytes<float4>();
            foreach (var plane in GeometryUtility.CalculateFrustumPlanes(cam))
                planes.Add(new float4(plane.normal, plane.distance));
            float2 c = new float2(cam.transform.position.x,
                layoutXy ? cam.transform.position.y : cam.transform.position.z);
            bool distOn = s.MaxDistanceSq > 0f;

            // Burst job writes the enableable bit directly — no per-entity
            // managed calls, no array copies (those cost ~50 ms at 100k).
            var job = new CullJob
            {
                Center = c,
                LayoutXy = layoutXy,
                Planes = planes,
                Margin = math.max(0, s.MarginUnits),
                Transforms = SystemAPI.GetComponentLookup<LocalTransform>(true),
                Parents = SystemAPI.GetComponentLookup<Parent>(true),
                PostTransforms = SystemAPI.GetComponentLookup<PostTransformMatrix>(true),
                Frames = SystemAPI.GetComponentLookup<SpriteAnimFrame>(true),
                Flips = SystemAPI.GetComponentLookup<SpriteFlip>(true),
                Bindings = SystemAPI.GetComponentLookup<SpriteSheetBinding>(true),
                Sheets = SystemAPI.GetComponentLookup<SpriteSheetDefinition>(true),
                LegacyAspect = SystemAPI.TryGetSingleton(out SpriteAnimGrid grid) ? math.max(1, grid.CellAspect) : 1,
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
            public bool LayoutXy;
            public FixedList128Bytes<float4> Planes;
            public float Margin;
            public float LegacyAspect;
            [ReadOnly] public ComponentLookup<LocalTransform> Transforms;
            [ReadOnly] public ComponentLookup<Parent> Parents;
            [ReadOnly] public ComponentLookup<PostTransformMatrix> PostTransforms;
            [ReadOnly] public ComponentLookup<SpriteAnimFrame> Frames;
            [ReadOnly] public ComponentLookup<SpriteFlip> Flips;
            [ReadOnly] public ComponentLookup<SpriteSheetBinding> Bindings;
            [ReadOnly] public ComponentLookup<SpriteSheetDefinition> Sheets;
            public bool DistOn;
            public float MaxDistSq;

            void Execute(Entity entity, in LocalTransform lt, EnabledRefRW<SpriteAnimEnabled> enabled)
            {
                var matrix = lt.ToMatrix();
                if (Parents.HasComponent(entity) || PostTransforms.HasComponent(entity))
                    TransformHelpers.ComputeWorldTransformMatrix(entity, out matrix, ref Transforms, ref Parents, ref PostTransforms);
                var position = matrix.c3.xyz;
                float scale = math.length(matrix.c0.xyz) + math.length(matrix.c1.xyz);
                float aspect = LegacyAspect;
                if (Bindings.TryGetComponent(entity, out var binding) && Sheets.TryGetComponent(binding.Sheet, out var sheet))
                    aspect = math.max(1, sheet.CellAspect);
                float radius = scale * aspect;
                if (Frames.TryGetComponent(entity, out var frame))
                    radius *= math.max(1, math.cmax(math.abs(frame.Scale))) + math.length(frame.Offset);
                if (Flips.TryGetComponent(entity, out var flip))
                    radius *= 1 + 2 * math.length(flip.ResolvedPivot - .5f);
                radius += Margin;
                bool vis = true;
                foreach (var plane in Planes)
                    if (math.dot(plane.xyz, position) + plane.w < -radius) { vis = false; break; }
                float2 p = new float2(position.x, LayoutXy ? position.y : position.z);
                if (vis && DistOn)
                    vis = math.distancesq(p, Center) <= MaxDistSq;
                enabled.ValueRW = vis;
            }
        }
    }
}
