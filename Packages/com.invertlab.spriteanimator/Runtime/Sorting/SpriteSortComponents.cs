using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>2D-style sorting layer (higher = drawn on top).</summary>
    public struct SpriteSortLayer : IComponentData
    {
        public short Layer;
    }

    /// <summary>Order within a layer (higher = on top).</summary>
    public struct SpriteSortOrder : IComponentData
    {
        public short Order;
    }

    /// <summary>
    /// Maps SpriteSortLayer/Order to a world z offset so top-down cameras
    /// composite sprites the way URP 2D sorting would. Layer step 10 units,
    /// order step 0.001 units.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(InvertLab.Sprites.DOTS.SpriteAnimPlayerSystem))]
    public partial struct SpriteSortToZSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (lt, layer, order) in
                     SystemAPI.Query<RefRW<LocalTransform>,
                                     RefRO<SpriteSortLayer>,
                                     RefRO<SpriteSortOrder>>())
            {
                var p = lt.ValueRO.Position;
                p.z = layer.ValueRO.Layer * 10f + order.ValueRO.Order * 0.001f;
                lt.ValueRW.Position = p;
            }
        }
    }
}
