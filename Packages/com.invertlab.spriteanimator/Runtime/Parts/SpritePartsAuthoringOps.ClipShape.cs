using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    public static partial class SpritePartsAuthoringOps
    {
        /// <summary>
        /// Keys deform offsets (one per outline point) on a clip shape (Spine deform keys on clipping attachments).
        /// The first deform key after the start also gets a setup anchor at 0, like other channels.
        /// </summary>
        public static KeyEditResult SetDeformKey(SpriteSheetProfile profile, int clipIndex, string slotId, float time, Vector2[] offsets)
        {
            var result = new KeyEditResult();
            var clip = GetClip(profile, clipIndex);
            if (clip == null || FindSlot(profile, slotId ?? string.Empty) == null)
            {
                result.Reason = "No clip or part.";
                return result;
            }
            var track = GetOrCreateTrack(clip, slotId);
            track.Keys ??= new List<SpritePartsKeyDef>();
            bool anyDeform = track.Keys.Exists(k => k != null && (k.Channels & SpritePartsKeyChannel.Deform) != 0);
            if (!anyDeform && time > 1e-4f)
            {
                var zero = FindKeyAtTime(track, 0f);
                if (zero == null)
                {
                    zero = new SpritePartsKeyDef { Time = 0f, Channels = SpritePartsKeyChannel.None, AppearanceId = string.Empty };
                    track.Keys.Add(zero);
                }
                zero.Deform = null; // the setup outline
                zero.Channels |= SpritePartsKeyChannel.Deform;
            }
            var key = FindKeyAtTime(track, time);
            if (key == null)
            {
                key = new SpritePartsKeyDef { Time = time, Channels = SpritePartsKeyChannel.None, AppearanceId = string.Empty };
                track.Keys.Add(key);
            }
            track.Keys.Sort((x, y) => x.Time.CompareTo(y.Time));
            key.Deform = offsets != null ? (Vector2[])offsets.Clone() : null;
            key.Channels |= SpritePartsKeyChannel.Deform;
            result.Ok = true;
            result.Affected = 1;
            return result;
        }
    }
}
