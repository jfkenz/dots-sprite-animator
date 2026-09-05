using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Pure-DOTS overlap queries over the baked hitbox data — no Unity 2D
    /// colliders, no Rigidbody2D, no physics package. Computes the world AABB
    /// of the boxes visible on a clip/frame so gameplay can do simple
    /// bounds-vs-bounds checks ("did my attack hit the enemy?") with zero
    /// per-game collider setup. Pairs with SpriteColliderAuthoring set to
    /// Query (pure) — can also be combined with Unity Physics 3D queries if
    /// that package is installed.
    /// </summary>
    public static class SpriteHitboxQuery
    {
        /// <summary>Lifetime filter helpers (same bits as SpriteColliderAuthoring).</summary>
        public const byte FrameBoxes = 1 << 0;
        public const byte CharacterBoxes = 1 << 1;
        public const byte ClipBoxes = 1 << 2;

        /// <summary>
        /// World-space AABB union of all visible boxes matching
        /// <paramref name="lifetimeMask"/> on the given clip/frame, relative
        /// to the authoring's transform (pivot- and flip-aware). False when
        /// the profile/sheet resolves to nothing or no box is visible.
        /// </summary>
        public static bool TryGetBounds(SpriteAnimSetAuthoring set, string clipName, int frame,
            byte lifetimeMask, bool flipX, out Rect bounds)
        {
            bounds = default;
            var data = set != null ? set.Profile != null ? set.Profile.Data : null : null;
            if (data == null || data.Hitboxes == null || data.Hitboxes.Count == 0)
                return false;

            var sheet = SpriteSocketWorld.DisplaySheet(data, clipName);
            if (sheet == null)
                return false;
            if (!SpriteSheetProfile.TryGetCellPixels(sheet, out float cellW, out float cellH))
            {
                cellW = 100f;
                cellH = 100f;
            }
            float ppu = SpriteSheetProfile.GetPixelsPerUnit(sheet);
            var cell = new Vector2(cellW / ppu, cellH / ppu);
            if (cell.x <= 0f || cell.y <= 0f)
                return false;

            var pivot = SpriteSocketWorld.ResolvePivot(data, sheet);
            var origin = set.transform.position;
            bool any = false;

            foreach (var box in SpriteColliderWorld.VisibleOn(data.Hitboxes, clipName, frame))
            {
                if (box == null || box.Hidden)
                    continue;
                if (((1 << Mathf.Clamp(box.Lifetime, 0, 2)) & lifetimeMask) == 0)
                    continue;
                if (!SpriteColliderWorld.TryLocalFromUv(box, out var offset,
                        out var size, out _))
                    continue;

                // cell center relative to the entity origin (pivot places the cell)
                var cellCenter = new Vector2(
                    (0.5f - pivot.x) * cell.x,
                    (0.5f - pivot.y) * cell.y);
                // box center relative to the cell center (flip mirrors x)
                var boxOffset = new Vector2(
                    (flipX ? -offset.x : offset.x) * cell.x,
                    offset.y * cell.y);
                var half = new Vector2(size.x * cell.x, size.y * cell.y) * 0.5f;

                var min = new Vector2(
                    origin.x + cellCenter.x + boxOffset.x - half.x,
                    origin.y + cellCenter.y + boxOffset.y - half.y);
                var max = new Vector2(
                    origin.x + cellCenter.x + boxOffset.x + half.x,
                    origin.y + cellCenter.y + boxOffset.y + half.y);

                if (!any)
                {
                    bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                    any = true;
                }
                else
                {
                    bounds = Rect.MinMaxRect(
                        Mathf.Min(bounds.xMin, min.x), Mathf.Min(bounds.yMin, min.y),
                        Mathf.Max(bounds.xMax, max.x), Mathf.Max(bounds.yMax, max.y));
                }
            }
            return any;
        }

        /// <summary>True when two world AABBs overlap.</summary>
        public static bool Overlaps(Rect a, Rect b)
            => a.Overlaps(b);
    }
}
