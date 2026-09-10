using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteFrameworkRegressionTests
    {
        static (SpriteAnimSetRef, SpriteAnimPlayer) Build(byte wrap = SpriteAnimWrap.Loop)
            => SpriteAnimSetBuilder.Build(Allocator.Persistent, new[]
            {
                new SpriteAnimSetBuilder.ClipInput
                {
                    Name = "Test", FrameRate = 4f, WrapMode = wrap,
                    GlobalFrameIndices = new[] { 3, 4, 5, 6 }, OnCompleteClipIndex = -1,
                },
            });

        [TestCase(false)]
        [TestCase(true)]
        public void DisabledAnimationDoesNotTickButEnabledAndUntaggedDo(bool hasFlag)
        {
            using var world = new World("Playback regression");
            var (set, player) = Build();
            try
            {
                var em = world.EntityManager;
                var entity = em.CreateEntity();
                em.AddComponentData(entity, set);
                em.AddComponentData(entity, player);
                em.AddComponentData(entity, new SpriteAnimFrame { Scale = new float2(1f) });
                if (hasFlag)
                {
                    em.AddComponent<SpriteAnimEnabled>(entity);
                    em.SetComponentEnabled<SpriteAnimEnabled>(entity, false);
                }
                var system = world.GetOrCreateSystem<SpriteAnimPlayerSystem>();
                world.SetTime(new TimeData(0.25, 0.25f));
                system.Update(world.Unmanaged);
                Assert.AreEqual(hasFlag ? 0f : 1f, em.GetComponentData<SpriteAnimPlayer>(entity).Time);
                if (hasFlag)
                {
                    em.SetComponentEnabled<SpriteAnimEnabled>(entity, true);
                    system.Update(world.Unmanaged);
                    Assert.AreEqual(1f, em.GetComponentData<SpriteAnimPlayer>(entity).Time);
                }
            }
            finally { set.Set.Dispose(); }
        }

        [TestCase(0, 1f)]
        [TestCase(1, 0f)]
        public void FrozenGpuConversionKeepsFrameAndSpeed(int playing, float speed)
        {
            using var world = new World("GPU pause regression");
            var (set, player) = Build();
            try
            {
                var em = world.EntityManager;
                var grid = em.CreateEntity();
                em.AddComponentData(grid, new SpriteAnimGrid { Cols = 4, Rows = 2 });
                var entity = em.CreateEntity();
                player.Time = 2.5f;
                player.Playing = (byte)playing;
                player.Speed = speed;
                em.AddComponentData(entity, set);
                em.AddComponentData(entity, player);
                Assert.IsTrue(SpriteGpuAnimSwitch.ToGpu(em, entity, 10f));
                var gpu = em.GetComponentData<SpriteGpuAnim>(entity);
                Assert.AreEqual(0f, gpu.Rate);
                Assert.AreEqual(0.25f, gpu.SlotOriginX); // slot 5: column 1, bottom row
                Assert.AreEqual(0f, gpu.SlotOriginY);
                Assert.IsTrue(SpriteGpuAnimSwitch.ToCpu(em, entity));
                var restored = em.GetComponentData<SpriteAnimPlayer>(entity);
                Assert.AreEqual(2.5f, restored.Time);
                Assert.AreEqual(speed, restored.Speed);
            }
            finally { set.Set.Dispose(); SpriteGpuAnimResources.TakeDirty(); }
        }

        [Test]
        public void CompletedGpuTintTweenStillRequestsUpload()
        {
            using var world = new World("Tint regression");
            var em = world.EntityManager;
            var entity = em.CreateEntity(typeof(SpriteGpuDriven), typeof(SpriteTint));
            SpriteTintFx.FadeOut(em, entity, 0.1f);
            SpriteGpuAnimResources.TakeDirty();
            world.SetTime(new TimeData(0.2, 0.2f));
            world.GetOrCreateSystem<SpriteTintTweenSystem>().Update(world.Unmanaged);
            Assert.IsFalse(em.IsComponentEnabled<SpriteTintTween>(entity));
            Assert.AreEqual(0f, em.GetComponentData<SpriteTint>(entity).Value.w);
            Assert.IsTrue(SpriteGpuAnimResources.TakeDirty());
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void CullingUsesSelectedSpritePlane(bool xy, bool expectedVisible)
        {
            var camera = Camera.main;
            bool ownsCamera = camera == null;
            if (ownsCamera)
            {
                var cameraObject = new GameObject("Culling regression camera", typeof(Camera));
                cameraObject.tag = "MainCamera";
                camera = cameraObject.GetComponent<Camera>();
            }
            var previousPosition = camera.transform.position;
            bool previousOrthographic = camera.orthographic;
            float previousSize = camera.orthographicSize;
            float previousAspect = camera.aspect;
            camera.transform.position = Vector3.zero;
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.aspect = 1f;
            bool previous = SpriteBatchSpawner.LayoutXy;
            try
            {
                SpriteBatchSpawner.LayoutXy = xy;
                using var world = new World("Culling plane regression");
                var em = world.EntityManager;
                var entity = em.CreateEntity(typeof(SpriteAnimEnabled));
                em.AddComponentData(entity, LocalTransform.FromPosition(new float3(0, 50, 0)));
                world.GetOrCreateSystem<SpriteAnimCullingSystem>().Update(world.Unmanaged);
                em.CompleteAllTrackedJobs();
                Assert.AreEqual(expectedVisible, em.IsComponentEnabled<SpriteAnimEnabled>(entity));
            }
            finally
            {
                SpriteBatchSpawner.LayoutXy = previous;
                if (ownsCamera) Object.DestroyImmediate(camera.gameObject);
                else
                {
                    camera.transform.position = previousPosition;
                    camera.orthographic = previousOrthographic;
                    camera.orthographicSize = previousSize;
                    camera.aspect = previousAspect;
                }
            }
        }

        [Test]
        public void PingPongHitboxesFollowReturningFrame()
        {
            using var world = new World("Hitbox regression");
            var (set, player) = Build(SpriteAnimWrap.PingPong);
            var hitboxes = SpriteHitboxSetBuilder.Build(Allocator.Persistent, new[]
            {
                new SpriteHitboxSetBuilder.BoxInput { ClipName = "Test", FrameIndex = 2, Id = 7 },
                new SpriteHitboxSetBuilder.BoxInput { ClipName = "Test", FrameIndex = 3, Id = 8 },
            });
            try
            {
                var em = world.EntityManager;
                var entity = em.CreateEntity();
                player.Time = 4f; // 0,1,2,3,2,1
                em.AddComponentData(entity, player);
                em.AddComponentData(entity, set);
                em.AddComponentData(entity, SpriteFlip.Identity);
                em.AddComponentData(entity, new SpriteHitboxSetRef { Set = hitboxes });
                em.AddBuffer<SpriteHitboxLive>(entity);
                world.GetOrCreateSystem<SpriteHitboxActivationSystem>().Update(world.Unmanaged);
                var live = em.GetBuffer<SpriteHitboxLive>(entity);
                Assert.AreEqual(1, live.Length);
                Assert.AreEqual(7, live[0].Box.Id);
            }
            finally { hitboxes.Dispose(); set.Set.Dispose(); }
        }

        [Test]
        public void LightingTogglePreservesRegisteredMaterial()
        {
            var previous = SpriteShaderLibrary.UseLit;
            var material = new Material(Shader.Find(SpriteShaderLibrary.InstancedShader));
            var record = new SpriteSheetRecord { Material = material };
            SpriteSheetRegistry.Records.Add(record);
            try
            {
                SpriteShaderLibrary.UseLit = true;
                SpriteSheetRegistry.ResetMaterials();
                Assert.AreSame(material, record.Material);
                Assert.AreEqual(SpriteShaderLibrary.InstancedShaderLit, record.Material.shader.name);
                SpriteShaderLibrary.UseLit = false;
                SpriteSheetRegistry.ResetMaterials();
                Assert.AreEqual(SpriteShaderLibrary.InstancedShader, record.Material.shader.name);
            }
            finally
            {
                SpriteSheetRegistry.Records.Remove(record);
                SpriteShaderLibrary.UseLit = previous;
                SpriteSheetRegistry.ResetMaterials();
                Object.DestroyImmediate(material);
            }
        }
    }
}
