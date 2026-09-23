using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Dopesheet tools: scale key timing, offset parts, stepped preview sampling.</summary>
    public sealed class SpritePartsDopesheetTests
    {
        static SpriteSheetProfile Profile(byte wrap)
        {
            var profile = new SpriteSheetProfile();
            profile.PartsSlots.Add(new SpritePartSlotDef { SlotId = "a", Name = "A" });
            profile.PartsSlots.Add(new SpritePartSlotDef { SlotId = "b", Name = "B" });
            profile.PartsSlots.Add(new SpritePartSlotDef { SlotId = "c", Name = "C" });
            var clip = new SpritePartsClipDef { Name = "Clip", ClipId = "clip", Duration = 1f, WrapMode = wrap };
            foreach (var id in new[] { "a", "b", "c" })
            {
                clip.Tracks.Add(new SpritePartsTrackDef
                {
                    SlotId = id,
                    Keys = new List<SpritePartsKeyDef>
                    {
                        new SpritePartsKeyDef { Time = 0f }, new SpritePartsKeyDef { Time = 0.5f },
                    },
                });
            }
            profile.PartsClips.Add(clip);
            return profile;
        }

        static List<SpritePartsKeyDef> AllKeys(SpriteSheetProfile p)
        {
            var list = new List<SpritePartsKeyDef>();
            foreach (var t in p.PartsClips[0].Tracks)
                list.AddRange(t.Keys);
            return list;
        }

        [Test]
        public void Scale_Stretches_Around_The_Pivot()
        {
            var p = Profile((byte)SpritePartsWrap.Loop);
            var keys = p.PartsClips[0].Tracks[0].Keys;
            var r = SpritePartsAuthoringOps.ScaleKeyTimes(p, 0, new List<SpritePartsKeyDef>(keys), 0f, 1.5f, 30f);
            Assert.IsTrue(r.Ok);
            Assert.AreEqual(0f, keys[0].Time, 1e-4f, "The pivot stays.");
            Assert.AreEqual(0.75f, keys[1].Time, 0.02f);
            SpritePartsAuthoringOps.ScaleKeyTimes(p, 0, new List<SpritePartsKeyDef>(keys), 0f, 5f, 30f);
            Assert.AreEqual(1f, keys[1].Time, 1e-4f, "Kept inside the clip.");
        }

        [Test]
        public void Offset_Staggers_Each_Part_And_Wraps_On_Loops()
        {
            var p = Profile((byte)SpritePartsWrap.Loop);
            var order = new List<string> { "a", "b", "c" };
            var r = SpritePartsAuthoringOps.OffsetKeysByPart(p, 0, AllKeys(p), order, 0.3f, 10f);
            Assert.IsTrue(r.Ok);
            var tracks = p.PartsClips[0].Tracks;
            Assert.AreEqual(0f, tracks[0].Keys[0].Time, 1e-4f, "The first part stays.");
            Assert.AreEqual(0.3f, tracks[1].Keys[0].Time, 1e-4f);
            Assert.AreEqual(0.8f, tracks[1].Keys[1].Time, 1e-4f);
            // c: 0 + 0.6 = 0.6, and 0.5 + 0.6 = 1.1 wraps to 0.1 (the track is re-sorted).
            Assert.IsTrue(tracks[2].Keys.Exists(k => Mathf.Abs(k.Time - 0.6f) < 1e-4f));
            Assert.IsTrue(tracks[2].Keys.Exists(k => Mathf.Abs(k.Time - 0.1f) < 1e-4f), "Keys past the end wrap around on loop clips.");
        }

        [Test]
        public void Stepped_Sampling_Holds_Key_Poses()
        {
            var slots = new[] { new SpritePartsSetBuilder.SlotInput { Name = "a", SlotId = "a", RestScale = new float2(1f, 1f) } };
            var clips = new[]
            {
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "c", ClipId = "c", Duration = 1f, SpeedMultiplier = 1f,
                    Tracks = new[]
                    {
                        new SpritePartsSetBuilder.TrackInput
                        {
                            SlotId = "a",
                            Keys = new[]
                            {
                                new SpritePartsSetBuilder.KeyInput { Time = 0f, Scale = new float2(1f, 1f), AppearanceId = string.Empty },
                                new SpritePartsSetBuilder.KeyInput { Time = 1f, Position = new float2(10f, 0f), Scale = new float2(1f, 1f), AppearanceId = string.Empty },
                            },
                        },
                    },
                },
            };
            var blob = SpritePartsSetBuilder.Build(Allocator.Temp, slots, System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                clips, System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
            var local = new NativeArray<SpritePartsSampler.Pose>(1, Allocator.Temp);
            var ltr = new NativeArray<float4x4>(1, Allocator.Temp);
            try
            {
                SpritePartsPoseWriter.EvaluateEditor(ref blob.Value, 0, 0.9f, local, ltr, new SpritePartsEvalExtras { Stepped = true });
                Assert.AreEqual(0f, local[0].Position.x, 1e-5f, "Stepped holds the earlier key.");
                SpritePartsPoseWriter.EvaluateEditor(ref blob.Value, 0, 0.9f, local, ltr, default);
                Assert.AreEqual(9f, local[0].Position.x, 1e-4f);
            }
            finally
            {
                local.Dispose();
                ltr.Dispose();
                blob.Dispose();
            }
        }
    }
}
