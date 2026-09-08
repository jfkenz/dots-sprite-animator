using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// SpriteRenderer-equivalent for baked sprites: shows ONE static cell from
    /// a profile sheet — no clips, no player, no animation clock (profiles
    /// without clips are fine). Sheet texture, grid, aspect and cropped-cell
    /// UVs all come from the profile; SheetIndex picks which sheet. Use the
    /// inspector's Pick Cell button to choose Row/Column by clicking the
    /// sheet. Static sprites batch per-sheet alongside animated ones and work
    /// with SpriteSortAuthoring depth.
    /// </summary>
    [AddComponentMenu("DOTS Sprite Animator/Sprite Static Authoring")]
    [DisallowMultipleComponent]
    public class SpriteStaticAuthoring : MonoBehaviour
    {
        [Tooltip("Profile authored in Window > DOTS Sprite Animator. The sheet texture " +
                 "and grid come from here (profiles without clips work fine).")]
        public ScriptableSpriteSheetProfile Profile;

        [Tooltip("Show this sprite: edit-mode preview quad + baked render in play mode. " +
                 "OFF hides it everywhere without removing the component.")]
        public bool ShowSpriteInScene = true;

        [Tooltip("Which sheet of the profile (0 = first). Adjusted by the picker's sheet buttons.")]
        [HideInInspector] public int SheetIndex;

        [Tooltip("Cell row, 0 = top row (same convention as clip authoring).")]
        [Min(0)] public int Row;

        [Tooltip("Cell column, 0 = left.")]
        [Min(0)] public int Column;

        [Tooltip("World height of the sprite, overriding the profile's scale for THIS " +
                 "instance only (the profile asset is never modified). 1 = one cell tall.")]
        [Min(0.001f)] public float SizeUnits = 1f;

        [Tooltip("OFF: use the profile's pivot for this cell. ON: use the local Pivot " +
                 "below for THIS instance only (the profile asset is never modified).")]
        public bool OverridePivot;

        [Tooltip("Anchor inside the cell (0-1 per axis). (0.5, 0.5) = center, (0.5, 0) = " +
                 "bottom-center — the transform position sits on this point, and rotation " +
                 "and scaling pivot from it. Only used with Override Pivot ON.")]
        public Vector2 Pivot = new Vector2(0.5f, 0.5f);

        public Color Tint = Color.white;

        [Header("Facing")]
        [Tooltip("Mirror left-right.")]
        public bool FlipX;

        [Tooltip("Mirror top-bottom.")]
        public bool FlipY;

        [Header("Debug")]
        [Tooltip("Draw the sprite bounds (cyan) and pivot cross (yellow) in the Scene " +
                 "view while this object is selected.")]
        public bool ShowSceneGizmos = true;

        [Tooltip("Keep a BoxCollider2D on this GameObject sized to the sprite " +
                 "(cropped content when the profile uses Cropped layout, else the full " +
                 "cell). Re-synced on every change; removed when OFF.")]
        public bool AddUnityBoxCollider;

        /// <summary>
        /// Effective pivot for this instance: the profile's pivot (resolved
        /// for the current sheet), or the local Pivot when Override Pivot is
        /// on. Returns false when no profile exists (falls back to center).
        /// </summary>
        public bool ResolvePivot(out Vector2 pivot)
        {
            var data = Profile != null ? Profile.Data : null;
            if (!OverridePivot && data != null)
            {
                data.EnsureSheets();
                var sheetDef = data.SheetAt(Mathf.Max(0, SheetIndex));
                if (sheetDef != null)
                {
                    // per-cell override wins over the sheet pivot
                    int slot = Mathf.Clamp(Row, 0, Mathf.Max(1, sheetDef.Rows) - 1)
                               * Mathf.Max(1, sheetDef.Columns)
                               + Mathf.Clamp(Column, 0, Mathf.Max(1, sheetDef.Columns) - 1);
                    if (SpriteSheetProfile.TryGetCellPivot(sheetDef, slot, out var cellPivot))
                    {
                        pivot = cellPivot;
                        return true;
                    }
                    pivot = SpriteSocketWorld.ResolvePivot(data, sheetDef);
                    return true;
                }
            }

            pivot = OverridePivot ? Pivot : new Vector2(0.5f, 0.5f);
            return OverridePivot;
        }

        /// <summary>
        /// Resolve the effective sheet from the profile. False when no
        /// profile/texture is available. Crops is non-null when the sheet uses
        /// the Cropped cell layout.
        /// </summary>
        public bool ResolveSheet(out Texture2D texture, out int cols, out int rows,
                                 out Vector4[] crops)
        {
            crops = null;
            texture = null;
            cols = 1;
            rows = 1;

            var data = Profile != null ? Profile.Data : null;
            if (data == null)
                return false;

            data.EnsureSheets();
            var sheetDef = data.SheetAt(Mathf.Max(0, SheetIndex));
            if (sheetDef == null)
            {
                texture = data.Sheet;
                cols = Mathf.Max(1, data.Columns);
                rows = Mathf.Max(1, data.Rows);
                return texture != null;
            }

            texture = sheetDef.Texture != null ? sheetDef.Texture : data.Sheet;
            cols = Mathf.Max(1, sheetDef.Columns);
            rows = Mathf.Max(1, sheetDef.Rows);
            if (sheetDef.CellLayoutMode == SpriteSheetCellLayoutMode.Cropped &&
                SpriteSheetProfile.HasCroppedCellData(sheetDef))
            {
                var built = SpriteSheetProfile.BuildCellCropSTArray(sheetDef);
                if (built != null && built.Length == cols * rows)
                    crops = built;
            }
            return texture != null;
        }

        /// <summary>Flat slot (row-major, row 0 = top) this authoring shows.</summary>
        public int CellSlot
        {
            get
            {
                if (!ResolveSheet(out _, out int cols, out int rows, out _))
                    return 0;
                return Mathf.Clamp(Row, 0, rows - 1) * cols + Mathf.Clamp(Column, 0, cols - 1);
            }
        }

        const string PreviewMaterialName = "SpriteStaticPreviewMaterial";

        // scene objects get OnEnable on play-entry: kill the editor preview
        // so it cannot double-render on top of the baked DOTS sprite
        void OnEnable()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                var renderer = GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.enabled = false;
            }
