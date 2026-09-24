using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>A character leaving afterimages (fast attacks, dashes): a ghost of every visible part every Interval.</summary>
    public struct SpritePartsTrail : IComponentData
    {
        public float Interval;
        public float Life;
        public float4 Tint;
        public float Timer;
        /// <summary>Seconds left (below 0 = until StopTrail).</summary>
        public float Remaining;
    }

    /// <summary>One afterimage sprite: frozen where its part was, fading out over Life.</summary>
    public struct SpritePartsGhost : IComponentData
    {
        public float Life;
        public float Age;
        public float4 Tint;
    }

    public static partial class SpriteParts
    {
        /// <summary>
        /// Leaves afterimages: every <paramref name="interval"/> seconds each visible part leaves a ghost tinted
        /// <paramref name="tint"/> that fades over <paramref name="life"/> seconds, behind the character. Runs for
        /// <paramref name="seconds"/> (negative = until <see cref="StopTrail"/>).
        /// </summary>
        public static bool StartTrail(EntityManager em, Entity root, float interval = 0.04f, float life = 0.25f,
            float4 tint = default, float seconds = -1f)
        {
            if (!IsPartsRoot(em, root))
                return false;
            var trail = new SpritePartsTrail
            {
                Interval = math.max(0.01f, interval),
                Life = math.max(0.01f, life),
                Tint = math.all(tint == float4.zero) ? new float4(0.6f, 0.8f, 1f, 0.5f) : tint,
                Timer = 0f,
                Remaining = seconds,
            };
            if (em.HasComponent<SpritePartsTrail>(root))
                em.SetComponentData(root, trail);
            else
                em.AddComponentData(root, trail);
            return true;
        }

        public static void StopTrail(EntityManager em, Entity root)
        {
            if (em.Exists(root) && em.HasComponent<SpritePartsTrail>(root))
                em.RemoveComponent<SpritePartsTrail>(root);
        }

        /// <summary>One afterimage of the character as it is drawn now (after transforms). Returns how many ghosts.</summary>
        public static int SpawnAfterimage(EntityManager em, Entity root, float life, float4 tint)
        {
            if (!IsPartsRoot(em, root) || !em.HasBuffer<SpritePartLink>(root))
                return 0;
            var links = em.GetBuffer<SpritePartLink>(root).ToNativeArray(Allocator.Temp);
            // Behind the whole character: one character order further back.
            float behind = SpriteSortDepth.FromIndex(0) - SpriteSortDepth.FromIndex(64);
            int made = 0;
            try
            {
                for (int i = 0; i < links.Length; i++)
                {
                    var part = links[i].Part;
                    if (part == Entity.Null || !em.Exists(part) || !em.HasComponent<LocalToWorld>(part)
                        || !em.HasComponent<SpriteAnimFrame>(part) || !em.HasComponent<SpriteAnimEnabled>(part)
                        || !em.IsComponentEnabled<SpriteAnimEnabled>(part))
                        continue;
                    if (em.HasComponent<SpritePartSlot>(part) && em.GetComponentData<SpritePartSlot>(part).Hidden != 0)
                        continue;
                    float4x4 world = em.GetComponentData<LocalToWorld>(part).Value;
                    var ghost = em.CreateEntity();
                    em.AddComponentData(ghost, LocalTransform.Identity);
                    em.AddComponentData(ghost, new PostTransformMatrix { Value = world });
                    em.AddComponentData(ghost, new LocalToWorld { Value = world });
                    em.AddComponentData(ghost, em.GetComponentData<SpriteAnimFrame>(part));
                    em.AddComponentData(ghost, new SpriteTint { Value = tint });
                    em.AddComponentData(ghost, em.HasComponent<SpriteFlip>(part) ? em.GetComponentData<SpriteFlip>(part) : new SpriteFlip { Pivot = new float2(0.5f, 0.5f) });
                    em.AddComponentData(ghost, new SpriteAnimEnabled());
                    if (em.HasComponent<SpriteSheetBinding>(part))
                        em.AddComponentData(ghost, em.GetComponentData<SpriteSheetBinding>(part));
                    if (em.HasComponent<SpritePartKeyedTint>(part))
                        em.AddComponentData(ghost, em.GetComponentData<SpritePartKeyedTint>(part));
                    if (em.HasComponent<SpritePartLattice>(part))
                        em.AddComponentData(ghost, em.GetComponentData<SpritePartLattice>(part));
                    float depth = em.HasComponent<SpritePartRenderDepth>(part) ? em.GetComponentData<SpritePartRenderDepth>(part).Value : 0f;
                    em.AddComponentData(ghost, new SpritePartRenderDepth { Value = depth + behind });
                    em.AddComponentData(ghost, new SpritePartsGhost { Life = math.max(0.01f, life), Tint = tint });
                    made++;
                }
            }
            finally
            {
                links.Dispose();
            }
            return made;
        }
    }

    /// <summary>Spawns trail afterimages (after transforms, so parts are where they are drawn) and fades them out.</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpritePartsRenderDepthSystem))]
    [UpdateBefore(typeof(SpriteInstanceRenderSystem))]
    public partial struct SpritePartsTrailSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;
            using var commands = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (ghost, tint, entity) in SystemAPI.Query<RefRW<SpritePartsGhost>, RefRW<SpriteTint>>().WithEntityAccess())
            {
                ghost.ValueRW.Age += dt;
                float left = 1f - math.saturate(ghost.ValueRO.Age / ghost.ValueRO.Life);
                if (left <= 0f)
                    commands.DestroyEntity(entity);
                else
                    tint.ValueRW.Value = new float4(ghost.ValueRO.Tint.xyz, ghost.ValueRO.Tint.w * left);
            }
            using var due = new NativeList<Entity>(4, Allocator.Temp);
            using var dueTrails = new NativeList<SpritePartsTrail>(4, Allocator.Temp);
            foreach (var (trail, entity) in SystemAPI.Query<RefRW<SpritePartsTrail>>().WithEntityAccess())
            {
                ref var t = ref trail.ValueRW;
                if (t.Remaining >= 0f)
                {
                    t.Remaining -= dt;
                    if (t.Remaining < 0f)
                    {
                        commands.RemoveComponent<SpritePartsTrail>(entity);
                        continue;
                    }
                }
                t.Timer -= dt;
                if (t.Timer > 0f)
                    continue;
                t.Timer += t.Interval;
                if (t.Timer < 0f)
                    t.Timer = t.Interval;
                due.Add(entity);
                dueTrails.Add(t);
            }
            commands.Playback(em);
            for (int i = 0; i < due.Length; i++)
                if (em.Exists(due[i]))
                    SpriteParts.SpawnAfterimage(em, due[i], dueTrails[i].Life, dueTrails[i].Tint);
        }
    }
}
