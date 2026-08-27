using Unity.Collections;
using Unity.Entities;
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

        static bool dataDirty;

        /// <summary>Force re-upload of instance data next frame (spawn/move/convert).</summary>
        public static void MarkDirty() => dataDirty = true;

        /// <summary>Consume the dirty flag (renderer-side).</summary>
        public static bool TakeDirty()
        {
            bool d = dataDirty;
            dataDirty = false;
            return d;
        }

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
            Capacity = 0;
            dataDirty = false;
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
            Material ??= new Material(Shader.Find(SpriteShaderLibrary.GpuAnimShader));
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
        public static bool ToGpu(EntityManager em, Entity e, float now)
        {
            if (!em.HasComponent<SpriteAnimPlayer>(e) ||
                !em.HasComponent<SpriteAnimSetRef>(e))
                return false;

            var p = em.GetComponentData<SpriteAnimPlayer>(e);
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            int ci = math.clamp(p.ClipIndex, 0, set.Clips.Length - 1);
            ref var def = ref set.Clips[ci];

            if (!SpriteGpuEligibility.IsGpuEligible(ref set, ci, out _))
                return false;

            int cols = 4, rows = 4;
            var gq = em.CreateEntityQuery(typeof(SpriteAnimGrid));
            if (!gq.IsEmpty)
            {
                var g = em.GetComponentData<SpriteAnimGrid>(gq.GetSingletonEntity());
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
            bool frozen = p.Playing == 0;
            int slot0 = (int)set.Frames[def.FirstFrame].x;

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
            if (!em.HasComponent<SpriteAnimPlayer>(e))
                em.AddComponentData(e, new SpriteAnimPlayer
                {
                    Time = gpu.SavedTime,
                    ClipIndex = gpu.SavedClipIndex,
                    Speed = gpu.SavedSpeed == 0f ? 1f : gpu.SavedSpeed,
                    Playing = gpu.SavedPlaying,
                });
            if (!em.HasComponent<SpriteAnimSetRef>(e))
                em.AddComponentData(e, new SpriteAnimSetRef { Set = gpu.SavedSet });

            em.RemoveComponent<SpriteGpuDriven>(e);
            em.RemoveComponent<SpriteGpuAnim>(e);
            return true;
        }

        /// <summary>Batch-convert every sprite entity. Returns converted count.</summary>
        public static int AllToGpu(EntityManager em, float now)
        {
            var q = em.CreateEntityQuery(
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
            var q = em.CreateEntityQuery(typeof(SpriteGpuDriven));
            var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                ToCpu(em, ents[i]);
            int n = ents.Length;
            ents.Dispose();
            return n;
        }
    }
}
