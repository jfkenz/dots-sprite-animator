using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    // Mirror helpers (Spine / AnyPortrait-style symmetry): a vertical axis at x = axis (image space),
    // pairs of vertices that sit at each other's mirrored spot, and mirrored copies of a selection.
    public static partial class SpritePartsMeshOps
    {
        public static Vector2 MirrorPoint(Vector2 p, float axis) => new Vector2(2f * axis - p.x, p.y);

        /// <summary>
        /// For each vertex, the vertex at its mirrored spot across x = <paramref name="axis"/> (within
        /// <paramref name="tolerance"/>, image space), itself when it sits on the axis, or -1. Pairs are mutual:
        /// each vertex is matched once, closest pairs first.
        /// </summary>
        public static int[] MirrorPartners(SpritePartMeshDef mesh, float axis, float tolerance = 0.03f)
        {
            int n = mesh?.VertexCount ?? 0;
            var partner = new int[n];
            for (int i = 0; i < n; i++)
                partner[i] = -1;
            var candidates = new List<(float d, int a, int b)>();
            for (int i = 0; i < n; i++)
            {
                Vector2 m = MirrorPoint(mesh.Vertices[i], axis);
                if (Mathf.Abs(mesh.Vertices[i].x - axis) <= tolerance * 0.5f)
                    candidates.Add((Mathf.Abs(mesh.Vertices[i].x - axis) * 2f, i, i)); // on the axis: its own partner
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
        {
            var added = new List<int>();
            if (!IsDraft(mesh) || selection == null)
                return added;
            var partner = MirrorPartners(mesh, axis, tolerance);
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
                if (!TryAddGraphVertex(mesh, MirrorPoint(mesh.Vertices[i], axis), out int copy))
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
