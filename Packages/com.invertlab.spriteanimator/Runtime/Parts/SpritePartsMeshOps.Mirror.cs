using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Which way a mesh is symmetric.</summary>
    public enum SpritePartsMirrorMode : byte
    {
        /// <summary>Left / right across a vertical line (x = axis).</summary>
        Horizontal = 0,
        /// <summary>Top / bottom across a horizontal line (y = axis).</summary>
        Vertical = 1,
        /// <summary>Four ways: left / right, top / bottom and the opposite corner.</summary>
        Both = 2,
    }

    // Mirror helpers (Spine / AnyPortrait-style symmetry): axes in image space, groups of vertices that sit
    // at each other's mirrored spots, and mirrored copies of a selection.
    public static partial class SpritePartsMeshOps
    {
        public static Vector2 MirrorPoint(Vector2 p, float axis) => new Vector2(2f * axis - p.x, p.y);

        public static Vector2 MirrorPoint(Vector2 p, float axisX, float axisY, bool flipX, bool flipY)
            => new Vector2(flipX ? 2f * axisX - p.x : p.x, flipY ? 2f * axisY - p.y : p.y);

        /// <summary>
        /// Every vertex's mirror partners and how a move carries over to each (multiply the move by Flip),
        /// plus which directions a vertex on an axis may not move in (it stays on the axis).
        /// </summary>
        public sealed class MirrorMap
        {
            public bool[] LockX;
            public bool[] LockY;
            public List<(int vertex, Vector2 flip)>[] Partners;

            public int Count => Partners?.Length ?? 0;

            /// <summary>A move of vertex <paramref name="i"/>, kept on its axes.</summary>
            public Vector2 Constrain(int i, Vector2 move)
                => new Vector2(LockX[i] ? 0f : move.x, LockY[i] ? 0f : move.y);
        }

        /// <summary>The flips a mode uses: Horizontal x; Vertical y; Both x, y and the opposite corner.</summary>
        public static List<(bool x, bool y)> MirrorFlips(SpritePartsMirrorMode mode)
        {
            var flips = new List<(bool, bool)>(3);
            if (mode != SpritePartsMirrorMode.Vertical)
                flips.Add((true, false));
            if (mode != SpritePartsMirrorMode.Horizontal)
                flips.Add((false, true));
            if (mode == SpritePartsMirrorMode.Both)
                flips.Add((true, true));
            return flips;
        }

        public static MirrorMap BuildMirrorMap(SpritePartMeshDef mesh, float axisX, float axisY, SpritePartsMirrorMode mode,
            float tolerance = 0.03f)
        {
            int n = mesh?.VertexCount ?? 0;
            var map = new MirrorMap { LockX = new bool[n], LockY = new bool[n], Partners = new List<(int, Vector2)>[n] };
            for (int i = 0; i < n; i++)
                map.Partners[i] = new List<(int, Vector2)>(3);
            foreach (var (fx, fy) in MirrorFlips(mode))
            {
                var partner = MirrorPartners(mesh, axisX, axisY, fx, fy, tolerance);
                var flip = new Vector2(fx ? -1f : 1f, fy ? -1f : 1f);
                for (int i = 0; i < n; i++)
                {
                    int p = partner[i];
                    if (p == i)
                    {
                        // Its own mirror: on the axis (a corner flip only matters through the two axes).
                        if (fx && !fy) map.LockX[i] = true;
                        if (fy && !fx) map.LockY[i] = true;
                        continue;
                    }
                    if (p >= 0 && !map.Partners[i].Exists(e => e.Item1 == p))
                        map.Partners[i].Add((p, flip));
                }
            }
            return map;
        }

        /// <summary>
        /// For each vertex, the vertex at its mirrored spot across x = <paramref name="axis"/> (within
        /// <paramref name="tolerance"/>, image space), itself when it sits on the axis, or -1. Pairs are mutual:
        /// each vertex is matched once, closest pairs first.
        /// </summary>
        public static int[] MirrorPartners(SpritePartMeshDef mesh, float axis, float tolerance = 0.03f)
            => MirrorPartners(mesh, axis, 0.5f, true, false, tolerance);

        /// <summary>
        /// Partners across x = <paramref name="axisX"/> (<paramref name="flipX"/>), y = <paramref name="axisY"/>
        /// (<paramref name="flipY"/>), or both (the opposite corner). Same rules as the one-axis overload.
        /// </summary>
        public static int[] MirrorPartners(SpritePartMeshDef mesh, float axisX, float axisY, bool flipX, bool flipY,
            float tolerance = 0.03f)
        {
            int n = mesh?.VertexCount ?? 0;
            var partner = new int[n];
            for (int i = 0; i < n; i++)
                partner[i] = -1;
            var candidates = new List<(float d, int a, int b)>();
            for (int i = 0; i < n; i++)
            {
                Vector2 m = MirrorPoint(mesh.Vertices[i], axisX, axisY, flipX, flipY);
                float self = Vector2.Distance(m, mesh.Vertices[i]);
                if (self <= tolerance)
                    candidates.Add((self, i, i)); // on the axis (or the centre): its own partner
                for (int j = i + 1; j < n; j++)
                {
                    float d = Vector2.Distance(m, mesh.Vertices[j]);
                    if (d <= tolerance)
                        candidates.Add((d, i, j));
                }
            }
            candidates.Sort((x, y) => x.d.CompareTo(y.d));
            foreach (var c in candidates)
            {
                if (partner[c.a] >= 0 || partner[c.b] >= 0)
                    continue;
                partner[c.a] = c.b;
                partner[c.b] = c.a;
            }
            return partner;
        }

        /// <summary>
        /// Draft: adds a mirrored copy of <paramref name="selection"/> across the axis (vertices that already have
        /// a partner there are reused) and mirrors the edges between selected vertices. Returns the new vertices.
        /// </summary>
        public static List<int> MirrorCopyGraph(SpritePartMeshDef mesh, IReadOnlyCollection<int> selection, float axis, float tolerance = 0.03f)
            => MirrorCopyGraph(mesh, selection, axis, 0.5f, true, false, tolerance);

        /// <summary>Mirror Copy across x (<paramref name="flipX"/>), y (<paramref name="flipY"/>) or the opposite corner.</summary>
        public static List<int> MirrorCopyGraph(SpritePartMeshDef mesh, IReadOnlyCollection<int> selection, float axisX, float axisY,
            bool flipX, bool flipY, float tolerance = 0.03f)
        {
            var added = new List<int>();
            if (!IsDraft(mesh) || selection == null)
                return added;
            var partner = MirrorPartners(mesh, axisX, axisY, flipX, flipY, tolerance);
            var twin = new Dictionary<int, int>();
            foreach (int i in selection)
            {
                if ((uint)i >= (uint)mesh.VertexCount || twin.ContainsKey(i))
                    continue;
                if (partner[i] >= 0)
                {
                    twin[i] = partner[i];
                    continue;
                }
                if (!TryAddGraphVertex(mesh, MirrorPoint(mesh.Vertices[i], axisX, axisY, flipX, flipY), out int copy))
                    break; // mesh is full
                twin[i] = copy;
                added.Add(copy);
            }
            foreach (var e in GraphEdges(mesh, false))
            {
                if (twin.TryGetValue(e.x, out int a) && twin.TryGetValue(e.y, out int b))
                    TryAddEdge(mesh, a, b);
            }
            return added;
        }
    }
}
