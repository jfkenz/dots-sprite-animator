using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Mirrors spritesheet cells in place so clip frame indices stay valid.
    /// Pixel origin is Texture2D (bottom-left); tool row 0 is the top row of cells.
    /// </summary>
    public static class SpriteSheetPixelFlip
    {
        public static void FlipCells(
            Color32[] pixels, int width, int height, int columns, int rows,
            bool flipX, bool flipY)
        {
            if (pixels == null || (!flipX && !flipY))
                return;
            if (width <= 0 || height <= 0 || pixels.Length < width * height)
                return;

            columns = Mathf.Max(1, columns);
            rows = Mathf.Max(1, rows);
            int cellW = width / columns;
            int cellH = height / rows;
            if (cellW <= 0 || cellH <= 0)
                return;

            for (int row = 0; row < rows; row++)
            {
                int pixelY0 = (rows - 1 - row) * cellH;
                for (int column = 0; column < columns; column++)
                {
                    int pixelX0 = column * cellW;
                    if (flipX)
                        FlipCellHorizontal(pixels, width, pixelX0, pixelY0, cellW, cellH);
                    if (flipY)
                        FlipCellVertical(pixels, width, pixelX0, pixelY0, cellW, cellH);
                }
            }
        }

        static void FlipCellHorizontal(
            Color32[] pixels, int stride, int x0, int y0, int cellW, int cellH)
        {
            int half = cellW / 2;
            for (int y = 0; y < cellH; y++)
            {
                int row = (y0 + y) * stride + x0;
                for (int x = 0; x < half; x++)
                {
                    int a = row + x;
                    int b = row + (cellW - 1 - x);
                    (pixels[a], pixels[b]) = (pixels[b], pixels[a]);
                }
            }
        }

        static void FlipCellVertical(
            Color32[] pixels, int stride, int x0, int y0, int cellW, int cellH)
        {
            int half = cellH / 2;
            for (int y = 0; y < half; y++)
            {
                int rowA = (y0 + y) * stride + x0;
                int rowB = (y0 + (cellH - 1 - y)) * stride + x0;
                for (int x = 0; x < cellW; x++)
                    (pixels[rowA + x], pixels[rowB + x]) = (pixels[rowB + x], pixels[rowA + x]);
            }
        }

        public static Vector2 FlipNormalizedPivot(Vector2 pivot, bool flipX, bool flipY)
        {
            if (flipX)
                pivot.x = 1f - pivot.x;
            if (flipY)
                pivot.y = 1f - pivot.y;
            return pivot;
        }

        public static Vector2 FlipLocalPixels(Vector2 local, bool flipX, bool flipY)
        {
            if (flipX)
                local.x = -local.x;
            if (flipY)
                local.y = -local.y;
            return local;
        }

        public static Vector2 FlipCellUv(Vector2 uv, bool flipX, bool flipY)
        {
            if (flipX)
                uv.x = 1f - uv.x;
            if (flipY)
                uv.y = 1f - uv.y;
            return uv;
        }

        /// <summary>
        /// Frame collider UV origin is top-left of the cell.
        /// Horizontal flip keeps width; the left edge moves to 1 - (x + w).
        /// </summary>
        public static Rect FlipCellRectUv(Rect rect, bool flipX, bool flipY)
        {
            if (flipX)
                rect.x = 1f - (rect.x + rect.width);
            if (flipY)
                rect.y = 1f - (rect.y + rect.height);
            return rect;
        }

        public static float FlipAngle(float angle, bool flipX, bool flipY)
        {
            if (flipX)
                angle = -angle;
            if (flipY)
                angle = -angle;
            return angle;
        }

        public static SpriteFacingDirection FlipFacing(
            SpriteFacingDirection facing, bool flipX, bool flipY)
        {
            if (facing == SpriteFacingDirection.None)
                return facing;
            int dir = (int)facing;
            if (dir < 0 || dir > 7)
                return facing;
            if (flipX)
                dir = (4 - dir + 8) % 8;
            if (flipY)
                dir = (8 - dir) % 8;
            return (SpriteFacingDirection)dir;
        }

        public static void RemapProfileAfterCellFlip(
            SpriteSheetProfile profile, int sheetIndex, bool flipX, bool flipY)
        {
            if (profile == null || (!flipX && !flipY))
                return;

            var sheet = profile.SheetAt(sheetIndex);
            if (sheet != null)
                sheet.Pivot = FlipNormalizedPivot(sheet.Pivot, flipX, flipY);

            if (profile.Clips != null)
            {
                for (int c = 0; c < profile.Clips.Count; c++)
                {
                    var clip = profile.Clips[c];
                    if (clip == null || clip.SheetIndex != sheetIndex)
                        continue;
                    RemapClip(clip, flipX, flipY);
                }
            }

            if (profile.Hitboxes != null)
            {
                for (int i = 0; i < profile.Hitboxes.Count; i++)
                {
                    var box = profile.Hitboxes[i];
                    if (box == null || !BoxBelongsToSheet(profile, box, sheetIndex))
                        continue;
                    RemapBox(box, flipX, flipY);
                }
            }

            if (profile.SocketMotions != null)
            {
                for (int t = 0; t < profile.SocketMotions.Count; t++)
                {
                    var track = profile.SocketMotions[t];
                    if (track == null || track.ReferenceSheetIndex != sheetIndex)
                        continue;
                    RemapTrack(track, flipX, flipY);
                }
            }

            if (profile.TimelineHitShape == SpriteTimelineHitShape.Polygon &&
                profile.TimelineHitPolygon != null)
            {
                for (int i = 0; i < profile.TimelineHitPolygon.Length; i++)
                    profile.TimelineHitPolygon[i] =
                        FlipCellUv(profile.TimelineHitPolygon[i], flipX, flipY);
            }
        }

        static bool BoxBelongsToSheet(SpriteSheetProfile profile, FrameBoxDef box, int sheetIndex)
        {
            if (box.IsCharacter)
                return true;
            var clip = profile.FindClip(box.ClipName);
            if (clip == null)
                return true;
            return clip.SheetIndex == sheetIndex;
        }

        static void RemapClip(SpriteClipDef clip, bool flipX, bool flipY)
        {
            clip.EnsureFrameData();
            clip.Facing = FlipFacing(clip.Facing, flipX, flipY);
            if (clip.OnionOffsets != null)
            {
                for (int i = 0; i < clip.OnionOffsets.Length; i++)
                    clip.OnionOffsets[i] = FlipLocalPixels(clip.OnionOffsets[i], flipX, flipY);
            }
            if (clip.FrameRotations != null)
            {
                for (int i = 0; i < clip.FrameRotations.Length; i++)
                    clip.FrameRotations[i] = FlipAngle(clip.FrameRotations[i], flipX, flipY);
            }
            if (clip.Sockets == null)
                return;
            for (int i = 0; i < clip.Sockets.Count; i++)
            {
                var socket = clip.Sockets[i];
                if (socket == null)
                    continue;
                socket.LocalPosition = FlipLocalPixels(socket.LocalPosition, flipX, flipY);
                socket.LocalAngle = FlipAngle(socket.LocalAngle, flipX, flipY);
            }
        }

        static void RemapBox(FrameBoxDef box, bool flipX, bool flipY)
        {
            box.RectUV = FlipCellRectUv(box.RectUV, flipX, flipY);
            box.Angle = FlipAngle(box.Angle, flipX, flipY);
            if (box.PolygonUV == null)
                return;
            for (int i = 0; i < box.PolygonUV.Length; i++)
                box.PolygonUV[i] = FlipCellUv(box.PolygonUV[i], flipX, flipY);
        }

        static void RemapTrack(SpriteSocketMotionTrack track, bool flipX, bool flipY)
        {
            if (track.Keys == null)
                return;
            bool invertArc = flipX ^ flipY;
            for (int i = 0; i < track.Keys.Count; i++)
            {
                var key = track.Keys[i];
                if (key == null)
                    continue;
                key.LocalPosition = FlipLocalPixels(key.LocalPosition, flipX, flipY);
                key.LocalAngle = FlipAngle(key.LocalAngle, flipX, flipY);
                key.InTangent = FlipLocalPixels(key.InTangent, flipX, flipY);
                key.OutTangent = FlipLocalPixels(key.OutTangent, flipX, flipY);
                key.FacingAngleOffset = FlipAngle(key.FacingAngleOffset, flipX, flipY);
                if (invertArc)
                    key.ArcClockwise = !key.ArcClockwise;
            }
        }
    }
}
