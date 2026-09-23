using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    public static partial class SpritePartsAuthoringOps
    {
        /// <summary>
        /// After a mesh topology change, moves every deform key of the slot (all clips) onto the new
        /// vertex order. Removed vertices drop their offsets; new vertices start at rest, or halfway
        /// between their <paramref name="parents"/> (from Subdivide) when given.
        /// </summary>
        public static int RemapSlotDeforms(SpriteSheetProfile profile, string slotId, int[] remap, int newCount,
            Vector2Int[] parents = null)
        {
            RemapSlotPins(profile, slotId, remap, newCount);
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
                        key.Deform = parents != null && parents.Length == newCount
                            ? SpritePartsMeshOps.SubdivideDeform(key.Deform, remap, parents)
                            : SpritePartsMeshOps.RemapDeform(key.Deform, remap, newCount);
                        changed++;
                    }
                }
            }
            return changed;
        }

        /// <summary>Pins follow their vertices across a topology change; pins on removed vertices go.</summary>
        public static void RemapSlotPins(SpriteSheetProfile profile, string slotId, int[] remap, int newCount)
        {
            var mesh = FindSlot(profile, slotId)?.Mesh;
            if (mesh?.Pins == null || mesh.Pins.Length == 0 || remap == null)
                return;
            var next = new System.Collections.Generic.List<int>(mesh.Pins.Length);
            foreach (int v in mesh.Pins)
            {
                int to = (uint)v < (uint)remap.Length ? remap[v] : -1;
                if ((uint)to < (uint)newCount && !next.Contains(to))
                    next.Add(to);
            }
            next.Sort();
            mesh.Pins = next.ToArray();
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
