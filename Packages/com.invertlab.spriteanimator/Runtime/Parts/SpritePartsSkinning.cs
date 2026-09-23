using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Spine-style weights (linear blend skinning) for Parts meshes. The "bones" are part slots.
    /// Every weighted vertex is carried by each bound bone from the bind pose (rest) to the
    /// current pose, and the results are blended by weight:
    /// <c>p = Σ w_b · (Mesh⁻¹ · Bone_b · BoneRest_b⁻¹ · MeshRest) · p_rest</c>, in part space.
    /// Deform keys stay in pre-skin space (applied first), the same as Spine deform on weighted meshes.
    /// </summary>
    public static partial class SpritePartsSkinning
    {
        public const int MaxBones = 8;

        public static bool IsSkinned(ref SpritePartSlotBlob slot)
            => slot.SkinBones.Length > 0 && slot.Mesh.HasMesh
               && slot.SkinWeights.Length == slot.SkinBones.Length * slot.Mesh.PointCount;

        /// <summary>
        /// Skins <paramref name="lattice"/> (unit-quad points of slot <paramref name="slotIndex"/>) in place.
        /// <paramref name="localToRoot"/> is the current pose of every slot. No-op for unweighted meshes.
        /// </summary>
        public static void Apply(
            ref SpritePartsSetBlob set, int slotIndex, NativeArray<float4x4> localToRoot, ref SpritePartsLattice lattice)
        {
            if ((uint)slotIndex >= (uint)set.Slots.Length || !localToRoot.IsCreated)
                return;
            ref var slot = ref set.Slots[slotIndex];
            if (!IsSkinned(ref slot) || lattice.PointCount != slot.Mesh.PointCount || lattice.Final != 0)
                return;
            var bones = new FixedList4096Bytes<float4x4>();
            if (!TryBoneTransforms(ref set, slotIndex, localToRoot, ref bones))
                return;
            int boneCount = bones.Length;
            float2 size = slot.SkinQuadSize;
            float2 pivot = slot.SkinQuadPivot;
            for (int v = 0; v < lattice.PointCount; v++)
            {
                float2 local = (lattice.Points[v] + 0.5f - pivot) * size;
                float2 acc = float2.zero;
                float total = 0f;
                for (int b = 0; b < boneCount; b++)
                {
                    float w = slot.SkinWeights[v * boneCount + b];
                    if (w <= 0f)
                        continue;
                    acc += w * math.mul(bones[b], new float4(local, 0f, 1f)).xy;
                    total += w;
                }
                if (total <= 1e-6f)
                    continue;
                lattice.Points[v] = acc / total / size - 0.5f + pivot;
            }
        }

        /// <summary>
        /// Linear part (2x2, part space) of the blended bone transform at a vertex.
        /// The editor inverts it so a drag on a skinned vertex becomes a pre-skin deform offset.
        /// Identity for unweighted meshes.
        /// </summary>
        public static float2x2 VertexLinear(
            ref SpritePartsSetBlob set, int slotIndex, NativeArray<float4x4> localToRoot, int vertex)
        {
            if ((uint)slotIndex >= (uint)set.Slots.Length || !localToRoot.IsCreated)
                return float2x2.identity;
            ref var slot = ref set.Slots[slotIndex];
            if (!IsSkinned(ref slot) || (uint)vertex >= (uint)slot.Mesh.PointCount)
                return float2x2.identity;
            var bones = new FixedList4096Bytes<float4x4>();
            if (!TryBoneTransforms(ref set, slotIndex, localToRoot, ref bones))
                return float2x2.identity;
            var m = new float2x2();
            float total = 0f;
            for (int b = 0; b < bones.Length; b++)
            {
                float w = slot.SkinWeights[vertex * bones.Length + b];
                if (w <= 0f)
                    continue;
                m += w * new float2x2(bones[b].c0.xy, bones[b].c1.xy);
                total += w;
            }
            return total > 1e-6f ? m * (1f / total) : float2x2.identity;
        }

        static bool TryBoneTransforms(
            ref SpritePartsSetBlob set, int slotIndex, NativeArray<float4x4> localToRoot,
            ref FixedList4096Bytes<float4x4> bones)
        {
            ref var slot = ref set.Slots[slotIndex];
            if (slotIndex >= localToRoot.Length)
                return false;
            float4x4 meshInv = math.inverse(localToRoot[slotIndex]);
            for (int b = 0; b < slot.SkinBones.Length && b < MaxBones; b++)
            {
                int bone = slot.SkinBones[b];
                if ((uint)bone >= (uint)set.Slots.Length || bone >= localToRoot.Length)
                    return false;
                float4x4 bind = math.mul(math.inverse(set.Slots[bone].RestToRoot), slot.RestToRoot);
                bones.Add(math.mul(meshInv, math.mul(localToRoot[bone], bind)));
            }
            return bones.Length == slot.SkinBones.Length;
        }

        // ------------------------------------------------------------------ authoring helpers (managed)

        /// <summary>Quad size/pivot of the slot's default appearance, the space weights are bound in.</summary>
        public static bool TryResolveQuad(SpriteSheetProfile profile, SpritePartSlotDef slot, out float2 size, out float2 pivot)
        {
            size = float2.zero;
            pivot = new float2(0.5f, 0.5f);
            if (profile?.PartsAppearances == null || slot == null || string.IsNullOrWhiteSpace(slot.DefaultAppearanceId))
                return false;
            int index = SpritePartsValidation.FindAppearanceIndex(profile, SpritePartIdUtility.Canonical(slot.DefaultAppearanceId));
            if (index < 0 || !SpritePartsGeometry.TryResolve(profile, profile.PartsAppearances[index], false, out var geo, out _))
                return false;
            size = geo.LogicalWorldSize;
            pivot = geo.Pivot;
            return size.x > 1e-6f && size.y > 1e-6f;
        }

        /// <summary>
        /// Spine's automatic weights, simplified: each vertex is weighted by inverse distance (power 4)
        /// to every bone segment (joint to its first child joint, or the joint point), normalized,
        /// then influences under 1% are pruned. Inputs are in character-root space at rest.
        /// </summary>
        public static float[] AutoWeights(IReadOnlyList<float2> vertices, IReadOnlyList<float2> boneStart, IReadOnlyList<float2> boneEnd)
        {
            int n = vertices.Count;
            int bones = boneStart.Count;
            var weights = new float[n * bones];
            if (bones == 0)
                return weights;
            float scale = 1e-6f;
            for (int b = 0; b < bones; b++)
                scale = math.max(scale, math.length(boneEnd[b] - boneStart[b]));
            float eps = scale * scale * 1e-3f + 1e-8f;
            for (int v = 0; v < n; v++)
            {
                for (int b = 0; b < bones; b++)
                {
                    float d = DistanceToSegment(vertices[v], boneStart[b], boneEnd[b]);
                    float inv = 1f / (d * d + eps);
                    weights[v * bones + b] = inv * inv;
                }
            }
            Normalize(weights, bones);
            Prune(weights, bones, 0.01f);
            return weights;
        }

        /// <summary>Each row sums to 1. A row of zeros goes fully to bone 0.</summary>
        public static void Normalize(float[] weights, int bones)
        {
            if (weights == null || bones <= 0)
                return;
            for (int v = 0; v + bones <= weights.Length; v += bones)
            {
                float sum = 0f;
                for (int b = 0; b < bones; b++)
                    sum += weights[v + b] = math.max(0f, weights[v + b]);
                if (sum <= 1e-8f)
                {
                    weights[v] = 1f;
                    continue;
                }
                for (int b = 0; b < bones; b++)
                    weights[v + b] /= sum;
            }
        }

        /// <summary>Spine Prune: drops influences below <paramref name="threshold"/> and renormalizes.</summary>
        public static void Prune(float[] weights, int bones, float threshold)
        {
            if (weights == null || bones <= 0)
                return;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] < threshold)
                    weights[i] = 0f;
            }
            Normalize(weights, bones);
        }

        /// <summary>Spine Smooth: averages each chosen vertex with its triangle neighbours.</summary>
        public static void Smooth(float[] weights, int bones, int[] triangles, IReadOnlyCollection<int> only, float amount = 0.5f)
        {
            if (weights == null || bones <= 0 || triangles == null)
                return;
            int n = weights.Length / bones;
            var neighbours = new List<int>[n];
            for (int i = 0; i < n; i++)
                neighbours[i] = new List<int>(6);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = triangles[t + e];
                    int c = triangles[t + (e + 1) % 3];
                    if ((uint)a >= (uint)n || (uint)c >= (uint)n)
                        continue;
                    if (!neighbours[a].Contains(c)) neighbours[a].Add(c);
                    if (!neighbours[c].Contains(a)) neighbours[c].Add(a);
                }
            }
            var source = (float[])weights.Clone();
            for (int v = 0; v < n; v++)
            {
                if (only != null && only.Count > 0 && !Contains(only, v))
                    continue;
                if (neighbours[v].Count == 0)
                    continue;
                for (int b = 0; b < bones; b++)
                {
                    float avg = 0f;
                    foreach (int u in neighbours[v])
                        avg += source[u * bones + b];
                    avg /= neighbours[v].Count;
                    weights[v * bones + b] = math.lerp(source[v * bones + b], avg, amount);
                }
            }
            Normalize(weights, bones);
        }

        /// <summary>
        /// Sets one bone's weight on a vertex and rescales the others so the row still sums to 1
        /// (Spine Direct / Add / Remove).
        /// </summary>
        public static void SetWeight(float[] weights, int bones, int vertex, int bone, float value)
        {
            if (weights == null || (uint)bone >= (uint)bones)
                return;
            int row = vertex * bones;
            if (row < 0 || row + bones > weights.Length)
                return;
            value = math.saturate(value);
            float others = 0f;
            for (int b = 0; b < bones; b++)
            {
                if (b != bone)
                    others += weights[row + b];
            }
            float rest = 1f - value;
            for (int b = 0; b < bones; b++)
            {
                if (b == bone)
                    continue;
                weights[row + b] = others > 1e-8f
                    ? weights[row + b] / others * rest
                    : bones > 1 ? rest / (bones - 1) : 0f;
            }
            weights[row + bone] = bones == 1 ? 1f : value;
        }

        /// <summary>
        /// Carries weights across a topology change. <paramref name="remap"/>[old] = new index or -1.
        /// New vertices take inverse-distance weights from the two nearest old vertices (rest positions).
        /// </summary>
        public static float[] CarryWeights(SpritePartMeshDef from, Vector2[] newVertices, int[] remap)
        {
            if (from == null || !from.HasWeights || newVertices == null)
                return null;
            int bones = from.BoneCount;
            int oldCount = from.VertexCount;
            var result = new float[newVertices.Length * bones];
            var filled = new bool[newVertices.Length];
            for (int o = 0; o < oldCount; o++)
            {
                int to = remap != null && o < remap.Length ? remap[o] : o;
                if ((uint)to >= (uint)newVertices.Length)
                    continue;
                System.Array.Copy(from.Weights, o * bones, result, to * bones, bones);
                filled[to] = true;
            }
            for (int v = 0; v < newVertices.Length; v++)
            {
                if (filled[v])
                    continue;
                int best = -1, second = -1;
                float bestD = float.MaxValue, secondD = float.MaxValue;
                for (int o = 0; o < oldCount; o++)
                {
                    float d = Vector2.Distance(from.Vertices[o], newVertices[v]);
                    if (d < bestD)
                    {
                        second = best; secondD = bestD;
                        best = o; bestD = d;
                    }
                    else if (d < secondD)
                    {
                        second = o; secondD = d;
                    }
                }
                if (best < 0)
                {
                    result[v * bones] = 1f;
                    continue;
                }
                float wa = 1f / math.max(1e-5f, bestD);
                float wb = second >= 0 ? 1f / math.max(1e-5f, secondD) : 0f;
                for (int b = 0; b < bones; b++)
                {
                    float value = from.Weights[best * bones + b] * wa;
                    if (second >= 0)
                        value += from.Weights[second * bones + b] * wb;
                    result[v * bones + b] = value / (wa + wb);
                }
            }
            Normalize(result, bones);
            return result;
        }

        static bool Contains(IReadOnlyCollection<int> set, int value)
        {
            foreach (int i in set)
            {
                if (i == value)
                    return true;
            }
            return false;
        }

        static float DistanceToSegment(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float len = math.lengthsq(ab);
            float t = len < 1e-12f ? 0f : math.saturate(math.dot(p - a, ab) / len);
            return math.length(p - (a + ab * t));
        }
    }
}
