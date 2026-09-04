using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Points the bulk spawner at the entity to clone.</summary>
    public struct SpriteSpawnPrototype : IComponentData
    {
        public Entity Value;
    }

    /// <summary>
    /// Bulk spawning: one native Instantiate, then a Burst job writes
    /// LocalTransform / clocks. No per-entity managed roundtrips.
    /// </summary>
    public static class SpriteBatchSpawner
    {
        /// <summary>
        /// True (default) = flat quads on XY facing a 2D camera; depth = world z
        /// (what SpriteSortDepth writes). False = XZ billboard mode (soldier
        /// top-down); depth rides on gameplay-owned y and sort authoring is
        /// ignored. Set from bootstrap code when needed.
        /// </summary>
        public static bool LayoutXy = true;

        /// <summary>Point the spawner at a prototype entity (call once).</summary>
        public static void SetPrototype(EntityManager em, Entity proto)
        {
            var q = em.CreateEntityQuery(typeof(SpriteSpawnPrototype));
            if (q.CalculateEntityCount() > 0)
                em.SetComponentData(q.GetSingletonEntity(), new SpriteSpawnPrototype { Value = proto });
            else
            {
                var e = em.CreateEntity();
                em.AddComponentData(e, new SpriteSpawnPrototype { Value = proto });
            }
        }

        static bool TryGetProto(EntityManager em, out Entity proto)
        {
            proto = default;
            var q = em.CreateEntityQuery(typeof(SpriteSpawnPrototype));
            if (q.CalculateEntityCount() == 0) return false;
            var value = em.GetComponentData<SpriteSpawnPrototype>(q.GetSingletonEntity()).Value;
            if (value == Entity.Null) return false;
            proto = value;
            return true;
        }

        /// <summary>
        /// Immediate bulk spawn. grid=true square formation around center,
        /// otherwise uniform random scatter within ±spread.
        /// </summary>
        public static int SpawnNow(EntityManager em, float3 center, float spread,
                                   float scale, int count, bool grid,
                                   bool randomizeClocks = true)
        {
            if (!TryGetProto(em, out var proto)) return 0;
            count = math.max(0, count);
            if (count == 0) return 0;

            using var instances = em.Instantiate(proto, count, Allocator.TempJob);
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return 0;

            var driver = world.GetExistingSystemManaged<SpriteBatchPlaceDriver>()
                         ?? world.GetOrCreateSystemManaged<SpriteBatchPlaceDriver>();
            driver.Place(instances, center, spread, scale, grid, randomizeClocks, LayoutXy,
                Time.unscaledTime);

            if (em.HasComponent<DisableRendering>(proto))
            {
                for (int i = 0; i < instances.Length; i++)
                {
                    if (em.HasComponent<DisableRendering>(instances[i]))
                        em.RemoveComponent<DisableRendering>(instances[i]);
                }
            }

            SpriteGpuAnimResources.MarkDirty();
            return count;
        }
    }

    /// <summary>Burst-places a just-instantiated batch. Invoked from SpawnNow.</summary>
    public partial class SpriteBatchPlaceDriver : SystemBase
    {
        protected override void OnUpdate() { }

        public void Place(NativeArray<Entity> instances, float3 center, float spread,
                          float scale, bool grid, bool randomizeClocks, bool layoutXy,
                          float now)
        {
            int count = instances.Length;
            if (count == 0)
                return;

            int side = (int)math.ceil(math.sqrt(count));
            var job = new PlaceJob
            {
                Entities = instances,
                Transforms = GetComponentLookup<LocalTransform>(false),
                Players = GetComponentLookup<SpriteAnimPlayer>(false),
                GpuAnims = GetComponentLookup<SpriteGpuAnim>(false),
                Center = center,
                Spread = spread,
                Scale = scale,
                Side = math.max(1, side),
                Step = scale * 1.15f,
                Grid = (byte)(grid ? 1 : 0),
                LayoutXy = (byte)(layoutXy ? 1 : 0),
                RandomizeClocks = (byte)(randomizeClocks ? 1 : 0),
                Seed = 0x9E3779B9u ^ (uint)UnityEngine.Time.frameCount * 747796405u + 2891336453u,
                Now = now,
            };
            job.Schedule(count, 64).Complete();
        }

        [BurstCompile]
        struct PlaceJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> Transforms;
            [NativeDisableParallelForRestriction] public ComponentLookup<SpriteAnimPlayer> Players;
            [NativeDisableParallelForRestriction] public ComponentLookup<SpriteGpuAnim> GpuAnims;
            public float3 Center;
            public float Spread;
            public float Scale;
            public int Side;
            public float Step;
            public byte Grid;
            public byte LayoutXy;
            public byte RandomizeClocks;
            public uint Seed;
            public float Now;

            public void Execute(int i)
            {
                var rng = Unity.Mathematics.Random.CreateFromIndex(Seed + (uint)i);
                float3 p;
                if (Grid != 0)
                {
                    int gx = i % Side;
                    int gz = i / Side;
                    float a = (Side - 1) * Step * 0.5f;
                    p = LayoutXy != 0
                        ? new float3(Center.x - a + gx * Step, Center.y - a + gz * Step, Center.z)
                        : new float3(Center.x - a + gx * Step, Center.y, Center.z - a + gz * Step);
                }
                else
                {
                    p = LayoutXy != 0
                        ? new float3(
                            Center.x + rng.NextFloat(-Spread, Spread),
                            Center.y + rng.NextFloat(-Spread, Spread),
                            Center.z)
                        : new float3(
                            Center.x + rng.NextFloat(-Spread, Spread),
                            Center.y,
                            Center.z + rng.NextFloat(-Spread, Spread));
                }

                var e = Entities[i];
                if (!Transforms.HasComponent(e))
                    return;
                var lt = Transforms[e];
                lt.Position = p;
                lt.Scale = Scale;
                Transforms[e] = lt;

                if (RandomizeClocks == 0)
                    return;

                if (Players.HasComponent(e))
                {
                    var pl = Players[e];
                    pl.Time = rng.NextFloat(0f, 4f);
                    pl.Playing = 1;
                    Players[e] = pl;
                }

                if (GpuAnims.HasComponent(e))
                {
                    var g = GpuAnims[e];
                    if (g.Rate > 0f)
                        g.StartTime = Now - rng.NextFloat(0f, 4f);
                    GpuAnims[e] = g;
                }
            }
        }
    }
}
