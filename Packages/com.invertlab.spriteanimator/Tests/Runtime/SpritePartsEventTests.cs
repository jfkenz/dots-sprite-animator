using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Parts clip events: which a tick passes, loop wrap, and firing into the event buffer.</summary>
    public sealed class SpritePartsEventTests
    {
        static BlobAssetReference<SpritePartsSetBlob> Build(byte wrap)
        {
            var slots = new[] { new SpritePartsSetBuilder.SlotInput { Name = "body", SlotId = "body", RestScale = new float2(1f, 1f) } };
            var clips = new[]
            {
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "Walk", ClipId = "walk", Duration = 1f, SpeedMultiplier = 1f, WrapMode = wrap,
                    Tracks = System.Array.Empty<SpritePartsSetBuilder.TrackInput>(),
                    Events = new[]
                    {
                        new SpritePartsSetBuilder.EventInput { Time = 0.5f, Id = 3, IntPayload = 7 },
                        new SpritePartsSetBuilder.EventInput { Time = 0f, Id = 1 },
                        new SpritePartsSetBuilder.EventInput { Time = 1f, Id = 2, TextPayload = "land" },
                        new SpritePartsSetBuilder.EventInput { Time = 0.25f, Id = 0 }, // id 0: dropped
                        new SpritePartsSetBuilder.EventInput { Time = 5f, Id = 4 },    // clamped to the end
                    },
                },
            };
            return SpritePartsSetBuilder.Build(Allocator.Temp, slots, System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                clips, System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
        }

        static int[] Ids(BlobAssetReference<SpritePartsSetBlob> blob, float from, float to)
        {
            var hits = new NativeList<int>(4, Allocator.Temp);
            try
            {
                ref var clip = ref blob.Value.Clips[0];
                SpritePartsEventFiring.Collect(ref clip, from, to, hits);
                var ids = new int[hits.Length];
                for (int i = 0; i < hits.Length; i++)
                    ids[i] = clip.Events[hits[i]].Id;
                return ids;
            }
            finally
            {
                hits.Dispose();
            }
        }

        [Test]
        public void Events_Are_Sorted_Clamped_And_Id_Zero_Dropped()
        {
            var blob = Build((byte)SpritePartsWrap.Loop);
            try
            {
                ref var clip = ref blob.Value.Clips[0];
                Assert.AreEqual(4, clip.Events.Length);
                Assert.AreEqual(0f, clip.Events[0].Time);
                Assert.AreEqual(1f, clip.Events[3].Time, "Past the end is clamped to it.");
                ulong land = 0;
                for (int i = 0; i < clip.Events.Length; i++)
                    if (clip.Events[i].Id == 2) land = clip.Events[i].TextHash;
                Assert.AreEqual(SpriteAnimSetBuilder.Fnv("land"), land, "Text goes as a hash.");
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void A_Tick_Fires_What_It_Passes_Once_And_Wraps_On_Loops()
        {
            var loop = Build((byte)SpritePartsWrap.Loop);
            var once = Build((byte)SpritePartsWrap.Once);
            try
            {
                CollectionAssert.AreEqual(new[] { 1 }, Ids(loop, 0f, 0.3f), "The start includes 0.");
                CollectionAssert.AreEqual(new[] { 3 }, Ids(loop, 0.3f, 0.5f), "The end is included.");
                CollectionAssert.IsEmpty(Ids(loop, 0.5f, 0.6f), "Not twice.");
                CollectionAssert.AreEquivalent(new[] { 2, 4, 1 }, Ids(loop, 0.9f, 0.1f), "A loop wrap fires the end and the start.");
                CollectionAssert.IsEmpty(Ids(once, 0.9f, 0.1f), "Once clips do not wrap.");
                CollectionAssert.IsEmpty(Ids(loop, 0.4f, 0.4f), "Paused: nothing.");
            }
            finally
            {
                loop.Dispose();
                once.Dispose();
            }
        }

        [Test]
        public void Fire_Writes_The_Event_Buffer_And_Pending()
        {
            var world = new World("parts-events");
            var blob = Build((byte)SpritePartsWrap.Loop);
            try
            {
                var em = world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new SpritePartsSetRef { Set = blob });
                SpritePartsEventFiring.Fire(em, new SpritePartsEventFiring.Tick { Entity = e, ClipIndex = 0, From = 0.3f, To = 0.6f, Playing = true });
                var buffer = em.GetBuffer<SpriteAnimEventBuffer>(e);
                Assert.AreEqual(1, buffer.Length);
                Assert.AreEqual(3, buffer[0].Id);
                Assert.AreEqual(7, buffer[0].IntPayload);
                Assert.AreEqual(-1, buffer[0].FrameIndex, "Parts events carry no frame.");
                Assert.IsTrue(em.IsComponentEnabled<SpriteAnimEventsPending>(e));
            }
            finally
            {
                world.Dispose();
                blob.Dispose();
            }
        }
    }
}
