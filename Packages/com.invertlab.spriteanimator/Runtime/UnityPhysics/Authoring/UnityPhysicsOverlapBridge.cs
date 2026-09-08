using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Caches the physics world singleton and executes overlap requests from
    /// MonoBehaviour gameplay INSIDE the system update — against the live
    /// physics world, never a stale cached copy (the world is rebuilt every
    /// frame by the physics systems, so querying it from the editor update
    /// loop can miss).
    /// </summary>
    /// <summary>
    /// Runs in Unity Physics' AfterPhysicsSystemGroup (inside FixedStep),
    /// after ExportPhysicsWorld, so OverlapAabb sees the rebuilt world.
    /// </summary>
    [UpdateInGroup(typeof(Unity.Physics.Systems.AfterPhysicsSystemGroup))]
    public sealed partial class UnityPhysicsOverlapBridge : SystemBase
    {
        public static bool Ready;
        public static PhysicsWorldSingleton CachedWorld;
        public static int BodyCount;

        static bool _hasPending;
        static Aabb _pendingAabb;
        static int _pendingAttackId;
        static int _pendingDamage;

        /// <summary>Fired per entity inside the slash box (system update).</summary>
        public static event System.Action<Entity, int, int> EntityHit;

        protected override void OnUpdate()
        {
            if (SystemAPI.TryGetSingleton(out PhysicsWorldSingleton world))
            {
                CachedWorld = world;
                Ready = true;
                BodyCount = world.PhysicsWorld.NumBodies;
            }

            if (!Ready || !_hasPending)
                return;
            _hasPending = false;

            var hits = new NativeList<int>(8, Allocator.Temp);
            var input = new OverlapAabbInput
            {
                Aabb = _pendingAabb,
                Filter = CollisionFilter.Default,
            };
            CachedWorld.OverlapAabb(input, ref hits);

            for (int i = 0; i < hits.Length; i++)
            {
                var entity = CachedWorld.PhysicsWorld.Bodies[hits[i]].Entity;
                EntityHit?.Invoke(entity, _pendingAttackId, _pendingDamage);
            }
            hits.Dispose();
        }

        /// <summary>Queue an overlap (executed in the next system update).</summary>
        public static void Queue(Aabb aabb, int attackId, int damage)
        {
            _pendingAabb = aabb;
            _pendingAttackId = attackId;
            _pendingDamage = damage;
            _hasPending = true;
        }

        /// <summary>
        /// Editor/test helper: overlap against the last cached world. Call
        /// from the main thread after the systems tick (valid until the next
        /// physics rebuild). Returns the hit rigid-body count; entities are
        /// appended to <paramref name="results"/>.
        /// </summary>
        public static int Overlap(Aabb aabb, NativeList<Entity> results)
        {
            if (!Ready)
                return 0;
            var input = new OverlapAabbInput
            {
                Aabb = aabb,
                Filter = CollisionFilter.Default,
            };
            var indices = new NativeList<int>(8, Allocator.Temp);
            CachedWorld.OverlapAabb(input, ref indices);
            for (int i = 0; i < indices.Length; i++)
                results.Add(CachedWorld.PhysicsWorld.Bodies[indices[i]].Entity);
            int count = indices.Length;
            indices.Dispose();
            return count;
        }
    }
}
