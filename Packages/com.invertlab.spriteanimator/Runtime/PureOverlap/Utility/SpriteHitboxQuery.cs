using System.Collections.Generic;
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
        /// Polygon shapes use authored PolygonUV verts (tight), not the fat RectUV.
        /// Placement matches Scene gizmos / SpriteColliderWorld (transform + flip root).
        /// </summary>
        public static bool TryGetBounds(SpriteAnimSetAuthoring set, string clipName, int frame,
            byte lifetimeMask, bool flipX, out Rect bounds, bool flipY = false)
        {
            bounds = default;
            if (!TryCollectWorldShapes(set, clipName, frame, lifetimeMask, flipX, flipY,
                    out var shapes, out _))
                return false;

            bool any = false;
            for (int s = 0; s < shapes.Count; s++)
            {
                var shape = shapes[s];
                if (!any)
                {
                    bounds = shape.Bounds;
                    any = true;
                }
                else
                {
                    bounds = Rect.MinMaxRect(
                        Mathf.Min(bounds.xMin, shape.Bounds.xMin),
                        Mathf.Min(bounds.yMin, shape.Bounds.yMin),
                        Mathf.Max(bounds.xMax, shape.Bounds.xMax),
                        Mathf.Max(bounds.yMax, shape.Bounds.yMax));
                }
            }
            return any;
        }

        /// <summary>True when two world AABBs overlap.</summary>
        public static bool Overlaps(Rect a, Rect b)
            => a.Overlaps(b);

        /// <summary>
        /// Precise hit test: polygon attack shapes vs a hurt AABB (character body).
        /// Prefer this over <see cref="Overlaps"/> for melee — AABB-vs-AABB still
        /// hits in the empty corners around a crescent slash.
        /// </summary>
        public static bool OverlapsHurtPrecise(
            SpriteAnimSetAuthoring attackSet, string clipName, int frame,
            byte lifetimeMask, bool flipX, Rect hurtBounds, bool flipY = false)
        {
            if (!TryCollectWorldShapes(attackSet, clipName, frame, lifetimeMask, flipX, flipY,
                    out var shapes, out _))
                return false;

            for (int i = 0; i < shapes.Count; i++)
            {
                var shape = shapes[i];
                if (!shape.Bounds.Overlaps(hurtBounds))
                    continue;
                if (shape.Polygon != null && shape.Polygon.Length >= 3)
                {
                    if (PolygonOverlapsRect(shape.Polygon, hurtBounds))
                        return true;
                }
                else if (shape.Bounds.Overlaps(hurtBounds))
                {
                    return true;
                }
            }
            return false;
        }

        struct WorldShape
        {
            public Rect Bounds;
            public Vector2[] Polygon; // null => axis-aligned box (Bounds)
        }

        static Matrix4x4 GizmoMatrix(SpriteAnimSetAuthoring set, SpriteSheetProfile data,
            string clipName, bool flipX, bool flipY)
        {
            var displaySheet = SpriteSocketWorld.DisplaySheet(data, clipName);
            Vector3 facingScale = new Vector3(flipX ? -1f : 1f, flipY ? -1f : 1f, 1f);
            if (!flipX && !flipY)
                return set.transform.localToWorldMatrix * Matrix4x4.Scale(facingScale);

            Vector2 flipPivot = SpriteSocketWorld.ResolvePivot(data, displaySheet);
            Vector2 axis = SpriteSocketWorld.PixelsFromPivotToMeshLocal(
                displaySheet, flipPivot, Vector2.zero);
            Vector3 hostScale = set.transform.localScale;
            float invSx = 1f / (Mathf.Abs(hostScale.x) > 1e-4f ? hostScale.x : 1f);
            float invSy = 1f / (Mathf.Abs(hostScale.y) > 1e-4f ? hostScale.y : 1f);
            Vector3 flipRoot = new Vector3(
                flipX ? 2f * axis.x * invSx : 0f,
                flipY ? 2f * axis.y * invSy : 0f,
                0f);
            return set.transform.localToWorldMatrix *
                   Matrix4x4.TRS(flipRoot, Quaternion.identity, Vector3.one) *
                   Matrix4x4.Scale(facingScale);
        }

        static bool TryCollectWorldShapes(
            SpriteAnimSetAuthoring set, string clipName, int frame,
            byte lifetimeMask, bool flipX, bool flipY,
            out List<WorldShape> shapes, out Rect unused)
        {
            unused = default;
            shapes = new List<WorldShape>(4);
            var data = set != null ? set.Profile != null ? set.Profile.Data : null : null;
            if (data == null || data.Hitboxes == null || data.Hitboxes.Count == 0)
                return false;
            if (set.transform == null)
                return false;

            var m = GizmoMatrix(set, data, clipName, flipX, flipY);

            foreach (var box in SpriteColliderWorld.VisibleOn(data.Hitboxes, clipName, frame))
            {
                if (box == null || box.Hidden)
                    continue;
                if (((1 << Mathf.Clamp(box.Lifetime, 0, 2)) & lifetimeMask) == 0)
                    continue;
                if (!SpriteColliderWorld.TryLocalFromUv(box, out var offset,
                        out var size, out float angle))
                    continue;
                if (size.x < 0.001f || size.y < 0.001f)
                    continue;

                var rot = Quaternion.Euler(0f, 0f, angle);

                if (box.Shape == SpriteColliderShape.Polygon &&
                    box.PolygonUV != null && box.PolygonUV.Length >= 3)
                {
                    var pts = SpriteColliderWorld.PolygonLocalPoints(box, size);
                    var world = new Vector2[pts.Length];
                    float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
                    float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        var lp = rot * new Vector3(pts[i].x, pts[i].y, 0f);
                        var w = m.MultiplyPoint3x4(new Vector3(offset.x + lp.x, offset.y + lp.y, 0f));
                        world[i] = new Vector2(w.x, w.y);
                        if (w.x < minX) minX = w.x;
                        if (w.y < minY) minY = w.y;
                        if (w.x > maxX) maxX = w.x;
                        if (w.y > maxY) maxY = w.y;
                    }
                    if (maxX - minX < 0.001f || maxY - minY < 0.001f)
                        continue;
                    shapes.Add(new WorldShape
                    {
                        Bounds = Rect.MinMaxRect(minX, minY, maxX, maxY),
                        Polygon = world,
                    });
                }
                else
                {
                    var half = size * 0.5f;
                    var c0 = m.MultiplyPoint3x4((Vector3)offset + rot * new Vector3(-half.x, -half.y, 0f));
                    var c1 = m.MultiplyPoint3x4((Vector3)offset + rot * new Vector3(half.x, -half.y, 0f));
                    var c2 = m.MultiplyPoint3x4((Vector3)offset + rot * new Vector3(half.x, half.y, 0f));
                    var c3 = m.MultiplyPoint3x4((Vector3)offset + rot * new Vector3(-half.x, half.y, 0f));
                    float minX = Mathf.Min(Mathf.Min(c0.x, c1.x), Mathf.Min(c2.x, c3.x));
                    float minY = Mathf.Min(Mathf.Min(c0.y, c1.y), Mathf.Min(c2.y, c3.y));
                    float maxX = Mathf.Max(Mathf.Max(c0.x, c1.x), Mathf.Max(c2.x, c3.x));
                    float maxY = Mathf.Max(Mathf.Max(c0.y, c1.y), Mathf.Max(c2.y, c3.y));
                    shapes.Add(new WorldShape
                    {
                        Bounds = Rect.MinMaxRect(minX, minY, maxX, maxY),
                        Polygon = null,
                    });
                }
            }
            return shapes.Count > 0;
        }

        /// <summary>Polygon vs axis-aligned rect (edge crosses or either contains a point).</summary>
        public static bool PolygonOverlapsRect(Vector2[] poly, Rect rect)
        {
            if (poly == null || poly.Length < 3)
                return false;

            for (int i = 0; i < poly.Length; i++)
            {
                if (rect.Contains(poly[i]))
                    return true;
            }

            if (PointInPolygon(new Vector2(rect.xMin, rect.yMin), poly) ||
                PointInPolygon(new Vector2(rect.xMax, rect.yMin), poly) ||
                PointInPolygon(new Vector2(rect.xMin, rect.yMax), poly) ||
                PointInPolygon(new Vector2(rect.xMax, rect.yMax), poly))
                return true;

            var corners = new Vector2[]
            {
                new Vector2(rect.xMin, rect.yMin),
                new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax),
                new Vector2(rect.xMin, rect.yMax),
            };
            for (int i = 0; i < poly.Length; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Length];
                for (int j = 0; j < 4; j++)
                {
                    if (SegmentsIntersect(a, b, corners[j], corners[(j + 1) % 4]))
                        return true;
                }
            }
            return false;
        }

        public static bool PointInPolygon(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var pi = poly[i];
                var pj = poly[j];
                if (((pi.y > p.y) != (pj.y > p.y)) &&
                    (p.x < (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y + 1e-12f) + pi.x))
                    inside = !inside;
            }
            return inside;
        }

        static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float o1 = Orient(a, b, c);
            float o2 = Orient(a, b, d);
            float o3 = Orient(c, d, a);
            float o4 = Orient(c, d, b);
            if (o1 * o2 < 0f && o3 * o4 < 0f)
                return true;
            return false;
        }

        static float Orient(Vector2 a, Vector2 b, Vector2 c)
            => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }
}
