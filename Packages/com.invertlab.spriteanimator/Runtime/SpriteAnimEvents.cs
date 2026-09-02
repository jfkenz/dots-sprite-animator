using System;
using Unity.Entities;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Reserved <see cref="SpriteAnimEventBuffer.Id"/> values for clip lifecycle.
    /// User-authored frame events should stay below these (1–249).
    /// </summary>
    public static class SpriteAnimLifecycleId
    {
        public const byte Start = 250;
        public const byte Complete = 251;
    }

    /// <summary>
    /// Managed bridge for animation events. Pure ECS consumers should query
    /// SpriteAnimEventBuffer with SpriteAnimEventsPending instead.
    /// </summary>
    public static class SpriteAnimEvents
    {
        public static event Action<Entity, SpriteAnimEventBuffer> Raised;

        /// <summary>Fired when a clip successfully begins (Play / chained completion start).</summary>
        public static event Action<Entity, int> ClipStarted;

        /// <summary>Fired when a Once clip finishes (forward end or reverse-to-start). Not on Loop wraps.</summary>
        public static event Action<Entity, int> ClipCompleted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Raised = null;
            ClipStarted = null;
            ClipCompleted = null;
        }

        internal static void Raise(Entity entity, SpriteAnimEventBuffer animationEvent)
            => Raised?.Invoke(entity, animationEvent);

        internal static void RaiseClipStarted(Entity entity, int clipIndex)
            => ClipStarted?.Invoke(entity, clipIndex);

        internal static void RaiseClipCompleted(Entity entity, int clipIndex)
            => ClipCompleted?.Invoke(entity, clipIndex);

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

    public static class SpriteSocketEvents
    {
        public static event Action<Entity, SpriteSocketEventBuffer> Raised;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Raised = null;

        internal static void Raise(Entity entity, SpriteSocketEventBuffer socketEvent)
            => Raised?.Invoke(entity, socketEvent);

        public static void Ensure(EntityManager entityManager, Entity entity)
        {
            if (!entityManager.HasBuffer<SpriteSocketEventBuffer>(entity))
                entityManager.AddBuffer<SpriteSocketEventBuffer>(entity);
            if (!entityManager.HasComponent<SpriteSocketEventsPending>(entity))
            {
                entityManager.AddComponent<SpriteSocketEventsPending>(entity);
                entityManager.SetComponentEnabled<SpriteSocketEventsPending>(entity, false);
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
                {
                    var ev = events[i];
                    SpriteAnimEvents.Raise(entity, ev);
                    if (ev.Id == SpriteAnimLifecycleId.Start)
                        SpriteAnimEvents.RaiseClipStarted(entity, ev.ClipIndex);
                    else if (ev.Id == SpriteAnimLifecycleId.Complete)
                        SpriteAnimEvents.RaiseClipCompleted(entity, ev.ClipIndex);
                }
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpriteAnimEventClearSystem))]
    [UpdateBefore(typeof(SpriteSocketMotionSystem))]
    public partial struct SpriteSocketEventClearSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var pending = SystemAPI.GetComponentLookup<SpriteSocketEventsPending>();
            foreach (var (events, entity) in
                     SystemAPI.Query<DynamicBuffer<SpriteSocketEventBuffer>>()
                         .WithAll<SpriteSocketEventsPending>()
                         .WithEntityAccess())
            {
                events.Clear();
                pending.SetComponentEnabled(entity, false);
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpriteSocketMotionSystem))]
    public partial class SpriteSocketEventDispatchSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            foreach (var (events, entity) in
                     SystemAPI.Query<DynamicBuffer<SpriteSocketEventBuffer>>()
                         .WithAll<SpriteSocketEventsPending>()
                         .WithEntityAccess())
            {
                for (int i = 0; i < events.Length; i++)
                    SpriteSocketEvents.Raise(entity, events[i]);
            }
        }
    }
}
