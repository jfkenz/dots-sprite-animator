using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteResourceLifetimeTests
    {
        [Test]
        public void AlternatingWorldsRefreshTheSharedGpuUpload()
        {
            using var a = new World("GPU world A");
            using var b = new World("GPU world B");
            var texture = new Texture2D(4, 4);
            try
            {
                SpriteInstanceRenderSystem.SetSheet(texture);
                SpriteInstanceRenderSystem.SetGrid(a.EntityManager, 1, 1);
                SpriteInstanceRenderSystem.SetGrid(b.EntityManager, 1, 1);
                void Add(World world, float x)
                {
                    var em = world.EntityManager;
                    var entity = em.CreateEntity(typeof(SpriteGpuDriven));
                    em.AddComponentData(entity, LocalTransform.FromPosition(new float3(x, 0, 0)));
                    em.AddComponentData(entity, new SpriteGpuAnim { CellW = 1, CellH = 1, N = 1 });
                    em.AddComponentData(entity, new SpriteTint { Value = new float4(1) });
                    em.AddComponentData(entity, new SpriteFlip());
                }
                Add(a, 1);
                Add(b, 2);
                var renderA = a.GetOrCreateSystem<SpriteGpuAnimRenderSystem>();
                var renderB = b.GetOrCreateSystem<SpriteGpuAnimRenderSystem>();
                renderA.Update(a.Unmanaged);
                Assert.AreEqual(1, SpriteGpuAnimResources.Staging[0].PosScale.x);
                renderB.Update(b.Unmanaged);
                Assert.AreEqual(2, SpriteGpuAnimResources.Staging[0].PosScale.x);
                renderA.Update(a.Unmanaged);
                Assert.AreEqual(1, SpriteGpuAnimResources.Staging[0].PosScale.x);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FactoryBlobSurvivesDisabledCloneAndIsReleasedAfterLastOwner(bool gpu)
        {
            using var world = new World("Factory lifetime");
            var texture = new Texture2D(8, 8);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), Vector2.one * 0.5f);
            try
            {
                var em = world.EntityManager;
                var cleanup = world.GetOrCreateSystemManaged<SpriteAnimBlobLifetimeSystem>();
                for (int i = 0; i < 8; i++)
                {
                    var entity = SpriteEntityFactory.Create(em, new[] { sprite }, 4, true, Vector3.zero, 1);
                    var blob = em.GetComponentData<SpriteAnimSetRef>(entity).Set;
                    if (gpu) Assert.IsTrue(SpriteGpuAnimSwitch.ToGpu(em, entity, 0));
                    em.AddComponent<Prefab>(entity);
                    var copy = em.Instantiate(entity);
                    em.AddComponent<Disabled>(copy);
                    em.DestroyEntity(entity);
                    cleanup.Update();
                    Assert.AreEqual(1, blob.Value.Clips.Length);
                    Assert.IsTrue(em.HasComponent<SpriteAnimOwnedBlob>(copy));
                    Assert.IsFalse(em.Exists(entity));
                    em.DestroyEntity(copy);
                    cleanup.Update();
                    Assert.IsFalse(em.Exists(copy));
                    Assert.Throws<System.InvalidOperationException>(() => { var count = blob.Value.Clips.Length; });
                }
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void WorldDisposalReleasesFactoryBlobsWithoutWaitingForAnUpdate()
        {
            var texture = new Texture2D(8, 8);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), Vector2.one * 0.5f);
            BlobAssetReference<SpriteAnimSetBlob> blob;
            try
            {
                using (var world = new World("Factory shutdown"))
                {
                    var entity = SpriteEntityFactory.Create(world.EntityManager, new[] { sprite }, 4, false, Vector3.zero, 1);
                    blob = world.EntityManager.GetComponentData<SpriteAnimSetRef>(entity).Set;
                    Assert.AreEqual(-1, blob.Value.Clips[0].OnCompleteClipIndex);
                }
                Assert.Throws<System.InvalidOperationException>(() => { var count = blob.Value.Clips.Length; });
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void DisposingRecordDestroysOwnedMaterialButKeepsBorrowedMaterial()
        {
            var shader = Shader.Find(SpriteShaderLibrary.InstancedShader);
            Assert.IsNotNull(shader);
            var owned = new Material(shader);
            var borrowed = new Material(shader);
            var a = new SpriteSheetRecord { Material = owned, OwnsMaterial = true };
            var b = new SpriteSheetRecord { Material = borrowed };
            try
            {
                a.EnsureCapacity(1);
                a.Dispose();
                a.Dispose();
                b.Dispose();
                Assert.IsTrue(owned == null);
                Assert.IsTrue(borrowed != null);
                Assert.IsNull(a.Buffer);
                Assert.IsFalse(a.Crops.IsCreated);
            }
            finally { Object.DestroyImmediate(borrowed); }
        }

        [Test]
        public void SharedRenderResourcesAreReleasedOnlyAfterLastWorld()
        {
            var a = new World("Renderer A");
            var b = new World("Renderer B");
            var texture = new Texture2D(8, 8);
            try
            {
                a.GetOrCreateSystemManaged<SpriteRenderResourceLifetimeSystem>();
                b.GetOrCreateSystemManaged<SpriteRenderResourceLifetimeSystem>();
                SpriteRenderResources.EnsureCapacity(1);
                SpriteRenderResources.EnsureQuad();
                SpriteGpuAnimResources.EnsureCapacity(1);
                SpriteGpuAnimResources.EnsureObjects(texture);
                int record = SpriteSheetRegistry.GetOrAdd(texture);
                var material = SpriteSheetRegistry.Records[record].Material;
                a.Dispose();
                Assert.IsTrue(SpriteRenderResources.Staging.IsCreated);
                Assert.IsTrue(material != null);
                b.Dispose();
                Assert.IsFalse(SpriteRenderResources.Staging.IsCreated);
                Assert.IsFalse(SpriteGpuAnimResources.Staging.IsCreated);
                Assert.IsNull(SpriteGpuAnimResources.Buffer);
                Assert.IsTrue(material == null);
                Assert.IsEmpty(SpriteSheetRegistry.Records);
            }
            finally
            {
                if (a.IsCreated) a.Dispose();
                if (b.IsCreated) b.Dispose();
                Object.DestroyImmediate(texture);
            }
        }
    }
}
