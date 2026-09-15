using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Converts Parts profile authoring into SpritePartsSetBuilder inputs.
    /// Frame SpriteAnimClipConversion stays frame-only.
    /// </summary>
    public static class SpritePartsClipConversion
    {
        public static bool TryBuildBlob(
            SpriteSheetProfile profile,
            Allocator allocator,
            out BlobAssetReference<SpritePartsSetBlob> blob,
            out string error)
        {
            blob = default;
            error = null;
            if (profile == null)
            {
                error = "Profile is null.";
                return false;
            }

            profile.EnsurePartsRig();
            SpritePartsValidation.CanonicalizeIds(profile);
            var validation = SpritePartsValidation.Validate(profile);
            if (!validation.Ok)
            {
                error = string.Join(" | ", validation.Errors);
                return false;
            }

            try
            {
                var slots = CreateSlots(profile);
                var appearances = CreateAppearances(profile, out error);
                if (appearances == null)
                    return false;
                var clips = CreateClips(profile);
                var skins = CreateSkins(profile);
                blob = SpritePartsSetBuilder.Build(allocator, slots, appearances, clips, skins);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static SpritePartsSetBuilder.SlotInput[] CreateSlots(SpriteSheetProfile profile)
        {
            var list = profile.PartsSlots;
            var result = new SpritePartsSetBuilder.SlotInput[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                result[i] = new SpritePartsSetBuilder.SlotInput
                {
                    Name = s.Name,
                    SlotId = s.SlotId,
                    ParentSlotId = s.ParentSlotId,
                    RestPosition = new float2(s.RestPosition.x, s.RestPosition.y),
                    RestRotation = s.RestRotation,
                    RestScale = new float2(s.RestScale.x, s.RestScale.y),
                    DefaultAppearanceId = s.DefaultAppearanceId,
                    DrawRank = s.DrawRank,
                };
            }
            return result;
        }

        public static SpritePartsSetBuilder.AppearanceInput[] CreateAppearances(
            SpriteSheetProfile profile, out string error)
        {
            error = null;
            var list = profile.PartsAppearances;
            var result = new SpritePartsSetBuilder.AppearanceInput[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var app = list[i];
                if (!SpritePartsGeometry.TryResolve(profile, app, rotatedPacking: false,
                        out var geo, out error))
                    return null;
                result[i] = new SpritePartsSetBuilder.AppearanceInput
                {
                    AppearanceId = app.AppearanceId,
                    SheetTableIndex = geo.SheetIndex,
                    CellIndex = geo.CellIndex,
                    LogicalWorldSize = geo.LogicalWorldSize,
                    Pivot = geo.Pivot,
                    FrameOffset = geo.FrameOffset,
                    FrameScale = geo.FrameScale,
                };
            }
            return result;
        }

        public static SpritePartsSetBuilder.ClipInput[] CreateClips(SpriteSheetProfile profile)
        {
            var list = profile.PartsClips;
            var result = new SpritePartsSetBuilder.ClipInput[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var clip = list[i];
                var tracks = clip.Tracks ?? new List<SpritePartsTrackDef>();
                var trackInputs = new SpritePartsSetBuilder.TrackInput[tracks.Count];
                for (int t = 0; t < tracks.Count; t++)
                {
                    var track = tracks[t];
                    var keys = track.Keys ?? new List<SpritePartsKeyDef>();
                    var keyInputs = new SpritePartsSetBuilder.KeyInput[keys.Count];
                    for (int k = 0; k < keys.Count; k++)
                    {
                        var key = keys[k];
                        keyInputs[k] = new SpritePartsSetBuilder.KeyInput
                        {
                            Time = key.Time,
                            Position = new float2(key.Position.x, key.Position.y),
                            Rotation = key.Rotation,
                            Scale = new float2(key.Scale.x, key.Scale.y),
                            EaseMode = key.EaseMode,
                            AppearanceId = key.AppearanceId,
                        };
                    }
                    trackInputs[t] = new SpritePartsSetBuilder.TrackInput
                    {
                        SlotId = track.SlotId,
                        Keys = keyInputs,
                    };
                }

                result[i] = new SpritePartsSetBuilder.ClipInput
                {
                    Name = clip.Name,
                    ClipId = clip.ClipId,
                    Duration = clip.Duration,
                    SpeedMultiplier = clip.Speed,
                    WrapMode = clip.WrapMode,
                    Tracks = trackInputs,
                };
            }
            return result;
        }

        public static SpritePartsSetBuilder.SkinInput[] CreateSkins(SpriteSheetProfile profile)
        {
            var list = profile.PartsSkins;
            var result = new SpritePartsSetBuilder.SkinInput[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var skin = list[i];
                var bindings = skin.Bindings ?? new List<SpritePartsSkinBindingDef>();
                var bindInputs = new SpritePartsSetBuilder.SkinBindingInput[bindings.Count];
                for (int b = 0; b < bindings.Count; b++)
                {
                    bindInputs[b] = new SpritePartsSetBuilder.SkinBindingInput
                    {
                        SlotId = bindings[b].SlotId,
                        AppearanceId = bindings[b].AppearanceId,
                    };
                }
                result[i] = new SpritePartsSetBuilder.SkinInput
                {
                    SkinId = skin.SkinId,
                    Bindings = bindInputs,
                };
            }
            return result;
        }
        /// <summary>
        /// Pose-only blob for editor preview/onion. Skips appearance geometry validation
        /// so transforms can be evaluated without sheet art. Clears default appearance links.
        /// </summary>
        public static bool TryBuildPoseEvaluationBlob(
            SpriteSheetProfile profile,
            Allocator allocator,
            out BlobAssetReference<SpritePartsSetBlob> blob,
            out string error)
        {
            blob = default;
            error = null;
            if (profile == null)
            {
                error = "Profile is null.";
                return false;
            }

            profile.EnsurePartsRig();
            SpritePartsValidation.CanonicalizeIds(profile);
            if (profile.PartsSlots == null || profile.PartsSlots.Count == 0)
            {
                error = "Parts profile has no slots.";
                return false;
            }

            try
            {
                var slots = CreateSlots(profile);
                for (int i = 0; i < slots.Length; i++)
                    slots[i].DefaultAppearanceId = string.Empty;
                var clips = CreateClips(profile);
                blob = SpritePartsSetBuilder.Build(
                    allocator,
                    slots,
                    System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                    clips,
                    System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}

