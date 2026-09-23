using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Spine-style weights: mesh vertices follow bound part joints (linear blend skinning).</summary>
    public sealed class SpritePartsSkinningTests
    {
        // arm (mesh, 2 x 1 world units, pivot at its left edge) -> hand joint at the arm's right side.
        // Left vertices belong to the arm, right vertices to the hand.
        static BlobAssetReference<SpritePartsSetBlob> BuildArm(string[] bones, float[] weights, float handRotation)
        {
            var mesh = SpritePartsLattice.FromMesh(SpritePartsMeshOps.CreateQuad());
            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput
                {
                    SlotId = "arm", Name = "Arm", RestScale = new float2(1f, 1f), DrawRank = 0,
                    Mesh = mesh, SkinBones = bones, SkinWeights = weights,
                    SkinQuadSize = new float2(2f, 1f), SkinQuadPivot = new float2(0f, 0.5f),
                },
                new SpritePartsSetBuilder.SlotInput
                {
                    SlotId = "hand", Name = "Hand", ParentSlotId = "arm",
                    RestPosition = new float2(1f, 0f), RestScale = new float2(1f, 1f), DrawRank = 1,
                },
            };
            var clips = new[]
            {
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "Bend", ClipId = "bend", Duration = 1f, SpeedMultiplier = 1f,
                    Tracks = new[]
                    {
                        new SpritePartsSetBuilder.TrackInput
                        {
                            SlotId = "hand",
                            Keys = new[]
                            {
                                new SpritePartsSetBuilder.KeyInput
                                {
                                    Time = 0f, Position = new float2(1f, 0f), Rotation = handRotation,
                                    Scale = new float2(1f, 1f), AppearanceId = string.Empty,
                                },
                            },
                        },
                    },
                },
            };
            return SpritePartsSetBuilder.Build(Allocator.Temp, slots,
                System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(), clips,
                System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
        }

        static SpritePartsLattice SkinArm(BlobAssetReference<SpritePartsSetBlob> blob, out float2x2 handLinear)
        {
            int n = blob.Value.Slots.Length;
            var poses = new NativeArray<SpritePartsSampler.Pose>(n, Allocator.Temp);
            var mats = new NativeArray<float4x4>(n, Allocator.Temp);
            try
            {
                SpritePartsPoseWriter.EvaluateEditor(ref blob.Value, 0, 0f, poses, mats);
                var lattice = poses[0].Lattice;
                SpritePartsSkinning.Apply(ref blob.Value, 0, mats, ref lattice);
                handLinear = SpritePartsSkinning.VertexLinear(ref blob.Value, 0, mats, 1);
                return lattice;
            }
            finally
            {
                poses.Dispose();
                mats.Dispose();
            }
        }

        static readonly float[] SplitWeights =
        {
            // arm, hand   (vertex order: BL, BR, TR, TL)
            1f, 0f,
            0f, 1f,
            0f, 1f,
            1f, 0f,
        };

        [Test]
        public void VerticesFollowTheirBone()
        {
            var blob = BuildArm(new[] { "arm", "hand" }, SplitWeights, 90f);
            try
            {
                Assert.IsTrue(SpritePartsSkinning.IsSkinned(ref blob.Value.Slots[0]));
                var skinned = SkinArm(blob, out var linear);
                var rest = SpritePartsLattice.FromMesh(SpritePartsMeshOps.CreateQuad());

                // Arm-owned vertices stay put.
                Assert.AreEqual(rest.GetPoint(0).x, skinned.GetPoint(0).x, 1e-4f);
                Assert.AreEqual(rest.GetPoint(3).y, skinned.GetPoint(3).y, 1e-4f);

                // BR is at arm-local (2, -0.5). Rotating the hand (at (1, 0)) by 90 degrees puts it at (1.5, 1),
                // which is unit (0.75 - 0.5, 1 - 0.5 + 0.5) = (0.25, 1).
                Assert.AreEqual(0.25f, skinned.GetPoint(1).x, 1e-4f);
                Assert.AreEqual(1f, skinned.GetPoint(1).y, 1e-4f);
                Assert.AreEqual(rest.GetUv(1), skinned.GetUv(1), "Texture coordinates never move.");

                // The editor inverts this to turn a drag into a pre-skin deform offset.
                Assert.AreEqual(0f, linear.c0.x, 1e-4f);
                Assert.AreEqual(1f, linear.c0.y, 1e-4f);
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void MeshBoundOnlyToItself_IsUnchanged()
        {
            var blob = BuildArm(new[] { "arm" }, new[] { 1f, 1f, 1f, 1f }, 90f);
            try
            {
                var skinned = SkinArm(blob, out var linear);
                var rest = SpritePartsLattice.FromMesh(SpritePartsMeshOps.CreateQuad());
                for (int i = 0; i < 4; i++)
                {
                    Assert.AreEqual(rest.GetPoint(i).x, skinned.GetPoint(i).x, 1e-4f);
                    Assert.AreEqual(rest.GetPoint(i).y, skinned.GetPoint(i).y, 1e-4f);
                }
                Assert.AreEqual(1f, linear.c0.x, 1e-4f);
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void BadWeights_LeaveTheMeshUnweighted()
        {
            var blob = BuildArm(new[] { "arm", "missing.bone" }, SplitWeights, 90f);
            try
            {
                Assert.IsFalse(SpritePartsSkinning.IsSkinned(ref blob.Value.Slots[0]));
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void AutoWeights_NearestBoneWinsAndRowsSumToOne()
        {
            var verts = new[] { new float2(0.1f, 0f), new float2(1.9f, 0f), new float2(1f, 0f) };
            var starts = new[] { new float2(0f, 0f), new float2(2f, 0f) };
            var ends = new[] { new float2(0.5f, 0f), new float2(2.5f, 0f) };
            var w = SpritePartsSkinning.AutoWeights(verts, starts, ends);
            Assert.Greater(w[0], 0.95f);
            Assert.Greater(w[3], 0.95f);
            for (int v = 0; v < 3; v++)
                Assert.AreEqual(1f, w[v * 2] + w[v * 2 + 1], 1e-5f);
        }

        [Test]
        public void SetWeight_KeepsRowsNormalized()
        {
            var w = new[] { 0.5f, 0.3f, 0.2f };
            SpritePartsSkinning.SetWeight(w, 3, 0, 0, 0.8f);
            Assert.AreEqual(0.8f, w[0], 1e-5f);
            Assert.AreEqual(1f, w[0] + w[1] + w[2], 1e-5f);
            Assert.AreEqual(0.12f, w[1], 1e-5f, "Other bones keep their ratio.");
        }

        [Test]
        public void TopologyChanges_CarryWeights()
        {
            var mesh = SpritePartsMeshOps.CreateQuad();
            mesh.Bones = new[] { "arm", "hand" };
            mesh.Weights = (float[])SplitWeights.Clone();
            Assert.IsTrue(SpritePartsMeshOps.TryAddInteriorVertex(mesh, new Vector2(0.9f, 0.5f), out int added, out _));
            Assert.IsTrue(mesh.HasWeights);
            Assert.Greater(mesh.Weights[added * 2 + 1], 0.5f, "A vertex near the hand side leans to the hand.");

            Assert.IsTrue(SpritePartsMeshOps.TryRemoveVertices(mesh, new[] { 0 }, out _));
            Assert.IsTrue(mesh.HasWeights);
            Assert.AreEqual(0f, mesh.Weights[0], 1e-5f, "Old BR (hand) is now vertex 0.");
        }
    }
}
