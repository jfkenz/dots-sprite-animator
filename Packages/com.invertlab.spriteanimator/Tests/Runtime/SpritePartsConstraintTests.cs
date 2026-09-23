using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Transform constraints (copy a target) and path constraints (follow a curve).</summary>
    public sealed class SpritePartsConstraintTests
    {
        static SpritePartsSetBuilder.SlotInput Slot(string id, float2 pos, float rot = 0f, string parent = null)
            => new SpritePartsSetBuilder.SlotInput
            {
                Name = id, SlotId = id, ParentSlotId = parent, RestPosition = pos, RestRotation = rot, RestScale = new float2(1f, 1f),
            };

        static BlobAssetReference<SpritePartsSetBlob> Build(SpritePartsSetBuilder.SlotInput[] slots,
            SpritePartsSetBuilder.TransformInput[] transforms = null, SpritePartsSetBuilder.PathInput[] paths = null,
            SpritePartsSetBuilder.ClipInput[] clips = null)
            => SpritePartsSetBuilder.Build(Allocator.Temp, slots, System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                clips ?? System.Array.Empty<SpritePartsSetBuilder.ClipInput>(), System.Array.Empty<SpritePartsSetBuilder.SkinInput>(),
                transforms: transforms, paths: paths);

        /// <summary>Root-space position (xy) and rotation (z) of each slot.</summary>
        static float3[] Evaluate(BlobAssetReference<SpritePartsSetBlob> blob, int clip = -1, float time = 0f)
        {
            int n = blob.Value.Slots.Length;
            var local = new NativeArray<SpritePartsSampler.Pose>(n, Allocator.Temp);
            var ltr = new NativeArray<float4x4>(n, Allocator.Temp);
            try
            {
                SpritePartsPoseWriter.EvaluateEditor(ref blob.Value, clip, time, local, ltr, default);
                var result = new float3[n];
                for (int i = 0; i < n; i++)
                    result[i] = new float3(ltr[i].c3.xy, SpritePartsHierarchy.ExtractRotationDeg(ltr[i]));
                return result;
            }
            finally
            {
                local.Dispose();
                ltr.Dispose();
            }
        }

        static SpritePartsSetBuilder.TransformInput Copy(float mix, bool local = false, bool relative = false)
            => new SpritePartsSetBuilder.TransformInput
            {
                Name = "Copy", TargetSlotId = "target", BoneSlotIds = new[] { "bone" },
                MixRotate = mix, MixX = mix, MixY = mix, Local = local, Relative = relative,
            };

        [Test]
        public void Transform_Matches_The_Target_By_Mix()
        {
            var slots = new[] { Slot("target", new float2(3f, 4f), 30f), Slot("bone", float2.zero), Slot("child", new float2(1f, 0f), 0f, "bone") };
            var full = Build(slots, new[] { Copy(1f) });
            var half = Build(slots, new[] { Copy(0.5f) });
            try
            {
                var r = Evaluate(full);
                Assert.AreEqual(3f, r[1].x, 1e-4f);
                Assert.AreEqual(4f, r[1].y, 1e-4f);
                Assert.AreEqual(30f, r[1].z, 1e-3f);
                Assert.AreEqual(3f + math.cos(math.radians(30f)), r[2].x, 1e-4f, "Children follow the constrained part.");
                var h = Evaluate(half);
                Assert.AreEqual(1.5f, h[1].x, 1e-4f);
                Assert.AreEqual(15f, h[1].z, 1e-3f);
            }
            finally
            {
                full.Dispose();
                half.Dispose();
            }
        }

        [Test]
        public void Relative_Adds_The_Targets_Change_From_Setup()
        {
            var slots = new[] { Slot("target", new float2(3f, 4f)), Slot("bone", new float2(1f, 0f), 10f) };
            var clip = new SpritePartsSetBuilder.ClipInput
            {
                Name = "turn", ClipId = "turn", Duration = 1f, SpeedMultiplier = 1f,
                Tracks = new[]
                {
                    new SpritePartsSetBuilder.TrackInput
                    {
                        SlotId = "target",
                        Keys = new[] { new SpritePartsSetBuilder.KeyInput { Time = 0f, Position = new float2(3f, 5f), Rotation = 20f, Scale = new float2(1f, 1f), AppearanceId = string.Empty } },
                    },
                },
            };
            var blob = Build(slots, new[] { Copy(1f, local: true, relative: true) }, clips: new[] { clip });
            try
            {
                var rest = Evaluate(blob);
                Assert.AreEqual(10f, rest[1].z, 1e-3f, "At setup the target has not changed: nothing added.");
                var turned = Evaluate(blob, 0, 0f);
                Assert.AreEqual(30f, turned[1].z, 1e-3f, "Its own 10 plus the target's 20.");
                Assert.AreEqual(1f, turned[1].y, 1e-4f, "Plus the target's move (0, 1).");
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Keyed_Transform_Mix_Scales_It()
        {
            var slots = new[] { Slot("target", new float2(4f, 0f)), Slot("bone", float2.zero) };
            var clip = new SpritePartsSetBuilder.ClipInput
            {
                Name = "fade", ClipId = "fade", Duration = 1f, SpeedMultiplier = 1f, Tracks = System.Array.Empty<SpritePartsSetBuilder.TrackInput>(),
                ValueTracks = new[]
                {
                    new SpritePartsSetBuilder.ValueTrackInput
                    {
                        Kind = (byte)SpritePartsValueKind.TransformMix, Target = "Copy",
                        Keys = new[] { new SpritePartsSetBuilder.ValueKeyInput { Time = 0f, Value = 0.25f } },
                    },
                },
            };
            var blob = Build(slots, new[] { Copy(1f) }, clips: new[] { clip });
            try
            {
                Assert.AreEqual(4f, Evaluate(blob)[1].x, 1e-4f);
                Assert.AreEqual(1f, Evaluate(blob, 0, 0f)[1].x, 1e-4f);
            }
            finally
            {
                blob.Dispose();
            }
        }

        static SpritePartsSetBuilder.SlotInput PathSlot(params float2[] points)
        {
            var s = Slot("path", float2.zero);
            s.IsPath = true;
            s.PathPoints = points;
            s.Hidden = 1;
            return s;
        }

        static SpritePartsSetBuilder.PathInput Follow(float position, float spacing = 0.1f,
            SpritePartsPathRotate rotate = SpritePartsPathRotate.Tangent)
            => new SpritePartsSetBuilder.PathInput
            {
                Name = "Follow", PathSlotId = "path", BoneSlotIds = new[] { "a", "b" }, Position = position, Spacing = spacing,
                SpacingMode = (byte)SpritePartsPathSpacing.Percent, RotateMode = (byte)rotate, MixRotate = 1f, MixTranslate = 1f,
            };

        [Test]
        public void Path_Places_The_Chain_Along_The_Curve()
        {
            var slots = new[] { PathSlot(new float2(0f, 0f), new float2(0f, 10f)), Slot("a", new float2(7f, 7f)), Slot("b", new float2(-3f, 0f)) };
            var blob = Build(slots, paths: new[] { Follow(0.5f) });
            try
            {
                var r = Evaluate(blob);
                Assert.AreEqual(0f, r[1].x, 1e-3f);
                Assert.AreEqual(5f, r[1].y, 1e-2f, "Halfway up the path.");
                Assert.AreEqual(6f, r[2].y, 1e-2f, "10% of the path further on.");
                Assert.AreEqual(90f, r[1].z, 1e-2f, "Turned along the path.");
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Path_Follows_Its_Part_And_Clamps_At_The_End()
        {
            var path = PathSlot(new float2(0f, 0f), new float2(10f, 0f));
            path.RestPosition = new float2(0f, 2f);
            var blob = Build(new[] { path, Slot("a", float2.zero), Slot("b", float2.zero) }, paths: new[] { Follow(0.95f) });
            try
            {
                var r = Evaluate(blob);
                Assert.AreEqual(2f, r[1].y, 1e-3f, "The curve moves with its part.");
                Assert.AreEqual(9.5f, r[1].x, 1e-2f);
                Assert.AreEqual(10f, r[2].x, 1e-2f, "Past the end of an open path: held at the end.");
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Catmull_Rom_Passes_Through_Its_Points()
        {
            float2 p0 = new float2(-1f, 0f), p1 = new float2(0f, 0f), p2 = new float2(1f, 1f), p3 = new float2(2f, 1f);
            Assert.That(math.distance(SpritePartsConstraints.CatmullRom(p0, p1, p2, p3, 0f), p1), Is.LessThan(1e-5f));
            Assert.That(math.distance(SpritePartsConstraints.CatmullRom(p0, p1, p2, p3, 1f), p2), Is.LessThan(1e-5f));
        }
    }
}
