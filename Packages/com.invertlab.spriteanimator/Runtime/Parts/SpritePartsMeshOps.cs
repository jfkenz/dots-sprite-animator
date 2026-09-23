using System;
using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Setup-mesh editing, following the Spine mesh workflow:
    /// the hull is an ordered outline, interior vertices sit inside it, user edges steer
    /// the triangulation, and triangles are always generated (constrained Delaunay).
    /// Every topology change returns a remap (old index to new index, -1 = removed) so
    /// deform keys can follow with <see cref="RemapDeform"/>.
    /// Coordinates are texture space of the part image: 0..1, y up.
    /// </summary>
    public static partial class SpritePartsMeshOps
    {
        public const int MaxVertices = SpritePartsLattice.MaxVertices;
        const float Eps = 1e-7f;

        public static SpritePartMeshDef CreateQuad()
        {
            var mesh = new SpritePartMeshDef
            {
                Vertices = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f),
                },
                HullCount = 4,
                Edges = Array.Empty<int>(),
            };
            Retriangulate(mesh);
            return mesh;
        }

        /// <summary>Hull from a traced outline. Returns null when the outline cannot be triangulated.</summary>
        public static SpritePartMeshDef FromOutline(IReadOnlyList<Vector2> outline)
        {
            if (outline == null || outline.Count < 3)
                return null;
            int n = Mathf.Min(outline.Count, MaxVertices);
            var verts = new Vector2[n];
            for (int i = 0; i < n; i++)
                verts[i] = Clamp01(outline[i]);
            var mesh = new SpritePartMeshDef { Vertices = verts, HullCount = n, Edges = Array.Empty<int>() };
            return Retriangulate(mesh) ? mesh : null;
        }

        /// <summary>Rebuilds <see cref="SpritePartMeshDef.Triangles"/>. False when the hull is not a simple polygon.</summary>
        public static bool Retriangulate(SpritePartMeshDef mesh)
        {
            if (mesh == null)
                return false;
            var tris = Triangulate(mesh.Vertices, mesh.HullCount, mesh.Edges);
            if (tris == null)
                return false;
            mesh.Triangles = tris;
            return true;
        }

        public static bool HasEdge(SpritePartMeshDef mesh, int a, int b)
        {
            if (mesh?.Edges == null)
                return false;
            for (int i = 0; i + 1 < mesh.Edges.Length; i += 2)
            {
                if ((mesh.Edges[i] == a && mesh.Edges[i + 1] == b) || (mesh.Edges[i] == b && mesh.Edges[i + 1] == a))
                    return true;
            }
            return false;
        }

        public static bool IsHullEdge(SpritePartMeshDef mesh, int a, int b)
        {
            int h = mesh?.HullCount ?? 0;
            if ((uint)a >= (uint)h || (uint)b >= (uint)h)
                return false;
            return (a + 1) % h == b || (b + 1) % h == a;
        }

        public static bool IsInsideHull(SpritePartMeshDef mesh, Vector2 p)
        {
            if (mesh?.Vertices == null || mesh.HullCount < 3)
                return false;
            bool inside = false;
            int h = mesh.HullCount;
            for (int i = 0, j = h - 1; i < h; j = i++)
            {
                Vector2 a = mesh.Vertices[i];
                Vector2 b = mesh.Vertices[j];
                if ((a.y > p.y) != (b.y > p.y) &&
                    p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Hull edge i runs from hull vertex i to i+1. Distance is in texture space.</summary>
        public static int NearestHullEdge(SpritePartMeshDef mesh, Vector2 p, out float distance)
        {
            distance = float.MaxValue;
            int best = -1;
            int h = mesh?.HullCount ?? 0;
            for (int i = 0; i < h; i++)
            {
                float d = DistanceToSegment(p, mesh.Vertices[i], mesh.Vertices[(i + 1) % h]);
                if (d >= distance)
                    continue;
                distance = d;
                best = i;
            }
            return best;
        }

        /// <summary>Inserts a hull vertex after hull vertex <paramref name="hullEdge"/>.</summary>
        public static bool TryInsertHullVertex(SpritePartMeshDef mesh, int hullEdge, Vector2 uv, out int index, out int[] remap)
        {
            index = -1;
            remap = null;
            int n = mesh?.VertexCount ?? 0;
            if (n >= MaxVertices || (uint)hullEdge >= (uint)mesh.HullCount)
                return false;
            int at = hullEdge + 1;
            var verts = new List<Vector2>(mesh.Vertices);
            verts.Insert(at, Clamp01(uv));
            remap = new int[n];
            for (int i = 0; i < n; i++)
                remap[i] = i < at ? i : i + 1;
            var next = new SpritePartMeshDef
            {
                Vertices = verts.ToArray(),
                HullCount = mesh.HullCount + 1,
                Edges = RemapEdges(mesh.Edges, remap),
            };
            if (!Retriangulate(next))
                return false;
            Assign(mesh, next, remap);
            index = at;
            return true;
        }

        /// <summary>
        /// Hull still being drawn (no triangles yet): adds the next outline point in click order.
        /// Triangles appear once three points make a valid polygon.
        /// </summary>
        public static bool TryAppendHullVertex(SpritePartMeshDef mesh, Vector2 uv, out int index)
        {
            index = -1;
            if (mesh == null || mesh.VertexCount >= MaxVertices || mesh.VertexCount != mesh.HullCount)
                return false;
            var verts = new List<Vector2>(mesh.Vertices ?? Array.Empty<Vector2>()) { Clamp01(uv) };
            mesh.Vertices = verts.ToArray();
            mesh.HullCount = verts.Count;
            mesh.Edges ??= Array.Empty<int>();
            mesh.Triangles = verts.Count >= 3 ? Triangulate(mesh.Vertices, mesh.HullCount, mesh.Edges) : null;
            index = verts.Count - 1;
            return true;
        }

        /// <summary>
        /// Adds a hull vertex outside the hull on the edge that grows the outline least
        /// without making it cross itself.
        /// </summary>
        public static bool TryInsertHullVertexAuto(SpritePartMeshDef mesh, Vector2 uv, out int index, out int[] remap)
        {
            index = -1;
            remap = null;
            int h = mesh?.HullCount ?? 0;
            if (h < 3)
                return false;
            var order = new List<(float cost, int edge)>(h);
            for (int i = 0; i < h; i++)
            {
                Vector2 a = mesh.Vertices[i];
                Vector2 b = mesh.Vertices[(i + 1) % h];
                order.Add((Vector2.Distance(a, uv) + Vector2.Distance(uv, b) - Vector2.Distance(a, b), i));
            }
            order.Sort((x, y) => x.cost.CompareTo(y.cost));
            foreach (var (_, edge) in order)
            {
                if (TryInsertHullVertex(mesh, edge, uv, out index, out remap))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Free points (no drawn outline): the convex hull becomes the outline, every other point an
        /// interior vertex. <paramref name="remap"/>[i] = new index of input point i.
        /// With fewer than 3 non-collinear points the result has no triangles yet.
        /// </summary>
        public static SpritePartMeshDef FromPoints(IReadOnlyList<Vector2> points, out int[] remap)
        {
            int n = Mathf.Min(points?.Count ?? 0, MaxVertices);
            remap = new int[n];
            if (n == 0)
                return new SpritePartMeshDef { Vertices = Array.Empty<Vector2>(), Edges = Array.Empty<int>() };
            var hull = ConvexHull(points, n);
            bool closed = hull.Count >= 3;
            var used = new bool[n];
            var verts = new List<Vector2>(n);
            if (closed)
            {
                foreach (int i in hull)
                {
                    remap[i] = verts.Count;
                    verts.Add(Clamp01(points[i]));
                    used[i] = true;
                }
            }
            for (int i = 0; i < n; i++)
            {
                if (used[i])
                    continue;
                remap[i] = verts.Count;
                verts.Add(Clamp01(points[i]));
            }
            var mesh = new SpritePartMeshDef
            {
                Vertices = verts.ToArray(),
                HullCount = closed ? hull.Count : verts.Count,
                Edges = Array.Empty<int>(),
            };
            if (closed)
                Retriangulate(mesh);
            return mesh;
        }

        /// <summary>
        /// Puts a vertex on edge (a, b) at <paramref name="uv"/> (projected onto the edge).
        /// A hull edge gains a hull vertex; a user edge is replaced by two edges through the new vertex.
        /// </summary>
        public static bool TrySplitEdgeAt(SpritePartMeshDef mesh, int a, int b, Vector2 uv, out int index, out int[] remap)
        {
            index = -1;
            remap = null;
            int n = mesh?.VertexCount ?? 0;
            if (a == b || (uint)a >= (uint)n || (uint)b >= (uint)n)
                return false;
            Vector2 pa = mesh.Vertices[a];
            Vector2 ab = mesh.Vertices[b] - pa;
            float t = Mathf.Clamp(Vector2.Dot(uv - pa, ab) / Mathf.Max(1e-10f, ab.sqrMagnitude), 0.02f, 0.98f);
            Vector2 p = pa + ab * t;
            if (IsHullEdge(mesh, a, b))
            {
                int h = mesh.HullCount;
                int edge = (a + 1) % h == b ? a : b;
                return TryInsertHullVertex(mesh, edge, p, out index, out remap);
            }
            if (!HasEdge(mesh, a, b))
                return false;
            var work = mesh.Clone();
            if (!TryAddInteriorVertex(work, p, out index, out remap))
                return false;
            var edges = new List<int>(work.Edges.Length + 2);
            for (int i = 0; i + 1 < work.Edges.Length; i += 2)
            {
                int ea = work.Edges[i], eb = work.Edges[i + 1];
                if ((ea == a && eb == b) || (ea == b && eb == a))
                    continue;
                edges.Add(ea);
                edges.Add(eb);
            }
            edges.Add(a); edges.Add(index);
            edges.Add(index); edges.Add(b);
            work.Edges = edges.ToArray();
            if (!Retriangulate(work))
                return false;
            Assign(mesh, work);
            return true;
        }

        /// <summary>
        /// Welds <paramref name="drop"/> into <paramref name="keep"/> at their midpoint.
        /// Edges of the dropped vertex move to the kept one.
        /// </summary>
        public static bool TryMergeVertices(SpritePartMeshDef mesh, int keep, int drop, out int survivor, out int[] remap)
        {
            survivor = -1;
            remap = null;
            int n = mesh?.VertexCount ?? 0;
            if (keep == drop || (uint)keep >= (uint)n || (uint)drop >= (uint)n)
                return false;
            var work = mesh.Clone();
            work.Vertices[keep] = (mesh.Vertices[keep] + mesh.Vertices[drop]) * 0.5f;
            var edges = new List<int>();
            for (int i = 0; work.Edges != null && i + 1 < work.Edges.Length; i += 2)
            {
                int ea = work.Edges[i] == drop ? keep : work.Edges[i];
                int eb = work.Edges[i + 1] == drop ? keep : work.Edges[i + 1];
                if (ea == eb)
                    continue;
                bool duplicate = false;
                for (int k = 0; k + 1 < edges.Count; k += 2)
                    duplicate |= (edges[k] == ea && edges[k + 1] == eb) || (edges[k] == eb && edges[k + 1] == ea);
                if (!duplicate)
                {
                    edges.Add(ea);
                    edges.Add(eb);
                }
            }
            work.Edges = edges.ToArray();
            if (!TryRemoveVertices(work, new[] { drop }, out remap))
                return false;
            survivor = remap[keep];
            Assign(mesh, work);
            return survivor >= 0;
        }

        /// <summary>Where segment a-b crosses segment c-d strictly inside both (t along a-b).</summary>
        public static bool TrySegmentHit(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t, out Vector2 point)
        {
            t = 0f;
            point = default;
            Vector2 r = b - a;
            Vector2 s = d - c;
            float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-10f)
                return false;
            Vector2 ca = c - a;
            float along = (ca.x * s.y - ca.y * s.x) / den;
            float edge = (ca.x * r.y - ca.y * r.x) / den;
            if (along <= 0.01f || along >= 0.99f || edge <= 0.01f || edge >= 0.99f)
                return false;
            t = along;
            point = a + r * along;
            return true;
        }

        public static bool TryAddInteriorVertex(SpritePartMeshDef mesh, Vector2 uv, out int index, out int[] remap)
        {
            index = -1;
            remap = null;
            int n = mesh?.VertexCount ?? 0;
            if (n >= MaxVertices || !IsInsideHull(mesh, uv))
                return false;
            var verts = new Vector2[n + 1];
            Array.Copy(mesh.Vertices, verts, n);
            verts[n] = uv;
            var next = new SpritePartMeshDef
            {
                Vertices = verts,
                HullCount = mesh.HullCount,
                Edges = mesh.Edges == null ? Array.Empty<int>() : (int[])mesh.Edges.Clone(),
            };
            if (!Retriangulate(next))
                return false;
            remap = Identity(n);
            Assign(mesh, next);
            index = n;
            return true;
        }

        /// <summary>Deletes vertices. Fails when fewer than three hull vertices would remain.</summary>
        public static bool TryRemoveVertices(SpritePartMeshDef mesh, IReadOnlyCollection<int> remove, out int[] remap)
        {
            remap = null;
            int n = mesh?.VertexCount ?? 0;
            if (n == 0 || remove == null || remove.Count == 0)
                return false;
            var drop = new bool[n];
            int hullDropped = 0;
            foreach (int i in remove)
            {
                if ((uint)i >= (uint)n || drop[i])
                    continue;
                drop[i] = true;
                if (i < mesh.HullCount)
                    hullDropped++;
            }
            bool pending = !mesh.HasMesh;
            if (!pending && mesh.HullCount - hullDropped < 3)
                return false;
            remap = new int[n];
            var verts = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                if (drop[i])
                {
                    remap[i] = -1;
                    continue;
                }
                remap[i] = verts.Count;
                verts.Add(mesh.Vertices[i]);
            }
            var next = new SpritePartMeshDef
            {
                Vertices = verts.ToArray(),
                HullCount = mesh.HullCount - hullDropped,
                Edges = RemapEdges(mesh.Edges, remap),
            };
            if (pending)
            {
                // Outline still being drawn: keep the remaining points, triangulate when possible.
                next.Triangles = next.HullCount >= 3 ? Triangulate(next.Vertices, next.HullCount, next.Edges) : null;
                Assign(mesh, next, remap);
                return true;
            }
            if (!Retriangulate(next))
                return false;
            Assign(mesh, next, remap);
            return true;
        }

        /// <summary>Adds a user edge. Hull edges already exist and return true.</summary>
        public static bool TryAddEdge(SpritePartMeshDef mesh, int a, int b)
        {
            int n = mesh?.VertexCount ?? 0;
            if (a == b || (uint)a >= (uint)n || (uint)b >= (uint)n)
                return false;
            if (IsHullEdge(mesh, a, b) || HasEdge(mesh, a, b))
                return true;
            var edges = new List<int>(mesh.Edges ?? Array.Empty<int>()) { a, b };
            var next = mesh.Clone();
            next.Edges = edges.ToArray();
            if (IsDraft(mesh))
            {
                mesh.Edges = next.Edges; // draft: no triangles until Make Polygons
                return true;
            }
            if (!Retriangulate(next))
                return false;
            Assign(mesh, next);
            return true;
        }

        public static bool TryRemoveEdge(SpritePartMeshDef mesh, int a, int b)
        {
            if (!HasEdge(mesh, a, b))
                return false;
            var edges = new List<int>(mesh.Edges.Length);
            for (int i = 0; i + 1 < mesh.Edges.Length; i += 2)
            {
                int ea = mesh.Edges[i];
                int eb = mesh.Edges[i + 1];
                if ((ea == a && eb == b) || (ea == b && eb == a))
                    continue;
                edges.Add(ea);
                edges.Add(eb);
            }
            var next = mesh.Clone();
            next.Edges = edges.ToArray();
            if (IsDraft(mesh))
            {
                mesh.Edges = next.Edges;
                return true;
            }
            if (!Retriangulate(next))
                return false;
            Assign(mesh, next);
            return true;
        }

        /// <summary>
        /// Moves vertices in texture space (the image stays put, the mesh slides over it).
        /// Fails, leaving the mesh unchanged, when the hull would cross itself.
        /// </summary>
        public static bool TrySetVertices(SpritePartMeshDef mesh, IReadOnlyList<int> indices, IReadOnlyList<Vector2> positions)
        {
            if (mesh?.Vertices == null || indices == null || positions == null || indices.Count != positions.Count)
                return false;
            var next = mesh.Clone();
            for (int i = 0; i < indices.Count; i++)
            {
                if ((uint)indices[i] < (uint)next.Vertices.Length)
                    next.Vertices[indices[i]] = Clamp01(positions[i]);
            }
            if (!Retriangulate(next))
                return false;
            Assign(mesh, next);
            return true;
        }

        /// <summary>
        /// Spine's Generate: fills the hull with evenly spaced interior vertices up to <paramref name="budget"/>.
        /// Returns the number added.
        /// </summary>
        public static int GenerateInterior(SpritePartMeshDef mesh, int budget, out int[] remap)
        {
            remap = Identity(mesh?.VertexCount ?? 0);
            if (mesh?.Vertices == null || mesh.HullCount < 3)
                return 0;
            budget = Mathf.Min(budget, MaxVertices - mesh.VertexCount);
            if (budget <= 0)
                return 0;
            Vector2 min = mesh.Vertices[0];
            Vector2 max = mesh.Vertices[0];
            for (int i = 1; i < mesh.HullCount; i++)
            {
                min = Vector2.Min(min, mesh.Vertices[i]);
                max = Vector2.Max(max, mesh.Vertices[i]);
            }
            Vector2 size = max - min;
            if (size.x < 1e-4f || size.y < 1e-4f)
                return 0;

            var verts = new List<Vector2>(mesh.Vertices);
            int added = 0;
            // Shrink the grid until enough candidates land inside the hull.
            for (int cells = 2; cells <= 8 && added < budget; cells++)
            {
                var candidates = new List<Vector2>();
                for (int y = 1; y < cells; y++)
                {
                    for (int x = 1; x < cells; x++)
                    {
                        var p = new Vector2(min.x + size.x * x / cells, min.y + size.y * y / cells);
                        if (IsInsideHull(mesh, p))
                            candidates.Add(p);
                    }
                }
                if (candidates.Count < budget && cells < 8)
                    continue;
                float minGap = Mathf.Min(size.x, size.y) / (cells * 2f);
                foreach (var p in candidates)
                {
                    if (added >= budget)
                        break;
                    if (NearestDistance(verts, p) < minGap)
                        continue;
                    if (DistanceToHull(mesh, p) < minGap)
                        continue;
                    verts.Add(p);
                    added++;
                }
                break;
            }
            if (added == 0)
                return 0;
            var next = mesh.Clone();
            next.Vertices = verts.ToArray();
            if (!Retriangulate(next))
                return 0;
            Assign(mesh, next);
            return added;
        }

        /// <summary>Carries deform offsets across a topology change. New vertices start at zero.</summary>
        public static Vector2[] RemapDeform(Vector2[] offsets, int[] remap, int newCount)
        {
            if (offsets == null || remap == null || offsets.Length != remap.Length)
                return null;
            var result = new Vector2[newCount];
            bool any = false;
            for (int i = 0; i < remap.Length; i++)
            {
                int to = remap[i];
                if ((uint)to >= (uint)newCount)
                    continue;
                result[to] = offsets[i];
                any |= offsets[i].sqrMagnitude > 1e-12f;
            }
            return any ? result : null;
        }

        // ----- triangulation -----

        /// <summary>
        /// Constrained Delaunay triangulation of a hull polygon (vertices 0..hullCount-1 in order)
        /// plus interior vertices, keeping user edges where possible. Output winding is CCW.
        /// Returns null when the hull is not a simple polygon.
        /// </summary>
        public static int[] Triangulate(Vector2[] v, int hullCount, int[] edges)
        {
            if (v == null || hullCount < 3 || hullCount > v.Length)
                return null;
            var tris = new List<int>();
            if (!EarClip(v, hullCount, tris))
                return null;
            var constrained = new HashSet<long>();
            if (edges != null)
            {
                for (int i = 0; i + 1 < edges.Length; i += 2)
                {
                    if ((uint)edges[i] < (uint)v.Length && (uint)edges[i + 1] < (uint)v.Length)
                        constrained.Add(Key(edges[i], edges[i + 1]));
                }
            }

            Legalize(v, tris, constrained);
            for (int i = hullCount; i < v.Length; i++)
            {
                InsertPoint(v, tris, i);
                Legalize(v, tris, constrained);
            }
            if (edges != null)
            {
                for (int i = 0; i + 1 < edges.Length; i += 2)
                {
                    int a = edges[i];
                    int b = edges[i + 1];
                    if ((uint)a >= (uint)v.Length || (uint)b >= (uint)v.Length || a == b)
                        continue;
                    RecoverEdge(v, tris, a, b);
                }
                Legalize(v, tris, constrained);
            }
            if (tris.Count > SpritePartsLattice.MaxIndices)
                return null;
            return tris.ToArray();
        }

        static bool EarClip(Vector2[] v, int h, List<int> tris)
        {
            var ring = new List<int>(h);
            float area = 0f;
            for (int i = 0; i < h; i++)
            {
                ring.Add(i);
                Vector2 p = v[i];
                Vector2 q = v[(i + 1) % h];
                area += p.x * q.y - q.x * p.y;
            }
            if (Mathf.Abs(area) < Eps)
                return false;
            if (area < 0f)
                ring.Reverse();
            if (HullSelfIntersects(v, h))
                return false;

            int guard = h * h + 8;
            while (ring.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < ring.Count; i++)
                {
                    int a = ring[(i + ring.Count - 1) % ring.Count];
                    int b = ring[i];
                    int c = ring[(i + 1) % ring.Count];
                    float o = Orient(v[a], v[b], v[c]);
                    if (o <= Eps)
                    {
                        // A straight run: drop the middle vertex only when no ear is left elsewhere.
                        continue;
                    }
                    bool blocked = false;
                    for (int k = 0; k < ring.Count; k++)
                    {
                        int id = ring[k];
                        if (id == a || id == b || id == c)
                            continue;
                        if (PointInTriangleInclusive(v[id], v[a], v[b], v[c]))
                        {
                            blocked = true;
                            break;
                        }
                    }
                    if (blocked)
                        continue;
                    tris.Add(a);
                    tris.Add(b);
                    tris.Add(c);
                    ring.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (clipped)
                    continue;
                // Only collinear corners remain clip-able: remove one flat vertex.
                bool removed = false;
                for (int i = 0; i < ring.Count; i++)
                {
                    int a = ring[(i + ring.Count - 1) % ring.Count];
                    int b = ring[i];
                    int c = ring[(i + 1) % ring.Count];
                    if (Mathf.Abs(Orient(v[a], v[b], v[c])) <= Eps)
                    {
                        ring.RemoveAt(i);
                        removed = true;
                        break;
                    }
                }
                if (!removed)
                    return false;
            }
            if (ring.Count == 3 && Orient(v[ring[0]], v[ring[1]], v[ring[2]]) > Eps)
            {
                tris.Add(ring[0]);
                tris.Add(ring[1]);
                tris.Add(ring[2]);
            }
            return tris.Count >= 3;
        }

        static bool HullSelfIntersects(Vector2[] v, int h)
        {
            for (int i = 0; i < h; i++)
            {
                Vector2 a = v[i];
                Vector2 b = v[(i + 1) % h];
                for (int j = i + 1; j < h; j++)
                {
                    if (j == i || (j + 1) % h == i || (i + 1) % h == j)
                        continue;
                    if (SegmentsCross(a, b, v[j], v[(j + 1) % h]))
                        return true;
                }
            }
            return false;
        }

        static void InsertPoint(Vector2[] v, List<int> tris, int p)
        {
            Vector2 pt = v[p];
            for (int t = 0; t < tris.Count; t += 3)
            {
                int a = tris[t];
                int b = tris[t + 1];
                int c = tris[t + 2];
                if (DistanceSq(pt, v[a]) < 1e-10f || DistanceSq(pt, v[b]) < 1e-10f || DistanceSq(pt, v[c]) < 1e-10f)
                    return;
                float area = Orient(v[a], v[b], v[c]);
                if (area <= Eps)
                    continue;
                float wa = Orient(v[b], v[c], pt) / area;
                float wb = Orient(v[c], v[a], pt) / area;
                float wc = Orient(v[a], v[b], pt) / area;
                const float onEdge = 1e-5f;
                if (wa < -onEdge || wb < -onEdge || wc < -onEdge)
                    continue;

                // On an edge: split this triangle and the neighbour across that edge.
                int e0 = -1, e1 = -1, opp = -1;
                if (wa <= onEdge) { e0 = b; e1 = c; opp = a; }
                else if (wb <= onEdge) { e0 = c; e1 = a; opp = b; }
                else if (wc <= onEdge) { e0 = a; e1 = b; opp = c; }
                if (e0 >= 0)
                {
                    tris.RemoveRange(t, 3);
                    AddTri(tris, e0, p, opp);
                    AddTri(tris, p, e1, opp);
                    int n = FindTriWithEdge(tris, e1, e0, out int nOpp);
                    if (n >= 0)
                    {
                        tris.RemoveRange(n, 3);
                        AddTri(tris, e1, p, nOpp);
                        AddTri(tris, p, e0, nOpp);
                    }
                    return;
                }
                tris.RemoveRange(t, 3);
                AddTri(tris, a, b, p);
                AddTri(tris, b, c, p);
                AddTri(tris, c, a, p);
                return;
            }
        }

        /// <summary>Lawson flips until every unconstrained interior edge is locally Delaunay.</summary>
        static void Legalize(Vector2[] v, List<int> tris, HashSet<long> constrained)
        {
            for (int pass = 0; pass < 64; pass++)
            {
                bool flipped = false;
                for (int t = 0; t < tris.Count; t += 3)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int a = tris[t + e];
                        int b = tris[t + (e + 1) % 3];
                        int c = tris[t + (e + 2) % 3];
                        if (constrained.Contains(Key(a, b)))
                            continue;
                        int n = FindTriWithEdge(tris, b, a, out int d);
                        if (n < 0)
                            continue;
                        if (InCircle(v[a], v[b], v[c], v[d]) <= 1e-9f)
                            continue;
                        if (!TryFlip(v, tris, t, n, a, b, c, d))
                            continue;
                        flipped = true;
                        break;
                    }
                }
                if (!flipped)
                    return;
            }
        }

        static void RecoverEdge(Vector2[] v, List<int> tris, int a, int b)
        {
            for (int guard = 0; guard < 256; guard++)
            {
                if (FindTriWithEdge(tris, a, b, out _) >= 0 || FindTriWithEdge(tris, b, a, out _) >= 0)
                    return;
                bool flipped = false;
                for (int t = 0; t < tris.Count && !flipped; t += 3)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int p = tris[t + e];
                        int q = tris[t + (e + 1) % 3];
                        int r = tris[t + (e + 2) % 3];
                        if (!SegmentsCross(v[a], v[b], v[p], v[q]))
                            continue;
                        int n = FindTriWithEdge(tris, q, p, out int s);
                        if (n < 0)
                            continue;
                        if (TryFlip(v, tris, t, n, p, q, r, s))
                        {
                            flipped = true;
                            break;
                        }
                    }
                }
                if (!flipped)
                    return;
            }
        }

        /// <summary>Triangles (a,b,c) at t and (b,a,d) at n become (a,d,c) and (d,b,c) when the quad is convex.</summary>
        static bool TryFlip(Vector2[] v, List<int> tris, int t, int n, int a, int b, int c, int d)
        {
            if (Orient(v[a], v[d], v[c]) <= Eps || Orient(v[d], v[b], v[c]) <= Eps)
                return false;
            tris[t] = a; tris[t + 1] = d; tris[t + 2] = c;
            tris[n] = d; tris[n + 1] = b; tris[n + 2] = c;
            return true;
        }

        static int FindTriWithEdge(List<int> tris, int a, int b, out int opposite)
        {
            for (int t = 0; t < tris.Count; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    if (tris[t + e] == a && tris[t + (e + 1) % 3] == b)
                    {
                        opposite = tris[t + (e + 2) % 3];
                        return t;
                    }
                }
            }
            opposite = -1;
            return -1;
        }

        static void AddTri(List<int> tris, int a, int b, int c)
        {
            tris.Add(a);
            tris.Add(b);
            tris.Add(c);
        }

        // ----- geometry -----

        static float Orient(Vector2 a, Vector2 b, Vector2 c)
            => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        /// <summary>Positive when d is inside the circumcircle of CCW (a, b, c).</summary>
        static float InCircle(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float adx = a.x - d.x, ady = a.y - d.y;
            float bdx = b.x - d.x, bdy = b.y - d.y;
            float cdx = c.x - d.x, cdy = c.y - d.y;
            float ad = adx * adx + ady * ady;
            float bd = bdx * bdx + bdy * bdy;
            float cd = cdx * cdx + cdy * cdy;
            return adx * (bdy * cd - bd * cdy) - ady * (bdx * cd - bd * cdx) + ad * (bdx * cdy - bdy * cdx);
        }

        static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float c1 = Orient(a, b, c);
            float c2 = Orient(a, b, d);
            float c3 = Orient(c, d, a);
            float c4 = Orient(c, d, b);
            return c1 * c2 < -Eps * Eps && c3 * c4 < -Eps * Eps;
        }

        static bool PointInTriangleInclusive(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float o1 = Orient(a, b, p);
            float o2 = Orient(b, c, p);
            float o3 = Orient(c, a, p);
            return o1 >= -Eps && o2 >= -Eps && o3 >= -Eps;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len = ab.sqrMagnitude;
            float t = len < 1e-12f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len);
            return Vector2.Distance(p, a + ab * t);
        }

        static float DistanceToHull(SpritePartMeshDef mesh, Vector2 p)
        {
            NearestHullEdge(mesh, p, out float d);
            return d;
        }

        static float NearestDistance(List<Vector2> verts, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < verts.Count; i++)
                best = Mathf.Min(best, Vector2.Distance(verts[i], p));
            return best;
        }

        static float DistanceSq(Vector2 a, Vector2 b) => (a - b).sqrMagnitude;

        static Vector2 Clamp01(Vector2 p) => new Vector2(Mathf.Clamp01(p.x), Mathf.Clamp01(p.y));

        static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        static int[] Identity(int n)
        {
            var map = new int[n];
            for (int i = 0; i < n; i++)
                map[i] = i;
            return map;
        }

        static int[] RemapEdges(int[] edges, int[] remap)
        {
            if (edges == null)
                return Array.Empty<int>();
            var result = new List<int>(edges.Length);
            for (int i = 0; i + 1 < edges.Length; i += 2)
            {
                int a = (uint)edges[i] < (uint)remap.Length ? remap[edges[i]] : -1;
                int b = (uint)edges[i + 1] < (uint)remap.Length ? remap[edges[i + 1]] : -1;
                if (a < 0 || b < 0 || a == b)
                    continue;
                result.Add(a);
                result.Add(b);
            }
            return result.ToArray();
        }

        static void Assign(SpritePartMeshDef target, SpritePartMeshDef source, int[] remap = null)
        {
            // Weights follow the vertices: kept when the count matches, carried across a topology change otherwise.
            if (target.HasWeights)
            {
                source.Bones = target.Bones;
                if (source.Weights == null || source.Weights.Length != source.VertexCount * target.BoneCount)
                    source.Weights = SpritePartsSkinning.CarryWeights(target, source.Vertices, remap);
            }
            target.Bones = source.Bones;
            target.Weights = source.Weights;
            target.Vertices = source.Vertices;
            target.HullCount = source.HullCount;
            target.Edges = source.Edges ?? Array.Empty<int>();
            target.Triangles = source.Triangles;
        }
    }
}
