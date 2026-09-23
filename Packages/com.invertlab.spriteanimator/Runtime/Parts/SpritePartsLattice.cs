using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// One part's mesh as it is drawn: posed vertex positions, texture coordinates and triangles.
    /// Positions are in the unit quad (-0.5..0.5, y up). The rest shape is <c>Uv - 0.5</c>,
    /// so a deform offset is always <c>Point - (Uv - 0.5)</c>.
    /// Topology comes from <see cref="SpritePartMeshDef"/>; see <see cref="SpritePartsMeshOps"/>.
    /// </summary>
    public struct SpritePartsLattice
    {
        /// <summary>FixedList512Bytes&lt;float2&gt; holds 63 entries.</summary>
        public const int MaxVertices = 63;
        /// <summary>Enough for a fully triangulated 63-vertex mesh (2n - hull - 2 triangles, hull &gt;= 3).</summary>
        public const int MaxIndices = 366;

        public FixedList512Bytes<float2> Points;
        public FixedList512Bytes<float2> Uvs;
        /// <summary>Triangle list. Vertex indices fit in a byte (MaxVertices = 63).</summary>
        public FixedList512Bytes<byte> Indices;
        /// <summary>1 = already weighted and clipped (a clipping result): skinning leaves it alone.</summary>
        public byte Final;

        public int PointCount => Points.Length;
        public int IndexCount => Indices.Length;
        public bool HasMesh => Points.Length >= 3 && Indices.Length >= 3;
        public bool IsIdentity => Points.Length == 0;

        public float2 GetPoint(int index)
        {
            if ((uint)index >= (uint)Points.Length)
                return float2.zero;
            return Points[index];
        }

        public float2 GetUv(int index)
        {
            if ((uint)index >= (uint)Uvs.Length)
                return new float2(0.5f, 0.5f);
            return Uvs[index];
        }

        public float2 GetRest(int index) => GetUv(index) - 0.5f;

        public float2 GetOffset(int index) => GetPoint(index) - GetRest(index);

        public int GetIndex(int index)
        {
            if ((uint)index >= (uint)Indices.Length)
                return 0;
            return Indices[index];
        }

        public void SetPoint(int index, float2 value)
        {
            if ((uint)index >= (uint)Points.Length)
                return;
            Points[index] = value;
        }

        /// <summary>
        /// Builds the rest mesh from setup data. Vertices are texture coordinates (0..1).
        /// Returns an empty lattice when the data is incomplete or too large.
        /// </summary>
        public static SpritePartsLattice FromMesh(SpritePartMeshDef mesh)
        {
            if (mesh == null || !mesh.HasMesh)
                return default;
            var verts = mesh.Vertices;
            var tris = mesh.Triangles;
            if (verts.Length > MaxVertices || tris.Length > MaxIndices || tris.Length % 3 != 0)
                return default;
            var lattice = new SpritePartsLattice();
            for (int i = 0; i < verts.Length; i++)
            {
                var uv = new float2(verts[i].x, verts[i].y);
                lattice.Uvs.Add(uv);
                lattice.Points.Add(uv - 0.5f);
            }
            for (int i = 0; i < tris.Length; i++)
            {
                if ((uint)tris[i] >= (uint)verts.Length)
                    return default;
                lattice.Indices.Add((byte)tris[i]);
            }
            return lattice;
        }

        /// <summary>Offsets for a key. Null when the key does not deform, or the count does not match.</summary>
        public static FixedList512Bytes<float2> DeformFromArray(Vector2[] offsets, int vertexCount)
        {
            var list = new FixedList512Bytes<float2>();
            if (offsets == null || offsets.Length != vertexCount || vertexCount > MaxVertices)
                return list;
            for (int i = 0; i < offsets.Length; i++)
                list.Add(new float2(offsets[i].x, offsets[i].y));
            return list;
        }

        /// <summary>
        /// Posed positions = rest + lerp(a, b, t). An offset list whose length is not the vertex count
        /// counts as zero, the way a missing deform key falls back to the setup mesh in Spine.
        /// </summary>
        public static void ApplyDeform(
            ref SpritePartsLattice mesh, in FixedList512Bytes<float2> a, in FixedList512Bytes<float2> b, float t)
        {
            int n = mesh.Points.Length;
            if (n == 0)
                return;
            bool hasA = a.Length == n;
            bool hasB = b.Length == n;
            if (!hasA && !hasB)
                return;
            for (int i = 0; i < n; i++)
            {
                float2 oa = hasA ? a[i] : float2.zero;
                float2 ob = hasB ? b[i] : float2.zero;
                mesh.Points[i] = mesh.GetRest(i) + math.lerp(oa, ob, t);
            }
        }

        /// <summary>Key offsets for <see cref="SpritePartsKeyDef.Deform"/>. Null when the mesh sits at rest.</summary>
        public Vector2[] OffsetArray()
        {
            if (!HasMesh)
                return null;
            bool any = false;
            var result = new Vector2[Points.Length];
            for (int i = 0; i < Points.Length; i++)
            {
                float2 o = GetOffset(i);
                if (math.lengthsq(o) > 1e-12f)
                    any = true;
                result[i] = new Vector2(o.x, o.y);
            }
            return any ? result : null;
        }

        /// <summary>Blends two posed meshes. Different topology snaps at the midpoint.</summary>
        public static SpritePartsLattice Lerp(in SpritePartsLattice a, in SpritePartsLattice b, float t)
        {
            if (a.PointCount != b.PointCount || a.IndexCount != b.IndexCount)
                return t < 0.5f ? a : b;
            var mesh = a;
            for (int i = 0; i < a.PointCount; i++)
                mesh.Points[i] = math.lerp(a.Points[i], b.Points[i], t);
            return mesh;
        }
    }

    public struct SpritePartLattice : IComponentData
    {
        public SpritePartsLattice Value;
    }
}
