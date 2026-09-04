using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Authored 2D depth as one world-z value (XY layout). Smaller z = closer
    /// to the default 2D camera (sits at z −10, looking +z) = drawn on top.
    /// Baked by <see cref="SpriteSortAuthoring"/>; gameplay can override at
    /// runtime with <c>em.SetComponentData(e, new SpriteSortDepth { Value = z })</c>
    /// and <see cref="SpriteSortDepthSystem"/> re-applies it in timed batches.
    /// </summary>
    public struct SpriteSortDepth : IComponentData
    {
        /// <summary>World-z distance between two sort layers.</summary>
        public const float LayerStep = 1f;

        /// <summary>World-z distance between two orders inside one layer.</summary>
        public const float OrderStep = 0.001f;

        /// <summary>
        /// Smallest depth step the depth buffer can reliably resolve with a
        /// far plane around 1000–2000 (one bucket ≈ 0.00006–0.00012, so this
        /// keeps ~10× margin). Baked z is snapped to this grid; anything
        /// finer quantizes to zero.
        /// </summary>
        public const float MinDepthStep = 0.001f;

        public const float DefaultRefreshInterval = 0.3f;

        /// <summary>Final world z. Lower = on top with a default 2D camera.</summary>
        public float Value;

        /// <summary>How many order steps fill one layer (grid base for the flat index).</summary>
        public const int OrdersPerLayer = 1000;

        /// <summary>
        /// Layer/order/offset → world z used by the authoring baker. All three
        /// inputs are integers on one number line where 1 = 0.001 z and
        /// higher = on top: total milli = layer×1000 + order + offset.
        /// The result is quantized to <see cref="MinDepthStep"/> so no value
        /// can land below depth-buffer resolution (a no-op for integers).
        /// </summary>
        public static float FromLayerOrder(int layer, int orderInLayer, int depthOffsetMilli)
        {
            long milli = (long)layer * OrdersPerLayer + orderInLayer + depthOffsetMilli;
            float z = -milli * OrderStep;
            return math.round(z / MinDepthStep) * MinDepthStep;
        }

        /// <summary>
        /// Total depth index → world z, like indexing a 3D array where one
        /// unit = 0.001 z. Index 2,346,000-spanning values roll into layers
        /// automatically (2340 = layer 2, order 340). Negative indices go
        /// behind index 0 (−1 = one step back).
        /// </summary>
        public static float FromIndex(int index)
        {
            float z = -(long)index * OrderStep;
            return math.round(z / MinDepthStep) * MinDepthStep;
        }

        /// <summary>Total depth index from layer/order/offset (inverse of <see cref="DecomposeIndex"/>).</summary>
        public static int ToIndex(int layer, int orderInLayer, int depthOffsetMilli = 0)
            => (int)((long)layer * OrdersPerLayer + orderInLayer + depthOffsetMilli);

        /// <summary>Split a flat index back into layer + order. Pure integer
        /// floor division — exact for every int, no float edge cases.</summary>
        public static void DecomposeIndex(int index, out int layer, out int orderInLayer)
        {
            layer = index / OrdersPerLayer;
            if (index < 0 && index % OrdersPerLayer != 0)
                layer -= 1; // C# int division truncates toward zero; shift down for negatives
            orderInLayer = index - layer * OrdersPerLayer;
        }

        /// <summary>
        /// True while integer orders stay within half a layer of drift.
        /// Depth Offset is deliberately excluded: it is a world-unit control
        /// (1 = one full layer forward) and may hold any adopted scene z.
        /// </summary>
        public static bool StaysInsideLayer(int orderInLayer)
            => math.abs(orderInLayer * OrderStep) < LayerStep * 0.5f;
    }

    public struct SpriteSortSettings : IComponentData
    {
        public float RefreshInterval;
    }

    /// <summary>
    /// Tag: this sprite's depth is authored once and never changes. The
    /// periodic re-pin skips it entirely; after the one-shot startup pin the
    /// entity's z belongs to whoever writes it last (usually nobody).
    /// </summary>
    public struct SpriteSortStatic : IComponentData { }

    /// <summary>
    /// Tag: pin z once at startup (bake does not write LocalTransform.z for
    /// sort-authoring sprites). Added by the baker for every sort sprite;
    /// removed by the system on the first frame it is seen.
    /// </summary>
    public struct SpriteSortPinPending : IComponentData { }

    /// <summary>
    /// Pins LocalTransform.Position.z to SpriteSortDepth.Value so gameplay
    /// movement (xy) can never clobber render depth. Runs BEFORE the transform
    /// group (a child of SimulationSystemGroup), so the pinned z is composed
    /// into the fresh LocalToWorld the render systems pack from.
    /// Sprites tagged <see cref="SpriteSortStatic"/> are pinned once at
    /// startup and then excluded from the periodic refresh (zero per-tick
    /// cost). XY layout only; the system gate reads the managed layout flag,
    /// entity work runs as a Burst parallel job.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(Unity.Transforms.TransformSystemGroup))]
    public partial struct SpriteSortDepthSystem : ISystem
    {
        double _nextRefresh;

        public void OnUpdate(ref SystemState state)
        {
            if (!SpriteBatchSpawner.LayoutXy)
                return;

            // ---- one-shot pins (freshly baked / spawned sprites) ----
            var pending = SystemAPI.QueryBuilder()
                .WithAllRW<LocalTransform>()
                .WithAll<SpriteSortDepth, SpriteSortPinPending>()
                .Build();
            if (!pending.IsEmpty)
            {
                var entities = pending.ToEntityArray(Allocator.Temp);
                foreach (var entity in entities)
                {
                    var lt = state.EntityManager.GetComponentData<LocalTransform>(entity);
                    lt.Position.z = state.EntityManager.GetComponentData<SpriteSortDepth>(entity).Value;
                    state.EntityManager.SetComponentData(entity, lt);
                    state.EntityManager.RemoveComponent<SpriteSortPinPending>(entity);
                }
                entities.Dispose();
                if (state.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<SpriteGpuDriven>(),
                        ComponentType.ReadOnly<SpriteSortDepth>())
                    .CalculateEntityCount() > 0)
                {
                    SpriteGpuAnimResources.MarkDirty();
                }
            }

            float interval = SpriteSortDepth.DefaultRefreshInterval;
            if (SystemAPI.TryGetSingleton<SpriteSortSettings>(out var settings))
                interval = math.max(0.01f, settings.RefreshInterval);

            double now = SystemAPI.Time.ElapsedTime;
            if (now < _nextRefresh)
                return;

            _nextRefresh = now + interval;
            // periodic re-pin covers DYNAMIC sprites only; static ones were
            // pinned once by the pending pass above and are excluded here
            var refresh = SystemAPI.QueryBuilder()
                .WithAllRW<LocalTransform>()
                .WithAll<SpriteSortDepth>()
                .WithNone<SpriteSortStatic>()
                .Build();
            state.Dependency = new ApplySortJob().ScheduleParallel(refresh, state.Dependency);

            // GPU-driven sprites only re-upload packed instance data when
            // marked dirty; each timed sorting batch must reach the GPU
            // buffer. Static sprites never need it.
            if (SystemAPI.QueryBuilder()
                    .WithAll<SpriteGpuDriven, SpriteSortDepth>()
                    .WithNone<SpriteSortStatic>()
                    .Build().CalculateEntityCount() > 0)
            {
                SpriteGpuAnimResources.MarkDirty();
            }
        }

        [BurstCompile]
        partial struct ApplySortJob : IJobEntity
        {
            void Execute(ref LocalTransform transform, in SpriteSortDepth depth)
            {
                if (transform.Position.z != depth.Value)
                    transform.Position.z = depth.Value;
            }
        }
    }
}
