using Unity.Entities;
using Unity.Rendering;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Editor Scene Quad MeshRenderer must not become an Entities Graphics draw.
    /// Preview uses a DontSave mesh/material that either fails to serialize (null
    /// refs in SubScenes) or is SRP-incompatible if left outside UnityPerMaterial.
    /// SpriteAnimSetAuthoring already adds DisableRendering; this strip removes
    /// MaterialMeshInfo so section load cannot NRE on a missing mesh/material.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(PostBakingSystemGroup))]
    partial struct SpriteAnimSetPreviewRenderStripBakingSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<SpriteAnimSetRef>(),
                ComponentType.ReadOnly<MaterialMeshInfo>());
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (em.HasComponent<MaterialMeshInfo>(entity))
                    em.RemoveComponent<MaterialMeshInfo>(entity);
            }
        }
    }
}
