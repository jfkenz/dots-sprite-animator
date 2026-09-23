using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Rig / Animate / Skins — editor mode contract for Parts authoring.</summary>
    public enum SpritePartsStudioMode : byte
    {
        Rig = 0,
        Animate = 1,
        Skins = 2,
    }

    /// <summary>9-point align anchors. Row 0 is world +Y (top).</summary>
    public enum SpritePartsAlignPivot : byte
    {
        TopLeft = 0,
        Top = 1,
        TopRight = 2,
        Left = 3,
        Center = 4,
        Right = 5,
        BottomLeft = 6,
        Bottom = 7,
        BottomRight = 8,
    }

    /// <summary>
    /// Pure Parts authoring operations. Mode isolation and templates live here so
    /// EditMode tests can cover contracts without opening the tool window.
    /// </summary>
    public static partial class SpritePartsAuthoringOps
    {
        public const float DefaultDisplayFps = 30f;
        public const float DefaultOnionOpacity = 0.30f;
        public const int DefaultOnionBefore = 1;
        public const int DefaultOnionAfter = 1;
        public const int DefaultOnionSpacingFrames = 2;

        public static float2 PivotOnAabb(float2 min, float2 max, SpritePartsAlignPivot pivot)
        {
            int i = (int)pivot;
            int col = i % 3;
            int row = i / 3;
            float x = col == 0 ? min.x : col == 1 ? 0.5f * (min.x + max.x) : max.x;
            float y = row == 0 ? max.y : row == 1 ? 0.5f * (min.y + max.y) : min.y;
            return new float2(x, y);
        }

        public static string AlignPivotLabel(SpritePartsAlignPivot pivot)
            => pivot switch
            {
                SpritePartsAlignPivot.TopLeft => "Top Left",
                SpritePartsAlignPivot.Top => "Top",
                SpritePartsAlignPivot.TopRight => "Top Right",
                SpritePartsAlignPivot.Left => "Left",
                SpritePartsAlignPivot.Center => "Center",
                SpritePartsAlignPivot.Right => "Right",
                SpritePartsAlignPivot.BottomLeft => "Bottom Left",
                SpritePartsAlignPivot.Bottom => "Bottom",
                SpritePartsAlignPivot.BottomRight => "Bottom Right",
                _ => "Center",
            };

        public struct PoseEdit
        {
            public Vector2 Position;
            public float Rotation;
            public Vector2 Scale;
            public SpritePartsLattice Lattice;
        }

        public struct WriteResult
        {
            public bool WroteRest;
            public bool WroteKey;
            public bool WroteAppearance;
            public bool InsertedRestAnchorAtZero;
            public bool Rejected;
            public string Reason;
        }

        /// <summary>Create Floating Parts preset: Body, Hand L, Hand R, Weapon + Idle clip.</summary>
        public static void ApplyFloatingPartsTemplate(SpriteSheetProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.EnsurePartsRig();
            profile.AnimKind = SpriteAnimKind.Parts;
            profile.PartsSlots.Clear();
            profile.PartsGroups ??= new List<SpritePartsGroupDef>();
            profile.PartsGroups.Clear();
            profile.PartsAppearances.Clear();
            profile.PartsClips.Clear();
            profile.PartsSkins.Clear();

            profile.PartsSlots.Add(new SpritePartSlotDef
            {
                Name = "Body", SlotId = "body", ParentSlotId = string.Empty,
                SiblingOrder = 0,
                RestPosition = Vector2.zero, RestScale = Vector2.one, DrawRank = 0,
            });
            profile.PartsSlots.Add(new SpritePartSlotDef
            {
                Name = "Hand L", SlotId = "hand.l", ParentSlotId = "body",
                SiblingOrder = 0,
                RestPosition = new Vector2(-0.35f, 0.1f), RestScale = Vector2.one, DrawRank = 1,
            });
            profile.PartsSlots.Add(new SpritePartSlotDef
            {
                Name = "Hand R", SlotId = "hand.r", ParentSlotId = "body",
                SiblingOrder = 1,
                RestPosition = new Vector2(0.35f, 0.1f), RestScale = Vector2.one, DrawRank = 2,
            });
            profile.PartsSlots.Add(new SpritePartSlotDef
            {
                Name = "Weapon", SlotId = "weapon", ParentSlotId = "hand.r",
                SiblingOrder = 0,
                RestPosition = new Vector2(0.25f, 0f), RestScale = Vector2.one, DrawRank = 3,
            });

            profile.PartsClips.Add(new SpritePartsClipDef
            {
                Name = "Idle", ClipId = "idle", Duration = 1f, Speed = 1f,
                WrapMode = (byte)SpritePartsWrap.Loop,
                Tracks = new List<SpritePartsTrackDef>(),
            });
            profile.PartsClips.Add(new SpritePartsClipDef
            {
                Name = "Walk", ClipId = "walk", Duration = 0.6f, Speed = 1f,
                WrapMode = (byte)SpritePartsWrap.Loop,
                Tracks = new List<SpritePartsTrackDef>(),
            });
            profile.PartsSkins.Add(new SpritePartsSkinDef
            {
                Name = "Default", SkinId = "default",
                Bindings = new List<SpritePartsSkinBindingDef>(),
            });
            profile.PartsDefaultClipId = "idle";
            profile.PartsDefaultSkinId = "default";
            SpritePartsValidation.CanonicalizeIds(profile);
        }

        public static void CreateEmptyPartsRig(SpriteSheetProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.EnsurePartsRig();
            profile.AnimKind = SpriteAnimKind.Parts;
            if (profile.PartsClips.Count == 0)
            {
                profile.PartsClips.Add(new SpritePartsClipDef
                {
                    Name = "Idle", ClipId = "idle", Duration = 1f, Speed = 1f,
                    WrapMode = (byte)SpritePartsWrap.Loop,
                    Tracks = new List<SpritePartsTrackDef>(),
                });
                profile.PartsDefaultClipId = "idle";
            }
            if (profile.PartsSkins.Count == 0)
            {
                profile.PartsSkins.Add(new SpritePartsSkinDef
                {
                    Name = "Default", SkinId = "default",
                    Bindings = new List<SpritePartsSkinBindingDef>(),
                });
                profile.PartsDefaultSkinId = "default";
            }
            SpritePartsValidation.CanonicalizeIds(profile);
        }

        /// <summary>
        /// Mode contract: Rig writes rest only; Animate writes keys only; Skins rejects transforms.
        /// </summary>
        public static WriteResult ApplyPoseEdit(
            SpriteSheetProfile profile,
            SpritePartsStudioMode mode,
            int clipIndex,
            string slotId,
            float timeSeconds,
            PoseEdit pose,
            bool autoKey,
            float snapFps = DefaultDisplayFps)
        {
            var result = new WriteResult();
            if (profile == null)
            {
                result.Rejected = true;
                result.Reason = "Profile is null.";
                return result;
            }
            profile.EnsurePartsRig();
            var slot = FindSlot(profile, slotId);
            if (slot == null)
            {
                result.Rejected = true;
                result.Reason = "Slot not found.";
                return result;
            }

            if (mode == SpritePartsStudioMode.Skins)
            {
                result.Rejected = true;
                result.Reason = "Transform tools are disabled in Skins mode.";
                return result;
            }

            if (mode == SpritePartsStudioMode.Rig)
            {
                slot.RestPosition = pose.Position;
                slot.RestRotation = pose.Rotation;
                slot.RestScale = SanitizeScale(pose.Scale);
                result.WroteRest = true;
                return result;
            }

            // Animate — never write rest.
            if (!autoKey)
            {
                result.Rejected = true;
                result.Reason = "Auto Key is off; use Key Pose to commit.";
                return result;
            }

            if (clipIndex < 0 || clipIndex >= profile.PartsClips.Count)
            {
                result.Rejected = true;
                result.Reason = "No Parts clip selected.";
                return result;
            }

            var clip = profile.PartsClips[clipIndex];
            float duration = Mathf.Max(1e-3f, clip.Duration);
            float time = SnapTime(Mathf.Clamp(timeSeconds, 0f, duration), snapFps, duration);
            var track = GetOrCreateTrack(clip, slot.SlotId);
            bool emptyBefore = track.Keys.Count == 0;
            UpsertKey(track, time, pose);
            result.WroteKey = true;

            // First key after t=0 also inserts a rest key at t=0 in the same operation.
            if (emptyBefore && time > 1e-5f)
            {
                UpsertKey(track, 0f, new PoseEdit
                {
                    Position = slot.RestPosition,
                    Rotation = slot.RestRotation,
                    Scale = slot.RestScale,
                });
                result.InsertedRestAnchorAtZero = true;
            }
            return result;
        }

        /// <summary>
        /// Add a parent-local position delta to rest and/or pose keys. Does not rewrite
        /// rotation/scale. clipIndex &lt; 0 offsets every clip. skipTime skips keys at that
        /// playhead so a current key is not double-offset.
        /// </summary>
        public static int OffsetSlotLocalPosition(
            SpriteSheetProfile profile,
            string slotId,
            Vector2 localDelta,
            bool includeRest,
            int clipIndex = -1,
            float skipTime = float.NaN,
            float skipEps = 1e-4f)
        {
            if (profile == null ||
                (Mathf.Abs(localDelta.x) < 1e-8f && Mathf.Abs(localDelta.y) < 1e-8f))
                return 0;
            profile.EnsurePartsRig();
            var slot = FindSlot(profile, slotId);
            if (slot == null)
                return 0;

            int count = 0;
            if (includeRest)
            {
                slot.RestPosition += localDelta;
                count++;
            }

            if (profile.PartsClips == null)
                return count;
            int start = clipIndex < 0 ? 0 : clipIndex;
            int end = clipIndex < 0 ? profile.PartsClips.Count - 1 : clipIndex;
            bool skip = !float.IsNaN(skipTime);
            for (int c = start; c <= end && c < profile.PartsClips.Count; c++)
            {
                if (c < 0)
                    continue;
                var track = FindTrack(profile.PartsClips[c], slotId, SpritePartsTrackKind.Pose);
                if (track?.Keys == null)
                    continue;
                for (int k = 0; k < track.Keys.Count; k++)
                {
                    var key = track.Keys[k];
                    if (key == null)
                        continue;
                    if (skip && Mathf.Abs(key.Time - skipTime) <= skipEps)
                        continue;
                    key.Position += localDelta;
                    count++;
                }
            }
            return count;
        }

        public static Vector2 EvaluateSlotLocalPosition(
            SpritePartSlotDef slot,
            SpritePartsTrackDef track,
            float time,
            float skipTime = float.NaN,
            float skipEps = 1e-4f)
        {
            Vector2 pos = slot != null ? slot.RestPosition : Vector2.zero;
            if (track?.Keys == null || track.Keys.Count == 0)
                return pos;
            SpritePartsKeyDef prev = null;
            SpritePartsKeyDef next = null;
            bool skip = !float.IsNaN(skipTime);
            for (int i = 0; i < track.Keys.Count; i++)
            {
                var key = track.Keys[i];
                if (key == null)
                    continue;
                if (skip && Mathf.Abs(key.Time - skipTime) <= skipEps)
                    continue;
                if (key.Time <= time)
                    prev = key;
                if (key.Time >= time && next == null)
                    next = key;
            }
            if (prev == null && next == null)
                return pos;
            if (prev == null)
                return next.Position;
            if (next == null || Mathf.Abs(next.Time - prev.Time) < 1e-8f)
                return prev.Position;
            float u = Mathf.InverseLerp(prev.Time, next.Time, time);
            return Vector2.LerpUnclamped(prev.Position, next.Position, u);
        }

        public static WriteResult WriteKeyPose(
            SpriteSheetProfile profile,
            int clipIndex,
            string slotId,
            float timeSeconds,
            PoseEdit pose,
            float snapFps = DefaultDisplayFps,
            string appearanceId = null,
            bool includeAppearance = false)
        {
            var result = ApplyPoseEdit(profile, SpritePartsStudioMode.Animate, clipIndex, slotId,
                timeSeconds, pose, autoKey: true, snapFps);
            if (!includeAppearance)
                return result;
            // Pose and appearance are separate channels: the id never lands on
            // the pose key this write created.
            var sprite = WriteKeyAppearance(profile, clipIndex, slotId, timeSeconds, appearanceId, snapFps);
            result.WroteAppearance = sprite.WroteAppearance;
            if (sprite.Rejected)
                result.Reason = sprite.Reason;
            return result;
        }

        /// <summary>
        /// Upsert a sprite key at playhead. The appearance goes to the slot's
        /// independent appearance channel - pose keys never carry ids from new
        /// writes. When poseFallback has a value a pose key is written too
        /// (without an id); without one, no pose key is created.
        /// Empty appearanceId writes a hold marker on the channel.
        /// </summary>
        public static WriteResult WriteKeySprite(
            SpriteSheetProfile profile,
            int clipIndex,
            string slotId,
            float timeSeconds,
            string appearanceId,
            PoseEdit? poseFallback = null,
            float snapFps = DefaultDisplayFps)
        {
            var result = WriteKeyAppearance(profile, clipIndex, slotId, timeSeconds, appearanceId, snapFps);
            if (result.Rejected)
                return result;
            if (poseFallback.HasValue)
            {
                var poseResult = ApplyPoseEdit(profile, SpritePartsStudioMode.Animate, clipIndex, slotId,
                    timeSeconds, poseFallback.Value, autoKey: true, snapFps);
                result.WroteKey = poseResult.WroteKey || result.WroteKey;
                if (poseResult.Rejected && !string.IsNullOrEmpty(poseResult.Reason))
                    result.Reason = poseResult.Reason;
            }
            return result;
        }

        /// <summary>
        /// Upsert an appearance key on the slot's independent channel. Pose keys
        /// are never created or modified; legacy ids on the pose track migrate
        /// into the channel on first write (see EnsureAppearanceTrack).
        /// </summary>
        public static WriteResult WriteKeyAppearance(
            SpriteSheetProfile profile,
            int clipIndex,
            string slotId,
            float timeSeconds,
            string appearanceId,
            float snapFps = DefaultDisplayFps)
        {
            var result = new WriteResult();
            if (profile == null)
            {
                result.Rejected = true;
                result.Reason = "Profile is null.";
                return result;
            }
            profile.EnsurePartsRig();
            var slot = FindSlot(profile, slotId);
            if (slot == null)
            {
                result.Rejected = true;
                result.Reason = "Slot not found.";
                return result;
            }
            if (clipIndex < 0 || clipIndex >= profile.PartsClips.Count)
            {
                result.Rejected = true;
                result.Reason = "No Parts clip selected.";
                return result;
            }

            string aid = string.IsNullOrWhiteSpace(appearanceId)
                ? string.Empty
                : SpritePartIdUtility.Canonical(appearanceId);
            if (!string.IsNullOrEmpty(aid))
            {
                bool found = false;
                if (profile.PartsAppearances != null)
                {
                    for (int i = 0; i < profile.PartsAppearances.Count; i++)
                    {
                        var app = profile.PartsAppearances[i];
                        if (app != null &&
                            SpritePartIdUtility.Canonical(app.AppearanceId, app.Name) == aid)
                        {
                            found = true;
                            break;
                        }
                    }
                }
                if (!found)
                {
                    result.Rejected = true;
                    result.Reason = $"Appearance '{aid}' is not on this profile.";
                    return result;
                }
            }

            var clip = profile.PartsClips[clipIndex];
            float duration = Mathf.Max(1e-3f, clip.Duration);
            float time = SnapTime(Mathf.Clamp(timeSeconds, 0f, duration), snapFps, duration);
            var channel = EnsureAppearanceTrack(clip, slot.SlotId);
            UpsertAppearanceKey(channel, time, aid);
            result.WroteAppearance = true;
            return result;
        }

        /// <summary>
        /// Sample keyed appearance id for editor preview: last non-empty at/before time,
        /// with Loop carry. Empty string means no keyed appearance (skin/default applies).
        /// </summary>
        public static string SampleKeyedAppearanceId(
            SpriteSheetProfile profile, int clipIndex, string slotId, float timeSeconds)
        {
            if (profile?.PartsClips == null || clipIndex < 0 || clipIndex >= profile.PartsClips.Count)
                return string.Empty;
            var clip = profile.PartsClips[clipIndex];
            if (clip == null) return string.Empty;
            float duration = Mathf.Max(1e-3f, clip.Duration);
            float time = SpritePartsSampler.WrapTime(timeSeconds, duration, clip.WrapMode);
            // Dedicated appearance channel wins; legacy ids mixed into pose keys
            // still sample when the slot has no channel.
            var track = FindAppearanceTrack(clip, slotId);
            if (track == null)
                track = FindTrack(clip, slotId);
            if (track?.Keys == null || track.Keys.Count == 0)
                return string.Empty;

            string best = null;
            string lastInClip = null;
            for (int i = 0; i < track.Keys.Count; i++)
            {
                var key = track.Keys[i];
                if (key == null || string.IsNullOrWhiteSpace(key.AppearanceId))
                    continue;
                string aid = SpritePartIdUtility.Canonical(key.AppearanceId);
                lastInClip = aid;
                if (key.Time <= time + 1e-6f)
                    best = aid;
            }
            if (!string.IsNullOrEmpty(best))
                return best;
            if (clip.WrapMode != (byte)SpritePartsWrap.Once && !string.IsNullOrEmpty(lastInClip))
                return lastInClip;
            return string.Empty;
        }

        public static SpritePartsKeyDef FindKeyAtTime(SpritePartsTrackDef track, float time, float epsilon = 1e-4f)
        {
            if (track?.Keys == null) return null;
            for (int i = 0; i < track.Keys.Count; i++)
            {
                if (Mathf.Abs(track.Keys[i].Time - time) <= epsilon)
                    return track.Keys[i];
            }
            return null;
        }

        /// <summary>Find the slot's POSE track. Never returns an appearance track.</summary>
        public static SpritePartsTrackDef FindTrack(SpritePartsClipDef clip, string slotId)
            => FindTrack(clip, slotId, SpritePartsTrackKind.Pose);

        public static SpritePartsTrackDef FindTrack(SpritePartsClipDef clip, string slotId, SpritePartsTrackKind kind)
        {
            if (clip?.Tracks == null) return null;
            string id = SpritePartIdUtility.Canonical(slotId);
            for (int i = 0; i < clip.Tracks.Count; i++)
            {
                var t = clip.Tracks[i];
                if (t != null && t.Kind == kind && SpritePartIdUtility.Canonical(t.SlotId) == id)
                    return t;
            }
            return null;
        }

        /// <summary>The slot's independent appearance channel, or null.</summary>
        public static SpritePartsTrackDef FindAppearanceTrack(SpritePartsClipDef clip, string slotId)
            => FindTrack(clip, slotId, SpritePartsTrackKind.Appearance);

        public struct AssignArtResult
        {
            public bool Ok;
            public string Reason;
            public string AppearanceId;
            public int SheetIndex;
            public int CellIndex;
        }

        public static SpritePartAppearanceDef FindAppearance(SpriteSheetProfile profile, string appearanceId)
        {
            if (profile?.PartsAppearances == null || string.IsNullOrWhiteSpace(appearanceId))
                return null;
            string id = SpritePartIdUtility.Canonical(appearanceId);
            for (int i = 0; i < profile.PartsAppearances.Count; i++)
            {
                var app = profile.PartsAppearances[i];
                if (app != null && SpritePartIdUtility.Canonical(app.AppearanceId, app.Name) == id)
                    return app;
            }
            return null;
        }

        /// <summary>
        /// Bind a texture cell as this slot's default appearance. 1x1 = whole image.
        /// Reuses a sheet with the same texture+grid. Updates the slot's existing
        /// default appearance when present, otherwise creates `{slotId}.default`.
        /// </summary>
        public static AssignArtResult AssignSlotArt(
            SpriteSheetProfile profile,
            string slotId,
            Texture2D texture,
            int columns,
            int rows,
            int cellIndex)
        {
            var result = new AssignArtResult();
            if (profile == null)
            {
                result.Reason = "Profile is null.";
                return result;
            }
            if (texture == null)
            {
                result.Reason = "Texture is null.";
                return result;
            }
            profile.EnsurePartsRig();
            profile.EnsureSheets();
            var slot = FindSlot(profile, slotId);
            if (slot == null)
            {
                result.Reason = "Slot not found.";
                return result;
            }

            columns = Mathf.Max(1, columns);
            rows = Mathf.Max(1, rows);
            int cellCount = columns * rows;
            cellIndex = ((cellIndex % cellCount) + cellCount) % cellCount;

            int sheetIndex = -1;
            for (int i = 0; i < profile.Sheets.Count; i++)
            {
                var s = profile.Sheets[i];
                if (s == null || s.Texture != texture) continue;
                if (Mathf.Max(1, s.Columns) != columns) continue;
                if (Mathf.Max(1, s.Rows) != rows) continue;
                sheetIndex = i;
                break;
            }
            if (sheetIndex < 0)
            {
                profile.Sheets.Add(new SpriteSheetDef
                {
                    Name = string.IsNullOrWhiteSpace(texture.name) ? "Sheet" : texture.name,
                    Texture = texture,
                    Columns = columns,
                    Rows = rows,
                    PixelsPerUnit = profile.PixelsPerUnit > 0f
                        ? profile.PixelsPerUnit
                        : SpriteSheetProfile.DefaultPixelsPerUnit,
                    Pivot = SpriteSheetProfile.DefaultPivot,
                    CellLayoutMode = SpriteSheetCellLayoutMode.Grid,
                });
                sheetIndex = profile.Sheets.Count - 1;
            }

            string desiredId = SpritePartIdUtility.Canonical(slot.SlotId) + ".default";
            SpritePartAppearanceDef app = null;
            if (!string.IsNullOrWhiteSpace(slot.DefaultAppearanceId))
                app = FindAppearance(profile, slot.DefaultAppearanceId);
            if (app == null)
                app = FindAppearance(profile, desiredId);
            if (app == null)
            {
                for (int i = 0; i < profile.PartsAppearances.Count; i++)
                {
                    var a = profile.PartsAppearances[i];
                    if (a != null && a.SheetIndex == sheetIndex && a.CellIndex == cellIndex)
                    {
                        app = a;
                        break;
                    }
                }
            }
            if (app == null)
            {
                string id = desiredId;
                int n = 2;
                while (FindAppearance(profile, id) != null)
                {
                    id = desiredId + n;
                    n++;
                }
                app = new SpritePartAppearanceDef
                {
                    Name = (string.IsNullOrWhiteSpace(slot.Name) ? slot.SlotId : slot.Name) + " Default",
                    AppearanceId = id,
                    SheetIndex = sheetIndex,
                    CellIndex = cellIndex,
                    LogicalWorldSize = Vector2.zero,
                    PivotSource = SpritePartPivotSource.SheetDefault,
                };
                profile.PartsAppearances.Add(app);
            }
            else
            {
                app.SheetIndex = sheetIndex;
                app.CellIndex = cellIndex;
            }

            slot.DefaultAppearanceId = SpritePartIdUtility.Canonical(app.AppearanceId, app.Name);
            SpritePartsValidation.CanonicalizeIds(profile);
            result.Ok = true;
            result.AppearanceId = slot.DefaultAppearanceId;
            result.SheetIndex = sheetIndex;
            result.CellIndex = cellIndex;
            return result;
        }

        public struct KindSwitchResult
        {
            public bool Ok;
            public string Reason;
        }

        /// <summary>
        /// Explicit, validated runtime-mode change. The editor workspace never
        /// calls this implicitly. Switching preserves both data sets; activating
        /// Parts requires a rig that passes validation, activating Frames requires
        /// at least one frame clip on a sheet with a texture, and Static needs a
        /// sheet texture. Rejected switches change nothing and explain the fix.
        /// </summary>
        public static KindSwitchResult TrySetAnimKind(SpriteSheetProfile profile, SpriteAnimKind kind)
        {
            if (profile == null)
                return new KindSwitchResult { Reason = "Profile is null." };
            // Revalidate even the active mode: data may have been edited since activation.
            if (kind == SpriteAnimKind.Parts)
            {
                if (profile.PartsSlots == null || profile.PartsSlots.Count == 0)
                    return new KindSwitchResult { Reason = "Parts rig has no slots. Add a part before activating runtime." };
                var previous = profile.AnimKind;
                profile.AnimKind = SpriteAnimKind.Parts;
                var validation = SpritePartsValidation.Validate(profile);
                if (validation.Ok)
                    return new KindSwitchResult { Ok = true };
                profile.AnimKind = previous;
                return new KindSwitchResult { Reason = SummarizeErrors(validation.Errors) };
            }

            if (kind == SpriteAnimKind.Static)
            {
                if (HasUsableStaticSheet(profile))
                {
                    profile.AnimKind = SpriteAnimKind.Static;
                    return new KindSwitchResult { Ok = true };
                }
                return new KindSwitchResult
                {
                    Reason = "Static runtime needs a valid selected sheet and cell with a texture. " +
                             "Assign a sheet in the Static or Frames workspace first.",
                };
            }

            // Read-only: a rejected switch must not migrate legacy sheet fields.
            if (profile.Clips != null)
            {
                for (int i = 0; i < profile.Clips.Count; i++)
                {
                    var clip = profile.Clips[i];
                    if (clip?.Frames == null || clip.Frames.Length == 0)
                        continue;
                    var sheet = profile.SheetForClip(clip);
                    if (sheet != null && sheet.Texture != null)
                    {
                        profile.AnimKind = SpriteAnimKind.Frame;
                        return new KindSwitchResult { Ok = true };
                    }
                }
            }
            return new KindSwitchResult
            {
                Reason = "Frames runtime needs at least one frame clip on a sheet with a texture. " +
                         "Assign a sheet in the Frames workspace first.",
            };
        }

        /// <summary>
        /// True when a sheet texture exists without calling EnsureSheets (peek-safe).
        /// </summary>
        public static bool HasUsableStaticSheet(SpriteSheetProfile profile)
            => SpriteProfileSheetOps.HasValidStaticCell(profile);

        /// <summary>
        /// Peek variant of <see cref="TrySetAnimKind"/>: validates and restores
        /// the previous kind, so callers can check first and only record Undo
        /// for switches that will actually apply.
        /// </summary>
        public static bool CanSetAnimKind(SpriteSheetProfile profile, SpriteAnimKind kind, out string reason)
        {
            var previous = profile?.AnimKind ?? SpriteAnimKind.Frame;
            var result = TrySetAnimKind(profile, kind);
            reason = result.Reason;
            if (result.Ok && profile != null && previous != kind)
                profile.AnimKind = previous;
            return result.Ok;
        }

        static string SummarizeErrors(List<string> errors)
        {
            if (errors == null || errors.Count == 0)
                return "Parts data is not usable yet.";
            return errors.Count == 1
                ? errors[0]
                : errors[0] + $" (and {errors.Count - 1} more)";
        }

        public struct BindAppearanceResult
        {
            public bool Ok;
            public string Reason;
            public string AppearanceId;
        }

        /// <summary>Bind an existing profile appearance as the slot's default art.</summary>
        public static BindAppearanceResult SetSlotDefaultAppearance(
            SpriteSheetProfile profile, string slotId, string appearanceId)
        {
            if (profile == null)
                return new BindAppearanceResult { Reason = "Profile is null." };
            var slot = FindSlot(profile, slotId);
            if (slot == null)
                return new BindAppearanceResult { Reason = $"Slot '{slotId}' not found." };
            var app = FindAppearance(profile, appearanceId);
            if (app == null)
                return new BindAppearanceResult
                {
                    Reason = $"Appearance '{appearanceId}' is not in this profile. Use Import from Profile first.",
                };
            string id = SpritePartIdUtility.Canonical(app.AppearanceId, app.Name);
            slot.DefaultAppearanceId = id;
            SpritePartsValidation.CanonicalizeIds(profile);
            return new BindAppearanceResult { Ok = true, AppearanceId = id };
        }

        /// <summary>
        /// Bind a sheet cell of this profile as the slot's default art. Reuses an
        /// appearance already pointing at that cell, otherwise creates one.
        /// </summary>
        public static BindAppearanceResult BindSlotArtFromCell(
            SpriteSheetProfile profile, string slotId, int sheetIndex, int cellIndex)
        {
            if (profile == null)
                return new BindAppearanceResult { Reason = "Profile is null." };
            var slot = FindSlot(profile, slotId);
            if (slot == null)
                return new BindAppearanceResult { Reason = $"Slot '{slotId}' not found." };
            profile.EnsureSheets();
            var sheet = profile.SheetAt(sheetIndex);
            if (sheet == null || sheet.Texture == null)
                return new BindAppearanceResult { Reason = $"Sheet {sheetIndex} has no texture." };
            int columns = Mathf.Max(1, sheet.Columns);
            int rows = Mathf.Max(1, sheet.Rows);
            int cells = columns * rows;
            cellIndex = Mathf.Clamp(cellIndex, 0, cells - 1);

            SpritePartAppearanceDef app = null;
            for (int i = 0; i < profile.PartsAppearances.Count; i++)
            {
                var a = profile.PartsAppearances[i];
                if (a != null && a.SheetIndex == sheetIndex && a.CellIndex == cellIndex)
                {
                    app = a;
                    break;
                }
            }
            if (app == null)
            {
                string desired = SpritePartIdUtility.Canonical(slot.SlotId) + ".cell" + cellIndex;
                string id = desired;
                int n = 2;
                while (FindAppearance(profile, id) != null)
                    id = desired + n++;
                app = new SpritePartAppearanceDef
                {
                    Name = (string.IsNullOrWhiteSpace(slot.Name) ? slot.SlotId : slot.Name) + " Cell " + cellIndex,
                    AppearanceId = id,
                    SheetIndex = sheetIndex,
                    CellIndex = cellIndex,
                    LogicalWorldSize = Vector2.zero,
                    PivotSource = SpritePartPivotSource.SheetDefault,
                };
                profile.PartsAppearances.Add(app);
            }

            string boundId = SpritePartIdUtility.Canonical(app.AppearanceId, app.Name);
            slot.DefaultAppearanceId = boundId;
            SpritePartsValidation.CanonicalizeIds(profile);
            return new BindAppearanceResult { Ok = true, AppearanceId = boundId };
        }

        public struct BatchAppearanceResult
        {
            public bool Ok;
            public string Reason;
            public int Created;
            public int Reused;
            public List<string> AppearanceIds;
        }

        /// <summary>
        /// Batch-create one appearance per selected sheet cell (From This
        /// Profile batch mode). Exact sheet+cell matches are reused; new ids
        /// derive from the sheet name and are unique on the profile.
        /// </summary>
        public static BatchAppearanceResult CreateAppearancesForCells(
            SpriteSheetProfile profile, int sheetIndex, List<int> cellIndices)
        {
            var result = new BatchAppearanceResult
            {
                AppearanceIds = new List<string>(),
            };
            if (profile == null)
            {
                result.Reason = "Profile is null.";
                return result;
            }
            profile.EnsureSheets();
            var sheet = profile.SheetAt(sheetIndex);
            if (sheet == null || sheet.Texture == null)
            {
                result.Reason = $"Sheet {sheetIndex} has no texture.";
                return result;
            }
            if (cellIndices == null || cellIndices.Count == 0)
            {
                result.Reason = "No cells selected.";
                return result;
            }
            int columns = Mathf.Max(1, sheet.Columns);
            int rows = Mathf.Max(1, sheet.Rows);
            int cells = columns * rows;
            string sheetBase = SpritePartIdUtility.Canonical(
                string.IsNullOrWhiteSpace(sheet.Name) ? "sheet" : sheet.Name);

            var taken = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < profile.PartsAppearances.Count; i++)
            {
                var a = profile.PartsAppearances[i];
                if (a != null)
                    taken.Add(SpritePartIdUtility.Canonical(a.AppearanceId, a.Name));
            }

            for (int c = 0; c < cellIndices.Count; c++)
            {
                int cell = Mathf.Clamp(cellIndices[c], 0, cells - 1);
                SpritePartAppearanceDef match = null;
                for (int i = 0; i < profile.PartsAppearances.Count; i++)
                {
                    var a = profile.PartsAppearances[i];
                    if (a != null && a.SheetIndex == sheetIndex && a.CellIndex == cell)
                    {
                        match = a;
                        break;
                    }
                }
                if (match != null)
                {
                    result.Reused++;
                    result.AppearanceIds.Add(SpritePartIdUtility.Canonical(match.AppearanceId, match.Name));
                    continue;
                }
                string desired = sheetBase + ".cell" + cell;
                string id = desired;
                int n = 2;
                while (taken.Contains(id))
                    id = desired + n++;
                taken.Add(id);
                profile.PartsAppearances.Add(new SpritePartAppearanceDef
                {
                    Name = (string.IsNullOrWhiteSpace(sheet.Name) ? "Sheet" : sheet.Name) + " Cell " + cell,
                    AppearanceId = id,
                    SheetIndex = sheetIndex,
                    CellIndex = cell,
                    LogicalWorldSize = Vector2.zero,
                    PivotSource = SpritePartPivotSource.SheetDefault,
                });
                result.Created++;
                result.AppearanceIds.Add(id);
            }
            SpritePartsValidation.CanonicalizeIds(profile);
            result.Ok = true;
            return result;
        }

        /// <summary>
        /// Preview resolve: keyed clip appearance, else skin preview, else slot default.
        /// </summary>
        public static string ResolvePreviewAppearanceId(
            SpriteSheetProfile profile,
            SpritePartSlotDef slot,
            int clipIndex,
            float timeSeconds,
            Dictionary<string, string> previewOverrides)
        {
            if (slot == null) return string.Empty;
            string keyed = SampleKeyedAppearanceId(profile, clipIndex, slot.SlotId, timeSeconds);
            if (!string.IsNullOrEmpty(keyed))
                return keyed;
            return ResolveAppearanceId(slot, previewOverrides);
        }

        public static SpritePartSlotDef FindSlot(SpriteSheetProfile profile, string slotId)
        {
            if (profile?.PartsSlots == null) return null;
            string id = SpritePartIdUtility.Canonical(slotId);
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var s = profile.PartsSlots[i];
                if (s != null && SpritePartIdUtility.Canonical(s.SlotId) == id)
                    return s;
            }
            return null;
        }

        public static int FindSlotIndex(SpriteSheetProfile profile, string slotId)
        {
            if (profile?.PartsSlots == null) return -1;
            string id = SpritePartIdUtility.Canonical(slotId);
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var s = profile.PartsSlots[i];
                if (s != null && SpritePartIdUtility.Canonical(s.SlotId) == id)
                    return i;
            }
            return -1;
        }

        public static int FindClipIndex(SpriteSheetProfile profile, string clipId)
        {
            if (profile?.PartsClips == null) return -1;
            string id = SpritePartIdUtility.Canonical(clipId);
            for (int i = 0; i < profile.PartsClips.Count; i++)
            {
                var c = profile.PartsClips[i];
                if (c != null && SpritePartIdUtility.Canonical(c.ClipId) == id)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Transient skin preview: SlotId → AppearanceId overrides. Does not mutate profile skins.
        /// </summary>
        public static Dictionary<string, string> ApplySkinPreview(
            SpriteSheetProfile profile, string skinId)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (profile?.PartsSkins == null) return map;
            string id = SpritePartIdUtility.Canonical(skinId);
            for (int i = 0; i < profile.PartsSkins.Count; i++)
            {
                var skin = profile.PartsSkins[i];
                if (skin == null || SpritePartIdUtility.Canonical(skin.SkinId) != id)
                    continue;
                skin.Bindings ??= new List<SpritePartsSkinBindingDef>();
                for (int b = 0; b < skin.Bindings.Count; b++)
                {
                    var binding = skin.Bindings[b];
                    if (binding == null) continue;
                    map[SpritePartIdUtility.Canonical(binding.SlotId)] =
                        SpritePartIdUtility.Canonical(binding.AppearanceId);
                }
                break;
            }
            return map;
        }

        /// <summary>Persist preview overrides into a named skin patch (Save Skin).</summary>
        public static void SaveSkinFromPreview(
            SpriteSheetProfile profile,
            string skinId,
            string skinName,
            Dictionary<string, string> previewOverrides)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.EnsurePartsRig();
            string id = SpritePartIdUtility.Canonical(skinId, skinName);
            SpritePartsSkinDef skin = null;
            for (int i = 0; i < profile.PartsSkins.Count; i++)
            {
                if (SpritePartIdUtility.Canonical(profile.PartsSkins[i].SkinId) == id)
                {
                    skin = profile.PartsSkins[i];
                    break;
                }
            }
            if (skin == null)
            {
                skin = new SpritePartsSkinDef { SkinId = id, Name = skinName ?? id };
                profile.PartsSkins.Add(skin);
            }
            skin.Bindings ??= new List<SpritePartsSkinBindingDef>();
            skin.Bindings.Clear();
            if (previewOverrides != null)
            {
                foreach (var kv in previewOverrides)
                {
                    skin.Bindings.Add(new SpritePartsSkinBindingDef
                    {
                        SlotId = SpritePartIdUtility.Canonical(kv.Key),
                        AppearanceId = SpritePartIdUtility.Canonical(kv.Value),
                    });
                }
            }
            SpritePartsValidation.CanonicalizeIds(profile);
        }

        /// <summary>
        /// Resolve appearance id for a slot given default + optional transient preview map.
        /// Preview never writes into slot.DefaultAppearanceId.
        /// </summary>
        public static string ResolveAppearanceId(
            SpritePartSlotDef slot,
            Dictionary<string, string> previewOverrides)
        {
            if (slot == null) return string.Empty;
            string sid = SpritePartIdUtility.Canonical(slot.SlotId);
            if (previewOverrides != null &&
                previewOverrides.TryGetValue(sid, out var overrideId) &&
                !string.IsNullOrEmpty(overrideId))
                return overrideId;
            return slot.DefaultAppearanceId ?? string.Empty;
        }

        public static float SnapTime(float time, float fps, float duration)
        {
            if (!(fps > 0f)) return Mathf.Clamp(time, 0f, duration);
            float step = 1f / fps;
            float snapped = Mathf.Round(time / step) * step;
            return Mathf.Clamp(snapped, 0f, duration);
        }

        static SpritePartsTrackDef GetOrCreateTrack(SpritePartsClipDef clip, string slotId)
        {
            clip.Tracks ??= new List<SpritePartsTrackDef>();
            var existing = FindTrack(clip, slotId, SpritePartsTrackKind.Pose);
            if (existing != null) return existing;
            var track = new SpritePartsTrackDef
            {
                SlotId = SpritePartIdUtility.Canonical(slotId),
                Kind = SpritePartsTrackKind.Pose,
                Keys = new List<SpritePartsKeyDef>(),
            };
            clip.Tracks.Add(track);
            return track;
        }

        /// <summary>
        /// Ensure the slot's independent appearance channel. Creating it migrates
        /// legacy ids mixed into the pose track into the channel (same times,
        /// same ids) and blanks them on the pose keys, so after any channel
        /// write the channel is the single source for that slot. Pose key TRS
        /// values are never touched.
        /// </summary>
        public static SpritePartsTrackDef EnsureAppearanceTrack(
            SpritePartsClipDef clip, string slotId)
        {
            clip.Tracks ??= new List<SpritePartsTrackDef>();
            var existing = FindAppearanceTrack(clip, slotId);
            if (existing != null)
                return existing;
            var channel = new SpritePartsTrackDef
            {
                SlotId = SpritePartIdUtility.Canonical(slotId),
                Kind = SpritePartsTrackKind.Appearance,
                Keys = new List<SpritePartsKeyDef>(),
            };
            var pose = FindTrack(clip, slotId, SpritePartsTrackKind.Pose);
            if (pose?.Keys != null)
            {
                for (int i = 0; i < pose.Keys.Count; i++)
                {
                    var key = pose.Keys[i];
                    if (key == null || string.IsNullOrWhiteSpace(key.AppearanceId))
                        continue;
                    channel.Keys.Add(new SpritePartsKeyDef
                    {
                        Time = key.Time,
                        EaseMode = (byte)SpriteEaseMode.Step,
                        AppearanceId = SpritePartIdUtility.Canonical(key.AppearanceId),
                    });
                    key.AppearanceId = string.Empty;
                }
            }
            clip.Tracks.Add(channel);
            return channel;
        }

        /// <summary>
        /// Upsert an appearance key on the channel. Pose fields stay default;
        /// sampling is step/hold. Same snapped time replaces the existing key.
        /// </summary>
        static void UpsertAppearanceKey(SpritePartsTrackDef channel, float time, string canonicalAppearanceId)
        {
            channel.Keys ??= new List<SpritePartsKeyDef>();
            var existing = FindKeyAtTime(channel, time);
            if (existing != null)
            {
                existing.AppearanceId = canonicalAppearanceId;
                existing.EaseMode = (byte)SpriteEaseMode.Step;
                return;
            }
            channel.Keys.Add(new SpritePartsKeyDef
            {
                Time = time,
                EaseMode = (byte)SpriteEaseMode.Step,
                AppearanceId = canonicalAppearanceId,
            });
            channel.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        static void UpsertKey(
            SpritePartsTrackDef track,
            float time,
            PoseEdit pose,
            string appearanceId = null,
            bool setAppearance = false)
        {
            track.Keys ??= new List<SpritePartsKeyDef>();
            var existing = FindKeyAtTime(track, time);
            if (existing != null)
            {
                existing.Position = pose.Position;
                existing.Rotation = pose.Rotation;
                existing.Scale = SanitizeScale(pose.Scale);
                existing.Deform = pose.Lattice.OffsetArray();
                if (setAppearance)
                    existing.AppearanceId = appearanceId ?? string.Empty;
                return;
            }
            track.Keys.Add(new SpritePartsKeyDef
            {
                Time = time,
                Position = pose.Position,
                Rotation = pose.Rotation,
                Scale = SanitizeScale(pose.Scale),
                Deform = pose.Lattice.OffsetArray(),
                EaseMode = (byte)SpriteEaseMode.Linear,
                AppearanceId = setAppearance ? (appearanceId ?? string.Empty) : string.Empty,
            });
            track.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        static Vector2 SanitizeScale(Vector2 scale)
        {
            // Allow negative axes so Flip Horizontal/Vertical can use RestScale / key Scale.
            return new Vector2(SanitizeScaleAxis(scale.x), SanitizeScaleAxis(scale.y));
        }

        static float SanitizeScaleAxis(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || Mathf.Abs(v) < 1e-5f)
                return v < 0f ? -1f : 1f;
            return v;
        }

        public static bool HasStoredRootBounds(SpriteSheetProfile profile)
            => profile != null &&
               profile.PartsRootBoundsSize.x > 1e-5f &&
               profile.PartsRootBoundsSize.y > 1e-5f;

        public static void SetRootBounds(SpriteSheetProfile profile, float2 center, float2 size)
        {
            if (profile == null)
                return;
            size = math.max(size, new float2(0.01f, 0.01f));
            profile.PartsRootBoundsCenter = new Vector2(center.x, center.y);
            profile.PartsRootBoundsSize = new Vector2(size.x, size.y);
        }

        /// <summary>
        /// Writes a bounds rect that frames the pose AABB. Does not change rest poses or keys.
        /// </summary>
        public static void FitRootBoundsToAabb(SpriteSheetProfile profile, float2 min, float2 max,
            float padding = 0.06f)
        {
            float2 size = math.max(max - min, new float2(0.01f, 0.01f));
            size += size * math.max(0f, padding);
            SetRootBounds(profile, 0.5f * (min + max), size);
        }

        public static bool TryEncapsulateWorldAabb(
            SpriteSheetProfile profile,
            Dictionary<string, string> previewOverrides,
            ref SpritePartsSetBlob set,
            NativeArray<float4x4> matrices,
            int clipIndex,
            float timeSeconds,
            out float2 min,
            out float2 max)
        {
            min = new float2(float.PositiveInfinity, float.PositiveInfinity);
            max = new float2(float.NegativeInfinity, float.NegativeInfinity);
            if (profile == null || !matrices.IsCreated)
                return false;

            bool any = false;
            int n = math.min(set.Slots.Length, matrices.Length);
            for (int i = 0; i < n; i++)
            {
                string sid = set.Slots[i].SlotId.ToString();
                if (SlotOrAncestorHidden(profile, sid))
                    continue;
                var slot = FindSlot(profile, sid);
                var app = FindAppearance(profile, ResolvePreviewAppearanceId(
                    profile, slot, clipIndex, timeSeconds, previewOverrides));
                if (app == null ||
                    !SpritePartsGeometry.TryResolve(profile, app, false, out var geo, out _))
                    continue;

                float4x4 m = matrices[i];
                EncapsulateVisualCorner(ref min, ref max, m, geo, new float2(-0.5f, -0.5f));
                EncapsulateVisualCorner(ref min, ref max, m, geo, new float2(0.5f, -0.5f));
                EncapsulateVisualCorner(ref min, ref max, m, geo, new float2(0.5f, 0.5f));
                EncapsulateVisualCorner(ref min, ref max, m, geo, new float2(-0.5f, 0.5f));
                any = true;
            }

            return any && math.all(math.isfinite(min)) && math.all(math.isfinite(max)) &&
                   max.x > min.x && max.y > min.y;
        }

        static void EncapsulateVisualCorner(
            ref float2 min, ref float2 max, float4x4 matrix,
            SpritePartsGeometry.Resolved geo, float2 quad)
        {
            float2 local = SpritePartsGeometry.VisualPoint(quad, geo.Pivot, geo.LogicalWorldSize);
            float4 world = math.mul(matrix, new float4(local.x, local.y, 0f, 1f));
            min = math.min(min, world.xy);
            max = math.max(max, world.xy);
        }
    }
}


