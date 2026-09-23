using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Control parameters: a value scrubs its clip, added on top of (or replacing) the playing pose.</summary>
    public sealed class SpritePartsParamsTests
    {
        static SpritePartsSetBuilder.KeyInput Key(float t, float2 pos, float rot = 0f)
            => new SpritePartsSetBuilder.KeyInput
            {
                Time = t, Position = pos, Rotation = rot, Scale = new float2(1f, 1f), AppearanceId = string.Empty,
            };

        static SpritePartsSetBuilder.ClipInput Clip(string id, params SpritePartsSetBuilder.KeyInput[] mouthKeys)
            => new SpritePartsSetBuilder.ClipInput
            {
                Name = id, ClipId = id, Duration = 1f, SpeedMultiplier = 1f,
                Tracks = mouthKeys.Length == 0
                    ? System.Array.Empty<SpritePartsSetBuilder.TrackInput>()
                    : new[] { new SpritePartsSetBuilder.TrackInput { SlotId = "mouth", Keys = mouthKeys } },
            };

        // Clip 0 "idle": the mouth sits at (2, 0). Clip 1 "open": (0, 0) at the start, (0, -1) turned 30 at the end.
        static BlobAssetReference<SpritePartsSetBlob> Face(bool additive)
        {
            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput { Name = "head", SlotId = "head", RestScale = new float2(1f, 1f) },
                new SpritePartsSetBuilder.SlotInput { Name = "mouth", SlotId = "mouth", ParentSlotId = "head", RestScale = new float2(1f, 1f) },
            };
            var clips = new[]
            {
                Clip("idle", Key(0f, new float2(2f, 0f))),
                Clip("open", Key(0f, float2.zero), Key(1f, new float2(0f, -1f), 30f)),
            };
            var parameters = new[]
            {
                new SpritePartsSetBuilder.ParamInput { Name = "Mouth", ClipId = "open", Min = 0f, Max = 1f, Default = 0f, Additive = additive },
            };
            return SpritePartsSetBuilder.Build(Allocator.Temp, slots, System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                clips, System.Array.Empty<SpritePartsSetBuilder.SkinInput>(), null, null, parameters);
        }

        static SpritePartsSampler.Pose Mouth(BlobAssetReference<SpritePartsSetBlob> blob, int clip, float value, bool keyed = false)
        {
            var values = new NativeArray<float>(1, Allocator.Temp);
            var local = new NativeArray<SpritePartsSampler.Pose>(2, Allocator.Temp);
            var ltr = new NativeArray<float4x4>(2, Allocator.Temp);
            try
            {
                values[0] = value;
                SpritePartsPoseWriter.EvaluateEditor(ref blob.Value, clip, 0.25f, local, ltr,
                    new SpritePartsEvalExtras { ParamValues = values, KeyedPoseOnly = keyed });
                return local[1];
            }
            finally
            {
                values.Dispose();
                local.Dispose();
                ltr.Dispose();
            }
        }

        [Test]
        public void Additive_Adds_The_Change_From_Default_On_Top_Of_The_Playing_Clip()
        {
            var blob = Face(true);
            try
            {
                Assert.AreEqual(1, blob.Value.Params[0].ClipIndex);
                var rest = Mouth(blob, 0, 0f);
                Assert.AreEqual(new float2(2f, 0f), rest.Position, "The default value changes nothing.");
                var half = Mouth(blob, 0, 0.5f);
                Assert.AreEqual(2f, half.Position.x, 1e-4f, "Idle still places the mouth.");
                Assert.AreEqual(-0.5f, half.Position.y, 1e-4f, "Half open.");
                Assert.AreEqual(15f, half.Rotation, 1e-3f);
                Assert.AreEqual(0f, Mouth(blob, 0, 1f, keyed: true).Position.y, 1e-5f, "Keyed poses leave parameters out.");
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Replace_Takes_The_Clip_Pose_And_Is_Skipped_When_Its_Clip_Plays()
        {
            var blob = Face(false);
            try
            {
                var open = Mouth(blob, 0, 1f);
                Assert.AreEqual(0f, open.Position.x, 1e-4f, "Replace drops the idle offset.");
                Assert.AreEqual(-1f, open.Position.y, 1e-4f);
                var own = Mouth(blob, 1, 1f);
                Assert.AreEqual(-0.25f, own.Position.y, 1e-4f, "Playing the parameter's own clip shows the clip at its time.");
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Values_Default_Clamp_And_Resolve_By_Name()
        {
            var blob = Face(true);
            try
            {
                Assert.AreEqual(0, SpritePartsParams.Find(ref blob.Value, new FixedString64Bytes("Mouth")));
                Assert.AreEqual(-1, SpritePartsParams.Find(ref blob.Value, new FixedString64Bytes("Eyes")));
                var over = Mouth(blob, 0, 5f);
                Assert.AreEqual(-1f, over.Position.y, 1e-4f, "Past Max holds the end pose.");
            }
            finally
            {
                blob.Dispose();
            }
        }
    }
}
