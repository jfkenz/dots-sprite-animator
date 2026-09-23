using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Clipping masks: a part is cut to its mask's rectangle or mesh outline.</summary>
    public sealed class SpritePartsClippingTests
    {
        static SpritePartsSetBuilder.SlotInput Part(string id, float2 pos, float size, string mask = null, SpritePartMeshDef mesh = null)
            => new SpritePartsSetBuilder.SlotInput
            {
                Name = id, SlotId = id, RestPosition = pos, RestScale = new float2(1f, 1f),
                SkinQuadSize = new float2(size, size), SkinQuadPivot = new float2(0.5f, 0.5f),
                ClipMaskSlotId = mask, Mesh = mesh != null ? SpritePartsLattice.FromMesh(mesh) : default,
            };

        static SpritePartsLattice Clip(params SpritePartsSetBuilder.SlotInput[] slots)
        {
            var blob = SpritePartsSetBuilder.Build(Allocator.Temp, slots, System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                System.Array.Empty<SpritePartsSetBuilder.ClipInput>(), System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
            var local = new NativeArray<SpritePartsSampler.Pose>(slots.Length, Allocator.Temp);
            var ltr = new NativeArray<float4x4>(slots.Length, Allocator.Temp);
            try
            {
                SpritePartsPoseWriter.EvaluateEditor(ref blob.Value, -1, 0f, local, ltr,
                    new SpritePartsEvalExtras { Clip = true });
                return local[1].Lattice;
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
        public void Rectangle_Mask_Cuts_Off_What_Hangs_Outside()
        {
            // Mask covers x -1..1; the eye covers x 0..2: only its left half shows.
            var eye = Clip(Part("mask", float2.zero, 2f), Part("eye", new float2(1f, 0f), 2f, "mask"));
            Assert.IsTrue(eye.HasMesh);
            Assert.AreEqual(1, eye.Final, "A clip result is final (not weighted again).");
            Assert.AreEqual(0.5f, Area(eye), 1e-4f);
            for (int v = 0; v < eye.PointCount; v++)
            {
                Assert.LessOrEqual(eye.Points[v].x, 1e-4f);
                Assert.AreEqual(eye.Points[v].x + 0.5f, eye.Uvs[v].x, 1e-4f, "Texture coordinates follow the cut.");
            }
        }

        [Test]
        public void Outside_Hides_And_Inside_Keeps_The_Whole_Part()
        {
            var gone = Clip(Part("mask", float2.zero, 2f), Part("eye", new float2(5f, 0f), 1f, "mask"));
            Assert.IsTrue(gone.HasMesh, "Still a mesh, so the flat rectangle does not come back.");
            Assert.AreEqual(0f, Area(gone), 1e-6f);
            var whole = Clip(Part("mask", float2.zero, 2f), Part("eye", float2.zero, 0.5f, "mask"));
            Assert.AreEqual(1f, Area(whole), 1e-4f);
            Assert.AreEqual(4, whole.PointCount);
        }

        [Test]
        public void Concave_Mesh_Mask_Clips_To_Its_Outline()
        {
            // An L: the unit square without its top-right quarter.
            var outline = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, 1f),
            };
            var mesh = SpritePartsMeshOps.FromOutline(outline);
            var lattice = SpritePartsLattice.FromMesh(mesh);
            var flat = SpritePartsClipping.ConvexPieces(ref lattice);
            int pieces = 0;
            for (int p = 0; p < flat.Count; p += flat[p] + 1)
                pieces++;
            Assert.That(pieces, Is.InRange(2, 3), "An L is not convex, and its 4 triangles merge into fewer pieces.");
            var eye = Clip(Part("mask", float2.zero, 1f, null, mesh), Part("eye", float2.zero, 1f, "mask"));
            Assert.AreEqual(0.75f, Area(eye), 1e-4f);
        }
    }
}
