using Unity.Burst;
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
        public const float OrderStep = 0.00001f;

        public const float DefaultRefreshInterval = 0.3f;

        /// <summary>Final world z. Lower = on top with a default 2D camera.</summary>
        public float Value;

        /// <summary>
        /// Layer/order/offset → world z used by the authoring baker.
        /// All three inputs follow one rule: higher = on top.
        /// </summary>
        public static float FromLayerOrder(int layer, int orderInLayer, float depthOffset)
            => -depthOffset - layer * LayerStep - orderInLayer * OrderStep;

        public static bool StaysInsideLayer(int orderInLayer, float depthOffset)
            => math.abs(-depthOffset - orderInLayer * OrderStep) < LayerStep * 0.5f;
    }

    public struct SpriteSortSettings : IComponentData
    {
        public float RefreshInterval;
    }

    /// <summary>
    /// Pins LocalTransform.Position.z to SpriteSortDepth.Value so gameplay
    /// movement (xy) can never clobber render depth. Runs last in
    /// SimulationSystemGroup, right before the sprite render systems, which
    /// pack LocalTransform.Position directly into instance data.
    /// XY layout only — in XZ layout z is a ground-plane axis, depth rides on
    /// gameplay-owned y, and this system does nothing.
    /// The system gate reads the managed layout flag; entity work runs as a
    /// Burst parallel job.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateBefore(typeof(SpriteInstanceRenderSystem))]
    [UpdateBefore(typeof(SpriteGpuAnimRenderSystem))]
    public partial struct SpriteSortDepthSystem : ISystem
    {
        double _nextRefresh;

        public void OnUpdate(ref SystemState state)
        {
            if (!SpriteBatchSpawner.LayoutXy)
                return;

            float interval = SpriteSortDepth.DefaultRefreshInterval;
            if (SystemAPI.TryGetSingleton<SpriteSortSettings>(out var settings))
                interval = math.max(0.01f, settings.RefreshInterval);

            double now = SystemAPI.Time.ElapsedTime;
            if (now < _nextRefresh)
                return;

            _nextRefresh = now + interval;
            state.Dependency = new ApplySortJob().ScheduleParallel(state.Dependency);

            // GPU-driven sprites only re-upload packed instance data when
            // marked dirty; each timed sorting batch must reach the GPU buffer.
            if (SystemAPI.QueryBuilder()
                    .WithAll<SpriteGpuDriven, SpriteSortDepth>()
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
