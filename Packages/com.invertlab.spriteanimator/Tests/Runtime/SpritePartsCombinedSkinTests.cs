using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Combined skins: several active at once, in order; removing one restores the rest.</summary>
    public sealed class SpritePartsCombinedSkinTests
    {
        static SpritePartsSetBuilder.AppearanceInput Art(string id, int cell) => new SpritePartsSetBuilder.AppearanceInput
        {
            AppearanceId = id, CellIndex = cell, LogicalWorldSize = new float2(1f, 1f), Pivot = new float2(0.5f, 0.5f), FrameScale = new float2(1f, 1f),
        };

        static SpritePartsSetBuilder.SkinInput Skin(string id, params (string slot, string app)[] bindings)
        {
            var b = new SpritePartsSetBuilder.SkinBindingInput[bindings.Length];
            for (int i = 0; i < bindings.Length; i++)
                b[i] = new SpritePartsSetBuilder.SkinBindingInput { SlotId = bindings[i].slot, AppearanceId = bindings[i].app };
            return new SpritePartsSetBuilder.SkinInput { SkinId = id, Bindings = b };
        }

        [Test]
        public void Skins_Stack_And_Removing_One_Restores_The_Others()
        {
            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput { Name = "hat", SlotId = "hat", RestScale = new float2(1f, 1f), DefaultAppearanceId = "plain" },
                new SpritePartsSetBuilder.SlotInput { Name = "coat", SlotId = "coat", RestScale = new float2(1f, 1f), DefaultAppearanceId = "plain", DrawRank = 1 },
            };
            var blob = SpritePartsSetBuilder.Build(Allocator.Temp, slots,
                new[] { Art("plain", 0), Art("red", 1), Art("blue", 2), Art("gold", 3) }, System.Array.Empty<SpritePartsSetBuilder.ClipInput>(),
                new[] { Skin("red", ("hat", "red")), Skin("blue", ("coat", "blue"), ("hat", "blue")), Skin("gold", ("hat", "gold")) });
            using var world = new World("parts-skins");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, float3.zero, playing: false);
            try
            {
                int App(int part) => em.GetComponentData<SpritePartAppearanceState>(created.Parts[part]).AppearanceIndex;
                Assert.IsTrue(SpriteParts.SetSkins(em, created.Root, "blue", "red"));
                Assert.AreEqual(2, SpriteParts.ActiveSkinCount(em, created.Root));
                Assert.AreEqual(1, App(0), "Red is on top: its hat.");
                Assert.AreEqual(2, App(1), "Blue's coat stays.");
                Assert.IsTrue(SpriteParts.AddSkin(em, created.Root, "gold"));
                Assert.AreEqual(3, App(0));
                Assert.IsTrue(SpriteParts.RemoveSkin(em, created.Root, "gold"));
                Assert.IsTrue(SpriteParts.RemoveSkin(em, created.Root, "red"));
                Assert.AreEqual(2, App(0), "Back to blue's hat, not the default.");
                Assert.IsTrue(SpriteParts.ApplySkin(em, created.Root, "red"));
                Assert.AreEqual(1, SpriteParts.ActiveSkinCount(em, created.Root), "ApplySkin replaces the list.");
            }
            finally
            {
                created.Parts.Dispose();
                blob.Dispose();
            }
        }
    }
}
