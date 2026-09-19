using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    public static class SpriteStaticAuthoringOps
    {
        public static bool TryFindSheetWithTexture(SpriteSheetProfile profile, out SpriteSheetDef sheet)
        {
            sheet = null;
            if (profile == null)
                return false;
            profile.EnsureSheets();
            if (profile.Sheets != null)
            {
                for (int i = 0; i < profile.Sheets.Count; i++)
                {
                    var s = profile.Sheets[i];
                    if (s?.Texture != null)
                    {
                        sheet = s;
                        return true;
                    }
                }
            }
            if (profile.Sheet != null)
            {
                sheet = profile.SheetAt(0);
                return sheet != null;
            }
            return false;
        }

        public static void SyncStaticPivotToCell(SpriteSheetProfile profile)
        {
            if (profile == null)
                return;
            profile.EnsureStaticSprite();
            profile.EnsureSheets();
            var def = profile.StaticSprite;
            var sheet = profile.SheetAt(Mathf.Max(0, def.SheetIndex));
            if (sheet == null)
                return;
            int cols = Mathf.Max(1, sheet.Columns);
            int rows = Mathf.Max(1, sheet.Rows);
            int row = Mathf.Clamp(def.Row, 0, rows - 1);
            int col = Mathf.Clamp(def.Column, 0, cols - 1);
            int slot = row * cols + col;
            SpriteSheetProfile.SetCellPivot(sheet, slot, def.Pivot);
        }

        public static void ReadStaticPivotFromCell(SpriteSheetProfile profile)
        {
            if (profile == null)
                return;
            profile.EnsureStaticSprite();
            profile.EnsureSheets();
            var def = profile.StaticSprite;
            var sheet = profile.SheetAt(Mathf.Max(0, def.SheetIndex));
            if (sheet == null)
                return;
            int cols = Mathf.Max(1, sheet.Columns);
            int rows = Mathf.Max(1, sheet.Rows);
            int row = Mathf.Clamp(def.Row, 0, rows - 1);
            int col = Mathf.Clamp(def.Column, 0, cols - 1);
            int slot = row * cols + col;
            if (SpriteSheetProfile.TryGetCellPivot(sheet, slot, out var pivot))
                def.Pivot = pivot;
            else
                def.Pivot = SpriteSocketWorld.ResolvePivot(profile, sheet);
        }
    }
}
