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
    }

    /// <summary>
    /// Managed scratch + legacy defaults. Multi-sheet GPU buffers live in
    /// SpriteSheetRegistry records; these statics hold the shared quad, the
    /// legacy default sheet (SetSheet path), and the pack scratch arrays.
    /// </summary>
    public static class SpriteRenderResources
    {
        public const int Stride = 96; // 6 * float4

        public static NativeArray<SpriteInstanceData> Staging;  // pack target (indexed by entity)
        public static NativeArray<SpriteInstanceData> Sorted;   // scatter target (grouped by record)
        public static NativeArray<int> RecordIds;               // record index per packed instance
        public static Material Material;                        // legacy default-sheet material
        public static Mesh Quad;
        public static Texture2D Sheet;
        public static int Capacity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            if (Staging.IsCreated) Staging.Dispose();
            if (Sorted.IsCreated) Sorted.Dispose();
            if (RecordIds.IsCreated) RecordIds.Dispose();
            if (Material != null) Object.Destroy(Material);
            if (Quad != null) Object.Destroy(Quad);
            Staging = default;
            Sorted = default;
            RecordIds = default;
            Material = null;
            Quad = null;
            Sheet = null;
            Capacity = 0;
        }

        /// <summary>Grow the pack scratch to hold at least <paramref name="need"/> instances.</summary>
        public static void EnsureCapacity(int need)
        {
            if (Staging.IsCreated && need <= Capacity && RecordIds.IsCreated)
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
            Capacity = cap;
        }

        /// <summary>Shared procedural quad (created once, reused by every record).</summary>
        public static void EnsureQuad()
        {
            if (Quad != null)
                return;
            Quad = new Mesh { name = "InstancedSpriteQuad" };
            Quad.vertices = new Vector3[6];
            Quad.uv = new Vector2[6];
            Quad.SetIndices(new[] { 0, 1, 2, 3, 4, 5 }, MeshTopology.Triangles, 0);
            Quad.RecalculateBounds();
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
    public partial struct SpriteInstanceRenderSystem : ISystem
    {
        /// <summary>True once the instanced path has drawn at least one frame.</summary>
        public static bool Active;

        /// <summary>Last runtime failure inside the render update (diagnostics).</summary>
        public static string LastError;

        /// <summary>How many times OnUpdate entered (diagnostics).</summary>
        public static int Ticks;

        /// <summary>Hand the renderer the legacy default sheet (call once after Install).</summary>
        public static void SetSheet(Texture2D sheet) => SpriteRenderResources.Sheet = sheet;

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
                legacyId = SpriteSheetRegistry.GetOrAdd(SpriteRenderResources.Sheet);
                if (legacyId >= 0)
                {
                    var legacy = SpriteSheetRegistry.Records[legacyId];
                    legacy.Cols = grid.Cols;
                    legacy.Rows = grid.Rows;
                    legacy.CellAspect = grid.CellAspect > 0.01f ? grid.CellAspect : 1f;
                    byte useCrops = 0;
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
                                useCrops = 1;
                            }
                        }
                    }
                    legacy.UseCellCrops = useCrops;
                    if (useCrops != 0)
                        legacy.SetCrops(crops);
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
            int cropTotal = 0;
            for (int r = 0; r < recordCount; r++)
            {
                var rec = SpriteSheetRegistry.Records[r];
                gridCR[r] = new int2(rec.Cols, rec.Rows);
                useCropsArr[r] = rec.UseCellCrops;
                cropOffsets[r] = cropTotal;
                cropTotal += rec.UseCellCrops != 0 && rec.Crops.IsCreated ? rec.Crops.Length : 0;
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
                GridCR = gridCR,
                UseCropsArr = useCropsArr,
                CropOffsets = cropOffsets,
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
            var bounds = layoutXy
                ? new Bounds(Vector3.zero, new Vector3(4000f, 4000f, 4000f))
                : new Bounds(Vector3.zero, new Vector3(4000f, 200f, 4000f));
            for (int r = 0; r < recordCount; r++)
            {
                var rec = SpriteSheetRegistry.Records[r];
                rec.Count = counts[r];
                if (rec.Count == 0)
                    continue;
                rec.EnsureCapacity(rec.Count);
                rec.Buffer.SetData(SpriteRenderResources.Sorted, cursors[r], 0, rec.Count);
                rec.Material.SetFloat("_LayoutXy", layoutXy ? 1f : 0f);
                rec.Material.SetFloat("_CellAspect", rec.CellAspect > 0.01f ? rec.CellAspect : 1f);
                rec.Material.SetBuffer("_InstanceData", rec.Buffer);
                Graphics.DrawMeshInstancedProcedural(
                    SpriteRenderResources.Quad, 0, rec.Material, bounds, rec.Count,
                    null, UnityEngine.Rendering.ShadowCastingMode.Off, false, 0);
            }
            Active = true;

            gridCR.Dispose();
            useCropsArr.Dispose();
            cropOffsets.Dispose();
            allCrops.Dispose();
            counts.Dispose();
            cursors.Dispose();
        }

        [BurstCompile]
        partial struct PackJob : IJobEntity
        {
            public int LegacyRecordId;
            public byte LayoutXy;
            [ReadOnly] public ComponentLookup<SpriteSheetBinding> Bindings;
            [ReadOnly] public ComponentLookup<SpriteSheetRegistered> Registered;
            [ReadOnly] public NativeArray<int2> GridCR;
            [ReadOnly] public NativeArray<byte> UseCropsArr;
            [ReadOnly] public NativeArray<int> CropOffsets;
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
                    if (binding.Sheet != Entity.Null && Registered.HasComponent(binding.Sheet))
                        record = Registered[binding.Sheet].RegistryId;
                }
                RecordIds[i] = record;
                if (record < 0 || record >= GridCR.Length)
                    return; // unregistered sheet: skip this frame

                int slot = frame.Slot;
                int2 cr = GridCR[record];
                int cols = math.max(1, cr.x);
                int rows = math.max(1, cr.y);
                int col = slot % cols;
                int row = slot / cols;

                float2 offset = SpriteFlipUtility.LocalPosition(frame.Offset, flip);
                float frameRotation = SpriteFlipUtility.Angle(frame.Rotation, flip);

                float4 cropST;
                if (UseCropsArr[record] != 0 && slot >= 0)
                {
                    int cropIndex = CropOffsets[record] + slot;
                    if (cropIndex < AllCrops.Length)
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
                    posScale = new float4(worldPos.x + offset.x, worldPos.y + offset.y, 1f, worldPos.z);
                    transform2 = new float4(entityScaleX, entityScaleY, entityRot, 0f);
                }
                else
                {
                    // flat-lay: z is a ground-plane axis; keep uniform scale and
                    // skip entity rotation (XZ sprites face up, not the camera)
                    posScale = new float4(worldPos.x + offset.x, worldPos.z + offset.y,
                                          entityScaleX, worldPos.y);
                    transform2 = new float4(1f, 1f, 0f, 0f);
                }

                Staging[i] = new SpriteInstanceData
                {
                    PosScale = posScale,
                    CropST = cropST,
                    FrameTRS = new float4(frame.Scale.x, frame.Scale.y, math.radians(frameRotation), 0f),
                    Flip = new float4(flip.X, flip.Y, flip.ResolvedPivot.x, flip.ResolvedPivot.y),
                    Transform2 = transform2,
                    Color = tint.Value,
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
            var q = em.CreateEntityQuery(typeof(SpriteAnimGrid));
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
            Install(em);
            var q = em.CreateEntityQuery(typeof(SpriteAnimGrid));
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
