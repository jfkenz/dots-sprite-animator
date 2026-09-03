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
        public float4 PosScale;  // XZ: xy=world xz, z=scale, w=height y; XY: xy=world xy, z=scale, w=depth z
        public float4 CropST;    // xy = cell size, zw = cell origin (uv bottom-left)
        public float4 FrameTRS;  // xy = frame scale, z = rotation radians, w = reserved
        public float4 Flip;      // xy = flip flags, zw = normalized pivot
        public float4 Color;     // rgba tint
    }

    /// <summary>
    /// Managed GPU resources live OUTSIDE the ISystem struct — a system struct
    /// holding managed fields would become a managed type and never instantiate.
    /// </summary>
    public static class SpriteRenderResources
    {
        public const int Stride = 80; // 5 * float4

        public static ComputeBuffer Buffer;
        public static NativeArray<SpriteInstanceData> Staging;
        public static Material Material;
        public static Mesh Quad;
        public static Texture2D Sheet;
        public static int Capacity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Buffer?.Dispose();
            Buffer = null;
            if (Staging.IsCreated) Staging.Dispose();
            if (Material != null) Object.Destroy(Material);
            if (Quad != null) Object.Destroy(Quad);
            Material = null;
            Quad = null;
            Sheet = null;
            Capacity = 0;
        }

        public static void EnsureCapacity(int need)
        {
            if (Buffer != null && need <= Capacity && Staging.IsCreated && Staging.Length >= need)
                return;
            int cap = math.max(4096, Capacity);
            while (cap < need) cap *= 2;
            Buffer?.Dispose();
            Buffer = new ComputeBuffer(cap, Stride);
            Capacity = cap;
            if (Staging.IsCreated) Staging.Dispose();
            Staging = new NativeArray<SpriteInstanceData>(cap, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        public static void EnsureObjects(Texture2D sheet)
        {
            Sheet = sheet;
            Material ??= new Material(Shader.Find(SpriteShaderLibrary.InstancedShader));
            Material.mainTexture = sheet;
            // tiny on-screen sprites average mostly-transparent texels; a 0.5
            // cutout erases whole crowds at wide zoom. Keep it permissive.
            Material.SetFloat("_Cutoff", 0.02f);
            if (Quad == null)
            {
                // geometry comes from SV_VertexID in the shader; the mesh only
                // needs a valid 6-vertex triangle list for the procedural draw.
                Quad = new Mesh { name = "InstancedSpriteQuad" };
                Quad.vertices = new Vector3[6];
                Quad.uv = new Vector2[6];
                Quad.SetIndices(new[] { 0, 1, 2, 3, 4, 5 }, MeshTopology.Triangles, 0);
                Quad.RecalculateBounds();
            }
        }
    }

    /// <summary>
    /// NSprites-style renderer: ONE ComputeBuffer, ONE DrawMeshInstancedProcedural
    /// per frame — all animation states, any crowd size, one draw call.
    /// Fieldless unmanaged ISystem (managed resources live in SpriteRenderResources);
    /// packing runs in Burst via PackJob; the draw tail is managed code.
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

        /// <summary>Hand the renderer the sheet texture (call once after Install).</summary>
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

            if (SpriteRenderResources.Sheet == null) return;
            SpriteRenderResources.EnsureObjects(SpriteRenderResources.Sheet);

            var q = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, SpriteAnimFrame, SpriteTint, SpriteFlip, SpriteAnimEnabled>()
                .WithNone<SpriteGpuDriven>() // GPU-driven sprites draw via SpriteGpuAnimRenderSystem
                .Build();
            int count = q.CalculateEntityCount(); // enable-bit aware: culled sprites excluded
            Active = false;
            if (count == 0) return;

            SpriteRenderResources.EnsureCapacity(count);

            byte layoutXy = SpriteBatchSpawner.LayoutXy ? (byte)1 : (byte)0;
            SpriteRenderResources.Material.SetFloat("_LayoutXy", layoutXy);
            float cellAspect = grid.CellAspect > 0.01f ? grid.CellAspect : 1f;
            SpriteRenderResources.Material.SetFloat("_CellAspect", cellAspect);
            var gridEntity = SystemAPI.GetSingletonEntity<SpriteAnimGrid>();
            NativeArray<float4> cellCrops = default;
            byte useCrops = 0;
            bool ownsCrops = false;
            if (grid.UseCellCrops != 0 && em.HasBuffer<SpriteAnimCellCrop>(gridEntity))
            {
                var buf = em.GetBuffer<SpriteAnimCellCrop>(gridEntity, true);
                if (buf.Length > 0)
                {
                    cellCrops = new NativeArray<float4>(buf.Length, Allocator.TempJob);
                    ownsCrops = true;
                    for (int c = 0; c < buf.Length; c++)
                        cellCrops[c] = buf[c].Value;
                    useCrops = 1;
                }
            }
            if (!ownsCrops)
                cellCrops = new NativeArray<float4>(0, Allocator.TempJob);

            var job = new PackJob
            {
                Cols = grid.Cols,
                Rows = grid.Rows,
                LayoutXy = layoutXy,
                UseCellCrops = useCrops,
                CellCrops = cellCrops,
                Data = SpriteRenderResources.Staging,
            };
            state.Dependency = job.ScheduleParallel(q, state.Dependency);
            state.Dependency.Complete();
            if (cellCrops.IsCreated)
                cellCrops.Dispose();

            var res = SpriteRenderResources.Buffer;
            res.SetData(SpriteRenderResources.Staging, 0, 0, count);
            SpriteRenderResources.Material.SetBuffer("_InstanceData", res);

            var bounds = layoutXy != 0
                ? new Bounds(Vector3.zero, new Vector3(4000f, 4000f, 200f))
                : new Bounds(Vector3.zero, new Vector3(4000f, 200f, 4000f));
            Graphics.DrawMeshInstancedProcedural(
                SpriteRenderResources.Quad, 0, SpriteRenderResources.Material, bounds, count,
                null, UnityEngine.Rendering.ShadowCastingMode.Off, false, 0);

            Active = true;
        }

        [BurstCompile]
        partial struct PackJob : IJobEntity
        {
            public int Cols;
            public int Rows;
            public byte LayoutXy;
            public byte UseCellCrops;
            [ReadOnly] public NativeArray<float4> CellCrops;
            [WriteOnly] public NativeArray<SpriteInstanceData> Data;

            void Execute([EntityIndexInQuery] int i,
                         in LocalTransform lt,
                         in SpriteAnimFrame frame,
                         in SpriteFlip flip,
                         in SpriteTint tint)
            {
                int slot = frame.Slot;
                int col = slot % Cols;
                int row = slot / Cols;
                float2 offset = SpriteFlipUtility.LocalPosition(frame.Offset, flip);
                float rotation = SpriteFlipUtility.Angle(frame.Rotation, flip);

                float4 cropST;
                if (UseCellCrops != 0 && slot >= 0 && slot < CellCrops.Length)
                    cropST = CellCrops[slot];
                else
                    cropST = new float4(1f / Cols, 1f / Rows,
                                        col * (1f / Cols),
                                        (Rows - 1 - row) * (1f / Rows));

                Data[i] = new SpriteInstanceData
                {
                    PosScale = LayoutXy != 0
                        ? new float4(lt.Position.x + offset.x,
                                     lt.Position.y + offset.y,
                                     lt.Scale, lt.Position.z)
                        : new float4(lt.Position.x + offset.x,
                                     lt.Position.z + offset.y,
                                     lt.Scale, lt.Position.y),
                    CropST = cropST,
                    FrameTRS = new float4(frame.Scale.x, frame.Scale.y, math.radians(rotation), 0f),
                    Flip = new float4(flip.X, flip.Y, flip.ResolvedPivot.x, flip.ResolvedPivot.y),
                    Color = tint.Value,
                };
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
