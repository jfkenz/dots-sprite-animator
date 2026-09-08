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
        public static bool Active;
        public static string LastError;
        public static int Ticks;

        int lastCount;
        bool uploadedOnce;

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

            bool dirty = SpriteGpuAnimResources.TakeDirty()
                         || count != lastCount || !uploadedOnce;
            if (count == 0) return;

            SpriteGpuAnimResources.EnsureCapacity(count);
            SpriteGpuAnimResources.EnsureObjects(sheet);

            byte layoutXy = SpriteBatchSpawner.LayoutXy ? (byte)1 : (byte)0;
            SpriteGpuAnimResources.Material.SetFloat("_LayoutXy", layoutXy);
            float cellAspect = grid.CellAspect > 0.01f ? grid.CellAspect : 1f;
            SpriteGpuAnimResources.Material.SetFloat("_CellAspect", cellAspect);

            if (dirty)
            {
                var job = new PackJob
                {
                    LayoutXy = layoutXy,
                    Data = SpriteGpuAnimResources.Staging,
                };
                state.Dependency = job.ScheduleParallel(q, state.Dependency);
                state.Dependency.Complete();
                SpriteGpuAnimResources.Buffer.SetData(
                    SpriteGpuAnimResources.Staging, 0, 0, count);
                uploadedOnce = true;
                lastCount = count;
            }

            var mat = SpriteGpuAnimResources.Material;
            mat.SetBuffer("_InstanceData", SpriteGpuAnimResources.Buffer);
            mat.SetFloat("_Now", Time.unscaledTime);
            mat.SetFloat("_UseSharedClip", SpriteGpuAnimResources.UseSharedClip ? 1f : 0f);
            mat.SetVector("_SharedCell", (Vector4)SpriteGpuAnimResources.SharedCell);
            mat.SetVector("_SharedAnim", (Vector4)SpriteGpuAnimResources.SharedAnim);

            var bounds = layoutXy != 0
                ? new Bounds(Vector3.zero, new Vector3(4000f, 4000f, 4000f))
                : new Bounds(Vector3.zero, new Vector3(4000f, 200f, 4000f));
            Graphics.DrawMeshInstancedProcedural(
                SpriteGpuAnimResources.Quad, 0, mat, bounds, count,
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
