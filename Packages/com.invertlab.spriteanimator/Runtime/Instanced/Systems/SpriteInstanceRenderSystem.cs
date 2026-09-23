using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Per-sheet grid singleton (cells row-major, row 0 = top of sheet).</summary>
    public struct SpriteAnimGrid : IComponentData
    {
        public int Cols;
        public int Rows;
        /// <summary>Pixel width / height of one sheet cell. 0 or 1 = square.</summary>
        public float CellAspect;
        /// <summary>1 when a per-slot CropST buffer is installed (Cropped layout).</summary>
        public byte UseCellCrops;
    }

    /// <summary>Optional per-slot CropST (xy size, zw origin) on the grid singleton entity.</summary>
    public struct SpriteAnimCellCrop : IBufferElementData
    {
        public float4 Value;
    }

    /// <summary>Per-entity tint. Required by the instanced path; white for untinted.</summary>
    public struct SpriteTint : IComponentData
    {
        public float4 Value;
    }

    /// <summary>Packed per-instance GPU data. Mirrors the shader struct exactly.</summary>
    public struct SpriteInstanceData
    {
        public float4 PosScale;   // XY: xy=world xy, z=1, w=depth z | XZ: xy=world xz, z=scale, w=height y
        public float4 CropST;     // xy = cell size, zw = cell origin (uv bottom-left)
        public float4 FrameTRS;   // xy = frame scale, z = rotation radians, w = reserved
        public float4 Flip;       // xy = flip flags, zw = normalized pivot
        public float4 Transform2; // xy = entity scale (world), z = entity rotation radians, w = reserved
        public float4 Color;      // rgba tint
        public float4 WarpMeta;   // x = polygon vertex count (0 = rigid quad), y = scratch index, z = index count
    }

    /// <summary>
    /// Managed scratch + legacy defaults. Multi-sheet GPU buffers live in
    /// SpriteSheetRegistry records; these statics hold the shared quad, the
    /// legacy default sheet (SetSheet path), and the pack scratch arrays.
    /// </summary>
    public static class SpriteRenderResources
    {
        public const int Stride = 112; // 7 * float4 (rigid quad; warp points live in WarpScratch)

        public static NativeArray<SpriteInstanceData> Staging;  // pack target (indexed by entity)
        public static NativeArray<SpriteInstanceData> Sorted;   // scatter target (grouped by record)
        public static NativeArray<int> RecordIds;
        public static NativeArray<float2> WarpScratch;
        public static NativeArray<float2> WarpUvScratch;
        public static NativeArray<int> WarpIndexScratch;
        public static ComputeBuffer WarpDummy;
        public static ComputeBuffer WarpInstances;
        public static ComputeBuffer WarpPoints;
        public static int WarpInstanceCapacity;
        public static int WarpPointCapacity;
        public static Mesh WarpCombined;
        public static Material WarpMeshMaterial;

        public static void DestroyWarpResources()
        {
            if (WarpScratch.IsCreated) WarpScratch.Dispose();
            if (WarpUvScratch.IsCreated) WarpUvScratch.Dispose();
            if (WarpIndexScratch.IsCreated) WarpIndexScratch.Dispose();
            WarpScratch = default;
            WarpUvScratch = default;
            WarpIndexScratch = default;
            WarpDummy?.Dispose();
            WarpInstances?.Dispose();
            WarpPoints?.Dispose();
            WarpDummy = null;
            WarpInstances = null;
            WarpPoints = null;
            WarpInstanceCapacity = 0;
            WarpPointCapacity = 0;
            SpriteRenderResourceLifetimeSystem.DestroyOwnedObject(WarpCombined);
            SpriteRenderResourceLifetimeSystem.DestroyOwnedObject(WarpMeshMaterial);
            WarpCombined = null;
            WarpMeshMaterial = null;
        }

        public static void EnsureWarpBuffers(int instances, int points)
        {
            if (WarpDummy == null)
                WarpDummy = new ComputeBuffer(1, sizeof(float) * 2);
            if (WarpInstances == null || instances > WarpInstanceCapacity)
            {
                WarpInstances?.Dispose();
                WarpInstanceCapacity = math.max(64, instances);
                WarpInstances = new ComputeBuffer(WarpInstanceCapacity, Stride);
            }
            if (WarpPoints == null || points > WarpPointCapacity)
            {
                WarpPoints?.Dispose();
                WarpPointCapacity = math.max(64, points);
                WarpPoints = new ComputeBuffer(WarpPointCapacity, sizeof(float) * 2);
            }
        }
        public static Material Material;                        // legacy default-sheet material
        public static Mesh Quad;
        public static Texture2D Sheet;
        public static int Capacity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            if (Staging.IsCreated) Staging.Dispose();
            if (Sorted.IsCreated) Sorted.Dispose();
            if (RecordIds.IsCreated) RecordIds.Dispose();
            if (WarpScratch.IsCreated) WarpScratch.Dispose();
            if (WarpUvScratch.IsCreated) WarpUvScratch.Dispose();
            if (WarpIndexScratch.IsCreated) WarpIndexScratch.Dispose();
            SpriteRenderResourceLifetimeSystem.DestroyOwnedObject(Material);
            SpriteRenderResourceLifetimeSystem.DestroyOwnedObject(Quad);
            Staging = default;
            Sorted = default;
            RecordIds = default;
            WarpScratch = default;
            WarpUvScratch = default;
            WarpIndexScratch = default;
            Material = null;
            Quad = null;
            DestroyWarpResources();
            Sheet = null;
            Capacity = 0;
        }

        /// <summary>Grow the pack scratch to hold at least <paramref name="need"/> instances.</summary>
        public static void EnsureCapacity(int need)
        {
            if (Staging.IsCreated && need <= Capacity && RecordIds.IsCreated && WarpScratch.IsCreated
                && WarpUvScratch.IsCreated && WarpIndexScratch.IsCreated)
                return;
            int cap = math.max(4096, Capacity);
            while (cap < need) cap *= 2;
            if (Staging.IsCreated) Staging.Dispose();
            if (Sorted.IsCreated) Sorted.Dispose();
            if (RecordIds.IsCreated) RecordIds.Dispose();
            Staging = new NativeArray<SpriteInstanceData>(cap, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            Sorted = new NativeArray<SpriteInstanceData>(cap, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            RecordIds = new NativeArray<int>(cap, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            if (WarpScratch.IsCreated) WarpScratch.Dispose();
            if (WarpUvScratch.IsCreated) WarpUvScratch.Dispose();
            if (WarpIndexScratch.IsCreated) WarpIndexScratch.Dispose();
            WarpScratch = new NativeArray<float2>(cap * SpritePartsLattice.MaxVertices, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            WarpUvScratch = new NativeArray<float2>(cap * SpritePartsLattice.MaxVertices, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            WarpIndexScratch = new NativeArray<int>(cap * SpritePartsLattice.MaxIndices, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            Capacity = cap;
        }

        /// <summary>Shared procedural quad. Vertices are ignored; the shader builds the quad from SV_VertexID.</summary>
        public static void EnsureQuad()
        {
            if (Quad != null)
                return;
            Quad = new Mesh { name = "InstancedSpriteQuad" };
            var ids = new int[6];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = i;
            Quad.vertices = new Vector3[6];
            Quad.uv = new Vector2[6];
            Quad.SetIndices(ids, MeshTopology.Triangles, 0);
            Quad.bounds = new Bounds(Vector3.zero, Vector3.one * 8f);
        }
    }

    /// <summary>
    /// NSprites-style renderer: ALL CPU-ticked sprites in per-sheet instanced
    /// batches — one ComputeBuffer + one DrawMeshInstancedProcedural per
    /// distinct sheet texture. Sprites bound to a baked sheet entity
    /// (SpriteSheetBinding → SpriteSheetRegistry record) group onto their
    /// sheet; unbound sprites draw on the legacy default sheet
    /// (SpriteRenderResources.Sheet). Packing runs in Burst; the draw tail is
    /// managed code. Reads LocalToWorld, so entity rotation, non-uniform
    /// scale, and parenting all render correctly.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial struct SpriteInstanceRenderSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
            => state.EntityManager.World.GetOrCreateSystemManaged<SpriteRenderResourceLifetimeSystem>();

        /// <summary>True once the instanced path has drawn at least one frame.</summary>
        public static bool Active;

        /// <summary>Last runtime failure inside the render update (diagnostics).</summary>
        public static string LastError;

        /// <summary>How many times OnUpdate entered (diagnostics).</summary>
        public static int Ticks;

        /// <summary>Hand the renderer the legacy default sheet (call once after Install).</summary>
        public static void SetSheet(Texture2D sheet)
        {
            if (sheet != SpriteRenderResources.Sheet && SpriteGpuAnimResources.HasGpuSprites())
                throw new System.InvalidOperationException("Convert active GPU sprites to CPU before changing the shared sheet.");
            SpriteRenderResources.Sheet = sheet;
        }

        public void OnUpdate(ref SystemState state)
        {
            Ticks++;
            try
            {
                UpdateInner(ref state);
                LastError = null;
            }
            catch (System.Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
                throw;
            }
        }

        void UpdateInner(ref SystemState state)
        {
            var em = state.EntityManager;

            if (!SystemAPI.TryGetSingleton(out SpriteAnimGrid grid))
            {
                var ge = em.CreateEntity();
                em.AddComponentData(ge, new SpriteAnimGrid { Cols = 4, Rows = 4, CellAspect = 1f, UseCellCrops = 0 });
                return; // draw next frame
            }

            // ---- seed the legacy default-sheet record (SetSheet path) ----
            int legacyId = -1;
            if (SpriteRenderResources.Sheet != null)
            {
                {
                    float4[] crops = null;
                    if (grid.UseCellCrops != 0)
                    {
                        var gridEntity = SystemAPI.GetSingletonEntity<SpriteAnimGrid>();
                        if (em.HasBuffer<SpriteAnimCellCrop>(gridEntity))
                        {
                            var buf = em.GetBuffer<SpriteAnimCellCrop>(gridEntity);
                            if (buf.Length > 0)
                            {
                                crops = new float4[buf.Length];
                                for (int c = 0; c < buf.Length; c++)
                                    crops[c] = buf[c].Value;
                            }
                        }
                    }
                    legacyId = SpriteSheetRegistry.GetOrAdd(SpriteRenderResources.Sheet,
                        grid.Cols, grid.Rows, grid.CellAspect, crops, em.World.SequenceNumber);
                }
            }

            int recordCount = SpriteSheetRegistry.Records.Count;
            if (recordCount == 0) return;
            SpriteRenderResources.EnsureQuad();

            var q = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, LocalToWorld, SpriteAnimFrame, SpriteTint, SpriteFlip, SpriteAnimEnabled>()
                .WithNone<SpriteGpuDriven>() // GPU-driven sprites draw via SpriteGpuAnimRenderSystem
                .Build();
            int total = q.CalculateEntityCount(); // enable-bit aware: culled sprites excluded
            Active = false;
            if (total == 0) return;

            SpriteRenderResources.EnsureCapacity(total);

            // per-record grid tables for the pack job
            var gridCR = new NativeArray<int2>(recordCount, Allocator.TempJob);
            var useCropsArr = new NativeArray<byte>(recordCount, Allocator.TempJob);
            var cropOffsets = new NativeArray<int>(recordCount, Allocator.TempJob);
            var cropCounts = new NativeArray<int>(recordCount, Allocator.TempJob);
            int cropTotal = 0;
            for (int r = 0; r < recordCount; r++)
            {
                var rec = SpriteSheetRegistry.Records[r];
                gridCR[r] = new int2(rec.Cols, rec.Rows);
                useCropsArr[r] = rec.UseCellCrops;
                cropOffsets[r] = cropTotal;
                cropCounts[r] = rec.UseCellCrops != 0 && rec.Crops.IsCreated ? rec.Crops.Length : 0;
                cropTotal += cropCounts[r];
            }
            var allCrops = new NativeArray<float4>(math.max(1, cropTotal), Allocator.TempJob);
            for (int r = 0; r < recordCount; r++)
            {
                var rec = SpriteSheetRegistry.Records[r];
                if (rec.UseCellCrops != 0 && rec.Crops.IsCreated)
                    for (int c = 0; c < rec.Crops.Length; c++)
                        allCrops[cropOffsets[r] + c] = rec.Crops[c];
            }

            var counts = new NativeArray<int>(recordCount, Allocator.TempJob);
            var cursors = new NativeArray<int>(recordCount, Allocator.TempJob);

            var pack = new PackJob
            {
                LegacyRecordId = legacyId,
                LayoutXy = (byte)(SpriteBatchSpawner.LayoutXy ? 1 : 0),
                Bindings = SystemAPI.GetComponentLookup<SpriteSheetBinding>(true),
                Registered = SystemAPI.GetComponentLookup<SpriteSheetRegistered>(true),
                PartDepths = SystemAPI.GetComponentLookup<SpritePartRenderDepth>(true),
                Lattices = SystemAPI.GetComponentLookup<SpritePartLattice>(true),
                WarpScratch = SpriteRenderResources.WarpScratch,
                WarpUvScratch = SpriteRenderResources.WarpUvScratch,
                WarpIndexScratch = SpriteRenderResources.WarpIndexScratch,
                GridCR = gridCR,
                UseCropsArr = useCropsArr,
                CropOffsets = cropOffsets,
                CropCounts = cropCounts,
                AllCrops = allCrops,
                Staging = SpriteRenderResources.Staging,
                RecordIds = SpriteRenderResources.RecordIds,
            };
            state.Dependency = pack.ScheduleParallel(q, state.Dependency);
            state.Dependency.Complete();

            // group instances by record: count → prefix offsets → scatter
            new CountJob { RecordIds = SpriteRenderResources.RecordIds, Counts = counts, Total = total }
                .Schedule().Complete();

            int running = 0;
            for (int r = 0; r < recordCount; r++)
            {
                cursors[r] = running;
                running += counts[r];
            }

            new ScatterJob
            {
                RecordIds = SpriteRenderResources.RecordIds,
                Cursors = cursors,
                Source = SpriteRenderResources.Staging,
                Target = SpriteRenderResources.Sorted,
                Total = total,
            }.Schedule().Complete();

            // ---- draw one batch per record ----
            bool layoutXy = SpriteBatchSpawner.LayoutXy;
            for (int r = 0; r < recordCount; r++)
            {
                var rec = SpriteSheetRegistry.Records[r];
                rec.Count = counts[r];
                if (rec.Count == 0)
                    continue;
                rec.EnsureCapacity(rec.Count);
                Bounds bounds = default;
                int start = cursors[r] - rec.Count;
                for (int i = 0; i < rec.Count; i++)
                {
                    var data = SpriteRenderResources.Sorted[start + i];
                    var center = layoutXy ? new Vector3(data.PosScale.x, data.PosScale.y, data.PosScale.w)
                        : new Vector3(data.PosScale.x, data.PosScale.w, data.PosScale.y);
                    float entityScale = layoutXy ? math.length(data.Transform2.xy) + math.length(data.Transform2.zw) : math.abs(data.PosScale.z);
                    float radius = entityScale * math.cmax(math.abs(data.FrameTRS.xy)) * math.max(1, rec.CellAspect)
                        * (1 + math.length(data.Flip.zw - .5f) * 2);
                    var item = new Bounds(center, Vector3.one * math.max(.01f, radius * 2));
                    if (i == 0) bounds = item; else bounds.Encapsulate(item);
                }
                // Scatter advances each cursor to the END of its batch.
                rec.Buffer.SetData(SpriteRenderResources.Sorted, cursors[r] - rec.Count, 0, rec.Count);
                rec.Material.SetFloat("_LayoutXy", layoutXy ? 1f : 0f);
                rec.Material.SetFloat("_CellAspect", rec.CellAspect > 0.01f ? rec.CellAspect : 1f);
                rec.Material.SetFloat("_WarpResolution", 0f);
                SpriteRenderResources.EnsureWarpBuffers(1, 1);
                rec.Material.SetBuffer("_WarpOffsets", SpriteRenderResources.WarpDummy);
                rec.Material.SetBuffer("_InstanceData", rec.Buffer);
                Graphics.DrawMeshInstancedProcedural(
                    SpriteRenderResources.Quad, 0, rec.Material, bounds, rec.Count,
                    null, UnityEngine.Rendering.ShadowCastingMode.Off, false, 0);
                DrawWarpedRecord(rec, start, bounds);
            }
            Active = true;

            gridCR.Dispose();
            useCropsArr.Dispose();
            cropOffsets.Dispose();
            cropCounts.Dispose();
            allCrops.Dispose();
            counts.Dispose();
            cursors.Dispose();
        }

        static void DrawWarpedRecord(SpriteSheetRecord rec, int start, Bounds bounds)
        {
            int vertCount = 0;
            int indexCount = 0;
            for (int i = 0; i < rec.Count; i++)
            {
                var meta = SpriteRenderResources.Sorted[start + i].WarpMeta;
                if (meta.x <= 0.5f)
                    continue;
                vertCount += (int)math.round(meta.x);
                indexCount += (int)math.round(meta.z);
            }
            if (vertCount < 3 || indexCount < 3)
                return;

            var vertices = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var colors = new Color[vertCount];
            var indices = new int[indexCount];
            float aspect = rec.CellAspect > 0.01f ? rec.CellAspect : 1f;
            float2 texel = rec.Texture != null
                ? new float2(1f / math.max(1, rec.Texture.width), 1f / math.max(1, rec.Texture.height))
                : float2.zero;
            int vbase = 0;
            int ibase = 0;
            for (int i = 0; i < rec.Count; i++)
            {
                var data = SpriteRenderResources.Sorted[start + i];
                int verts = (int)math.round(data.WarpMeta.x);
                int inds = (int)math.round(data.WarpMeta.z);
                if (verts < 3 || inds < 3)
                    continue;
                int src = (int)math.round(data.WarpMeta.y);
                int pointBase = src * SpritePartsLattice.MaxVertices;
                int indexBase = src * SpritePartsLattice.MaxIndices;
                float2 inset = texel / math.max(data.CropST.xy, new float2(1e-5f));
                for (int k = 0; k < verts; k++)
                {
                    vertices[vbase + k] = (Vector3)WarpPointWorld(data, SpriteRenderResources.WarpScratch[pointBase + k], aspect);
                    float2 uv = math.clamp(SpriteRenderResources.WarpUvScratch[pointBase + k], inset, 1f - inset);
                    float2 atlas = data.CropST.zw + uv * data.CropST.xy;
                    uvs[vbase + k] = new Vector2(atlas.x, atlas.y);
                    colors[vbase + k] = new Color(data.Color.x, data.Color.y, data.Color.z, data.Color.w);
                }
                for (int k = 0; k < inds; k++)
                    indices[ibase + k] = vbase + SpriteRenderResources.WarpIndexScratch[indexBase + k];
                vbase += verts;
                ibase += inds;
            }

            var mesh = SpriteRenderResources.WarpCombined;
            if (mesh == null)
            {
                mesh = new Mesh { name = "SpriteWarpCombined", hideFlags = HideFlags.HideAndDontSave };
                SpriteRenderResources.WarpCombined = mesh;
            }
            mesh.Clear();
            mesh.indexFormat = vertCount > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.bounds = bounds;

            var material = SpriteRenderResources.WarpMeshMaterial;
            if (material == null)
            {
                var shader = Shader.Find(SpriteShaderLibrary.WarpMeshShader);
                if (shader == null)
                    return;
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                SpriteRenderResources.WarpMeshMaterial = material;
            }
            material.mainTexture = rec.Texture;
            if (rec.Material != null && rec.Material.HasProperty("_Cutoff"))
                material.SetFloat("_Cutoff", rec.Material.GetFloat("_Cutoff"));
            Graphics.DrawMesh(mesh, Matrix4x4.identity, material, 0);
        }

        static float3 WarpPointWorld(in SpriteInstanceData d, float2 quad, float aspect)
        {
            float2 pivot = d.Flip.zw;
            if (pivot.x == 0f && pivot.y == 0f)
                pivot = new float2(0.5f, 0.5f);
            float2 posed = quad;
            if (d.Flip.x > 0.5f)
                posed.x = 2f * (pivot.x - 0.5f) - quad.x;
            if (d.Flip.y > 0.5f)
                posed.y = 2f * (pivot.y - 0.5f) - quad.y;
            float2 local = new float2(posed.x * d.FrameTRS.x * aspect, posed.y * d.FrameTRS.y);
            float cs = math.cos(d.FrameTRS.z);
            float sn = math.sin(d.FrameTRS.z);
            float2 rotated = new float2(local.x * cs - local.y * sn, local.x * sn + local.y * cs);
            if (d.FrameTRS.w > 0.5f)
            {
                float2 entityRotated = d.Transform2.xy * rotated.x + d.Transform2.zw * rotated.y;
                return new float3(d.PosScale.x + entityRotated.x, d.PosScale.y + entityRotated.y, d.PosScale.w);
            }
            return new float3(
                d.PosScale.x + rotated.x * d.PosScale.z,
                d.PosScale.w,
                d.PosScale.y - rotated.y * d.PosScale.z);
        }

        [BurstCompile]
        partial struct PackJob : IJobEntity
        {
            public int LegacyRecordId;
            public byte LayoutXy;
            [ReadOnly] public ComponentLookup<SpriteSheetBinding> Bindings;
            [ReadOnly] public ComponentLookup<SpriteSheetRegistered> Registered;
            [ReadOnly] public ComponentLookup<SpritePartRenderDepth> PartDepths;
            [ReadOnly] public ComponentLookup<SpritePartLattice> Lattices;
            [NativeDisableParallelForRestriction] public NativeArray<float2> WarpScratch;
            [NativeDisableParallelForRestriction] public NativeArray<float2> WarpUvScratch;
            [NativeDisableParallelForRestriction] public NativeArray<int> WarpIndexScratch;
            [ReadOnly] public NativeArray<int2> GridCR;
            [ReadOnly] public NativeArray<byte> UseCropsArr;
            [ReadOnly] public NativeArray<int> CropOffsets;
            [ReadOnly] public NativeArray<int> CropCounts;
            [ReadOnly] public NativeArray<float4> AllCrops;
            [WriteOnly] public NativeArray<SpriteInstanceData> Staging;
            [WriteOnly] public NativeArray<int> RecordIds;

            void Execute([EntityIndexInQuery] int i,
                         Entity entity,
                         in LocalToWorld ltw,
                         in SpriteAnimFrame frame,
                         in SpriteFlip flip,
                         in SpriteTint tint)
            {
                // resolve the sprite's sheet record (default = legacy sheet)
                int record = LegacyRecordId;
                if (Bindings.HasComponent(entity))
                {
                    var binding = Bindings[entity];
                    if (binding.Sheet != Entity.Null)
                        record = Registered.HasComponent(binding.Sheet) ? Registered[binding.Sheet].RegistryId : -1;
                }
                RecordIds[i] = record;
                if (record < 0 || record >= GridCR.Length)
                    return; // unregistered sheet: skip this frame

                int slot = frame.Slot;
                int2 cr = GridCR[record];
                int cols = math.max(1, cr.x);
                int rows = math.max(1, cr.y);
                if (slot < 0 || (UseCropsArr[record] != 0 ? slot >= CropCounts[record] : (long)slot >= (long)cols * rows))
                {
                    RecordIds[i] = -1;
                    return;
                }
                int col = slot % cols;
                int row = slot / cols;

                float2 offset = SpriteFlipUtility.LocalPosition(frame.Offset, flip);
                float frameRotation = SpriteFlipUtility.Angle(frame.Rotation, flip);

                float4 cropST;
                if (UseCropsArr[record] != 0 && slot >= 0)
                {
                    int cropIndex = CropOffsets[record] + slot;
                    if (slot < CropCounts[record])
                        cropST = AllCrops[cropIndex];
                    else
                        cropST = new float4(1f / cols, 1f / rows,
                                            col * (1f / cols),
                                            (rows - 1 - row) * (1f / rows));
                }
                else
                {
                    cropST = new float4(1f / cols, 1f / rows,
                                        col * (1f / cols),
                                        (rows - 1 - row) * (1f / rows));
                }

                // entity transform from the world matrix (fresh LocalToWorld
                // covers gameplay movement, rotation, squash/stretch, parents)
                float3 worldPos = ltw.Value.c3.xyz;
                float3 xAxis = ltw.Value.c0.xyz;
                float entityRot = math.atan2(xAxis.y, xAxis.x);
                float entityScaleX = math.length(xAxis);
                float entityScaleY = math.length(ltw.Value.c1.xyz);

                float4 posScale;
                float4 transform2;
                if (LayoutXy != 0)
                {
                    float3 worldOffset = ltw.Value.c0.xyz * offset.x + ltw.Value.c1.xyz * offset.y;
                    float depthZ = worldPos.z + worldOffset.z;
                    if (PartDepths.HasComponent(entity))
                        depthZ = PartDepths[entity].Value;
                    posScale = new float4(worldPos.x + worldOffset.x, worldPos.y + worldOffset.y, 1f, depthZ);
                    // Keep both basis vectors: decomposing loses reflection and parent-induced shear.
                    transform2 = new float4(ltw.Value.c0.xy, ltw.Value.c1.xy);
                }
                else
                {
                    // flat-lay: z is a ground-plane axis; keep uniform scale and
                    // skip entity rotation (XZ sprites face up, not the camera)
                    posScale = new float4(worldPos.x + offset.x, worldPos.z + offset.y,
                                          entityScaleX, worldPos.y);
                    transform2 = new float4(1f, 1f, 0f, 0f);
                }

                int warpVerts = 0;
                int warpIndices = 0;
                if (Lattices.HasComponent(entity))
                {
                    var lattice = Lattices[entity].Value;
                    if (lattice.HasMesh)
                    {
                        warpVerts = lattice.PointCount;
                        warpIndices = lattice.IndexCount;
                        int pointBase = i * SpritePartsLattice.MaxVertices;
                        int indexBase = i * SpritePartsLattice.MaxIndices;
                        for (int k = 0; k < SpritePartsLattice.MaxVertices; k++)
                        {
                            WarpScratch[pointBase + k] = k < warpVerts ? lattice.GetPoint(k) : float2.zero;
                            WarpUvScratch[pointBase + k] = k < warpVerts ? lattice.GetUv(k) : float2.zero;
                        }
                        for (int k = 0; k < SpritePartsLattice.MaxIndices; k++)
                            WarpIndexScratch[indexBase + k] = k < warpIndices ? lattice.GetIndex(k) : 0;
                    }
                }

                Staging[i] = new SpriteInstanceData
                {
                    PosScale = posScale,
                    CropST = cropST,
                    FrameTRS = new float4(frame.Scale.x, frame.Scale.y, math.radians(frameRotation), LayoutXy != 0 ? 1f : 0f),
                    Flip = new float4(flip.X, flip.Y, flip.ResolvedPivot.x, flip.ResolvedPivot.y),
                    Transform2 = transform2,
                    Color = tint.Value,
                    WarpMeta = new float4(warpVerts, i, warpIndices, 0f),
                };
            }
        }

        [BurstCompile]
        struct CountJob : IJob
        {
            [ReadOnly] public NativeArray<int> RecordIds;
            public NativeArray<int> Counts;
            public int Total;

            public void Execute()
            {
                for (int i = 0; i < Total; i++)
                {
                    int record = RecordIds[i];
                    if ((uint)record < (uint)Counts.Length)
                        Counts[record]++;
                }
            }
        }

        [BurstCompile]
        struct ScatterJob : IJob
        {
            [ReadOnly] public NativeArray<int> RecordIds;
            public NativeArray<int> Cursors;
            [ReadOnly] public NativeArray<SpriteInstanceData> Source;
            [WriteOnly] public NativeArray<SpriteInstanceData> Target;
            public int Total;

            public void Execute()
            {
                for (int i = 0; i < Total; i++)
                {
                    int record = RecordIds[i];
                    if ((uint)record < (uint)Cursors.Length)
                        Target[Cursors[record]++] = Source[i];
                }
            }
        }

        /// <summary>Create the grid singleton if missing (idempotent).</summary>
        public static void Install(EntityManager em)
        {
            em.World.GetOrCreateSystemManaged<SpriteRenderResourceLifetimeSystem>();
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<SpriteAnimGrid>());
            if (q.CalculateEntityCount() > 0) return;
            var e = em.CreateEntity();
            em.AddComponentData(e, new SpriteAnimGrid { Cols = 4, Rows = 4, CellAspect = 1f, UseCellCrops = 0 });
        }

        /// <summary>Update the grid singleton (call when switching sheets).</summary>
        public static void SetGrid(EntityManager em, int cols, int rows, float cellAspect = 1f)
        {
            SetGrid(em, cols, rows, cellAspect, null);
        }

        /// <summary>
        /// Update the grid singleton. When <paramref name="cellCropSTs"/> is non-null and
        /// matches cols×rows, PackJob uses those CropST values (Cropped layout).
        /// Pass null to clear cropped UV overrides and use uniform Grid math.
        /// </summary>
        public static void SetGrid(EntityManager em, int cols, int rows, float cellAspect,
            Vector4[] cellCropSTs)
        {
            if (SpriteGpuAnimResources.HasGpuSprites() &&
                ((cellCropSTs != null && cellCropSTs.Length > 0) ||
                 !SpriteGpuAnimResources.CanUseSheet(SpriteRenderResources.Sheet, Mathf.Max(1, cols),
                     Mathf.Max(1, rows), cellAspect > 0.01f ? cellAspect : 1f)))
                throw new System.InvalidOperationException("Convert active GPU sprites to CPU before changing the shared sheet layout.");
            Install(em);
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<SpriteAnimGrid>());
            var entity = q.GetSingletonEntity();
            cols = Mathf.Max(1, cols);
            rows = Mathf.Max(1, rows);
            int expected = cols * rows;
            bool useCrops = cellCropSTs != null && cellCropSTs.Length == expected;
            em.SetComponentData(entity, new SpriteAnimGrid
            {
                Cols = cols,
                Rows = rows,
                CellAspect = cellAspect > 0.01f ? cellAspect : 1f,
                UseCellCrops = useCrops ? (byte)1 : (byte)0,
            });

            if (!em.HasBuffer<SpriteAnimCellCrop>(entity))
                em.AddBuffer<SpriteAnimCellCrop>(entity);
            var buffer = em.GetBuffer<SpriteAnimCellCrop>(entity);
            buffer.Clear();
            if (useCrops)
            {
                for (int i = 0; i < cellCropSTs.Length; i++)
                    buffer.Add(new SpriteAnimCellCrop { Value = new float4(cellCropSTs[i].x, cellCropSTs[i].y, cellCropSTs[i].z, cellCropSTs[i].w) });
            }
        }

        /// <summary>Install grid UV crops from a sheet profile def (no-op when Grid / empty).</summary>
        public static void SetGridFromSheet(EntityManager em, SpriteSheetDef sheet)
        {
            if (sheet == null)
            {
                SetGrid(em, 4, 4, 1f, null);
                return;
            }
            int cols = Mathf.Max(1, sheet.Columns);
            int rows = Mathf.Max(1, sheet.Rows);
            float aspect = SpriteSheetProfile.GetCellAspect(sheet);
            Vector4[] crops = null;
            if (sheet.CellLayoutMode == SpriteSheetCellLayoutMode.Cropped &&
                SpriteSheetProfile.HasCroppedCellData(sheet))
                crops = SpriteSheetProfile.BuildCellCropSTArray(sheet);
            SetGrid(em, cols, rows, aspect, crops);
        }
    }
}
