using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Keyed IK / jiggle / parameter values in clips (value tracks).</summary>
    public static partial class SpritePartsAuthoringOps
    {
        const float ValueKeyEpsilon = 1e-4f;

        public static SpritePartsValueTrackDef FindValueTrack(SpritePartsClipDef clip, SpritePartsValueKind kind, string target)
        {
            if (clip?.ValueTracks == null)
                return null;
            string name = (target ?? string.Empty).Trim();
            foreach (var t in clip.ValueTracks)
                if (t != null && t.Kind == kind && (t.Target ?? string.Empty).Trim() == name)
                    return t;
            return null;
        }

        /// <summary>The keyed value at <paramref name="time"/> (as the game plays it). False when the clip does not key it.</summary>
        public static bool TrySampleValue(SpritePartsClipDef clip, SpritePartsValueKind kind, string target, float time, out float value)
        {
            value = 0f;
            var track = FindValueTrack(clip, kind, target);
            if (track?.Keys == null || track.Keys.Count == 0)
                return false;
            var keys = SortedKeys(track);
            if (time <= keys[0].Time)
            {
                value = keys[0].Value;
                return true;
            }
            for (int i = 0; i + 1 < keys.Count; i++)
            {
                if (time >= keys[i + 1].Time)
                    continue;
                var a = keys[i];
                if (kind == SpritePartsValueKind.IkBend || kind == SpritePartsValueKind.SpriteGroup || a.EaseMode == (byte)SpriteEaseMode.Step)
                {
                    value = a.Value;
                    return true;
                }
                float span = keys[i + 1].Time - a.Time;
                float u = span > 1e-8f ? (time - a.Time) / span : 0f;
                u = a.EaseMode == (byte)SpriteEaseMode.Bezier
                    ? SpriteEase.EvaluateBezier(new Unity.Mathematics.float4(a.Curve.x, a.Curve.y, a.Curve.z, a.Curve.w), u)
                    : SpriteEase.Evaluate((SpriteEaseMode)a.EaseMode, u);
                value = Mathf.LerpUnclamped(a.Value, keys[i + 1].Value, u);
                return true;
            }
            value = keys[keys.Count - 1].Value;
            return true;
        }

        public static SpritePartsValueKeyDef ValueKeyAt(SpritePartsClipDef clip, SpritePartsValueKind kind, string target, float time)
        {
            var track = FindValueTrack(clip, kind, target);
            if (track?.Keys == null)
                return null;
            foreach (var k in track.Keys)
                if (k != null && Mathf.Abs(k.Time - time) <= ValueKeyEpsilon)
                    return k;
            return null;
        }

        /// <summary>Keys <paramref name="value"/> at <paramref name="time"/> (replacing a key there), making the track if needed.</summary>
        public static SpritePartsValueKeyDef SetValueKey(SpritePartsClipDef clip, SpritePartsValueKind kind, string target,
            float time, float value)
        {
            if (clip == null)
                return null;
            clip.ValueTracks ??= new List<SpritePartsValueTrackDef>();
            var track = FindValueTrack(clip, kind, target);
            if (track == null)
            {
                track = new SpritePartsValueTrackDef { Kind = kind, Target = (target ?? string.Empty).Trim() };
                clip.ValueTracks.Add(track);
            }
            track.Keys ??= new List<SpritePartsValueKeyDef>();
            time = Mathf.Clamp(time, 0f, Mathf.Max(1e-3f, clip.Duration));
            var key = ValueKeyAt(clip, kind, target, time);
            if (key == null)
            {
                key = new SpritePartsValueKeyDef { Time = time };
                track.Keys.Add(key);
                track.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
            }
            key.Value = value;
            return key;
        }

        /// <summary>Removes the key at <paramref name="time"/>; an emptied track is removed too.</summary>
        public static bool RemoveValueKey(SpritePartsClipDef clip, SpritePartsValueKind kind, string target, float time)
        {
            var track = FindValueTrack(clip, kind, target);
            var key = ValueKeyAt(clip, kind, target, time);
            if (track == null || key == null)
                return false;
            track.Keys.Remove(key);
            if (track.Keys.Count == 0)
                clip.ValueTracks.Remove(track);
            return true;
        }

        /// <summary>After an IK / jiggle / parameter rename: every clip's tracks follow it.</summary>
        public static int RenameValueTarget(SpriteSheetProfile profile, string oldName, string newName, params SpritePartsValueKind[] kinds)
        {
            int renamed = 0;
            string from = (oldName ?? string.Empty).Trim();
            string to = (newName ?? string.Empty).Trim();
            if (profile?.PartsClips == null || from == to)
                return 0;
            foreach (var clip in profile.PartsClips)
            {
                if (clip?.ValueTracks == null)
                    continue;
                foreach (var t in clip.ValueTracks)
                {
                    if (t == null || (t.Target ?? string.Empty).Trim() != from || System.Array.IndexOf(kinds, t.Kind) < 0)
                        continue;
                    t.Target = to;
                    renamed++;
                }
            }
            return renamed;
        }

        /// <summary>After an IK / jiggle / parameter is removed: its tracks go from every clip.</summary>
        public static int RemoveValueTracks(SpriteSheetProfile profile, string name, params SpritePartsValueKind[] kinds)
        {
            int removed = 0;
            string target = (name ?? string.Empty).Trim();
            if (profile?.PartsClips == null)
                return 0;
            foreach (var clip in profile.PartsClips)
                removed += clip?.ValueTracks?.RemoveAll(t => t != null && (t.Target ?? string.Empty).Trim() == target
                    && System.Array.IndexOf(kinds, t.Kind) >= 0) ?? 0;
            return removed;
        }

        static List<SpritePartsValueKeyDef> SortedKeys(SpritePartsValueTrackDef track)
        {
            var keys = track.Keys.FindAll(k => k != null);
            keys.Sort((a, b) => a.Time.CompareTo(b.Time));
            return keys;
        }
    }
}
