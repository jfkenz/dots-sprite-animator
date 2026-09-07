using System.Collections.Generic;
using Unity.Entities;
using Unity.Physics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Which collider lifetimes this sprite uses.</summary>
    public enum SpriteColliderScope
    {
        /// <summary>Detect from the profile's baked boxes (all lifetimes found).</summary>
        Auto = 0,
        /// <summary>Lifetime 0 — this-frame boxes only (slash windows).</summary>
        Frame = 1,
        /// <summary>Lifetime 1 — character body on every clip.</summary>
        Character = 2,
        /// <summary>Lifetime 2 — boxes scoped to one clip.</summary>
        Clip = 3,
        /// <summary>Everything.</summary>
        All = 4,
    }

    /// <summary>How colliders are detected at runtime.</summary>
    public enum SpriteColliderMethod
    {
        /// <summary>Respect each box's stored Physics flag (query / Unity 2D / both).</summary>
        Auto = 0,
        /// <summary>No Unity children — boxes stay DOTS query data only.</summary>
        Query = 1,
        /// <summary>Boxes authored as Unity 2D spawn as collider children.</summary>
        Unity2D = 2,
        /// <summary>Unity 2D children + frame query windows.</summary>
        Both = 3,
        /// <summary>Body box bakes as a Unity Physics collider (com.unity.physics,
        /// 3D flattened onto the sprite plane) — overlap via Unity Physics queries.</summary>
        UnityPhysics = 4,
    }

    /// <summary>
    /// One component that owns colliders for an animated sprite: pick the
    /// scope (Auto detects which lifetimes exist in the profile) and the
    /// detection method, and this drives the AnimSet's collider baking and
    /// Unity 2D collider spawning — no per-game setup code for the common
    /// cases. Same GameObject as <see cref="SpriteAnimSetAuthoring"/>.
    /// </summary>
    [AddComponentMenu("DOTS Sprite Animator/Sprite Collider Authoring")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteAnimSetAuthoring))]
    public class SpriteColliderAuthoring : MonoBehaviour
    {
        [Header("Detection")]
        [Tooltip("Auto scans the profile's baked boxes for which lifetimes exist " +
                 "(frame / character / clip) and enables all of them.")]
        public SpriteColliderScope Scope = SpriteColliderScope.Auto;

        [Tooltip("Auto = each box spawns by its stored Physics flag (query / Unity 2D / both).\n\nQuery (pure DOTS): NO Unity GameObjects or colliders are spawned. Boxes stay baked data - overlap them yourself with SpriteHitboxQuery.TryGetBounds (bounds-vs-bounds math, no physics package), or pair with Unity Physics 3D overlap queries (flattened boxes) if com.unity.physics is installed. Best for crowds and custom hit detection.\n\nUnity2D: boxes authored as Unity 2D spawn as collider children (Rigidbody2D world, triggers).\n\nBoth = Unity children + frame query windows.\n\nUnityPhysics: the body box bakes as a Unity Physics collider (com.unity.physics, 3D flattened onto the sprite plane) - overlap via Unity Physics queries (OverlapAabb), broadphase-accelerated. Use Bake Unity Physics Preview on this component to see Box / Sphere / Convex in the Scene.")]
        public SpriteColliderMethod Method = SpriteColliderMethod.Auto;

        [Header("Debug")]
        [Tooltip("Draw query AABB gizmos in the Scene view.")]
        public bool ShowSceneGizmos = true;

        public const byte LifetimeFrame = 1 << 0;
        public const byte LifetimeCharacter = 1 << 1;
        public const byte LifetimeClip = 1 << 2;
        public const byte LifetimeAll = LifetimeFrame | LifetimeCharacter | LifetimeClip;
/// <summary>
        /// Resolve the lifetime mask this authoring yields: explicit scopes
        /// map to their bit; Auto ORs the lifetimes actually present in the
        /// profile (all bits when the profile has no boxes).
        /// </summary>
        public byte ResolveLifetimeMask(SpriteSheetProfile data)
        {
            switch (Scope)
            {
                case SpriteColliderScope.Frame: return LifetimeFrame;
                case SpriteColliderScope.Character: return LifetimeCharacter;
                case SpriteColliderScope.Clip: return LifetimeClip;
                case SpriteColliderScope.All: return LifetimeAll;
            }

            byte mask = 0;
            var boxes = data != null ? data.Hitboxes : null;
            if (boxes != null)
            {
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (boxes[i] == null)
                        continue;
                    mask |= (byte)(1 << Mathf.Clamp(boxes[i].Lifetime, 0, 2));
                }
            }
            return mask == 0 ? LifetimeAll : mask;
        }

        /// <summary>Push scope/method onto the sibling AnimSet authoring so
        /// the existing bake + spawn machinery runs with these settings.</summary>
        public void ApplyToAnimSet()
        {
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set == null)
                return;

            var data = set.Profile != null ? set.Profile.Data : null;
            byte mask = ResolveLifetimeMask(data);

            set.ColliderLifetimeMask = mask;
            set.BakeUnityColliders = Method != SpriteColliderMethod.Query &&
                                     Method != SpriteColliderMethod.UnityPhysics;
            set.BakeFrameColliders = Method != SpriteColliderMethod.Query &&
                                     Method != SpriteColliderMethod.UnityPhysics &&
                                     (mask & LifetimeFrame) != 0;
            set.ShowSceneColliderGizmos = ShowSceneGizmos;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                set.ScheduleUnityColliderSync();
