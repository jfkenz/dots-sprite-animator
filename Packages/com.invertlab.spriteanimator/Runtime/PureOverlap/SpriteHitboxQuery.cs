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
            byte lifetimeMask, bool flipX, out Rect bounds, bool flipY = false)
        {
            bounds = default;
            var data = set != null ? set.Profile != null ? set.Profile.Data : null : null;
            if (data == null || data.Hitboxes == null || data.Hitboxes.Count == 0)
                return false;

            var sheet = SpriteSocketWorld.DisplaySheet(data, clipName);
            if (sheet == null)
                return false;

            // EXACT parity with SpriteColliderWorld's Unity 2D spawn:
            // normalized cell space IS world space (cell = 1x1 at any ppu),
            // offset is the raw child localPosition, and flips mirror the
            // whole subtree around the pivot axis (root shift 2*axis + scale -1).
            var pivot = SpriteSocketWorld.ResolvePivot(data, sheet);
            var axis = SpriteSocketWorld.PixelsFromPivotToMeshLocal(sheet, pivot, Vector2.zero);
            var origin = set.transform.position;
            float rootX = flipX ? 2f * axis.x : 0f;
            float rootY = flipY ? 2f * axis.y : 0f;
            // the sprite render anchors the cell at the pivot via baked frame
            // offsets — the query must live in the same shifted space
            var pivotShift = new Vector2(0.5f - pivot.x, 0.5f - pivot.y);
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
                // degenerate boxes (zero-area placeholder/stale rects) can
                // never hit — skipping them keeps gotBounds honest so the
                // no-box diagnostic fires instead of a phantom 0-size bounds
                if (size.x < 0.001f || size.y < 0.001f)
                    continue;

                // child local position under the (possibly mirrored) root,
                // shifted by the pivot offset the sprite render applies
                var center = new Vector2(
                    origin.x + pivotShift.x + rootX + (flipX ? -offset.x : offset.x),
                    origin.y + pivotShift.y + rootY + (flipY ? -offset.y : offset.y));
                var half = new Vector2(size.x, size.y) * 0.5f;

                var min = new Vector2(center.x - half.x, center.y - half.y);
                var max = new Vector2(center.x + half.x, center.y + half.y);

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
