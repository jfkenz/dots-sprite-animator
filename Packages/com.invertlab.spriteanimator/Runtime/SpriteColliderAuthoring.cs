using Unity.Entities;
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

        [Tooltip("Auto = each box spawns by its stored Physics flag (query / Unity 2D / both).\n\nQuery (pure DOTS): NO Unity GameObjects or colliders are spawned. Boxes stay baked data - overlap them yourself with SpriteHitboxQuery.TryGetBounds (bounds-vs-bounds math, no physics package), or pair with Unity Physics 3D overlap queries (flattened boxes) if com.unity.physics is installed. Best for crowds and custom hit detection.\n\nUnity2D: boxes authored as Unity 2D spawn as collider children (Rigidbody2D world, triggers).\n\nBoth = Unity children + frame query windows.")]
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

            // Auto: detect from baked data
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
            set.BakeUnityColliders = Method != SpriteColliderMethod.Query;
            set.BakeFrameColliders = Method != SpriteColliderMethod.Query &&
                                     (mask & LifetimeFrame) != 0;
            set.ShowSceneColliderGizmos = ShowSceneGizmos;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                set.ScheduleUnityColliderSync();
#endif
        }

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
}
