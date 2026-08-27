using Unity.Collections;
using Unity.Entities;
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
    /// Bulk spawning: clones the prototype with ONE native Instantiate call per
    /// batch, then places instances. No per-entity managed roundtrips.
    /// </summary>
    public static class SpriteBatchSpawner
    {
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
        /// otherwise uniform random scatter within ±spread on x/z.
        /// </summary>
        public static int SpawnNow(EntityManager em, float3 center, float spread,
                                   float scale, int count, bool grid,
                                   bool randomizeClocks = true)
        {
            if (!TryGetProto(em, out var proto)) return 0;
            count = math.max(0, count);
            if (count == 0) return 0;

            using var instances = em.Instantiate(proto, count, Allocator.Temp);
            var rng = new Unity.Mathematics.Random(0x9E3779B9u ^
                (uint)UnityEngine.Time.frameCount * 747796405u + 2891336453u);

            if (grid)
            {
                int side = (int)math.ceil(math.sqrt(count));
                float step = scale * 1.15f;
                for (int i = 0; i < instances.Length; i++)
                {
                    int gx = i % side, gz = i / side;
                    var p = new float3(
                        center.x - (side - 1) * step * 0.5f + gx * step,
                        center.y,
                        center.z - (side - 1) * step * 0.5f + gz * step);
                    Place(em, instances[i], p, scale, ref rng, randomizeClocks);
                }
            }
            else
            {
                for (int i = 0; i < instances.Length; i++)
                {
                    var p = new float3(
                        center.x + rng.NextFloat(-spread, spread),
                        center.y,
                        center.z + rng.NextFloat(-spread, spread));
                    Place(em, instances[i], p, scale, ref rng, randomizeClocks);
                }
            }
            return count;
        }

        static void Place(EntityManager em, Entity e, float3 pos, float scale,
                          ref Unity.Mathematics.Random rng, bool randomizeClocks)
        {
            var lt = em.GetComponentData<LocalTransform>(e);
            lt.Position = pos;
            lt.Scale = scale;
            em.SetComponentData(e, lt);

            if (randomizeClocks && em.HasComponent<SpriteAnimPlayer>(e))
            {
                var pl = em.GetComponentData<SpriteAnimPlayer>(e);
                pl.Time = rng.NextFloat(0f, 4f);
                pl.Playing = 1;
                em.SetComponentData(e, pl);
            }
            if (em.HasComponent<DisableRendering>(e))
                em.RemoveComponent<DisableRendering>(e);
        }
    }
}
