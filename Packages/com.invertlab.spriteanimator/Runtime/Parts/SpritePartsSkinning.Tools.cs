using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    // Weight setup helpers: bind a whole bone chain at once, pick the bones nearest a mesh,
    // and mirror weights across the Mirror axis with Left <-> Right bone names swapped.
    public static partial class SpritePartsSkinning
    {
        /// <summary>
        /// A bone and its chain, canonical ids, at most <paramref name="max"/>:
        /// its descendants (breadth first) when <paramref name="down"/>, else its parents up to the root.
        /// </summary>
        public static List<string> ChainBones(SpriteSheetProfile profile, string slotId, bool down, int max = MaxBones)
        {
            var result = new List<string>();
            var start = SpritePartsAuthoringOps.FindSlot(profile, slotId ?? string.Empty);
            if (start == null || max <= 0)
                return result;
            result.Add(SpritePartIdUtility.Canonical(start.SlotId));
            if (!down)
            {
                var cur = start;
                while (result.Count < max && !string.IsNullOrWhiteSpace(cur.ParentSlotId))
                {
                    cur = SpritePartsAuthoringOps.FindSlot(profile, cur.ParentSlotId);
                    if (cur == null || result.Contains(SpritePartIdUtility.Canonical(cur.SlotId)))
                        break;
                    result.Add(SpritePartIdUtility.Canonical(cur.SlotId));
                }
                return result;
            }
            for (int head = 0; head < result.Count && result.Count < max; head++)
            {
                foreach (var s in profile.PartsSlots)
                {
                    if (s == null || string.IsNullOrWhiteSpace(s.ParentSlotId)
                        || SpritePartIdUtility.Canonical(s.ParentSlotId) != result[head])
                        continue;
                    string id = SpritePartIdUtility.Canonical(s.SlotId);
                    if (!result.Contains(id))
                        result.Add(id);
                    if (result.Count >= max)
                        break;
                }
            }
            return result;
        }

        /// <summary>
        /// Indices of the bone segments nearest the mesh, best first, at most <paramref name="max"/>.
        /// A bone whose start, middle or end lies inside a triangle scores 0; others score their closest
        /// vertex distance and count only within <paramref name="reach"/>. Root space.
        /// </summary>
        public static List<int> NearestBones(IReadOnlyList<float2> vertices, IReadOnlyList<float2> boneStart,
            IReadOnlyList<float2> boneEnd, float reach, int max, IReadOnlyList<int> triangles = null)
        {
            var scored = new List<(float d, int b)>();
            for (int b = 0; b < boneStart.Count; b++)
            {
                float best = float.MaxValue;
                float2 a = boneStart[b], e = boneEnd[b];
                if (triangles != null
                    && (InsideTriangles(vertices, triangles, a) || InsideTriangles(vertices, triangles, (a + e) * 0.5f)
                        || InsideTriangles(vertices, triangles, e)))
                    best = 0f;
                foreach (var v in vertices)
                    best = math.min(best, DistanceToSegment(v, a, e));
                if (best <= reach)
                    scored.Add((best, b));
            }
            scored.Sort((x, y) => x.d.CompareTo(y.d));
            var result = new List<int>();
            for (int i = 0; i < scored.Count && result.Count < max; i++)
                result.Add(scored[i].b);
            return result;
        }

        static bool InsideTriangles(IReadOnlyList<float2> vertices, IReadOnlyList<int> triangles, float2 p)
        {
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                int i = triangles[t], j = triangles[t + 1], k = triangles[t + 2];
                if ((uint)i >= (uint)vertices.Count || (uint)j >= (uint)vertices.Count || (uint)k >= (uint)vertices.Count)
                    continue;
                float2 a = vertices[i], b = vertices[j], c = vertices[k];
                if (math.abs(Cross(b - a, c - a)) < 1e-12f)
                    continue; // degenerate
                float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
                bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
                bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
                if (!(neg && pos))
                    return true;
            }
            return false;
        }

        static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;

        /// <summary>
        /// The name of the other side: Left/Right words, or L/R as a separate token
        /// ("arm_L", "L Arm", "hand.l"). Null when the name has no side.
        /// </summary>
        public static string MirrorSideName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            // No static table: this class is also compiled by Burst.
            var sideWords = new[] { ("Left", "Right"), ("left", "right"), ("LEFT", "RIGHT") };
            foreach (var (a, b) in sideWords)
            {
                if (name.Contains(a))
                    return name.Replace(a, "\u0001").Replace(b, a).Replace("\u0001", b);
                if (name.Contains(b))
                    return name.Replace(b, a);
            }
            // A lone L / R between separators (start, end, space, _ . -).
            var token = new Regex(@"(?<=^|[\s_.\-])([LRlr])(?=$|[\s_.\-])");
            if (!token.IsMatch(name))
                return null;
            return token.Replace(name, m => m.Value switch { "L" => "R", "R" => "L", "l" => "r", _ => "l" });
        }

        /// <summary>
        /// Copies the weights of one side onto the other across x = <paramref name="axis"/> (image space):
        /// each partner vertex takes its twin's row with bones swapped by <paramref name="boneSwap"/>
        /// (<c>boneSwap[b]</c> = the other side's bone index, or b). Vertices on the axis get a symmetric blend.
        /// Returns the number of pairs written.
        /// </summary>
        public static int MirrorWeights(SpritePartMeshDef mesh, float axis, bool leftToRight, IReadOnlyList<int> boneSwap, float tolerance = 0.03f)
        {
            if (mesh == null || !mesh.HasWeights)
                return 0;
            int bones = mesh.BoneCount;
            var partner = SpritePartsMeshOps.MirrorPartners(mesh, axis, tolerance);
            var source = (float[])mesh.Weights.Clone();
            int pairs = 0;
            for (int i = 0; i < partner.Length; i++)
            {
                int p = partner[i];
                if (p < 0)
                    continue;
                if (p == i)
                {
                    // On the axis: average the row with its swapped self so both sides pull equally.
                    for (int b = 0; b < bones; b++)
                        mesh.Weights[i * bones + b] = 0f;
                    for (int b = 0; b < bones; b++)
                    {
                        float w = source[i * bones + b] * 0.5f;
                        mesh.Weights[i * bones + b] += w;
                        mesh.Weights[i * bones + Swap(boneSwap, b, bones)] += w;
                    }
                    continue;
                }
                bool left = mesh.Vertices[i].x < axis;
                if (left != leftToRight)
                    continue; // i is on the side that gets written
                for (int b = 0; b < bones; b++)
                    mesh.Weights[p * bones + b] = 0f;
                for (int b = 0; b < bones; b++)
                    mesh.Weights[p * bones + Swap(boneSwap, b, bones)] += source[i * bones + b];
                pairs++;
            }
            Normalize(mesh.Weights, bones);
            return pairs;
        }

        static int Swap(IReadOnlyList<int> boneSwap, int b, int bones)
        {
            int s = boneSwap != null && b < boneSwap.Count ? boneSwap[b] : b;
            return (uint)s < (uint)bones ? s : b;
        }
    }
}
