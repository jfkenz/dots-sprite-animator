using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Subdivide: a vertex in the middle of every line of the chosen triangles.</summary>
    public sealed class SpritePartsSubdivideTests
    {
        static void AssertValid(SpritePartMeshDef mesh)
        {
            Assert.IsTrue(mesh.HasMesh);
            var used = new bool[mesh.VertexCount];
            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                Vector2 a = mesh.Vertices[mesh.Triangles[t]], b = mesh.Vertices[mesh.Triangles[t + 1]], c = mesh.Vertices[mesh.Triangles[t + 2]];
                float area = ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
                Assert.Greater(Mathf.Abs(area), 1e-6f, "No flat triangles.");
                used[mesh.Triangles[t]] = used[mesh.Triangles[t + 1]] = used[mesh.Triangles[t + 2]] = true;
            }
            CollectionAssert.DoesNotContain(used, false, "Every vertex is in a triangle.");
        }

        [Test]
        public void Whole_Quad_Becomes_A_3x3_Grid()
        {
            var mesh = SpritePartsMeshOps.CreateQuad();
            int added = SpritePartsMeshOps.Subdivide(mesh, null, out var remap, out var parents);
            Assert.AreEqual(5, added, "4 outline lines + the diagonal.");
            Assert.AreEqual(9, mesh.VertexCount);
            Assert.AreEqual(8, mesh.HullCount, "Outline middles join the outline, in order.");
            Assert.AreEqual(new Vector2(0.5f, 0f), mesh.Vertices[1]);
            Assert.AreEqual(new Vector2(1f, 0f), mesh.Vertices[remap[1]]);
            Assert.AreEqual(new Vector2Int(0, 2), parents[1]);
            Assert.AreEqual(8, mesh.Triangles.Length / 3);
            AssertValid(mesh);
        }

        [Test]
        public void Selected_Triangle_Only_Splits_Its_Lines()
        {
            var mesh = SpritePartsMeshOps.CreateQuad();
            var tri = new List<int> { mesh.Triangles[0], mesh.Triangles[1], mesh.Triangles[2] };
            int added = SpritePartsMeshOps.Subdivide(mesh, tri, out _, out _);
            Assert.AreEqual(3, added, "2 outline lines + the diagonal.");
            Assert.AreEqual(6, mesh.HullCount);
            AssertValid(mesh);
            Assert.AreEqual(0, SpritePartsMeshOps.Subdivide(SpritePartsMeshOps.CreateQuad(), new List<int> { 0, 1 }, out _, out _),
                "Two corners are not a triangle.");
        }

        [Test]
        public void Weights_And_Deform_Take_The_Line_Average()
        {
            var mesh = SpritePartsMeshOps.CreateQuad();
            mesh.Bones = new[] { "a", "b" };
            mesh.Weights = new[] { 1f, 0f, 0f, 1f, 0f, 1f, 1f, 0f }; // left corners -> a, right corners -> b
            var deform = new[] { Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero };
            SpritePartsMeshOps.Subdivide(mesh, null, out var remap, out var parents);
            Assert.IsTrue(mesh.HasWeights);
            Assert.AreEqual(0.5f, mesh.Weights[1 * 2 + 0], 1e-5f, "Bottom middle is half a, half b.");
            Assert.AreEqual(0.5f, mesh.Weights[1 * 2 + 1], 1e-5f);
            int right = remap[1];
            Assert.AreEqual(1f, mesh.Weights[right * 2 + 1], 1e-5f, "Kept corners keep their weights.");
            var moved = SpritePartsMeshOps.SubdivideDeform(deform, remap, parents);
            Assert.AreEqual(new Vector2(0f, 0.5f), moved[1], "The middle sits halfway along the deformed line.");
            Assert.AreEqual(new Vector2(0f, 1f), moved[right]);
        }

        [Test]
        public void Stops_At_The_Vertex_Limit()
        {
            var mesh = SpritePartsMeshOps.CreateQuad();
            for (int pass = 0; pass < 5; pass++)
                SpritePartsMeshOps.Subdivide(mesh, null, out _, out _);
            Assert.AreEqual(SpritePartsMeshOps.MaxVertices, mesh.VertexCount);
            AssertValid(mesh);
            Assert.AreEqual(0, SpritePartsMeshOps.Subdivide(mesh, null, out _, out _), "A full mesh adds nothing.");
        }
    }
}
