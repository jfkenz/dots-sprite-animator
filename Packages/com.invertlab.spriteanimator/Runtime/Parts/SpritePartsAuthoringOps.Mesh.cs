using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    public static partial class SpritePartsAuthoringOps
    {
        /// <summary>
        /// After a mesh topology change, moves every deform key of the slot (all clips) onto the new
        /// vertex order. Removed vertices drop their offsets; new vertices start at rest.
        /// </summary>
        public static int RemapSlotDeforms(SpriteSheetProfile profile, string slotId, int[] remap, int newCount)
        {
            if (profile?.PartsClips == null || remap == null)
                return 0;
            string id = SpritePartIdUtility.Canonical(slotId);
            int changed = 0;
            foreach (var clip in profile.PartsClips)
            {
                if (clip?.Tracks == null)
                    continue;
                foreach (var track in clip.Tracks)
                {
                    if (track?.Keys == null || SpritePartIdUtility.Canonical(track.SlotId) != id)
                        continue;
                    foreach (var key in track.Keys)
                    {
                        if (key?.Deform == null)
                            continue;
                        key.Deform = SpritePartsMeshOps.RemapDeform(key.Deform, remap, newCount);
                        changed++;
                    }
                }
            }
            return changed;
        }

        /// <summary>Drops every deform key of the slot, e.g. when its mesh is removed.</summary>
        public static int ClearSlotDeforms(SpriteSheetProfile profile, string slotId)
        {
            if (profile?.PartsClips == null)
                return 0;
            string id = SpritePartIdUtility.Canonical(slotId);
            int changed = 0;
            foreach (var clip in profile.PartsClips)
            {
                if (clip?.Tracks == null)
                    continue;
                foreach (var track in clip.Tracks)
                {
                    if (track?.Keys == null || SpritePartIdUtility.Canonical(track.SlotId) != id)
                        continue;
                    foreach (var key in track.Keys)
                    {
                        if (key?.Deform == null)
                            continue;
                        key.Deform = null;
                        changed++;
                    }
                }
            }
            return changed;
        }
    }
}