#endif
        }

        /// <summary>
        /// Workflow A: bake now. Adds BoxCollider / SphereCollider /
        /// MeshCollider(convex) under UnityPhysicsColliders on this object.
        /// Workflow B: skip this — Play recreates the DOTS hurtbox at runtime.
        /// </summary>
        public int BakeUnityPhysicsPreview()
        {
            var set = GetComponent<SpriteAnimSetAuthoring>();
            var data = set != null && set.Profile != null ? set.Profile.Data : null;
            float sizeUnits = set != null ? set.SizeUnits : 1f;
            return SpriteUnityPhysicsPreview.Bake(transform, data, sizeUnits);
        }

        /// <summary>Remove baked UnityPhysicsColliders + marker.</summary>
        public void ClearUnityPhysicsPreview()
        {
            SpriteUnityPhysicsPreview.Clear(transform);
        }

        /// <summary>True when Bake was used and collider children exist.</summary>
        public bool HasBakedUnityPhysicsColliders =>
            SpriteUnityPhysicsPreview.HasBaked(transform);

#if UNITY_EDITOR
        void Reset()
        {
            ApplyToAnimSet();
        }

        void OnValidate()
        {
            if (!Application.isPlaying)
                ApplyToAnimSet();
        }
#endif
    }

    /// <summary>Tag: this sprite's body box is baked as a Unity Physics
    /// collider and participates in PhysicsWorld overlap queries.</summary>
    public struct SpriteHurtbox : IComponentData { }

    sealed class Baker : Baker<SpriteColliderAuthoring>
    {
        public override void Bake(SpriteColliderAuthoring authoring)
        {
            if (authoring.Method != SpriteColliderMethod.UnityPhysics)
                return;

            var entity = GetEntity(TransformUsageFlags.Dynamic);

            var set = authoring.GetComponent<SpriteAnimSetAuthoring>();
            if (set != null && set.Profile != null)
                DependsOn(set.Profile);

            var data = set != null && set.Profile != null ? set.Profile.Data : null;
            var sheet = data != null ? SpriteSocketWorld.DisplaySheet(data, null) : null;
            var pivot = data != null && sheet != null
                ? SpriteSocketWorld.ResolvePivot(data, sheet)
                : new Vector2(0.5f, 0.5f);
            float sizeUnits = set != null ? set.SizeUnits : 1f;

            var bodies = new List<FrameBoxDef>();
            SpriteUnityPhysicsShape.CollectCharacterBodyBoxes(data, bodies);
            var blob = SpriteUnityPhysicsShape.CreateBodyCollider(
                bodies, pivot, sizeUnits, false, false);

            AddComponent(entity, new PhysicsCollider { Value = blob });
            AddComponent(entity, new SpriteHurtbox());
            AddComponent(entity, PhysicsMass.CreateKinematic(blob.Value.MassProperties));
            AddComponent(entity, new PhysicsVelocity());
            AddComponent(entity, new PhysicsGravityFactor { Value = 0f });
            AddSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });
        }
    }
}
