using System;
using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    public static partial class SpritePartsAuthoringOps
    {
        public struct KeyEditResult
        {
            public bool Ok;
            public string Reason;
            public int Affected;
        }

        public static KeyEditResult DeleteKeys(
            SpriteSheetProfile profile, int clipIndex, ICollection<SpritePartsKeyDef> keys)
        {
            var result = new KeyEditResult();
            if (keys == null || keys.Count == 0)
            {
                result.Reason = "No keys selected.";
                return result;
            }
            var clip = GetClip(profile, clipIndex);
            if (clip == null)
            {
                result.Reason = "Clip not found.";
                return result;
            }
            clip.Tracks ??= new List<SpritePartsTrackDef>();
            var set = keys as HashSet<SpritePartsKeyDef> ?? new HashSet<SpritePartsKeyDef>(keys);
            int removed = 0;
            for (int t = 0; t < clip.Tracks.Count; t++)
            {
                var track = clip.Tracks[t];
                if (track?.Keys == null) continue;
                for (int k = track.Keys.Count - 1; k >= 0; k--)
                {
                    if (track.Keys[k] != null && set.Contains(track.Keys[k]))
                    {
                        track.Keys.RemoveAt(k);
                        removed++;
                    }
                }
            }
            result.Ok = removed > 0;
            result.Affected = removed;
            if (!result.Ok)
                result.Reason = "No matching keys found.";
            return result;
        }

        public static KeyEditResult MoveKeys(
            SpriteSheetProfile profile,
            int clipIndex,
            IList<SpritePartsKeyDef> keys,
            IList<float> startTimes,
            float deltaTime,
            float snapFps,
            bool snap)
        {
            var result = new KeyEditResult();
            var clip = GetClip(profile, clipIndex);
            if (clip == null)
            {
                result.Reason = "Clip not found.";
                return result;
            }
            if (keys == null || startTimes == null || keys.Count == 0 ||
                keys.Count != startTimes.Count)
            {
                result.Reason = "Invalid key drag set.";
                return result;
            }
            float duration = Mathf.Max(1e-3f, clip.Duration);
            // Apply new times first, then merge collisions per track.
            for (int i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                if (key == null) continue;
                float t = Mathf.Clamp(startTimes[i] + deltaTime, 0f, duration);
                if (snap)
                    t = SnapTime(t, snapFps, duration);
                key.Time = t;
            }
            MergeSameTrackTimeCollisions(clip, keys);
            result.Ok = true;
            result.Affected = keys.Count;
            return result;
        }

        public static KeyEditResult DuplicateKeys(
            SpriteSheetProfile profile,
            int clipIndex,
            ICollection<SpritePartsKeyDef> keys,
            float timeOffset,
            float snapFps,
            bool snap,
            List<SpritePartsKeyDef> createdOut)
        {
            var result = new KeyEditResult();
            createdOut?.Clear();
            var clip = GetClip(profile, clipIndex);
            if (clip == null)
            {
                result.Reason = "Clip not found.";
                return result;
            }
            if (keys == null || keys.Count == 0)
            {
                result.Reason = "No keys selected.";
                return result;
            }
            float duration = Mathf.Max(1e-3f, clip.Duration);
            int made = 0;
            // Locate each key's track, clone with offset.
            for (int t = 0; t < (clip.Tracks?.Count ?? 0); t++)
            {
                var track = clip.Tracks[t];
                if (track?.Keys == null) continue;
                var toAdd = new List<SpritePartsKeyDef>();
                for (int k = 0; k < track.Keys.Count; k++)
                {
                    var src = track.Keys[k];
                    if (src == null || !keys.Contains(src)) continue;
                    float tNew = Mathf.Clamp(src.Time + timeOffset, 0f, duration);
                    if (snap)
                        tNew = SnapTime(tNew, snapFps, duration);
                    var copy = CloneKey(src);
                    copy.Time = tNew;
                    toAdd.Add(copy);
                }
                for (int i = 0; i < toAdd.Count; i++)
                {
                    var live = UpsertOrMergeKey(track, toAdd[i]);
                    createdOut?.Add(live);
                    made++;
                }
                track.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
            }
            result.Ok = made > 0;
            result.Affected = made;
            if (!result.Ok)
                result.Reason = "Nothing duplicated.";
            return result;
        }

        public static KeyEditResult PasteKeysAtTime(
            SpriteSheetProfile profile,
            int clipIndex,
            IList<(string slotId, float relativeTime, SpritePartsKeyDef template)> clipboard,
            float playhead,
            float snapFps,
            bool snap,
            List<SpritePartsKeyDef> createdOut)
        {
            var result = new KeyEditResult();
            createdOut?.Clear();
            var clip = GetClip(profile, clipIndex);
            if (clip == null)
            {
                result.Reason = "Clip not found.";
                return result;
            }
            if (clipboard == null || clipboard.Count == 0)
            {
                result.Reason = "Clipboard empty.";
                return result;
            }
            float duration = Mathf.Max(1e-3f, clip.Duration);
            int made = 0;
            for (int i = 0; i < clipboard.Count; i++)
            {
                var entry = clipboard[i];
                if (entry.template == null) continue;
                var track = GetOrCreateTrack(clip, entry.slotId);
                float t = Mathf.Clamp(playhead + entry.relativeTime, 0f, duration);
                if (snap)
                    t = SnapTime(t, snapFps, duration);
                var copy = CloneKey(entry.template);
                copy.Time = t;
                var live = UpsertOrMergeKey(track, copy);
                createdOut?.Add(live);
                made++;
                track.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
            }
            result.Ok = made > 0;
            result.Affected = made;
            return result;
        }

        static SpritePartsClipDef GetClip(SpriteSheetProfile profile, int clipIndex)
        {
            if (profile?.PartsClips == null || clipIndex < 0 || clipIndex >= profile.PartsClips.Count)
                return null;
            return profile.PartsClips[clipIndex];
        }

        static SpritePartsKeyDef CloneKey(SpritePartsKeyDef src)
        {
            return new SpritePartsKeyDef
            {
                Time = src.Time,
                Position = src.Position,
                Rotation = src.Rotation,
                Scale = SanitizeScale(src.Scale),
                EaseMode = src.EaseMode,
                AppearanceId = src.AppearanceId ?? string.Empty,
            };
        }

        static SpritePartsKeyDef UpsertOrMergeKey(SpritePartsTrackDef track, SpritePartsKeyDef incoming)
        {
            track.Keys ??= new List<SpritePartsKeyDef>();
            const float eps = 1e-4f;
            for (int i = 0; i < track.Keys.Count; i++)
            {
                var k = track.Keys[i];
                if (k == null) continue;
                if (Mathf.Abs(k.Time - incoming.Time) <= eps)
                {
                    // Prefer incoming (moved/pasted) values; keep existing instance.
                    if (!ReferenceEquals(k, incoming))
                    {
                        k.Position = incoming.Position;
                        k.Rotation = incoming.Rotation;
                        k.Scale = SanitizeScale(incoming.Scale);
                        k.EaseMode = incoming.EaseMode;
                        k.AppearanceId = incoming.AppearanceId ?? string.Empty;
                    }
                    return k;
                }
            }
            track.Keys.Add(incoming);
            return incoming;
        }

        static void MergeSameTrackTimeCollisions(
            SpritePartsClipDef clip, IList<SpritePartsKeyDef> movedKeys)
        {
            if (clip?.Tracks == null) return;
            var moved = new HashSet<SpritePartsKeyDef>();
            for (int i = 0; i < movedKeys.Count; i++)
                if (movedKeys[i] != null) moved.Add(movedKeys[i]);

            for (int t = 0; t < clip.Tracks.Count; t++)
            {
                var track = clip.Tracks[t];
                if (track?.Keys == null || track.Keys.Count < 2) continue;
                track.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
                const float eps = 1e-4f;
                for (int k = track.Keys.Count - 1; k > 0; k--)
                {
                    var a = track.Keys[k - 1];
                    var b = track.Keys[k];
                    if (a == null || b == null) continue;
                    if (Mathf.Abs(a.Time - b.Time) > eps) continue;
                    // Keep the moved key when possible.
                    bool aMoved = moved.Contains(a);
                    bool bMoved = moved.Contains(b);
                    SpritePartsKeyDef keep = bMoved ? b : (aMoved ? a : b);
                    SpritePartsKeyDef drop = ReferenceEquals(keep, a) ? b : a;
                    if (!ReferenceEquals(keep, drop))
                    {
                        keep.Position = keep.Position;
                        // already has keep's TRS
                        track.Keys.Remove(drop);
                    }
                }
            }
        }
    }
}
