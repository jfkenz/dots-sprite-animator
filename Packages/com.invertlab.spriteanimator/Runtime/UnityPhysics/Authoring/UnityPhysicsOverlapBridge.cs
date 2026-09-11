using System;
using System.Collections.Generic;
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
        // Legacy static API targets only the default world. Other worlds use
        // their own bridge instance and Hit event.
        static UnityPhysicsOverlapBridge DefaultBridge
        {
            get
            {
                var world = World.DefaultGameObjectInjectionWorld;
                return world != null && world.IsCreated
                    ? world.GetExistingSystemManaged<UnityPhysicsOverlapBridge>() : null;
            }
        }

        public static bool Ready => DefaultBridge?.IsReady ?? false;
        public static PhysicsWorldSingleton CachedWorld => DefaultBridge?._cachedWorld ?? default;
        public static int BodyCount => Ready ? CachedWorld.PhysicsWorld.NumBodies : 0;

        readonly Queue<(Aabb Bounds, int AttackId, int Damage)> _pending = new();
        PhysicsWorldSingleton _cachedWorld;
        public bool IsReady { get; private set; }
        public int PendingCount => _pending.Count;

        /// <summary>Fired only for requests queued on this world.</summary>
        public event Action<Entity, int, int> Hit;

        /// <summary>Fired per entity inside the slash box (system update).</summary>
        public static event System.Action<Entity, int, int> EntityHit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSubscriptions() => EntityHit = null;

        protected override void OnDestroy()
        {
            _pending.Clear();
            _cachedWorld = default;
            IsReady = false;
            Hit = null;
            if (World == World.DefaultGameObjectInjectionWorld)
                EntityHit = null;
        }

        protected override void OnUpdate()
        {
            IsReady = SystemAPI.TryGetSingleton(out PhysicsWorldSingleton world);
            _cachedWorld = IsReady ? world : default;
            if (!IsReady || _pending.Count == 0)
                return;

            var hits = new NativeList<int>(8, Allocator.Temp);
            try
            {
                // Requests enqueued by a hit callback belong to the next update.
                int count = _pending.Count;
                for (int requestIndex = 0; requestIndex < count; requestIndex++)
                {
                    var request = _pending.Dequeue();
                    hits.Clear();
                    var input = new OverlapAabbInput
                    {
                        Aabb = request.Bounds,
                        Filter = CollisionFilter.Default,
                    };
                    world.OverlapAabb(input, ref hits);
                    for (int i = 0; i < hits.Length; i++)
                    {
                        var entity = world.PhysicsWorld.Bodies[hits[i]].Entity;
                        Hit?.Invoke(entity, request.AttackId, request.Damage);
                        if (World == World.DefaultGameObjectInjectionWorld)
                            EntityHit?.Invoke(entity, request.AttackId, request.Damage);
                    }
                }
            }
            finally { hits.Dispose(); }
        }

        /// <summary>Queue an overlap in the default world. Call on the main thread.</summary>
        public static void Queue(Aabb aabb, int attackId, int damage)
            => Queue(World.DefaultGameObjectInjectionWorld, aabb, attackId, damage);

        /// <summary>Queue an overlap in an explicit world. Call on the main thread.</summary>
        public static void Queue(World world, Aabb aabb, int attackId, int damage)
        {
            if (world == null || !world.IsCreated)
                throw new InvalidOperationException("An overlap request requires a live ECS world.");
            world.GetOrCreateSystemManaged<UnityPhysicsOverlapBridge>().Enqueue(aabb, attackId, damage);
        }

        /// <summary>FIFO request, processed when this world's physics singleton is ready.</summary>
        public void Enqueue(Aabb aabb, int attackId, int damage)
        {
            _pending.Enqueue((aabb, attackId, damage));
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
