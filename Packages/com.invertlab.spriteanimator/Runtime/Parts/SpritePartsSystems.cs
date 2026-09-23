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
            using var commands = new EntityCommandBuffer(Allocator.Temp);
            using var pending = new NativeList<Entity>(16, Allocator.Temp);
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

                byte already = player.Completed;
                if (em.HasComponent<SpritePartsCompleted>(entity))
                    already = 1;
                player.Completed = already;
                SpritePartsPoseWriter.TickClocks(ref player, ref set, dt);
                if (player.Completed != 0 && !em.HasComponent<SpritePartsCompleted>(entity))
                    commands.AddComponent(entity, new SpritePartsCompleted());
                pending.Add(entity);
            }

            // Pose writes can add PostTransformMatrix. Do that after the query
            // enumerator is gone, and only through the ECB.
            for (int i = 0; i < pending.Length; i++)
            {
                var entity = pending[i];
                if (!em.HasComponent<SpritePartsSetRef>(entity) || !em.HasComponent<SpritePartsPlayer>(entity))
                    continue;
                SpritePartsPoseWriter.Apply(em, entity, commands, true);
            }
            commands.Playback(em);
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpritePartsPlayerSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct SpritePartsPoseDiagnosticsSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (diag, entity) in
                     SystemAPI.Query<RefRW<SpritePartsPoseDiagnostics>>()
                              .WithAll<SpritePartsPlayer>()
                              .WithEntityAccess())
            {
                byte flags = diag.ValueRO.Flags;
                byte logged = diag.ValueRO.LoggedFlags;
                byte fresh = (byte)(flags & ~logged);
                if (fresh == 0)
                    continue;
                if ((fresh & SpritePartsPoseDiagnostics.MissingVisualRoot) != 0)
                    Debug.LogError(
                        "[Parts] Missing Visual Root. Apply Runtime Mode / rebake the Parts character.",
                        null);
                if ((fresh & SpritePartsPoseDiagnostics.LinkMismatch) != 0)
                    Debug.LogError(
                        "[Parts] Slot link count does not match the baked rig.",
                        null);
                if ((fresh & SpritePartsPoseDiagnostics.GameplayWroteTransform) != 0)
                    Debug.LogError(
                        "[Parts] Gameplay wrote a part LocalTransform. Use SpriteParts.SetOverride instead.",
                        null);
                diag.ValueRW.LoggedFlags = (byte)(logged | fresh);
                _ = entity;
            }
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
                        {
                            rank = blob.Value.Slots[si].DrawRank;
                            // Clip keys: draw order (held) and colour (tint / fade) on the current clip.
                            if (em.HasComponent<SpritePartsPlayer>(root))
                            {
                                var player = em.GetComponentData<SpritePartsPlayer>(root);
                                int keyed = SpritePartsSampler.SampleDrawOrder(ref blob.Value, player.ClipIndex, si, player.TimeSeconds);
                                if (keyed >= 0)
                                    rank = keyed;
                                if (em.HasComponent<SpritePartKeyedTint>(entity))
                                {
                                    SpritePartsSampler.SampleColor(ref blob.Value, player.ClipIndex, si, player.TimeSeconds, out var color);
                                    if (player.PreviousClipIndex >= 0 && player.BlendDuration > 0f
                                        && player.BlendElapsed < player.BlendDuration)
                                    {
                                        SpritePartsSampler.SampleColor(ref blob.Value, player.PreviousClipIndex, si,
                                            player.PreviousTimeSeconds, out var previous);
                                        color = math.lerp(previous, color, math.saturate(player.BlendElapsed / player.BlendDuration));
                                    }
                                    em.SetComponentData(entity, new SpritePartKeyedTint { Value = color });
                                }
                            }
                        }
                    }
                }
                int drawIndex = SpritePartsPlayback.DrawIndex(order, rank);
                depth.ValueRW.Value = SpriteSortDepth.FromIndex(drawIndex);

                // Keep joint local z at 0 (no SpriteSortDepth accumulation).
                if (!em.HasComponent<SpritePartPhysicsOwned>(entity) && em.HasComponent<LocalTransform>(entity))
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

            foreach (var (ltw, depth, slot, enabled, entity) in
                     SystemAPI.Query<RefRO<LocalToWorld>, RefRO<SpritePartRenderDepth>,
                         RefRO<SpritePartSlot>, EnabledRefRW<SpriteAnimEnabled>>()
                              .WithAll<SpriteAnimFrame>()
                              .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                              .WithEntityAccess())
            {
                if (slot.ValueRO.Hidden != 0)
                {
                    enabled.ValueRW = false;
                    continue;
                }
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
