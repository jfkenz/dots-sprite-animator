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
                blob = SpritePartsSetBuilder.Build(allocator, slots, appearances, clips, skins, CreateIk(bakeProfile),
                    CreateJiggles(bakeProfile), CreateParams(bakeProfile), CreateTransitions(bakeProfile),
                    CreateTransforms(bakeProfile), CreatePaths(bakeProfile));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static SpritePartsSetBuilder.IkInput[] CreateIk(SpriteSheetProfile profile)
        {
            var list = profile?.PartsIkConstraints;
            if (list == null || list.Count == 0)
                return Array.Empty<SpritePartsSetBuilder.IkInput>();
            var result = new System.Collections.Generic.List<SpritePartsSetBuilder.IkInput>(list.Count);
            foreach (var c in list)
            {
                if (c == null || !c.Enabled
                    || (c.Mix <= 0f && !IsKeyed(profile, SpritePartsValueKind.IkMix, c.Name)))
                    continue;
                result.Add(new SpritePartsSetBuilder.IkInput
                {
                    Name = c.Name,
                    EffectorSlotId = c.EffectorSlotId,
                    TargetSlotId = c.TargetSlotId,
                    ChainLength = Mathf.Clamp(c.ChainLength, 1, 2),
                    BendPositive = c.BendPositive,
                    Mix = c.Mix,
                });
            }
            return result.ToArray();
        }

        public static SpritePartsSetBuilder.TransformInput[] CreateTransforms(SpriteSheetProfile profile)
        {
            var list = profile?.PartsTransformConstraints;
            if (list == null || list.Count == 0)
                return Array.Empty<SpritePartsSetBuilder.TransformInput>();
            return list.FindAll(c => c != null && c.Enabled).ConvertAll(c => new SpritePartsSetBuilder.TransformInput
            {
                Name = c.Name,
                TargetSlotId = c.TargetSlotId,
                BoneSlotIds = (c.BoneSlotIds ?? new List<string>()).ToArray(),
                MixRotate = c.MixRotate,
                MixX = c.MixX,
                MixY = c.MixY,
                MixScaleX = c.MixScaleX,
                MixScaleY = c.MixScaleY,
                OffsetRotation = c.OffsetRotation,
                OffsetPosition = new float2(c.OffsetPosition.x, c.OffsetPosition.y),
                OffsetScale = new float2(c.OffsetScale.x, c.OffsetScale.y),
                Local = c.Local,
                Relative = c.Relative,
            }).ToArray();
        }

        public static SpritePartsSetBuilder.PathInput[] CreatePaths(SpriteSheetProfile profile)
        {
            var list = profile?.PartsPathConstraints;
            if (list == null || list.Count == 0)
                return Array.Empty<SpritePartsSetBuilder.PathInput>();
            return list.FindAll(c => c != null && c.Enabled).ConvertAll(c => new SpritePartsSetBuilder.PathInput
            {
                Name = c.Name,
                PathSlotId = c.PathSlotId,
                BoneSlotIds = (c.BoneSlotIds ?? new List<string>()).ToArray(),
                Position = c.Position,
                Spacing = c.Spacing,
                SpacingMode = (byte)c.SpacingMode,
                RotateMode = (byte)c.RotateMode,
                OffsetRotation = c.OffsetRotation,
                MixRotate = c.MixRotate,
                MixTranslate = c.MixTranslate,
            }).ToArray();
        }

        public static SpritePartsSetBuilder.TransitionsInput CreateTransitions(SpriteSheetProfile profile)
        {
            if (profile == null)
                return null;
            var result = new SpritePartsSetBuilder.TransitionsInput
            {
                DefaultMix = profile.PartsDefaultMix,
                DefaultMixEase = profile.PartsDefaultMixEase,
                FadeOutEvents = profile.PartsFadeOutEvents,
                Mixes = (profile.PartsMixes ?? new System.Collections.Generic.List<SpritePartsMixDef>()).FindAll(m => m != null)
                    .ConvertAll(m => new SpritePartsSetBuilder.MixInput
                    {
                        FromClipId = m.FromClipId, ToClipId = m.ToClipId, Duration = m.Duration, Ease = m.Ease,
                    }).ToArray(),
                Masks = (profile.PartsMasks ?? new System.Collections.Generic.List<SpritePartsMaskDef>()).FindAll(m => m != null)
                    .ConvertAll(m => new SpritePartsSetBuilder.MaskInput
                    {
                        Name = m.Name, SlotIds = (m.SlotIds ?? new System.Collections.Generic.List<string>()).ToArray(),
                    }).ToArray(),
                BlendSpaces = (profile.PartsBlendSpaces ?? new System.Collections.Generic.List<SpritePartsBlendSpaceDef>()).FindAll(b => b != null)
                    .ConvertAll(b => new SpritePartsSetBuilder.BlendSpaceInput
                    {
                        Name = b.Name,
                        ClipIds = (b.Points ?? new System.Collections.Generic.List<SpritePartsBlendPointDef>()).ConvertAll(q => q?.ClipId).ToArray(),
                        Values = (b.Points ?? new System.Collections.Generic.List<SpritePartsBlendPointDef>()).ConvertAll(q => q?.Value ?? 0f).ToArray(),
                    }).ToArray(),
            };
            return result;
        }

        public static SpritePartsSetBuilder.ParamInput[] CreateParams(SpriteSheetProfile profile)
        {
            var list = profile?.PartsParams;
            if (list == null || list.Count == 0)
                return Array.Empty<SpritePartsSetBuilder.ParamInput>();
            var result = new System.Collections.Generic.List<SpritePartsSetBuilder.ParamInput>(list.Count);
            foreach (var p in list)
            {
                if (p == null)
                    continue;
                result.Add(new SpritePartsSetBuilder.ParamInput
                {
                    Name = p.Name, ClipId = p.ClipId, Min = p.Min, Max = p.Max, Default = p.Default, Additive = p.Additive,
                });
            }
            return result.ToArray();
        }

        /// <summary>
        /// Each jiggle chain as one spring per joint. The tip of a joint is its first child, else the end of the
        /// bone, else the centre of its image, else one unit down.
        /// </summary>
        public static SpritePartsSetBuilder.JiggleInput[] CreateJiggles(SpriteSheetProfile profile)
        {
            var list = profile?.PartsJiggles;
            if (list == null || list.Count == 0)
                return Array.Empty<SpritePartsSetBuilder.JiggleInput>();
            var result = new System.Collections.Generic.List<SpritePartsSetBuilder.JiggleInput>();
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var c in list)
            {
                if (c == null || !c.Enabled
                    || (c.Mix <= 0f && !IsKeyed(profile, SpritePartsValueKind.JiggleMix, c.Name)))
                    continue;
                var slot = SpritePartsAuthoringOps.FindSlot(profile, c.SlotId ?? string.Empty);
                for (int n = 0; slot != null && n < Mathf.Clamp(c.ChainLength, 1, 8); n++)
                {
                    string id = SpritePartIdUtility.Canonical(slot.SlotId);
                    var child = FirstChild(profile, id);
                    if (seen.Add(id))
                    {
                        result.Add(new SpritePartsSetBuilder.JiggleInput
                        {
                            Name = c.Name,
                            SlotId = id,
                            TipLocal = JiggleTip(profile, slot, child),
                            Stiffness = c.Stiffness,
                            Damping = c.Damping,
                            Gravity = c.Gravity,
                            Mix = c.Mix,
                        });
                    }
                    slot = child;
                }
            }
            return result.ToArray();
        }

        /// <summary>True when some clip keys this IK / jiggle (so it stays even at a setup Mix of 0).</summary>
        static bool IsKeyed(SpriteSheetProfile profile, SpritePartsValueKind kind, string name)
        {
            string target = (name ?? string.Empty).Trim();
            foreach (var clip in profile.PartsClips)
                foreach (var t in clip?.ValueTracks ?? new List<SpritePartsValueTrackDef>())
                    if (t != null && t.Kind == kind && (t.Target ?? string.Empty).Trim() == target && t.Keys != null && t.Keys.Count > 0)
                        return true;
            return false;
        }

        static SpritePartSlotDef FirstChild(SpriteSheetProfile profile, string canonicalId)
        {
            foreach (var s in profile.PartsSlots)
            {
                if (s != null && !string.IsNullOrWhiteSpace(s.ParentSlotId) && SpritePartIdUtility.Canonical(s.ParentSlotId) == canonicalId)
                    return s;
            }
            return null;
        }

        static Unity.Mathematics.float2 JiggleTip(SpriteSheetProfile profile, SpritePartSlotDef slot, SpritePartSlotDef child)
        {
            if (child != null && child.RestPosition.sqrMagnitude > 1e-8f)
                return new Unity.Mathematics.float2(child.RestPosition.x, child.RestPosition.y);
            if (slot.IsBone)
                return new Unity.Mathematics.float2(slot.BoneLength > 1e-4f ? slot.BoneLength : 1f, 0f);
            if (SpritePartsSkinning.TryResolveQuad(profile, slot, out var size, out var pivot))
            {
                var centre = (new Unity.Mathematics.float2(0.5f, 0.5f) - pivot) * size;
                if (Unity.Mathematics.math.lengthsq(centre) > 1e-8f)
                    return centre;
            }
            return new Unity.Mathematics.float2(0f, -1f);
        }

        public static SpritePartsSetBuilder.SlotInput[] CreateSlots(SpriteSheetProfile profile)
        {
            var list = profile.PartsSlots;
            if (list == null || list.Count == 0)
                return Array.Empty<SpritePartsSetBuilder.SlotInput>();
            var result = new SpritePartsSetBuilder.SlotInput[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                string defaultApp = s.IsBone || s.IsClipShape || s.IsPath ? string.Empty : s.DefaultAppearanceId; // bones, clip shapes and paths have no image
                if (!string.IsNullOrWhiteSpace(defaultApp)
                    && SpritePartsValidation.FindAppearanceIndex(profile, defaultApp) < 0)
                    defaultApp = string.Empty;
                string parentId = s.ParentSlotId;
                if (!string.IsNullOrWhiteSpace(parentId)
                    && SpritePartsAuthoringOps.FindSlot(profile, parentId) == null)
                    parentId = string.Empty;
                result[i] = new SpritePartsSetBuilder.SlotInput
                {
                    Name = s.Name,
                    SlotId = s.SlotId,
                    ParentSlotId = parentId,
                    RestPosition = new float2(s.RestPosition.x, s.RestPosition.y),
                    RestRotation = s.RestRotation,
                    RestScale = new float2(s.RestScale.x, s.RestScale.y),
                    DefaultAppearanceId = defaultApp,
                    DrawRank = s.DrawRank,
                    // A bone never draws itself, but its children do (Hidden is per slot here).
                    Hidden = s.IsBone || s.IsClipShape || s.IsPath || SpritePartsAuthoringOps.SlotOrAncestorHidden(profile, s.SlotId)
                        ? (byte)1 : (byte)0,
                    Mesh = SpritePartsLattice.FromMesh(s.Mesh),
                    ClipMaskSlotId = s.ClipMaskSlotId,
                    IsClipShape = s.IsClipShape,
                    ClipPolygon = s.IsClipShape && s.ClipPolygon != null
                        ? Array.ConvertAll(s.ClipPolygon, v => new float2(v.x, v.y))
                        : null,
                    ClipEndSlotId = s.IsClipShape ? s.ClipEndSlotId : null,
                    IsPath = s.IsPath,
                    PathPoints = s.IsPath && s.PathPoints != null ? Array.ConvertAll(s.PathPoints, v => new float2(v.x, v.y)) : null,
                    PathClosed = s.PathClosed,
                };
                // Clipping maps between part images: masks and clipped parts need their image size and pivot
                // (with a clip shape in the rig any part may be clipped).
                bool clipping = !string.IsNullOrWhiteSpace(s.ClipMaskSlotId) || IsClipMask(profile, s.SlotId) || HasClipShape(profile);
                if (clipping && SpritePartsSkinning.TryResolveQuad(profile, s, out var clipSize, out var clipPivot))
                {
                    result[i].SkinQuadSize = clipSize;
                    result[i].SkinQuadPivot = clipPivot;
                }
                if (s.Mesh != null && s.Mesh.HasWeights
                    && SpritePartsSkinning.TryResolveQuad(profile, s, out var quadSize, out var quadPivot))
                {
                    result[i].SkinBones = s.Mesh.Bones;
                    result[i].SkinWeights = s.Mesh.Weights;
                    result[i].SkinQuadSize = quadSize;
                    result[i].SkinQuadPivot = quadPivot;
                }
            }
            return result;
        }

        static bool HasClipShape(SpriteSheetProfile profile)
        {
            foreach (var s in profile.PartsSlots)
                if (s != null && s.IsClipShape)
                    return true;
            return false;
        }

        static bool IsClipMask(SpriteSheetProfile profile, string slotId)
        {
            string id = SpritePartIdUtility.Canonical(slotId);
            foreach (var s in profile.PartsSlots)
            {
                if (s != null && !string.IsNullOrWhiteSpace(s.ClipMaskSlotId) && SpritePartIdUtility.Canonical(s.ClipMaskSlotId) == id)
                    return true;
            }
            return false;
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
            => CreateClips(profile.PartsClips, profile);

        public static SpritePartsSetBuilder.ClipInput[] CreateClips(IReadOnlyList<SpritePartsClipDef> list)
            => CreateClips(list, null);

        /// <summary>The distinct sounds the clips' events play, in a fixed order (the audio bank baked with the blob).</summary>
        public static AudioClip[] EventAudio(IReadOnlyList<SpritePartsClipDef> list)
        {
            var result = new List<AudioClip>();
            if (list == null)
                return result.ToArray();
            foreach (var clip in list)
                foreach (var e in clip?.Events ?? new List<SpritePartsEventMarker>())
                    if (e?.Audio != null && !result.Contains(e.Audio))
                        result.Add(e.Audio);
            return result.ToArray();
        }

        public static SpritePartsSetBuilder.ClipInput[] CreateClips(
            IReadOnlyList<SpritePartsClipDef> list, SpriteSheetProfile profile)
        {
            if (list == null)
                return Array.Empty<SpritePartsSetBuilder.ClipInput>();
            var audio = new List<AudioClip>(EventAudio(list));
            var result = new SpritePartsSetBuilder.ClipInput[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var clip = list[i];
                var tracks = clip.Tracks ?? new List<SpritePartsTrackDef>();
                var kept = new List<SpritePartsSetBuilder.TrackInput>(tracks.Count);
                for (int t = 0; t < tracks.Count; t++)
                {
                    var track = tracks[t];
                    if (profile != null
                        && SpritePartsAuthoringOps.FindSlot(profile, track.SlotId) == null)
                        continue;
                    var keys = track.Keys ?? new List<SpritePartsKeyDef>();
                    var keyInputs = new SpritePartsSetBuilder.KeyInput[keys.Count];
                    for (int k = 0; k < keys.Count; k++)
                    {
                        var key = keys[k];
                        string appearanceId = key.AppearanceId;
                        if (profile != null
                            && !string.IsNullOrWhiteSpace(appearanceId)
                            && SpritePartsValidation.FindAppearanceIndex(
                                profile, SpritePartIdUtility.Canonical(appearanceId)) < 0)
                            appearanceId = string.Empty;
                        keyInputs[k] = new SpritePartsSetBuilder.KeyInput
                        {
                            Time = key.Time,
                            Position = new float2(key.Position.x, key.Position.y),
                            Rotation = key.Rotation,
                            Scale = new float2(key.Scale.x, key.Scale.y),
                            EaseMode = key.EaseMode,
                            AppearanceId = appearanceId,
                            Deform = SpritePartsLattice.DeformFromArray(key.Deform, key.Deform?.Length ?? 0),
                            HasColor = key.HasColor,
                            Color = new float4(key.Color.r, key.Color.g, key.Color.b, key.Color.a),
                            HasDrawOrder = key.HasDrawOrder,
                            DrawOrder = key.DrawOrder,
                            Curve = new float4(key.Curve.x, key.Curve.y, key.Curve.z, key.Curve.w),
                            SkipChannels = (byte)(~(byte)key.Channels & (byte)SpritePartsKeyChannel.All),
                            SeparateCurves = key.Separate.On,
                            CurveY = key.Separate.Y,
                            CurveRotation = key.Separate.Rotation,
                            CurveScaleX = key.Separate.ScaleX,
                            CurveScaleY = key.Separate.ScaleY,
                            HasClipActive = key.HasClipActive,
                            ClipActive = key.ClipActive,
                        };
                    }
                    kept.Add(new SpritePartsSetBuilder.TrackInput
                    {
                        SlotId = track.SlotId,
                        Kind = (byte)(track.Kind == SpritePartsTrackKind.Appearance
                            ? SpritePartsTrackKind.Appearance
                            : SpritePartsTrackKind.Pose),
                        Keys = keyInputs,
                    });
                }

                result[i] = new SpritePartsSetBuilder.ClipInput
                {
                    Name = clip.Name,
                    ClipId = clip.ClipId,
                    Duration = clip.Duration,
                    SpeedMultiplier = clip.Speed,
                    WrapMode = clip.WrapMode,
                    Tracks = kept.ToArray(),
                    Events = clip.Events == null
                        ? Array.Empty<SpritePartsSetBuilder.EventInput>()
                        : clip.Events.ConvertAll(e => new SpritePartsSetBuilder.EventInput
                        {
                            Time = e.Time, Id = e.EventId, IntPayload = e.IntPayload, FloatPayload = e.FloatPayload,
                            TextPayload = e.TextPayload,
                            AudioIndex = e.Audio != null ? audio.IndexOf(e.Audio) : -1,
                            Volume = e.Audio != null ? e.Volume : 0f,
                            Balance = e.Balance,
                        }).ToArray(),
                    ValueTracks = (clip.ValueTracks ?? new List<SpritePartsValueTrackDef>()).FindAll(t => t != null)
                        .ConvertAll(t => new SpritePartsSetBuilder.ValueTrackInput
                        {
                            Kind = (byte)t.Kind,
                            Target = t.Target,
                            Keys = (t.Keys ?? new List<SpritePartsValueKeyDef>()).FindAll(k => k != null)
                                .ConvertAll(k => new SpritePartsSetBuilder.ValueKeyInput
                                {
                                    Time = k.Time, Value = k.Value, EaseMode = k.EaseMode,
                                    Curve = new float4(k.Curve.x, k.Curve.y, k.Curve.z, k.Curve.w),
                                }).ToArray(),
                        }).ToArray(),
                };
            }
            return result;
        }

        public static SpritePartsSetBuilder.SkinInput[] CreateSkins(SpriteSheetProfile profile)
        {
            var list = profile.PartsSkins;
            if (list == null)
                return Array.Empty<SpritePartsSetBuilder.SkinInput>();
            var result = new SpritePartsSetBuilder.SkinInput[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var skin = list[i];
                var bindings = skin.Bindings ?? new List<SpritePartsSkinBindingDef>();
                var kept = new List<SpritePartsSetBuilder.SkinBindingInput>(bindings.Count);
                for (int b = 0; b < bindings.Count; b++)
                {
                    var bind = bindings[b];
                    if (SpritePartsAuthoringOps.FindSlot(profile, bind.SlotId) == null)
                        continue;
                    if (SpritePartsValidation.FindAppearanceIndex(
                            profile, SpritePartIdUtility.Canonical(bind.AppearanceId)) < 0)
                        continue;
                    kept.Add(new SpritePartsSetBuilder.SkinBindingInput
                    {
                        SlotId = bind.SlotId,
                        AppearanceId = bind.AppearanceId,
                    });
                }
                result[i] = new SpritePartsSetBuilder.SkinInput
                {
                    SkinId = skin.SkinId,
                    Bindings = kept.ToArray(),
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
                    System.Array.Empty<SpritePartsSetBuilder.SkinInput>(),
                    CreateIk(profile),
                    CreateJiggles(profile),
                    CreateParams(profile),
                    CreateTransitions(profile),
                    CreateTransforms(profile),
                    CreatePaths(profile));
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
                var clips = CreateClips(clipsOverride ?? profile.PartsClips, profile);
                BlankKeyAppearanceIdsForPose(clips);
                blob = SpritePartsSetBuilder.Build(
                    allocator,
                    slots,
                    System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                    clips,
                    System.Array.Empty<SpritePartsSetBuilder.SkinInput>(),
                    CreateIk(profile),
                    CreateJiggles(profile),
                    CreateParams(profile),
                    CreateTransitions(profile),
                    CreateTransforms(profile),
                    CreatePaths(profile));
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
