using Unity.Burst;
using Unity.Collections;
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
    /// Advances tint tweens and writes <see cref="SpriteTint"/>. Managed
    /// OnUpdate: the tween advance runs as Burst jobs; the GPU re-upload
    /// decision reads the job result in managed code (Burst cannot write
    /// managed statics).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SpriteTintTweenSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var gpuFlag = new NativeReference<int>(Allocator.TempJob);

            state.Dependency = new AdvanceTweenJob { Dt = dt }
                .ScheduleParallel(state.Dependency);
            state.Dependency = new GpuDirtyJob { GpuFlag = gpuFlag }
                .Schedule(state.Dependency);
            state.Dependency.Complete();

            if (gpuFlag.Value != 0)
                SpriteGpuAnimResources.MarkDirty();
            gpuFlag.Dispose();
        }

        /// <summary>Advances every enabled tween, CPU- and GPU-driven alike
        /// (GPU sprites need the fresh tint when the buffer re-uploads).</summary>
        [BurstCompile]
        partial struct AdvanceTweenJob : IJobEntity
        {
            public float Dt;

            void Execute(ref SpriteTint tint, ref SpriteTintTween tween,
                         EnabledRefRW<SpriteTintTween> enabled)
            {
                if (!enabled.ValueRO)
                    return;

                tween.Time += Dt;
                float t = SpriteTintFx.Evaluate(tween.Time, tween.Duration, tween.Wrap);
                tint.Value = math.lerp(tween.From, tween.To, t);

                if (tween.Time < tween.Duration)
                    return;
                if (tween.Wrap == 1 || tween.Wrap == 2)
                    tween.Time = 0f; // loop / pingpong restart
                else
                    enabled.ValueRW = false;
            }
        }

        /// <summary>Single-threaded: flags the managed dirty write when any
        /// enabled tween sits on a GPU-driven sprite.</summary>
        [BurstCompile]
        [WithAll(typeof(SpriteGpuDriven))]
        partial struct GpuDirtyJob : IJobEntity
        {
            public NativeReference<int> GpuFlag;

            void Execute(EnabledRefRO<SpriteTintTween> enabled)
            {
                if (enabled.ValueRO)
                    GpuFlag.Value = 1;
            }
        }
    }
}
