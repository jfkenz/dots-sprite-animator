using NUnit.Framework;
using Unity.Collections;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteAnimEventBakeTests
    {
        [Test]
        public void EnsureEventMarkersMigratesLegacyEventIds()
        {
            var clip = new SpriteClipDef
            {
                Frames = new[] { 0, 1, 2 },
                EventIds = new byte[] { 0, 3, 0 },
                EventNormalizedTimes = new[] { 0f, 0.25f, 0f },
            };

            clip.EnsureFrameData();

            Assert.AreEqual(1, clip.EventMarkers.Count);
            Assert.AreEqual(1, clip.EventMarkers[0].FrameIndex);
            Assert.AreEqual(3, clip.EventMarkers[0].EventId);
            Assert.AreEqual(0.25f, clip.EventMarkers[0].NormalizedTime, 0.0001f);
            Assert.AreEqual(3, clip.EventIds[1]);
        }

        [Test]
        public void TwoMarkersOnOneFrameKeepFirstInLegacyProjection()
        {
            var clip = new SpriteClipDef { Frames = new[] { 0, 1 } };
            clip.EnsureFrameData();
            clip.AddEventMarker(0, 1, 0.1f);
            clip.AddEventMarker(0, 7, 0.8f);

            Assert.AreEqual(2, clip.EventMarkers.Count);
            Assert.AreEqual(1, clip.EventIds[0]);
            Assert.AreEqual(0.1f, clip.EventNormalizedTimes[0], 0.0001f);
            Assert.AreEqual(7, clip.EventMarkers[1].EventId);
        }

        [Test]
        public void BakesPayloadOnceAndMultipleKeysOnOneFrame()
        {
            var (setRef, _) = SpriteAnimSetBuilder.Build(Allocator.Temp, new[]
            {
                new SpriteAnimSetBuilder.ClipInput
                {
                    Name = "Attack",
                    FrameRate = 8f,
                    GlobalFrameIndices = new[] { 0, 1, 2 },
                    EventKeys = new[]
                    {
                        new SpriteAnimSetBuilder.ClipInput.EventKeyInput
                        {
                            FrameIndex = 1,
                            NormalizedTime = 0.2f,
                            EventId = 1,
                            FireMode = (byte)SpriteEventFireMode.Loop,
                            IntPayload = 4,
                        },
                        new SpriteAnimSetBuilder.ClipInput.EventKeyInput
                        {
                            FrameIndex = 1,
                            NormalizedTime = 0.75f,
                            EventId = 2,
                            FireMode = (byte)SpriteEventFireMode.Once,
                            FloatPayload = 1.5f,
                            TextPayload = "slash",
                        },
                    },
                },
            });

            try
            {
                ref var def = ref setRef.Set.Value.Clips[0];
                Assert.AreEqual(2, def.EventKeys.Length);
                Assert.AreEqual(1, def.EventKeys[0].EventId);
                Assert.AreEqual(4, def.EventKeys[0].IntPayload);
                Assert.AreEqual((byte)SpriteEventFireMode.Loop, def.EventKeys[0].FireMode);
                Assert.AreEqual(2, def.EventKeys[1].EventId);
                Assert.AreEqual((byte)SpriteEventFireMode.Once, def.EventKeys[1].FireMode);
                Assert.AreEqual(1.5f, def.EventKeys[1].FloatPayload, 0.0001f);
                Assert.AreEqual(SpriteAnimSetBuilder.Fnv("slash"), def.EventKeys[1].TextHash);
            }
            finally
            {
                if (setRef.Set.IsCreated)
                    setRef.Set.Dispose();
            }
        }

        [Test]
        public void EventMarkersMakeClipCpuOnly()
        {
            var clip = new SpriteClipDef
            {
                Frames = new[] { 0, 1, 2, 3 },
                WrapMode = SpriteAnimWrap.Loop,
            };
            clip.EnsureFrameData();
            Assert.IsTrue(SpriteGpuEligibility.IsGpuEligible(clip, out _));

            clip.AddEventMarker(2, 1, 0f);
            Assert.IsFalse(SpriteGpuEligibility.IsGpuEligible(clip, out string reason));
            StringAssert.Contains("events", reason);
        }
    }
}
