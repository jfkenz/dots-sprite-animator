using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Draws all GPU-driven sprites in ONE DrawMeshInstancedProcedural call.
    /// Instance data is STATIC (position + clip recipe): re-uploaded only when
    /// something actually changed (count changed / conversion ran / forced),
    /// never per-frame. Frame selection happens IN THE SHADER from _Now.
    ///
    /// NOTE: moving units must mark SpriteGpuAnimResources.DataDirty (or call
    /// MarkDirty) so positions re-upload; stationary crowds cost zero CPU.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct SpriteGpuAnimRenderSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
            => state.EntityManager.World.GetOrCreateSystemManaged<SpriteRenderResourceLifetimeSystem>();

        public static bool Active;
        public static string LastError;
        public static int Ticks;

        int lastCount;
        bool uploadedOnce;
        uint uploadedVersion;
        byte uploadedLayout;
        float uploadedAspect;

        public void OnUpdate(ref SystemState state)
        {
            Ticks++;
            try { UpdateInner(ref state); LastError = null; }
            catch (System.Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
                throw;
            }
        }

        void UpdateInner(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton(out SpriteAnimGrid grid)) return;
            var sheet = SpriteRenderResources.Sheet;
            if (sheet == null) return;

            var q = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, SpriteGpuAnim, SpriteTint, SpriteFlip, SpriteGpuDriven>()
                .Build();
            int count = q.CalculateEntityCount();
            Active = false;

            byte layoutXy = SpriteBatchSpawner.LayoutXy ? (byte)1 : (byte)0;
            bool dirty = uploadedVersion != SpriteGpuAnimResources.DataVersion
                         || count != lastCount || !uploadedOnce || uploadedLayout != layoutXy;
            if (count == 0) return;

            SpriteGpuAnimResources.EnsureCapacity(count, false);
            SpriteGpuAnimResources.EnsureObjects(sheet);
            var batch = state.EntityManager.World.GetOrCreateSystemManaged<SpriteRenderResourceLifetimeSystem>();
            batch.EnsureGpuBatch(count, sheet);
            batch.GpuMaterial.SetFloat("_LayoutXy", layoutXy);
            float cellAspect = grid.CellAspect > 0.01f ? grid.CellAspect : 1f;
            batch.GpuMaterial.SetFloat("_CellAspect", cellAspect);

            dirty |= uploadedAspect != cellAspect;
            if (dirty)
            {
                var job = new PackJob
                {
                    LayoutXy = layoutXy,
                    Data = SpriteGpuAnimResources.Staging,
                };
                state.Dependency = job.ScheduleParallel(q, state.Dependency);
                state.Dependency.Complete();
                batch.GpuBuffer.SetData(
                    SpriteGpuAnimResources.Staging, 0, 0, count);
                uploadedOnce = true;
                lastCount = count;
                SpriteGpuAnimResources.LastUploadWorld = state.EntityManager.World.SequenceNumber;
                uploadedVersion = SpriteGpuAnimResources.DataVersion;
                uploadedLayout = layoutXy;
                uploadedAspect = cellAspect;
                Bounds bounds = default;
                for (int i = 0; i < count; i++)
                {
                    var data = SpriteGpuAnimResources.Staging[i];
                    var center = layoutXy != 0 ? new Vector3(data.PosScale.x, data.PosScale.y, data.PosScale.w)
                        : new Vector3(data.PosScale.x, data.PosScale.w, data.PosScale.y);
                    float radius = math.abs(data.PosScale.z) * (1 + math.length(data.Flip.zw - .5f) * 2) * math.max(1, cellAspect);
                    var item = new Bounds(center, Vector3.one * math.max(.01f, radius * 2));
                    if (i == 0) bounds = item; else bounds.Encapsulate(item);
                }
                batch.GpuBounds = bounds;
            }

            var mat = batch.GpuMaterial;
            mat.SetBuffer("_InstanceData", batch.GpuBuffer);
            mat.SetFloat("_Now", Time.unscaledTime);
            mat.SetFloat("_UseSharedClip", SpriteGpuAnimResources.UseSharedClip ? 1f : 0f);
            mat.SetVector("_SharedCell", (Vector4)SpriteGpuAnimResources.SharedCell);
            mat.SetVector("_SharedAnim", (Vector4)SpriteGpuAnimResources.SharedAnim);

            Graphics.DrawMeshInstancedProcedural(
                SpriteGpuAnimResources.Quad, 0, mat, batch.GpuBounds, count,
                null, UnityEngine.Rendering.ShadowCastingMode.Off, false, 0);
            Active = true;
        }

        [BurstCompile]
        partial struct PackJob : IJobEntity
        {
            public byte LayoutXy;
            [WriteOnly] public NativeArray<SpriteGpuInstanceData> Data;

            void Execute([EntityIndexInQuery] int i,
                         in LocalTransform lt,
                         in SpriteGpuAnim a,
                         in SpriteFlip flip,
                         in SpriteTint tint)
            {
                Data[i] = new SpriteGpuInstanceData
                {
                    PosScale = LayoutXy != 0
                        ? new float4(lt.Position.x, lt.Position.y, lt.Scale, lt.Position.z)
                        : new float4(lt.Position.x, lt.Position.z, lt.Scale, lt.Position.y),
                    Cell = new float4(a.CellW, a.CellH, a.SlotOriginX, a.SlotOriginY),
                    Anim = new float4(a.StartTime, a.Rate, a.N, a.WrapLoop),
                    Flip = new float4(math.select(0f, 1f, flip.X != 0), math.select(0f, 1f, flip.Y != 0), flip.ResolvedPivot.x, flip.ResolvedPivot.y),
                    Color = tint.Value,
                };
            }
        }
    }
}
