using Unity.Entities;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Z-depth control for a baked sprite (SpriteRenderer-style). Put it on
    /// the same GameObject as <see cref="SpriteAnimSetAuthoring"/>; the Baker
    /// adds <see cref="SpriteSortDepth"/> to the sprite entity and
    /// <see cref="SpriteSortDepthSystem"/> owns the entity's world z every
    /// refresh — gameplay movement stays free to use x/y.
    /// ALL fields follow one rule: higher = drawn on top (maps to smaller
    /// world z, matching the default 2D camera at z −10 looking +z).
    /// Requires the XY shader layout (the default).
    /// </summary>
    [AddComponentMenu("DOTS Sprite Animator/Sprite Sort Authoring")]
    [DisallowMultipleComponent]
    public class SpriteSortAuthoring : MonoBehaviour
    {
        [Header("Z Depth (higher = on top)")]
        [Tooltip("2D-style sorting layer. Each layer is 1 world unit of z away from the next. " +
                 "Higher layer = on top.")]
        public int SortLayer;

        [Tooltip("Order within the layer, like SpriteRenderer's Order in Layer. " +
                 "One order step = 0.00001 world units of z.")]
        public int OrderInLayer;

        [Tooltip("Manual depth nudge in world units. Positive = closer to the camera " +
                 "(on top), negative = behind. Same direction as Layer/Order.")]
        public float DepthOffset;

        /// <summary>World z this authoring bakes (what the inspector shows).</summary>
        public float BakedDepth => SpriteSortDepth.FromLayerOrder(SortLayer, OrderInLayer, DepthOffset);

        /// <summary>
        /// Called by Unity when the component is added (or reset): adopt the
        /// GameObject's current z so the sprite does not jump, then layer/order
        /// edits move it from there. Sign is negated because DepthOffset is
        /// "higher = on top" while world z is "lower = on top".
        /// </summary>
        void Reset()
        {
            DepthOffset = -transform.position.z;
#if UNITY_EDITOR
            SpriteAuthoringBundle.Ensure(gameObject);
#endif
        }

#if UNITY_EDITOR
        /// <summary>Result of the editor-only camera-range check.</summary>
        public enum DepthStatus { Ok, Risky, Invisible }

        /// <summary>
        /// Editor-only: compare a baked depth against a camera's clip slab
        /// [camera.z + nearClipPlane, camera.z + farClipPlane]. A sprite whose
        /// depth lands outside that slab is clipped — it will NOT render.
        /// </summary>
        public static DepthStatus CheckDepth(float depth, Camera camera, out string message)
        {
            message = null;
            if (camera == null)
                return DepthStatus.Ok;

            float camZ = camera.transform.position.z;
            float nearEdge = camZ + camera.nearClipPlane;
            float farEdge = camZ + camera.farClipPlane;

            if (depth < nearEdge || depth > farEdge)
            {
                message = depth < nearEdge
                    ? $"baked depth {depth:0.####} is behind the camera's near plane " +
                      $"({nearEdge:0.###}; camera z {camZ:0.###}) — this sprite will NOT render. " +
                      "Raise Depth Offset / Sort Layer."
                    : $"baked depth {depth:0.####} is beyond the camera's far plane " +
                      $"({farEdge:0.#}) — this sprite will NOT render. " +
                      "Lower Depth Offset / Sort Layer or widen the camera's far clip.";
                return DepthStatus.Invisible;
            }

            if (depth < nearEdge + 1f)
            {
                message = $"baked depth {depth:0.####} is only {(depth - nearEdge):0.##} units " +
                          "in front of the near plane — z-fighting risk. Nudge it further back.";
                return DepthStatus.Risky;
            }

            return DepthStatus.Ok;
        }

        /// <summary>
        /// Editor-only warning when an edit pushes the sprite outside the
        /// scene camera's clip range (stripped from builds).
        /// </summary>
        void OnValidate()
        {
            if (!SpriteSortDepth.StaysInsideLayer(OrderInLayer, DepthOffset))
            {
                Debug.LogWarning(
                    $"[SpriteSortAuthoring] '{name}': Order In Layer plus Depth Offset crosses " +
                    "half a sorting layer and may overlap a neighbouring layer.", this);
            }

            var camera = Camera.main;
            if (camera == null)
                camera = FindAnyObjectByType<Camera>();
            var status = CheckDepth(BakedDepth, camera, out var message);
            if (status == DepthStatus.Invisible)
                Debug.LogWarning($"[SpriteSortAuthoring] '{name}': {message}", this);
        }
#endif

        sealed class Baker : Baker<SpriteSortAuthoring>
        {
            public override void Bake(SpriteSortAuthoring authoring)
            {
                // Same GameObject as SpriteAnimSetAuthoring → same baked entity.
                // TransformUsageFlags.None defers to the other baker's flags.
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new SpriteSortDepth
                {
                    Value = authoring.BakedDepth,
                });
            }
        }
    }

    [AddComponentMenu("DOTS Sprite Animator/Sprite Sort Settings Authoring")]
    [DisallowMultipleComponent]
    public sealed class SpriteSortSettingsAuthoring : MonoBehaviour
    {
        [Min(0.01f)]
        [Tooltip("Seconds between runtime sorting batches. Default: 0.3.")]
        public float RefreshInterval = SpriteSortDepth.DefaultRefreshInterval;

#if UNITY_EDITOR
        void OnValidate()
        {
            if (RefreshInterval < 0.01f)
                Debug.LogWarning("Sprite sort refresh interval must be at least 0.01 seconds.", this);
        }
#endif

        sealed class Baker : Baker<SpriteSortSettingsAuthoring>
        {
            public override void Bake(SpriteSortSettingsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new SpriteSortSettings
                {
                    RefreshInterval = Mathf.Max(0.01f, authoring.RefreshInterval),
                });
            }
        }
    }
}
