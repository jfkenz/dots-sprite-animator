using System;
using Unity.Entities;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Managed bridge for animation events. Pure ECS consumers should query
    /// SpriteAnimEventBuffer with SpriteAnimEventsPending instead.
    /// </summary>
    public static class SpriteAnimEvents
    {
        public static event Action<Entity, SpriteAnimEventBuffer> Raised;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Raised = null;

        internal static void Raise(Entity entity, SpriteAnimEventBuffer animationEvent)
            => Raised?.Invoke(entity, animationEvent);

        /// <summary>Install event storage on an entity created without an authoring baker.</summary>
        public static void Ensure(EntityManager entityManager, Entity entity)
        {
            if (!entityManager.HasBuffer<SpriteAnimEventBuffer>(entity))
                entityManager.AddBuffer<SpriteAnimEventBuffer>(entity);
            if (!entityManager.HasComponent<SpriteAnimEventsPending>(entity))
            {
                entityManager.AddComponent<SpriteAnimEventsPending>(entity);
                entityManager.SetComponentEnabled<SpriteAnimEventsPending>(entity, false);
            }
        }
    }

    /// <summary>Adds event storage to legacy/programmatically-created animators once.</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct SpriteAnimEventBootstrapSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            // Event storage is OPT-IN: a dynamic buffer + enableable tag on
            // every crowd entity costs real per-frame churn (chunk dirtying,
            // clear-system iterations). Entities that need animation events
            // get storage from their baker/constructor or via Play()'s
            // Ensure(); everyone else stays lean.
            foreach (var (_, entity) in
                     SystemAPI.Query<RefRO<SpriteAnimPlayer>>()
                              .WithNone<SpriteAnimEventBuffer>()
                              .WithEntityAccess())
            {
                ecb.AddBuffer<SpriteAnimEventBuffer>(entity);
            }
            foreach (var (_, entity) in
                     SystemAPI.Query<RefRO<SpriteAnimPlayer>>()
                              .WithNone<SpriteAnimEventsPending>()
                              .WithEntityAccess())
            {
                ecb.AddComponent<SpriteAnimEventsPending>(entity);
                ecb.SetComponentEnabled<SpriteAnimEventsPending>(entity, false);
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }

    /// <summary>Clears last tick's events before new animation events are emitted.</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpriteAnimEventBootstrapSystem))]
    [UpdateBefore(typeof(SpriteAnimPlayerSystem))]
    public partial struct SpriteAnimEventClearSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var pending = SystemAPI.GetComponentLookup<SpriteAnimEventsPending>();
            foreach (var (events, entity) in
                     SystemAPI.Query<DynamicBuffer<SpriteAnimEventBuffer>>()
                              .WithAll<SpriteAnimEventsPending>()
                              .WithEntityAccess())
            {
                events.Clear();
                pending.SetComponentEnabled(entity, false);
            }
        }
    }

    /// <summary>Forwards pending DOTS events to managed subscribers after playback.</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpriteAnimPlayerSystem))]
    public partial class SpriteAnimEventDispatchSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            foreach (var (events, entity) in
                     SystemAPI.Query<DynamicBuffer<SpriteAnimEventBuffer>>()
                              .WithAll<SpriteAnimEventsPending>()
                              .WithEntityAccess())
            {
                for (int i = 0; i < events.Length; i++)
                    SpriteAnimEvents.Raise(entity, events[i]);
            }
        }
    }
}
