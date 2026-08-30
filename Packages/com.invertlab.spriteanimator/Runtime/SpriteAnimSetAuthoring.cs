using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Authoring: attach to any GameObject to make it a pure-ECS animated sprite.
    /// Point it at a spritesheet texture and define the animation states (rows of
    /// the sheet, or explicit frame lists).
    ///
    /// Bakes into: LocalTransform + SpriteAnimSheetAsset/Def + SpriteAnimSetRef
    /// (all clips in one blob) + SpriteAnimPlayer. Rendering (one material per
    /// sheet cell, crop baked in) is wired by SpriteAnimRenderInitSystem; frame
    /// playback is done by SpriteAnimPlayerSystem.
    ///
    /// Usage from gameplay:  SpriteAnims.Play(em, entity, "Attack");
    /// </summary>
    [DisallowMultipleComponent]
    public class SpriteAnimSetAuthoring : MonoBehaviour
    {
        [Tooltip("Optional profile authored in Window > DOTS Sprite Animator. When set, it overrides Sheet, grid, and Clips below.")]
        public ScriptableSpriteSheetProfile Profile;

        [Tooltip("Spritesheet: grid of frames, left-to-right then top-to-bottom")]
        public Texture2D Sheet;

        [Tooltip("Grid columns / rows in the sheet")]
        public int Columns = 4;
        public int Rows = 4;

        [System.Serializable]
        public struct ClipAuthoring
        {
            public string Name;      // "Idle", "Run", ...
            public int    SheetIndex; // 0-based into SpriteSheetProfile.Sheets
            public int    Row;       // which sheet row (0 = top row)
            public int[]  Frames;    // column indices, in play order (e.g. 0 1 2 3)
            public float  FrameRate; // frames per second
            public bool   Loop;
            public byte   WrapMode;
            public float[] FrameDurationScales;
            public byte[] EventIds;
            public float[] EventNormalizedTimes;
            public Vector2[] FrameOffsets;
            public Vector2[] FrameScales;
            public float[] FrameRotations;
            public byte[] FrameTweenModes;
            public string FacingGroup;
            public SpriteFacingDirection FacingDirection;
            public FrameSocketDef[] Sockets;
        }

        [Tooltip("Animation states — e.g. soldier: Idle, Run, Attack, Block")]
        public ClipAuthoring[] Clips =
        {
            new ClipAuthoring { Name = "Idle",   Row = 0, Frames = new[] { 0, 1, 2, 3 }, FrameRate = 8f,  Loop = true },
            new ClipAuthoring { Name = "Run",    Row = 1, Frames = new[] { 0, 1, 2, 3 }, FrameRate = 10f, Loop = true },
            new ClipAuthoring { Name = "Attack", Row = 2, Frames = new[] { 0, 1, 2, 3 }, FrameRate = 12f, Loop = false },
            new ClipAuthoring { Name = "Block",  Row = 3, Frames = new[] { 0, 1, 2, 3 }, FrameRate = 8f,  Loop = true },
        };

        [Tooltip("First clip to play")]
        public int InitialClipIndex = 0;

        [Min(0.01f)]
        public float SizeUnits = 1f;

        [Tooltip("Optional tint")]
        public Color Tint = Color.white;

        [Tooltip("Show the top clip first frame on this Quad in the Scene view. Uncheck to hide the preview mesh.")]
        public bool ShowScenePreview = true;


        public bool ApplyFromProfile()
        {
            var data = Profile?.Data;
            if (data == null)
                return false;

            data.EnsureSheets();
            SpriteSheetDef bakeSheet = null;
            if (data.Clips != null && data.Clips.Count > 0)
                bakeSheet = data.SheetForClip(data.Clips[0]);
            if (bakeSheet == null && data.Sheets != null && data.Sheets.Count > 0)
                bakeSheet = data.Sheets[0];

            if (bakeSheet != null)
            {
                if (bakeSheet.Texture != null)
                    Sheet = bakeSheet.Texture;
                Columns = Mathf.Max(1, bakeSheet.Columns);
                Rows = Mathf.Max(1, bakeSheet.Rows);
            }
            else
            {
                if (data.Sheet != null)
                    Sheet = data.Sheet;
                Columns = Mathf.Max(1, data.Columns);
                Rows = Mathf.Max(1, data.Rows);
            }

            if (data.Clips != null && data.Clips.Count > 0)
                Clips = CopyClips(data.Clips);

            int clipCount = Clips != null ? Clips.Length : 0;
            InitialClipIndex = clipCount > 0
                ? Mathf.Clamp(InitialClipIndex, 0, clipCount - 1)
                : 0;
#if UNITY_EDITOR
            RefreshQuadPreview();
#endif
            return true;
        }

        public bool TryGetClipSheet(int clipIndex, out Texture2D texture, out int columns, out int rows, out float ppu)
        {
            texture = Sheet;
            columns = Mathf.Max(1, Columns);
            rows = Mathf.Max(1, Rows);
            ppu = SpriteSheetProfile.DefaultPixelsPerUnit;
            var data = Profile?.Data;
            if (data != null)
            {
                data.EnsureSheets();
                int sheetIndex = 0;
                if (Clips != null && clipIndex >= 0 && clipIndex < Clips.Length)
                    sheetIndex = Clips[clipIndex].SheetIndex;
                else if (data.Clips != null && clipIndex >= 0 && clipIndex < data.Clips.Count)
                    sheetIndex = data.Clips[clipIndex].SheetIndex;
                var def = data.SheetAt(sheetIndex);
                if (def != null)
                {
                    if (def.Texture != null)
                        texture = def.Texture;
                    columns = Mathf.Max(1, def.Columns);
                    rows = Mathf.Max(1, def.Rows);
                    ppu = SpriteSheetProfile.GetPixelsPerUnit(def);
                }
            }
            return texture != null;
        }

        void OnValidate()
        {
            if (Profile != null)
                ApplyFromProfile();
#if UNITY_EDITOR
            RefreshQuadPreview();
#endif
        }

#if UNITY_EDITOR
        void RefreshQuadPreview()
        {
            var player = GetComponent<SpriteAnimPlayerAuthoring>();
            if (player != null)
                ApplyQuadPreview(player.ClipIndex, player.Frame);
            else
                ApplyQuadPreview();
        }

        public void ApplyQuadPreview() => ApplyQuadPreview(0, 0);

        public void ApplyQuadPreview(int clipIndex, int frameIndex)
        {
            var filter = GetComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            if (filter == null || renderer == null)
                return;

            var mesh = filter.sharedMesh;
            if (mesh == null)
                return;
            if (mesh.name.IndexOf("Quad", System.StringComparison.OrdinalIgnoreCase) < 0 && mesh.vertexCount != 4)
                return;

            if (!ShowScenePreview)
            {
                renderer.SetPropertyBlock(null);
                renderer.enabled = false;
                return;
            }

            Texture2D previewSheet = Sheet;
            int previewColumns = Mathf.Max(1, Columns);
            int previewRows = Mathf.Max(1, Rows);
            SpriteSheetDef clipSheet = null;
            if (Profile?.Data != null)
            {
                var data = Profile.Data;
                data.EnsureSheets();
                if (Clips != null && clipIndex >= 0 && clipIndex < Clips.Length)
                    clipSheet = data.SheetAt(Clips[clipIndex].SheetIndex);
                if (clipSheet?.Texture == null)
                    clipSheet = data.SheetAt(0);
                if (clipSheet?.Texture != null)
                {
                    previewSheet = clipSheet.Texture;
                    previewColumns = Mathf.Max(1, clipSheet.Columns);
                    previewRows = Mathf.Max(1, clipSheet.Rows);
                }
            }

            if (previewSheet == null)
                return;

            var shader = Shader.Find(SpriteShaderLibrary.UnlitShader);
            if (shader == null)
                return;

            var mat = renderer.sharedMaterial;
            if (mat == null || mat.shader != shader)
            {
                mat = new Material(shader)
                {
                    name = "InvertLab Sprite Preview",
                    hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
                };
                renderer.sharedMaterial = mat;
            }

            mat.enableInstancing = false;
            mat.DisableKeyword("DOTS_INSTANCING_ON");
            var kw = new LocalKeyword(shader, "DOTS_INSTANCING_ON");
            if (kw.isValid)
                mat.SetKeyword(kw, false);

            int cols = previewColumns;
            int rows = previewRows;
            int col = 0;
            int row = 0;
            if (Clips != null && Clips.Length > 0)
            {
                if (clipIndex >= 0 && clipIndex < Clips.Length)
                {
                    var clip = Clips[clipIndex];
                    row = clip.Row;
                    if (clip.Frames != null && clip.Frames.Length > 0)
                    {
                        int fi = Mathf.Clamp(frameIndex, 0, clip.Frames.Length - 1);
                        col = clip.Frames[fi];
                    }
                }
                else
                {
                    var clip = Clips[0];
                    if (clip.Frames != null && clip.Frames.Length > 0)
                        col = clip.Frames[0];
                    row = clip.Row;
                }
            }

            float w = 1f / cols;
            float h = 1f / rows;
            var cropST = new Vector4(w, h, col * w, 1f - (row + 1) * h);

            mat.SetTexture("_MainTex", previewSheet);
            mat.SetColor("_Color", Tint);
            mat.SetVector("_CropST", cropST);

            var block = new MaterialPropertyBlock();
            block.SetTexture("_MainTex", previewSheet);
            block.SetColor("_Color", Tint);
            block.SetVector("_CropST", cropST);
            renderer.SetPropertyBlock(block);

            renderer.enabled = true;

            // 1x1 Quad crop is UV-only; scale the transform so PPU is visible in Scene view.
            if (clipSheet != null &&
                SpriteSheetProfile.TryGetCellPixels(clipSheet, out float cellW, out float cellH))
            {
                float ppu = SpriteSheetProfile.GetPixelsPerUnit(clipSheet);
                float sx = cellW / ppu;
                float sy = cellH / ppu;
                var scale = transform.localScale;
                if (!Mathf.Approximately(scale.x, sx) || !Mathf.Approximately(scale.y, sy))
                {
                    scale.x = sx;
                    scale.y = sy;
                    transform.localScale = scale;
                }
            }
        }
#endif

        static ClipAuthoring[] CopyClips(List<SpriteClipDef> clips)
        {
            var result = new ClipAuthoring[clips.Count];
            for (int i = 0; i < clips.Count; i++)
            {
                var src = clips[i];
                if (src == null)
                    continue;

                result[i] = new ClipAuthoring
                {
                    Name = src.Name,
                    SheetIndex = src.SheetIndex,
                    Row = src.Row,
                    Frames = CopyArray(src.Frames),
                    FrameRate = src.FrameRate,
                    WrapMode = src.WrapMode,
                    Loop = src.WrapMode == SpriteAnimWrap.Loop
                        || src.WrapMode == SpriteAnimWrap.ReverseLoop,
                    FrameDurationScales = CopyArray(src.FrameDurationScales),
                    EventIds = CopyArray(src.EventIds),
                    EventNormalizedTimes = CopyArray(src.EventNormalizedTimes),
                    FrameOffsets = CopyArray(src.OnionOffsets),
                    FrameScales = CopyArray(src.FrameScales),
                    FrameRotations = CopyArray(src.FrameRotations),
                    FrameTweenModes = CopyArray(src.FrameTweenModes),
                    FacingGroup = src.FacingGroup,
                    FacingDirection = src.Facing,
                    Sockets = CopySockets(src.Sockets),
                };
            }
            return result;
        }

        static T[] CopyArray<T>(T[] source)
        {
            if (source == null)
                return null;
            return (T[])source.Clone();
        }

        static FrameSocketDef[] CopySockets(List<FrameSocketDef> sockets)
        {
            if (sockets == null || sockets.Count == 0)
                return null;

            var result = new FrameSocketDef[sockets.Count];
            for (int i = 0; i < sockets.Count; i++)
            {
                var src = sockets[i];
                if (src == null)
                    continue;

                result[i] = new FrameSocketDef
                {
                    Name = src.Name,
                    FrameIndex = src.FrameIndex,
                    LocalPosition = src.LocalPosition,
                    LocalAngle = src.LocalAngle,
                    LocalScale = src.LocalScale,
                    DrawLayer = src.DrawLayer,
                };
            }
            return result;
        }

        static SpriteAnimSetBuilder.SocketInventoryInput[] BuildSocketInventoryInputs(
            SpriteSheetProfile profile)
        {
            profile?.EnsureSocketInventories();
            if (profile?.SocketInventories == null || profile.SocketInventories.Count == 0)
                return null;
            var result = new SpriteAnimSetBuilder.SocketInventoryInput[profile.SocketInventories.Count];
            for (int i = 0; i < profile.SocketInventories.Count; i++)
            {
                var inv = profile.SocketInventories[i];
                int memberCount = inv.SocketNames?.Count ?? 0;
                var ids = new string[memberCount];
                var names = new string[memberCount];
                var kinds = new byte[memberCount];
                for (int m = 0; m < memberCount; m++)
                {
                    string socketName = inv.SocketNames[m];
                    var item = profile.SocketCatalog?.Find(socketName);
                    names[m] = socketName;
                    ids[m] = SpriteSocketIdUtility.Canonical(item != null ? item.SocketId : null, socketName);
                    bool independent = item != null && item.UsesOwnClock
                        || profile.FindSocketMotion(socketName) != null;
                    kinds[m] = independent
                        ? (byte)SpriteSocketInventoryKind.Independent
                        : (byte)SpriteSocketInventoryKind.Frame;
                }
                result[i] = new SpriteAnimSetBuilder.SocketInventoryInput
                {
                    Name = string.IsNullOrWhiteSpace(inv.Name) ? "Inventory" : inv.Name.Trim(),
                    SocketIds = ids,
                    SocketNames = names,
                    Kinds = kinds,
                };
            }
            return result;
        }

        class Baker : Baker<SpriteAnimSetAuthoring>
        {
            public override void Bake(SpriteAnimSetAuthoring authoring)
            {
                var profile = authoring.Profile != null ? authoring.Profile.Data : null;
                if (profile != null)
                {
                    profile.EnsureSheets();
                    profile.EnsureSocketCatalog();
                    profile.EnsureSocketMotions();
                }

                SpriteSheetDef bakeSheetDef = null;
                bool useProfile = profile?.Clips != null && profile.Clips.Count > 0;
                if (useProfile)
                    bakeSheetDef = profile.SheetForClip(profile.Clips[0]);
                var sheet = useProfile
                    ? (bakeSheetDef?.Texture ?? profile.Sheet)
                    : authoring.Sheet;
                int clipCount = useProfile ? profile.Clips.Count : authoring.Clips?.Length ?? 0;
                if (sheet == null || clipCount == 0)
                    return;

                if (authoring.Profile != null)
                    DependsOn(authoring.Profile);
                DependsOn(sheet);

                var entity = GetEntity(authoring, TransformUsageFlags.Renderable);

                // data-only bake: the GPU-instanced renderer consumes these directly
                // (no GameObjects graphics components involved)

                // ---- clip blob ----
                var inputs = new SpriteAnimSetBuilder.ClipInput[clipCount];
                for (int i = 0; i < clipCount; i++)
                {
                    var profileClip = useProfile ? profile.Clips[i] : null;
                    var authorClip = useProfile ? default : authoring.Clips[i];
                    var clipSheet = useProfile ? profile.SheetForClip(profileClip) : null;
                    int cols = Mathf.Max(1, clipSheet != null ? clipSheet.Columns : authoring.Columns);
                    int rows = Mathf.Max(1, clipSheet != null ? clipSheet.Rows : authoring.Rows);
                    float bakePpu = clipSheet != null
                        ? SpriteSheetProfile.GetPixelsPerUnit(clipSheet)
                        : 1f;
                    if (clipSheet?.Texture != null)
                        DependsOn(clipSheet.Texture);
                    var frameCols = useProfile ? profileClip.Frames : authorClip.Frames;
                    frameCols = frameCols != null && frameCols.Length > 0
                        ? frameCols
                        : new[] { 0, 1, 2, 3 };
                    var frameScales = useProfile ? profileClip.FrameScales : authorClip.FrameScales;
                    var frameRotations = useProfile ? profileClip.FrameRotations : authorClip.FrameRotations;
                    var frameTweens = useProfile ? profileClip.FrameTweenModes : authorClip.FrameTweenModes;
                    int row = useProfile ? profileClip.Row : authorClip.Row;
                    var slots = new int[frameCols.Length];
                    var frameOffsets = new float2[frameCols.Length];
                    var clipScales = new float2[frameCols.Length];
                    var clipRotations = new float[frameCols.Length];
                    var clipTweens = new byte[frameCols.Length];
                    for (int f = 0; f < frameCols.Length; f++)
                    {
                        slots[f] = Mathf.Clamp(row, 0, rows - 1) * cols + Mathf.Clamp(frameCols[f], 0, cols - 1);
                        Vector2 offset = useProfile && profileClip.OnionOffsets != null && f < profileClip.OnionOffsets.Length
                            ? profileClip.OnionOffsets[f] / bakePpu
                            : !useProfile && authorClip.FrameOffsets != null && f < authorClip.FrameOffsets.Length
                                ? authorClip.FrameOffsets[f]
                                : Vector2.zero;
                        frameOffsets[f] = new float2(offset.x, offset.y);
                        Vector2 scale = frameScales != null && f < frameScales.Length
                            ? frameScales[f]
                            : Vector2.one;
                        clipScales[f] = new float2(scale.x, scale.y);
                        clipRotations[f] = frameRotations != null && f < frameRotations.Length
                            ? frameRotations[f]
                            : 0f;
                        clipTweens[f] = frameTweens != null && f < frameTweens.Length
                            ? frameTweens[f]
                            : (byte)SpriteEaseMode.Linear;
                    }

                    int socketCount = useProfile
                        ? profileClip.Sockets?.Count ?? 0
                        : authorClip.Sockets?.Length ?? 0;
                    var socketInputs = new SpriteAnimSetBuilder.ClipInput.FrameSocketInput[socketCount];
                    for (int s = 0; s < socketInputs.Length; s++)
                    {
                        var socket = useProfile ? profileClip.Sockets[s] : authorClip.Sockets[s];
                        float2 position = useProfile
                            ? new float2(
                                socket.LocalPosition.x / bakePpu,
                                socket.LocalPosition.y / bakePpu)
                            : new float2(socket.LocalPosition.x, socket.LocalPosition.y);
                        string socketId = useProfile
                            ? profile.SocketCatalog.Find(socket.Name)?.SocketId
                            : socket.Name;
                        socketInputs[s] = new SpriteAnimSetBuilder.ClipInput.FrameSocketInput
                        {
                            FrameIndex = socket.FrameIndex,
                            LocalPosition = position,
                            LocalAngle = socket.LocalAngle,
                            LocalScale = new float2(
                                SpriteSocketKeys.ResolvedScale(socket.LocalScale).x,
                                SpriteSocketKeys.ResolvedScale(socket.LocalScale).y),
                            Name = socket.Name,
                            SocketId = socketId,
                        };
                    }

                    inputs[i] = new SpriteAnimSetBuilder.ClipInput
                    {
                        Name = useProfile
                            ? (string.IsNullOrEmpty(profileClip.Name) ? ("clip" + i) : profileClip.Name)
                            : (string.IsNullOrEmpty(authorClip.Name) ? ("clip" + i) : authorClip.Name),
                        Loop = useProfile
                            ? profileClip.WrapMode == SpriteAnimWrap.Loop || profileClip.WrapMode == SpriteAnimWrap.ReverseLoop
                            : authorClip.Loop || authorClip.WrapMode == SpriteAnimWrap.ReverseLoop,
                        WrapMode = useProfile ? profileClip.WrapMode : authorClip.WrapMode,
                        FrameRate = Mathf.Max(0.1f, useProfile ? profileClip.FrameRate : authorClip.FrameRate),
                        GlobalFrameIndices = slots,
                        FrameDurationScales = useProfile ? profileClip.FrameDurationScales : authorClip.FrameDurationScales,
                        EventIds = useProfile ? profileClip.EventIds : authorClip.EventIds,
                        EventNormalizedTimes = useProfile
                            ? profileClip.EventNormalizedTimes
                            : authorClip.EventNormalizedTimes,
                        FrameOffsets = frameOffsets,
                        FrameScales = clipScales,
                        FrameRotations = clipRotations,
                        FrameTweenModes = clipTweens,
                        FacingGroup = useProfile ? profileClip.FacingGroup : authorClip.FacingGroup,
                        FacingDirection = useProfile ? profileClip.Facing : authorClip.FacingDirection,
                        FrameSockets = socketInputs,
                    };
                }
                var activeMotions = new List<SpriteSocketMotionTrack>();
                if (useProfile && profile.SocketMotions != null)
                {
                    for (int i = 0; i < profile.SocketMotions.Count; i++)
                    {
                        var candidate = profile.SocketMotions[i];
                        var candidateItem = candidate != null
                            ? profile.SocketCatalog.Find(candidate.SocketName)
                            : null;
                        if (candidate != null && candidate.Keys != null &&
                            candidate.Keys.Count > 0 && candidateItem != null &&
                            candidateItem.UsesOwnClock)
                            activeMotions.Add(candidate);
                    }
                }
                int motionCount = activeMotions.Count;
                var motionInputs = new SpriteAnimSetBuilder.SocketMotionInput[motionCount];
                for (int i = 0; i < motionCount; i++)
                {
                    var motion = activeMotions[i];
                    var motionSheet = profile.SheetAt(motion.ReferenceSheetIndex);
                    float motionPpu = SpriteSheetProfile.GetPixelsPerUnit(motionSheet);
                    var catalogItem = profile.SocketCatalog.Find(motion.SocketName);
                    int keyCount = motion.Keys?.Count ?? 0;
                    var keys =
                        new SpriteAnimSetBuilder.SocketMotionInput.SocketMotionPointInput[keyCount];
                    for (int k = 0; k < keyCount; k++)
                    {
                        var key = motion.Keys[k];
                        Vector2 resolvedScale = SpriteSocketKeys.ResolvedScale(key.LocalScale);
                        keys[k] =
                            new SpriteAnimSetBuilder.SocketMotionInput.SocketMotionPointInput
                            {
                                NormalizedTime = key.NormalizedTime,
                                LocalPosition = new float2(
                                    key.LocalPosition.x / motionPpu,
                                    key.LocalPosition.y / motionPpu),
                                LocalAngle = key.LocalAngle,
                                LocalScale = new float2(resolvedScale.x, resolvedScale.y),
                                EaseMode = key.EaseMode,
                                PathMode = key.PathMode,
                                UseCustomEase = key.UseCustomEase ? (byte)1 : (byte)0,
                                CustomEaseSamplesA = new float4(
                                    key.CustomEaseSamplesA.x, key.CustomEaseSamplesA.y,
                                    key.CustomEaseSamplesA.z, key.CustomEaseSamplesA.w),
                                CustomEaseSamplesB = new float4(
                                    key.CustomEaseSamplesB.x, key.CustomEaseSamplesB.y,
                                    key.CustomEaseSamplesB.z, key.CustomEaseSamplesB.w),
                                AllowOvershoot = key.AllowOvershoot ? (byte)1 : (byte)0,
                                InTangent = new float2(
                                    key.InTangent.x / motionPpu,
                                    key.InTangent.y / motionPpu),
                                OutTangent = new float2(
                                    key.OutTangent.x / motionPpu,
                                    key.OutTangent.y / motionPpu),
                                ArcBulge = key.ArcBulge / motionPpu,
                                ArcClockwise = key.ArcClockwise ? (byte)1 : (byte)0,
                                RotationMode = key.RotationMode,
                                RotationTurns = key.RotationTurns,
                                FacingAngleOffset = key.FacingAngleOffset,
                            };
                    }
                    int triggerCount = motion.Triggers?.Count ?? 0;
                    var triggers =
                        new SpriteAnimSetBuilder.SocketMotionInput.SocketTriggerInput[triggerCount];
                    for (int t = 0; t < triggerCount; t++)
                    {
                        triggers[t] = new SpriteAnimSetBuilder.SocketMotionInput.SocketTriggerInput
                        {
                            NormalizedTime = motion.Triggers[t].NormalizedTime,
                            EventId = motion.Triggers[t].EventId,
                        };
                    }
                    motionInputs[i] = new SpriteAnimSetBuilder.SocketMotionInput
                    {
                        Name = motion.SocketName,
                        SocketId = catalogItem?.SocketId,
                        Duration = profile.IndependentMotionDuration,
                        Speed = 1f,
                        Loop = profile.IndependentMotionLoop,
                        AnchorSpace = motion.AnchorSpace,
                        Keys = keys,
                        Triggers = triggers,
                    };
                }

                var inventoryInputs = BuildSocketInventoryInputs(profile);
                var (setRef, player) = SpriteAnimSetBuilder.Build(
                    Allocator.Persistent, inputs, motionInputs, inventoryInputs);
                AddComponent(entity, setRef);
                var playerAuthoring = GetComponent<SpriteAnimPlayerAuthoring>();
                int initialClip;
                if (playerAuthoring != null)
                {
                    initialClip = Mathf.Clamp(playerAuthoring.ClipIndex, 0, clipCount - 1);
                    player.ClipIndex = initialClip;
                    player.Speed = Mathf.Max(0.01f, playerAuthoring.Speed);
                    player.Playing = playerAuthoring.Playing ? (byte)1 : (byte)0;
                }
                else
                {
                    initialClip = Mathf.Clamp(authoring.InitialClipIndex, 0, clipCount - 1);
                    player.ClipIndex = initialClip;
                }
                AddComponent(entity, player);
                ref var initialDef = ref setRef.Set.Value.Clips[initialClip];
                int initialFrame = initialDef.WrapMode == SpriteAnimWrap.ReverseLoop
                    ? Mathf.Max(0, initialDef.FrameCount - 1)
                    : 0;
                float4 firstFrame = setRef.Set.Value.Frames[initialDef.FirstFrame + initialFrame];
                AddComponent(entity, new SpriteAnimFrame
                {
                    Slot = (int)firstFrame.x,
                    Offset = firstFrame.yz,
                    Scale = initialDef.FrameScales.Length > initialFrame
                        ? initialDef.FrameScales[initialFrame]
                        : new float2(1f, 1f),
                    Rotation = initialDef.FrameRotations.Length > initialFrame
                        ? initialDef.FrameRotations[initialFrame]
                        : 0f,
                });
                AddComponent(entity, new SpriteTint { Value = new float4(
                    authoring.Tint.r, authoring.Tint.g, authoring.Tint.b, authoring.Tint.a) });
                AddComponent(entity, new SpriteAnimEnabled());
                AddComponent(entity, new SpriteFlip());
                AddBuffer<SpriteAnimEventBuffer>(entity);
                AddComponent(entity, new SpriteAnimEventsPending());
                AddBuffer<SpriteSocketBuffer>(entity);
                if (inventoryInputs != null && inventoryInputs.Length > 0)
                {
                    AddComponent<SpriteSocketInventoryTag>(entity);
                    var inventoryBuffer = AddBuffer<SpriteSocketInventoryMember>(entity);
                    for (int i = 0; i < inventoryInputs.Length; i++)
                    {
                        var inv = inventoryInputs[i];
                        string invName = string.IsNullOrWhiteSpace(inv.Name) ? "inventory" : inv.Name.Trim();
                        uint groupHash = SpriteSockets.InventoryHash(invName);
                        int memberCount = inv.SocketIds?.Length ?? 0;
                        for (int m = 0; m < memberCount; m++)
                        {
                            string socketId = inv.SocketIds[m];
                            string socketName = inv.SocketNames != null && m < inv.SocketNames.Length
                                ? inv.SocketNames[m] : socketId;
                            inventoryBuffer.Add(new SpriteSocketInventoryMember
                            {
                                GroupHash = groupHash,
                                GroupName = invName,
                                SocketIdHash = SpriteSockets.Hash(socketId),
                                SocketId = socketId,
                                SocketName = socketName,
                                Kind = inv.Kinds != null && m < inv.Kinds.Length
                                    ? inv.Kinds[m]
                                    : (byte)SpriteSocketInventoryKind.Frame,
                            });
                        }
                    }
                }
                if (motionCount > 0)
                {
                    AddComponent(entity, new SpriteSocketMotionPlayer
                    {
                        Time = 0f,
                        Speed = profile.IndependentMotionSpeed,
                        Playing = 1,
                    });
                    AddBuffer<SpriteSocketEventBuffer>(entity);
                    AddComponent(entity, new SpriteSocketEventsPending());
                }
            }
        }
    }
}
