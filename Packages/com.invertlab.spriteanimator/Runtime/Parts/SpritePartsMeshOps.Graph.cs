using System;
using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    // Draft meshes, the AnyPortrait Create workflow: free vertices joined by edges, no triangles,
    // until Make Polygons fills every closed loop of edges. A draft has HullCount 0; Make Polygons
    // turns the outer loop into the hull and the other edges into user edges.
    // Draft edits only append vertices, so existing indices never shift (except on delete).
    public static partial class SpritePartsMeshOps
    {
        public static bool IsDraft(SpritePartMeshDef mesh)
            => mesh != null && !mesh.HasMesh && mesh.HullCount == 0;

        /// <summary>Every drawn line once: hull outline, user edges and triangle sides.</summary>
        public static List<Vector2Int> GraphEdges(SpritePartMeshDef mesh)
        {
            var list = new List<Vector2Int>();
            int n = mesh?.VertexCount ?? 0;
            if (n == 0)
                return list;
            var seen = new HashSet<long>();
            void Add(int a, int b)
            {
                if (a != b && (uint)a < (uint)n && (uint)b < (uint)n && seen.Add(Key(a, b)))
                    list.Add(new Vector2Int(a, b));
            }
            int h = Mathf.Min(mesh.HullCount, n);
            if (mesh.HasMesh)
            {
                for (int i = 0; i < h; i++)
                    Add(i, (i + 1) % h);
            }
            else
            {
                for (int i = 0; i + 1 < h; i++)
                    Add(i, i + 1); // outline still open
            }
            for (int i = 0; mesh.Edges != null && i + 1 < mesh.Edges.Length; i += 2)
                Add(mesh.Edges[i], mesh.Edges[i + 1]);
            for (int t = 0; mesh.HasMesh && t + 2 < mesh.Triangles.Length; t += 3)
            {
                Add(mesh.Triangles[t], mesh.Triangles[t + 1]);
                Add(mesh.Triangles[t + 1], mesh.Triangles[t + 2]);
                Add(mesh.Triangles[t + 2], mesh.Triangles[t]);
            }
            return list;
        }

        /// <summary>
        /// Turns the mesh into a draft: same vertices, every drawn line becomes an edge, no triangles.
        /// False when it already is one.
        /// </summary>
        public static bool ToDraft(SpritePartMeshDef mesh)
        {
            if (mesh == null || IsDraft(mesh))
                return false;
            var next = new SpritePartMeshDef
            {
                Vertices = mesh.Vertices == null ? Array.Empty<Vector2>() : (Vector2[])mesh.Vertices.Clone(),
                HullCount = 0,
                Edges = Flatten(GraphEdges(mesh)),
                Triangles = null,
            };
            Assign(mesh, next);
            return true;
        }

        public static bool TryAddGraphVertex(SpritePartMeshDef mesh, Vector2 uv, out int index)
        {
            index = -1;
            int n = mesh?.VertexCount ?? 0;
            if (mesh == null || !IsDraft(mesh) || n >= MaxVertices)
                return false;
            var verts = new Vector2[n + 1];
            if (n > 0)
                Array.Copy(mesh.Vertices, verts, n);
            verts[n] = Clamp01(uv);
            var next = new SpritePartMeshDef { Vertices = verts, HullCount = 0, Edges = mesh.Edges ?? Array.Empty<int>() };
            Assign(mesh, next, Identity(n));
            index = n;
            return true;
        }

        /// <summary>Adds a vertex on edge a-b (projected onto it); the edge now runs through it.</summary>
        public static bool TrySplitGraphEdge(SpritePartMeshDef mesh, int a, int b, Vector2 uv, out int index)
        {
            index = -1;
            if (!IsDraft(mesh) || !HasEdge(mesh, a, b))
                return false;
            Vector2 pa = mesh.Vertices[a], pb = mesh.Vertices[b];
            Vector2 ab = pb - pa;
            float t = Mathf.Clamp(Vector2.Dot(uv - pa, ab) / Mathf.Max(1e-12f, ab.sqrMagnitude), 0.02f, 0.98f);
            if (!TryAddGraphVertex(mesh, pa + ab * t, out index))
                return false;
            TryRemoveEdge(mesh, a, b);
            TryAddEdge(mesh, a, index);
            TryAddEdge(mesh, index, b);
            return true;
        }

        /// <summary>
        /// Deletes one vertex and its edges. <paramref name="keepEdges"/>: a vertex between exactly
        /// two others leaves one edge joining them.
        /// </summary>
        public static bool TryRemoveGraphVertex(SpritePartMeshDef mesh, int v, bool keepEdges, out int[] remap)
        {
            remap = null;
            int n = mesh?.VertexCount ?? 0;
            if (!IsDraft(mesh) || (uint)v >= (uint)n)
                return false;
            var neighbors = Neighbors(mesh, v);
            remap = new int[n];
            var verts = new Vector2[n - 1];
            for (int i = 0, k = 0; i < n; i++)
            {
                remap[i] = i == v ? -1 : k;
                if (i != v)
                    verts[k++] = mesh.Vertices[i];
            }
            var edges = new List<int>(RemapEdges(mesh.Edges, remap));
            if (keepEdges && neighbors.Count == 2)
            {
                int a = remap[neighbors[0]], b = remap[neighbors[1]];
                if (!ContainsEdge(edges, a, b))
                {
                    edges.Add(a);
                    edges.Add(b);
                }
            }
            Assign(mesh, new SpritePartMeshDef { Vertices = verts, HullCount = 0, Edges = edges.ToArray() }, remap);
            return true;
        }

        /// <summary>Deletes several vertices (see <see cref="TryRemoveGraphVertex"/>); one remap for all of them.</summary>
        public static bool TryRemoveGraphVertices(SpritePartMeshDef mesh, IReadOnlyCollection<int> indices, bool keepEdges, out int[] remap)
        {
            remap = null;
            int n = mesh?.VertexCount ?? 0;
            if (!IsDraft(mesh) || indices == null || indices.Count == 0)
                return false;
            var sorted = new List<int>();
            foreach (int i in indices)
            {
                if ((uint)i < (uint)n && !sorted.Contains(i))
                    sorted.Add(i);
            }
            sorted.Sort();
            remap = Identity(n);
            for (int k = sorted.Count - 1; k >= 0; k--)
            {
                if (!TryRemoveGraphVertex(mesh, sorted[k], keepEdges, out var step))
                    return false;
                for (int i = 0; i < n; i++)
                    remap[i] = remap[i] >= 0 ? step[remap[i]] : -1;
            }
            return sorted.Count > 0;
        }

        /// <summary>
        /// Merges vertices into one at their centre. Their edges now meet there.
        /// <paramref name="survivor"/> is the merged vertex in the new order.
        /// </summary>
        public static bool TryMergeGraphVertices(SpritePartMeshDef mesh, IReadOnlyCollection<int> indices, out int survivor, out int[] remap)
        {
            survivor = -1;
            remap = null;
            int n = mesh?.VertexCount ?? 0;
            if (!IsDraft(mesh) || indices == null)
                return false;
            var merge = new List<int>();
            Vector2 centre = Vector2.zero;
            foreach (int i in indices)
            {
                if ((uint)i >= (uint)n || merge.Contains(i))
                    continue;
                merge.Add(i);
                centre += mesh.Vertices[i];
            }
            if (merge.Count < 2)
                return false;
            merge.Sort();
            int keep = merge[0];
            var verts = (Vector2[])mesh.Vertices.Clone();
            verts[keep] = centre / merge.Count;
            var edges = new List<int>();
            for (int i = 0; mesh.Edges != null && i + 1 < mesh.Edges.Length; i += 2)
            {
                int a = merge.Contains(mesh.Edges[i]) ? keep : mesh.Edges[i];
                int b = merge.Contains(mesh.Edges[i + 1]) ? keep : mesh.Edges[i + 1];
                if (a != b && !ContainsEdge(edges, a, b))
                {
                    edges.Add(a);
                    edges.Add(b);
                }
            }
            mesh.Vertices = verts;
            mesh.Edges = edges.ToArray();
            merge.RemoveAt(0);
            if (!TryRemoveGraphVertices(mesh, merge, false, out remap))
                return false;
            survivor = remap[keep];
            return true;
        }

        /// <summary>
        /// Joins <paramref name="from"/> to <paramref name="to"/>. <paramref name="cut"/>: every edge the
        /// line crosses gains a vertex at the crossing and the new edge runs through them (Shift in AnyPortrait).
        /// </summary>
        public static bool TryConnectGraph(SpritePartMeshDef mesh, int from, int to, bool cut)
        {
            int n = mesh?.VertexCount ?? 0;
            if (!IsDraft(mesh) || from == to || (uint)from >= (uint)n || (uint)to >= (uint)n)
                return false;
            int prev = from;
            for (int guard = 0; cut && guard < MaxVertices; guard++)
            {
                if (!TryFirstGraphCrossing(mesh, prev, to, out int ea, out int eb, out var p)
                    || !TrySplitGraphEdge(mesh, ea, eb, p, out int x))
                    break;
                TryAddEdge(mesh, prev, x);
                prev = x;
            }
            return TryAddEdge(mesh, prev, to);
        }

        /// <summary>Nearest edge crossed by from-&gt;to, skipping edges that touch either end.</summary>
        public static bool TryFirstGraphCrossing(SpritePartMeshDef mesh, int from, int to, out int ea, out int eb, out Vector2 point)
        {
            ea = eb = -1;
            point = default;
            float best = float.MaxValue;
            foreach (var e in GraphEdges(mesh))
            {
                if (e.x == from || e.y == from || e.x == to || e.y == to)
                    continue;
                if (!TrySegmentHit(mesh.Vertices[from], mesh.Vertices[to], mesh.Vertices[e.x], mesh.Vertices[e.y], out float t, out var p)
                    || t >= best)
                    continue;
                best = t;
                ea = e.x;
                eb = e.y;
                point = p;
            }
            return ea >= 0;
        }

        /// <summary>
        /// Turns edge a-b into the other diagonal of the two triangles beside it (AnyPortrait Edge tool click).
        /// </summary>
        public static bool TryTurnGraphEdge(SpritePartMeshDef mesh, int a, int b)
        {
            if (!IsDraft(mesh) || !HasEdge(mesh, a, b))
                return false;
            var v = mesh.Vertices;
            var na = Neighbors(mesh, a);
            var nb = Neighbors(mesh, b);
            int left = -1, right = -1;
            float leftD = float.MaxValue, rightD = float.MaxValue;
            foreach (int c in na)
            {
                if (c == b || !nb.Contains(c))
                    continue;
                float side = Orient(v[a], v[b], v[c]);
                float d = DistanceToSegment(v[c], v[a], v[b]);
                if (side > Eps && d < leftD)
                {
                    leftD = d;
                    left = c;
                }
                else if (side < -Eps && d < rightD)
                {
                    rightD = d;
                    right = c;
                }
            }
            if (left < 0 || right < 0 || HasEdge(mesh, left, right) || !SegmentsCross(v[a], v[b], v[left], v[right]))
                return false;
            TryRemoveEdge(mesh, a, b);
            TryAddEdge(mesh, left, right);
            return true;
        }

        /// <summary>
        /// Auto Link: adds edges between vertices so the shape can be filled. Inside a closed loop only the
        /// enclosed area is linked; with no loop yet, all vertices are. Returns the number of edges added.
        /// </summary>
        public static int AutoLinkGraph(SpritePartMeshDef mesh)
        {
            if (!IsDraft(mesh) || mesh.VertexCount < 3)
                return 0;
            var tris = GraphTriangles(mesh.Vertices, mesh.Edges, out var inside);
            if (tris == null)
                return 0;
            bool anyInside = Array.IndexOf(inside, true) >= 0;
            int added = 0;
            for (int t = 0; t < inside.Length; t++)
            {
                if (anyInside && !inside[t])
                    continue;
                for (int e = 0; e < 3; e++)
                {
                    int a = tris[t * 3 + e], b = tris[t * 3 + (e + 1) % 3];
                    if (HasEdge(mesh, a, b))
                        continue;
                    TryAddEdge(mesh, a, b);
                    added++;
                }
            }
            return added;
        }

        /// <summary>
        /// Make Polygons: fills the area enclosed by edges with triangles. Crossing edges get a vertex
        /// where they meet. The outer loop becomes the hull, vertices outside it are dropped, holes are filled.
        /// <paramref name="remap"/> maps the draft's vertices to the new mesh (-1 = dropped).
        /// </summary>
        public static bool TryMakePolygons(SpritePartMeshDef mesh, out int[] remap, out string message)
        {
            remap = null;
            message = null;
            if (!IsDraft(mesh) || mesh.VertexCount < 3)
            {
                message = "Make Polygons needs at least 3 vertices joined into a loop.";
                return false;
            }
            int original = mesh.VertexCount;
            var work = mesh.Clone();
            if (!SplitCrossings(work))
            {
                message = "Edges cross, and the mesh is too full to add vertices where they meet.";
                return false;
            }
            var v = work.Vertices;
            int n = v.Length;
            var tris = GraphTriangles(v, work.Edges, out var inside);
            if (tris == null || Array.IndexOf(inside, true) < 0)
            {
                message = "Nothing to fill. Join the edges into a closed loop (click the first vertex again).";
                return false;
            }

            // Keep the largest connected piece.
            int triCount = inside.Length;
            var piece = new int[triCount];
            for (int t = 0; t < triCount; t++)
                piece[t] = -1;
            var byEdge = TrianglesByEdge(tris);
            int pieces = 0, bestPiece = -1;
            float bestArea = -1f;
            var stack = new Stack<int>();
            for (int s = 0; s < triCount; s++)
            {
                if (!inside[s] || piece[s] >= 0)
                    continue;
                float area = 0f;
                piece[s] = pieces;
                stack.Push(s);
                while (stack.Count > 0)
                {
                    int t = stack.Pop();
                    area += Orient(v[tris[t * 3]], v[tris[t * 3 + 1]], v[tris[t * 3 + 2]]);
                    for (int e = 0; e < 3; e++)
                    {
                        foreach (int o in byEdge[Key(tris[t * 3 + e], tris[t * 3 + (e + 1) % 3])])
                        {
                            if (inside[o] && piece[o] < 0)
                            {
                                piece[o] = pieces;
                                stack.Push(o);
                            }
                        }
                    }
                }
                if (area > bestArea)
                {
                    bestArea = area;
                    bestPiece = pieces;
                }
                pieces++;
            }

            // Boundary of that piece: directed edges whose reverse is not in it (triangles are CCW).
            var directed = new HashSet<long>();
            for (int t = 0; t < triCount; t++)
            {
                if (piece[t] != bestPiece)
                    continue;
                for (int e = 0; e < 3; e++)
                    directed.Add(Directed(tris[t * 3 + e], tris[t * 3 + (e + 1) % 3]));
            }
            var next = new Dictionary<int, int>();
            foreach (long d in directed)
            {
                int a = (int)(d >> 32), b = (int)(d & 0xffffffff);
                if (directed.Contains(Directed(b, a)))
                    continue;
                if (next.ContainsKey(a))
                {
                    message = "The outline touches itself at vertex " + a + ". Move it apart or add an edge there.";
                    return false;
                }
                next[a] = b;
            }
            List<int> outline = null;
            float outlineArea = float.MinValue;
            int loops = 0;
            var seen = new HashSet<int>();
            foreach (int start in next.Keys)
            {
                if (seen.Contains(start))
                    continue;
                var loop = new List<int>();
                for (int c = start; seen.Add(c); c = next[c])
                    loop.Add(c);
                loops++;
                float area = PolygonArea(v, loop);
                if (area > outlineArea)
                {
                    outlineArea = area;
                    outline = loop;
                }
            }
            if (outline == null || outline.Count < 3)
            {
                message = "Nothing to fill. Join the edges into a closed loop.";
                return false;
            }

            // Hull first, then every vertex inside it; the rest are dropped.
            var order = new int[n];
            for (int i = 0; i < n; i++)
                order[i] = -1;
            var verts = new List<Vector2>(n);
            foreach (int i in outline)
            {
                order[i] = verts.Count;
                verts.Add(v[i]);
            }
            var outlinePoints = new List<Vector2>(outline.Count);
            foreach (int i in outline)
                outlinePoints.Add(v[i]);
            for (int i = 0; i < n; i++)
            {
                if (order[i] >= 0 || !InsidePolygon(outlinePoints, v[i]))
                    continue;
                order[i] = verts.Count;
                verts.Add(v[i]);
            }
            int hull = outline.Count;
            var edges = new List<int>();
            for (int i = 0; work.Edges != null && i + 1 < work.Edges.Length; i += 2)
            {
                int a = order[work.Edges[i]], b = order[work.Edges[i + 1]];
                if (a < 0 || b < 0 || a == b)
                    continue;
                if (a < hull && b < hull)
                {
                    if ((a + 1) % hull == b || (b + 1) % hull == a)
                        continue; // outline edge
                    if (!InsidePolygon(outlinePoints, (verts[a] + verts[b]) * 0.5f))
                        continue; // runs outside the shape
                }
                if (!ContainsEdge(edges, a, b))
                {
                    edges.Add(a);
                    edges.Add(b);
                }
            }
            if (verts.Count > MaxVertices)
            {
                message = "Too many vertices (" + verts.Count + " of " + MaxVertices + ").";
                return false;
            }
            var result = new SpritePartMeshDef { Vertices = verts.ToArray(), HullCount = hull, Edges = edges.ToArray() };
            if (!Retriangulate(result))
            {
                message = "Could not fill that outline. Check for edges that double back on themselves.";
                return false;
            }
            Assign(work, result, order);
            Assign(mesh, work);
            remap = new int[original];
            Array.Copy(order, remap, original);

            int dropped = n - verts.Count;
            message = "Made " + result.Triangles.Length / 3 + " polygons.";
            if (pieces > 1)
                message += " Only the largest shape was kept.";
            if (loops > 1)
                message += " Holes were filled.";
            if (dropped > 0)
                message += " " + dropped + " vertices outside the shape were removed.";
            return true;
        }

        // ----- helpers -----

        /// <summary>
        /// Triangulates all draft vertices (their convex hull) with the edges as constraints.
        /// <paramref name="inside"/>[t] = triangle t is enclosed by edges (cannot reach the outside
        /// without crossing one). Triangles use the draft's own indices, CCW.
        /// </summary>
        static int[] GraphTriangles(Vector2[] v, int[] edges, out bool[] inside)
        {
            inside = Array.Empty<bool>();
            int n = v?.Length ?? 0;
            var hull = ConvexHull(v, n);
            if (hull.Count < 3)
                return null;
            var order = new List<int>(hull);
            var isHull = new bool[n];
            foreach (int i in hull)
                isHull[i] = true;
            for (int i = 0; i < n; i++)
            {
                if (!isHull[i])
                    order.Add(i);
            }
            var toSorted = new int[n];
            var sorted = new Vector2[n];
            for (int k = 0; k < n; k++)
            {
                toSorted[order[k]] = k;
                sorted[k] = v[order[k]];
            }
            var sortedEdges = RemapEdges(edges, toSorted);
            var local = Triangulate(sorted, hull.Count, sortedEdges);
            if (local == null)
                return null;
            var tris = new int[local.Length];
            for (int i = 0; i < local.Length; i++)
                tris[i] = order[local[i]];

            var constrained = new HashSet<long>();
            for (int i = 0; edges != null && i + 1 < edges.Length; i += 2)
                constrained.Add(Key(edges[i], edges[i + 1]));
            var byEdge = TrianglesByEdge(tris);
            int count = tris.Length / 3;
            var outside = new bool[count];
            var queue = new Queue<int>();
            for (int t = 0; t < count; t++)
            {
                for (int e = 0; e < 3; e++)
                {
                    long k = Key(tris[t * 3 + e], tris[t * 3 + (e + 1) % 3]);
                    if (byEdge[k].Count == 1 && !constrained.Contains(k) && !outside[t])
                    {
                        outside[t] = true;
                        queue.Enqueue(t);
                    }
                }
            }
            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                for (int e = 0; e < 3; e++)
                {
                    long k = Key(tris[t * 3 + e], tris[t * 3 + (e + 1) % 3]);
                    if (constrained.Contains(k))
                        continue;
                    foreach (int o in byEdge[k])
                    {
                        if (outside[o])
                            continue;
                        outside[o] = true;
                        queue.Enqueue(o);
                    }
                }
            }
            inside = new bool[count];
            for (int t = 0; t < count; t++)
                inside[t] = !outside[t];
            return tris;
        }

        /// <summary>Gives every pair of crossing edges a shared vertex. False when the mesh is full.</summary>
        static bool SplitCrossings(SpritePartMeshDef mesh)
        {
            for (int guard = 0; guard < 256; guard++)
            {
                var edges = GraphEdges(mesh);
                bool found = false;
                for (int i = 0; i < edges.Count && !found; i++)
                {
                    for (int j = i + 1; j < edges.Count && !found; j++)
                    {
                        var e = edges[i];
                        var f = edges[j];
                        if (e.x == f.x || e.x == f.y || e.y == f.x || e.y == f.y)
                            continue;
                        var v = mesh.Vertices;
                        if (!TrySegmentHit(v[e.x], v[e.y], v[f.x], v[f.y], out _, out var p))
                            continue;
                        if (!TrySplitGraphEdge(mesh, e.x, e.y, p, out int x))
                            return false;
                        TryRemoveEdge(mesh, f.x, f.y);
                        TryAddEdge(mesh, f.x, x);
                        TryAddEdge(mesh, x, f.y);
                        found = true;
                    }
                }
                if (!found)
                    return true;
            }
            return true;
        }

        /// <summary>Andrew's monotone chain, counter-clockwise; collinear points are left out.</summary>
        static List<int> ConvexHull(IReadOnlyList<Vector2> points, int n)
        {
            var order = new List<int>(n);
            for (int i = 0; i < n; i++)
                order.Add(i);
            order.Sort((a, b) =>
            {
                int c = points[a].x.CompareTo(points[b].x);
                return c != 0 ? c : points[a].y.CompareTo(points[b].y);
            });
            var hull = new List<int>(n + 1);
            if (n == 0)
                return hull;
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int k = 0; k < n; k++)
                {
                    int i = pass == 0 ? order[k] : order[n - 1 - k];
                    while (hull.Count >= start + 2
                           && Orient(points[hull[hull.Count - 2]], points[hull[hull.Count - 1]], points[i]) <= Eps)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(i);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        static Dictionary<long, List<int>> TrianglesByEdge(int[] tris)
        {
            var map = new Dictionary<long, List<int>>();
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    long k = Key(tris[t + e], tris[t + (e + 1) % 3]);
                    if (!map.TryGetValue(k, out var list))
                        map[k] = list = new List<int>(2);
                    list.Add(t / 3);
                }
            }
            return map;
        }

        static List<int> Neighbors(SpritePartMeshDef mesh, int v)
        {
            var result = new List<int>();
            for (int i = 0; mesh.Edges != null && i + 1 < mesh.Edges.Length; i += 2)
            {
                int other = mesh.Edges[i] == v ? mesh.Edges[i + 1] : mesh.Edges[i + 1] == v ? mesh.Edges[i] : -1;
                if (other >= 0 && other != v && !result.Contains(other))
                    result.Add(other);
            }
            return result;
        }

        static bool ContainsEdge(List<int> edges, int a, int b)
        {
            for (int i = 0; i + 1 < edges.Count; i += 2)
            {
                if ((edges[i] == a && edges[i + 1] == b) || (edges[i] == b && edges[i + 1] == a))
                    return true;
            }
            return false;
        }

        static int[] Flatten(List<Vector2Int> edges)
        {
            var flat = new int[edges.Count * 2];
            for (int i = 0; i < edges.Count; i++)
            {
                flat[i * 2] = edges[i].x;
                flat[i * 2 + 1] = edges[i].y;
            }
            return flat;
        }

        static long Directed(int a, int b) => ((long)a << 32) | (uint)b;

        static float PolygonArea(Vector2[] v, List<int> loop)
        {
            float area = 0f;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                area += v[loop[j]].x * v[loop[i]].y - v[loop[i]].x * v[loop[j]].y;
            return area * 0.5f;
        }

        static bool InsidePolygon(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
