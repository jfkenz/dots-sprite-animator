using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteGpuControlTests
    {
        [Test]
        public void IndependentSocketTracksRejectTheCompactGpuClock()
        {
            var (set, _) = SpriteAnimSetBuilder.Build(Allocator.Persistent, new[]
            {
                new SpriteAnimSetBuilder.ClipInput
                {
                    Name = "Loop", Loop = true, FrameRate = 4,
                    GlobalFrameIndices = new[] { 0, 1 }, OnCompleteClipIndex = -1,
                },
            }, new[] { new SpriteAnimSetBuilder.SocketMotionInput { Name = "Independent" } });
            try
            {
                Assert.IsFalse(SpriteGpuEligibility.IsGpuEligible(ref set.Set.Value, 0, out var reason));
                StringAssert.Contains("socket", reason.ToString());
            }
            finally { set.Set.Dispose(); }
        }

        static (SpriteAnimSetRef, SpriteAnimPlayer) Build(int complete = -1)
            => SpriteAnimSetBuilder.Build(Allocator.Persistent, new[]
            {
                new SpriteAnimSetBuilder.ClipInput
                {
                    Name = "Test", FrameRate = 4, Loop = true, WrapMode = SpriteAnimWrap.Loop,
                    GlobalFrameIndices = new[] { 0, 1, 2, 3 }, OnCompleteClipIndex = complete,
                },
            });

        static Entity Create(EntityManager em, SpriteAnimSetRef set, SpriteAnimPlayer player)
        {
            SpriteInstanceRenderSystem.SetGrid(em, 4, 1);
            var entity = em.CreateEntity();
            em.AddComponentData(entity, set);
            em.AddComponentData(entity, player);
            em.AddComponentData(entity, new SpriteAnimFrame { Scale = new float2(1) });
            return entity;
        }

        [TestCase("hitboxes")]
        [TestCase("sockets")]
        [TestCase("events")]
        [TestCase("queue")]
        [TestCase("oneshot")]
        [TestCase("hold")]
        [TestCase("negative")]
        [TestCase("parent")]
        [TestCase("complete")]
        public void GameplayFeaturesRejectGpuConversionWithoutRemovingState(string feature)
        {
            using var world = new World("GPU eligibility");
            var (set, player) = Build(feature == "complete" ? 0 : -1);
            try
            {
                if (feature == "queue") player.QueuedClipIndex = 0;
                if (feature == "oneshot") player.OneShotActive = 1;
                if (feature == "hold") player.HitstopActive = 1;
                if (feature == "negative") player.Speed = -1;
                var em = world.EntityManager;
                var entity = Create(em, set, player);
                if (feature == "hitboxes") em.AddComponent<SpriteHitboxSetRef>(entity);
                if (feature == "sockets") em.AddComponent<SpriteSocketMotionPlayer>(entity);
                if (feature == "events") em.AddBuffer<SpriteAnimEventBuffer>(entity);
                if (feature == "parent") em.AddComponentData(entity, new Parent { Value = em.CreateEntity() });
                Assert.IsFalse(SpriteGpuAnimSwitch.ToGpu(em, entity, 10));
                Assert.IsTrue(em.HasComponent<SpriteAnimPlayer>(entity));
                Assert.IsTrue(em.HasComponent<SpriteAnimSetRef>(entity));
                Assert.IsFalse(em.HasComponent<SpriteGpuDriven>(entity));
            }
            finally { set.Set.Dispose(); }
        }

        [Test]
        public void PublicPauseSeekSpeedAndPlayWorkAfterConversion()
        {
            using var world = new World("GPU controls");
            var (set, player) = Build();
            try
            {
                var em = world.EntityManager;
                player.Time = 1.5f;
                player.CrossfadeDuration = 0.75f;
                var entity = Create(em, set, player);
                float now = Time.unscaledTime;
                Assert.IsTrue(SpriteGpuAnimSwitch.ToGpu(em, entity, now));
                Assert.AreEqual(1, SpriteAnims.GetSpeed(em, entity));
                Assert.IsTrue(SpriteAnims.IsGpuDriven(em, entity), "Reading speed must not change rendering mode.");
                SpriteAnims.Pause(em, entity);
                Assert.IsFalse(SpriteAnims.IsGpuDriven(em, entity));
                var paused = em.GetComponentData<SpriteAnimPlayer>(entity);
                Assert.AreEqual(0, paused.Playing);
                Assert.AreEqual(1.5f, paused.Time, 0.1f);
                Assert.AreEqual(-1, paused.QueuedClipIndex);
                Assert.AreEqual(-1, paused.ResumeClipIndex);
                Assert.AreEqual(0.75f, paused.CrossfadeDuration);
                SpriteAnims.SeekFrame(em, entity, 3);
                Assert.AreEqual(3, em.GetComponentData<SpriteAnimFrame>(entity).Slot);
                SpriteAnims.Resume(em, entity);
                Assert.IsTrue(SpriteGpuAnimSwitch.ToGpu(em, entity, Time.unscaledTime));
                SpriteAnims.SetSpeed(em, entity, -2);
                Assert.AreEqual(-2, SpriteAnims.GetSpeed(em, entity));
                Assert.IsFalse(SpriteAnims.IsGpuDriven(em, entity));
                SpriteAnims.SetSpeed(em, entity, 1);
                Assert.IsTrue(SpriteGpuAnimSwitch.ToGpu(em, entity, Time.unscaledTime));
                Assert.IsTrue(SpriteAnims.Play(em, entity, "Test", true));
                Assert.IsFalse(SpriteAnims.IsGpuDriven(em, entity));
                Assert.AreEqual(0, em.GetComponentData<SpriteAnimPlayer>(entity).Time);
            }
            finally { set.Set.Dispose(); }
        }

        [Test]
        public void ConflictingSheetConversionFailsWithoutChangingExistingSprites()
        {
            using var world = new World("GPU sheets");
            var (set, player) = Build();
            var a = new Texture2D(8, 8);
            var b = new Texture2D(8, 8);
            try
            {
                var em = world.EntityManager;
                Entity Bind(Entity entity, Texture2D texture)
                {
                    var sheet = em.CreateEntity();
                    em.AddComponentData(sheet, new SpriteSheetDefinition { Cols = 4, Rows = 1, CellAspect = 1 });
                    em.AddComponentObject(sheet, new SpriteSheetAsset { Texture = texture });
                    em.AddComponentData(entity, new SpriteSheetBinding { Sheet = sheet });
                    return sheet;
                }
                var first = Create(em, set, player);
                var second = Create(em, set, player);
                var firstSheet = Bind(first, a);
                var secondSheet = Bind(second, b);
                Assert.IsTrue(SpriteGpuAnimSwitch.ToGpu(em, first, 10));
                Assert.IsFalse(SpriteGpuAnimSwitch.ToGpu(em, second, 10));
                Assert.AreSame(a, SpriteRenderResources.Sheet);
                Assert.AreEqual(secondSheet, em.GetComponentData<SpriteSheetBinding>(second).Sheet);
                Assert.Throws<System.InvalidOperationException>(() => SpriteInstanceRenderSystem.SetSheet(b));
                Assert.IsTrue(SpriteGpuAnimSwitch.ToCpu(em, first));
                Assert.AreEqual(firstSheet, em.GetComponentData<SpriteSheetBinding>(first).Sheet);
                Assert.IsTrue(SpriteGpuAnimSwitch.ToGpu(em, second, 10));
                Assert.AreSame(b, SpriteRenderResources.Sheet);
            }
            finally { set.Set.Dispose(); Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }

        [Test]
        public void SharedCrowdClockCannotOverrideAnIndividuallyConvertedSprite()
        {
            using var world = new World("GPU shared clip");
            var (set, player) = Build();
            try
            {
                var em = world.EntityManager;
                var entity = Create(em, set, player);
                Assert.IsTrue(SpriteGpuAnimSwitch.ToGpu(em, entity, 10));
                var gpu = em.GetComponentData<SpriteGpuAnim>(entity);
                Assert.IsFalse(SpriteGpuAnimResources.CanUseSharedClip(set.Set));
                Assert.Throws<System.InvalidOperationException>(() => SpriteGpuAnimResources.SetSharedClip(gpu));
                Assert.IsFalse(SpriteGpuAnimResources.UseSharedClip);
            }
            finally { set.Set.Dispose(); }
        }
    }
}