#endif
        }

#if UNITY_EDITOR
        void Reset()
        {
            if (GetComponent<MeshFilter>() == null)
                UnityEditor.Undo.AddComponent<MeshFilter>(gameObject);
            if (GetComponent<MeshRenderer>() == null)
                UnityEditor.Undo.AddComponent<MeshRenderer>(gameObject);
            // attached by now, so the bundle takes the static branch (adds
            // only the sort authoring — never the animated stack)
            SpriteAuthoringBundle.Ensure(gameObject);
            UpdatePreview();
            SyncUnityBoxCollider();
        }

        /// <summary>
        /// Scene-view debug draw (gated by Show Scene Gizmos): sprite bounds
        /// (cyan) + pivot point (yellow cross on the transform origin — the
        /// pivot sits there by definition), and the collider box (orange)
        /// when the Unity box collider is enabled. Rotation-aware.
        /// </summary>
        void OnDrawGizmosSelected()
        {
            if (!ShowSceneGizmos)
                return;
            if (!ResolveSheet(out var texture, out int cols, out int rows, out _) ||
                texture == null)
                return;

            ResolvePivot(out var gPivot);
            float aspect = (texture.width / (float)cols) / Mathf.Max(1f, texture.height / (float)rows);
            float w = SizeUnits * aspect;
            float h = SizeUnits;
            float ox = (0.5f - Mathf.Clamp01(gPivot.x)) * w;
            float oy = (0.5f - Mathf.Clamp01(gPivot.y)) * h;
            if (FlipX) ox = -ox;
            if (FlipY) oy = -oy;

            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

            // bounds rect around the actual sprite
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireCube(new Vector3(ox, oy, 0f), new Vector3(w, h, 0.02f));

            // collider box (orange) — matches the anim set's collider gizmos
            if (AddUnityBoxCollider && ColliderSizeOffset(out var cSize, out var cOff))
            {
                Gizmos.color = new Color(1f, 0.55f, 0.15f, 0.95f);
                Gizmos.DrawWireCube(new Vector3(cOff.x, cOff.y, 0f),
                    new Vector3(cSize.x, cSize.y, 0.02f));
            }

            // pivot cross at the transform origin
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 1f);
            const float s = 0.06f;
            Gizmos.DrawLine(new Vector3(-s, 0f, 0f), new Vector3(s, 0f, 0f));
            Gizmos.DrawLine(new Vector3(0f, -s, 0f), new Vector3(0f, s, 0f));
            Gizmos.DrawSphere(Vector3.zero, 0.015f);
        }

        void OnValidate()
        {
#if UNITY_EDITOR
            // static and animated authoring are mutually exclusive — both
            // bakers would add duplicate components to the same entity
            var set = GetComponent<SpriteAnimSetAuthoring>();
            var player = GetComponent<SpriteAnimPlayerAuthoring>();
            var colliderAuthoring = GetComponent<SpriteColliderAuthoring>();
            if (set != null || player != null || colliderAuthoring != null)
            {
                Debug.LogError(
                    $"[{nameof(SpriteStaticAuthoring)}] '{name}': cannot coexist with animated " +
                    $"authoring (set={(set != null)} player={(player != null)} " +
                    $"collider={(colliderAuthoring != null)}) — removing the animated components.",
                    set != null ? (Object)set : (Object)player);
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this == null)
                        return;
                    if (colliderAuthoring != null)
                        UnityEditor.Undo.DestroyObjectImmediate(colliderAuthoring);
                    if (player != null)
                        UnityEditor.Undo.DestroyObjectImmediate(player);
                    if (set != null)
                        UnityEditor.Undo.DestroyObjectImmediate(set);
                };
                return; // animated stack is going away; skip preview work
            }
