using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Converts baked sprite collider boxes into flattened Unity Physics
    /// colliders (XY gameplay plane, thin Z). Shapes: Square -> BoxCollider,
    /// Circle -> SphereCollider, Polygon -> ConvexCollider (convex hull of
    /// the authored points — Unity Physics has no concave primitive).
    /// Requires the com.unity.physics package.
    /// </summary>
    public static class SpriteUnityPhysicsShape
    {
        public const float DefaultThickness = 0.05f;

        public static string ShapeLabel(SpriteColliderShape shape)
        {
            switch (shape)
            {
                case SpriteColliderShape.Circle: return "Sphere";
                case SpriteColliderShape.Polygon: return "Convex";
                default: return "Box";
            }
        }

        /// <summary>
        /// Character body boxes (Lifetime 1) that should bake into Unity Physics.
        /// Skips hidden boxes.
        /// </summary>
        public static void CollectCharacterBodyBoxes(
            SpriteSheetProfile data, List<FrameBoxDef> into)
        {
            into.Clear();
            if (data?.Hitboxes == null)
                return;
            for (int i = 0; i < data.Hitboxes.Count; i++)
            {
                var h = data.Hitboxes[i];
                if (h == null || h.Hidden)
                    continue;
                if (h.Lifetime != 1)
                    continue;
                into.Add(h);
            }
        }

        /// <summary>
        /// Count authored shapes that will bake (for inspector summary).
        /// </summary>
        public static void CountShapes(
            IList<FrameBoxDef> boxes, out int boxesN, out int spheres, out int convex)
        {
            boxesN = spheres = convex = 0;
            if (boxes == null)
                return;
            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                if (b == null)
                    continue;
                switch (b.Shape)
                {
                    case SpriteColliderShape.Circle: spheres++; break;
                    case SpriteColliderShape.Polygon: convex++; break;
                    default: boxesN++; break;
                }
            }
        }

        /// <summary>
        /// Create one collider blob, or a compound when several body boxes exist.
        /// </summary>
        public static BlobAssetReference<Unity.Physics.Collider> CreateBodyCollider(
            IList<FrameBoxDef> boxes, Vector2 pivot, float sizeUnits,
            bool flipX = false, bool flipY = false,
            uint belongsTo = uint.MaxValue, uint collidesWith = uint.MaxValue,
            float thickness = DefaultThickness)
        {
            if (boxes == null || boxes.Count == 0)
            {
                var fallback = new FrameBoxDef
                {
                    ClipName = "body",
                    RectUV = new Rect(0f, 0f, 1f, 1f),
                    Shape = SpriteColliderShape.Square,
                    Lifetime = 1,
                };
                return CreateCollider(fallback, pivot, sizeUnits, flipX, flipY,
                    belongsTo, collidesWith, thickness);
            }

            if (boxes.Count == 1)
                return CreateCollider(boxes[0], pivot, sizeUnits, flipX, flipY,
                    belongsTo, collidesWith, thickness);

            var children = new NativeArray<CompoundCollider.ColliderBlobInstance>(
                boxes.Count, Allocator.Temp);
            for (int i = 0; i < boxes.Count; i++)
            {
                children[i] = new CompoundCollider.ColliderBlobInstance
                {
                    CompoundFromChild = RigidTransform.identity,
                    Collider = CreateCollider(boxes[i], pivot, sizeUnits, flipX, flipY,
                        belongsTo, collidesWith, thickness),
                };
            }
            var compound = CompoundCollider.Create(children);
            children.Dispose();
            return compound;
        }

        /// <summary>
        /// Create a flattened Unity Physics collider for one baked box.
        /// Geometry matches SpriteColliderWorld's spawn convention: X centered
        /// on the sprite, Y measured from the cell bottom (feet), scaled by
        /// SizeUnits, mirrored on flip.
        /// </summary>
        public static BlobAssetReference<Unity.Physics.Collider> CreateCollider(
            FrameBoxDef box, Vector2 pivot, float sizeUnits, bool flipX, bool flipY,
            uint belongsTo = uint.MaxValue, uint collidesWith = uint.MaxValue,
            float thickness = DefaultThickness)
        {
            SpriteColliderWorld.TryLocalFromUv(box, out var offset,
                out var sizeN, out _);

            var center = new float3(
                (flipX ? 2f * (pivot.x - 0.5f) - offset.x : offset.x) * sizeUnits,
                (flipY ? 2f * pivot.y - offset.y : offset.y) * sizeUnits,
                0f);
            var size = new float2(sizeN.x, sizeN.y) * sizeUnits;

            var filter = new CollisionFilter
            {
                BelongsTo = belongsTo,
                CollidesWith = collidesWith,
            };

            switch (box.Shape)
            {
                case SpriteColliderShape.Circle:
                {
                    var geometry = new SphereGeometry
                    {
                        Center = center,
                        Radius = math.max(size.x, size.y) * 0.5f,
                    };
                    return Unity.Physics.SphereCollider.Create(geometry, filter);
                }
                case SpriteColliderShape.Polygon:
                {
                    var uv = box.PolygonUV != null && box.PolygonUV.Length >= 3
                        ? box.PolygonUV
                        : FrameBoxDef.CreateRegularPolygon();
                    float z = math.max(0.005f, thickness * 0.5f);
                    var points = new NativeArray<float3>(uv.Length * 2, Allocator.Temp);
                    for (int i = 0; i < uv.Length; i++)
                    {
                        var rel = new float2(
                            (flipX ? -1f : 1f) * (uv[i].x - 0.5f) * size.x,
                            (0.5f - uv[i].y) * size.y);
                        points[i * 2] = center + new float3(rel.x, rel.y, -z);
                        points[i * 2 + 1] = center + new float3(rel.x, rel.y, z);
                    }
                    var parameters = new ConvexHullGenerationParameters
                    {
                        BevelRadius = 0f,
                    };
                    var collider = Unity.Physics.ConvexCollider.Create(
                        points, parameters, filter);
                    points.Dispose();
                    return collider;
                }
                default:
                {
                    var geometry = new BoxGeometry
                    {
                        Center = center,
                        Size = new float3(size.x, size.y, thickness),
                        Orientation = quaternion.identity,
                        BevelRadius = 0f,
                    };
                    return Unity.Physics.BoxCollider.Create(geometry, filter);
                }
            }
        }
    }
}
