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
    }
}
