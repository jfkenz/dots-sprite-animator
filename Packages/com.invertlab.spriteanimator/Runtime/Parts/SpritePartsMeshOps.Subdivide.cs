using System;
using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    // Subdivide: a vertex in the middle of every line of the chosen triangles, then the triangles are
    // rebuilt around them, so a bend has more joints without placing vertices by hand.
    public static partial class SpritePartsMeshOps
    {
        /// <summary>
        /// Splits every line of the triangles whose three corners are in <paramref name="only"/>
        /// (null or empty = the whole mesh) at its middle. Hull lines gain hull vertices, user edges are split
        /// in two, and weights of a new vertex are the average of its line's ends. With too little room left
        /// (<see cref="MaxVertices"/>) the longest lines are split first.
        /// <paramref name="parents"/>[new vertex] = the line's ends in new indices, or (-1, -1) for a kept vertex.
        /// Returns the number of vertices added.
        /// </summary>
        public static int Subdivide(SpritePartMeshDef mesh, IReadOnlyCollection<int> only, out int[] remap, out Vector2Int[] parents)
        {
            remap = null;
            parents = null;
            if (mesh == null || !mesh.HasMesh)
                return 0;
            int n = mesh.VertexCount;
            int h = mesh.HullCount;
            bool all = only == null || only.Count == 0;
            var chosen = new HashSet<int>();
            if (!all)
            {
                foreach (int i in only)
                    chosen.Add(i);
            }

            // Lines of the chosen triangles, longest first.
            var lines = new Dictionary<long, (int a, int b)>();
            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                int a = mesh.Triangles[t], b = mesh.Triangles[t + 1], c = mesh.Triangles[t + 2];
                if (!all && !(chosen.Contains(a) && chosen.Contains(b) && chosen.Contains(c)))
                    continue;
                AddLine(lines, a, b);
                AddLine(lines, b, c);
                AddLine(lines, c, a);
            }
            var order = new List<(int a, int b)>(lines.Values);
            order.RemoveAll(l => (mesh.Vertices[l.a] - mesh.Vertices[l.b]).sqrMagnitude < 1e-8f);
            order.Sort((x, y) => (mesh.Vertices[y.a] - mesh.Vertices[y.b]).sqrMagnitude
                .CompareTo((mesh.Vertices[x.a] - mesh.Vertices[x.b]).sqrMagnitude));
            int budget = MaxVertices - n;
            if (budget <= 0 || order.Count == 0)
                return 0;
            if (order.Count > budget)
                order.RemoveRange(budget, order.Count - budget);
            var split = new HashSet<long>();
            foreach (var l in order)
                split.Add(Key(l.a, l.b));

            // New order: hull (with its new middles in place), old interior, new interior middles.
            var verts = new List<Vector2>(n + order.Count);
            var oldParents = new List<(int a, int b)>(n + order.Count);
            remap = new int[n];
            for (int i = 0; i < h; i++)
            {
                remap[i] = verts.Count;
                verts.Add(mesh.Vertices[i]);
                oldParents.Add((-1, -1));
                int j = (i + 1) % h;
                if (split.Contains(Key(i, j)))
                {
                    verts.Add((mesh.Vertices[i] + mesh.Vertices[j]) * 0.5f);
                    oldParents.Add((i, j));
                }
            }
            int hull = verts.Count;
            for (int i = h; i < n; i++)
            {
                remap[i] = verts.Count;
                verts.Add(mesh.Vertices[i]);
                oldParents.Add((-1, -1));
            }
            var middle = new Dictionary<long, int>();
            for (int k = 0; k < oldParents.Count; k++)
            {
                if (oldParents[k].a >= 0)
                    middle[Key(oldParents[k].a, oldParents[k].b)] = k;
            }
            foreach (var l in order)
            {
                long key = Key(l.a, l.b);
                if (middle.ContainsKey(key))
                    continue; // hull line, placed above
                middle[key] = verts.Count;
                verts.Add((mesh.Vertices[l.a] + mesh.Vertices[l.b]) * 0.5f);
                oldParents.Add((l.a, l.b));
            }

            // User edges through a new middle become two edges.
            var edges = new List<int>();
            for (int i = 0; mesh.Edges != null && i + 1 < mesh.Edges.Length; i += 2)
            {
                int a = mesh.Edges[i], b = mesh.Edges[i + 1];
                if ((uint)a >= (uint)n || (uint)b >= (uint)n)
                    continue;
                if (middle.TryGetValue(Key(a, b), out int m))
                {
                    edges.Add(remap[a]); edges.Add(m);
                    edges.Add(m); edges.Add(remap[b]);
                }
                else
                {
                    edges.Add(remap[a]); edges.Add(remap[b]);
                }
            }

            int count = verts.Count;
            parents = new Vector2Int[count];
            for (int k = 0; k < count; k++)
            {
                var p = oldParents[k];
                parents[k] = p.a < 0 ? new Vector2Int(-1, -1) : new Vector2Int(remap[p.a], remap[p.b]);
            }
            var next = new SpritePartMeshDef
            {
                Vertices = verts.ToArray(),
                HullCount = hull,
                Edges = edges.ToArray(),
            };
            if (mesh.HasWeights)
            {
                // Exact: a middle takes the average of its line's ends.
                int bones = mesh.BoneCount;
                var w = new float[count * bones];
                for (int i = 0; i < n; i++)
                    Array.Copy(mesh.Weights, i * bones, w, remap[i] * bones, bones);
                for (int k = 0; k < count; k++)
                {
                    if (parents[k].x < 0)
                        continue;
                    for (int b = 0; b < bones; b++)
                        w[k * bones + b] = (w[parents[k].x * bones + b] + w[parents[k].y * bones + b]) * 0.5f;
                }
                next.Bones = mesh.Bones;
                next.Weights = w;
            }
            if (!RetriangulateKeeping(next, mesh, remap) && !Retriangulate(next))
            {
                remap = null;
                parents = null;
                return 0;
            }
            Assign(mesh, next, remap);
            return count - n;
        }

        /// <summary>Deform offsets after <see cref="Subdivide"/>: kept vertices keep theirs, a middle takes its line's average.</summary>
        public static Vector2[] SubdivideDeform(Vector2[] offsets, int[] remap, Vector2Int[] parents)
        {
            var result = RemapDeform(offsets, remap, parents.Length);
            if (result == null)
                return null;
            for (int k = 0; k < parents.Length; k++)
            {
                if (parents[k].x >= 0)
                    result[k] = (result[parents[k].x] + result[parents[k].y]) * 0.5f;
            }
            return result;
        }

        static void AddLine(Dictionary<long, (int a, int b)> lines, int a, int b)
        {
            long key = Key(a, b);
            if (!lines.ContainsKey(key))
                lines.Add(key, (Mathf.Min(a, b), Mathf.Max(a, b)));
        }
    }
}
