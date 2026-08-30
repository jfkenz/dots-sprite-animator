using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Converts authored cell-UV colliders into local 2D pose (origin at the
    /// sprite quad center, +y up) and optionally spawns Unity 2D colliders.
    /// </summary>
    public static class SpriteColliderWorld
    {
        public const string RootName = "SpriteColliders";

        public static bool TryLocalFromUv(FrameBoxDef box,
            out Vector2 offset, out Vector2 size, out float angle)
        {
            offset = Vector2.zero;
            size = Vector2.zero;
            angle = 0f;
            if (box == null)
                return false;
            Rect uv = box.RectUV;
            offset = new Vector2(
                uv.x + uv.width * 0.5f - 0.5f,
                0.5f - (uv.y + uv.height * 0.5f));
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
                    yield return box;
                else if (box.IsClip)
                {
                    if (string.Equals(box.ClipName, clipName))
                        yield return box;
                }
                else if (string.Equals(box.ClipName, clipName) && box.FrameIndex == frame)
                    yield return box;
            }
        }

        public static void SyncUnityColliders(Transform host, IList<FrameBoxDef> boxes,
            string clipName, int frame, bool includeFrameBoxes)
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
                if (box.IsCharacter)
                    spawn.Add(box);
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

            for (int i = root.childCount - 1; i >= 0; i--)
                DestroyGo(root.GetChild(i).gameObject);

            for (int i = 0; i < spawn.Count; i++)
                CreateUnityCollider(root, spawn[i], i);
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
#if UNITY_EDITOR
            Object.DestroyImmediate(go);
#else
            Object.Destroy(go);
#endif
        }

        static void CreateUnityCollider(Transform root, FrameBoxDef box, int index)
        {
            if (!TryLocalFromUv(box, out var offset, out var size, out float angle))
                return;
            string label = box.IsCharacter ? "Body" : box.IsClip ? "Clip" : "Frame";
            var go = new GameObject($"Collider_{label}_{box.Id}_{box.Shape}_{index}");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(offset.x, offset.y, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            go.transform.localScale = Vector3.one;
            Collider2D collider;
            if (box.Shape == SpriteColliderShape.Circle)
            {
                var circle = go.AddComponent<CircleCollider2D>();
                circle.radius = Mathf.Max(size.x, size.y) * 0.5f;
                collider = circle;
            }
            else if (box.Shape == SpriteColliderShape.Polygon)
            {
                var polygon = go.AddComponent<PolygonCollider2D>();
                polygon.pathCount = 1;
                polygon.SetPath(0, PolygonLocalPoints(box, size));
                collider = polygon;
            }
            else
            {
                var box2d = go.AddComponent<BoxCollider2D>();
                box2d.size = size;
                collider = box2d;
            }
            collider.isTrigger = box.IsTrigger;
            collider.offset = Vector2.zero;
        }

        static Vector2[] PolygonLocalPoints(FrameBoxDef box, Vector2 size)
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
