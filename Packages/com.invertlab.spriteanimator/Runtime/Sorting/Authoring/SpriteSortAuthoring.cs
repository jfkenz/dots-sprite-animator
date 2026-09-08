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
                 "One order step = 0.001 world units of z (the smallest safe depth step).")]
        public int OrderInLayer;

        [Tooltip("Manual depth nudge in thousandths of a z unit: 1 = 0.001 z, " +
                 "1000 = one full layer. Positive = closer to the camera (on top), " +
                 "same direction as Layer/Order.")]
        [HideInInspector]
        public int DepthOffset;

        [Tooltip("ON (default): depth is authored once — pin z at startup and skip " +
                 "the periodic re-pin entirely (zero per-tick cost). Turn OFF only " +
                 "when gameplay will write SpriteSortDepth on this entity at runtime.")]
        public bool Static = true;

        /// <summary>World z this authoring bakes (what the inspector shows).</summary>
        public float BakedDepth => SpriteSortDepth.FromLayerOrder(SortLayer, OrderInLayer, DepthOffset);

        /// <summary>
        /// Called by Unity when the component is added (or reset): adopt the
        /// GameObject's current z so the sprite does not jump, then layer/order
        /// edits move it from there. Scene z converts to thousandths (0.736 → −736).
        /// </summary>
        void Reset()
        {
            DepthOffset = Mathf.RoundToInt(-transform.position.z * 1000f);
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
        /// The message carries the exact numbers and the exact fix.
        /// </summary>
        public static DepthStatus CheckDepth(float depth, Camera camera, out string message)
        {
            message = null;
            if (camera == null)
                return DepthStatus.Ok;

            float camZ = camera.transform.position.z;
            float near = camera.nearClipPlane;
            float far = camera.farClipPlane;
            float nearEdge = camZ + near;
            float farEdge = camZ + far;

            if (depth < nearEdge || depth > farEdge)
            {
                message = depth < nearEdge
                    ? $"baked z {depth:0.####} is {nearEdge - depth:0.####} units BEHIND the near plane " +
                      $"— this sprite will NOT render.\n" +
                      $"Camera '{camera.name}': z {camZ:0.###}, near {near:0.##}, far {far:0.#} " +
                      $"→ visible z range {nearEdge:0.###} … {farEdge:0.#}.\n" +
                      $"Fix: raise Depth Offset / Sort Layer by at least {(nearEdge - depth) + 0.001f:0.###} " +
                      $"(positive = toward the camera), or move the camera further back in −z."
                    : $"baked z {depth:0.####} is {depth - farEdge:0.####} units BEYOND the far plane " +
                      $"— this sprite will NOT render.\n" +
                      $"Camera '{camera.name}': z {camZ:0.###}, near {near:0.##}, far {far:0.#} " +
                      $"→ visible z range {nearEdge:0.###} … {farEdge:0.#}.\n" +
                      $"Fix: lower Depth Offset / Sort Layer by at least {(depth - farEdge) + 0.001f:0.###}, " +
                      $"or raise the camera's Far clip plane above {(depth - camZ):0.#}.";
                return DepthStatus.Invisible;
            }

            if (depth < nearEdge + 1f)
            {
                message = $"baked z {depth:0.####} sits only {depth - nearEdge:0.##} units in front of " +
                          $"the near plane ({nearEdge:0.###}) — z-fighting risk zone (keep ≥ 1 unit).\n" +
                          $"Sprites stacked this close to the camera can flicker against each other. " +
                          $"Nudge depth back toward {nearEdge + 1f:0.###} or raise the near clip.";
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
            if (!SpriteSortDepth.StaysInsideLayer(OrderInLayer))
            {
                float drift = Mathf.Abs(OrderInLayer * SpriteSortDepth.OrderStep);
                Debug.LogWarning(
                    $"[SpriteSortAuthoring] '{name}': Order In Layer {OrderInLayer} drifts " +
                    $"{drift:0.####} of z — past half a sorting layer (0.5) and can land on the " +
                    $"same depth as sprites in a neighbouring Sort Layer. Keep orders under " +
                    $"{Mathf.FloorToInt(SpriteSortDepth.LayerStep * 0.5f / SpriteSortDepth.OrderStep)} " +
                    "or give this sprite its own Sort Layer.", this);
            }

            var camera = Camera.main;
            if (camera == null)
                camera = FindAnyObjectByType<Camera>();
            var status = CheckDepth(BakedDepth, camera, out var message);
            if (status == DepthStatus.Invisible)
                Debug.LogWarning(
                    $"[SpriteSortAuthoring] '{name}' (Layer {SortLayer}, Order {OrderInLayer}, " +
                    $"Offset {DepthOffset}): {message}", this);
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
                // one-shot startup pin for every sort sprite; Static (default)
                // additionally opts the entity out of the periodic re-pin
                AddComponent<SpriteSortPinPending>(entity);
                if (authoring.Static)
                    AddComponent<SpriteSortStatic>(entity);
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
