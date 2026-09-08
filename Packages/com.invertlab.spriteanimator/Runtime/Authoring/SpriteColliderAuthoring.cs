using Unity.Entities;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Which collider lifetimes this sprite uses.</summary>
    public enum SpriteColliderScope
    {
        Auto = 0,
        Frame = 1,
        Character = 2,
        Clip = 3,
        All = 4,
    }

    /// <summary>How colliders are detected at runtime.</summary>
    public enum SpriteColliderMethod
    {
        Auto = 0,
        Query = 1,
        Unity2D = 2,
        Both = 3,
        /// <summary>Requires optional com.unity.physics. Character body bakes
        /// as Unity Physics; frame boxes stay AABB query data.</summary>
        UnityPhysics = 4,
    }

    /// <summary>
    /// Owns collider scope/method for an animated sprite. Same GameObject as
    /// <see cref="SpriteAnimSetAuthoring"/>. Unity Physics features live in the
    /// optional InvertLab.SpriteAnimator.UnityPhysics assembly.
    /// </summary>
    [AddComponentMenu("DOTS Sprite Animator/Sprite Collider Authoring")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteAnimSetAuthoring))]
    public class SpriteColliderAuthoring : MonoBehaviour
    {
        [Header("Detection")]
        [Tooltip("Auto scans the profile's baked boxes for which lifetimes exist.")]
        public SpriteColliderScope Scope = SpriteColliderScope.Auto;

        [Tooltip("Query = AABB data only (SpriteHitboxQuery).\nUnity2D = Collider2D children.\nBoth = Unity2D + frame query.\nUnityPhysics = optional com.unity.physics (Character body bake; frame stays AABB query).")]
        public SpriteColliderMethod Method = SpriteColliderMethod.Auto;

        [Header("Debug")]
        [Tooltip("Draw query AABB gizmos in the Scene view.")]
        public bool ShowSceneGizmos = true;

        public const byte LifetimeFrame = 1 << 0;
        public const byte LifetimeCharacter = 1 << 1;
        public const byte LifetimeClip = 1 << 2;
        public const byte LifetimeAll = LifetimeFrame | LifetimeCharacter | LifetimeClip;

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

#if UNITY_EDITOR
        void Reset() => ApplyToAnimSet();

        void OnValidate()
        {
            if (!Application.isPlaying)
                ApplyToAnimSet();
        }
#endif
    }

    /// <summary>Tag: entity participates as a Unity Physics hurtbox when the
    /// optional Unity Physics assembly is present.</summary>
    public struct SpriteHurtbox : IComponentData { }
}
