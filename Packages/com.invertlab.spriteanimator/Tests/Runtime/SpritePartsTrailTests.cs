using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Afterimage trails: ghosts where the parts were, behind them, fading and removed.</summary>
    public sealed class SpritePartsTrailTests
    {
        [Test]
        public void Trails_Leave_Fading_Ghosts_Behind_The_Character()
        {
            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput { Name = "a", SlotId = "a", RestScale = new float2(1f, 1f) },
                new SpritePartsSetBuilder.SlotInput { Name = "b", SlotId = "b", RestScale = new float2(1f, 1f), DrawRank = 1 },
                new SpritePartsSetBuilder.SlotInput { Name = "hidden", SlotId = "hidden", RestScale = new float2(1f, 1f), Hidden = 1 },
            };
            var blob = SpritePartsSetBuilder.Build(Allocator.Temp, slots, System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                System.Array.Empty<SpritePartsSetBuilder.ClipInput>(), System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
            using var world = new World("parts-trails");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, float3.zero, playing: false);
            try
            {
                em.SetComponentData(created.Parts[0], new LocalToWorld { Value = float4x4.Translate(new float3(5f, 1f, 0f)) });
                var ghosts = em.CreateEntityQuery(typeof(SpritePartsGhost));
                var system = world.GetOrCreateSystem<SpritePartsTrailSystem>();
                double time = 0;
                void Tick(float dt)
                {
                    time += dt;
                    world.SetTime(new Unity.Core.TimeData(time, dt));
                    system.Update(world.Unmanaged);
                }

                Assert.IsTrue(SpriteParts.StartTrail(em, created.Root, interval: 0.05f, life: 0.2f, seconds: 0.12f));
                Tick(0.01f);
                Assert.AreEqual(2, ghosts.CalculateEntityCount(), "One ghost per visible part (not the hidden one).");
                using (var list = ghosts.ToEntityArray(Allocator.Temp))
                {
                    bool atPart = false;
                    foreach (var g in list)
                    {
                        atPart |= math.all(em.GetComponentData<LocalToWorld>(g).Value.c3.xy == new float2(5f, 1f));
                        Assert.Greater(em.GetComponentData<SpritePartRenderDepth>(g).Value, 0f, "Behind the character.");
                    }
                    Assert.IsTrue(atPart, "Frozen where the part was drawn.");
                }
                Tick(0.05f);
                Assert.AreEqual(4, ghosts.CalculateEntityCount(), "The next interval adds more.");
                Tick(0.1f);
                Assert.IsFalse(em.HasComponent<SpritePartsTrail>(created.Root), "The trail ends after its seconds.");
                Tick(0.3f);
                Assert.AreEqual(0, ghosts.CalculateEntityCount(), "Faded out and removed.");
            }
            finally
            {
                created.Parts.Dispose();
                blob.Dispose();
            }
        }
    }
}
