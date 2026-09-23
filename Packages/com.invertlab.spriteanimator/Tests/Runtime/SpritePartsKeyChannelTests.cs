using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Colour keys, draw-order keys and Bezier-curve keys on Parts clips.</summary>
    public sealed class SpritePartsKeyChannelTests
    {
        static SpritePartsSetBuilder.KeyInput Key(float time, float x = 0f)
            => new SpritePartsSetBuilder.KeyInput
            {
                Time = time, Position = new float2(x, 0f), Scale = new float2(1f, 1f),
                EaseMode = (byte)SpriteEaseMode.Linear,
            };

        static BlobAssetReference<SpritePartsSetBlob> Build(SpritePartsSetBuilder.KeyInput[] keys, byte wrap = (byte)SpritePartsWrap.Loop)
        {
            var appearances = new[]
            {
                new SpritePartsSetBuilder.AppearanceInput
                {
                    AppearanceId = "art", SheetTableIndex = 0, CellIndex = 0,
                    LogicalWorldSize = new float2(1f, 1f), Pivot = new float2(0.5f, 0.5f),
                    FrameOffset = float2.zero, FrameScale = new float2(1f, 1f),
                },
            };
            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Body", SlotId = "body", RestScale = new float2(1f, 1f), DefaultAppearanceId = "art", DrawRank = 3,
                },
            };
            var clips = new[]
            {
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "Clip", ClipId = "clip", Duration = 1f, SpeedMultiplier = 1f, WrapMode = wrap,
                    Tracks = new[] { new SpritePartsSetBuilder.TrackInput { SlotId = "body", Keys = keys } },
                },
            };
            return SpritePartsSetBuilder.Build(Allocator.Temp, slots, appearances, clips,
                System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
        }

        [Test]
        public void ColourKeys_Blend_With_Each_Other_Only()
        {
            var k0 = Key(0f);
            k0.HasColor = true;
            k0.Color = new float4(1f, 0f, 0f, 1f);
            var k1 = Key(0.5f); // no colour: skipped for colour
            var k2 = Key(1f);
            k2.HasColor = true;
            k2.Color = new float4(0f, 0f, 1f, 0f);
            var blob = Build(new[] { k0, k1, k2 }, (byte)SpritePartsWrap.Once);
            try
            {
                Assert.IsTrue(SpritePartsSampler.SampleColor(ref blob.Value, 0, 0, 0.25f, out var c));
                Assert.AreEqual(0.75f, c.x, 1e-4f);
                Assert.AreEqual(0.25f, c.z, 1e-4f);
                Assert.AreEqual(0.75f, c.w, 1e-4f, "Alpha blends too (fade).");
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void No_Colour_Keys_Means_White()
        {
            var blob = Build(new[] { Key(0f), Key(1f, 2f) });
            try
            {
                Assert.IsFalse(SpritePartsSampler.SampleColor(ref blob.Value, 0, 0, 0.5f, out var c));
                Assert.AreEqual(new float4(1f), c);
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void DrawOrder_Keys_Are_Held_And_Loop_Carries()
        {
            var k0 = Key(0f);
            var k1 = Key(0.4f);
            k1.HasDrawOrder = true;
            k1.DrawOrder = 0;
            var k2 = Key(0.8f);
            k2.HasDrawOrder = true;
            k2.DrawOrder = 7;
            var blob = Build(new[] { k0, k1, k2 });
            try
            {
                Assert.AreEqual(0, SpritePartsSampler.SampleDrawOrder(ref blob.Value, 0, 0, 0.5f));
                Assert.AreEqual(7, SpritePartsSampler.SampleDrawOrder(ref blob.Value, 0, 0, 0.9f));
                Assert.AreEqual(7, SpritePartsSampler.SampleDrawOrder(ref blob.Value, 0, 0, 0.1f), "Loop: the last key carries over.");
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Bezier_Key_Uses_Its_Own_Curve()
        {
            var k0 = Key(0f, 0f);
            k0.EaseMode = (byte)SpriteEaseMode.Bezier;
            k0.Curve = new float4(0f, 1f, 0f, 1f); // shoots up fast, then flattens
            var k1 = Key(1f, 10f);
            var blob = Build(new[] { k0, k1 }, (byte)SpritePartsWrap.Once);
            try
            {
                SpritePartsSampler.SampleSlot(ref blob.Value, 0, 0, 0.25f, out var pose);
                float expected = SpriteEase.EvaluateBezier(k0.Curve, 0.25f) * 10f;
                Assert.AreEqual(expected, pose.Position.x, 1e-3f);
                Assert.Greater(pose.Position.x, 2.5f + 2f, "Faster than linear early on.");
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Bezier_Linear_Handles_Are_Linear()
        {
            var lin = new float4(1f / 3f, 1f / 3f, 2f / 3f, 2f / 3f);
            for (float t = 0f; t <= 1f; t += 0.125f)
                Assert.AreEqual(t, SpriteEase.EvaluateBezier(lin, t), 1e-4f);
        }

        // ------------------------------------------------------------------ separate channels

        const byte OnlyRotation = (byte)(SpritePartsKeyChannel.All & ~SpritePartsKeyChannel.Rotation);
        const byte OnlyPosition = (byte)(SpritePartsKeyChannel.All & ~SpritePartsKeyChannel.Position);

        [Test]
        public void Each_Channel_Blends_Only_With_Keys_That_Hold_It()
        {
            var k0 = Key(0f, 0f);                    // all channels, rotation 0
            var k1 = Key(0.5f, 999f);                // rotation only (its position is ignored)
            k1.Rotation = 90f;
            k1.SkipChannels = OnlyRotation;
            var k2 = Key(1f, 10f);                   // position only
            k2.SkipChannels = OnlyPosition;
            k2.Rotation = -500f;                     // ignored
            var blob = Build(new[] { k0, k1, k2 }, (byte)SpritePartsWrap.Once);
            try
            {
                SpritePartsSampler.SampleSlot(ref blob.Value, 0, 0, 0.25f, out var a);
                Assert.AreEqual(2.5f, a.Position.x, 1e-4f, "The rotation key does not pin the position.");
                Assert.AreEqual(45f, a.Rotation, 1e-3f);
                SpritePartsSampler.SampleSlot(ref blob.Value, 0, 0, 0.75f, out var b);
                Assert.AreEqual(7.5f, b.Position.x, 1e-4f);
                Assert.AreEqual(90f, b.Rotation, 1e-3f, "Rotation holds after its last key.");
            }
            finally
            {
                blob.Dispose();
            }
        }

        static SpriteSheetProfile ProfileWithClip()
        {
            var profile = new SpriteSheetProfile();
            profile.EnsurePartsRig();
            profile.PartsClips.Clear();
            profile.PartsClips.Add(new SpritePartsClipDef { Name = "Clip", ClipId = "clip", Duration = 1f });
            profile.PartsSlots.Add(new SpritePartSlotDef { SlotId = "arm", Name = "Arm", RestPosition = new UnityEngine.Vector2(1f, 2f), RestRotation = 10f });
            return profile;
        }

        [Test]
        public void Keying_One_Channel_Leaves_The_Others_Free_And_Anchors_It_At_Rest()
        {
            var profile = ProfileWithClip();
            var pose = new SpritePartsAuthoringOps.PoseEdit
            {
                Position = new UnityEngine.Vector2(5f, 5f), Rotation = 45f, Scale = UnityEngine.Vector2.one,
            };
            var r = SpritePartsAuthoringOps.ApplyPoseEdit(profile, SpritePartsStudioMode.Animate, 0, "arm", 0.5f, pose, true,
                30f, SpritePartsKeyChannel.Rotation);
            Assert.IsTrue(r.WroteKey);
            Assert.IsTrue(r.InsertedRestAnchorAtZero);
            var track = SpritePartsAuthoringOps.FindTrack(profile.PartsClips[0], "arm");
            Assert.AreEqual(2, track.Keys.Count);
            Assert.AreEqual(SpritePartsKeyChannel.Rotation, track.Keys[0].Channels, "The anchor at 0 holds rotation only.");
            Assert.AreEqual(10f, track.Keys[0].Rotation, 1e-5f);
            Assert.AreEqual(SpritePartsKeyChannel.Rotation, track.Keys[1].Channels);
            Assert.AreEqual(45f, track.Keys[1].Rotation, 1e-5f);

            // A later move at the same time adds Position to that key only.
            SpritePartsAuthoringOps.ApplyPoseEdit(profile, SpritePartsStudioMode.Animate, 0, "arm", 0.5f, pose, true,
                30f, SpritePartsKeyChannel.Position);
            Assert.AreEqual(SpritePartsKeyChannel.Rotation | SpritePartsKeyChannel.Position, track.Keys[1].Channels);
            Assert.AreEqual(SpritePartsKeyChannel.Rotation | SpritePartsKeyChannel.Position, track.Keys[0].Channels,
                "Position gets its own rest anchor at 0.");
            Assert.AreEqual(new UnityEngine.Vector2(1f, 2f), track.Keys[0].Position);
        }

        [Test]
        public void Split_Move_And_Merge_A_Channel()
        {
            var profile = ProfileWithClip();
            var pose = new SpritePartsAuthoringOps.PoseEdit { Position = new UnityEngine.Vector2(3f, 0f), Rotation = 30f, Scale = UnityEngine.Vector2.one };
            SpritePartsAuthoringOps.ApplyPoseEdit(profile, SpritePartsStudioMode.Animate, 0, "arm", 0f, pose, true);
            var track = SpritePartsAuthoringOps.FindTrack(profile.PartsClips[0], "arm");
            var key = track.Keys[0];
            Assert.AreEqual(SpritePartsKeyChannel.All, key.Channels);

            var moving = SpritePartsAuthoringOps.SplitKeyChannels(profile, 0, new[] { key }, SpritePartsKeyChannel.Rotation);
            Assert.AreEqual(1, moving.Count);
            Assert.AreEqual(2, track.Keys.Count);
            Assert.AreEqual(SpritePartsKeyChannel.All & ~SpritePartsKeyChannel.Rotation, key.Channels);
            SpritePartsAuthoringOps.MoveKeys(profile, 0, moving, new[] { 0f }, 0.2f, 30f, true, merge: false);
            SpritePartsAuthoringOps.MergeKeyCollisions(profile, 0, moving);
            Assert.AreEqual(2, track.Keys.Count, "The rotation now has its own time.");
            Assert.AreEqual(0.2f, moving[0].Time, 1e-4f);

            // Moved back onto the first key: they merge into one again.
            SpritePartsAuthoringOps.MoveKeys(profile, 0, moving, new[] { 0.2f }, -0.2f, 30f, true, merge: false);
            SpritePartsAuthoringOps.MergeKeyCollisions(profile, 0, moving);
            Assert.AreEqual(1, track.Keys.Count);
            Assert.AreEqual(SpritePartsKeyChannel.All, track.Keys[0].Channels);

            int changed = SpritePartsAuthoringOps.RemoveKeyChannels(profile, 0,
                new System.Collections.Generic.HashSet<SpritePartsKeyDef>(track.Keys), SpritePartsKeyChannel.All);
            Assert.AreEqual(1, changed);
            Assert.AreEqual(0, track.Keys.Count, "A key left with nothing is removed.");
        }
    }
}
