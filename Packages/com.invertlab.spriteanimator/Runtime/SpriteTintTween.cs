using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Active tint tween on a sprite entity (enable-bit gated). The system
    /// lerps <see cref="SpriteTint"/> from <see cref="From"/> to <see cref="To"/>
    /// over <see cref="Duration"/> seconds, then disables itself. Add/enable
    /// via <see cref="SpriteTintFx"/>.
    /// </summary>
    public struct SpriteTintTween : IComponentData, IEnableableComponent
    {
        /// <summary>once = disable at the end; loop = restart; pingpong = bounce.</summary>
        public byte Wrap; // 0 once, 1 loop, 2 pingpong

        public float Duration;
        public float Time;
        public float4 From;
        public float4 To;
    }

    /// <summary>Static API for the common tint effects (hit flash, fades).</summary>
    public static class SpriteTintFx
    {
        /// <summary>Linear 0-1 progress with pingpong folding when Wrap == 2.</summary>
        public static float Evaluate(float time, float duration, byte wrap)
        {
            if (duration <= 1e-6f)
                return 1f;
            float t = math.saturate(time / duration);
            if (wrap == 2)
                t = 1f - math.abs(t * 2f - 1f); // 0..1..0
            return t;
        }

        /// <summary>Flash: tint jumps to <paramref name="flashColor"/> and
        /// eases back to the sprite's current tint.</summary>
        public static void Flash(EntityManager em, Entity e, Color flashColor, float duration)
        {
            var current = em.HasComponent<SpriteTint>(e)
                ? em.GetComponentData<SpriteTint>(e).Value
                : new float4(1f);
            Play(em, e, ToFloat4(flashColor), current, duration);
        }

        /// <summary>Fade the whole tint to <paramref name="targetColor"/>.</summary>
        public static void FadeTo(EntityManager em, Entity e, Color targetColor, float duration,
            bool loop = false, bool pingpong = false)
            => Play(em, e, CurrentOrWhite(em, e), ToFloat4(targetColor), duration,
                loop, pingpong);

        /// <summary>Fade alpha to 0 (color channels keep tweening to themselves).</summary>
        public static void FadeOut(EntityManager em, Entity e, float duration)
        {
            var from = CurrentOrWhite(em, e);
            Play(em, e, from, new float4(from.x, from.y, from.z, 0f), duration);
        }

        /// <summary>Fade alpha from 0 back to <paramref name="targetAlpha"/>.</summary>
        public static void FadeIn(EntityManager em, Entity e, float duration,
            float targetAlpha = 1f)
        {
            var to = CurrentOrWhite(em, e);
            Play(em, e, new float4(to.x, to.y, to.z, 0f),
                new float4(to.x, to.y, to.z, targetAlpha), duration);
        }

        /// <summary>Play a raw from→to tween (restarts if one is active).</summary>
        public static void Play(EntityManager em, Entity e, float4 from, float4 to,
            float duration, bool loop = false, bool pingpong = false)
        {
            var tween = new SpriteTintTween
            {
                Wrap = (byte)(pingpong ? 2 : loop ? 1 : 0),
                Duration = math.max(0.01f, duration),
                Time = 0f,
                From = from,
                To = to,
            };
            if (em.HasComponent<SpriteTintTween>(e))
            {
                em.SetComponentData(e, tween);
                em.SetComponentEnabled<SpriteTintTween>(e, true);
            }
            else
            {
                em.AddComponentData(e, tween);
            }
        }

        static float4 CurrentOrWhite(EntityManager em, Entity e)
            => em.HasComponent<SpriteTint>(e)
                ? em.GetComponentData<SpriteTint>(e).Value
                : new float4(1f);

        static float4 ToFloat4(Color c) => new(c.r, c.g, c.b, c.a);
    }

    /// <summary>
    /// Advances tint tweens and writes <see cref="SpriteTint"/>. Runs in the
    /// plain simulation bucket (before the OrderLast render packers). When any
    /// tweened sprite is GPU-driven the instance buffer is marked dirty — GPU
    /// crowds only re-upload on change, so static tints stay free.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct SpriteTintTweenSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            bool anyGpu = false;

            foreach (var (tint, tween, enabled) in
                     SystemAPI.Query<RefRW<SpriteTint>, RefRW<SpriteTintTween>,
                                     EnabledRefRW<SpriteTintTween>>())
            {
                if (!enabled.ValueRO)
                    continue;

                tween.ValueRW.Time += dt;
                float t = SpriteTintFx.Evaluate(
                    tween.ValueRO.Time, tween.ValueRO.Duration, tween.ValueRO.Wrap);
                tint.ValueRW.Value = math.lerp(tween.ValueRO.From, tween.ValueRO.To, t);

                bool finished = tween.ValueRO.Time >= tween.ValueRO.Duration;
                if (finished && tween.ValueRO.Wrap != 1 && tween.ValueRO.Wrap != 2)
                {
                    enabled.ValueRW = false;
                }
                else if (finished)
                {
                    // loop / pingpong: fold the clock, keep going
                    tween.ValueRW.Time = 0f;
                }
            }

            // GPU re-upload only when a tweened sprite is actually GPU-driven
            foreach (var (tween, enabled, gpu) in
                     SystemAPI.Query<RefRO<SpriteTintTween>, EnabledRefRO<SpriteTintTween>,
                                     RefRO<SpriteGpuDriven>>())
            {
                if (enabled.ValueRO)
                {
                    anyGpu = true;
                    break;
                }
            }

            if (anyGpu)
                SpriteGpuAnimResources.MarkDirty();
        }
    }
}