#endif

            if (Profile == null)
                Debug.LogWarning(
                    $"[SpriteStaticAuthoring] '{name}': assign a Profile (Window > DOTS Sprite " +
                    "Animator).", this);

            if (!Application.isPlaying)
            {
                UpdatePreview();
                SyncUnityBoxCollider();
            }
        }

        /// <summary>
        /// World-space size and pivot offset of the collider box: the cell
        /// (SizeUnits scaled), or the cropped content rect when the profile
        /// uses Cropped layout. False when no sheet resolves.
        /// </summary>
        bool ColliderSizeOffset(out Vector2 size, out Vector2 offset)
        {
            size = offset = default;
            if (!ResolveSheet(out var texture, out int cols, out int rows, out var crops))
                return false;
            ResolvePivot(out var cPivot);

            float aspect = (texture.width / (float)cols) / Mathf.Max(1f, texture.height / (float)rows);
            float w = SizeUnits * aspect;
            float h = SizeUnits;

            int slot = CellSlot;
            if (crops != null && slot >= 0 && slot < crops.Length)
            {
                // CropST: xy = content size (cell uv), zw = content origin
                // (uv bottom-left inside the cell)
                var crop = crops[slot];
                Vector2 contentSize = new Vector2(crop.x * w, crop.y * h);
                Vector2 contentCenter = new Vector2(
                    (crop.z + crop.x * 0.5f - 0.5f) * w,
                    (crop.w + crop.y * 0.5f - 0.5f) * h);
                size = contentSize;

                // pivot is cell-relative; carry the content center with it
                offset = new Vector2(
                    (0.5f - Mathf.Clamp01(cPivot.x)) * w,
                    (0.5f - Mathf.Clamp01(cPivot.y)) * h) + contentCenter;
            }
            else
            {
                size = new Vector2(w, h);
                offset = new Vector2(
                    (0.5f - Mathf.Clamp01(cPivot.x)) * w,
                    (0.5f - Mathf.Clamp01(cPivot.y)) * h);
            }

            if (FlipX) offset.x = -offset.x;
            if (FlipY) offset.y = -offset.y;
            return true;
        }

        /// <summary>
        /// Add/update/remove the BoxCollider2D on this GameObject so it always
        /// matches the sprite (same idea as the anim set's Sync Unity
        /// Colliders, single-box edition). Editor only.
        /// </summary>
        void SyncUnityBoxCollider()
        {
            var box = GetComponent<BoxCollider2D>();
            if (!AddUnityBoxCollider)
            {
                if (box != null)
                    UnityEditor.Undo.DestroyObjectImmediate(box);
                return;
            }

            if (!ColliderSizeOffset(out var size, out var offset))
                return;

            if (box == null)
                box = UnityEditor.Undo.AddComponent<BoxCollider2D>(gameObject);
            box.size = size;
            box.offset = offset;
        }

        /// <summary>
        /// Rebuild the editor preview quad: UV-baked cell on the same
        /// GameObject's MeshFilter/MeshRenderer (same pattern as
        /// SpriteAnimSetAuthoring's scene preview).
        /// </summary>
        public void UpdatePreview()
        {
            var filter = GetComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            if (filter == null || renderer == null)
                return;

            if (!ShowSpriteInScene || !ResolveSheet(out var texture, out int cols, out int rows, out _))
            {
                // hidden (or unresolvable): keep a serializable builtin quad so
                // a subscene bake never sees a null/DontSave mesh (same guard
                // the anim set preview uses)
                renderer.SetPropertyBlock(null);
                renderer.enabled = false;
                SanitizePreview(filter);
                return;
            }

            var shader = Shader.Find(SpriteShaderLibrary.PreviewShader)
                         ?? Shader.Find(SpriteShaderLibrary.UnlitShader);
            if (shader == null)
                return;

            var mat = renderer.sharedMaterial;
            if (mat == null || mat.shader != shader || mat.name != PreviewMaterialName)
            {
                mat = new Material(shader)
                {
                    name = PreviewMaterialName,
                    hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild,
                };
                renderer.sharedMaterial = mat;
            }
            mat.mainTexture = texture;
            mat.color = Tint; // preview shader's _Color

            filter.sharedMesh = BuildPreviewMesh(texture, cols, rows);
            renderer.enabled = true;
        }

        Mesh BuildPreviewMesh(Texture2D texture, int cols, int rows)
        {
            float aspect = (texture.width / (float)cols) / Mathf.Max(1f, texture.height / (float)rows);
            int col = Mathf.Clamp(Column, 0, cols - 1);
            int row = Mathf.Clamp(Row, 0, rows - 1);

            // cell rect in sheet UV (bottom-left origin)
            float u0 = col / (float)cols;
            float u1 = (col + 1) / (float)cols;
            float v0 = 1f - (row + 1) / (float)rows;
            float v1 = 1f - row / (float)rows;
            if (FlipX) (u0, u1) = (u1, u0);
            if (FlipY) (v0, v1) = (v1, v0);

            // world-space quad: SizeUnits tall, aspect wide, pivot on the origin
            // (matches the bake: Scale = SizeUnits + frame Offset carries pivot)
            float w = SizeUnits * aspect;
            float h = SizeUnits;
            ResolvePivot(out var pPivot);
            float ox = (0.5f - Mathf.Clamp01(pPivot.x)) * w;
            float oy = (0.5f - Mathf.Clamp01(pPivot.y)) * h;
            if (FlipX) ox = -ox;
            if (FlipY) oy = -oy;

            var mesh = new Mesh { name = "SpriteStaticPreviewQuad" };
            mesh.vertices = new[]
            {
                new Vector3(ox - w * 0.5f, oy - h * 0.5f, 0f),
                new Vector3(ox - w * 0.5f, oy + h * 0.5f, 0f),
                new Vector3(ox + w * 0.5f, oy + h * 0.5f, 0f),
                new Vector3(ox + w * 0.5f, oy - h * 0.5f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(u0, v0),
                new Vector2(u0, v1),
                new Vector2(u1, v1),
                new Vector2(u1, v0),
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mesh.hideFlags = HideFlags.DontSave;
            return mesh;
        }

        /// <summary>
        /// Swap any DontSave preview mesh for the serializable builtin quad
        /// before it can leak into a subscene bake (same guard the anim set
        /// preview uses).
        /// </summary>
        static void SanitizePreview(MeshFilter filter)
        {
            var mesh = filter.sharedMesh;
            // only swap an existing DontSave preview mesh — a null mesh needs
            // nothing, and touching builtin resources here polluted edit-mode
            // test runs (GUILayout errors from the runner context)
            if (mesh != null && (mesh.hideFlags & HideFlags.DontSave) != 0)
                filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("New-Quad.fbx");
        }
#endif

        sealed class Baker : Baker<SpriteStaticAuthoring>
        {
            public override void Bake(SpriteStaticAuthoring authoring)
            {
                if (authoring.Profile != null)
                    DependsOn(authoring.Profile);

                // ShowSpriteInScene hides the editor preview quad only —
                // exactly like the anim set's toggle, the baked sprite still
                // renders in play mode. (Disable the GameObject to exclude it
                // from the bake entirely.)

                if (!authoring.ResolveSheet(out var texture, out int cols, out int rows,
                                            out var crops))
                    return;

                DependsOn(texture);

                byte useCrops = (byte)(crops != null ? 1 : 0);

                // ---- sheet-definition entity (dedup happens in the registry) ----
                var sheetEntity = CreateAdditionalEntity(TransformUsageFlags.None);
                float cellAspect = SpriteSheetProfile.GetCellAspect(texture, cols, rows);
                AddComponent(sheetEntity, new SpriteSheetDefinition
                {
                    Cols = cols,
                    Rows = rows,
                    CellAspect = cellAspect > 0.01f ? cellAspect : 1f,
                    UseCellCrops = useCrops,
                });
                AddComponentObject(sheetEntity, new SpriteSheetAsset { Texture = texture });
                if (useCrops != 0)
                {
                    var cropBuffer = AddBuffer<SpriteAnimCellCrop>(sheetEntity);
                    foreach (var crop in crops)
                        cropBuffer.Add(new SpriteAnimCellCrop
                        {
                            Value = new float4(crop.x, crop.y, crop.z, crop.w),
                        });
                }

                // ---- static sprite entity ----
                // manual transform so SizeUnits (not the GameObject scale)
                // defines the sprite size
                var entity = GetEntity(TransformUsageFlags.None);
                var tr = authoring.transform;
                AddComponent(entity, LocalTransform.FromPositionRotationScale(
                    tr.position, tr.rotation, authoring.SizeUnits));
                AddComponent(entity, new LocalToWorld
                {
                    Value = float4x4.TRS(tr.position, tr.rotation, new float3(authoring.SizeUnits)),
                });
                AddComponent(entity, new SpriteSheetBinding { Sheet = sheetEntity });

                int slot = Mathf.Clamp(authoring.Row, 0, rows - 1) * cols
                           + Mathf.Clamp(authoring.Column, 0, cols - 1);
                // pivot rides as the frame offset: (0.5,0.5) = centered, and
                // the pack job mirrors it under flip (SpriteFlipUtility)
                float aspect = cellAspect > 0.01f ? cellAspect : 1f;
                authoring.ResolvePivot(out var bakedPivot);
                var pivotOffset = new float2(
                    (0.5f - Mathf.Clamp01(bakedPivot.x)) * aspect * authoring.SizeUnits,
                    (0.5f - Mathf.Clamp01(bakedPivot.y)) * authoring.SizeUnits);
                AddComponent(entity, new SpriteAnimFrame
                {
                    Slot = slot,
                    Offset = pivotOffset,
                    Scale = new float2(1f, 1f),
                    Rotation = 0f,
                });
                var t = authoring.Tint;
                AddComponent(entity, new SpriteTint
                {
                    Value = new float4(t.r, t.g, t.b, t.a),
                });
                AddComponent(entity, new SpriteFlip
                {
                    X = (byte)(authoring.FlipX ? 1 : 0),
                    Y = (byte)(authoring.FlipY ? 1 : 0),
                    Pivot = new float2(bakedPivot.x, bakedPivot.y),
                });
                AddComponent(entity, new SpriteAnimEnabled());
                // the GameObject preview must not double-render the baked sprite
                AddComponent<DisableRendering>(entity);
            }
        }
    }
}
