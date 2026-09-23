using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Clipping (Spine's clipping, AnyPortrait's clipped meshes), cut on the CPU so the result draws through the
    /// normal mesh path. Two sources, applied one after the other:
    ///   Clip To     a part only shows inside its mask part: the mask's mesh (deformed and weighted, as drawn) or its rectangle
    ///   clip shape  an invisible polygon that clips every part drawn above it, up to its end part (Spine's clipping
    ///               attachment); the current draw order (with draw-order keys) decides the range, clip keys turn it on / off
    /// Masks are split into convex pieces once at build time (<see cref="ConvexPieces"/>); each frame every triangle
    /// of a clipped part is cut against each piece (Sutherland-Hodgman) and fanned back into triangles, texture
    /// coordinates carried by barycentric weights. A result too big for a mesh
    /// (<see cref="SpritePartsLattice.MaxVertices"/>) leaves the part as it was.
    /// </summary>
    public static class SpritePartsClipping
    {
        const float Weld = 1e-5f;

        public static bool Any(ref SpritePartsSetBlob set)
        {
            for (int i = 0; i < set.Slots.Length; i++)
            {
                if (set.Slots[i].ClipMaskIndex >= 0 || set.Slots[i].IsClipShape != 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Cuts every clipped part's lattice in <paramref name="local"/>. <paramref name="clipIndex"/> / <paramref name="time"/>
        /// give the draw order and clip keys (a clip shape's range and on / off). Marks results final.
        /// </summary>
        public static void Apply(ref SpritePartsSetBlob set, NativeArray<SpritePartsSampler.Pose> local, NativeArray<float4x4> localToRoot,
            int clipIndex = -1, float time = 0f)
        {
            int n = math.min(set.Slots.Length, math.min(local.Length, localToRoot.Length));
            bool shapes = false;
            for (int i = 0; i < n; i++)
                shapes |= set.Slots[i].IsClipShape != 0;
            var rank = new NativeArray<int>(n, Allocator.Temp);
            var active = new NativeArray<bool>(n, Allocator.Temp);
            try
            {
                if (shapes)
                {
                    for (int i = 0; i < n; i++)
                    {
                        int keyed = SpritePartsSampler.SampleDrawOrder(ref set, clipIndex, i, time);
                        rank[i] = keyed >= 0 ? keyed : set.Slots[i].DrawRank;
                        active[i] = set.Slots[i].IsClipShape != 0 && set.Slots[i].ClipPolygon.Length >= 3
                                    && set.Slots[i].MaskPieces.Length > 0 && SpritePartsSampler.SampleClipActive(ref set, clipIndex, i, time);
                    }
                }
                for (int s = 0; s < n; s++)
                {
                    ref var slot = ref set.Slots[s];
                    if (slot.IsClipShape != 0 || !ValidQuad(slot.SkinQuadSize))
                        continue;
                    int m = slot.ClipMaskIndex;
                    bool masked = m >= 0 && m < n && m != s && ValidQuad(set.Slots[m].SkinQuadSize);
                    int shape = shapes ? ClipShapeFor(ref set, s, rank, active, n) : -1;
                    if (!masked && shape < 0)
                        continue;
                    float4x4 toSlot = math.inverse(localToRoot[s]);
                    if (!math.all(math.isfinite(toSlot.c0)))
                        continue;

                    var pose = local[s];
                    var subject = pose.Lattice;
                    if (!subject.HasMesh)
                        subject = UnitQuad();
                    else if (subject.Final == 0)
                        SpritePartsSkinning.Apply(ref set, s, localToRoot, ref subject);
                    bool changed = false;

                    if (masked)
                    {
                        // The mask as drawn, in the clipped part's unit-quad space.
                        ref var mask = ref set.Slots[m];
                        var maskLattice = local[m].Lattice;
                        if (maskLattice.HasMesh && maskLattice.Final == 0)
                            SpritePartsSkinning.Apply(ref set, m, localToRoot, ref maskLattice);
                        var maskPoints = new FixedList512Bytes<float2>();
                        if (maskLattice.HasMesh)
                        {
                            for (int v = 0; v < maskLattice.PointCount; v++)
                                maskPoints.Add(ToSlotQuad(QuadToLocal(maskLattice.Points[v], mask.SkinQuadSize, mask.SkinQuadPivot), localToRoot[m], toSlot, ref slot));
                        }
                        else
                        {
                            maskPoints.Add(ToSlotQuad(QuadToLocal(new float2(-0.5f, -0.5f), mask.SkinQuadSize, mask.SkinQuadPivot), localToRoot[m], toSlot, ref slot));
                            maskPoints.Add(ToSlotQuad(QuadToLocal(new float2(0.5f, -0.5f), mask.SkinQuadSize, mask.SkinQuadPivot), localToRoot[m], toSlot, ref slot));
                            maskPoints.Add(ToSlotQuad(QuadToLocal(new float2(0.5f, 0.5f), mask.SkinQuadSize, mask.SkinQuadPivot), localToRoot[m], toSlot, ref slot));
                            maskPoints.Add(ToSlotQuad(QuadToLocal(new float2(-0.5f, 0.5f), mask.SkinQuadSize, mask.SkinQuadPivot), localToRoot[m], toSlot, ref slot));
                        }
                        bool usePieces = maskLattice.HasMesh && mask.MaskPieces.Length > 0 && maskLattice.PointCount == mask.Mesh.PointCount;
                        if (TryClip(subject, maskPoints, ref mask.MaskPieces, usePieces, maskLattice, out var cut))
                        {
                            subject = cut;
                            changed = true;
                        }
                    }

                    if (shape >= 0)
                    {
                        ref var clip = ref set.Slots[shape];
                        var shapePoints = new FixedList512Bytes<float2>();
                        for (int v = 0; v < clip.ClipPolygon.Length && shapePoints.Length < shapePoints.Capacity; v++)
                            shapePoints.Add(ToSlotQuad(clip.ClipPolygon[v], localToRoot[shape], toSlot, ref slot));
                        if (TryClip(subject, shapePoints, ref clip.MaskPieces, true, default, out var cut))
                        {
                            subject = cut;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        pose.Lattice = subject;
                        local[s] = pose;
                    }
                }
            }
            finally
            {
                rank.Dispose();
                active.Dispose();
            }
        }

        /// <summary>
        /// The active clip shape that covers slot <paramref name="s"/>: the nearest one drawn below it whose range
        /// (up to its end part) reaches it. -1 = none.
        /// </summary>
        static int ClipShapeFor(ref SpritePartsSetBlob set, int s, NativeArray<int> rank, NativeArray<bool> active, int n)
        {
            int best = -1;
            for (int c = 0; c < n; c++)
            {
                if (!active[c] || c == s || rank[c] >= rank[s])
                    continue;
                int end = set.Slots[c].ClipEndIndex;
                if (end >= 0 && end < n && rank[s] > rank[end])
                    continue; // above the end part
                if (best < 0 || rank[c] > rank[best])
                    best = c;
            }
            return best;
        }

        static bool ValidQuad(float2 size) => size.x > 1e-6f && size.y > 1e-6f;

        static float2 QuadToLocal(float2 q, float2 size, float2 pivot) => (q + 0.5f - pivot) * size;

        /// <summary>A point in another slot's space (world units) into slot <paramref name="slot"/>'s unit-quad space.</summary>
        static float2 ToSlotQuad(float2 fromLocal, float4x4 fromToRoot, float4x4 rootToSlot, ref SpritePartSlotBlob slot)
        {
            float2 root = math.mul(fromToRoot, new float4(fromLocal, 0f, 1f)).xy;
            float2 slotLocal = math.mul(rootToSlot, new float4(root, 0f, 1f)).xy;
            return slotLocal / slot.SkinQuadSize - 0.5f + slot.SkinQuadPivot;
        }

        static SpritePartsLattice UnitQuad()
        {
            var q = new SpritePartsLattice();
            q.Points.Add(new float2(-0.5f, -0.5f)); q.Uvs.Add(new float2(0f, 0f));
            q.Points.Add(new float2(0.5f, -0.5f)); q.Uvs.Add(new float2(1f, 0f));
            q.Points.Add(new float2(0.5f, 0.5f)); q.Uvs.Add(new float2(1f, 1f));
            q.Points.Add(new float2(-0.5f, 0.5f)); q.Uvs.Add(new float2(0f, 1f));
            q.Indices.Add(0); q.Indices.Add(1); q.Indices.Add(2);
            q.Indices.Add(0); q.Indices.Add(2); q.Indices.Add(3);
            return q;
        }

        /// <summary>
        /// The subject cut to the mask: its convex <paramref name="pieces"/> (vertex indices into
        /// <paramref name="maskPoints"/>) when <paramref name="usePieces"/>, else <paramref name="maskTriangles"/>'
        /// triangles, else the mask points as one convex polygon. False when the result would not fit a mesh.
        /// </summary>
        static bool TryClip(in SpritePartsLattice subject, in FixedList512Bytes<float2> maskPoints, ref BlobArray<int> pieces,
            bool usePieces, in SpritePartsLattice maskTriangles, out SpritePartsLattice result)
        {
            result = new SpritePartsLattice { Final = 1 };
            var poly = new FixedList512Bytes<float2>();
            var scratch = new FixedList512Bytes<float2>();
            var piece = new FixedList512Bytes<float2>();
            for (int t = 0; t + 2 < subject.IndexCount; t += 3)
            {
                int ia = subject.Indices[t], ib = subject.Indices[t + 1], ic = subject.Indices[t + 2];
                float2 a = subject.Points[ia], b = subject.Points[ib], c = subject.Points[ic];
                float2 ua = subject.GetUv(ia), ub = subject.GetUv(ib), uc = subject.GetUv(ic);
                float area = Cross(b - a, c - a);
                if (math.abs(area) < 1e-12f)
                    continue;

                if (usePieces)
                {
                    for (int p = 0; p < pieces.Length;)
                    {
                        int count = pieces[p++];
                        piece.Clear();
                        for (int k = 0; k < count && p < pieces.Length; k++, p++)
                        {
                            int vi = pieces[p];
                            if (vi >= 0 && vi < maskPoints.Length)
                                piece.Add(maskPoints[vi]);
                        }
                        if (!CutAndAdd(a, b, c, ua, ub, uc, area, piece, ref poly, ref scratch, ref result))
                            return false;
                    }
                    continue;
                }
                if (!maskTriangles.HasMesh)
                {
                    piece.Clear();
                    for (int k = 0; k < maskPoints.Length; k++)
                        piece.Add(maskPoints[k]);
                    if (!CutAndAdd(a, b, c, ua, ub, uc, area, piece, ref poly, ref scratch, ref result))
                        return false;
                    continue;
                }
                for (int mt = 0; mt + 2 < maskTriangles.IndexCount; mt += 3)
                {
                    piece.Clear();
                    for (int k = 0; k < 3; k++)
                    {
                        int vi = maskTriangles.Indices[mt + k];
                        if (vi < maskPoints.Length)
                            piece.Add(maskPoints[vi]);
                    }
                    if (!CutAndAdd(a, b, c, ua, ub, uc, area, piece, ref poly, ref scratch, ref result))
                        return false;
                }
            }
            if (result.IndexCount == 0)
            {
                // Fully outside: a zero-area triangle, so the part still counts as a mesh and draws nothing.
                result.Points.Add(float2.zero); result.Uvs.Add(float2.zero);
                result.Points.Add(float2.zero); result.Uvs.Add(float2.zero);
                result.Points.Add(float2.zero); result.Uvs.Add(float2.zero);
                result.Indices.Add(0); result.Indices.Add(1); result.Indices.Add(2);
            }
            return true;
        }

        /// <summary>Cuts triangle abc to the convex <paramref name="piece"/> and appends the triangles. False = full.</summary>
        static bool CutAndAdd(float2 a, float2 b, float2 c, float2 ua, float2 ub, float2 uc, float area,
            in FixedList512Bytes<float2> piece, ref FixedList512Bytes<float2> poly, ref FixedList512Bytes<float2> scratch,
            ref SpritePartsLattice result)
        {
            if (piece.Length < 3)
                return true;
            float orient = 0f;
            for (int k = 0; k < piece.Length; k++)
                orient += Cross(piece[k], piece[(k + 1) % piece.Length]);
            if (math.abs(orient) < 1e-12f)
                return true;
            float side = orient > 0f ? 1f : -1f;

            poly.Clear();
            poly.Add(a); poly.Add(b); poly.Add(c);
            for (int k = 0; k < piece.Length && poly.Length > 0; k++)
            {
                float2 e0 = piece[k], e1 = piece[(k + 1) % piece.Length];
                scratch.Clear();
                for (int i = 0; i < poly.Length; i++)
                {
                    float2 p = poly[i], q = poly[(i + 1) % poly.Length];
                    float dp = side * Cross(e1 - e0, p - e0);
                    float dq = side * Cross(e1 - e0, q - e0);
                    if (dp >= 0f)
                    {
                        if (scratch.Length >= scratch.Capacity) return false;
                        scratch.Add(p);
                    }
                    if ((dp >= 0f) != (dq >= 0f))
                    {
                        if (scratch.Length >= scratch.Capacity) return false;
                        scratch.Add(p + (q - p) * (dp / (dp - dq)));
                    }
                }
                poly.Clear();
                for (int i = 0; i < scratch.Length; i++)
                    poly.Add(scratch[i]);
            }
            if (poly.Length < 3)
                return true;

            // Fan into triangles, welding shared corners so neighbours reuse vertices.
            int first = AddPoint(ref result, poly[0], a, b, c, ua, ub, uc, area);
            if (first < 0) return false;
            int prev = AddPoint(ref result, poly[1], a, b, c, ua, ub, uc, area);
            if (prev < 0) return false;
            for (int i = 2; i < poly.Length; i++)
            {
                int cur = AddPoint(ref result, poly[i], a, b, c, ua, ub, uc, area);
                if (cur < 0) return false;
                if (math.abs(Cross(result.Points[prev] - result.Points[first], result.Points[cur] - result.Points[first])) > 1e-12f)
                {
                    if (result.Indices.Length + 3 > SpritePartsLattice.MaxIndices) return false;
                    result.Indices.Add((byte)first);
                    result.Indices.Add((byte)prev);
                    result.Indices.Add((byte)cur);
                }
                prev = cur;
            }
            return true;
        }

        static int AddPoint(ref SpritePartsLattice result, float2 p, float2 a, float2 b, float2 c, float2 ua, float2 ub, float2 uc, float area)
        {
            for (int i = 0; i < result.Points.Length; i++)
            {
                if (math.distancesq(result.Points[i], p) < Weld * Weld)
                    return i;
            }
            if (result.Points.Length >= SpritePartsLattice.MaxVertices)
                return -1;
            float wa = Cross(b - p, c - p) / area;
            float wb = Cross(c - p, a - p) / area;
            float wc = 1f - wa - wb;
            result.Points.Add(p);
            result.Uvs.Add(ua * wa + ub * wb + uc * wc);
            return result.Points.Length - 1;
        }

        static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;

        // ------------------------------------------------------------------ build time

        /// <summary>
        /// The rest mesh as convex polygons (vertex indices), flattened [count, v, v, ..., count, ...]: its
        /// triangles, merged across shared lines while the union stays convex. One piece for a convex outline.
        /// </summary>
        public static List<int> ConvexPieces(ref SpritePartsLattice mesh)
        {
            var polys = new List<List<int>>();
            for (int t = 0; t + 2 < mesh.IndexCount; t += 3)
            {
                int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
                float2 pa = mesh.GetPoint(a), pb = mesh.GetPoint(b), pc = mesh.GetPoint(c);
                float area = Cross(pb - pa, pc - pa);
                if (math.abs(area) < 1e-12f)
                    continue;
                polys.Add(area > 0f ? new List<int> { a, b, c } : new List<int> { a, c, b });
            }
            var points = new float2[mesh.PointCount];
            for (int i = 0; i < points.Length; i++)
                points[i] = mesh.GetPoint(i);
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < polys.Count && !merged; i++)
                {
                    for (int j = i + 1; j < polys.Count && !merged; j++)
                    {
                        var union = TryMerge(polys[i], polys[j], points);
                        if (union == null)
                            continue;
                        polys[i] = union;
                        polys.RemoveAt(j);
                        merged = true;
                    }
                }
            }
            var flat = new List<int>();
            foreach (var p in polys)
            {
                flat.Add(p.Count);
                flat.AddRange(p);
            }
            return flat;
        }

        /// <summary>The union of two CCW polygons sharing a line, when it is convex; else null.</summary>
        static List<int> TryMerge(List<int> p, List<int> q, float2[] points)
        {
            for (int k = 0; k < p.Count; k++)
            {
                int a = p[k], b = p[(k + 1) % p.Count];
                for (int l = 0; l < q.Count; l++)
                {
                    if (q[l] != b || q[(l + 1) % q.Count] != a)
                        continue;
                    // p from b round to a, then q from a round to b (without repeating a and b).
                    var union = new List<int>(p.Count + q.Count - 2);
                    for (int i = 0; i < p.Count; i++)
                        union.Add(p[(k + 1 + i) % p.Count]);
                    for (int i = 2; i < q.Count; i++)
                        union.Add(q[(l + i) % q.Count]);
                    for (int i = 0; i < union.Count; i++)
                    {
                        float2 x = points[union[i]], y = points[union[(i + 1) % union.Count]], z = points[union[(i + 2) % union.Count]];
                        if (Cross(y - x, z - y) < -1e-9f)
                            return null;
                    }
                    return union;
                }
            }
            return null;
        }
    }
}
