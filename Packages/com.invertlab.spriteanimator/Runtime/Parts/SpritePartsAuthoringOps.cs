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

        public struct PoseEdit
        {
            public Vector2 Position;
            public float Rotation;
            public Vector2 Scale;
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
            if (!result.WroteKey || !includeAppearance)
                return result;
            return WriteKeyAppearance(profile, clipIndex, slotId, timeSeconds, appearanceId, snapFps,
                ensurePoseFrom: pose);
        }

        /// <summary>
        /// Upsert a sprite/appearance key at playhead. Empty appearanceId clears the key's
        /// appearance field (hold). Missing track/key creates one from current/rest pose.
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
            return WriteKeyAppearance(profile, clipIndex, slotId, timeSeconds, appearanceId, snapFps,
                ensurePoseFrom: poseFallback);
        }

        static WriteResult WriteKeyAppearance(
            SpriteSheetProfile profile,
            int clipIndex,
            string slotId,
            float timeSeconds,
            string appearanceId,
            float snapFps,
            PoseEdit? ensurePoseFrom)
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
            var track = GetOrCreateTrack(clip, slot.SlotId);
            var existing = FindKeyAtTime(track, time);
            if (existing == null)
            {
                PoseEdit pose = ensurePoseFrom ?? new PoseEdit
                {
                    Position = slot.RestPosition,
                    Rotation = slot.RestRotation,
                    Scale = slot.RestScale,
                };
                UpsertKey(track, time, pose, appearanceId: aid, setAppearance: true);
                result.WroteKey = true;
            }
            else
            {
                existing.AppearanceId = aid;
                result.WroteKey = true;
            }
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
            var track = FindTrack(clip, slotId);
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

        public static SpritePartsTrackDef FindTrack(SpritePartsClipDef clip, string slotId)
        {
            if (clip?.Tracks == null) return null;
            string id = SpritePartIdUtility.Canonical(slotId);
            for (int i = 0; i < clip.Tracks.Count; i++)
            {
                var t = clip.Tracks[i];
                if (t != null && SpritePartIdUtility.Canonical(t.SlotId) == id)
                    return t;
            }
            return null;
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
            var existing = FindTrack(clip, slotId);
            if (existing != null) return existing;
            var track = new SpritePartsTrackDef
            {
                SlotId = SpritePartIdUtility.Canonical(slotId),
                Keys = new List<SpritePartsKeyDef>(),
            };
            clip.Tracks.Add(track);
            return track;
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
                EaseMode = (byte)SpriteEaseMode.Linear,
                AppearanceId = setAppearance ? (appearanceId ?? string.Empty) : string.Empty,
            });
            track.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        static Vector2 SanitizeScale(Vector2 scale)
        {
            float sx = scale.x <= 0f || !!(float.IsNaN(scale.x) || float.IsInfinity(scale.x)) ? 1f : scale.x;
            float sy = scale.y <= 0f || !!(float.IsNaN(scale.y) || float.IsInfinity(scale.y)) ? 1f : scale.y;
            return new Vector2(sx, sy);
        }
    }
}


