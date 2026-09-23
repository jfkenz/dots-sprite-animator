using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Setup mesh on the slot, per-vertex offsets on keys (the Spine mesh / deform split).</summary>
    public sealed class SpritePartsLatticeTests
    {
        static float TriangleArea(SpritePartMeshDef mesh)
        {
            float area = 0f;
            for (int t = 0; t < mesh.Triangles.Length; t += 3)
            {
                Vector2 a = mesh.Vertices[mesh.Triangles[t]];
                Vector2 b = mesh.Vertices[mesh.Triangles[t + 1]];
                Vector2 c = mesh.Vertices[mesh.Triangles[t + 2]];
                float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                Assert.Greater(cross, 0f, "Triangles are CCW and not degenerate.");
                area += cross * 0.5f;
            }
            return area;
        }

        static bool HasTriangleEdge(SpritePartMeshDef mesh, int a, int b)
        {
            for (int t = 0; t < mesh.Triangles.Length; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int p = mesh.Triangles[t + e];
                    int q = mesh.Triangles[t + (e + 1) % 3];
                    if ((p == a && q == b) || (p == b && q == a))
                        return true;
                }
            }
            return false;
        }

        [Test]
        public void EmptyLattice_IsIdentity()
        {
            var lattice = new SpritePartsLattice();
            Assert.IsTrue(lattice.IsIdentity);
            Assert.IsFalse(lattice.HasMesh);
            Assert.IsNull(lattice.OffsetArray());
        }

        [Test]
        public void Quad_CoversTheWholeImage()
        {
            var mesh = SpritePartsMeshOps.CreateQuad();
            Assert.IsTrue(mesh.HasMesh);
            Assert.AreEqual(4, mesh.HullCount);
            Assert.AreEqual(6, mesh.Triangles.Length);
            Assert.AreEqual(1f, TriangleArea(mesh), 1e-5f);

            var rest = SpritePartsLattice.FromMesh(mesh);
            Assert.AreEqual(4, rest.PointCount);
            Assert.AreEqual(new float2(-0.5f, -0.5f), rest.GetPoint(0));
            Assert.IsNull(rest.OffsetArray(), "The setup mesh has no deform.");
        }

        [Test]
        public void InteriorVertex_SplitsTheHullAndKeepsArea()
        {
            var mesh = SpritePartsMeshOps.CreateQuad();
            Assert.IsTrue(SpritePartsMeshOps.TryAddInteriorVertex(mesh, new Vector2(0.4f, 0.6f), out int index, out var remap));
            Assert.AreEqual(4, index);
            Assert.AreEqual(new[] { 0, 1, 2, 3 }, remap);
            Assert.AreEqual(4, mesh.HullCount);
            Assert.AreEqual(4 * 3, mesh.Triangles.Length);
            Assert.AreEqual(1f, TriangleArea(mesh), 1e-5f);

            Assert.IsFalse(SpritePartsMeshOps.TryAddInteriorVertex(mesh, new Vector2(1.5f, 0.5f), out _, out _),
                "Interior vertices must be inside the hull.");
        }

        [Test]
        public void HullInsert_ShiftsLaterIndicesAndRemapsDeform()
        {
            var mesh = SpritePartsMeshOps.CreateQuad();
            SpritePartsMeshOps.TryAddInteriorVertex(mesh, new Vector2(0.5f, 0.5f), out _, out _);
            var deform = new[] { Vector2.zero, new Vector2(0.1f, 0f), Vector2.zero, Vector2.zero, new Vector2(0f, 0.2f) };

            Assert.IsTrue(SpritePartsMeshOps.TryInsertHullVertex(mesh, 0, new Vector2(0.5f, -0.2f), out int index, out var remap));
            Assert.AreEqual(1, index);
            Assert.AreEqual(5, mesh.HullCount);
            Assert.AreEqual(new[] { 0, 2, 3, 4, 5 }, remap);

            var moved = SpritePartsMeshOps.RemapDeform(deform, remap, mesh.VertexCount);
            Assert.AreEqual(6, moved.Length);
            Assert.AreEqual(Vector2.zero, moved[1], "The new vertex starts at rest.");
            Assert.AreEqual(new Vector2(0.1f, 0f), moved[2]);
            Assert.AreEqual(new Vector2(0f, 0.2f), moved[5]);
        }

        [Test]
        public void HullInsertAuto_GrowsTheOutlineWithoutCrossing()
        {
            var mesh = SpritePartsMeshOps.CreateQuad();
            Assert.IsTrue(SpritePartsMeshOps.TryInsertHullVertexAuto(mesh, new Vector2(1f, 0.5f), out int index, out _));
            Assert.AreEqual(2, index, "Point right of the quad lands on the right edge (1 -> 2).");
            Assert.IsTrue(mesh.HasMesh);
        }

        [Test]
        public void AppendHull_TriangulatesOnceThreePointsExist()
        {
            var mesh = new SpritePartMeshDef();
            Assert.IsTrue(SpritePartsMeshOps.TryAppendHullVertex(mesh, new Vector2(0.1f, 0.1f), out _));
            Assert.IsTrue(SpritePartsMeshOps.TryAppendHullVertex(mesh, new Vector2(0.9f, 0.1f), out _));
            Assert.IsFalse(mesh.HasMesh);
            Assert.IsTrue(SpritePartsMeshOps.TryAppendHullVertex(mesh, new Vector2(0.5f, 0.9f), out _));
            Assert.IsTrue(mesh.HasMesh);
            Assert.AreEqual(3, mesh.Triangles.Length);
        }

        [Test]
        public void RemoveVertex_KeepsAtLeastThreeHullVertices()
        {
            var mesh = SpritePartsMeshOps.CreateQuad();
            Assert.IsTrue(SpritePartsMeshOps.TryRemoveVertices(mesh, new[] { 1 }, out var remap));
            Assert.AreEqual(new[] { 0, -1, 1, 2 }, remap);
            Assert.AreEqual(3, mesh.HullCount);
            Assert.IsFalse(SpritePartsMeshOps.TryRemoveVertices(mesh, new[] { 0 }, out _));
        }

        [Test]
        public void UserEdge_IsKeptByTheTriangulation()
        {
            var mesh = SpritePartsMeshOps.CreateQuad();
            // The Delaunay diagonal of a square is ambiguous; force the other one.
            bool has02 = HasTriangleEdge(mesh, 0, 2);
            int a = has02 ? 1 : 0;
            int b = has02 ? 3 : 2;
            Assert.IsTrue(SpritePartsMeshOps.TryAddEdge(mesh, a, b));
            Assert.IsTrue(HasTriangleEdge(mesh, a, b));
            Assert.AreEqual(1f, TriangleArea(mesh), 1e-5f);
        }

        [Test]
        public void SelfCrossingHull_IsRejected()
        {
            var bowtie = new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 1f),
            };
            Assert.IsNull(SpritePartsMeshOps.FromOutline(bowtie));

            var mesh = SpritePartsMeshOps.CreateQuad();
            Assert.IsFalse(SpritePartsMeshOps.TrySetVertices(mesh, new[] { 0, 1 }, new[] { new Vector2(1f, 0f), new Vector2(0f, 0f) }),
                "Swapping two corners turns the hull into a bowtie.");
            Assert.AreEqual(Vector2.zero, mesh.Vertices[0], "A rejected move leaves the mesh alone.");
        }

        [Test]
        public void SplitEdge_UserEdgeBecomesTwoAndHullEdgeGainsHullVertex()
        {
            var mesh = SpritePartsMeshOps.CreateQuad();
            SpritePartsMeshOps.TryAddInteriorVertex(mesh, new Vector2(0.3f, 0.5f), out int a, out _);
            SpritePartsMeshOps.TryAddInteriorVertex(mesh, new Vector2(0.7f, 0.5f), out int b, out _);
            Assert.IsTrue(SpritePartsMeshOps.TryAddEdge(mesh, a, b));
            Assert.IsTrue(SpritePartsMeshOps.TrySplitEdgeAt(mesh, a, b, new Vector2(0.5f, 0.5f), out int mid, out _));
            Assert.IsFalse(SpritePartsMeshOps.HasEdge(mesh, a, b));
            Assert.IsTrue(SpritePartsMeshOps.HasEdge(mesh, a, mid));
            Assert.IsTrue(SpritePartsMeshOps.HasEdge(mesh, mid, b));

            Assert.IsTrue(SpritePartsMeshOps.TrySplitEdgeAt(mesh, 0, 1, new Vector2(0.5f, 0f), out int hull, out _));
            Assert.AreEqual(1, hull);
            Assert.AreEqual(5, mesh.HullCount);
            Assert.AreEqual(1f, TriangleArea(mesh), 1e-4f);
        }

        [Test]
        public void MergeVertices_WeldsAtMidpointAndKeepsEdges()
        {
            var mesh = SpritePartsMeshOps.CreateQuad();
            SpritePartsMeshOps.TryAddInteriorVertex(mesh, new Vector2(0.4f, 0.5f), out int a, out _);
            SpritePartsMeshOps.TryAddInteriorVertex(mesh, new Vector2(0.6f, 0.5f), out int b, out _);
            Assert.IsTrue(SpritePartsMeshOps.TryAddEdge(mesh, b, 0));
            Assert.IsTrue(SpritePartsMeshOps.TryMergeVertices(mesh, a, b, out int survivor, out var remap));
            Assert.AreEqual(5, mesh.VertexCount);
            Assert.AreEqual(-1, remap[b]);
            Assert.AreEqual(0.5f, mesh.Vertices[survivor].x, 1e-5f);
            Assert.IsTrue(SpritePartsMeshOps.HasEdge(mesh, survivor, 0), "The dropped vertex's edge moves to the survivor.");
        }

        [Test]
        public void SegmentHit_FindsCrossingsOnly()
        {
            Assert.IsTrue(SpritePartsMeshOps.TrySegmentHit(new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), out float t, out var p));
            Assert.AreEqual(0.5f, t, 1e-5f);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), p);
            Assert.IsFalse(SpritePartsMeshOps.TrySegmentHit(new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), out _, out _));
        }

        [Test]
        public void Generate_AddsInteriorVertices()
        {
            var mesh = SpritePartsMeshOps.CreateQuad();
            int added = SpritePartsMeshOps.GenerateInterior(mesh, 6, out var remap);
            Assert.Greater(added, 0);
            Assert.AreEqual(4, remap.Length);
            Assert.AreEqual(4 + added, mesh.VertexCount);
            Assert.AreEqual(1f, TriangleArea(mesh), 1e-4f);
        }

        [Test]
        public void ApplyDeform_BlendsOffsetsAndTreatsMissingAsRest()
        {
            var rest = SpritePartsLattice.FromMesh(SpritePartsMeshOps.CreateQuad());
            var a = SpritePartsLattice.DeformFromArray(new[] { new Vector2(0.2f, 0f), Vector2.zero, Vector2.zero, Vector2.zero }, 4);
            var none = new FixedList512Bytes<float2>();

            var posed = rest;
            SpritePartsLattice.ApplyDeform(ref posed, a, none, 0.5f);
            Assert.AreEqual(-0.4f, posed.GetPoint(0).x, 1e-5f);
            Assert.AreEqual(new float2(0f, 0f), posed.GetUv(0), "Texture coordinates never move.");

            var wrongCount = SpritePartsLattice.DeformFromArray(new[] { Vector2.one }, 1);
            var unchanged = rest;
            SpritePartsLattice.ApplyDeform(ref unchanged, wrongCount, wrongCount, 0f);
            Assert.AreEqual(rest.GetPoint(0), unchanged.GetPoint(0));
        }

        [Test]
        public void Sampler_UsesSlotMeshAndKeyOffsets()
        {
            var profile = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            var body = SpritePartsAuthoringOps.FindSlot(profile, "body");
            body.Mesh = SpritePartsMeshOps.CreateQuad();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");

            // Posed lattice with vertex 2 pulled right: the key must store only that offset.
            var lattice = SpritePartsLattice.FromMesh(body.Mesh);
            lattice.SetPoint(2, lattice.GetPoint(2) + new float2(0.25f, 0f));
            SpritePartsAuthoringOps.ApplyPoseEdit(
                profile, SpritePartsStudioMode.Animate, walk, "body", 0.2f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = body.RestPosition,
                    Rotation = body.RestRotation,
                    Scale = body.RestScale,
                    Lattice = lattice,
                }, autoKey: true);

            SpritePartsKeyDef key = null;
            foreach (var track in profile.PartsClips[walk].Tracks)
            {
                if (track.SlotId != "body" || track.Kind != SpritePartsTrackKind.Pose)
                    continue;
                key = track.Keys.Find(k => Mathf.Abs(k.Time - 0.2f) < 1e-4f);
            }
            Assert.IsNotNull(key);
            Assert.AreEqual(4, key.Deform.Length);
            Assert.AreEqual(0.25f, key.Deform[2].x, 1e-5f);
            Assert.AreEqual(Vector2.zero, key.Deform[0]);

            Assert.IsTrue(SpritePartsOnion.TrySampleCharacter(profile, walk, 0.2f, Allocator.Temp,
                out var blob, out var poses, out var mats, out var err), err);
            try
            {
                int slot = SpritePartsAuthoringOps.FindSlotIndex(profile, "body");
                var posed = poses[slot].Lattice;
                Assert.IsTrue(posed.HasMesh);
                Assert.AreEqual(0.75f, posed.GetPoint(2).x, 1e-5f);
                Assert.AreEqual(new float2(1f, 1f), posed.GetUv(2));
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, mats);
            }
        }

        [Test]
        public void RemapSlotDeforms_FollowsTopologyInEveryClip()
        {
            var profile = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            var body = SpritePartsAuthoringOps.FindSlot(profile, "body");
            body.Mesh = SpritePartsMeshOps.CreateQuad();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            var lattice = SpritePartsLattice.FromMesh(body.Mesh);
            lattice.SetPoint(3, lattice.GetPoint(3) + new float2(0f, 0.1f));
            SpritePartsAuthoringOps.ApplyPoseEdit(
                profile, SpritePartsStudioMode.Animate, walk, "body", 0.1f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = body.RestPosition, Rotation = body.RestRotation, Scale = body.RestScale, Lattice = lattice,
                }, autoKey: true);

            Assert.IsTrue(SpritePartsMeshOps.TryRemoveVertices(body.Mesh, new[] { 0 }, out var remap));
            Assert.Greater(SpritePartsAuthoringOps.RemapSlotDeforms(profile, "body", remap, body.Mesh.VertexCount), 0);
            foreach (var track in profile.PartsClips[walk].Tracks)
            {
                if (track.SlotId != "body")
                    continue;
                foreach (var key in track.Keys)
                {
                    if (key.Deform == null)
                        continue;
                    Assert.AreEqual(3, key.Deform.Length);
                    Assert.AreEqual(0.1f, key.Deform[2].y, 1e-5f, "Old vertex 3 is now vertex 2.");
                }
            }
        }
    }
}
