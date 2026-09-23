using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    public struct SpritePartsSetBlob
    {
        public BlobArray<SpritePartSlotBlob> Slots;
        public BlobArray<SpritePartsClipBlob> Clips;
        public BlobArray<SpritePartAppearanceBlob> Appearances;
        public BlobArray<SpritePartsSkinBlob> SkinPatches;
        /// <summary>IK constraints in solve order.</summary>
        public BlobArray<SpritePartsIkBlob> IkConstraints;
        /// <summary>Jiggle joints, parents before children.</summary>
        public BlobArray<SpritePartsJiggleBlob> Jiggles;
        /// <summary>Control parameters; values live on the character (<see cref="SpritePartsParamValue"/>).</summary>
        public BlobArray<SpritePartsParamBlob> Params;
        /// <summary>Transform constraints, solved after IK.</summary>
        public BlobArray<SpritePartsTransformBlob> TransformConstraints;
        /// <summary>Path constraints, solved after transform constraints.</summary>
        public BlobArray<SpritePartsPathBlob> PathConstraints;
        /// <summary>Crossfade times per clip pair (-1 From = any clip).</summary>
        public BlobArray<SpritePartsMixBlob> Mixes;
        public float DefaultMix;
        public byte DefaultMixEase;
        /// <summary>1 = the clip fading out still fires its events.</summary>
        public byte FadeOutEvents;
        public BlobArray<SpritePartsMaskBlob> Masks;
        public BlobArray<SpritePartsBlendSpaceBlob> BlendSpaces;
    }

    public struct SpritePartsTransformBlob
    {
        public int Target;
        /// <summary>Constrained slots, parents first.</summary>
        public BlobArray<int> Bones;
        public float MixRotate;
        public float MixX;
        public float MixY;
        public float MixScaleX;
        public float MixScaleY;
        public float OffsetRotation;
        public float2 OffsetPosition;
        public float2 OffsetScale;
        public byte Local;
        public byte Relative;
    }

    public struct SpritePartsPathBlob
    {
        /// <summary>The slot whose <see cref="SpritePartSlotBlob.PathPoints"/> is the curve.</summary>
        public int PathSlot;
        /// <summary>Constrained slots in chain order (first sits at <see cref="Position"/>).</summary>
        public BlobArray<int> Bones;
        public float Position;
        public float Spacing;
        public byte SpacingMode;
        public byte RotateMode;
        public float OffsetRotation;
        public float MixRotate;
        public float MixTranslate;
    }

    public struct SpritePartsMixBlob
    {
        public int From;
        public int To;
        public float Duration;
        public byte Ease;
    }

    public struct SpritePartsMaskBlob
    {
        public FixedString64Bytes Name;
        public uint Bits;
    }

    public struct SpritePartsBlendSpaceBlob
    {
        public FixedString64Bytes Name;
        /// <summary>Sorted by value.</summary>
        public BlobArray<SpritePartsBlendPointBlob> Points;
    }

    public struct SpritePartsBlendPointBlob
    {
        public int ClipIndex;
        public float Value;
    }

    /// <summary>A control parameter: its value scrubs <see cref="ClipIndex"/> from start (Min) to end (Max).</summary>
    public struct SpritePartsParamBlob
    {
        public FixedString64Bytes Name;
        public int ClipIndex;
        public float Min;
        public float Max;
        public float Default;
        public byte Additive;
    }

    /// <summary>One spring joint: its tip (in the joint's space) swings behind the animated tip.</summary>
    public struct SpritePartsJiggleBlob
    {
        public int Slot;
        public float2 TipLocal;
        /// <summary>Spring constant (1/s²).</summary>
        public float Spring;
        /// <summary>Velocity damping (1/s).</summary>
        public float Damping;
        public float Gravity;
        public float Mix;
    }

    /// <summary>Resolved IK constraint: slot indices, -1 Upper for a one-joint chain.</summary>
    public struct SpritePartsIkBlob
    {
        public int Effector;
        public int Lower;
        public int Upper;
        public int Target;
        /// <summary>+1 / -1: which way the middle joint bends.</summary>
        public float BendSign;
        public float Mix;
    }

    public struct SpritePartSlotBlob
    {
        public FixedString64Bytes Name;
        public FixedString64Bytes SlotId;
        public ulong SlotIdHash;
        public int ParentSlotIndex;
        public float2 RestPosition;
        public float RestRotation;
        public float2 RestScale;
        public int DefaultAppearanceIndex;
        public int DrawRank;
        /// <summary>1 = do not draw this part (profile Enabled=false or hidden ancestor).</summary>
        public byte Hidden;
        /// <summary>Setup mesh at rest. Empty = rigid rectangle. Keys only carry offsets.</summary>
        public SpritePartsLattice Mesh;
        /// <summary>Rest (setup) pose in character-root space; the bind pose for weights.</summary>
        public float4x4 RestToRoot;
        /// <summary>Weighted bones (slot indices). Empty = the mesh only follows this part.</summary>
        public BlobArray<int> SkinBones;
        /// <summary><c>SkinWeights[vertex * SkinBones.Length + bone]</c>.</summary>
        public BlobArray<float> SkinWeights;
        /// <summary>Default appearance quad: unit quad q maps to <c>(q + 0.5 - pivot) * size</c> in part space.</summary>
        public float2 SkinQuadSize;
        public float2 SkinQuadPivot;
        /// <summary>The slot this part is clipped to (-1 = none).</summary>
        public int ClipMaskIndex;
        /// <summary>
        /// When this slot is a mask with a mesh: its rest mesh split into convex polygons, flattened as
        /// [count, vertex, vertex, ..., count, ...]. Empty = use its triangles (or its rectangle).
        /// </summary>
        public BlobArray<int> MaskPieces;
        /// <summary>1 = a clip shape: <see cref="ClipPolygon"/> clips the parts drawn above it (up to ClipEndIndex).</summary>
        public byte IsClipShape;
        /// <summary>The clip shape's polygon in this slot's space (world units); its convex split is in MaskPieces.</summary>
        public BlobArray<float2> ClipPolygon;
        /// <summary>The last slot (in draw order) a clip shape clips; -1 = every slot above it.</summary>
        public int ClipEndIndex;
        /// <summary>1 = a path: <see cref="PathPoints"/> (this slot's space) is a smooth curve through them.</summary>
        public byte IsPath;
        public byte PathClosed;
        public BlobArray<float2> PathPoints;
    }

    public struct SpritePartsClipBlob
    {
        public FixedString64Bytes Name;
        public FixedString64Bytes ClipId;
        public ulong NameHash;
        public ulong ClipIdHash;
        public float Duration;
        public float SpeedMultiplier;
        public byte WrapMode;
        /// <summary>Dense slot -> pose track index (-1 = rest). Never an appearance track.</summary>
        public BlobArray<int> SlotTrackIndices;
        /// <summary>Dense slot -> appearance track index (-1 = none; sampler then falls back to legacy pose-key ids).</summary>
        public BlobArray<int> SlotAppearanceTrackIndices;
        public BlobArray<SpritePartsTrackBlob> Tracks;
        /// <summary>Events by time.</summary>
        public BlobArray<SpritePartsEventBlob> Events;
        /// <summary>Keyed IK / jiggle / parameter values.</summary>
        public BlobArray<SpritePartsValueTrackBlob> ValueTracks;
    }

    /// <summary>Keys for one named value; <see cref="Targets"/> are the IK, jiggle or parameter indices it drives.</summary>
    public struct SpritePartsValueTrackBlob
    {
        /// <summary><see cref="SpritePartsValueKind"/>.</summary>
        public byte Kind;
        public BlobArray<int> Targets;
        /// <summary>By time.</summary>
        public BlobArray<SpritePartsValueKeyBlob> Keys;
    }

    public struct SpritePartsValueKeyBlob
    {
        public float Time;
        public float Value;
        public byte EaseMode;
        public float4 Curve;
    }

    public struct SpritePartsEventBlob
    {
        public float Time;
        public byte Id;
        public int IntPayload;
        public float FloatPayload;
        public ulong TextHash;
        /// <summary>Index into the character's <see cref="SpritePartsAudioBank"/> (-1 = no sound).</summary>
        public int AudioIndex;
        public float Volume;
        public float Balance;
    }

    public struct SpritePartsTrackBlob
    {
        public int SlotIndex;
        public ulong SlotIdHash;
        public BlobArray<SpritePartsKeyBlob> Keys;
    }

    public struct SpritePartsKeyBlob
    {
        public float Time;
        public float2 Position;
        public float Rotation;
        public float2 Scale;
        public byte EaseMode;
        /// <summary>-1 = no appearance change on this key (hold previous / skin default).</summary>
        public int AppearanceIndex;
        /// <summary>Per-vertex offsets from the slot mesh. Empty = setup mesh.</summary>
        public FixedList512Bytes<float2> Deform;
        /// <summary>1 = <see cref="Color"/> is a colour key (tint, alpha).</summary>
        public byte HasColor;
        public float4 Color;
        /// <summary>1 = <see cref="DrawOrder"/> is a draw-rank key (held until the next one).</summary>
        public byte HasDrawOrder;
        public int DrawOrder;
        /// <summary>Bezier handles (x1, y1, x2, y2) when EaseMode is Bezier.</summary>
        public float4 Curve;
        /// <summary>Channels this key does NOT hold (<see cref="SpritePartsKeyChannel"/> bits). 0 = all of them.</summary>
        public byte SkipChannels;
        /// <summary>1 = position Y, rotation and scale X / Y ease with their own Bezier handles below (X uses Curve).</summary>
        public byte SeparateCurves;
        public float4 CurveY;
        public float4 CurveRotation;
        public float4 CurveScaleX;
        public float4 CurveScaleY;
        /// <summary>1 = a clip key: <see cref="ClipActive"/> from here on (held).</summary>
        public byte HasClipActive;
        public byte ClipActive;

        public bool Holds(SpritePartsKeyChannel channel) => (SkipChannels & (byte)channel) == 0;
    }

    public struct SpritePartAppearanceBlob
    {
        public FixedString64Bytes AppearanceId;
        public ulong AppearanceIdHash;
        public int SheetTableIndex;
        public int CellIndex;
        public float2 LogicalWorldSize;
        public float2 Pivot;
        public float2 FrameOffset;
        public float2 FrameScale;
    }

    public struct SpritePartsSkinBlob
    {
        public FixedString64Bytes SkinId;
        public ulong SkinIdHash;
        public BlobArray<SpritePartsSkinBindingBlob> Bindings;
    }

    public struct SpritePartsSkinBindingBlob
    {
        public int SlotIndex;
        public int AppearanceIndex;
    }

    /// <summary>Builds SpritePartsSetBlob from validated authoring inputs.</summary>
    public static class SpritePartsSetBuilder
    {
        public struct SlotInput
        {
            public string Name;
            public string SlotId;
            public string ParentSlotId;
            public float2 RestPosition;
            public float RestRotation;
            public float2 RestScale;
            public string DefaultAppearanceId;
            public int DrawRank;
            public byte Hidden;
            public SpritePartsLattice Mesh;
            public string[] SkinBones;
            public float[] SkinWeights;
            public float2 SkinQuadSize;
            public float2 SkinQuadPivot;
            public string ClipMaskSlotId;
            public bool IsClipShape;
            public float2[] ClipPolygon;
            public string ClipEndSlotId;
            public bool IsPath;
            public float2[] PathPoints;
            public bool PathClosed;
        }

        public struct AppearanceInput
        {
            public string AppearanceId;
            public int SheetTableIndex;
            public int CellIndex;
            public float2 LogicalWorldSize;
            public float2 Pivot;
            public float2 FrameOffset;
            public float2 FrameScale;
        }

        public struct KeyInput
        {
            public float Time;
            public float2 Position;
            public float Rotation;
            public float2 Scale;
            public byte EaseMode;
            /// <summary>Empty = hold (-1). Unknown ids also hold.</summary>
            public string AppearanceId;
            public FixedList512Bytes<float2> Deform;
            public bool HasColor;
            public float4 Color;
            public bool HasDrawOrder;
            public int DrawOrder;
            public float4 Curve;
            /// <summary>Channels this key does NOT hold. 0 (default) = all.</summary>
            public byte SkipChannels;
            /// <summary>Per-channel Bezier handles (position Y, rotation, scale X, scale Y); X uses <see cref="Curve"/>.</summary>
            public bool SeparateCurves;
            public float4 CurveY;
            public float4 CurveRotation;
            public float4 CurveScaleX;
            public float4 CurveScaleY;
            public bool HasClipActive;
            public bool ClipActive;
        }

        public struct TrackInput
        {
            public string SlotId;
            /// <summary>0 = pose (default), 1 = appearance channel.</summary>
            public byte Kind;
            public KeyInput[] Keys;
        }

        public struct ClipInput
        {
            public string Name;
            public string ClipId;
            public float Duration;
            public float SpeedMultiplier;
            public byte WrapMode;
            public TrackInput[] Tracks;
            public EventInput[] Events;
            public ValueTrackInput[] ValueTracks;
        }

        public struct ValueTrackInput
        {
            public byte Kind;
            /// <summary>The IK, jiggle or parameter name.</summary>
            public string Target;
            public ValueKeyInput[] Keys;
        }

        public struct ValueKeyInput
        {
            public float Time;
            public float Value;
            public byte EaseMode;
            public float4 Curve;
        }

        public struct EventInput
        {
            public float Time;
            public byte Id;
            public int IntPayload;
            public float FloatPayload;
            public string TextPayload;
            /// <summary>-1 = no sound. Index 0 counts only with a Volume above 0, so a default input stays silent.</summary>
            public int AudioIndex;
            public float Volume;
            public float Balance;
        }

        public struct SkinBindingInput
        {
            public string SlotId;
            public string AppearanceId;
        }

        public struct IkInput
        {
            /// <summary>Clips key its Mix and bend by this name.</summary>
            public string Name;
            public string EffectorSlotId;
            public string TargetSlotId;
            public int ChainLength;
            public bool BendPositive;
            public float Mix;
        }

        public struct TransformInput
        {
            /// <summary>Clips key its overall mix by this name.</summary>
            public string Name;
            public string TargetSlotId;
            public string[] BoneSlotIds;
            public float MixRotate;
            public float MixX;
            public float MixY;
            public float MixScaleX;
            public float MixScaleY;
            public float OffsetRotation;
            public float2 OffsetPosition;
            public float2 OffsetScale;
            public bool Local;
            public bool Relative;
        }

        public struct PathInput
        {
            /// <summary>Clips key its position and overall mix by this name.</summary>
            public string Name;
            public string PathSlotId;
            public string[] BoneSlotIds;
            public float Position;
            public float Spacing;
            public byte SpacingMode;
            public byte RotateMode;
            public float OffsetRotation;
            public float MixRotate;
            public float MixTranslate;
        }

        public struct JiggleInput
        {
            /// <summary>Clips key its Mix by this name (every joint of a chain shares it).</summary>
            public string Name;
            public string SlotId;
            /// <summary>Where the swinging tip sits in the joint's own space (its child, or along the bone).</summary>
            public float2 TipLocal;
            /// <summary>0..1 (loose .. stiff).</summary>
            public float Stiffness;
            /// <summary>0..1 (bouncy .. critically damped).</summary>
            public float Damping;
            public float Gravity;
            public float Mix;
        }

        public struct ParamInput
        {
            public string Name;
            public string ClipId;
            public float Min;
            public float Max;
            public float Default;
            public bool Additive;
        }

        /// <summary>Clip transitions: mix times, part masks, blend spaces.</summary>
        public sealed class TransitionsInput
        {
            public MixInput[] Mixes;
            public float DefaultMix;
            public byte DefaultMixEase;
            public bool FadeOutEvents;
            public MaskInput[] Masks;
            public BlendSpaceInput[] BlendSpaces;
        }

        public struct MixInput
        {
            public string FromClipId;
            public string ToClipId;
            public float Duration;
            public byte Ease;
        }

        public struct MaskInput
        {
            public string Name;
            public string[] SlotIds;
        }

        public struct BlendSpaceInput
        {
            public string Name;
            public string[] ClipIds;
            public float[] Values;
        }

        public struct SkinInput
        {
            public string SkinId;
            public SkinBindingInput[] Bindings;
        }

        public static BlobAssetReference<SpritePartsSetBlob> Build(
            Allocator allocator,
            SlotInput[] slots,
            AppearanceInput[] appearances,
            ClipInput[] clips,
            SkinInput[] skins,
            IkInput[] ik = null,
            JiggleInput[] jiggles = null,
            ParamInput[] parameters = null,
            TransitionsInput transitions = null,
            TransformInput[] transforms = null,
            PathInput[] paths = null)
        {
            if (slots == null || slots.Length == 0)
                throw new ArgumentException("Parts set requires at least one slot.");
            if (slots.Length > SpritePartIdUtility.MaxParts)
                throw new ArgumentException($"Parts set exceeds max {SpritePartIdUtility.MaxParts} slots.");

            var slotIndex = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < slots.Length; i++)
            {
                string id = SpritePartIdUtility.Canonical(slots[i].SlotId, slots[i].Name);
                if (slotIndex.ContainsKey(id))
                    throw new ArgumentException($"Duplicate SlotId '{id}'.");
                slotIndex[id] = i;
            }

            var appIndex = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
            appearances ??= Array.Empty<AppearanceInput>();
            for (int i = 0; i < appearances.Length; i++)
            {
                string id = SpritePartIdUtility.Canonical(appearances[i].AppearanceId);
                if (appIndex.ContainsKey(id))
                    throw new ArgumentException($"Duplicate AppearanceId '{id}'.");
                appIndex[id] = i;
            }

            var builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref var root = ref builder.ConstructRoot<SpritePartsSetBlob>();
                var slotArr = builder.Allocate(ref root.Slots, slots.Length);
                for (int i = 0; i < slots.Length; i++)
                {
                    var src = slots[i];
                    string id = SpritePartIdUtility.Canonical(src.SlotId, src.Name);
                    int parent = -1;
                    if (!string.IsNullOrWhiteSpace(src.ParentSlotId))
                    {
                        string pid = SpritePartIdUtility.Canonical(src.ParentSlotId);
                        if (!slotIndex.TryGetValue(pid, out parent))
                            throw new ArgumentException($"Missing parent '{pid}' for slot '{id}'.");
                    }
                    int defaultApp = -1;
                    if (!string.IsNullOrWhiteSpace(src.DefaultAppearanceId))
                    {
                        string aid = SpritePartIdUtility.Canonical(src.DefaultAppearanceId);
                        if (!appIndex.TryGetValue(aid, out defaultApp))
                            throw new ArgumentException($"Missing default appearance '{aid}' for slot '{id}'.");
                    }

                    slotArr[i] = new SpritePartSlotBlob
                    {
                        Name = Truncate64(src.Name),
                        SlotId = Truncate64(id),
                        SlotIdHash = SpritePartIdUtility.Hash(id),
                        ParentSlotIndex = parent,
                        RestPosition = src.RestPosition,
                        RestRotation = src.RestRotation,
                        RestScale = src.RestScale.x == 0f && src.RestScale.y == 0f
                            ? new float2(1f, 1f)
                            : src.RestScale,
                        DefaultAppearanceIndex = defaultApp,
                        DrawRank = src.DrawRank,
                        Hidden = src.Hidden,
                        Mesh = src.Mesh,
                    };
                }

                WriteSkins(ref builder, slotArr, slots, slotIndex);

                var appArr = builder.Allocate(ref root.Appearances, appearances.Length);
                for (int i = 0; i < appearances.Length; i++)
                {
                    var src = appearances[i];
                    string id = SpritePartIdUtility.Canonical(src.AppearanceId);
                    appArr[i] = new SpritePartAppearanceBlob
                    {
                        AppearanceId = Truncate64(id),
                        AppearanceIdHash = SpritePartIdUtility.Hash(id),
                        SheetTableIndex = src.SheetTableIndex,
                        CellIndex = src.CellIndex,
                        LogicalWorldSize = src.LogicalWorldSize,
                        Pivot = src.Pivot,
                        FrameOffset = src.FrameOffset,
                        FrameScale = src.FrameScale,
                    };
                }

                clips ??= Array.Empty<ClipInput>();
                var clipArr = builder.Allocate(ref root.Clips, clips.Length);
                for (int ci = 0; ci < clips.Length; ci++)
                {
                    var src = clips[ci];
                    string clipId = SpritePartIdUtility.Canonical(src.ClipId, src.Name);
                    string name = string.IsNullOrEmpty(src.Name) ? clipId : src.Name;
                    float duration = math.max(1e-3f, src.Duration);
                    byte wrap = src.WrapMode == (byte)SpritePartsWrap.Once
                        ? (byte)SpritePartsWrap.Once
                        : (byte)SpritePartsWrap.Loop;

                    ref var clip = ref clipArr[ci];
                    clip.Name = Truncate64(name);
                    clip.ClipId = Truncate64(clipId);
                    clip.NameHash = SpriteAnimSetBuilder.Fnv(name);
                    clip.ClipIdHash = SpritePartIdUtility.Hash(clipId);
                    clip.Duration = duration;
                    clip.SpeedMultiplier = math.isfinite(src.SpeedMultiplier) ? src.SpeedMultiplier : 1f;
                    clip.WrapMode = wrap;

                    var events = new System.Collections.Generic.List<SpritePartsEventBlob>();
                    foreach (var ev in src.Events ?? Array.Empty<EventInput>())
                    {
                        if (ev.Id == 0 || !math.isfinite(ev.Time))
                            continue;
                        events.Add(new SpritePartsEventBlob
                        {
                            Time = math.clamp(ev.Time, 0f, duration),
                            Id = ev.Id,
                            IntPayload = ev.IntPayload,
                            FloatPayload = ev.FloatPayload,
                            TextHash = string.IsNullOrEmpty(ev.TextPayload) ? 0UL : SpriteAnimSetBuilder.Fnv(ev.TextPayload),
                            AudioIndex = ev.AudioIndex > 0 || (ev.AudioIndex == 0 && ev.Volume > 0f) ? ev.AudioIndex : -1,
                            Volume = math.saturate(ev.Volume),
                            Balance = math.clamp(ev.Balance, -1f, 1f),
                        });
                    }
                    events.Sort((a, b) => a.Time.CompareTo(b.Time));
                    var eventArr = builder.Allocate(ref clip.Events, events.Count);
                    for (int e = 0; e < events.Count; e++)
                        eventArr[e] = events[e];

                    var tracks = src.Tracks ?? Array.Empty<TrackInput>();
                    // One pose and one appearance track per slot at most. Pose
                    // tracks are written first so SlotTrackIndices never points
                    // at an appearance track.
                    var usedPose = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                    var usedAppearance = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                    int trackCount = 0;
                    for (int t = 0; t < tracks.Length; t++)
                    {
                        string sid = SpritePartIdUtility.Canonical(tracks[t].SlotId);
                        if (!slotIndex.ContainsKey(sid))
                            continue;
                        bool isAppearance = tracks[t].Kind == (byte)SpritePartsTrackKind.Appearance;
                        var used = isAppearance ? usedAppearance : usedPose;
                        if (!used.Add(sid))
                            throw new ArgumentException(
                                $"Clip '{name}' duplicate {(isAppearance ? "appearance" : "pose")} track for '{sid}'.");
                        trackCount++;
                    }

                    var trackArr = builder.Allocate(ref clip.Tracks, trackCount);
                    var dense = builder.Allocate(ref clip.SlotTrackIndices, slots.Length);
                    var denseAppearance = builder.Allocate(ref clip.SlotAppearanceTrackIndices, slots.Length);
                    for (int s = 0; s < slots.Length; s++)
                    {
                        dense[s] = -1;
                        denseAppearance[s] = -1;
                    }

                    int write = 0;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        bool appearancePass = pass == 1;
                        for (int t = 0; t < tracks.Length; t++)
                        {
                            var tr = tracks[t];
                            bool isAppearance = tr.Kind == (byte)SpritePartsTrackKind.Appearance;
                            if (isAppearance != appearancePass) continue;
                            string sid = SpritePartIdUtility.Canonical(tr.SlotId);
                            if (!slotIndex.TryGetValue(sid, out int sIndex))
                                continue;
                            if (isAppearance)
                                denseAppearance[sIndex] = write;
                            else
                                dense[sIndex] = write;

                            var keys = NormalizeKeys(tr.Keys, duration, appIndex,
                                skipPoseValidation: isAppearance);
                            var keyArr = builder.Allocate(ref trackArr[write].Keys, keys.Length);
                            for (int k = 0; k < keys.Length; k++)
                                keyArr[k] = keys[k];

                            trackArr[write].SlotIndex = sIndex;
                            trackArr[write].SlotIdHash = SpritePartIdUtility.Hash(sid);
                            write++;
                        }
                    }
                }

                skins ??= Array.Empty<SkinInput>();
                var skinArr = builder.Allocate(ref root.SkinPatches, skins.Length);
                for (int si = 0; si < skins.Length; si++)
                {
                    var src = skins[si];
                    string skinId = SpritePartIdUtility.Canonical(src.SkinId);
                    ref var skin = ref skinArr[si];
                    skin.SkinId = Truncate64(skinId);
                    skin.SkinIdHash = SpritePartIdUtility.Hash(skinId);

                    var bindings = src.Bindings ?? Array.Empty<SkinBindingInput>();
                    int count = 0;
                    for (int b = 0; b < bindings.Length; b++)
                    {
                        string sid = SpritePartIdUtility.Canonical(bindings[b].SlotId);
                        string aid = SpritePartIdUtility.Canonical(bindings[b].AppearanceId);
                        if (slotIndex.ContainsKey(sid) && appIndex.ContainsKey(aid))
                            count++;
                    }
                    var bindArr = builder.Allocate(ref skin.Bindings, count);
                    int w = 0;
                    for (int b = 0; b < bindings.Length; b++)
                    {
                        string sid = SpritePartIdUtility.Canonical(bindings[b].SlotId);
                        string aid = SpritePartIdUtility.Canonical(bindings[b].AppearanceId);
                        if (!slotIndex.TryGetValue(sid, out int sIdx)) continue;
                        if (!appIndex.TryGetValue(aid, out int aIdx)) continue;
                        bindArr[w++] = new SpritePartsSkinBindingBlob
                        {
                            SlotIndex = sIdx,
                            AppearanceIndex = aIdx,
                        };
                    }
                }

                // Names some clip keys: a constraint set to Mix 0 stays when a clip turns it on.
                var keyedNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                foreach (var clipIn in clips)
                    foreach (var vt in clipIn.ValueTracks ?? Array.Empty<ValueTrackInput>())
                        if (vt.Keys != null && vt.Keys.Length > 0)
                            keyedNames.Add(vt.Kind + ":" + (vt.Target ?? string.Empty).Trim());

                // IK: effector -> its parent (lower) -> grandparent (upper, chain 2). Unresolvable ones are dropped.
                var resolved = new System.Collections.Generic.List<SpritePartsIkBlob>();
                var ikNames = new System.Collections.Generic.List<string>();
                foreach (var c in ik ?? Array.Empty<IkInput>())
                {
                    if (!slotIndex.TryGetValue(SpritePartIdUtility.Canonical(c.EffectorSlotId ?? string.Empty), out int eff)
                        || !slotIndex.TryGetValue(SpritePartIdUtility.Canonical(c.TargetSlotId ?? string.Empty), out int tgt))
                        continue;
                    int lower = ParentIndex(slots, slotIndex, eff);
                    if (lower < 0 || tgt == eff)
                        continue;
                    int upper = c.ChainLength >= 2 ? ParentIndex(slots, slotIndex, lower) : -1;
                    resolved.Add(new SpritePartsIkBlob
                    {
                        Effector = eff,
                        Lower = lower,
                        Upper = upper,
                        Target = tgt,
                        BendSign = c.BendPositive ? 1f : -1f,
                        Mix = math.saturate(c.Mix),
                    });
                    ikNames.Add((c.Name ?? string.Empty).Trim());
                }
                var ikArr = builder.Allocate(ref root.IkConstraints, resolved.Count);
                for (int k = 0; k < resolved.Count; k++)
                    ikArr[k] = resolved[k];

                // Jiggle: unknown slots and zero-length tips are dropped; parents solve before children.
                var springs = new System.Collections.Generic.List<(int depth, SpritePartsJiggleBlob blob, string name)>();
                foreach (var j in jiggles ?? Array.Empty<JiggleInput>())
                {
                    string jName = (j.Name ?? string.Empty).Trim();
                    bool keyedMix = keyedNames.Contains((byte)SpritePartsValueKind.JiggleMix + ":" + jName);
                    if ((j.Mix <= 0f && !keyedMix) || math.lengthsq(j.TipLocal) < 1e-10f
                        || !slotIndex.TryGetValue(SpritePartIdUtility.Canonical(j.SlotId ?? string.Empty), out int s))
                        continue;
                    int depth = 0;
                    for (int p = ParentIndex(slots, slotIndex, s); p >= 0 && depth < 256; p = ParentIndex(slots, slotIndex, p))
                        depth++;
                    float stiff = math.saturate(j.Stiffness);
                    float spring = math.lerp(15f, 600f, stiff * stiff);
                    springs.Add((depth, new SpritePartsJiggleBlob
                    {
                        Slot = s,
                        TipLocal = j.TipLocal,
                        Spring = spring,
                        Damping = 2f * math.sqrt(spring) * math.lerp(0.05f, 1f, math.saturate(j.Damping)),
                        Gravity = j.Gravity,
                        Mix = math.saturate(j.Mix),
                    }, jName));
                }
                springs.Sort((a, b) => a.depth != b.depth ? a.depth.CompareTo(b.depth) : a.blob.Slot.CompareTo(b.blob.Slot));
                var jiggleArr = builder.Allocate(ref root.Jiggles, springs.Count);
                for (int k = 0; k < springs.Count; k++)
                    jiggleArr[k] = springs[k].blob;

                // Parameters: the clip is found by id; one with no clip (or an empty range) is kept but does nothing.
                parameters ??= Array.Empty<ParamInput>();
                var paramArr = builder.Allocate(ref root.Params, parameters.Length);
                for (int k = 0; k < parameters.Length; k++)
                {
                    var pin = parameters[k];
                    int clipIndex = -1;
                    string cid = SpritePartIdUtility.Canonical(pin.ClipId ?? string.Empty);
                    for (int c = 0; c < clips.Length && clipIndex < 0 && cid.Length > 0; c++)
                    {
                        if (SpritePartIdUtility.Canonical(clips[c].ClipId ?? string.Empty) == cid)
                            clipIndex = c;
                    }
                    paramArr[k] = new SpritePartsParamBlob
                    {
                        Name = Truncate64(pin.Name),
                        ClipIndex = math.abs(pin.Max - pin.Min) > 1e-6f ? clipIndex : -1,
                        Min = pin.Min,
                        Max = pin.Max,
                        Default = pin.Default,
                        Additive = pin.Additive ? (byte)1 : (byte)0,
                    };
                }

                // Transform and path constraints: unknown slots are dropped; bones solve parents first.
                int Depth(int s)
                {
                    int d = 0;
                    for (int p = ParentIndex(slots, slotIndex, s); p >= 0 && d < 256; p = ParentIndex(slots, slotIndex, p))
                        d++;
                    return d;
                }
                System.Collections.Generic.List<int> Bones(string[] ids, int exclude, bool byDepth)
                {
                    var list = new System.Collections.Generic.List<int>();
                    foreach (string id in ids ?? Array.Empty<string>())
                        if (slotIndex.TryGetValue(SpritePartIdUtility.Canonical(id ?? string.Empty), out int s) && s != exclude && !list.Contains(s))
                            list.Add(s);
                    if (byDepth)
                        list.Sort((a, b) => Depth(a) != Depth(b) ? Depth(a).CompareTo(Depth(b)) : a.CompareTo(b));
                    return list;
                }
                var transformNames = new System.Collections.Generic.List<string>();
                var transformList = new System.Collections.Generic.List<(TransformInput input, int target, System.Collections.Generic.List<int> bones)>();
                foreach (var t in transforms ?? Array.Empty<TransformInput>())
                {
                    if (!slotIndex.TryGetValue(SpritePartIdUtility.Canonical(t.TargetSlotId ?? string.Empty), out int target))
                        continue;
                    var bones = Bones(t.BoneSlotIds, target, true);
                    if (bones.Count == 0)
                        continue;
                    transformList.Add((t, target, bones));
                    transformNames.Add((t.Name ?? string.Empty).Trim());
                }
                var transformArr = builder.Allocate(ref root.TransformConstraints, transformList.Count);
                for (int k = 0; k < transformList.Count; k++)
                {
                    var (t, target, bones) = transformList[k];
                    ref var tc = ref transformArr[k];
                    tc.Target = target;
                    var boneArr = builder.Allocate(ref tc.Bones, bones.Count);
                    for (int b = 0; b < bones.Count; b++)
                        boneArr[b] = bones[b];
                    tc.MixRotate = math.saturate(t.MixRotate);
                    tc.MixX = math.saturate(t.MixX);
                    tc.MixY = math.saturate(t.MixY);
                    tc.MixScaleX = math.saturate(t.MixScaleX);
                    tc.MixScaleY = math.saturate(t.MixScaleY);
                    tc.OffsetRotation = math.isfinite(t.OffsetRotation) ? t.OffsetRotation : 0f;
                    tc.OffsetPosition = t.OffsetPosition;
                    tc.OffsetScale = t.OffsetScale;
                    tc.Local = t.Local ? (byte)1 : (byte)0;
                    tc.Relative = t.Relative ? (byte)1 : (byte)0;
                }
                var pathNames = new System.Collections.Generic.List<string>();
                var pathList = new System.Collections.Generic.List<(PathInput input, int path, System.Collections.Generic.List<int> bones)>();
                foreach (var p in paths ?? Array.Empty<PathInput>())
                {
                    if (!slotIndex.TryGetValue(SpritePartIdUtility.Canonical(p.PathSlotId ?? string.Empty), out int path)
                        || !slots[path].IsPath || slots[path].PathPoints == null || slots[path].PathPoints.Length < 2)
                        continue;
                    var bones = Bones(p.BoneSlotIds, path, false);
                    if (bones.Count == 0)
                        continue;
                    pathList.Add((p, path, bones));
                    pathNames.Add((p.Name ?? string.Empty).Trim());
                }
                var pathArr = builder.Allocate(ref root.PathConstraints, pathList.Count);
                for (int k = 0; k < pathList.Count; k++)
                {
                    var (p, path, bones) = pathList[k];
                    ref var pc = ref pathArr[k];
                    pc.PathSlot = path;
                    var boneArr = builder.Allocate(ref pc.Bones, bones.Count);
                    for (int b = 0; b < bones.Count; b++)
                        boneArr[b] = bones[b];
                    pc.Position = math.isfinite(p.Position) ? p.Position : 0f;
                    pc.Spacing = math.isfinite(p.Spacing) ? p.Spacing : 0f;
                    pc.SpacingMode = p.SpacingMode;
                    pc.RotateMode = p.RotateMode;
                    pc.OffsetRotation = math.isfinite(p.OffsetRotation) ? p.OffsetRotation : 0f;
                    pc.MixRotate = math.saturate(p.MixRotate);
                    pc.MixTranslate = math.saturate(p.MixTranslate);
                }

                // Keyed values: each track drives every IK / jiggle / parameter with its name.
                for (int ci = 0; ci < clips.Length; ci++)
                {
                    var tracksIn = clips[ci].ValueTracks ?? Array.Empty<ValueTrackInput>();
                    float clipDuration = math.max(1e-3f, clips[ci].Duration);
                    var kept = new System.Collections.Generic.List<(ValueTrackInput input, System.Collections.Generic.List<int> targets)>();
                    foreach (var vt in tracksIn)
                    {
                        if (vt.Keys == null || vt.Keys.Length == 0)
                            continue;
                        string target = (vt.Target ?? string.Empty).Trim();
                        var targets = new System.Collections.Generic.List<int>();
                        switch ((SpritePartsValueKind)vt.Kind)
                        {
                            case SpritePartsValueKind.IkMix:
                            case SpritePartsValueKind.IkBend:
                                for (int k = 0; k < ikNames.Count; k++)
                                    if (ikNames[k] == target)
                                        targets.Add(k);
                                break;
                            case SpritePartsValueKind.JiggleMix:
                                for (int k = 0; k < springs.Count; k++)
                                    if (springs[k].name == target)
                                        targets.Add(k);
                                break;
                            case SpritePartsValueKind.Param:
                                for (int k = 0; k < parameters.Length; k++)
                                    if ((parameters[k].Name ?? string.Empty).Trim() == target)
                                        targets.Add(k);
                                break;
                            case SpritePartsValueKind.TransformMix:
                                for (int k = 0; k < transformNames.Count; k++)
                                    if (transformNames[k] == target)
                                        targets.Add(k);
                                break;
                            case SpritePartsValueKind.PathPosition:
                            case SpritePartsValueKind.PathMix:
                                for (int k = 0; k < pathNames.Count; k++)
                                    if (pathNames[k] == target)
                                        targets.Add(k);
                                break;
                        }
                        if (targets.Count > 0)
                            kept.Add((vt, targets));
                    }
                    var valueArr = builder.Allocate(ref clipArr[ci].ValueTracks, kept.Count);
                    for (int v = 0; v < kept.Count; v++)
                    {
                        valueArr[v].Kind = kept[v].input.Kind;
                        var targetArr = builder.Allocate(ref valueArr[v].Targets, kept[v].targets.Count);
                        for (int k = 0; k < kept[v].targets.Count; k++)
                            targetArr[k] = kept[v].targets[k];
                        var keys = new System.Collections.Generic.List<SpritePartsValueKeyBlob>();
                        foreach (var key in kept[v].input.Keys)
                        {
                            if (!math.isfinite(key.Time) || !math.isfinite(key.Value))
                                continue;
                            keys.Add(new SpritePartsValueKeyBlob
                            {
                                Time = math.clamp(key.Time, 0f, clipDuration),
                                Value = key.Value,
                                EaseMode = SpriteEase.IsValidMode(key.EaseMode) ? key.EaseMode : (byte)SpriteEaseMode.Linear,
                                Curve = key.Curve,
                            });
                        }
                        keys.Sort((a, b) => a.Time.CompareTo(b.Time));
                        var keyArr = builder.Allocate(ref valueArr[v].Keys, keys.Count);
                        for (int k = 0; k < keys.Count; k++)
                            keyArr[k] = keys[k];
                    }
                }

                // Transitions: mix table, masks, blend spaces (clips found by id; unknown ones dropped).
                int ClipByIdIndex(string id)
                {
                    string cid = SpritePartIdUtility.Canonical(id ?? string.Empty);
                    for (int c = 0; cid.Length > 0 && c < clips.Length; c++)
                        if (SpritePartIdUtility.Canonical(clips[c].ClipId ?? string.Empty) == cid)
                            return c;
                    return -1;
                }
                var transIn = transitions ?? new TransitionsInput();
                root.DefaultMix = math.max(0f, math.isfinite(transIn.DefaultMix) ? transIn.DefaultMix : 0f);
                root.DefaultMixEase = SpriteEase.IsValidMode(transIn.DefaultMixEase) ? transIn.DefaultMixEase : (byte)SpriteEaseMode.Linear;
                root.FadeOutEvents = transIn.FadeOutEvents ? (byte)1 : (byte)0;
                var mixes = new System.Collections.Generic.List<SpritePartsMixBlob>();
                foreach (var m in transIn.Mixes ?? Array.Empty<MixInput>())
                {
                    int to = ClipByIdIndex(m.ToClipId);
                    int from = string.IsNullOrWhiteSpace(m.FromClipId) ? -1 : ClipByIdIndex(m.FromClipId);
                    if (to < 0 || (!string.IsNullOrWhiteSpace(m.FromClipId) && from < 0))
                        continue;
                    mixes.Add(new SpritePartsMixBlob
                    {
                        From = from, To = to, Duration = math.max(0f, m.Duration),
                        Ease = SpriteEase.IsValidMode(m.Ease) ? m.Ease : (byte)SpriteEaseMode.Linear,
                    });
                }
                var mixArr = builder.Allocate(ref root.Mixes, mixes.Count);
                for (int k = 0; k < mixes.Count; k++)
                    mixArr[k] = mixes[k];
                var masks = transIn.Masks ?? Array.Empty<MaskInput>();
                var maskArr = builder.Allocate(ref root.Masks, masks.Length);
                for (int k = 0; k < masks.Length; k++)
                {
                    uint bits = 0;
                    foreach (string sid in masks[k].SlotIds ?? Array.Empty<string>())
                        if (slotIndex.TryGetValue(SpritePartIdUtility.Canonical(sid ?? string.Empty), out int si) && si < 32)
                            bits |= 1u << si;
                    maskArr[k] = new SpritePartsMaskBlob { Name = Truncate64(masks[k].Name), Bits = bits };
                }
                var spaces = transIn.BlendSpaces ?? Array.Empty<BlendSpaceInput>();
                var spaceArr = builder.Allocate(ref root.BlendSpaces, spaces.Length);
                for (int k = 0; k < spaces.Length; k++)
                {
                    spaceArr[k].Name = Truncate64(spaces[k].Name);
                    var pts = new System.Collections.Generic.List<SpritePartsBlendPointBlob>();
                    var ids = spaces[k].ClipIds ?? Array.Empty<string>();
                    for (int q = 0; q < ids.Length; q++)
                    {
                        int ci = ClipByIdIndex(ids[q]);
                        float v = spaces[k].Values != null && q < spaces[k].Values.Length ? spaces[k].Values[q] : q;
                        if (ci >= 0 && math.isfinite(v))
                            pts.Add(new SpritePartsBlendPointBlob { ClipIndex = ci, Value = v });
                    }
                    pts.Sort((a, b) => a.Value.CompareTo(b.Value));
                    var ptArr = builder.Allocate(ref spaceArr[k].Points, pts.Count);
                    for (int q = 0; q < pts.Count; q++)
                        ptArr[q] = pts[q];
                }

                return builder.CreateBlobAssetReference<SpritePartsSetBlob>(allocator);
            }
            finally
            {
                builder.Dispose();
            }
        }

        static SpritePartsKeyBlob[] NormalizeKeys(
            KeyInput[] keys,
            float duration,
            System.Collections.Generic.Dictionary<string, int> appIndex,
            bool skipPoseValidation = false)
        {
            if (keys == null || keys.Length == 0)
                return Array.Empty<SpritePartsKeyBlob>();

            var list = new System.Collections.Generic.List<(int index, SpritePartsKeyBlob key)>(keys.Length);
            for (int i = 0; i < keys.Length; i++)
            {
                var k = keys[i];
                if (!math.isfinite(k.Time) || k.Time < 0f || k.Time > duration + 1e-5f)
                    throw new ArgumentException($"Key time {k.Time} outside [0, {duration}].");
                if (!skipPoseValidation &&
                    (math.abs(k.Scale.x) < 1e-5f || math.abs(k.Scale.y) < 1e-5f))
                    throw new ArgumentException("Key scale axes must be non-zero.");
                byte ease = SpriteEase.IsValidMode(k.EaseMode) ? k.EaseMode : (byte)SpriteEaseMode.Linear;
                int appearanceIndex = -1;
                if (!string.IsNullOrWhiteSpace(k.AppearanceId))
                {
                    string aid = SpritePartIdUtility.Canonical(k.AppearanceId);
                    if (appIndex == null || !appIndex.TryGetValue(aid, out appearanceIndex))
                        appearanceIndex = -1;
                }
                list.Add((i, new SpritePartsKeyBlob
                {
                    Time = math.clamp(k.Time, 0f, duration),
                    Position = k.Position,
                    Rotation = k.Rotation,
                    Scale = k.Scale,
                    EaseMode = ease,
                    AppearanceIndex = appearanceIndex,
                    Deform = k.Deform,
                    HasColor = (byte)(k.HasColor ? 1 : 0),
                    Color = k.HasColor ? k.Color : new float4(1f),
                    HasDrawOrder = (byte)(k.HasDrawOrder ? 1 : 0),
                    DrawOrder = k.DrawOrder,
                    Curve = math.all(k.Curve == float4.zero) ? new float4(0.33f, 0f, 0.67f, 1f) : k.Curve,
                    SkipChannels = (byte)(k.SkipChannels & (byte)SpritePartsKeyChannel.All),
                    SeparateCurves = (byte)(k.SeparateCurves ? 1 : 0),
                    CurveY = k.CurveY,
                    CurveRotation = k.CurveRotation,
                    CurveScaleX = k.CurveScaleX,
                    CurveScaleY = k.CurveScaleY,
                    HasClipActive = (byte)(k.HasClipActive ? 1 : 0),
                    ClipActive = (byte)(k.ClipActive ? 1 : 0),
                }));
            }
            list.Sort((a, b) =>
            {
                int cmp = a.key.Time.CompareTo(b.key.Time);
                return cmp != 0 ? cmp : a.index.CompareTo(b.index);
            });
            // Duplicate times: last writer wins (stable by original index).
            var unique = new System.Collections.Generic.List<SpritePartsKeyBlob>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                if (unique.Count > 0 &&
                    math.abs(unique[unique.Count - 1].Time - list[i].key.Time) <= 1e-6f)
                    unique[unique.Count - 1] = list[i].key;
                else
                    unique.Add(list[i].key);
            }
            return unique.ToArray();
        }

        /// <summary>
        /// Bind pose (rest matrices) for every slot, and bone indices + weights for weighted meshes.
        /// A mesh whose weights do not match its vertices, or name an unknown bone, stays unweighted.
        /// </summary>
        static void WriteSkins(
            ref BlobBuilder builder, BlobBuilderArray<SpritePartSlotBlob> slotArr, SlotInput[] slots,
            System.Collections.Generic.Dictionary<string, int> slotIndex)
        {
            int n = slots.Length;
            var rest = new float4x4[n];
            var state = new byte[n]; // 0 = todo, 1 = visiting, 2 = done
            for (int i = 0; i < n; i++)
                slotArr[i].RestToRoot = RestToRoot(slotArr, rest, state, i);

            for (int i = 0; i < n; i++)
            {
                var src = slots[i];
                slotArr[i].SkinQuadSize = src.SkinQuadSize;
                slotArr[i].SkinQuadPivot = src.SkinQuadPivot;
                slotArr[i].ClipMaskIndex = -1;
                if (!string.IsNullOrWhiteSpace(src.ClipMaskSlotId)
                    && slotIndex.TryGetValue(SpritePartIdUtility.Canonical(src.ClipMaskSlotId), out int maskIndex) && maskIndex != i)
                    slotArr[i].ClipMaskIndex = maskIndex;
                int vertices = slotArr[i].Mesh.PointCount;
                int bones = src.SkinBones?.Length ?? 0;
                bool ok = bones > 0 && vertices >= 3
                    && src.SkinWeights != null && src.SkinWeights.Length == vertices * bones
                    && src.SkinQuadSize.x > 1e-6f && src.SkinQuadSize.y > 1e-6f;
                var indices = new int[bones];
                for (int b = 0; ok && b < bones; b++)
                    ok = slotIndex.TryGetValue(SpritePartIdUtility.Canonical(src.SkinBones[b]), out indices[b]);
                if (!ok)
                    continue;
                var boneArr = builder.Allocate(ref slotArr[i].SkinBones, bones);
                for (int b = 0; b < bones; b++)
                    boneArr[b] = indices[b];
                var weightArr = builder.Allocate(ref slotArr[i].SkinWeights, src.SkinWeights.Length);
                for (int w = 0; w < src.SkinWeights.Length; w++)
                    weightArr[w] = math.isfinite(src.SkinWeights[w]) ? math.max(0f, src.SkinWeights[w]) : 0f;
            }

            // Clip shapes: their polygon, end slot, and convex pieces.
            for (int i = 0; i < n; i++)
            {
                var src = slots[i];
                slotArr[i].ClipEndIndex = -1;
                if (!src.IsClipShape)
                    continue;
                slotArr[i].IsClipShape = 1;
                if (!string.IsNullOrWhiteSpace(src.ClipEndSlotId)
                    && slotIndex.TryGetValue(SpritePartIdUtility.Canonical(src.ClipEndSlotId), out int end) && end != i)
                    slotArr[i].ClipEndIndex = end;
                var poly = src.ClipPolygon ?? Array.Empty<float2>();
                if (poly.Length < 3 || poly.Length > SpritePartsLattice.MaxVertices)
                    continue;
                var polyArr = builder.Allocate(ref slotArr[i].ClipPolygon, poly.Length);
                var verts = new UnityEngine.Vector2[poly.Length];
                for (int k = 0; k < poly.Length; k++)
                {
                    polyArr[k] = poly[k];
                    verts[k] = new UnityEngine.Vector2(poly[k].x, poly[k].y);
                }
                var tris = SpritePartsMeshOps.Triangulate(verts, verts.Length, null);
                if (tris == null)
                    continue; // the outline crosses itself: the shape clips nothing
                var lattice = new SpritePartsLattice();
                foreach (var v in poly)
                    lattice.Points.Add(v);
                foreach (int t in tris)
                    lattice.Indices.Add((byte)t);
                var flatPieces = SpritePartsClipping.ConvexPieces(ref lattice);
                var shapePieces = builder.Allocate(ref slotArr[i].MaskPieces, flatPieces.Count);
                for (int k = 0; k < flatPieces.Count; k++)
                    shapePieces[k] = flatPieces[k];
            }

            // Paths: their points (a smooth curve through them at runtime).
            for (int i = 0; i < n; i++)
            {
                var src = slots[i];
                if (!src.IsPath || src.PathPoints == null || src.PathPoints.Length < 2)
                    continue;
                slotArr[i].IsPath = 1;
                slotArr[i].PathClosed = src.PathClosed && src.PathPoints.Length >= 3 ? (byte)1 : (byte)0;
                var pathArr = builder.Allocate(ref slotArr[i].PathPoints, src.PathPoints.Length);
                for (int k = 0; k < src.PathPoints.Length; k++)
                    pathArr[k] = src.PathPoints[k];
            }

            // Masks with a mesh: split once into convex pieces for the per-frame clip.
            var isMask = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int m = slotArr[i].ClipMaskIndex;
                if (m >= 0 && m < n)
                    isMask[m] = true;
            }
            for (int i = 0; i < n; i++)
            {
                if (!isMask[i] || !slotArr[i].Mesh.HasMesh || slots[i].IsClipShape)
                    continue;
                var flat = SpritePartsClipping.ConvexPieces(ref slotArr[i].Mesh);
                var pieces = builder.Allocate(ref slotArr[i].MaskPieces, flat.Count);
                for (int k = 0; k < flat.Count; k++)
                    pieces[k] = flat[k];
            }
        }

        static float4x4 RestToRoot(BlobBuilderArray<SpritePartSlotBlob> slotArr, float4x4[] rest, byte[] state, int i)
        {
            if (state[i] == 2)
                return rest[i];
            var local = SpritePartsHierarchy.LocalMatrix(
                slotArr[i].RestPosition, slotArr[i].RestRotation, slotArr[i].RestScale);
            int parent = slotArr[i].ParentSlotIndex;
            state[i] = 1;
            if (parent >= 0 && parent < rest.Length && state[parent] != 1)
                local = math.mul(RestToRoot(slotArr, rest, state, parent), local);
            rest[i] = local;
            state[i] = 2;
            return local;
        }

        static int ParentIndex(SlotInput[] slots, System.Collections.Generic.Dictionary<string, int> slotIndex, int child)
        {
            string pid = slots[child].ParentSlotId;
            if (string.IsNullOrWhiteSpace(pid))
                return -1;
            return slotIndex.TryGetValue(SpritePartIdUtility.Canonical(pid), out int p) ? p : -1;
        }

        static FixedString64Bytes Truncate64(string value)
        {
            value ??= string.Empty;
            if (value.Length <= 61)
                return value;
            return value.Substring(0, 61);
        }
    }
}
