using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Appearance → visual geometry for Parts. Joint TRS is independent of art size/pivot.
    /// Shared by preview, bake, and runtime swaps.
    /// </summary>
    public static class SpritePartsGeometry
    {
        public struct Resolved
        {
            public float2 LogicalWorldSize;
            public float2 Pivot;
            public float2 FrameOffset;
            public float2 FrameScale;
            public float Aspect;
            public int SheetIndex;
            public int CellIndex;
        }

        /// <summary>
        /// Resolve Grid/Cropped art. Rotated packing is rejected with a clear error.
        /// When appearance.LogicalWorldSize is zero, size comes from active cell pixels / PPU.
        /// </summary>
        public static bool TryResolve(
            SpriteSheetProfile profile,
            SpritePartAppearanceDef appearance,
            bool rotatedPacking,
            out Resolved resolved,
            out string error)
        {
            resolved = default;
            error = null;
            if (appearance == null)
            {
                error = "Appearance is null.";
                return false;
            }
            if (rotatedPacking)
            {
                error = "Rotated packing is not supported for Parts appearances.";
                return false;
            }
            if (profile == null)
            {
                error = "Profile is null.";
                return false;
            }
            profile.EnsureSheets();
            if (profile.Sheets == null || appearance.SheetIndex < 0 ||
                appearance.SheetIndex >= profile.Sheets.Count)
            {
                error = $"Appearance sheet index {appearance.SheetIndex} is out of range.";
                return false;
            }

            var sheet = profile.Sheets[appearance.SheetIndex];
            if (sheet == null)
            {
                error = "Appearance sheet is null.";
                return false;
            }
            if (sheet.CellLayoutMode != SpriteSheetCellLayoutMode.Grid &&
                sheet.CellLayoutMode != SpriteSheetCellLayoutMode.Cropped)
            {
                error = $"Unsupported cell layout '{sheet.CellLayoutMode}' for Parts.";
                return false;
            }

            float2 logical = new float2(appearance.LogicalWorldSize.x, appearance.LogicalWorldSize.y);
            if (logical.x <= 0f || logical.y <= 0f)
            {
                if (!SpriteSheetProfile.TryGetActiveCellPixels(sheet, appearance.CellIndex,
                        out float cellW, out float cellH) || cellW <= 0f || cellH <= 0f)
                {
                    error = "Cannot resolve Parts appearance size: sheet has no usable cell pixels.";
                    return false;
                }
                float ppu = SpriteSheetProfile.GetPixelsPerUnit(sheet);
                logical = new float2(cellW / ppu, cellH / ppu);
            }

            if (!math.isfinite(logical.x) || !math.isfinite(logical.y) ||
                logical.x <= 0f || logical.y <= 0f)
            {
                error = "Resolved LogicalWorldSize must be finite and > 0.";
                return false;
            }

            float2 pivot = ResolvePivot(sheet, appearance);
            if (!TryResolveExplicit(logical, pivot, appearance.SheetIndex, appearance.CellIndex,
                    out resolved, out error))
                return false;
            // Profile sheet entities use the uniform grid's aspect in the shader.
            // Crops change UVs only; a part's logical aspect can be different.
            // Compensate for the actual sheet aspect, not the part's own aspect.
            float sheetAspect = SpriteSheetProfile.GetCellAspect(sheet.Texture,
                UnityEngine.Mathf.Max(1, sheet.Columns), UnityEngine.Mathf.Max(1, sheet.Rows));
            if (!math.isfinite(sheetAspect) || sheetAspect <= 0.01f) sheetAspect = 1f;
            resolved.FrameScale = new float2(logical.x / sheetAspect, logical.y);
            return true;
        }

        /// <summary>
        /// Low-level geometry from explicit size/pivot (SetSlotSheet / runtime swaps).
        /// Rejects missing or non-finite metadata. Never invents zero size.
        /// </summary>
        public static bool TryResolveExplicit(
            float2 logicalWorldSize,
            float2 pivot,
            int sheetIndex,
            int cellIndex,
            out Resolved resolved,
            out string error)
        {
            resolved = default;
            error = null;
            if (!math.isfinite(logicalWorldSize.x) || !math.isfinite(logicalWorldSize.y) ||
                logicalWorldSize.x <= 0f || logicalWorldSize.y <= 0f)
            {
                error = "LogicalWorldSize must be finite and > 0.";
                return false;
            }
            if (!math.isfinite(pivot.x) || !math.isfinite(pivot.y))
            {
                error = "Pivot must be finite.";
                return false;
            }
            if (cellIndex < 0)
            {
                error = "CellIndex must be >= 0.";
                return false;
            }

            pivot = math.saturate(pivot);
            float aspect = logicalWorldSize.y > 1e-8f ? logicalWorldSize.x / logicalWorldSize.y : 1f;
            // Existing shader multiplies X by aspect A; store Scale so final size = logical.
            float2 frameScale = new float2(logicalWorldSize.x / math.max(1e-8f, aspect), logicalWorldSize.y);
            float2 frameOffset = (new float2(0.5f, 0.5f) - pivot) * logicalWorldSize;

            resolved = new Resolved
            {
                LogicalWorldSize = logicalWorldSize,
                Pivot = pivot,
                FrameOffset = frameOffset,
                FrameScale = frameScale,
                Aspect = aspect,
                SheetIndex = sheetIndex,
                CellIndex = cellIndex,
            };
            return true;
        }

        public static float2 ResolvePivot(SpriteSheetDef sheet, SpritePartAppearanceDef appearance)
        {
            if (appearance.PivotSource == SpritePartPivotSource.Override)
                return math.saturate(new float2(appearance.PivotOverride.x, appearance.PivotOverride.y));

            if (appearance.PivotSource == SpritePartPivotSource.Cell &&
                SpriteSheetProfile.TryGetCellPivot(sheet, appearance.CellIndex, out var cellPivot))
                return math.saturate(new float2(cellPivot.x, cellPivot.y));

            UnityEngine.Vector2 sheetPivot = sheet != null && sheet.Pivot != default
                ? sheet.Pivot
                : SpriteSheetProfile.DefaultPivot;
            return math.saturate(new float2(sheetPivot.x, sheetPivot.y));
        }

        /// <summary>
        /// visualPoint = (q + 0.5 - pivot) * logicalWorldSize for normalized quad q in [-0.5,0.5].
        /// </summary>
        public static float2 VisualPoint(float2 q, float2 pivot, float2 logicalWorldSize)
            => (q + new float2(0.5f, 0.5f) - pivot) * logicalWorldSize;
    }
}
