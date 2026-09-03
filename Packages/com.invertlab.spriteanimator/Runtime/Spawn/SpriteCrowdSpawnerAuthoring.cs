using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Tag on every crowd instance cloned from the authoring prototype.</summary>
    public struct SpriteCrowdEntityTag : IComponentData { }

    /// <summary>
    /// GameObject spawner. Point it at a SpriteAnimSetAuthoring Quad, then spawn
    /// a GPU-instanced crowd from that sheet/clips. Add Component menu:
    /// DOTS Sprite Animator / Crowd Spawner.
    /// </summary>
    [AddComponentMenu("DOTS Sprite Animator/Crowd Spawner")]
    [DisallowMultipleComponent]
    public class SpriteCrowdSpawnerAuthoring : MonoBehaviour
    {
        [Tooltip("Quad with Sprite Anim Set Authoring (profile, sheet, clips).")]
        public SpriteAnimSetAuthoring Source;

        [Tooltip("How many sprites to spawn when Play starts. 0 = none.")]
        [Min(0)] public int SpawnOnStartCount = 400;

        [Tooltip("Count used by Spawn Batch / number keys in the sample.")]
        [Min(1)] public int BatchSize = 400;

        [Tooltip("Square grid when true, random scatter when false.")]
        public bool Grid = true;

        [Min(0.01f)] public float SizeUnits = 2f;
        [Min(0.01f)] public float Spread = 28f;
        public float HeightY = 1.55f;

        [Tooltip("Play-mode keys 1-9, 0, [ and ] switch spawned sprites (limited by clip count).")]
        public bool NumberKeysSwitchClips = true;

        [Tooltip("Shader-driven animation + Burst spawn/place. Uncheck only if you need CPU playback (events, sockets, ping-pong).")]
        public bool UseGpuAnim = true;

        static Entity s_proto;
        static bool s_ready;

        public static bool Ready => s_ready;
        public int LiveCount => CountAll();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_ready = false;
            s_proto = Entity.Null;
            s_appliedScale = -1f;
            s_hasAppliedScale = false;
        }

        void Start()
        {
            if (!Application.isPlaying)
                return;
            EnsureProto();
            if (SpawnOnStartCount > 0)
            {
                Spawn(SpawnOnStartCount, Grid);
                FitMainCamera(SpawnOnStartCount);
            }
        }

        public bool EnsureProto()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (s_ready || world == null || !world.IsCreated)
                return s_ready;

            var authoring = Source != null
                ? Source
                : Object.FindFirstObjectByType<SpriteAnimSetAuthoring>();
            if (authoring == null)
            {
                Debug.LogError("[Crowd Spawner] assign Source (Sprite Anim Set Authoring)", this);
                return false;
            }

            if (authoring.Profile != null)
                authoring.ApplyFromProfile();

            var sheet = authoring.Sheet;
            if (sheet == null)
            {
                Debug.LogError("[Crowd Spawner] Source has no Sheet. Assign a Profile.", authoring);
                return false;
            }

            var srcClips = authoring.Clips;
            if (srcClips == null || srcClips.Length == 0)
            {
                Debug.LogError("[Crowd Spawner] Source has no Clips.", authoring);
                return false;
            }

            authoring.TryGetClipSheet(0, out _, out int cols, out int rows, out _);
            cols = Mathf.Max(1, cols);
            rows = Mathf.Max(1, rows);
            var clips = new SpriteAnimSetBuilder.ClipInput[srcClips.Length];
            for (int i = 0; i < srcClips.Length; i++)
            {
                var src = srcClips[i];
                authoring.TryGetClipSheet(i, out _, out int clipCols, out int clipRows, out _);
                clipCols = Mathf.Max(1, clipCols);
                clipRows = Mathf.Max(1, clipRows);
                var frameCols = src.Frames != null && src.Frames.Length > 0
                    ? src.Frames
                    : new[] { 0 };
                var slots = new int[frameCols.Length];
                for (int f = 0; f < frameCols.Length; f++)
                    slots[f] = Mathf.Clamp(src.Row, 0, clipRows - 1) * clipCols
                               + Mathf.Clamp(frameCols[f], 0, clipCols - 1);

                byte wrap = src.WrapMode;
                if (UseGpuAnim && wrap != SpriteAnimWrap.Loop && wrap != SpriteAnimWrap.Once)
                    wrap = SpriteAnimWrap.Loop;
                clips[i] = new SpriteAnimSetBuilder.ClipInput
                {
                    Name = string.IsNullOrEmpty(src.Name) ? ("clip" + i) : src.Name,
                    Loop = wrap == SpriteAnimWrap.Loop || wrap == SpriteAnimWrap.ReverseLoop,
                    WrapMode = wrap,
                    FrameRate = Mathf.Max(0.1f, src.FrameRate),
                    GlobalFrameIndices = slots,
                };
            }

            var (setRef, player) = SpriteAnimSetBuilder.Build(Allocator.Persistent, clips);
            var em = world.EntityManager;
            s_proto = em.CreateEntity();
            // 2D scene: pack/draw on XY so the ortho camera at (0,0,-10) sees the grid.
            SpriteBatchSpawner.LayoutXy = true;
            float protoScale = SizeUnits * ClipWorldHeight(authoring, 0);
            em.AddComponentData(s_proto, LocalTransform.FromPositionRotationScale(
                float3.zero,
                quaternion.identity,
                protoScale));
            em.AddComponentData(s_proto, setRef);
            em.AddComponentData(s_proto, new SpriteAnimFrame { Slot = 0, Scale = new float2(1f, 1f) });
            em.AddComponentData(s_proto, new SpriteTint { Value = new float4(1, 1, 1, 1) });
            em.AddComponentData(s_proto, new SpriteFlip());
            em.AddComponent<SpriteCrowdEntityTag>(s_proto);

            bool gpu = UseGpuAnim;
            // Compact GPU clock needs uniform CellW; Cropped per-cell UVs use the CPU instance path.
            if (gpu)
            {
                var data = authoring.Profile != null ? authoring.Profile.Data : null;
                var sheetDef = data != null ? data.SheetAt(0) : null;
                if (sheetDef != null &&
                    sheetDef.CellLayoutMode == SpriteSheetCellLayoutMode.Cropped &&
                    SpriteSheetProfile.HasCroppedCellData(sheetDef))
                {
                    gpu = false;
                    Debug.LogWarning(
                        "[Crowd Spawner] Cropped cell layout needs CPU instance CropST. Spawning CPU path.",
                        this);
                }
            }
            if (gpu)
            {
                ref var blob = ref setRef.Set.Value;
                gpu = SpriteGpuAnimSwitch.TryFromClip(
                    ref blob, 0, Time.unscaledTime, 1f, cols, rows, setRef.Set, out var gpuAnim);
                if (gpu)
                {
                    em.AddComponentData(s_proto, gpuAnim);
                    em.AddComponentData(s_proto, new SpriteGpuDriven());
                    SpriteGpuAnimResources.SetSharedClip(gpuAnim);
                    Debug.Log("[Crowd Spawner] GPU + Burst (shader clock). Uncheck Use Gpu Anim for CPU playback.", this);
                }
                else
                {
                    Debug.LogWarning(
                        "[Crowd Spawner] clips need CPU playback (sockets/events/reorder). Spawning CPU path.",
                        this);
                }
            }

            if (!gpu)
            {
                em.AddComponentData(s_proto, player);
                em.AddComponentData(s_proto, new SpriteAnimEnabled());
                em.SetComponentEnabled<SpriteAnimEnabled>(s_proto, true);
                em.AddBuffer<SpriteAnimEventBuffer>(s_proto);
                em.AddComponent<SpriteAnimEventsPending>(s_proto);
                em.SetComponentEnabled<SpriteAnimEventsPending>(s_proto, false);
            }

            HideSourceMesh();

            SpriteInstanceRenderSystem.Install(em);
            SpriteInstanceRenderSystem.SetSheet(sheet);
            ApplyInstanceGrid(em, authoring, 0);

            SpriteBatchSpawner.SetPrototype(em, s_proto);
            s_ready = true;
            return true;
        }

        public int SpawnBatch() => Spawn(BatchSize, Grid);

        public int Spawn(int count, bool grid)
        {
            if (!EnsureProto())
                return 0;
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            float spawnScale = SizeUnits * ClipWorldHeight(
                Source != null ? Source : Object.FindFirstObjectByType<SpriteAnimSetAuthoring>(), 0);
            int spawned = SpriteBatchSpawner.SpawnNow(
                em,
                float3.zero,
                Spread,
                spawnScale,
                count,
                grid,
                randomizeClocks: true);
            // Avoid first SetAllClips hitch: entities already have spawnScale, so
            // prime the static without walking LocalTransform / MarkDirty.
            if (spawned > 0)
                PrimeAppliedScale(spawnScale);
            Debug.Log("[Crowd Spawner] +" + spawned + (grid ? " (grid)" : " (random)") +
                      " | total " + CountAll(), this);
            return spawned;
        }

        public int DespawnAll()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return 0;
            var em = world.EntityManager;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<SpriteCrowdEntityTag>());
            var ents = q.ToEntityArray(Allocator.Temp);
            int killed = 0;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (ents[i] == s_proto)
                    continue;
                ecb.DestroyEntity(ents[i]);
                killed++;
            }
            ecb.Playback(em);
            ecb.Dispose();
            ents.Dispose();
            return killed;
        }

        public static int CountAll()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return 0;
            var q = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SpriteCrowdEntityTag>());
            return q.CalculateEntityCount();
        }


        static void ApplyInstanceGrid(EntityManager em, SpriteAnimSetAuthoring authoring, int clipIndex)
        {
            if (authoring == null)
            {
                SpriteInstanceRenderSystem.SetGrid(em, 4, 4, 1f);
                return;
            }
            authoring.TryGetClipSheet(clipIndex, out var tex, out int cols, out int rows, out _);
            cols = Mathf.Max(1, cols);
            rows = Mathf.Max(1, rows);
            SpriteSheetDef def = null;
            var data = authoring.Profile != null ? authoring.Profile.Data : null;
            if (data != null)
            {
                data.EnsureSheets();
                int sheetIndex = 0;
                if (authoring.Clips != null && clipIndex >= 0 && clipIndex < authoring.Clips.Length)
                    sheetIndex = authoring.Clips[clipIndex].SheetIndex;
                def = data.SheetAt(sheetIndex);
            }
            if (def != null && def.CellLayoutMode == SpriteSheetCellLayoutMode.Cropped &&
                SpriteSheetProfile.HasCroppedCellData(def))
            {
                SpriteInstanceRenderSystem.SetGridFromSheet(em, def);
                return;
            }
            SpriteInstanceRenderSystem.SetGrid(em, cols, rows,
                SpriteSheetProfile.GetCellAspect(tex, cols, rows));
        }

        public void SetAllClips(int clipIndex)
        {
            var authoring = Source != null
                ? Source
                : Object.FindFirstObjectByType<SpriteAnimSetAuthoring>();
            if (authoring == null || authoring.Clips == null ||
                clipIndex < 0 || clipIndex >= authoring.Clips.Length)
                return;

            var preview = authoring.GetComponent<SpriteAnimPlayerAuthoring>();
            if (preview != null)
                preview.Play(clipIndex);

            if (!s_ready)
                return;

            string clipName = authoring.Clips[clipIndex].Name;
            if (string.IsNullOrWhiteSpace(clipName))
                clipName = "clip" + clipIndex;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;
            var em = world.EntityManager;
            authoring.TryGetClipSheet(clipIndex, out var clipTex, out int clipCols, out int clipRows, out _);
            if (clipTex != null)
                SpriteInstanceRenderSystem.SetSheet(clipTex);
            ApplyInstanceGrid(em, authoring, clipIndex);

            float scale = SizeUnits * ClipWorldHeight(authoring, clipIndex);
            ApplyCrowdScaleIfChanged(em, scale);

            var gpuQ = em.CreateEntityQuery(
                ComponentType.ReadOnly<SpriteCrowdEntityTag>(),
                ComponentType.ReadOnly<SpriteGpuDriven>());
            if (gpuQ.CalculateEntityCount() > 0)
            {
                if (s_ready && s_proto != Entity.Null && em.HasComponent<SpriteAnimSetRef>(s_proto))
                {
                    var setRef = em.GetComponentData<SpriteAnimSetRef>(s_proto);
                    ref var blob = ref setRef.Set.Value;
                    if (SpriteGpuAnimSwitch.TryFromClip(
                            ref blob, clipIndex, Time.unscaledTime, 1f,
                            clipCols, clipRows, setRef.Set, out var gpuAnim))
                    {
                        SpriteGpuAnimResources.SetSharedClip(gpuAnim);
                        return;
                    }
                }
                SpriteGpuAnimSwitch.SetAllCrowdClips(world, clipIndex, Time.unscaledTime);
                return;
            }

            var q = em.CreateEntityQuery(
                new ComponentType[] {
                    ComponentType.ReadOnly<SpriteCrowdEntityTag>(),
                    ComponentType.ReadWrite<SpriteAnimPlayer>(),
                    ComponentType.ReadOnly<SpriteAnimSetRef>(),
                });
            var ents = q.ToEntityArray(Allocator.Temp);
            int changed = 0;
            for (int n = 0; n < ents.Length; n++)
            {
                if (SpriteAnims.Play(em, ents[n], clipIndex))
                    changed++;
            }
            ents.Dispose();
            Debug.Log("[Crowd Spawner] clip " + clipIndex + " '" + clipName + "' -> " + changed);
        }

        static float ClipWorldHeight(SpriteAnimSetAuthoring authoring, int clipIndex)
        {
            if (authoring == null)
                return 1f;
            if (!authoring.TryGetClipSheet(clipIndex, out var tex, out _, out int rows, out float ppu))
                return 1f;
            if (tex == null)
                return 1f;
            float cellH = tex.height / (float)Mathf.Max(1, rows);
            return cellH / Mathf.Max(0.01f, ppu);
        }

        static float s_appliedScale = -1f;
        static bool s_hasAppliedScale;

        /// <summary>Record current world scale without walking entities (call after spawn).</summary>
        static void PrimeAppliedScale(float scale)
        {
            s_appliedScale = scale;
            s_hasAppliedScale = true;
        }

        static void ApplyCrowdScaleIfChanged(EntityManager em, float scale)
        {
            if (s_hasAppliedScale && Mathf.Abs(s_appliedScale - scale) < 0.0001f)
                return;
            s_appliedScale = scale;
            s_hasAppliedScale = true;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<SpriteCrowdEntityTag>(),
                ComponentType.ReadWrite<LocalTransform>());
            var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var lt = em.GetComponentData<LocalTransform>(ents[i]);
                lt.Scale = scale;
                em.SetComponentData(ents[i], lt);
            }
            ents.Dispose();
            SpriteGpuAnimResources.MarkDirty();
        }

        void HideSourceMesh()
        {
            var authoring = Source != null
                ? Source
                : Object.FindFirstObjectByType<SpriteAnimSetAuthoring>();
            if (authoring == null)
                return;
            var mr = authoring.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.enabled = false;
        }

        void FitMainCamera(int count)
        {
            var cam = Camera.main;
            if (cam == null || !cam.orthographic || count <= 0)
                return;
            int side = Mathf.CeilToInt(Mathf.Sqrt(count));
            float step = SizeUnits * 1.15f;
            float half = (side - 1) * step * 0.5f + SizeUnits;
            cam.orthographicSize = Mathf.Max(cam.orthographicSize, half);
        }
    }
}
