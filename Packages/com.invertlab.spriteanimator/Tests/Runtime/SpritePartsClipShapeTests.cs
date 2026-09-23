using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Clip shapes (Spine clipping attachments): range by draw order, End part, clip keys, concave outlines, deform.</summary>
    public sealed class SpritePartsClipShapeTests
    {
        static SpritePartsSetBuilder.SlotInput Part(string id, float2 pos, int rank)
            => new SpritePartsSetBuilder.SlotInput
            {
                Name = id, SlotId = id, RestPosition = pos, RestScale = new float2(1f, 1f), DrawRank = rank,
                SkinQuadSize = new float2(2f, 2f), SkinQuadPivot = new float2(0.5f, 0.5f),
            };

        static SpritePartsSetBuilder.SlotInput Shape(int rank, string end, params float2[] polygon)
            => new SpritePartsSetBuilder.SlotInput
            {
                Name = "clip", SlotId = "clip", RestScale = new float2(1f, 1f), DrawRank = rank, Hidden = 1,
                IsClipShape = true, ClipPolygon = polygon, ClipEndSlotId = end,
            };

        static readonly float2[] Square = { new float2(-1f, -1f), new float2(1f, -1f), new float2(1f, 1f), new float2(-1f, 1f) };

        static SpritePartsLattice[] Evaluate(SpritePartsSetBuilder.SlotInput[] slots, SpritePartsSetBuilder.ClipInput[] clips = null, int clip = -1)
        {
            var blob = SpritePartsSetBuilder.Build(Allocator.Temp, slots, System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                clips ?? System.Array.Empty<SpritePartsSetBuilder.ClipInput>(), System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
            var local = new NativeArray<SpritePartsSampler.Pose>(slots.Length, Allocator.Temp);
            var ltr = new NativeArray<float4x4>(slots.Length, Allocator.Temp);
            try
            {
                SpritePartsPoseWriter.EvaluateEditor(ref blob.Value, clip, 0f, local, ltr, new SpritePartsEvalExtras { Clip = true });
                var result = new SpritePartsLattice[slots.Length];
                for (int i = 0; i < slots.Length; i++)
                    result[i] = local[i].Lattice;
                return result;
            }
            finally
            {
                local.Dispose();
                ltr.Dispose();
                blob.Dispose();
            }
        }

        static float Area(in SpritePartsLattice l)
        {
            float area = 0f;
            for (int t = 0; t + 2 < l.IndexCount; t += 3)
            {
                float2 a = l.Points[l.Indices[t]], b = l.Points[l.Indices[t + 1]], c = l.Points[l.Indices[t + 2]];
                area += math.abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
            }
            return area;
        }

        [Test]
        public void Shape_Clips_The_Parts_Above_It_Up_To_The_End()
        {
            // Shape covers x -1..1. Eye (above, the End) covers x 0..2; hair is above the End; skin is below the shape.
            var lat = Evaluate(new[]
            {
                Shape(1, "eye", Square),
                Part("eye", new float2(1f, 0f), 2),
                Part("hair", new float2(1f, 0f), 3),
                Part("skin", new float2(1f, 0f), 0),
            });
            Assert.AreEqual(0.5f, Area(lat[1]), 1e-4f, "The eye shows only inside the shape.");
            Assert.IsFalse(lat[2].HasMesh, "Above the End part: not clipped.");
            Assert.IsFalse(lat[3].HasMesh, "Below the shape: not clipped.");
        }

        [Test]
        public void No_End_Clips_Everything_Above()
        {
            var lat = Evaluate(new[]
            {
                Shape(0, null, Square),
                Part("eye", new float2(1f, 0f), 1),
                Part("hair", new float2(1f, 0f), 2),
            });
            Assert.AreEqual(0.5f, Area(lat[1]), 1e-4f);
            Assert.AreEqual(0.5f, Area(lat[2]), 1e-4f);
        }

        [Test]
        public void A_Clip_Key_Turns_The_Shape_Off()
        {
            var clips = new[]
            {
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "c", ClipId = "c", Duration = 1f, SpeedMultiplier = 1f,
                    Tracks = new[]
                    {
                        new SpritePartsSetBuilder.TrackInput
                        {
                            SlotId = "clip",
                            Keys = new[]
                            {
                                new SpritePartsSetBuilder.KeyInput
                                {
                                    Time = 0f, Scale = new float2(1f, 1f), AppearanceId = string.Empty,
                                    HasClipActive = true, ClipActive = false,
                                },
                            },
                        },
                    },
                },
            };
            var lat = Evaluate(new[] { Shape(0, null, Square), Part("eye", new float2(1f, 0f), 1) }, clips, 0);
            Assert.IsFalse(lat[1].HasMesh, "Switched off: the eye is whole.");
        }

        [Test]
        public void Deform_Keys_Move_The_Outline()
        {
            var shift = new FixedList512Bytes<float2>();
            for (int i = 0; i < 4; i++)
                shift.Add(new float2(0.5f, 0f));
            var clips = new[]
            {
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "c", ClipId = "c", Duration = 1f, SpeedMultiplier = 1f,
                    Tracks = new[]
                    {
                        new SpritePartsSetBuilder.TrackInput
                        {
                            SlotId = "clip",
                            Keys = new[] { new SpritePartsSetBuilder.KeyInput { Time = 0f, Scale = new float2(1f, 1f), AppearanceId = string.Empty, Deform = shift } },
                        },
                    },
                },
            };
            var still = Evaluate(new[] { Shape(0, null, Square), Part("eye", new float2(1f, 0f), 1) });
            var moved = Evaluate(new[] { Shape(0, null, Square), Part("eye", new float2(1f, 0f), 1) }, clips, 0);
            Assert.AreEqual(0.5f, Area(still[1]), 1e-4f);
            Assert.AreEqual(0.75f, Area(moved[1]), 1e-4f, "The outline moved half a unit toward the eye.");
        }

        [Test]
        public void A_Concave_Shape_Clips_To_Its_Outline()
        {
            // An L: the 2 x 2 square around the eye without its top-right quarter.
            var lShape = new[]
            {
                new float2(-1f, -1f), new float2(1f, -1f), new float2(1f, 0f), new float2(0f, 0f), new float2(0f, 1f), new float2(-1f, 1f),
            };
            var lat = Evaluate(new[] { Shape(0, null, lShape), Part("eye", float2.zero, 1) });
            Assert.AreEqual(0.75f, Area(lat[1]), 1e-4f);
        }
    }
}
