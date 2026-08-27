using Unity.Collections;
using Unity.Entities;

namespace BallForge.Sprites.DOTS
{
    /// <summary>Adds default SpriteFlip data to legacy entities once.</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct SpriteFlipBootstrapSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            using var entities = new NativeList<Entity>(Allocator.Temp);
            foreach (var (frame, entity) in SystemAPI.Query<RefRO<SpriteAnimFrame>>()
                         .WithNone<SpriteFlip>()
                         .WithEntityAccess())
                entities.Add(entity);

            if (entities.Length == 0)
                return;

            var em = state.EntityManager;
            for (int i = 0; i < entities.Length; i++)
                em.AddComponentData(entities[i], new SpriteFlip());
        }
    }
}
