using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Point and bounding-box parts: world pose and hit tests.</summary>
    public sealed class SpritePartsAttachmentTests
    {
        [Test]
        public void Points_And_Boxes_Follow_The_Character()
        {
            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput { Name = "Body", SlotId = "body", RestPosition = new float2(1f, 0f), RestRotation = 90f, RestScale = new float2(1f, 1f) },
                new SpritePartsSetBuilder.SlotInput { Name = "Muzzle", SlotId = "muzzle", ParentSlotId = "body", RestPosition = new float2(2f, 0f), RestScale = new float2(1f, 1f), Hidden = 1 },
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Hurt", SlotId = "hurt", RestScale = new float2(1f, 1f), Hidden = 1, IsBoundingBox = true,
                    ClipPolygon = new[] { new float2(-1f, -1f), new float2(1f, -1f), new float2(1f, 1f), new float2(-1f, 1f) },
                },
            };
            var blob = SpritePartsSetBuilder.Build(Allocator.Temp, slots, System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                System.Array.Empty<SpritePartsSetBuilder.ClipInput>(), System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
            using var world = new World("parts-attachments");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, new float3(10f, 0f, 0f), playing: false);
            try
            {
                SpritePartsPoseWriter.Apply(em, created.Root);
                Assert.IsTrue(SpriteParts.TryGetPoint(em, created.Root, "Muzzle", out float2 pos, out float angle));
                Assert.AreEqual(11f, pos.x, 1e-4f, "Body at x 11, turned 90: the muzzle is 2 up from it.");
                Assert.AreEqual(2f, pos.y, 1e-4f);
                Assert.AreEqual(90f, angle, 1e-3f);
                Assert.IsTrue(SpriteParts.BoundingBoxContains(em, created.Root, "Hurt", new float2(10.5f, 0.5f)));
                Assert.IsFalse(SpriteParts.BoundingBoxContains(em, created.Root, "Hurt", new float2(12f, 0f)));
                Assert.IsFalse(SpriteParts.BoundingBoxContains(em, created.Root, "Body", new float2(10.5f, 0.5f)), "Not a box.");
                SpriteParts.SetFacing(em, created.Root, true);
                SpritePartsPoseWriter.Apply(em, created.Root);
                Assert.IsTrue(SpriteParts.TryGetPoint(em, created.Root, "muzzle", out pos, out _), "By id too.");
                Assert.AreEqual(9f, pos.x, 1e-4f, "Facing left mirrors it.");
            }
            finally
            {
                created.Parts.Dispose();
                blob.Dispose();
            }
        }
    }
}
