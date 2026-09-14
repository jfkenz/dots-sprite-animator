using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Advance Parts clock and write joint LocalTransform + PostTransformMatrix scale.
    /// Runs before TransformSystemGroup so LocalToWorld is fresh for depth/culling/render.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(SpriteSortDepthSystem))]
    public partial struct SpritePartsPlayerSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;
            foreach (var (playerRef, setRef, entity) in
                     SystemAPI.Query<RefRW<SpritePartsPlayer>, RefRO<SpritePartsSetRef>>()
                              .WithEntityAccess())
            {
                var blob = setRef.ValueRO.Set;
                if (!blob.IsCreated)
                    continue;
                ref var set = ref blob.Value;
                ref var player = ref playerRef.ValueRW;
                if (player.ClipIndex < 0 || player.ClipIndex >= set.Clips.Length)
                    continue;

                ref var clip = ref set.Clips[player.ClipIndex];
                byte already = player.Completed;
                if (em.HasComponent<SpritePartsCompleted>(entity))
                    already = 1;

                var tick = SpritePartsPlayback.Tick(
                    player.TimeSeconds, player.SpeedMultiplier, clip.SpeedMultiplier,
                    clip.Duration, clip.WrapMode, player.Playing, already, dt);
                player.TimeSeconds = tick.TimeSeconds;
                player.Playing = tick.Playing;
                player.Completed = tick.AlreadyCompleted;
                if (tick.CompletedThisTick != 0 && !em.HasComponent<SpritePartsCompleted>(entity))
                    em.AddComponentData(entity, new SpritePartsCompleted());

                ApplyPoseImmediate(em, entity, ref set, player.ClipIndex, player.TimeSeconds);
            }
        }

        static void ApplyPoseImmediate(EntityManager em, Entity root, ref SpritePartsSetBlob set,
            int clipIndex, float time)
        {
            if (!em.HasBuffer<SpritePartLink>(root))
                return;
            var links = em.GetBuffer<SpritePartLink>(root);
            for (int i = 0; i < links.Length; i++)
            {
                var part = links[i].Part;
                if (part == Entity.Null || !em.Exists(part))
                    continue;
                int slotIndex = links[i].SlotIndex;
                if (slotIndex < 0 || slotIndex >= set.Slots.Length)
                    continue;
                SpritePartsSampler.SampleSlot(ref set, clipIndex, slotIndex, time, out var pose);
                SpritePartsPoseUtility.ApplyPartTransform(em, part, pose);
            }
            SpritePartsPoseUtility.ApplyFacing(em, root);
        }
    }

    /// <summary>
    /// Render-only Parts depth after transforms. drawIndex = characterOrder*64 + partRank.
    /// Keeps part LocalTransform.z = 0.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial struct SpritePartsRenderDepthSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            foreach (var (slot, depth, entity) in
                     SystemAPI.Query<RefRO<SpritePartSlot>, RefRW<SpritePartRenderDepth>>()
                              .WithEntityAccess())
            {
                Entity root = slot.ValueRO.Root;
                int order = 0;
                int rank = 0;
                if (root != Entity.Null && em.Exists(root) && em.HasComponent<SpritePartsDrawGroup>(root))
                    order = em.GetComponentData<SpritePartsDrawGroup>(root).CharacterOrder;
                if (root != Entity.Null && em.Exists(root) && em.HasComponent<SpritePartsSetRef>(root))
                {
                    var blob = em.GetComponentData<SpritePartsSetRef>(root).Set;
                    if (blob.IsCreated)
                    {
                        int si = slot.ValueRO.SlotIndex;
                        if (si >= 0 && si < blob.Value.Slots.Length)
                            rank = blob.Value.Slots[si].DrawRank;
                    }
                }
                int drawIndex = SpritePartsPlayback.DrawIndex(order, rank);
                depth.ValueRW.Value = SpriteSortDepth.FromIndex(drawIndex);

                // Keep joint local z at 0 (no SpriteSortDepth accumulation).
                if (em.HasComponent<LocalTransform>(entity))
                {
                    var lt = em.GetComponentData<LocalTransform>(entity);
                    if (lt.Position.z != 0f)
                    {
                        lt.Position.z = 0f;
                        em.SetComponentData(entity, lt);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Parts-aware culling after transforms. Toggles SpriteAnimEnabled on part entities only.
    /// Root clock keeps advancing regardless of visibility.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpritePartsRenderDepthSystem))]
    [UpdateBefore(typeof(SpriteInstanceRenderSystem))]
    public partial struct SpritePartsCullingSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (!SystemAPI.TryGetSingleton(out SpriteCullSettings settings)) return;
            if (!SpriteBatchSpawner.LayoutXy) return; // Parts require XY layout

            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            float margin = math.max(0f, settings.MarginUnits);
            var em = state.EntityManager;

            foreach (var (ltw, depth, enabled, entity) in
                     SystemAPI.Query<RefRO<LocalToWorld>, RefRO<SpritePartRenderDepth>,
                         EnabledRefRW<SpriteAnimEnabled>>()
                              .WithAll<SpritePartSlot, SpriteAnimFrame>()
                              .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                              .WithEntityAccess())
            {
                float3 position = ltw.ValueRO.Value.c3.xyz;
                // Use rendered depth for bounds center z.
                position.z = depth.ValueRO.Value;
                float scale = math.length(ltw.ValueRO.Value.c0.xyz) + math.length(ltw.ValueRO.Value.c1.xyz);
                float radius = scale;
                if (em.HasComponent<SpriteAnimFrame>(entity))
                {
                    var frame = em.GetComponentData<SpriteAnimFrame>(entity);
                    radius *= math.max(1f, math.cmax(math.abs(frame.Scale))) + math.length(frame.Offset);
                }
                radius += margin;
                var bounds = new Bounds(position, new Vector3(radius * 2f, radius * 2f, radius * 2f));
                enabled.ValueRW = GeometryUtility.TestPlanesAABB(planes, bounds);
            }
        }
    }
}
