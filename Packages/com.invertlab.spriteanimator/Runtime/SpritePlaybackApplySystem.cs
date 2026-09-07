using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Applies SpritePlaybackPreference after spawn.
    /// PreferGpu promotes the baked SpriteSheetBinding onto the legacy GPU sheet
    /// (SetSheet / SetGrid), then converts once when the clip is eligible.
    /// ForceCpu restores CPU playback; Auto leaves the CPU default.
    /// Marks SpritePlaybackApplied only when finished or permanently impossible,
    /// so PreferGpu can retry if the sheet asset is not ready yet.
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

            if (HasMultipleClipSheets(em, e))
                return true; // permanent: GPU path is single-sheet

            if (!TryPrepareLegacySheet(em, e, out bool permanentFail))
                return permanentFail; // crops / missing asset: done or retry

            if (SpriteGpuAnimSwitch.ToGpu(em, e, now))
                return true;

            // Failed while still on CPU: finish only when the clip can never go GPU.
            var player = em.GetComponentData<SpriteAnimPlayer>(e);
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            int ci = player.ClipIndex;
            if (ci < 0 || ci >= set.Clips.Length)
                return true;
            return !SpriteGpuEligibility.IsGpuEligible(ref set, ci, out _);
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

        /// <summary>
        /// GPU clock draws SpriteRenderResources.Sheet only. Bound sheets must be
        /// promoted to that legacy path (clear binding) before ToGpu.
        /// </summary>
        static bool TryPrepareLegacySheet(EntityManager em, Entity e, out bool permanentFail)
        {
            permanentFail = false;
            if (!em.HasComponent<SpriteSheetBinding>(e))
                return true;

            var binding = em.GetComponentData<SpriteSheetBinding>(e);
            if (binding.Sheet == Entity.Null)
                return true;

            Entity sheet = binding.Sheet;
            if (!em.Exists(sheet) || !em.HasComponent<SpriteSheetDefinition>(sheet))
            {
                permanentFail = false;
                return false;
            }

            var def = em.GetComponentData<SpriteSheetDefinition>(sheet);
            if (def.UseCellCrops != 0)
            {
                permanentFail = true;
                return false;
            }

            if (!em.HasComponent<SpriteSheetAsset>(sheet))
            {
                permanentFail = false;
                return false;
            }

            var asset = em.GetComponentObject<SpriteSheetAsset>(sheet);
            if (asset == null || asset.Texture == null)
            {
                permanentFail = false;
                return false;
            }

            SpriteInstanceRenderSystem.Install(em);
            SpriteInstanceRenderSystem.SetSheet(asset.Texture);
            SpriteInstanceRenderSystem.SetGrid(em, def.Cols, def.Rows,
                def.CellAspect > 0.01f ? def.CellAspect : 1f, null);
            em.SetComponentData(e, new SpriteSheetBinding { Sheet = Entity.Null });
            return true;
        }
    }
}
