using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Converts authored cell-UV colliders into local 2D pose (origin at the
    /// sprite quad bottom-center / feet, +y up) and optionally spawns Unity 2D colliders.
    /// Unit cell: X -0.5..0.5, Y 0..1 (matches Scene preview mesh).
    /// </summary>
    public static class SpriteColliderWorld
    {
        public const string RootName = "SpriteColliders";
        const string DestroyedSuffix = "_Destroyed";

        public static bool TryLocalFromUv(FrameBoxDef box,
            out Vector2 offset, out Vector2 size, out float angle)
        {
            offset = Vector2.zero;
            size = Vector2.zero;
            angle = 0f;
            if (box == null)
                return false;
            Rect uv = box.RectUV;
            // Cell UV is y-down; Scene/local is bottom-center (feet at y=0, top at y=1).
            offset = new Vector2(
                uv.x + uv.width * 0.5f - 0.5f,
                1f - (uv.y + uv.height * 0.5f));
            size = new Vector2(
                Mathf.Max(0.001f, uv.width),
                Mathf.Max(0.001f, uv.height));
            angle = -box.Angle;
            return true;
        }

        public static IEnumerable<FrameBoxDef> VisibleOn(
            IList<FrameBoxDef> boxes, string clipName, int frame)
        {
            if (boxes == null)
                yield break;
            for (int i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (box == null)
                    continue;
                if (box.IsCharacter)
                {
                    if (box.AppliesToClip(clipName))
                        yield return box;
                }
                else if (box.IsClip)
                {
                    if (string.Equals(box.ClipName, clipName))
                        yield return box;
                }
                else if (string.Equals(box.ClipName, clipName) && box.FrameIndex == frame)
                    yield return box;
            }
        }

        static readonly HashSet<string> KeepScratch = new(System.StringComparer.Ordinal);

        public static void SyncUnityColliders(Transform host, IList<FrameBoxDef> boxes,
            string clipName, int frame, bool includeFrameBoxes, bool flipX = false, bool flipY = false,
            SpriteSheetDef sheet = null, Vector2 normalizedPivot = default,
            byte lifetimeMask = 7)
        {
            if (host == null)
                return;
            Transform root = host.Find(RootName);
            if (boxes == null || boxes.Count == 0)
            {
                ClearRoot(root);
                return;
            }

            var spawn = new List<FrameBoxDef>(boxes.Count);
            for (int i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (box == null || !box.UsesUnity2D || box.Hidden)
                    continue;
                // lifetime scope filter (frame=1, character=2, clip=4)
                if (((1 << Mathf.Clamp(box.Lifetime, 0, 2)) & lifetimeMask) == 0)
                    continue;
                if (box.IsCharacter)
                {
                    if (box.AppliesToClip(clipName))
                        spawn.Add(box);
                }
                else if (box.IsClip)
                {
                    if (string.Equals(box.ClipName, clipName))
                        spawn.Add(box);
                }
                else if (includeFrameBoxes &&
                         string.Equals(box.ClipName, clipName) && box.FrameIndex == frame)
                    spawn.Add(box);
            }

            if (spawn.Count == 0)
            {
                ClearRoot(root);
                return;
            }

            if (root == null)
            {
                var go = new GameObject(RootName);
                go.transform.SetParent(host, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                root = go.transform;
            }
            // Negative scale mirrors around root origin. Offset root to 2*pivot so the
            // flip axis matches UV / socket flip around the authored profile pivot
            // (default 0.5,0.5 => cell center / mesh x=0, y=0.5).
            Vector2 pivot = normalizedPivot == default
                ? SpriteSocketWorld.ResolvePivot(null, sheet)
                : new Vector2(Mathf.Clamp01(normalizedPivot.x), Mathf.Clamp01(normalizedPivot.y));
            Vector2 axisWorld = SpriteSocketWorld.PixelsFromPivotToMeshLocal(sheet, pivot, Vector2.zero);
            Vector3 hostScale = host.localScale;
            float invSx = 1f / (Mathf.Abs(hostScale.x) > 1e-4f ? hostScale.x : 1f);
            float invSy = 1f / (Mathf.Abs(hostScale.y) > 1e-4f ? hostScale.y : 1f);
            root.localPosition = new Vector3(
                flipX ? 2f * axisWorld.x * invSx : 0f,
                flipY ? 2f * axisWorld.y * invSy : 0f,
                0f);
            root.localRotation = Quaternion.identity;
            root.localScale = new Vector3(flipX ? -1f : 1f, flipY ? -1f : 1f, 1f);

            // Reuse children by stable name — avoid destroy/recreate every sync
            // (DestroyImmediate is illegal in OnValidate/physics/animation/render;
            // deferred Destroy during play would leave duplicate children for a frame).
            KeepScratch.Clear();
            for (int i = 0; i < spawn.Count; i++)
            {
                var box = spawn[i];
                string name = ColliderChildName(box, i);
                KeepScratch.Add(name);
                try
                {
                    EnsureUnityCollider(root, box, i, name);
                }
                catch (System.Exception ex)
                {
                    // One bad box must not abort the tick / remaining colliders.
                    Debug.LogException(ex);
                }
            }

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child == null || KeepScratch.Contains(child.name))
                    continue;
                DestroyGo(child.gameObject);
            }
        }

        public static void ClearUnityColliders(Transform host)
        {
            if (host == null)
                return;
            ClearRoot(host.Find(RootName));
        }

        static void ClearRoot(Transform root)
        {
            if (root == null)
                return;
            DestroyGo(root.gameObject);
        }

        static void DestroyGo(GameObject go)
        {
            if (go == null)
                return;
            // Rename before deferred Destroy so Find(name) / child scan cannot hit a zombie.
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                MarkDestroyedName(go);
                Object.Destroy(go);
            }
            else
                Object.DestroyImmediate(go);
#else
            MarkDestroyedName(go);
            Object.Destroy(go);
#endif
        }

        static void MarkDestroyedName(GameObject go)
        {
            if (go == null)
                return;
            if (!go.name.EndsWith(DestroyedSuffix, System.StringComparison.Ordinal))
                go.name = go.name + DestroyedSuffix;
        }

        static void DestroyColliderComponent(Collider2D collider)
        {
            if (collider == null)
                return;
#if UNITY_EDITOR
            if (Application.isPlaying)
                Object.Destroy(collider);
            else
                Object.DestroyImmediate(collider);
#else
            Object.Destroy(collider);
#endif
        }

        static string ColliderChildName(FrameBoxDef box, int index)
        {
            string label = box.IsCharacter ? "Body" : box.IsClip ? "Clip" : "Frame";
            return $"Collider_{label}_{box.Id}_{box.Shape}_{index}";
        }

        /// <summary>
        /// Find a live (not Unity-destroyed / not pending-destroy) child by exact name.
        /// Skips zombies that deferred Destroy left under the root for the rest of the frame.
        /// </summary>
        static Transform FindLiveChild(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform t = root.GetChild(i);
                // Unity fake-null: pending Destroy still enumerable but == null.
                if (t == null)
                    continue;
                if (t.name != name)
                    continue;
                if (t.gameObject == null)
                    continue;
                if (t.name.EndsWith(DestroyedSuffix, System.StringComparison.Ordinal))
                    continue;
                return t;
            }
            return null;
        }

        static Collider2D GetLiveShapeCollider(GameObject go, SpriteColliderShape shape)
        {
            if (go == null)
                return null;
            Collider2D c;
            if (shape == SpriteColliderShape.Circle)
                c = go.GetComponent<CircleCollider2D>();
            else if (shape == SpriteColliderShape.Polygon)
                c = go.GetComponent<PolygonCollider2D>();
            else
                c = go.GetComponent<BoxCollider2D>();
            // Unity overloaded == treats destroyed components as null.
            return c == null ? null : c;
        }

        /// <summary>
        /// Ensure the GameObject has exactly the Collider2D type for <paramref name="shape"/>.
        /// Wrong/missing types are stripped; APIs must use the returned live instance only.
        /// </summary>
        static Collider2D EnsureShapeCollider(GameObject go, SpriteColliderShape shape)
        {
            if (go == null)
                return null;

            Collider2D wanted = GetLiveShapeCollider(go, shape);
            Collider2D[] all = go.GetComponents<Collider2D>();
            for (int i = 0; i < all.Length; i++)
            {
                Collider2D c = all[i];
                if (c == null)
                    continue;
                if (wanted != null && ReferenceEquals(c, wanted))
                    continue;
                DestroyColliderComponent(c);
            }

            if (wanted != null)
                return wanted;

            // Any Collider2D still pending Destroy on this GO blocks a reliable AddComponent
            // (especially same-type). Signal caller to recreate a clean GameObject.
            if (HasPendingCollider(go))
                return null;

            Collider2D added;
            if (shape == SpriteColliderShape.Circle)
                added = go.AddComponent<CircleCollider2D>();
            else if (shape == SpriteColliderShape.Polygon)
                added = go.AddComponent<PolygonCollider2D>();
            else
                added = go.AddComponent<BoxCollider2D>();
            // Unity fake-null / failed add → caller recreates a fresh GameObject.
            return added == null ? null : added;
        }

        static bool HasPendingCollider(GameObject go)
        {
            // GetComponents can still list components Destroy() has not removed yet.
            // C# non-null + Unity == null => pending Destroy.
            Collider2D[] all = go.GetComponents<Collider2D>();
            for (int i = 0; i < all.Length; i++)
            {
                Collider2D c = all[i];
                if (!ReferenceEquals(c, null) && c == null)
                    return true;
            }
            return false;
        }

        static void EnsureUnityCollider(Transform root, FrameBoxDef box, int index, string name)
        {
            if (!TryLocalFromUv(box, out var offset, out var size, out float angle))
                return;

            Transform child = FindLiveChild(root, name);
            GameObject go;
            if (child == null)
            {
                go = new GameObject(name);
                go.transform.SetParent(root, false);
            }
            else
                go = child.gameObject;

            go.transform.localPosition = new Vector3(offset.x, offset.y, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            go.transform.localScale = Vector3.one;

            Collider2D collider = EnsureShapeCollider(go, box.Shape);
            if (collider == null)
            {
                // Pending same-type Destroy blocked AddComponent — retire this GO and retry fresh.
                DestroyGo(go);
                go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(offset.x, offset.y, 0f);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                go.transform.localScale = Vector3.one;
                collider = EnsureShapeCollider(go, box.Shape);
                if (collider == null)
                    return;
            }

            if (box.Shape == SpriteColliderShape.Circle)
            {
                var circle = collider as CircleCollider2D;
                if (circle == null)
                    return;
                circle.radius = Mathf.Max(size.x, size.y) * 0.5f;
            }
            else if (box.Shape == SpriteColliderShape.Polygon)
            {
                var polygon = collider as PolygonCollider2D;
                if (polygon == null)
                    return;
                polygon.pathCount = 1;
                polygon.SetPath(0, PolygonLocalPoints(box, size));
            }
            else
            {
                var box2d = collider as BoxCollider2D;
                if (box2d == null)
                    return;
                box2d.size = size;
            }
            collider.isTrigger = box.IsTrigger;
            collider.offset = Vector2.zero;
        }

        /// <summary>
        /// Polygon vertices in sprite-quad local space, origin at
        /// <see cref="TryLocalFromUv"/> offset (RectUV center), +y up.
        /// Matches the Sprite Animator preview (PolygonUV is 0-1 inside RectUV, y-down).
        /// </summary>
        public static Vector2[] PolygonLocalPoints(FrameBoxDef box)
        {
            if (!TryLocalFromUv(box, out _, out var size, out _))
                return System.Array.Empty<Vector2>();
            return PolygonLocalPoints(box, size);
        }

        public static Vector2[] PolygonLocalPoints(FrameBoxDef box, Vector2 size)
        {
            Vector2[] uv = box.PolygonUV != null && box.PolygonUV.Length >= 3
                ? box.PolygonUV
                : FrameBoxDef.CreateRegularPolygon();
            var points = new Vector2[uv.Length];
            for (int i = 0; i < uv.Length; i++)
            {
                points[i] = new Vector2(
                    (uv[i].x - 0.5f) * size.x,
                    (0.5f - uv[i].y) * size.y);
            }
            return points;
        }
    }
}
