using NUnit.Framework;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Create tool drafts (AnyPortrait workflow): free vertices and edges, then Make Polygons.</summary>
    public sealed class SpritePartsMeshGraphTests
    {
        static SpritePartMeshDef Draft(params Vector2[] points)
        {
            var mesh = new SpritePartMeshDef();
            foreach (var p in points)
                Assert.IsTrue(SpritePartsMeshOps.TryAddGraphVertex(mesh, p, out _));
            return mesh;
        }

        /// <summary>Joins the points 0..count-1 into a closed loop.</summary>
        static void Loop(SpritePartMeshDef mesh, int count)
        {
            for (int i = 0; i < count; i++)
                Assert.IsTrue(SpritePartsMeshOps.TryConnectGraph(mesh, i, (i + 1) % count, false));
        }

        static float Area(SpritePartMeshDef mesh)
        {
            float area = 0f;
            for (int t = 0; t < mesh.Triangles.Length; t += 3)
            {
                Vector2 a = mesh.Vertices[mesh.Triangles[t]];
                Vector2 b = mesh.Vertices[mesh.Triangles[t + 1]];
                Vector2 c = mesh.Vertices[mesh.Triangles[t + 2]];
                area += ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
            }
            return area;
        }

        [Test]
        public void Vertices_And_Edges_Have_No_Polygons_Until_MakePolygons()
        {
            var mesh = Draft(new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.9f, 0.9f), new Vector2(0.1f, 0.9f));
            Loop(mesh, 4);
            Assert.IsTrue(SpritePartsMeshOps.IsDraft(mesh));
            Assert.IsFalse(mesh.HasMesh);
            Assert.AreEqual(8, mesh.Edges.Length);

            Assert.IsTrue(SpritePartsMeshOps.TryMakePolygons(mesh, out var remap, out _));
            Assert.IsTrue(mesh.HasMesh);
            Assert.AreEqual(4, mesh.HullCount);
            Assert.AreEqual(2, mesh.Triangles.Length / 3);
            Assert.AreEqual(0.64f, Area(mesh), 1e-4f);
            Assert.AreEqual(4, remap.Length);
        }

        [Test]
        public void MakePolygons_Needs_A_Closed_Loop()
        {
            var mesh = Draft(new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.5f, 0.9f));
            Assert.IsTrue(SpritePartsMeshOps.TryConnectGraph(mesh, 0, 1, false));
            Assert.IsTrue(SpritePartsMeshOps.TryConnectGraph(mesh, 1, 2, false));
            Assert.IsFalse(SpritePartsMeshOps.TryMakePolygons(mesh, out _, out string message));
            Assert.IsNotEmpty(message);
            Assert.IsTrue(SpritePartsMeshOps.IsDraft(mesh), "A failed Make Polygons leaves the draft alone.");
        }

        [Test]
        public void MakePolygons_Fills_Concave_Loop_Keeps_Inner_Vertex_Drops_Outside_Vertex()
        {
            // L shape, one vertex inside it joined by an edge, one stray vertex outside.
            var mesh = Draft(
                new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.9f, 0.4f),
                new Vector2(0.4f, 0.4f), new Vector2(0.4f, 0.9f), new Vector2(0.1f, 0.9f),
                new Vector2(0.25f, 0.25f), new Vector2(0.8f, 0.8f));
            Loop(mesh, 6);
            Assert.IsTrue(SpritePartsMeshOps.TryConnectGraph(mesh, 0, 6, false));

            Assert.IsTrue(SpritePartsMeshOps.TryMakePolygons(mesh, out var remap, out _));
            Assert.AreEqual(6, mesh.HullCount);
            Assert.AreEqual(7, mesh.VertexCount);
            Assert.AreEqual(-1, remap[7], "The vertex outside the loop is removed.");
            Assert.GreaterOrEqual(remap[6], 6, "The inner vertex comes after the hull.");
            Assert.AreEqual(0.8f * 0.3f + 0.3f * 0.5f, Area(mesh), 1e-4f, "The notch of the L stays empty.");
            Assert.IsTrue(SpritePartsMeshOps.HasEdge(mesh, remap[0], remap[6]), "Inner edges are kept.");
        }

        [Test]
        public void Crossing_Edges_Meet_At_A_New_Vertex()
        {
            var mesh = Draft(new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.9f, 0.9f), new Vector2(0.1f, 0.9f));
            Loop(mesh, 4);
            SpritePartsMeshOps.TryConnectGraph(mesh, 0, 2, false);
            SpritePartsMeshOps.TryConnectGraph(mesh, 1, 3, false);
            Assert.IsTrue(SpritePartsMeshOps.TryMakePolygons(mesh, out _, out _));
            Assert.AreEqual(5, mesh.VertexCount);
            Assert.AreEqual(4, mesh.Triangles.Length / 3);
            Assert.AreEqual(new Vector2(0.5f, 0.5f).x, mesh.Vertices[4].x, 1e-4f);
        }

        [Test]
        public void Shift_Connect_Adds_A_Vertex_On_Each_Crossed_Edge()
        {
            var mesh = Draft(new Vector2(0.5f, 0.1f), new Vector2(0.5f, 0.9f), new Vector2(0.1f, 0.5f), new Vector2(0.9f, 0.5f));
            SpritePartsMeshOps.TryConnectGraph(mesh, 0, 1, false);
            Assert.IsTrue(SpritePartsMeshOps.TryConnectGraph(mesh, 2, 3, true));
            Assert.AreEqual(5, mesh.VertexCount);
            Assert.IsTrue(SpritePartsMeshOps.HasEdge(mesh, 2, 4));
            Assert.IsTrue(SpritePartsMeshOps.HasEdge(mesh, 4, 3));
            Assert.IsTrue(SpritePartsMeshOps.HasEdge(mesh, 0, 4));
            Assert.IsFalse(SpritePartsMeshOps.HasEdge(mesh, 0, 1));
        }

        [Test]
        public void Vertex_On_An_Edge_Splits_It_And_Joins_The_Outline()
        {
            var mesh = Draft(new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.9f, 0.9f), new Vector2(0.1f, 0.9f));
            Loop(mesh, 4);
            Assert.IsTrue(SpritePartsMeshOps.TrySplitGraphEdge(mesh, 0, 1, new Vector2(0.5f, 0.13f), out int x));
            Assert.AreEqual(0.1f, mesh.Vertices[x].y, 1e-5f, "Projected onto the edge.");
            Assert.IsTrue(SpritePartsMeshOps.TryMakePolygons(mesh, out _, out _));
            Assert.AreEqual(5, mesh.HullCount);
            Assert.AreEqual(0.64f, Area(mesh), 1e-4f);
        }

        [Test]
        public void ToDraft_Keeps_The_Outline_And_Your_Edges_Not_The_Hidden_Diagonal()
        {
            var mesh = SpritePartsMeshOps.CreateQuad();
            Assert.IsTrue(SpritePartsMeshOps.ToDraft(mesh));
            Assert.IsTrue(SpritePartsMeshOps.IsDraft(mesh));
            Assert.AreEqual(4 * 2, mesh.Edges.Length, "The 4 outline edges; the automatic diagonal stays hidden.");
            Assert.IsTrue(SpritePartsMeshOps.TryMakePolygons(mesh, out var remap, out _));
            Assert.AreEqual(4, mesh.VertexCount);
            Assert.AreEqual(1f, Area(mesh), 1e-4f);
            Assert.AreNotEqual(-1, remap[0]);
        }

        [Test]
        public void TurnEdge_Flips_The_Diagonal()
        {
            var mesh = SpritePartsMeshOps.CreateQuad();
            SpritePartsMeshOps.ToDraft(mesh);
            SpritePartsMeshOps.TryAddEdge(mesh, 0, 2); // a drawn diagonal
            int a = -1, b = -1;
            foreach (var e in SpritePartsMeshOps.GraphEdges(mesh))
            {
                if ((e.x + 2) % 4 == e.y || (e.y + 2) % 4 == e.x)
                {
                    a = e.x;
                    b = e.y;
                }
            }
            Assert.GreaterOrEqual(a, 0);
            Assert.IsTrue(SpritePartsMeshOps.TryTurnGraphEdge(mesh, a, b));
            Assert.IsFalse(SpritePartsMeshOps.HasEdge(mesh, a, b));
            Assert.IsTrue(SpritePartsMeshOps.HasEdge(mesh, (a + 1) % 4, (a + 3) % 4));
        }

        [Test]
        public void RemoveVertex_With_Shift_Keeps_The_Line()
        {
            var mesh = Draft(new Vector2(0.1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.9f, 0.5f));
            SpritePartsMeshOps.TryConnectGraph(mesh, 0, 1, false);
            SpritePartsMeshOps.TryConnectGraph(mesh, 1, 2, false);
            Assert.IsTrue(SpritePartsMeshOps.TryRemoveGraphVertex(mesh, 1, true, out var remap));
            Assert.AreEqual(2, mesh.VertexCount);
            Assert.AreEqual(-1, remap[1]);
            Assert.IsTrue(SpritePartsMeshOps.HasEdge(mesh, 0, 1));

            var plain = Draft(new Vector2(0.1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.9f, 0.5f));
            SpritePartsMeshOps.TryConnectGraph(plain, 0, 1, false);
            SpritePartsMeshOps.TryConnectGraph(plain, 1, 2, false);
            Assert.IsTrue(SpritePartsMeshOps.TryRemoveGraphVertex(plain, 1, false, out _));
            Assert.AreEqual(0, plain.Edges.Length);
        }

        [Test]
        public void Merge_Joins_Vertices_At_Their_Centre_And_Keeps_Their_Edges()
        {
            var mesh = Draft(new Vector2(0.1f, 0.5f), new Vector2(0.4f, 0.5f), new Vector2(0.6f, 0.5f), new Vector2(0.9f, 0.5f));
            SpritePartsMeshOps.TryConnectGraph(mesh, 0, 1, false);
            SpritePartsMeshOps.TryConnectGraph(mesh, 1, 2, false);
            SpritePartsMeshOps.TryConnectGraph(mesh, 2, 3, false);
            Assert.IsTrue(SpritePartsMeshOps.TryMergeGraphVertices(mesh, new[] { 2, 1 }, out int survivor, out var remap));
            Assert.AreEqual(3, mesh.VertexCount);
            Assert.AreEqual(1, survivor);
            Assert.AreEqual(-1, remap[2], "Merged away into vertex 1.");
            Assert.AreEqual(2, remap[3]);
            Assert.AreEqual(0.5f, mesh.Vertices[survivor].x, 1e-5f);
            Assert.IsTrue(SpritePartsMeshOps.HasEdge(mesh, 0, 1));
            Assert.IsTrue(SpritePartsMeshOps.HasEdge(mesh, 1, 2));
            Assert.AreEqual(4, mesh.Edges.Length, "The edge between the merged vertices is gone.");
        }

        [Test]
        public void Delete_Several_Vertices_Gives_One_Remap()
        {
            var mesh = Draft(new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.9f, 0.9f), new Vector2(0.1f, 0.9f));
            Loop(mesh, 4);
            Assert.IsTrue(SpritePartsMeshOps.TryRemoveGraphVertices(mesh, new[] { 0, 2 }, false, out var remap));
            CollectionAssert.AreEqual(new[] { -1, 0, -1, 1 }, remap);
            Assert.AreEqual(0, mesh.Edges.Length);
        }

        [Test]
        public void Vertices_May_Sit_Past_The_Image_Edge()
        {
            var mesh = Draft(new Vector2(-0.3f, -0.2f), new Vector2(1.4f, 0f), new Vector2(0.5f, 1.25f), new Vector2(5f, 5f));
            Assert.AreEqual(-0.3f, mesh.Vertices[0].x, 1e-6f);
            Assert.AreEqual(1.25f, mesh.Vertices[2].y, 1e-6f);
            Assert.AreEqual(SpritePartsMeshOps.MaxUv, mesh.Vertices[3].x, 1e-6f, "Clamped to one image size past the edge.");
            Loop(mesh, 3);
            SpritePartsMeshOps.TryRemoveGraphVertex(mesh, 3, false, out _);
            Assert.IsTrue(SpritePartsMeshOps.TryMakePolygons(mesh, out _, out _));
            var lattice = SpritePartsLattice.FromMesh(mesh);
            Assert.IsTrue(lattice.HasMesh);
            Assert.AreEqual(-0.3f, lattice.GetUv(0).x + lattice.GetUv(1).x + lattice.GetUv(2).x - 1.4f - 0.5f, 1e-5f);
        }

        [Test]
        public void AutoLink_Fills_The_Loop_With_Edges()
        {
            var mesh = Draft(
                new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.9f, 0.9f), new Vector2(0.1f, 0.9f),
                new Vector2(0.5f, 0.5f));
            Loop(mesh, 4);
            Assert.AreEqual(4, SpritePartsMeshOps.AutoLinkGraph(mesh), "The centre vertex joins all four corners.");
            Assert.IsTrue(SpritePartsMeshOps.TryMakePolygons(mesh, out _, out _));
            Assert.AreEqual(4, mesh.Triangles.Length / 3);
        }
    }
}
