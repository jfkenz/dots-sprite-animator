using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>Anything the Slice popup can drive (tool window or cell editor).</summary>
    internal interface ISpriteSheetSliceHost
    {
        Texture2D SliceTargetTexture { get; }
        bool TryGetSliceCellMetrics(SpriteSheetSliceRequest request,
            out int texW, out int texH, out int cellW, out int cellH);
        void RunSheetSlice(SpriteSheetSliceRequest request);
    }

    /// <summary>
    /// Shared slice engine: grid by cell size or by cell count, with
    /// per-cell opaque-bound tightening, for the Unity-style Slice dialog.
    /// </summary>
    internal static class SpriteSheetSlicing
    {
        /// <summary>Readable pixel copy; <paramref name="owned"/> must be
        /// destroyed by the caller when non-null.</summary>
        public static Color32[] GetPixels32(Texture2D src, out Texture2D owned)
        {
            if (src.isReadable)
            {
                owned = null;
                return src.GetPixels32();
            }

            var rt = RenderTexture.GetTemporary(src.width, src.height, 0,
                RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            copy.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            owned = copy;
            return copy.GetPixels32();
        }

        public static (RectInt[] rects, int cols, int rows, int empty) SliceGrid(
            Color32[] pixels, int texW, int texH, SpriteSheetSliceRequest request)
        {
            int cols, rows, cellW, cellH;
            if (request.Type == SpriteSheetSliceType.GridByCellSize)
            {
                cellW = Mathf.Max(1, request.CellSize.x);
                cellH = Mathf.Max(1, request.CellSize.y);
                cols = Mathf.Max(1, (texW - request.Offset.x + request.Padding.x) /
                                    (cellW + request.Padding.x));
                rows = Mathf.Max(1, (texH - request.Offset.y + request.Padding.y) /
                                    (cellH + request.Padding.y));
            }
            else
            {
                cols = Mathf.Max(1, request.Columns);
                rows = Mathf.Max(1, request.Rows);
                cellW = Mathf.Max(1, (texW - request.Offset.x -
                                      request.Padding.x * (cols - 1)) / cols);
                cellH = Mathf.Max(1, (texH - request.Offset.y -
                                      request.Padding.y * (rows - 1)) / rows);
            }

            var rects = new RectInt[cols * rows];
            int empty = 0;
            int threshold = SpriteSheetProfile.CroppedAlphaThreshold;
            for (int r = 0; r < rows; r++) // row 0 = top (profile convention)
            {
                for (int c = 0; c < cols; c++)
                {
                    int x0 = request.Offset.x + c * (cellW + request.Padding.x);
                    int yTop = request.Offset.y + r * (cellH + request.Padding.y);
                    int y0 = texH - yTop - cellH; // pixel space: bottom-left origin
                    var cell = ClampToTexture(new RectInt(x0, y0, cellW, cellH), texW, texH);
                    rects[r * cols + c] = SliceTightRect(
                        pixels, texW, texH, cell, threshold, request.KeepEmptyRects,
                        ref empty);
                }
            }
            return (rects, cols, rows, empty);
        }

        public static RectInt ClampToTexture(RectInt rect, int texW, int texH)
        {
            int x = Mathf.Clamp(rect.x, 0, texW - 1);
            int y = Mathf.Clamp(rect.y, 0, texH - 1);
            int xMax = Mathf.Clamp(rect.xMax, 1, texW);
            int yMax = Mathf.Clamp(rect.yMax, 1, texH);
            return new RectInt(x, y, Mathf.Max(1, xMax - x), Mathf.Max(1, yMax - y));
        }

        public static RectInt SliceTightRect(Color32[] pixels, int texW, int texH,
            RectInt cell, int threshold, bool keepEmpty, ref int emptyCount)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = cell.y; y < cell.yMax; y++)
            {
                int rowBase = y * texW;
                for (int x = cell.x; x < cell.xMax; x++)
                {
                    if (pixels[rowBase + x].a <= threshold)
                        continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < 0)
            {
                emptyCount++;
                return keepEmpty
                    ? cell
                    : new RectInt(cell.x, cell.y, 1, 1);
            }
            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
    }
}
