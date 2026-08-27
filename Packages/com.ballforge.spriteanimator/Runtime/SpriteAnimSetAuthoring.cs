using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace BallForge.Sprites.DOTS
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

        class Baker : Baker<SpriteAnimSetAuthoring>
        {
            public override void Bake(SpriteAnimSetAuthoring authoring)
            {
                var profile = authoring.Profile != null ? authoring.Profile.Data : null;
                bool useProfile = profile?.Sheet != null && profile.Clips != null && profile.Clips.Count > 0;
                var sheet = useProfile ? profile.Sheet : authoring.Sheet;
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
                int cols = Mathf.Max(1, useProfile ? profile.Columns : authoring.Columns);
                int rows = Mathf.Max(1, useProfile ? profile.Rows : authoring.Rows);
                var inputs = new SpriteAnimSetBuilder.ClipInput[clipCount];
                for (int i = 0; i < clipCount; i++)
                {
                    var profileClip = useProfile ? profile.Clips[i] : null;
                    var authorClip = useProfile ? default : authoring.Clips[i];
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
                            ? profileClip.OnionOffsets[f] / Mathf.Max(0.01f, profile.PixelsPerUnit)
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
                                socket.LocalPosition.x / Mathf.Max(0.01f, profile.PixelsPerUnit),
                                socket.LocalPosition.y / Mathf.Max(0.01f, profile.PixelsPerUnit))
                            : new float2(socket.LocalPosition.x, socket.LocalPosition.y);
                        socketInputs[s] = new SpriteAnimSetBuilder.ClipInput.FrameSocketInput
                        {
                            FrameIndex = socket.FrameIndex,
                            LocalPosition = position,
                            LocalAngle = socket.LocalAngle,
                            Name = socket.Name,
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
                var (setRef, player) = SpriteAnimSetBuilder.Build(Allocator.Persistent, inputs);
                AddComponent(entity, setRef);
                int initialClip = Mathf.Clamp(authoring.InitialClipIndex, 0, clipCount - 1);
                player.ClipIndex = initialClip;
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
            }
        }
    }
}
