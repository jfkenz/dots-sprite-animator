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
            /// <summary>Empty = hold (-1). Non-empty must exist in Appearances.</summary>
            public string AppearanceId;
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
        }

        public struct SkinBindingInput
        {
            public string SlotId;
            public string AppearanceId;
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
            SkinInput[] skins)
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
                    };
                }

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
                            throw new ArgumentException($"Clip '{name}' track slot '{sid}' missing.");
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
                            int sIndex = slotIndex[sid];
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
                        throw new ArgumentException($"Key appearance '{aid}' is missing from the profile.");
                }
                list.Add((i, new SpritePartsKeyBlob
                {
                    Time = math.clamp(k.Time, 0f, duration),
                    Position = k.Position,
                    Rotation = k.Rotation,
                    Scale = k.Scale,
                    EaseMode = ease,
                    AppearanceIndex = appearanceIndex,
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

        static FixedString64Bytes Truncate64(string value)
        {
            value ??= string.Empty;
            if (value.Length <= 61)
                return value;
            return value.Substring(0, 61);
        }
    }
}
