using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    public static partial class SpritePartsAuthoringOps
    {
        /// <summary>
        /// Breakdown (a tween-machine favor): keys the part at <paramref name="time"/> between the keys before and after
        /// it, channel by channel: 0 = the previous pose, 1 = the next, 0.5 = halfway (below 0 / above 1 overshoot).
        /// Rotation turns the short way; deform blends when both keys hold one of the same size.
        /// </summary>
        public static KeyEditResult KeyBreakdown(SpriteSheetProfile profile, int clipIndex, string slotId, float time, float favor)
        {
            var result = new KeyEditResult();
            var clip = GetClip(profile, clipIndex);
            var track = clip != null ? FindTrack(clip, slotId) : null;
            if (track?.Keys == null)
            {
                result.Reason = "No keys on this part.";
                return result;
            }
            const float eps = 1e-4f;
            var channels = SpritePartsKeyChannel.None;
            var values = new SpritePartsKeyDef();
            foreach (var channel in new[] { SpritePartsKeyChannel.Position, SpritePartsKeyChannel.Rotation, SpritePartsKeyChannel.Scale, SpritePartsKeyChannel.Deform })
            {
                SpritePartsKeyDef a = null, b = null;
                foreach (var k in track.Keys)
                {
                    if (k == null || (k.Channels & channel) == 0)
                        continue;
                    if (k.Time < time - eps && (a == null || k.Time > a.Time))
                        a = k;
                    if (k.Time > time + eps && (b == null || k.Time < b.Time))
                        b = k;
                }
                if (a == null || b == null)
                    continue;
                switch (channel)
                {
                    case SpritePartsKeyChannel.Position:
                        values.Position = Vector2.LerpUnclamped(a.Position, b.Position, favor);
                        break;
                    case SpritePartsKeyChannel.Rotation:
                        values.Rotation = a.Rotation + Mathf.DeltaAngle(a.Rotation, b.Rotation) * favor;
                        break;
                    case SpritePartsKeyChannel.Scale:
                        values.Scale = Vector2.LerpUnclamped(a.Scale, b.Scale, favor);
                        break;
                    case SpritePartsKeyChannel.Deform:
                        if (a.Deform == null || b.Deform == null || a.Deform.Length != b.Deform.Length)
                            continue;
                        values.Deform = new Vector2[a.Deform.Length];
                        for (int v = 0; v < a.Deform.Length; v++)
                            values.Deform[v] = Vector2.LerpUnclamped(a.Deform[v], b.Deform[v], favor);
                        break;
                }
                channels |= channel;
            }
            if (channels == SpritePartsKeyChannel.None)
            {
                result.Reason = "Needs a key before and after the playhead.";
                return result;
            }
            var key = FindKeyAtTime(track, time);
            if (key == null)
            {
                key = new SpritePartsKeyDef { Time = time, Channels = SpritePartsKeyChannel.None, AppearanceId = string.Empty };
                track.Keys.Add(key);
                track.Keys.Sort((x, y) => x.Time.CompareTo(y.Time));
            }
            if ((channels & SpritePartsKeyChannel.Position) != 0) key.Position = values.Position;
            if ((channels & SpritePartsKeyChannel.Rotation) != 0) key.Rotation = values.Rotation;
            if ((channels & SpritePartsKeyChannel.Scale) != 0) key.Scale = SanitizeScale(values.Scale);
            if ((channels & SpritePartsKeyChannel.Deform) != 0) key.Deform = values.Deform;
            key.Channels |= channels;
            result.Ok = true;
            result.Affected = 1;
            return result;
        }

        /// <summary><see cref="KeyBreakdown"/> for several parts; parts with no keys around the time are skipped.</summary>
        public static KeyEditResult KeyBreakdown(SpriteSheetProfile profile, int clipIndex, IEnumerable<string> slotIds, float time, float favor)
        {
            var result = new KeyEditResult();
            string reason = null;
            foreach (string id in slotIds)
            {
                var one = KeyBreakdown(profile, clipIndex, id, time, favor);
                if (one.Ok)
                    result.Affected++;
                else
                    reason ??= one.Reason;
            }
            result.Ok = result.Affected > 0;
            result.Reason = result.Ok ? null : reason ?? "Select parts.";
            return result;
        }
    }
}
