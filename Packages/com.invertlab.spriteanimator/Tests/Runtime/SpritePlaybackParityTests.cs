using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpritePlaybackParityTests
    {
        [TestCase(SpriteAnimWrap.Loop, false)]
        [TestCase(SpriteAnimWrap.Loop, true)]
        [TestCase(SpriteAnimWrap.Once, false)]
        [TestCase(SpriteAnimWrap.Once, true)]
        [TestCase(SpriteAnimWrap.PingPong, false)]
        [TestCase(SpriteAnimWrap.PingPong, true)]
        [TestCase(SpriteAnimWrap.ReverseLoop, false)]
        [TestCase(SpriteAnimWrap.ReverseLoop, true)]
        [TestCase(SpriteAnimWrap.ReverseOnce, false)]
        [TestCase(SpriteAnimWrap.ReverseOnce, true)]
        [TestCase(SpriteAnimWrap.Loop, true, true)]
        [TestCase(SpriteAnimWrap.PingPong, true, true)]
        [TestCase(SpriteAnimWrap.ReverseLoop, true, true)]
        public void PreviewFramesAndCompletionMatchRuntimeAcrossCycles(byte wrap, bool uneven, bool backward = false)
        {
            var durations = uneven ? new[] { 1f, 2f, 3f, 1f } : new[] { 1f, 1f, 1f, 1f };
            var preview = new SpriteClipDef
            {
                Name = "Parity", Frames = new[] { 0, 1, 2, 3 }, FrameRate = 4,
                WrapMode = wrap, FrameDurationScales = durations,
            };
            var (set, player) = SpriteAnimSetBuilder.Build(Allocator.Persistent, new[]
            {
                new SpriteAnimSetBuilder.ClipInput
                {
                    Name = "Parity", GlobalFrameIndices = preview.Frames, FrameRate = 4,
                    WrapMode = wrap, Loop = wrap == SpriteAnimWrap.Loop,
                    FrameDurationScales = durations, OnCompleteClipIndex = -1,
                },
            });
            using var world = new World("Playback parity");
            try
            {
                var em = world.EntityManager;
                var entity = em.CreateEntity();
                em.AddComponentData(entity, set);
                em.AddComponentData(entity, player);
                em.AddComponentData(entity, new SpriteAnimFrame { Scale = new float2(1) });
                Assert.IsTrue(SpriteAnims.Play(em, entity, 0, true));
                if (backward) SpriteAnims.SetSpeed(em, entity, -1);
                var system = world.GetOrCreateSystem<SpriteAnimPlayerSystem>();
                const float delta = 1f / 32f;
                float elapsed = 0;
                float total = SpriteAnimPlayback.TotalAuthoredDuration(preview);
                for (int tick = 0; tick < 240; tick++)
                {
                    float previewTime = wrap == SpriteAnimWrap.ReverseOnce ? Mathf.Max(0, total - elapsed) : elapsed;
                    if (backward) previewTime = Mathf.Repeat(-elapsed, SpriteAnimPlayback.CycleDuration(preview));
                    var sample = SpriteAnimPlayback.EvaluatePreview(preview, previewTime, false);
                    Assert.AreEqual(sample.Frame, em.GetComponentData<SpriteAnimFrame>(entity).Slot,
                        $"{wrap}, elapsed {elapsed}, uneven={uneven}");
                    Assert.AreEqual(sample.Ended, em.HasComponent<SpriteAnimCompleted>(entity),
                        $"Completion at {elapsed}");
                    if (tick == 20)
                    {
                        float heldPhase = em.GetComponentData<SpriteAnimPlayer>(entity).Time;
                        SpriteAnims.Pause(em, entity);
                        world.SetTime(new TimeData(elapsed, delta));
                        system.Update(world.Unmanaged);
                        Assert.AreEqual(heldPhase, em.GetComponentData<SpriteAnimPlayer>(entity).Time);
                        Assert.AreEqual(sample.Frame, em.GetComponentData<SpriteAnimFrame>(entity).Slot);
                        SpriteAnims.Resume(em, entity);
                    }
                    elapsed += delta;
                    world.SetTime(new TimeData(elapsed, delta));
                    system.Update(world.Unmanaged);
                }
            }
            finally { set.Set.Dispose(); }
        }

        [Test]
        public void ReverseLoopSeekRoundTripsAuthoredFramesAndFractions()
        {
            var clip = new SpriteClipDef
            {
                Frames = new[] { 0, 1, 2 }, FrameRate = 4, WrapMode = SpriteAnimWrap.ReverseLoop,
                FrameDurationScales = new[] { 1f, 2f, 3f },
            };
            for (int frame = 0; frame < 3; frame++)
            {
                float authored = SpriteAnimPlayback.AuthoredStartTime(clip, frame)
                    + SpriteAnimPlayback.FrameDuration(clip, frame) * 0.25f;
                var sample = SpriteAnimPlayback.EvaluatePreview(clip,
                    SpriteAnimPlayback.PreviewTimeForAuthoredTime(clip, authored), false);
                Assert.AreEqual(frame, sample.Frame);
                Assert.AreEqual(0.25f, sample.Fraction, 0.0001f);
            }
        }
    }
}
