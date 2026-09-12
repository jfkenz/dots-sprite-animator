using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteAtlasRenderingTests
    {
        [Test]
        public void SameTextureKeepsDifferentLayoutsAndWorldsIndependent()
        {
            var texture = new Texture2D(16, 16);
            try
            {
                var crop = new[] { new float4(.25f, .5f, 0, 0) };
                int a = SpriteSheetRegistry.GetOrAdd(texture, 4, 4, 1, crop, 1);
                Assert.AreEqual(a, SpriteSheetRegistry.GetOrAdd(texture, 4, 4, 1, crop, 1));
                int b = SpriteSheetRegistry.GetOrAdd(texture, 2, 2, 1, crop, 1);
                int c = SpriteSheetRegistry.GetOrAdd(texture, 4, 4, 2, crop, 1);
                int d = SpriteSheetRegistry.GetOrAdd(texture, 4, 4, 1, crop, 2);
                crop[0].z = .5f;
                int e = SpriteSheetRegistry.GetOrAdd(texture, 4, 4, 1, crop, 1);
                Assert.AreEqual(5, new System.Collections.Generic.HashSet<int> { a,b,c,d,e }.Count);
                Assert.AreEqual(0, SpriteSheetRegistry.Records[a].Crops[0].z);
                Assert.AreNotSame(SpriteSheetRegistry.Records[a].Material, SpriteSheetRegistry.Records[d].Material);
            }
            finally { SpriteSheetRegistry.Reset(); Object.DestroyImmediate(texture); }
        }

        [Test]
        public void FactoryAtlasSubsetKeepsFullGridAndIndependentBindings()
        {
            using var world = new World("Atlas subset");
            var atlas = new Texture2D(64, 32);
            var other = new Texture2D(8, 8);
            var last = Sprite.Create(atlas, new Rect(48, 0, 16, 16), Vector2.one * .5f);
            var first = Sprite.Create(atlas, new Rect(0, 16, 16, 16), Vector2.one * .5f);
            var second = Sprite.Create(other, new Rect(0, 0, 8, 8), Vector2.one * .5f);
            try
            {
                var em = world.EntityManager;
                var a = SpriteEntityFactory.Create(em, new[] {last, first}, 10, true, Vector3.zero, 1);
                var b = SpriteEntityFactory.Create(em, new[] {second}, 10, true, Vector3.zero, 1);
                var sheetA = em.GetComponentData<SpriteSheetBinding>(a).Sheet;
                var sheetB = em.GetComponentData<SpriteSheetBinding>(b).Sheet;
                Assert.AreNotEqual(sheetA, sheetB);
                var grid = em.GetComponentData<SpriteSheetDefinition>(sheetA);
                Assert.AreEqual(4, grid.Cols);
                Assert.AreEqual(2, grid.Rows);
                var blob = em.GetComponentData<SpriteAnimSetRef>(a).Set;
                Assert.AreEqual(7, blob.Value.Frames[0].x);
                Assert.AreEqual(0, blob.Value.Frames[1].x);
                world.GetOrCreateSystemManaged<SpriteSheetRegistrationSystem>().Update();
                Assert.AreNotEqual(em.GetComponentData<SpriteSheetRegistered>(sheetA).RegistryId,
                    em.GetComponentData<SpriteSheetRegistered>(sheetB).RegistryId);
            }
            finally
            {
                Object.DestroyImmediate(last); Object.DestroyImmediate(first); Object.DestroyImmediate(second);
                Object.DestroyImmediate(atlas); Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void FactoryRejectsMisalignedGridCells()
        {
            using var world = new World("Invalid atlas");
            var texture = new Texture2D(32, 32);
            var sprite = Sprite.Create(texture, new Rect(3, 0, 16, 16), Vector2.one * .5f);
            try
            {
                Assert.Throws<System.ArgumentException>(() => SpriteEntityFactory.Create(world.EntityManager,
                    new[] {sprite}, 10, true, Vector3.zero, 1));
            }
            finally { Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); }
        }

        [Test]
        public void UploadStartsAtBatchBeginningAndInvalidCropCannotReadNextSheet()
        {
            using var world = new World("Atlas batches");
            var em = world.EntityManager;
            var texture = new Texture2D(16, 16);
            bool oldLayout = SpriteBatchSpawner.LayoutXy;
            try
            {
                SpriteBatchSpawner.LayoutXy = true;
                SpriteInstanceRenderSystem.Install(em);
                Entity Sheet(float x)
                {
                    var sheet = em.CreateEntity();
                    em.AddComponentData(sheet, new SpriteSheetDefinition { Cols = 2, Rows = 1, CellAspect = 1, UseCellCrops = 1 });
                    em.AddComponentObject(sheet, new SpriteSheetAsset { Texture = texture });
                    em.AddBuffer<SpriteAnimCellCrop>(sheet).Add(new SpriteAnimCellCrop { Value = new float4(.5f, 1, x, 0) });
                    return sheet;
                }
                var a = Sheet(0);
                var b = Sheet(.5f);
                Entity Sprite(Entity sheet, int slot, float x)
                {
                    var e = em.CreateEntity(typeof(SpriteAnimEnabled));
                    em.AddComponentData(e, LocalTransform.Identity);
                    em.AddComponentData(e, new LocalToWorld { Value = float4x4.Translate(new float3(x, 0, 0)) });
                    em.AddComponentData(e, new SpriteAnimFrame { Slot = slot, Scale = new float2(1) });
                    em.AddComponentData(e, new SpriteFlip());
                    em.AddComponentData(e, new SpriteTint { Value = new float4(1) });
                    em.AddComponentData(e, new SpriteSheetBinding { Sheet = sheet });
                    return e;
                }
                var visible = Sprite(a, 0, 11);
                Sprite(a, 1, 99); // Would read sheet B's first crop from the flattened array.
                Sprite(b, 0, 22);
                Sprite(b, -1, 99);
                Sprite(em.CreateEntity(), 0, 99); // Unregistered explicit binding must not use legacy.
                world.GetOrCreateSystemManaged<SpriteSheetRegistrationSystem>().Update();
                var renderer = world.GetOrCreateSystem<SpriteInstanceRenderSystem>();
                renderer.Update(world.Unmanaged);
                var ra = SpriteSheetRegistry.Records[em.GetComponentData<SpriteSheetRegistered>(a).RegistryId];
                var rb = SpriteSheetRegistry.Records[em.GetComponentData<SpriteSheetRegistered>(b).RegistryId];
                Assert.AreEqual(1, ra.Count); Assert.AreEqual(1, rb.Count);
                var data = new SpriteInstanceData[1];
                ra.Buffer.GetData(data, 0, 0, 1);
                Assert.AreEqual(11, data[0].PosScale.x);
                Assert.AreEqual(0, data[0].CropST.z);
                rb.Buffer.GetData(data, 0, 0, 1);
                Assert.AreEqual(22, data[0].PosScale.x);
                Assert.AreEqual(.5f, data[0].CropST.z);

                em.SetComponentData(visible, new LocalToWorld { Value = float4x4.TRS(new float3(10, 0, 0),
                    quaternion.RotateZ(math.PI / 2), new float3(2)) });
                em.SetComponentData(visible, new SpriteAnimFrame { Slot = 0, Offset = new float2(1, 0), Scale = new float2(1) });
                renderer.Update(world.Unmanaged);
                ra.Buffer.GetData(data, 0, 0, 1);
                Assert.That(data[0].PosScale.x, Is.EqualTo(10).Within(.0001));
                Assert.That(data[0].PosScale.y, Is.EqualTo(2).Within(.0001));
            }
            finally { SpriteBatchSpawner.LayoutXy = oldLayout; Object.DestroyImmediate(texture); }
        }
    }
}
