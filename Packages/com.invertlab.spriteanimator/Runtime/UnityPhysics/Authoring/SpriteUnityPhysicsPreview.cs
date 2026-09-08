using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Marks that Unity Physics colliders were baked in the editor onto this
    /// object (children under <see cref="SpriteUnityPhysicsPreview.RootName"/>).
    /// Play mode still creates the DOTS PhysicsWorld body for OverlapAabb;
    /// this flag only means the author already previewed/baked shapes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpriteUnityPhysicsBaked : MonoBehaviour
    {
        public int ColliderCount;
        public bool HasBox;
        public bool HasSphere;
        public bool HasConvex;
    }

    /// <summary>
    /// Workflow A (edit): Bake button adds UnityEngine BoxCollider /
    /// SphereCollider / MeshCollider(convex) under <see cref="RootName"/>.
    /// Workflow B (play): if the user never baked, runtime recreates the DOTS
    /// hurtbox — these preview colliders are for authoring visibility.
    /// </summary>
    public static class SpriteUnityPhysicsPreview
    {
        public const string RootName = "UnityPhysicsColliders";

        static readonly List<FrameBoxDef> BodyScratch = new();
        static readonly HashSet<string> KeepScratch = new(System.StringComparer.Ordinal);

        /// <summary>
        /// Add / refresh colliders from profile body boxes. Always creates at
        /// least one full-cell Box when the profile has no character boxes.
        /// Returns how many collider children were written.
        /// </summary>
        public static int Bake(Transform host, SpriteSheetProfile data, float sizeUnits)
        {
            if (host == null)
                return 0;

            SpriteUnityPhysicsShape.CollectCharacterBodyBoxes(data, BodyScratch);
            if (BodyScratch.Count == 0)
            {
                BodyScratch.Add(new FrameBoxDef
                {
                    ClipName = "body",
                    RectUV = new Rect(0f, 0f, 1f, 1f),
                    Shape = SpriteColliderShape.Square,
                    Lifetime = 1,
                });
            }

            Transform root = host.Find(RootName);
            if (root == null)
            {
                var go = new GameObject(RootName);
                go.transform.SetParent(host, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                root = go.transform;
}

            KeepScratch.Clear();
            int boxes = 0, spheres = 0, convex = 0;
            for (int i = 0; i < BodyScratch.Count; i++)
            {
                var box = BodyScratch[i];
                string name = ChildName(box, i);
                KeepScratch.Add(name);
                EnsureChild(root, box, name, sizeUnits);
                switch (box.Shape)
                {
                    case SpriteColliderShape.Circle: spheres++; break;
                    case SpriteColliderShape.Polygon: convex++; break;
                    default: boxes++; break;
                }
            }

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child == null || KeepScratch.Contains(child.name))
                    continue;
                DestroyGo(child.gameObject);
            }

            var marker = host.GetComponent<SpriteUnityPhysicsBaked>();
            if (marker == null)
                marker = host.gameObject.AddComponent<SpriteUnityPhysicsBaked>();
            marker.ColliderCount = BodyScratch.Count;
            marker.HasBox = boxes > 0;
            marker.HasSphere = spheres > 0;
            marker.HasConvex = convex > 0;

            return BodyScratch.Count;
        }

        public static void Clear(Transform host)
        {
            if (host == null)
                return;
            ClearRoot(host.Find(RootName));
            var marker = host.GetComponent<SpriteUnityPhysicsBaked>();
            if (marker != null)
                DestroyComponent(marker);
        }

        public static bool HasBaked(Transform host)
        {
            if (host == null)
                return false;
            var root = host.Find(RootName);
            return root != null && root.childCount > 0;
        }

        // ---- legacy name kept for callers ----
        public static void Sync(
            Transform host, SpriteSheetProfile data, float sizeUnits,
            Vector2 pivot, byte lifetimeMask = SpriteColliderAuthoring.LifetimeCharacter)
        {
            Bake(host, data, sizeUnits);
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
            if (Application.isPlaying)
                Object.Destroy(go);
            else
                Object.DestroyImmediate(go);
        }

        static void DestroyComponent(Component c)
        {
            if (c == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(c);
            else
                Object.DestroyImmediate(c);
        }

        static string ChildName(FrameBoxDef box, int index)
        {
            string shape = SpriteUnityPhysicsShape.ShapeLabel(box.Shape);
            return shape + "_" + index;
        }

        static void EnsureChild(Transform root, FrameBoxDef box, string name, float sizeUnits)
        {
            Transform child = null;
            for (int i = 0; i < root.childCount; i++)
            {
                if (root.GetChild(i).name == name)
                {
                    child = root.GetChild(i);
                    break;
                }
            }

            GameObject go;
            if (child == null)
            {
                go = new GameObject(name);
                go.transform.SetParent(root, false);
            }
            else
                go = child.gameObject;

            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            SpriteColliderWorld.TryLocalFromUv(box, out var offset, out var sizeN, out _);
            float su = Mathf.Max(0.001f, sizeUnits);
            var center = new Vector3(offset.x * su, offset.y * su, 0f);
            var size = new Vector2(
                Mathf.Max(0.05f, sizeN.x * su),
                Mathf.Max(0.05f, sizeN.y * su));

            // Strip every collider type first. Do NOT use ?? after DestroyImmediate —
            // Unity "fake null" is not C# null, so ?? skips AddComponent and
            // setting isTrigger throws MissingComponentException.
            StripColliders(go);

            switch (box.Shape)
            {
                case SpriteColliderShape.Circle:
                {
                    var sphereCol = go.AddComponent<SphereCollider>();
                    sphereCol.isTrigger = true;
                    sphereCol.center = center;
                    sphereCol.radius = Mathf.Max(size.x, size.y) * 0.5f;
                    break;
                }
                case SpriteColliderShape.Polygon:
                {
                    var meshCol = go.AddComponent<MeshCollider>();
                    meshCol.convex = true;
                    meshCol.isTrigger = true;
                    meshCol.sharedMesh = BuildConvexPreviewMesh(box, center, size);
                    break;
                }
                default:
                {
                    var boxCol = go.AddComponent<BoxCollider>();
                    boxCol.isTrigger = true;
                    boxCol.center = center;
                    boxCol.size = new Vector3(
                        size.x, size.y, Mathf.Max(0.05f, SpriteUnityPhysicsShape.DefaultThickness));
                    break;
                }
            }
        }

        static void StripColliders(GameObject go)
        {
            if (go == null)
                return;
            // DestroyImmediate all collider types (may be more than one after shape swaps)
            var colliders = go.GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
                DestroyComponent(colliders[i]);
        }

        static Mesh BuildConvexPreviewMesh(FrameBoxDef box, Vector3 center, Vector2 size)
        {
            var uv = box.PolygonUV != null && box.PolygonUV.Length >= 3
                ? box.PolygonUV
                : FrameBoxDef.CreateRegularPolygon();
            float z = Mathf.Max(0.025f, SpriteUnityPhysicsShape.DefaultThickness * 0.5f);
            var verts = new Vector3[uv.Length * 2];
            for (int i = 0; i < uv.Length; i++)
            {
                var rel = new Vector2((uv[i].x - 0.5f) * size.x, (0.5f - uv[i].y) * size.y);
                verts[i] = center + new Vector3(rel.x, rel.y, -z);
                verts[i + uv.Length] = center + new Vector3(rel.x, rel.y, z);
            }

            var tris = new List<int>(uv.Length * 12);
            for (int i = 1; i < uv.Length - 1; i++)
            {
                tris.Add(0); tris.Add(i); tris.Add(i + 1);
                tris.Add(uv.Length); tris.Add(uv.Length + i + 1); tris.Add(uv.Length + i);
            }
            for (int i = 0; i < uv.Length; i++)
            {
                int n = (i + 1) % uv.Length;
                tris.Add(i); tris.Add(n); tris.Add(uv.Length + n);
                tris.Add(i); tris.Add(uv.Length + n); tris.Add(uv.Length + i);
            }

            var mesh = new Mesh { name = "UnityPhysicsConvexPreview" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
