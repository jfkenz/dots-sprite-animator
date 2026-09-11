using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// GPU-driven animation tag: the SHADER picks the displayed frame from a
    /// global clock passed once per frame, so the CPU never ticks these
    /// entities. CPU playback state is PARKED inside SpriteGpuAnim (data is
    /// preserved, not deleted) and restored by ToCpu when needed.
    /// </summary>
    public struct SpriteGpuDriven : IComponentData { }

    /// <summary>Full state for converted entities; direct GPU crowds do not allocate this component.</summary>
    public struct SpriteGpuParkedPlayer : IComponentData
    {
        public SpriteAnimPlayer Player;
        public Entity Sheet;
    }

    public struct SpriteGpuAnim : IComponentData
    {
        public float StartTime;    // world time playback began
        public float Rate;         // frames per second (fps * speed); 0 = frozen
        public int   N;            // frames in the clip
        public byte  WrapLoop;     // 1 loop / 0 play-once-clamp
        public float SlotOriginX;  // uv origin of FIRST cell (bottom-left)
        public float SlotOriginY;
        public float CellW;        // atlas cell size in uv
        public float CellH;
        public byte FlipX;
        public byte FlipY;

        // ---- parked CPU playback state (restored by ToCpu) ----
        public float SavedTime;
        public int   SavedClipIndex;
        public float SavedSpeed;
        public byte  SavedPlaying;
        public BlobAssetReference<SpriteAnimSetBlob> SavedSet;
    }

    /// <summary>Packed per-instance data for the GPU-anim shader (64 bytes).</summary>
    public struct SpriteGpuInstanceData
    {
        public float4 PosScale;  // xy = world xz, z = scale, w = world height y
        public float4 Cell;      // xy = cell size uv, zw = first-cell origin uv
        public float4 Anim;      // x = start time, y = rate, z = frame count, w = wrap(1/0)
        public float4 Flip;      // x/y = uv flip flags
        public float4 Color;     // rgba tint
    }

    /// <summary>
    /// Managed GPU resources for the GPU-animated draw call. Separate buffer +
    /// material from the CPU-ticked path so both modes coexist in one scene.
    /// </summary>
    public static class SpriteGpuAnimResources
    {
        public const int Stride = 80; // 5 * float4

        public static ComputeBuffer Buffer;
        public static NativeArray<SpriteGpuInstanceData> Staging;
        public static Material Material;
        public static Mesh Quad;
        public static int Capacity;
        public static ulong LastUploadWorld;

        static bool dataDirty;

        // Crowd plays one clip: shader uniforms, no 1M entity rewrite on switch.
        public static bool UseSharedClip;
        public static float4 SharedCell;
        public static float4 SharedAnim;
        public static SpriteGpuAnim SharedClip;

        /// <summary>Force re-upload of instance data next frame (spawn/move/convert).</summary>
        public static void MarkDirty() => dataDirty = true;

        public static void SetSharedClip(in SpriteGpuAnim anim)
        {
            if (!CanUseSharedClip(anim.SavedSet))
                throw new System.InvalidOperationException("Shared GPU clips require one crowd animation set and no individually converted GPU sprites.");
            UseSharedClip = true;
            SharedClip = anim;
            SharedCell = new float4(anim.CellW, anim.CellH, anim.SlotOriginX, anim.SlotOriginY);
            SharedAnim = new float4(anim.StartTime, anim.Rate, anim.N, anim.WrapLoop);
        }

        /// <summary>Consume the dirty flag (renderer-side).</summary>
        public static bool TakeDirty()
        {
            bool d = dataDirty;
            dataDirty = false;
            return d;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            Buffer?.Dispose();
            Buffer = null;
            if (Staging.IsCreated) Staging.Dispose();
            Staging = default;
            SpriteRenderResourceLifetimeSystem.DestroyOwnedObject(Material);
            SpriteRenderResourceLifetimeSystem.DestroyOwnedObject(Quad);
            Material = null;
            Quad = null;
            Capacity = 0;
            LastUploadWorld = 0;
            dataDirty = false;
            UseSharedClip = false;
            SharedCell = 0f;
            SharedAnim = 0f;
            SharedClip = default;
        }

        public static bool HasGpuSprites(bool excludeCrowds = false)
        {
            foreach (var world in World.All)
            {
                if (!world.IsCreated) continue;
                using var query = world.EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<SpriteGpuDriven>() },
                    None = excludeCrowds ? new[] { ComponentType.ReadOnly<SpriteCrowdEntityTag>() } : new ComponentType[0],
                    Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab,
                });
                if (!query.IsEmptyIgnoreFilter) return true;
            }
            return false;
        }

        public static bool CanUseSharedClip(BlobAssetReference<SpriteAnimSetBlob> set)
            => !HasGpuSprites(true) && (!UseSharedClip || SharedClip.SavedSet == set || !HasGpuSprites());

        public static bool CanUseSheet(Texture2D sheet, int cols, int rows, float aspect)
        {
            foreach (var world in World.All)
            {
                if (!world.IsCreated) continue;
                using var sprites = world.EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<SpriteGpuDriven>() },
                    Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab,
                });
                if (sprites.IsEmptyIgnoreFilter) continue;
                if (sheet != SpriteRenderResources.Sheet) return false;
                using var grids = world.EntityManager.CreateEntityQuery(typeof(SpriteAnimGrid));
                if (grids.CalculateEntityCount() != 1) return false;
                var grid = grids.GetSingleton<SpriteAnimGrid>();
                if (grid.Cols != cols || grid.Rows != rows || grid.UseCellCrops != 0 ||
                    math.abs((grid.CellAspect > 0.01f ? grid.CellAspect : 1f) - aspect) > 0.0001f) return false;
            }
            return true;
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
            Staging = new NativeArray<SpriteGpuInstanceData>(cap, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        public static void EnsureObjects(Texture2D sheet)
        {
            Material ??= new Material(Shader.Find(SpriteShaderLibrary.ActiveGpuAnimShader));
            Material.mainTexture = sheet;
            // same as CPU path: high cutoff makes distant/tiny sprites vanish
            Material.SetFloat("_Cutoff", 0.02f);
            if (Quad == null)
            {
                Quad = new Mesh { name = "GpuAnimSpriteQuad" };
                Quad.vertices = new Vector3[6];
                Quad.uv = new Vector2[6];
                Quad.SetIndices(new[] { 0, 1, 2, 3, 4, 5 }, MeshTopology.Triangles, 0);
                Quad.RecalculateBounds();
            }
        }
    }

    /// <summary>
    /// Mode switching. Conversion preserves the visual phase: StartTime is
    /// derived so the GPU clock shows exactly the frame the CPU clock showed
    /// at the switch instant. Ping-pong/reverse clips degrade to clamp/loop.
    /// </summary>
    public static class SpriteGpuAnimSwitch
    {
        /// <summary>
        /// Convert one entity to GPU-driven animation: CPU playback state is
        /// PARKED inside SpriteGpuAnim and the heavy components are REMOVED
        /// so simulation work no longer advances that entity on CPU.
        /// </summary>
        static bool TryGetAnimGrid(EntityManager em, out SpriteAnimGrid grid)
        {
            grid = default;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<SpriteAnimGrid>());
            if (q.CalculateEntityCount() == 0)
                return false;
            grid = q.GetSingleton<SpriteAnimGrid>();
            return true;
        }

        public static bool ToGpu(EntityManager em, Entity e, float now)
        {
            if (!math.isfinite(now) || !SpriteGpuEligibility.IsGpuEligible(em, e, out _))
                return false;

            if (SpriteGpuAnimResources.UseSharedClip)
            {
                if (SpriteGpuAnimResources.HasGpuSprites()) return false;
                SpriteGpuAnimResources.UseSharedClip = false;
            }

            var p = em.GetComponentData<SpriteAnimPlayer>(e);
            var setRef = em.GetComponentData<SpriteAnimSetRef>(e).Set;
            if (!setRef.IsCreated || setRef.Value.Clips.Length == 0)
                return false;
            ref var set = ref setRef.Value;
            int ci = math.clamp(p.ClipIndex, 0, set.Clips.Length - 1);
            ref var def = ref set.Clips[ci];
            if (!math.isfinite(def.FrameRate * p.Speed)) return false;

            if (!SpriteGpuEligibility.IsGpuEligible(ref set, ci, out _))
                return false;

            var savedSheet = em.HasComponent<SpriteSheetBinding>(e)
                ? em.GetComponentData<SpriteSheetBinding>(e).Sheet : Entity.Null;
            // Validate before promotion: failed conversion must not change the CPU sheet.
            if (!TryPromoteBoundSheetToLegacy(em, e))
                return false;

            int cols = 4, rows = 4;
            if (TryGetAnimGrid(em, out var g))
            {
                // Compact GPU clock assumes uniform CellW/CellH stride.
                if (g.UseCellCrops != 0 || g.Cols <= 0 || g.Rows <= 0)
                    return false;
                cols = g.Cols; rows = g.Rows;
            }

            byte flipX = 0;
            byte flipY = 0;
            if (em.HasComponent<SpriteFlip>(e))
            {
                var flip = em.GetComponentData<SpriteFlip>(e);
                flipX = flip.X;
                flipY = flip.Y;
            }

            bool loop = def.WrapMode == SpriteAnimWrap.Loop;
            float rate = math.max(0.0001f, def.FrameRate * math.max(0.01f, p.Speed));
            bool frozen = p.Playing == 0 || math.abs(p.Speed) <= 1e-6f;
            int slot0 = (int)set.Frames[def.FirstFrame].x;
            // When frozen, Rate==0 forces shader frame 0 — pack cell origin on the
            // paused atlas cell so the held frame is preserved across CPU→GPU.
            if (frozen && def.FrameCount > 0)
            {
                int pausedFrame = (int)math.floor(math.max(0f, p.Time));
                if (loop)
                {
                    pausedFrame %= def.FrameCount;
                    if (pausedFrame < 0) pausedFrame += def.FrameCount;
                }
                else
                    pausedFrame = math.clamp(pausedFrame, 0, def.FrameCount - 1);

                if (em.HasComponent<SpriteAnimFrame>(e))
                {
                    int curSlot = em.GetComponentData<SpriteAnimFrame>(e).Slot;
                    for (int fi = 0; fi < def.FrameCount; fi++)
                    {
                        if ((int)set.Frames[def.FirstFrame + fi].x == curSlot)
                        {
                            pausedFrame = fi;
                            break;
                        }
                    }
                }

                slot0 = (int)set.Frames[def.FirstFrame + pausedFrame].x;
            }

            if (!em.HasComponent<SpriteGpuDriven>(e))
                em.AddComponentData(e, new SpriteGpuDriven());
            if (!em.HasComponent<SpriteGpuAnim>(e))
                em.AddComponentData(e, new SpriteGpuAnim());

            em.SetComponentData(e, new SpriteGpuAnim
            {
                StartTime      = frozen ? float.MaxValue : now - p.Time / rate,
                Rate           = frozen ? 0f : rate,
                N              = def.FrameCount,
                WrapLoop       = (byte)(loop ? 1 : 0),
                SlotOriginX    = (slot0 % cols) / (float)cols,
                SlotOriginY    = (rows - 1 - slot0 / cols) / (float)rows,
                CellW          = 1f / cols,
                CellH          = 1f / rows,
                FlipX          = flipX,
                FlipY          = flipY,

                SavedTime      = p.Time,
                SavedClipIndex = p.ClipIndex,
                SavedSpeed     = p.Speed,
                SavedPlaying   = p.Playing,
                SavedSet       = em.GetComponentData<SpriteAnimSetRef>(e).Set,
            });

            em.RemoveComponent<SpriteAnimPlayer>(e);
            em.RemoveComponent<SpriteAnimSetRef>(e);
            em.AddComponentData(e, new SpriteGpuParkedPlayer { Player = p, Sheet = savedSheet });
            SpriteGpuAnimResources.MarkDirty();
            return true;
        }

        /// <summary>Switch one entity back to CPU animation (state restored).</summary>
        public static bool ToCpu(EntityManager em, Entity e)
        {
            if (!em.HasComponent<SpriteGpuDriven>(e) ||
                !em.HasComponent<SpriteGpuAnim>(e))
                return false;

            var gpu = em.GetComponentData<SpriteGpuAnim>(e);
            if (!gpu.SavedSet.IsCreated) return false;
            bool parked = em.HasComponent<SpriteGpuParkedPlayer>(e);
            var saved = parked ? em.GetComponentData<SpriteGpuParkedPlayer>(e) : default;
            if (!em.HasComponent<SpriteAnimPlayer>(e))
                em.AddComponentData(e, parked ? saved.Player : new SpriteAnimPlayer
                {
                    Time = gpu.SavedTime,
                    ClipIndex = gpu.SavedClipIndex,
                    Speed = gpu.SavedSpeed,
                    Playing = gpu.SavedPlaying,
                    QueuedClipIndex = -1,
                    ResumeClipIndex = -1,
                    LastEventStep = int.MinValue,
                    OnceEventClip = -1,
                });
            if (!em.HasComponent<SpriteAnimSetRef>(e))
                em.AddComponentData(e, new SpriteAnimSetRef { Set = gpu.SavedSet });

            em.RemoveComponent<SpriteGpuDriven>(e);
            em.RemoveComponent<SpriteGpuAnim>(e);
            if (parked)
            {
                if (em.HasComponent<SpriteSheetBinding>(e))
                    em.SetComponentData(e, new SpriteSheetBinding { Sheet = saved.Sheet });
                em.RemoveComponent<SpriteGpuParkedPlayer>(e);
            }
            SpriteGpuAnimResources.MarkDirty();
            return true;
        }

        /// <summary>Return to CPU at the currently displayed GPU phase, for gameplay controls.</summary>
        public static bool ToCpuAtTime(EntityManager em, Entity e, float now)
        {
            if (!math.isfinite(now) || !em.HasComponent<SpriteGpuAnim>(e)) return false;
            var gpu = em.GetComponentData<SpriteGpuAnim>(e);
            if (SpriteGpuAnimResources.UseSharedClip && em.HasComponent<SpriteCrowdEntityTag>(e))
                gpu = SpriteGpuAnimResources.SharedClip;
            if (!gpu.SavedSet.IsCreated || !ToCpu(em, e)) return false;
            var player = em.GetComponentData<SpriteAnimPlayer>(e);
            player.ClipIndex = gpu.SavedClipIndex;
            float phase = gpu.Rate == 0 ? gpu.SavedTime : math.max(0, (now - gpu.StartTime) * gpu.Rate);
            player.Time = gpu.WrapLoop != 0 ? phase % math.max(1, gpu.N) : math.min(phase, math.max(0, gpu.N - 1) + 0.999f);
            player.Speed = gpu.SavedSpeed;
            player.Playing = gpu.SavedPlaying;
            em.SetComponentData(e, player);
            SpriteAnims.SetTime(em, e, player.Time);
            return true;
        }

        /// <summary>Batch-convert every sprite entity. Returns converted count.</summary>
        public static int AllToGpu(EntityManager em, float now)
        {
            using var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<SpriteAnimPlayer>(),
                ComponentType.ReadOnly<SpriteAnimSetRef>(),
                ComponentType.Exclude<SpriteGpuDriven>());
            var ents = q.ToEntityArray(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < ents.Length; i++)
                if (ToGpu(em, ents[i], now)) n++;
            ents.Dispose();
            return n;
        }

        /// <summary>Batch-convert back to CPU animation. Returns switched count.</summary>
        public static int AllToCpu(EntityManager em)
        {
            using var q = em.CreateEntityQuery(typeof(SpriteGpuDriven));
            var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                ToCpu(em, ents[i]);
            int n = ents.Length;
            ents.Dispose();
            return n;
        }


        /// <summary>
        /// GPU draw uses SpriteRenderResources.Sheet. Clear a bound sheet onto
        /// that legacy path when the sheet is uniform (no cell crops).
        /// </summary>
        public static bool TryPromoteBoundSheetToLegacy(EntityManager em, Entity e)
        {
            if (!em.HasComponent<SpriteSheetBinding>(e))
                return true;
            var binding = em.GetComponentData<SpriteSheetBinding>(e);
            if (binding.Sheet == Entity.Null)
                return true;

            Entity sheet = binding.Sheet;
            if (!em.Exists(sheet) || !em.HasComponent<SpriteSheetDefinition>(sheet))
                return false;

            var def = em.GetComponentData<SpriteSheetDefinition>(sheet);
            if (def.UseCellCrops != 0)
                return false;

            if (!em.HasComponent<SpriteSheetAsset>(sheet))
                return false;
            var asset = em.GetComponentObject<SpriteSheetAsset>(sheet);
            if (asset == null || asset.Texture == null)
                return false;
            if (def.Cols <= 0 || def.Rows <= 0 || !SpriteGpuAnimResources.CanUseSheet(
                    asset.Texture, def.Cols, def.Rows, def.CellAspect > 0.01f ? def.CellAspect : 1f))
                return false;

            if (em.HasBuffer<SpriteClipSheetBindingEntry>(e))
            {
                var buf = em.GetBuffer<SpriteClipSheetBindingEntry>(e);
                for (int i = 0; i < buf.Length; i++)
                {
                    if (buf[i].Sheet != Entity.Null && buf[i].Sheet != sheet)
                        return false;
                }
            }

            SpriteInstanceRenderSystem.Install(em);
            SpriteInstanceRenderSystem.SetSheet(asset.Texture);
            SpriteInstanceRenderSystem.SetGrid(em, def.Cols, def.Rows,
                def.CellAspect > 0.01f ? def.CellAspect : 1f, null);
            em.SetComponentData(e, new SpriteSheetBinding { Sheet = Entity.Null });
            return true;
        }

        /// <summary>Build GPU clock state for a clip. False if the clip needs CPU.</summary>
        public static bool TryFromClip(ref SpriteAnimSetBlob set, int clipIndex, float now, float speed,
                                       int cols, int rows,
                                       Unity.Entities.BlobAssetReference<SpriteAnimSetBlob> savedSet,
                                       out SpriteGpuAnim gpu)
        {
            gpu = default;
            if (!math.isfinite(now) || !math.isfinite(speed) || speed < 0 ||
                !SpriteGpuEligibility.IsGpuEligible(ref set, clipIndex, out _))
                return false;

            ref var def = ref set.Clips[clipIndex];
            int slot0 = (int)set.Frames[def.FirstFrame].x;
            cols = math.max(1, cols);
            rows = math.max(1, rows);
            float rate = def.FrameRate * speed;
            gpu = new SpriteGpuAnim
            {
                StartTime      = now,
                Rate           = rate,
                N              = def.FrameCount,
                WrapLoop       = (byte)(def.WrapMode == SpriteAnimWrap.Loop ? 1 : 0),
                SlotOriginX    = (slot0 % cols) / (float)cols,
                SlotOriginY    = (rows - 1 - slot0 / cols) / (float)rows,
                CellW          = 1f / cols,
                CellH          = 1f / rows,
                SavedTime      = 0f,
                SavedClipIndex = clipIndex,
                SavedSpeed     = speed,
                SavedPlaying   = 1,
                SavedSet       = savedSet,
            };
            return true;
        }

        /// <summary>Burst-switch every crowd GPU sprite to clipIndex.</summary>
        public static void SetAllCrowdClips(World world, int clipIndex, float now)
        {
            if (world == null || !world.IsCreated)
                return;
            int cols = 4, rows = 4;
            var em = world.EntityManager;
            if (TryGetAnimGrid(em, out var g))
            {
                cols = g.Cols;
                rows = g.Rows;
            }

            var driver = world.GetExistingSystemManaged<SpriteCrowdGpuClipDriver>()
                         ?? world.GetOrCreateSystemManaged<SpriteCrowdGpuClipDriver>();
            driver.Apply(clipIndex, now, cols, rows);
        }
    }

    /// <summary>Burst-writes SpriteGpuAnim clip fields on every crowd entity.</summary>
    [BurstCompile]
    public partial struct SpriteCrowdSetGpuClipJob : IJobEntity
    {
        public int ClipIndex;
        public float Now;
        public int Cols;
        public int Rows;

        void Execute(ref SpriteGpuAnim gpu, in SpriteAnimSetRef setRef, in SpriteCrowdEntityTag tag)
        {
            if (!setRef.Set.IsCreated) return;
            ref var set = ref setRef.Set.Value;
            if (!SpriteGpuEligibility.IsGpuEligible(ref set, ClipIndex, out _))
                return;
            ref var def = ref set.Clips[ClipIndex];
            int slot0 = (int)set.Frames[def.FirstFrame].x;
            int cols = math.max(1, Cols);
            int rows = math.max(1, Rows);
            float speed = math.max(0f, gpu.SavedSpeed);
            gpu.StartTime = Now;
            gpu.Rate = def.FrameRate * speed;
            gpu.N = def.FrameCount;
            gpu.WrapLoop = (byte)(def.WrapMode == SpriteAnimWrap.Loop ? 1 : 0);
            gpu.SlotOriginX = (slot0 % cols) / (float)cols;
            gpu.SlotOriginY = (rows - 1 - slot0 / cols) / (float)rows;
            gpu.CellW = 1f / cols;
            gpu.CellH = 1f / rows;
            gpu.SavedClipIndex = ClipIndex;
            gpu.SavedTime = 0f;
        }
    }

    /// <summary>Runs <see cref="SpriteCrowdSetGpuClipJob"/> on demand from authoring.</summary>
    public partial class SpriteCrowdGpuClipDriver : SystemBase
    {
        protected override void OnUpdate() { }

        public void Apply(int clipIndex, float now, int cols, int rows)
        {
            if (SpriteGpuAnimResources.UseSharedClip)
            {
                var current = SpriteGpuAnimResources.SharedClip;
                var set = current.SavedSet;
                if (set.IsCreated && SpriteGpuAnimSwitch.TryFromClip(ref set.Value, clipIndex, now,
                        current.SavedSpeed, cols, rows, set, out var next))
                    SpriteGpuAnimResources.SetSharedClip(next);
                return;
            }
            var job = new SpriteCrowdSetGpuClipJob
            {
                ClipIndex = clipIndex,
                Now = now,
                Cols = cols,
                Rows = rows,
            };
            Dependency = job.ScheduleParallel(Dependency);
            CompleteDependency();
            SpriteGpuAnimResources.MarkDirty();
        }
    }
}
