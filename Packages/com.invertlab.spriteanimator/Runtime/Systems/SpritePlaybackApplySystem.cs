using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Applies SpritePlaybackPreference after spawn.
    /// PreferGpu uses SpriteGpuAnimSwitch.ToGpu (which promotes a bound sheet
    /// onto the legacy GPU SetSheet path when eligible).
    /// ForceCpu restores CPU playback; Auto leaves the CPU default.
    /// Marks SpritePlaybackApplied only when finished or permanently impossible.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(SpriteAnimPlayerSystem))]
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
                if (TryFinish(em, e, path, now) && !em.HasComponent<SpritePlaybackApplied>(e))
                    em.AddComponent<SpritePlaybackApplied>(e);
            }
            entities.Dispose();
        }

        static bool TryFinish(EntityManager em, Entity e, SpritePlaybackPath path, float now)
        {
            if (path == SpritePlaybackPath.Auto)
                return true;

            if (path == SpritePlaybackPath.ForceCpu)
            {
                SpriteGpuAnimSwitch.ToCpu(em, e);
                return true;
            }

            // PreferGpu
            if (em.HasComponent<SpriteGpuDriven>(e))
                return true;

            if (!em.HasComponent<SpriteAnimPlayer>(e) || !em.HasComponent<SpriteAnimSetRef>(e))
                return false; // not baked yet — retry

            // ToGpu promotes SpriteSheetBinding when possible. If promote fails
            // (crops / multi-sheet / missing asset), decide permanent vs retry.
            if (SpriteGpuAnimSwitch.ToGpu(em, e, now))
                return true;

            if (em.HasComponent<SpriteGpuDriven>(e))
                return true;

            // Still on CPU: permanent fail if clip ineligible, multi-sheet, or cropped.
            if (HasMultipleClipSheets(em, e) || HasCroppedBoundSheet(em, e))
                return true;

            // Bound sheet asset not ready yet — retry next frame.
            if (em.HasComponent<SpriteSheetBinding>(e))
            {
                var binding = em.GetComponentData<SpriteSheetBinding>(e);
                if (binding.Sheet != Entity.Null &&
                    (!em.Exists(binding.Sheet) || !em.HasComponent<SpriteSheetAsset>(binding.Sheet)))
                    return false;
            }

            var player = em.GetComponentData<SpriteAnimPlayer>(e);
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            int ci = player.ClipIndex;
            if (ci < 0 || ci >= set.Clips.Length)
                return true;
            return !SpriteGpuEligibility.IsGpuEligible(em, e, out _);
        }

        static bool HasMultipleClipSheets(EntityManager em, Entity e)
        {
            if (!em.HasBuffer<SpriteClipSheetBindingEntry>(e))
                return false;
            var buf = em.GetBuffer<SpriteClipSheetBindingEntry>(e);
            Entity first = Entity.Null;
            bool have = false;
            for (int i = 0; i < buf.Length; i++)
            {
                Entity sheet = buf[i].Sheet;
                if (sheet == Entity.Null)
                    continue;
                if (!have)
                {
                    first = sheet;
                    have = true;
                }
                else if (sheet != first)
                    return true;
            }
            return false;
        }

        static bool HasCroppedBoundSheet(EntityManager em, Entity e)
        {
            if (!em.HasComponent<SpriteSheetBinding>(e))
                return false;
            var binding = em.GetComponentData<SpriteSheetBinding>(e);
            if (binding.Sheet == Entity.Null || !em.Exists(binding.Sheet))
                return false;
            if (!em.HasComponent<SpriteSheetDefinition>(binding.Sheet))
                return false;
            return em.GetComponentData<SpriteSheetDefinition>(binding.Sheet).UseCellCrops != 0;
        }
    }
}
