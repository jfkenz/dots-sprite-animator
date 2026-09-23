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
            bool snap,
            bool merge = true)
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
            if (merge)
                MergeSameTrackTimeCollisions(clip, keys);
            else
                SortTracks(clip);
            result.Ok = true;
            result.Affected = keys.Count;
            return result;
        }

        /// <summary>
        /// Stretches (factor &gt; 1) or squeezes the keys' times around <paramref name="pivot"/>: t' = pivot + (t - pivot) x factor,
        /// kept inside the clip and snapped to frames; keys landing together merge.
        /// </summary>
        public static KeyEditResult ScaleKeyTimes(SpriteSheetProfile profile, int clipIndex, ICollection<SpritePartsKeyDef> keys,
            float pivot, float factor, float snapFps)
        {
            var result = new KeyEditResult();
            var clip = GetClip(profile, clipIndex);
            if (clip == null || keys == null || keys.Count == 0 || !(factor > 0f) || float.IsInfinity(factor))
            {
                result.Reason = "Select keys and a scale above 0.";
                return result;
            }
            float duration = Mathf.Max(1e-3f, clip.Duration);
            var moved = new List<SpritePartsKeyDef>();
            foreach (var key in keys)
            {
                if (key == null) continue;
                key.Time = SnapTime(Mathf.Clamp(pivot + (key.Time - pivot) * factor, 0f, duration), snapFps, duration);
                moved.Add(key);
            }
            MergeSameTrackTimeCollisions(clip, moved);
            result.Ok = true;
            result.Affected = moved.Count;
            return result;
        }

        /// <summary>
        /// Spine's Offset: the selected keys of each part move <paramref name="step"/> seconds more than the previous
        /// part's (part order = <paramref name="slotOrder"/>), for overlapping motion. Loop clips wrap keys around the
        /// end; Once clips clamp. Keys landing together merge.
        /// </summary>
        public static KeyEditResult OffsetKeysByPart(SpriteSheetProfile profile, int clipIndex, ICollection<SpritePartsKeyDef> keys,
            IList<string> slotOrder, float step, float snapFps)
        {
            var result = new KeyEditResult();
            var clip = GetClip(profile, clipIndex);
            if (clip?.Tracks == null || keys == null || keys.Count == 0 || slotOrder == null)
            {
                result.Reason = "Select keys on two or more parts.";
                return result;
            }
            float duration = Mathf.Max(1e-3f, clip.Duration);
            bool loop = clip.WrapMode != (byte)SpritePartsWrap.Once;
            var set = keys as HashSet<SpritePartsKeyDef> ?? new HashSet<SpritePartsKeyDef>(keys);
            var moved = new List<SpritePartsKeyDef>();
            int rank = 0;
            foreach (string slotId in slotOrder)
            {
                string id = SpritePartIdUtility.Canonical(slotId);
                bool any = false;
                foreach (var track in clip.Tracks)
                {
                    if (track?.Keys == null || SpritePartIdUtility.Canonical(track.SlotId) != id)
                        continue;
                    foreach (var key in track.Keys)
                    {
                        if (key == null || !set.Contains(key))
                            continue;
                        float t = key.Time + rank * step;
                        if (loop)
                        {
                            t %= duration;
                            if (t < 0f) t += duration;
                        }
                        key.Time = SnapTime(Mathf.Clamp(t, 0f, duration), snapFps, duration);
                        moved.Add(key);
                        any = true;
                    }
                }
                if (any)
                    rank++;
            }
            MergeSameTrackTimeCollisions(clip, moved);
            result.Ok = moved.Count > 0;
            result.Affected = moved.Count;
            if (!result.Ok)
                result.Reason = "No selected keys on those parts.";
            return result;
        }

        /// <summary>After a drag: keys that landed on the same time merge (the moved ones win on their channels).</summary>
        public static void MergeKeyCollisions(SpriteSheetProfile profile, int clipIndex, IList<SpritePartsKeyDef> moved)
        {
            var clip = GetClip(profile, clipIndex);
            if (clip != null)
                MergeSameTrackTimeCollisions(clip, moved ?? new List<SpritePartsKeyDef>());
        }

        static void SortTracks(SpritePartsClipDef clip)
        {
            if (clip?.Tracks == null) return;
            foreach (var track in clip.Tracks)
                track?.Keys?.Sort((a, b) => (a?.Time ?? 0f).CompareTo(b?.Time ?? 0f));
        }

        /// <summary>
        /// Splits <paramref name="channels"/> off each key into its own key at the same time (for moving one channel's
        /// timing alone). A key that holds only those channels is returned as it is. Returns the keys to move.
        /// </summary>
        public static List<SpritePartsKeyDef> SplitKeyChannels(
            SpriteSheetProfile profile, int clipIndex, IEnumerable<SpritePartsKeyDef> keys, SpritePartsKeyChannel channels)
        {
            var result = new List<SpritePartsKeyDef>();
            var clip = GetClip(profile, clipIndex);
            if (clip?.Tracks == null || keys == null)
                return result;
            foreach (var key in keys)
            {
                if (key == null)
                    continue;
                var take = key.Channels & channels;
                if (take == SpritePartsKeyChannel.None)
                    continue;
                bool alone = (key.Channels & ~channels) == SpritePartsKeyChannel.None && !key.HasColor && !key.HasDrawOrder;
                if (alone)
                {
                    result.Add(key);
                    continue;
                }
                var track = clip.Tracks.Find(t => t?.Keys != null && t.Keys.Contains(key));
                if (track == null)
                    continue;
                var part = CloneKey(key);
                part.Channels = take;
                part.HasColor = false;
                part.HasDrawOrder = false;
                part.AppearanceId = string.Empty;
                key.Channels &= ~take;
                track.Keys.Insert(track.Keys.IndexOf(key) + 1, part);
                result.Add(part);
            }
            return result;
        }

        /// <summary>
        /// Takes <paramref name="channels"/> out of the keys; a key left with nothing (no channel, colour, draw order
        /// or sprite) is deleted. Returns how many keys changed.
        /// </summary>
        public static int RemoveKeyChannels(
            SpriteSheetProfile profile, int clipIndex, ICollection<SpritePartsKeyDef> keys, SpritePartsKeyChannel channels)
        {
            var clip = GetClip(profile, clipIndex);
            if (clip?.Tracks == null || keys == null)
                return 0;
            int changed = 0;
            foreach (var track in clip.Tracks)
            {
                if (track?.Keys == null)
                    continue;
                for (int k = track.Keys.Count - 1; k >= 0; k--)
                {
                    var key = track.Keys[k];
                    if (key == null || !keys.Contains(key) || (key.Channels & channels) == 0)
                        continue;
                    key.Channels &= ~channels;
                    changed++;
                    if (key.Channels == SpritePartsKeyChannel.None && !key.HasColor && !key.HasDrawOrder && !key.HasClipActive
                        && string.IsNullOrWhiteSpace(key.AppearanceId))
                        track.Keys.RemoveAt(k);
                }
            }
            return changed;
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
                Deform = src.Deform == null ? null : (Vector2[])src.Deform.Clone(),
                HasColor = src.HasColor,
                Color = src.Color,
                HasDrawOrder = src.HasDrawOrder,
                DrawOrder = src.DrawOrder,
                Channels = src.Channels,
                HasClipActive = src.HasClipActive,
                ClipActive = src.ClipActive,
                Curve = src.Curve,
                AppearanceId = src.AppearanceId ?? string.Empty,
            };
        }

        /// <summary>
        /// Copies <paramref name="from"/>'s channels (and colour / draw order) into <paramref name="into"/>.
        /// <paramref name="onlyMissing"/>: only channels <paramref name="into"/> does not hold yet.
        /// </summary>
        public static void MergeKeyChannels(SpritePartsKeyDef into, SpritePartsKeyDef from, bool onlyMissing)
        {
            if (into == null || from == null)
                return;
            var take = from.Channels & (onlyMissing ? ~into.Channels : SpritePartsKeyChannel.All);
            if ((take & SpritePartsKeyChannel.Position) != 0) into.Position = from.Position;
            if ((take & SpritePartsKeyChannel.Rotation) != 0) into.Rotation = from.Rotation;
            if ((take & SpritePartsKeyChannel.Scale) != 0) into.Scale = SanitizeScale(from.Scale);
            if ((take & SpritePartsKeyChannel.Deform) != 0)
                into.Deform = from.Deform == null ? null : (Vector2[])from.Deform.Clone();
            into.Channels |= take;
            if (from.HasColor && (!onlyMissing || !into.HasColor))
            {
                into.HasColor = true;
                into.Color = from.Color;
            }
            if (from.HasDrawOrder && (!onlyMissing || !into.HasDrawOrder))
            {
                into.HasDrawOrder = true;
                into.DrawOrder = from.DrawOrder;
            }
            if (from.HasClipActive && (!onlyMissing || !into.HasClipActive))
            {
                into.HasClipActive = true;
                into.ClipActive = from.ClipActive;
            }
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
                    // Prefer incoming (moved/pasted) values on the channels it holds; keep existing instance.
                    if (!ReferenceEquals(k, incoming))
                    {
                        MergeKeyChannels(k, incoming, false);
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
                        // The kept key wins on its channels; the dropped one's other channels move into it.
                        MergeKeyChannels(keep, drop, true);
                        track.Keys.Remove(drop);
                    }
                }
            }
        }
    }
}
