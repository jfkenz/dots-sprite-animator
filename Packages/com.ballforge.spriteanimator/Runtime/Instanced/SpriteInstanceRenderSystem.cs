using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace BallForge.Sprites.DOTS
{
    /// <summary>Per-sheet grid singleton (cells row-major, row 0 = top of sheet).</summary>
    public struct SpriteAnimGrid : IComponentData
    {
        public int Cols;
        public int Rows;
    }

    /// <summary>Per-entity tint. Required by the instanced path; white for untinted.</summary>
    public struct SpriteTint : IComponentData
    {
        public float4 Value;
    }

    /// <summary>Packed per-instance GPU data. Mirrors the shader struct exactly.</summary>
    public struct SpriteInstanceData
    {
        public float4 PosScale;  // xy = world xz, z = scale, w = world height y
        public float4 CropST;    // xy = cell size, zw = cell origin (uv bottom-left)
        public float4 FrameTRS;  // xy = frame scale, z = rotation radians, w = reserved
        public float4 Flip;      // x/y = uv flip flags
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
                em.AddComponentData(ge, new SpriteAnimGrid { Cols = 4, Rows = 4 });
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

            var job = new PackJob
            {
                Cols = grid.Cols,
                Rows = grid.Rows,
                Data = SpriteRenderResources.Staging,
            };
            state.Dependency = job.ScheduleParallel(q, state.Dependency);
            state.Dependency.Complete();

            var res = SpriteRenderResources.Buffer;
            res.SetData(SpriteRenderResources.Staging, 0, 0, count);
            SpriteRenderResources.Material.SetBuffer("_InstanceData", res);

            var bounds = new Bounds(Vector3.zero, new Vector3(4000f, 200f, 4000f));
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

                Data[i] = new SpriteInstanceData
                {
                    PosScale = new float4(lt.Position.x + frame.Offset.x,
                                          lt.Position.z + frame.Offset.y,
                                          lt.Scale, lt.Position.y),
                    CropST = new float4(1f / Cols, 1f / Rows,
                                        col * (1f / Cols),
                                        (Rows - 1 - row) * (1f / Rows)),
                    FrameTRS = new float4(frame.Scale.x, frame.Scale.y, math.radians(frame.Rotation), 0f),
                    Flip = new float4(flip.X, flip.Y, 0f, 0f),
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
            em.AddComponentData(e, new SpriteAnimGrid { Cols = 4, Rows = 4 });
        }

        /// <summary>Update the grid singleton (call when switching sheets).</summary>
        public static void SetGrid(EntityManager em, int cols, int rows)
        {
            Install(em);
            var q = em.CreateEntityQuery(typeof(SpriteAnimGrid));
            em.SetComponentData(q.GetSingletonEntity(), new SpriteAnimGrid { Cols = cols, Rows = rows });
        }
    }
}
