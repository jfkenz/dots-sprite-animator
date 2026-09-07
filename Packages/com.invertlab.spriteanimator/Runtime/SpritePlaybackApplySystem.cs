using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Applies SpritePlaybackPreference once after an entity appears.
    /// PreferGpu converts to the GPU clock when the clip is eligible; ForceCpu
    /// restores CPU playback; Auto leaves the CPU default.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct SpritePlaybackApplySystem : ISystem
    {
        EntityQuery _pending;

        public void OnCreate(ref SystemState state)
        {
            _pending = SystemAPI.QueryBuilder()
                .WithAll<SpritePlaybackPreference>()
                .WithNone<SpritePlaybackApplied>()
                .Build();
            state.RequireForUpdate(_pending);
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float now = Time.unscaledTime;
            var entities = _pending.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                var path = em.GetComponentData<SpritePlaybackPreference>(e).Path;
                if (path == SpritePlaybackPath.PreferGpu)
                    SpriteGpuAnimSwitch.ToGpu(em, e, now);
                else if (path == SpritePlaybackPath.ForceCpu)
                    SpriteGpuAnimSwitch.ToCpu(em, e);

                if (!em.HasComponent<SpritePlaybackApplied>(e))
                    em.AddComponent<SpritePlaybackApplied>(e);
            }
            entities.Dispose();
        }
    }
}
