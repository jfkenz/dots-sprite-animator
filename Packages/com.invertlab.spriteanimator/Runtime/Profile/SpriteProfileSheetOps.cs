using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Shared-sheet edits must preserve all three workspace references.</summary>
    public static class SpriteProfileSheetOps
    {
        public static bool CanDelete(SpriteSheetProfile profile, int index, out string reason)
        {
            reason = null;
            if (profile?.Sheets == null || index < 0 || index >= profile.Sheets.Count || profile.Sheets.Count < 2)
                reason = "Keep at least one sheet.";
            else
            {
                if (profile.Clips != null)
                    foreach (var clip in profile.Clips)
                        if (clip != null && clip.SheetIndex == index)
                            reason = "Frame clips use this sheet. Reassign or remove those clips first.";
                if (profile.PartsAppearances != null)
                    foreach (var appearance in profile.PartsAppearances)
                        if (appearance != null && appearance.SheetIndex == index)
                            reason = "Parts appearances use this sheet. Reassign or remove those appearances first.";
                if (profile.SocketMotions != null)
                    foreach (var motion in profile.SocketMotions)
                        if (motion != null && motion.ReferenceSheetIndex == index)
                            reason = "Socket motion uses this sheet. Reassign its reference sheet first.";
            }
            return reason == null;
        }

        public static bool TryDelete(SpriteSheetProfile profile, int index, out string reason)
        {
            if (!CanDelete(profile, index, out reason)) return false;
            profile.Sheets.RemoveAt(index);
            if (profile.StaticSheetIndex > index) profile.StaticSheetIndex--;
            if (profile.Sheets.Count > 0)
                profile.StaticSheetIndex = Mathf.Clamp(profile.StaticSheetIndex, 0, profile.Sheets.Count - 1);
            if (profile.Clips != null)
                foreach (var clip in profile.Clips)
                    if (clip != null && clip.SheetIndex > index) clip.SheetIndex--;
            if (profile.PartsAppearances != null)
                foreach (var app in profile.PartsAppearances)
                    if (app != null && app.SheetIndex > index) app.SheetIndex--;
            if (profile.SocketMotions != null)
                foreach (var motion in profile.SocketMotions)
                    if (motion != null && motion.ReferenceSheetIndex > index) motion.ReferenceSheetIndex--;
            return true;
        }

        /// <summary>
        /// Authored Static world height. 0 on the profile means the sheet's natural
        /// cell height (pixels / PPU). This is SizeUnits, not GameObject scale.
        /// </summary>
        public static float ResolveStaticSizeUnits(SpriteSheetProfile profile)
        {
            if (profile == null)
                return 1f;
            if (profile.StaticSizeUnits > 0.001f)
                return profile.StaticSizeUnits;
            var sheet = profile.SheetAt(profile.StaticSheetIndex);
            return SpriteSheetProfile.GetWorldHeight(sheet, 1f);
        }

        public static bool HasValidStaticCell(SpriteSheetProfile profile)
        {
            if (profile == null) return false;
            var sheet = profile.SheetAt(profile.StaticSheetIndex);
            // Legacy profiles may still store their only sheet in the root fields.
            if ((profile.Sheets == null || profile.Sheets.Count == 0) && profile.StaticSheetIndex == 0)
                return profile.Sheet != null && profile.Columns > 0 && profile.Rows > 0 &&
                    profile.StaticRow >= 0 && profile.StaticRow < profile.Rows &&
                    profile.StaticColumn >= 0 && profile.StaticColumn < profile.Columns;
            return profile.Sheets != null && profile.StaticSheetIndex >= 0 && profile.StaticSheetIndex < profile.Sheets.Count &&
                sheet != null && sheet.Texture != null && sheet.Columns > 0 && sheet.Rows > 0 &&
                profile.StaticRow >= 0 && profile.StaticRow < sheet.Rows &&
                profile.StaticColumn >= 0 && profile.StaticColumn < sheet.Columns &&
                (sheet.CellLayoutMode == SpriteSheetCellLayoutMode.Grid ||
                 SpriteSheetProfile.HasCroppedCellData(sheet));
        }
    }
}
