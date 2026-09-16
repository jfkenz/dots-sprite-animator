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
            => TryBuildBlob(profile, allocator, out blob, out error, null);

        public static bool TryBuildBlob(
            SpriteSheetProfile profile,
            Allocator allocator,
            out BlobAssetReference<SpritePartsSetBlob> blob,
            out string error,
            SpriteArtLibraryOps.LibraryResolver libraryResolver)
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

            var bakeProfile = SpriteArtLibraryOps.ResolveForBake(profile, out error, libraryResolver);
            if (bakeProfile == null)
                return false;
            if (!ReferenceEquals(bakeProfile, profile))
            {
                bakeProfile.EnsurePartsRig();
                SpritePartsValidation.CanonicalizeIds(bakeProfile);
            }
            var validation = SpritePartsValidation.Validate(bakeProfile);
            if (!validation.Ok)
            {
                error = string.Join(" | ", validation.Errors);
                return false;
            }

            try
            {
                var slots = CreateSlots(bakeProfile);
                var appearances = CreateAppearances(bakeProfile, out error);
                if (appearances == null)
                    return false;
                var clips = CreateClips(bakeProfile);
                var skins = CreateSkins(bakeProfile);
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
            => CreateClips(profile.PartsClips);

        public static SpritePartsSetBuilder.ClipInput[] CreateClips(IReadOnlyList<SpritePartsClipDef> list)
        {
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
                        Kind = (byte)(track.Kind == SpritePartsTrackKind.Appearance
                            ? SpritePartsTrackKind.Appearance
                            : SpritePartsTrackKind.Pose),
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
                BlankKeyAppearanceIdsForPose(clips);
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

        /// <summary>
        /// Pose-only blob for transient clips that are not part of the profile
        /// (import previews). Strictly read-only: unlike the profile overload it
        /// never normalizes or null-coalesces the profile, so the destination
        /// rig is untouched. Precondition: the rig has canonical ids (the editor
        /// maintains this); the builder canonicalizes inputs itself anyway.
        /// The transient clip is sampled at blob clip index 0.
        /// </summary>
        public static bool TryBuildPoseEvaluationBlob(
            SpriteSheetProfile profile,
            IReadOnlyList<SpritePartsClipDef> clipsOverride,
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
                var clips = CreateClips(clipsOverride ?? profile.PartsClips);
                BlankKeyAppearanceIdsForPose(clips);
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

        /// <summary>
        /// Pose-only blobs carry no appearance table, so keyed appearance ids
        /// cannot resolve there; they must hold instead of failing the build.
        /// Editor art resolution uses the authoring-side string query
        /// (SampleKeyedAppearanceId), never this blob. Runtime bake is unaffected.
        /// </summary>
        static void BlankKeyAppearanceIdsForPose(SpritePartsSetBuilder.ClipInput[] clips)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                var tracks = clips[i].Tracks;
                if (tracks == null) continue;
                for (int t = 0; t < tracks.Length; t++)
                {
                    var keys = tracks[t].Keys;
                    if (keys == null) continue;
                    for (int k = 0; k < keys.Length; k++)
                        keys[k].AppearanceId = string.Empty;
                }
            }
        }
    }
}
