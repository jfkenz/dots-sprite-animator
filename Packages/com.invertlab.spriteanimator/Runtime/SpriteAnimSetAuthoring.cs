using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif



namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Authoring: attach to any GameObject to make it a pure-ECS animated sprite.
    /// Point it at a spritesheet texture and define the animation states (rows of
    /// the sheet, or explicit frame lists).
    ///
    /// Bakes into: LocalTransform + SpriteAnimSheetAsset/Def + SpriteAnimSetRef
    /// (all clips in one blob) + SpriteAnimPlayer. Rendering (one material per
    /// sheet cell, crop baked in) is wired by SpriteAnimRenderInitSystem; frame
    /// playback is done by SpriteAnimPlayerSystem.
    ///
    /// Usage from gameplay:  SpriteAnims.Play(em, entity, "Attack");
    /// </summary>
    [DisallowMultipleComponent]
    public class SpriteAnimSetAuthoring : MonoBehaviour
    {
#if UNITY_EDITOR
        void Reset() => SpriteAuthoringBundle.Ensure(gameObject);
#endif

        [Tooltip("Optional profile authored in Window > DOTS Sprite Animator. When set, it overrides Sheet, grid, and Clips below.")]
        public ScriptableSpriteSheetProfile Profile;



        [Tooltip("Spritesheet: grid of frames, left-to-right then top-to-bottom")]
        public Texture2D Sheet;



        [Tooltip("Grid columns / rows in the sheet")]
        public int Columns = 4;
        public int Rows = 4;



        [System.Serializable]
        public struct ClipAuthoring
        {
            public string Name;      // "Idle", "Run", ...
            public int    SheetIndex; // 0-based into SpriteSheetProfile.Sheets
            public int    Row;       // which sheet row (0 = top row)
            public int[]  Frames;    // column indices, in play order (e.g. 0 1 2 3)
            public int[]  FrameRows; // per-frame row override; -1 = use Row
            public float  FrameRate; // frames per second
            public bool   Loop;
            public byte   WrapMode;
            public byte   Interrupt;   // SpriteClipInterrupt.*
            public float  CancelAfter; // 0-1 when Interrupt == AfterTime
            public int    Priority;    // default 0
            public int    OnCompleteClipIndex; // -1 = none
            public int    ComboWindowStartFrame; // inclusive
            public int    ComboWindowEndFrame;   // inclusive; -1 = disabled
            public int    ComboWindowPriorityBoost;
            public float[] FrameDurationScales;
            public byte[] EventIds;
            public float[] EventNormalizedTimes;
            public Vector2[] FrameOffsets;
            public Vector2[] FrameScales;
            public float[] FrameRotations;
            public byte[] FrameTweenModes;
            public string FacingGroup;
            public SpriteFacingDirection FacingDirection;
            public FrameSocketDef[] Sockets;
        }



        [Tooltip("Animation states â€” e.g. soldier: Idle, Run, Attack, Block")]
        public ClipAuthoring[] Clips =
        {
            new ClipAuthoring { Name = "Idle",   Row = 0, Frames = new[] { 0, 1, 2, 3 }, FrameRate = 8f,  Loop = true, OnCompleteClipIndex = -1, ComboWindowEndFrame = -1 },
            new ClipAuthoring { Name = "Run",    Row = 1, Frames = new[] { 0, 1, 2, 3 }, FrameRate = 10f, Loop = true, OnCompleteClipIndex = -1, ComboWindowEndFrame = -1 },
            new ClipAuthoring { Name = "Attack", Row = 2, Frames = new[] { 0, 1, 2, 3 }, FrameRate = 12f, Loop = false, OnCompleteClipIndex = -1, ComboWindowEndFrame = -1 },
            new ClipAuthoring { Name = "Block",  Row = 3, Frames = new[] { 0, 1, 2, 3 }, FrameRate = 8f,  Loop = true, OnCompleteClipIndex = -1, ComboWindowEndFrame = -1 },
        };



        [Tooltip("First clip to play")]
        public int InitialClipIndex = 0;



        [Min(0.01f)]
        public float SizeUnits = 1f;



        [Tooltip("Optional tint")]
        public Color Tint = Color.white;



        [FormerlySerializedAs("ShowScenePreview")]
        [Tooltip("Edit Mode Scene Quad only (bottom-center cell pivot, like the animator preview). Uncheck to hide the sprite mesh. Does not affect Play mode ECS.")]
        public bool ShowSpriteInScene = true;



        [Tooltip("Spawn Unity 2D Box/Circle/Polygon colliders on this object from the profile.")]
        public bool BakeUnityColliders;

        /// <summary>Lifetime filter (frame=1, character=2, clip=4) driven by
        /// SpriteColliderAuthoring; all lifetimes by default.</summary>
        [HideInInspector] public byte ColliderLifetimeMask = 7;



        [Tooltip("Also spawn this-frame slash colliders. Off = Character and This Clip body colliders only.")]
        public bool BakeFrameColliders;



        [Tooltip("Spawn Unity Transform children under SpriteSockets from independent motions and frame sockets.")]
        public bool BakeUnitySockets = true;



        [Tooltip("Draw Query AABB gizmos in the Scene view (custom physics, not Unity colliders).")]
        public bool ShowSceneColliderGizmos = true;



        [Tooltip("Draw socket discs/labels in the Scene view for the current clip/frame.")]
        public bool ShowSceneSocketGizmos = true;



        [Tooltip("Bake an empty Pivot child at the authored profile.Pivot in mesh-local space (not cell feet unless pivot is bottom-center).")]
        public bool BakePivot = true;



        [Tooltip("Draw a Scene gizmo crosshair and Pivot label at the authored profile.Pivot (matches editor green pivot).")]
        public bool ShowScenePivotGizmos = true;





        public bool ApplyFromProfile()
        {
            var data = Profile?.Data;
            if (data == null)
                return false;



            data.EnsureSheets();
            SpriteSheetDef bakeSheet = null;
            if (data.Clips != null && data.Clips.Count > 0)
                bakeSheet = data.SheetForClip(data.Clips[0]);
            if (bakeSheet == null && data.Sheets != null && data.Sheets.Count > 0)
                bakeSheet = data.Sheets[0];



            if (bakeSheet != null)
            {
                if (bakeSheet.Texture != null)
                    Sheet = bakeSheet.Texture;
                Columns = Mathf.Max(1, bakeSheet.Columns);
                Rows = Mathf.Max(1, bakeSheet.Rows);
            }
            else
            {
                if (data.Sheet != null)
                    Sheet = data.Sheet;
                Columns = Mathf.Max(1, data.Columns);
                Rows = Mathf.Max(1, data.Rows);
            }



            if (data.Clips != null && data.Clips.Count > 0)
                Clips = CopyClips(data.Clips);



            int clipCount = Clips != null ? Clips.Length : 0;
            InitialClipIndex = clipCount > 0
                ? Mathf.Clamp(InitialClipIndex, 0, clipCount - 1)
                : 0;
#if UNITY_EDITOR
            RefreshQuadPreview();
#endif
            return true;
        }



        public bool TryGetClipSheet(int clipIndex, out Texture2D texture, out int columns, out int rows, out float ppu)
        {
            texture = Sheet;
            columns = Mathf.Max(1, Columns);
            rows = Mathf.Max(1, Rows);
            ppu = SpriteSheetProfile.DefaultPixelsPerUnit;
            var data = Profile?.Data;
            if (data != null)
            {
                data.EnsureSheets();
                int sheetIndex = 0;
                if (Clips != null && clipIndex >= 0 && clipIndex < Clips.Length)
                    sheetIndex = Clips[clipIndex].SheetIndex;
                else if (data.Clips != null && clipIndex >= 0 && clipIndex < data.Clips.Count)
                    sheetIndex = data.Clips[clipIndex].SheetIndex;
                var def = data.SheetAt(sheetIndex);
                if (def != null)
                {
                    if (def.Texture != null)
                        texture = def.Texture;
                    columns = Mathf.Max(1, def.Columns);
                    rows = Mathf.Max(1, def.Rows);
                    ppu = SpriteSheetProfile.GetPixelsPerUnit(def);
                }
            }
            return texture != null;
        }



        void OnValidate()
        {
#if UNITY_EDITOR
            // static and animated authoring are mutually exclusive — both
            // bakers would add duplicate components to the same entity
            var staticAuthoring = GetComponent<SpriteStaticAuthoring>();
            if (staticAuthoring != null)
            {
                Debug.LogError(
                    $"[{nameof(SpriteAnimSetAuthoring)}] '{name}': animated and static sprite " +
                    "authoring cannot coexist on one GameObject — removing the static authoring.",
                    staticAuthoring);
                var colliderAuthoring = GetComponent<SpriteColliderAuthoring>();
                EditorApplication.delayCall += () =>
                {
                    if (colliderAuthoring != null)
                        Undo.DestroyObjectImmediate(colliderAuthoring);
                    if (staticAuthoring != null)
                        Undo.DestroyObjectImmediate(staticAuthoring);
                };
            }

            if (Profile != null)
                ApplyFromProfile();
            RefreshQuadPreview();
            if (BakeUnityColliders || BakeUnitySockets)
                ScheduleUnityColliderSync();
#endif
        }



#if UNITY_EDITOR
        const string PreviewMeshName = "InvertLab Preview Quad";
        const string PreviewMaterialName = "InvertLab Sprite Preview";
        static bool _loggedMissingPreviewShader;



        void OnEnable()
        {
            // Domain reload / component enable: re-apply crop (DontSave mesh/material).
            if (!Application.isPlaying)
                RefreshQuadPreview();
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // After Play→Stop teardown finishes, rebuild colliders/sockets/preview
            // via delayCall — never Clear/Sync with DestroyImmediate during exit.
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                RefreshQuadPreview();
                if (BakeUnityColliders || BakeUnitySockets || BakePivot)
                    ScheduleUnityColliderSync();
            }
        }

        [InitializeOnLoadMethod]
        static void RegisterPreviewSceneSaveHooks()
        {
            EditorSceneManager.sceneSaving -= OnEditorSceneSaving;
            EditorSceneManager.sceneSaving += OnEditorSceneSaving;
            EditorSceneManager.sceneSaved -= OnEditorSceneSaved;
            EditorSceneManager.sceneSaved += OnEditorSceneSaved;
        }

        static void OnEditorSceneSaving(Scene scene, string path)
        {
            // DontSave preview mesh/material do not serialize; leaving them assigned
            // writes null MeshFilter/MeshRenderer refs into SubScenes and can NRE
            // Entities Graphics on section load. Swap to builtin Quad for the save.
            var sets = Object.FindObjectsByType<SpriteAnimSetAuthoring>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < sets.Length; i++)
            {
                var set = sets[i];
                if (set == null || set.gameObject.scene != scene)
                    continue;
                set.PreparePreviewForSceneSave();
            }
        }

        static void OnEditorSceneSaved(Scene scene)
        {
            var sets = Object.FindObjectsByType<SpriteAnimSetAuthoring>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < sets.Length; i++)
            {
                var set = sets[i];
                if (set == null || set.gameObject.scene != scene)
                    continue;
                if (!Application.isPlaying)
                    set.RefreshQuadPreview();
            }
        }

        void PreparePreviewForSceneSave()
        {
            var filter = GetComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            if (filter == null || renderer == null)
                return;
            SanitizePreviewForSerialization(filter, renderer);
            renderer.SetPropertyBlock(null);
            // Leave renderer.enabled as-is for edit UX; bake uses DisableRendering.
        }

        static void SanitizePreviewForSerialization(MeshFilter filter, MeshRenderer renderer)
        {
            var current = filter.sharedMesh;
            // Unity fake-null: destroyed DontSave mesh compares equal to null.
            if (current == null || current.name == PreviewMeshName)
            {
                var builtin = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                if (builtin != null)
                    filter.sharedMesh = builtin;
            }

            var mat = renderer.sharedMaterial;
            if (mat != null && (mat.name == PreviewMaterialName
                || (mat.hideFlags & HideFlags.DontSaveInEditor) != 0
                || (mat.hideFlags & HideFlags.DontSaveInBuild) != 0
                || (mat.hideFlags & HideFlags.HideAndDontSave) != 0))
            {
                renderer.sharedMaterial = null;
            }
        }



        void RefreshQuadPreview()
        {
            var player = GetComponent<SpriteAnimPlayerAuthoring>();
            if (player != null)
                ApplyQuadPreview(player.ClipIndex, player.Frame);
            else
                ApplyQuadPreview();
        }



        /// <summary>Editor entry: re-apply Scene Quad crop for the current player clip/frame.</summary>
        public void RefreshScenePreview() => RefreshQuadPreview();



        public void ApplyQuadPreview() => ApplyQuadPreview(0, 0);



        public void ApplyQuadPreview(int clipIndex, int frameIndex)
        {
            var filter = GetComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            if (filter == null || renderer == null)
                return;



            if (!ShowSpriteInScene)
            {
                // Keep a serializable builtin Quad (not DontSave preview mesh) so
                // SubScene bake never sees a null mesh / missing DontSave material.
                renderer.SetPropertyBlock(null);
                renderer.enabled = false;
                SanitizePreviewForSerialization(filter, renderer);
                return;
            }

            // Recreate preview mesh before any sheet/shader early-outs so Refresh /
            // EnteredEditMode never leave MeshFilter.sharedMesh null when Show Sprite is on.
            var previewMesh = EnsurePreviewMesh(filter);
            if (previewMesh == null)
                return;

            Texture2D previewSheet = Sheet;
            int previewColumns = Mathf.Max(1, Columns);
            int previewRows = Mathf.Max(1, Rows);
            SpriteSheetDef clipSheet = null;
            if (Profile?.Data != null)
            {
                var data = Profile.Data;
                data.EnsureSheets();
                if (Clips != null && clipIndex >= 0 && clipIndex < Clips.Length)
                    clipSheet = data.SheetAt(Clips[clipIndex].SheetIndex);
                if (clipSheet?.Texture == null)
                    clipSheet = data.SheetAt(0);
                if (clipSheet?.Texture != null)
                {
                    previewSheet = clipSheet.Texture;
                    previewColumns = Mathf.Max(1, clipSheet.Columns);
                    previewRows = Mathf.Max(1, clipSheet.Rows);
                }
            }



            if (previewSheet == null)
                return;



            // Preview must NOT use the DOTS Unlit shader: Entities Graphics can
            // force DOTS_INSTANCING_ON globally, so MeshRenderer ignores material
            // / MPB crop and draws the full sheet (property default 1,1,0,0).
            // Preview shader keeps props in UnityPerMaterial (SRP Batcher / BRG)
            // and ApplyQuadPreview UV-bakes the cell so crop does not need MPB.
            var shader = Shader.Find(SpriteShaderLibrary.PreviewShader);
            if (shader == null)
            {
                if (!_loggedMissingPreviewShader)
                {
                    _loggedMissingPreviewShader = true;
                    Debug.LogError(
                        $"InvertLab: Shader.Find(\"{SpriteShaderLibrary.PreviewShader}\") returned null. " +
                        "Scene Quad preview crop may show the full sheet until the Preview shader imports.",
                        this);
                }
                shader = Shader.Find(SpriteShaderLibrary.UnlitShader);
            }
            if (shader == null)
                return;



            var mat = renderer.sharedMaterial;
            bool usingPreviewShader = shader.name == SpriteShaderLibrary.PreviewShader;
            bool wrongMat = mat == null
                || mat.shader != shader
                || mat.name != PreviewMaterialName
                || (usingPreviewShader && mat.shader != null
                    && mat.shader.name == SpriteShaderLibrary.UnlitShader);
            if (wrongMat)
            {
                mat = new Material(shader)
                {
                    name = PreviewMaterialName,
                    hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
                };
                renderer.sharedMaterial = mat;
            }



            mat.enableInstancing = false;
            // Fallback path only: keep crop working if Preview shader is not imported yet.
            if (mat.shader != null && mat.shader.name == SpriteShaderLibrary.UnlitShader)
            {
                mat.DisableKeyword("DOTS_INSTANCING_ON");
                var kw = new LocalKeyword(mat.shader, "DOTS_INSTANCING_ON");
                if (kw.isValid)
                    mat.SetKeyword(kw, false);
            }



            int cols = previewColumns;
            int rows = previewRows;
            int col = 0;
            int row = 0;
            if (Clips != null && Clips.Length > 0)
            {
                var clip = clipIndex >= 0 && clipIndex < Clips.Length
                    ? Clips[clipIndex]
                    : Clips[0];
                int fi = 0;
                if (clip.Frames != null && clip.Frames.Length > 0)
                    fi = Mathf.Clamp(frameIndex, 0, clip.Frames.Length - 1);
                SpriteClipDef.ResolveSheetCell(clip.Row, clip.Frames, clip.FrameRows, fi,
                    cols, rows, out row, out col);
            }



            int cellIndex = row * cols + col;
            // Cell rect in sheet UV: scale.xy, offset.zw (bottom-left of cell).
            // Cropped mode uses tight opaque rects when present; Grid stays uniform.
            Vector4 cropST = clipSheet != null
                ? SpriteSheetProfile.GetCellCropST(clipSheet, cellIndex)
                : new Vector4(1f / cols, 1f / rows, col * (1f / cols), 1f - (row + 1) * (1f / rows));
            var flip = PreviewFlipVector();



            // Nuclear backup: bake cell (+ flip) into DontSave mesh UVs so a broken
            // crop shader still cannot show the full sheet. Then _CropST = (1,1,0,0).
            // Bottom-center cell pivot (feet at transform): X -0.5..0.5, Y 0..1.
            ApplyBottomCenterCellVertices(previewMesh);
            Vector2 flipPivot = SpriteSheetProfile.DefaultPivot;
            if (clipSheet != null)
                flipPivot = SpriteSocketWorld.ResolvePivot(Profile?.Data, clipSheet);
            else if (Profile?.Data != null)
                flipPivot = SpriteSocketWorld.ResolvePivot(Profile.Data, null);
            BakePreviewCellUVs(previewMesh, cropST, flip.x > 0.5f, flip.y > 0.5f, flipPivot);



            var identityCrop = new Vector4(1f, 1f, 0f, 0f);
            var identityFlip = new Vector4(0f, 0f, 0.5f, 0.5f);



            mat.SetTexture("_MainTex", previewSheet);
            mat.SetColor("_Color", Tint);
            mat.SetVector("_CropST", identityCrop);
            mat.SetVector("_Flip", identityFlip);



            var block = new MaterialPropertyBlock();
            block.SetTexture("_MainTex", previewSheet);
            block.SetColor("_Color", Tint);
            block.SetVector("_CropST", identityCrop);
            block.SetVector("_Flip", identityFlip);
            renderer.SetPropertyBlock(block);



            renderer.enabled = true;



            // 1x1 preview mesh is UV-baked; scale so PPU matches cell world size.
            // Mesh is bottom-center: localScale (sx,sy,1) â†’ width sx, height sy, feet at position.
            if (clipSheet != null &&
                SpriteSheetProfile.TryGetActiveCellPixels(clipSheet, cellIndex, out float cellW, out float cellH))
            {
                float ppu = SpriteSheetProfile.GetPixelsPerUnit(clipSheet);
                float sx = cellW / ppu;
                float sy = cellH / ppu;
                var scale = transform.localScale;
                if (!Mathf.Approximately(scale.x, sx) || !Mathf.Approximately(scale.y, sy))
                {
                    scale.x = sx;
                    scale.y = sy;
                    transform.localScale = scale;
                }
            }
        }



        /// <summary>
        /// Ensure MeshFilter has a DontSave "InvertLab Preview Quad". Recreate whenever
        /// sharedMesh is null, destroyed (Unity fake-null), or not our preview mesh.
        /// Never leaves sharedMesh null; never mutates the shared builtin Quad.
        /// </summary>
        Mesh EnsurePreviewMesh(MeshFilter filter)
        {
            var current = filter.sharedMesh;
            // Unity overloaded == is true for destroyed objects (fake null).
            if (current != null && current.name == PreviewMeshName && current.vertexCount == 4)
                return current;

            // Missing / destroyed / wrong mesh: build from builtin Quad (or hand-roll).
            Mesh source = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            Mesh clone;
            if (source != null)
            {
                clone = Object.Instantiate(source);
            }
            else
            {
                // Fallback if builtin resource unavailable in this editor context.
                clone = new Mesh();
                clone.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f),
                    new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f),
                };
                clone.uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                };
                clone.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                clone.RecalculateNormals();
                clone.RecalculateBounds();
            }

            clone.name = PreviewMeshName;
            clone.hideFlags = HideFlags.HideAndDontSave;

            // Capture corner UVs from Unity Quad positions BEFORE rebaking verts
            // (clone may already have been UV-baked if source was a previous preview mesh).
            var verts = clone.vertices;
            if (verts != null && verts.Length == 4)
            {
                var baseUvs = new Vector2[verts.Length];
                for (int i = 0; i < verts.Length; i++)
                {
                    // Unity builtin Quad: x,y in {-0.5,+0.5} → UV (0/1, 0/1).
                    baseUvs[i] = new Vector2(verts[i].x >= 0f ? 1f : 0f, verts[i].y >= 0f ? 1f : 0f);
                }
                clone.uv = baseUvs;
            }

            ApplyBottomCenterCellVertices(clone);
            filter.sharedMesh = clone;
            return clone;
        }



        /// <summary>
        /// Rebake preview quad so cell feet sit at local y=0 (bottom-center pivot).
        /// X stays -0.5..0.5; Y becomes 0..1. Safe for both center (±0.5) and
        /// already-bottom (0..1) source verts, and for pivot-mirrored verts from a
        /// previous flip bake (normalized by position span, not sign).
        /// </summary>
        static void ApplyBottomCenterCellVertices(Mesh mesh)
        {
            if (mesh == null)
                return;
            var verts = mesh.vertices;
            if (verts == null || verts.Length == 0)
                return;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                minX = Mathf.Min(minX, verts[i].x);
                maxX = Mathf.Max(maxX, verts[i].x);
                minY = Mathf.Min(minY, verts[i].y);
                maxY = Mathf.Max(maxY, verts[i].y);
            }
            float spanX = maxX - minX;
            float spanY = maxY - minY;
            if (spanX <= 1e-6f || spanY <= 1e-6f)
                return;

            bool changed = false;
            for (int i = 0; i < verts.Length; i++)
            {
                // Position-span normalization: a far-off pivot can mirror the quad
                // so both x values share a sign, so sign tests cannot find the sides.
                float u01 = (verts[i].x - minX) / spanX > 0.5f ? 1f : 0f;
                float v01 = (verts[i].y - minY) / spanY > 0.5f ? 1f : 0f;
                var next = new Vector3(
                    Mathf.Lerp(-0.5f, 0.5f, u01),
                    Mathf.Lerp(0f, 1f, v01),
                    0f);
                if ((next - verts[i]).sqrMagnitude > 1e-8f)
                    changed = true;
                verts[i] = next;
            }
            if (!changed)
                return;
            mesh.vertices = verts;
            mesh.RecalculateBounds();
        }



        /// <summary>
        /// Set mesh UVs to the cell rect. Bottom-center preview verts by position:
        /// BL(x&lt;0,y~0), BR(x&gt;0,y~0), TL(x&lt;0,y~1), TR(x&gt;0,y~1).
        /// Flip mirrors the VERTICES around the authored pivot axis (mesh x = u-0.5,
        /// mesh y = v); UVs stay attached to their vertices — a single mirror. Also
        /// mirroring UVs would cancel the flip, and UV-mirroring around a non-center
        /// pivot would sample outside the cell (neighboring-frame bleed).
        /// </summary>
        static void BakePreviewCellUVs(Mesh mesh, Vector4 cropST, bool flipX, bool flipY,
            Vector2 normalizedPivot)
        {
            var verts = mesh.vertices;
            if (verts == null || verts.Length == 0)
                return;

            float px = Mathf.Clamp01(normalizedPivot.x);
            float py = Mathf.Clamp01(normalizedPivot.y);
            if (normalizedPivot == default)
            {
                px = SpriteSheetProfile.DefaultPivot.x;
                py = SpriteSheetProfile.DefaultPivot.y;
            }

            var uvs = new Vector2[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                float u01 = verts[i].x >= 0f ? 1f : 0f;
                // Bottom-center mesh: y in {0,1} (also tolerates legacy ±0.5).
                float v01 = verts[i].y >= 0.5f ? 1f : 0f;
                uvs[i] = new Vector2(
                    cropST.z + u01 * cropST.x,
                    cropST.w + v01 * cropST.y);
                verts[i] = new Vector3(
                    flipX ? 2f * (px - 0.5f) - verts[i].x : verts[i].x,
                    flipY ? 2f * py - verts[i].y : verts[i].y,
                    0f);
            }
            mesh.uv = uvs;
            mesh.vertices = verts;
            mesh.RecalculateBounds();
        }



        Vector4 PreviewFlipVector()
        {
            var player = GetComponent<SpriteAnimPlayerAuthoring>();
            if (player == null)
                return Vector4.zero;
            return new Vector4(player.FlipX ? 1f : 0f, player.FlipY ? 1f : 0f, 0f, 0f);
        }



#if UNITY_EDITOR
        bool _colliderSyncScheduled;



        /// <summary>
        /// OnValidate / play-mode transitions must not create/destroy colliders/sockets
        /// inline (DestroyImmediate illegal during validation, physics, animation, render,
        /// and play-mode teardown). Always defer via delayCall in the editor; runtime
        /// play Tick still calls SyncUnityColliders / SyncUnitySockets directly (those
        /// paths use Object.Destroy while playing and reuse children).
        /// </summary>
        public void ScheduleUnityColliderSync()
        {
            if (_colliderSyncScheduled)
                return;
            _colliderSyncScheduled = true;
            EditorApplication.delayCall += FlushScheduledUnityColliderSync;
        }



        void FlushScheduledUnityColliderSync()
        {
            _colliderSyncScheduled = false;
            if (this == null)
                return;

            // Still entering/exiting play mode: wait until the transition completes
            // (isPlayingOrWillChangePlaymode stays true after isPlaying flips false).
            if (EditorApplication.isPlayingOrWillChangePlaymode && !Application.isPlaying)
            {
                _colliderSyncScheduled = true;
                EditorApplication.delayCall += FlushScheduledUnityColliderSync;
                return;
            }

            try
            {
                if (BakeUnityColliders)
                    SyncUnityColliders();
                else
                    SpriteColliderWorld.ClearUnityColliders(transform);
                if (BakeUnitySockets)
                    SyncUnitySockets();
                else
                {
                    SpriteSocketWorld.ClearUnitySockets(transform);
                    if (BakePivot)
                    {
                        var data = Profile?.Data;
                        ResolveUnitySyncClip(out string pivotClip, out _, out var pivotPlayer);
                        SpriteSocketWorld.SyncPivotMarker(
                            transform, data, pivotClip,
                            pivotPlayer != null && pivotPlayer.FlipX,
                            pivotPlayer != null && pivotPlayer.FlipY);
                    }
                    else
                        SpriteSocketWorld.ClearPivotMarker(transform);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"InvertLab: deferred collider/socket sync failed: {ex.Message}", this);
            }
        }
#endif



        public void SyncUnityColliders()
        {
            var data = Profile?.Data;
            if (!BakeUnityColliders || data?.Hitboxes == null)
            {
                SpriteColliderWorld.ClearUnityColliders(transform);
                return;
            }
            ResolveUnitySyncClip(out string clipName, out int frame, out var player);
            var displaySheet = SpriteSocketWorld.DisplaySheet(data, clipName);
            SpriteColliderWorld.SyncUnityColliders(
                transform, data.Hitboxes, clipName, frame, BakeFrameColliders,
                player != null && player.FlipX, player != null && player.FlipY,
                displaySheet, SpriteSocketWorld.ResolvePivot(data, displaySheet),
                ColliderLifetimeMask);
        }



        public void SyncUnitySockets()
        {
            var data = Profile?.Data;
            if (!BakeUnitySockets || data == null)
            {
                SpriteSocketWorld.ClearUnitySockets(transform);
                if (BakePivot)
                {
                    ResolveUnitySyncClip(out string pivotClip, out _, out var pivotPlayer);
                    SpriteSocketWorld.SyncPivotMarker(
                        transform, data, pivotClip,
                        pivotPlayer != null && pivotPlayer.FlipX,
                        pivotPlayer != null && pivotPlayer.FlipY);
                }
                else
                    SpriteSocketWorld.ClearPivotMarker(transform);
                return;
            }



            ResolveUnitySyncClip(out string clipName, out int frame, out var player);
            float independentTime = 0f;
            if (Application.isPlaying)
                independentTime = Time.time * Mathf.Max(0.01f, data.IndependentMotionSpeed);



            SpriteSocketWorld.SyncUnitySockets(
                transform, data, clipName, frame, independentTime,
                player != null && player.FlipX, player != null && player.FlipY, BakePivot);
        }



        void ResolveUnitySyncClip(out string clipName, out int frame, out SpriteAnimPlayerAuthoring player)
        {
            var data = Profile?.Data;
            int clipIndex = InitialClipIndex;
            frame = 0;
            player = GetComponent<SpriteAnimPlayerAuthoring>();
            if (player != null)
            {
                clipIndex = player.ClipIndex;
                frame = player.Frame;
            }
            clipName = "clip";
            if (Clips != null && clipIndex >= 0 && clipIndex < Clips.Length)
                clipName = string.IsNullOrEmpty(Clips[clipIndex].Name)
                    ? "clip" : Clips[clipIndex].Name;
            else if (data?.Clips != null && clipIndex >= 0 && clipIndex < data.Clips.Count)
                clipName = data.Clips[clipIndex].Name;
        }



        void OnDrawGizmos()
        {
            DrawSceneCellFrameGizmo();
            DrawScenePivotGizmo();
            DrawSceneColliderGizmos();
            DrawSceneSocketGizmos();
        }



        /// <summary>
        /// Cyan wire of the full cell bounds (padding included), matching the
        /// animator preview teal cell outline. Drawn in bottom-center local space.
        /// Follows the flip: with an off-center pivot the mirrored quad occupies a
        /// different area, so the frame mirrors its center around the pivot axis
        /// (same axis the render and colliders use) instead of staying static.
        /// </summary>
        void DrawSceneCellFrameGizmo()
        {
            if (!ShowSpriteInScene)
                return;
            var filter = GetComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            if (filter == null || renderer == null || !renderer.enabled)
                return;

            ResolveUnitySyncClip(out string clipName, out _, out var player);
            bool flipX = player != null && player.FlipX;
            bool flipY = player != null && player.FlipY;
            // Unit cell with bottom-center pivot: unflipped center at (0, 0.5).
            float centerX = 0f;
            float centerY = 0.5f;
            if (flipX || flipY)
            {
                var data = Profile?.Data;
                if (data != null)
                {
                    var sheet = SpriteSocketWorld.DisplaySheet(data, clipName);
                    Vector2 pivot = SpriteSocketWorld.ResolvePivot(data, sheet);
                    // Pivot normalized (0-1, bottom-left) -> unit-cell local x in
                    // -0.5..0.5, y in 0..1. Mirror the frame center around it.
                    if (flipX)
                        centerX = 2f * (Mathf.Clamp01(pivot.x) - 0.5f);
                    if (flipY)
                        centerY = 2f * Mathf.Clamp01(pivot.y) - 0.5f;
                }
            }

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.25f, 0.9f, 0.95f, 0.95f);
            Gizmos.DrawWireCube(new Vector3(centerX, centerY, 0f), new Vector3(1f, 1f, 0.02f));
            Gizmos.matrix = Matrix4x4.identity;
        }



        /// <summary>
        /// Crosshair + optional "Pivot" label at the authored profile.Pivot in mesh-local
        /// space (matches the editor green pivot). Falls back to the Pivot child when baked.
        /// </summary>
        void DrawScenePivotGizmo()
        {
            if (!ShowScenePivotGizmos)
                return;



            Vector3 origin = ResolveScenePivotWorldPosition();
            float sx = Mathf.Abs(transform.lossyScale.x);
            float sy = Mathf.Abs(transform.lossyScale.y);
            float arm = Mathf.Clamp(Mathf.Max(sx, sy) * 0.08f, 0.06f, 0.35f);



            Gizmos.color = new Color(0.25f, 0.95f, 0.4f, 0.98f);
            Gizmos.DrawLine(origin + Vector3.left * arm, origin + Vector3.right * arm);
            Gizmos.DrawLine(origin + Vector3.down * arm, origin + Vector3.up * arm);
            Gizmos.DrawWireSphere(origin, arm * 0.22f);
            Handles.color = Gizmos.color;
            Handles.Label(origin + Vector3.up * (arm * 1.35f), "Pivot");
        }



        Vector3 ResolveScenePivotWorldPosition()
        {
            var marker = transform.Find(SpriteSocketWorld.PivotName);
            if (marker != null)
                return marker.position;



            var data = Profile?.Data;
            if (data == null)
                return transform.position;



            ResolveUnitySyncClip(out string clipName, out _, out var player);
            var sheet = SpriteSocketWorld.DisplaySheet(data, clipName);
            Vector2 pivot = data.Pivot == default ? SpriteSheetProfile.DefaultPivot : data.Pivot;
            Vector2 meshLocal = SpriteSocketWorld.PixelsFromPivotToMeshLocal(sheet, pivot, Vector2.zero);
            meshLocal = SpriteSocketWorld.MirrorAroundPivot(
                meshLocal, sheet,
                SpriteSocketWorld.ResolvePivot(data, sheet),
                player != null && player.FlipX,
                player != null && player.FlipY);
            Vector3 hostScale = transform.localScale;
            float invSx = 1f / (Mathf.Abs(hostScale.x) > 1e-4f ? hostScale.x : 1f);
            float invSy = 1f / (Mathf.Abs(hostScale.y) > 1e-4f ? hostScale.y : 1f);
            return transform.TransformPoint(new Vector3(meshLocal.x * invSx, meshLocal.y * invSy, 0f));
        }



        void DrawSceneColliderGizmos()
        {
            if (!ShowSceneColliderGizmos)
                return;
            var data = Profile?.Data;
            if (data?.Hitboxes == null)
                return;
            int clipIndex = InitialClipIndex;
            int frame = 0;
            var player = GetComponent<SpriteAnimPlayerAuthoring>();
            if (player != null)
            {
                clipIndex = player.ClipIndex;
                frame = player.Frame;
            }
            string clipName = Clips != null && clipIndex >= 0 && clipIndex < Clips.Length
                ? Clips[clipIndex].Name
                : data.Clips != null && clipIndex >= 0 && clipIndex < data.Clips.Count
                    ? data.Clips[clipIndex].Name
                    : "clip";
            Gizmos.matrix = transform.localToWorldMatrix;
            Vector3 facingScale = player == null
                ? Vector3.one
                : new Vector3(player.FlipX ? -1f : 1f, player.FlipY ? -1f : 1f, 1f);
            // Mirror gizmos around the SAME pivot axis as the real Unity 2D
            // colliders (SpriteColliderWorld.SyncUnityColliders): root offset to
            // 2*axis in host-local units, then negative scale. Mirroring around
            // the mesh origin made boxes land elsewhere than the colliders
            // whenever the pivot is off-center.
            bool flipX = player != null && player.FlipX;
            bool flipY = player != null && player.FlipY;
            Matrix4x4 flipMatrix = transform.localToWorldMatrix;
            if (flipX || flipY)
            {
                var displaySheet = SpriteSocketWorld.DisplaySheet(data, clipName);
                Vector2 flipPivot = SpriteSocketWorld.ResolvePivot(data, displaySheet);
                Vector2 axis = SpriteSocketWorld.PixelsFromPivotToMeshLocal(
                    displaySheet, flipPivot, Vector2.zero);
                Vector3 hostScale = transform.localScale;
                float invSx = 1f / (Mathf.Abs(hostScale.x) > 1e-4f ? hostScale.x : 1f);
                float invSy = 1f / (Mathf.Abs(hostScale.y) > 1e-4f ? hostScale.y : 1f);
                Vector3 flipRoot = new Vector3(
                    flipX ? 2f * axis.x * invSx : 0f,
                    flipY ? 2f * axis.y * invSy : 0f,
                    0f);
                flipMatrix = transform.localToWorldMatrix *
                             Matrix4x4.TRS(flipRoot, Quaternion.identity, Vector3.one) *
                             Matrix4x4.Scale(facingScale);
            }
            else
            {
                flipMatrix = transform.localToWorldMatrix * Matrix4x4.Scale(facingScale);
            }
            foreach (var box in SpriteColliderWorld.VisibleOn(data.Hitboxes, clipName, frame))
            {
                if (box.Hidden || !box.UsesQuery)
                    continue;
                if (!SpriteColliderWorld.TryLocalFromUv(box, out var offset, out var size, out float angle))
                    continue;
                Gizmos.color = box.IsCharacter
                    ? new Color(0.25f, 0.9f, 0.8f, 0.9f)
                    : box.IsClip
                        ? new Color(0.95f, 0.72f, 0.22f, 0.9f)
                        : new Color(1f, 0.35f, 0.28f, 0.9f);
                var rotation = Quaternion.Euler(0f, 0f, angle);
                Gizmos.matrix = flipMatrix * Matrix4x4.TRS(offset, rotation, Vector3.one);
                if (box.Shape == SpriteColliderShape.Circle)
                    Gizmos.DrawWireSphere(Vector3.zero, Mathf.Max(size.x, size.y) * 0.5f);
                else if (box.Shape == SpriteColliderShape.Polygon)
                {
                    Vector2[] points = SpriteColliderWorld.PolygonLocalPoints(box, size);
                    for (int i = 0; i < points.Length; i++)
                    {
                        Vector2 from = points[i];
                        Vector2 to = points[(i + 1) % points.Length];
                        Gizmos.DrawLine(
                            new Vector3(from.x, from.y, 0f),
                            new Vector3(to.x, to.y, 0f));
                    }
                }
                else
                    Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0.02f));
            }
            Gizmos.matrix = Matrix4x4.identity;
        }



        static readonly List<SpriteSocketWorld.LocalPose> _socketGizmoScratch = new(16);



        void DrawSceneSocketGizmos()
        {
            if (!ShowSceneSocketGizmos)
                return;



            const float radius = 0.05f;
            var socketRoot = transform.Find(SpriteSocketWorld.RootName);
            if (socketRoot != null && socketRoot.childCount > 0)
            {
                Gizmos.matrix = Matrix4x4.identity;
                for (int i = 0; i < socketRoot.childCount; i++)
                {
                    var child = socketRoot.GetChild(i);
                    if (child == null)
                        continue;
                    DrawSocketDisc(child.position, child.name, radius);
                }
                return;
            }



            var data = Profile?.Data;
            if (data == null)
                return;



            ResolveUnitySyncClip(out string clipName, out int frame, out var player);
            float independentTime = 0f;
            if (Application.isPlaying)
                independentTime = Time.time * Mathf.Max(0.01f, data.IndependentMotionSpeed);



            SpriteSocketWorld.CollectLocalPoses(data, clipName, frame, independentTime, _socketGizmoScratch);
            if (_socketGizmoScratch.Count == 0)
                return;



            Vector3 hostScale = transform.localScale;
            float invSx = 1f / (Mathf.Abs(hostScale.x) > 1e-4f ? hostScale.x : 1f);
            float invSy = 1f / (Mathf.Abs(hostScale.y) > 1e-4f ? hostScale.y : 1f);
            bool flipX = player != null && player.FlipX;
            bool flipY = player != null && player.FlipY;
            var displaySheet = SpriteSocketWorld.DisplaySheet(data, clipName);



            Gizmos.matrix = Matrix4x4.identity;
            for (int i = 0; i < _socketGizmoScratch.Count; i++)
            {
                var pose = _socketGizmoScratch[i];
                Vector2 meshLocal = SpriteSocketWorld.MirrorAroundPivot(
                    pose.Position, displaySheet,
                    SpriteSocketWorld.ResolvePivot(data, displaySheet), flipX, flipY);
                var local = new Vector3(meshLocal.x * invSx, meshLocal.y * invSy, 0f);
                DrawSocketDisc(transform.TransformPoint(local), pose.Name, radius);
            }
        }



        static void DrawSocketDisc(Vector3 worldPos, string label, float radius)
        {
            Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.95f);
            Gizmos.DrawWireSphere(worldPos, radius);
            Gizmos.DrawSphere(worldPos, radius * 0.35f);
            Handles.color = Gizmos.color;
            Handles.Label(worldPos + Vector3.up * (radius * 1.6f),
                string.IsNullOrEmpty(label) ? "socket" : label);
        }
#endif



        static ClipAuthoring[] CopyClips(List<SpriteClipDef> clips)
        {
            var result = new ClipAuthoring[clips.Count];
            for (int i = 0; i < clips.Count; i++)
            {
                var src = clips[i];
                if (src == null)
                    continue;



                result[i] = new ClipAuthoring
                {
                    Name = src.Name,
                    SheetIndex = src.SheetIndex,
                    Row = src.Row,
                    Frames = CopyArray(src.Frames),
                    FrameRows = CopyArray(src.FrameRows),
                    FrameRate = src.FrameRate,
                    WrapMode = src.WrapMode,
                    Interrupt = src.Interrupt,
                    CancelAfter = src.CancelAfter,
                    Priority = src.Priority,
                    OnCompleteClipIndex = src.OnCompleteClipIndex,
                    ComboWindowStartFrame = src.ComboWindowStartFrame,
                    ComboWindowEndFrame = src.ComboWindowEndFrame,
                    ComboWindowPriorityBoost = src.ComboWindowPriorityBoost,
                    Loop = src.WrapMode == SpriteAnimWrap.Loop
                        || src.WrapMode == SpriteAnimWrap.ReverseLoop,
                    FrameDurationScales = CopyArray(src.FrameDurationScales),
                    EventIds = CopyArray(src.EventIds),
                    EventNormalizedTimes = CopyArray(src.EventNormalizedTimes),
                    FrameOffsets = CopyArray(src.OnionOffsets),
                    FrameScales = CopyArray(src.FrameScales),
                    FrameRotations = CopyArray(src.FrameRotations),
                    FrameTweenModes = CopyArray(src.FrameTweenModes),
                    FacingGroup = src.FacingGroup,
                    FacingDirection = src.Facing,
                    Sockets = CopySockets(src.Sockets),
                };
            }
            return result;
        }



        static T[] CopyArray<T>(T[] source)
        {
            if (source == null)
                return null;
            return (T[])source.Clone();
        }



        static FrameSocketDef[] CopySockets(List<FrameSocketDef> sockets)
        {
            if (sockets == null || sockets.Count == 0)
                return null;



            var result = new FrameSocketDef[sockets.Count];
            for (int i = 0; i < sockets.Count; i++)
            {
                var src = sockets[i];
                if (src == null)
                    continue;



                result[i] = new FrameSocketDef
                {
                    Name = src.Name,
                    FrameIndex = src.FrameIndex,
                    LocalPosition = src.LocalPosition,
                    LocalAngle = src.LocalAngle,
                    LocalScale = src.LocalScale,
                    DrawLayer = src.DrawLayer,
                };
            }
            return result;
        }



        static SpriteAnimSetBuilder.SocketInventoryInput[] BuildSocketInventoryInputs(
            SpriteSheetProfile profile)
        {
            profile?.EnsureSocketInventories();
            if (profile?.SocketInventories == null || profile.SocketInventories.Count == 0)
                return null;
            var result = new SpriteAnimSetBuilder.SocketInventoryInput[profile.SocketInventories.Count];
            for (int i = 0; i < profile.SocketInventories.Count; i++)
            {
                var inv = profile.SocketInventories[i];
                int memberCount = inv.SocketNames?.Count ?? 0;
                var ids = new string[memberCount];
                var names = new string[memberCount];
                var kinds = new byte[memberCount];
                for (int m = 0; m < memberCount; m++)
                {
                    string socketName = inv.SocketNames[m];
                    var item = profile.SocketCatalog?.Find(socketName);
                    names[m] = socketName;
                    ids[m] = SpriteSocketIdUtility.Canonical(item != null ? item.SocketId : null, socketName);
                    bool independent = item != null && item.UsesOwnClock
                        || profile.FindSocketMotion(socketName) != null;
                    kinds[m] = independent
                        ? (byte)SpriteSocketInventoryKind.Independent
                        : (byte)SpriteSocketInventoryKind.Frame;
                }
                result[i] = new SpriteAnimSetBuilder.SocketInventoryInput
                {
                    Name = string.IsNullOrWhiteSpace(inv.Name) ? "Inventory" : inv.Name.Trim(),
                    SocketIds = ids,
                    SocketNames = names,
                    Kinds = kinds,
                };
            }
            return result;
        }



        class Baker : Baker<SpriteAnimSetAuthoring>
        {
            public override void Bake(SpriteAnimSetAuthoring authoring)
            {
                var profile = authoring.Profile != null ? authoring.Profile.Data : null;
                if (profile != null)
                {
                    profile.EnsureSheets();
                    profile.EnsureSocketCatalog();
                    profile.EnsureSocketMotions();
                }



                SpriteSheetDef bakeSheetDef = null;
                bool useProfile = profile?.Clips != null && profile.Clips.Count > 0;
                if (useProfile)
                    bakeSheetDef = profile.SheetForClip(profile.Clips[0]);
                var sheet = useProfile
                    ? (bakeSheetDef?.Texture ?? profile.Sheet)
                    : authoring.Sheet;
                int clipCount = useProfile ? profile.Clips.Count : authoring.Clips?.Length ?? 0;
                if (sheet == null || clipCount == 0)
                    return;



                if (authoring.Profile != null)
                    DependsOn(authoring.Profile);
                DependsOn(sheet);



                var entity = GetEntity(authoring, TransformUsageFlags.Renderable);

                // Scene Quad MeshRenderer is editor preview only (often a DontSave
                // mesh/material). Sprite drawing uses SpriteInstanceRenderSystem /
                // GPU paths — not Entities Graphics MeshRenderer conversion.
                // DisableRendering stops BRG from drawing the preview/null mesh;
                // PostBaking strip removes MaterialMeshInfo so corrupt mesh refs
                // cannot NRE AsyncLoadSceneOperation.ScheduleSceneRead.
                AddComponent(entity, new DisableRendering());

                // data-only bake: the GPU-instanced renderer consumes these directly
                // (no GameObjects graphics components involved)



                // ---- clip blob ----
                var inputs = new SpriteAnimSetBuilder.ClipInput[clipCount];
                for (int i = 0; i < clipCount; i++)
                {
                    var profileClip = useProfile ? profile.Clips[i] : null;
                    var authorClip = useProfile ? default : authoring.Clips[i];
                    if (useProfile)
                        profileClip.EnsureFrameData();
                    var clipSheet = useProfile ? profile.SheetForClip(profileClip) : null;
                    int cols = Mathf.Max(1, clipSheet != null ? clipSheet.Columns : authoring.Columns);
                    int rows = Mathf.Max(1, clipSheet != null ? clipSheet.Rows : authoring.Rows);
                    float bakePpu = clipSheet != null
                        ? SpriteSheetProfile.GetPixelsPerUnit(clipSheet)
                        : 1f;
                    if (clipSheet?.Texture != null)
                        DependsOn(clipSheet.Texture);
                    var frameCols = useProfile ? profileClip.Frames : authorClip.Frames;
                    frameCols = frameCols != null && frameCols.Length > 0
                        ? frameCols
                        : new[] { 0, 1, 2, 3 };
                    var frameScales = useProfile ? profileClip.FrameScales : authorClip.FrameScales;
                    var frameRotations = useProfile ? profileClip.FrameRotations : authorClip.FrameRotations;
                    var frameTweens = useProfile ? profileClip.FrameTweenModes : authorClip.FrameTweenModes;
                    int row = useProfile ? profileClip.Row : authorClip.Row;
                    int[] frameRows = useProfile ? profileClip.FrameRows : authorClip.FrameRows;
                    var slots = new int[frameCols.Length];
                    var frameOffsets = new float2[frameCols.Length];
                    var clipScales = new float2[frameCols.Length];
                    var clipRotations = new float[frameCols.Length];
                    var clipTweens = new byte[frameCols.Length];
                    for (int f = 0; f < frameCols.Length; f++)
                    {
                        SpriteClipDef.ResolveSheetCell(row, frameCols, frameRows, f,
                            cols, rows, out int cellRow, out int cellCol);
                        slots[f] = cellRow * cols + cellCol;
                        Vector2 offset = useProfile && profileClip.OnionOffsets != null && f < profileClip.OnionOffsets.Length
                            ? profileClip.OnionOffsets[f] / bakePpu
                            : !useProfile && authorClip.FrameOffsets != null && f < authorClip.FrameOffsets.Length
                                ? authorClip.FrameOffsets[f]
                                : Vector2.zero;
                        frameOffsets[f] = new float2(offset.x, offset.y);
                        // per-cell pivot override: shift the frame so the
                        // cell pivot sits on the entity origin
                        if (clipSheet != null &&
                            SpriteSheetProfile.TryGetCellPivot(clipSheet, slots[f],
                                out var cellPivot))
                        {
                            var pivotTexture = clipSheet.Texture;
                            float cellWpx = pivotTexture != null
                                ? pivotTexture.width / (float)cols : 0f;
                            float cellHpx = pivotTexture != null
                                ? pivotTexture.height / (float)rows : 0f;
                            frameOffsets[f] += new float2(
                                (0.5f - Mathf.Clamp01(cellPivot.x)) * cellWpx /
                                Mathf.Max(0.01f, bakePpu),
                                (0.5f - Mathf.Clamp01(cellPivot.y)) * cellHpx /
                                Mathf.Max(0.01f, bakePpu));
                        }
                        Vector2 scale = frameScales != null && f < frameScales.Length
                            ? frameScales[f]
                            : Vector2.one;
                        clipScales[f] = new float2(scale.x, scale.y);
                        clipRotations[f] = frameRotations != null && f < frameRotations.Length
                            ? frameRotations[f]
                            : 0f;
                        clipTweens[f] = frameTweens != null && f < frameTweens.Length
                            ? frameTweens[f]
                            : (byte)SpriteEaseMode.Linear;
                    }



                    int socketCount = useProfile
                        ? profileClip.Sockets?.Count ?? 0
                        : authorClip.Sockets?.Length ?? 0;
                    var socketInputs = new SpriteAnimSetBuilder.ClipInput.FrameSocketInput[socketCount];
                    for (int s = 0; s < socketInputs.Length; s++)
                    {
                        var socket = useProfile ? profileClip.Sockets[s] : authorClip.Sockets[s];
                        float2 position = useProfile
                            ? new float2(
                                socket.LocalPosition.x / bakePpu,
                                socket.LocalPosition.y / bakePpu)
                            : new float2(socket.LocalPosition.x, socket.LocalPosition.y);
                        string socketId = useProfile
                            ? profile.SocketCatalog.Find(socket.Name)?.SocketId
                            : socket.Name;
                        socketInputs[s] = new SpriteAnimSetBuilder.ClipInput.FrameSocketInput
                        {
                            FrameIndex = socket.FrameIndex,
                            LocalPosition = position,
                            LocalAngle = socket.LocalAngle,
                            LocalScale = new float2(
                                SpriteSocketKeys.ResolvedScale(socket.LocalScale).x,
                                SpriteSocketKeys.ResolvedScale(socket.LocalScale).y),
                            Name = socket.Name,
                            SocketId = socketId,
                        };
                    }



                    inputs[i] = new SpriteAnimSetBuilder.ClipInput
                    {
                        Name = useProfile
                            ? (string.IsNullOrEmpty(profileClip.Name) ? ("clip" + i) : profileClip.Name)
                            : (string.IsNullOrEmpty(authorClip.Name) ? ("clip" + i) : authorClip.Name),
                        Loop = useProfile
                            ? profileClip.WrapMode == SpriteAnimWrap.Loop || profileClip.WrapMode == SpriteAnimWrap.ReverseLoop
                            : authorClip.Loop || authorClip.WrapMode == SpriteAnimWrap.ReverseLoop,
                        WrapMode = useProfile ? profileClip.WrapMode : authorClip.WrapMode,
                        Interrupt = useProfile ? profileClip.Interrupt : authorClip.Interrupt,
                        CancelAfter = useProfile ? profileClip.CancelAfter : authorClip.CancelAfter,
                        Priority = useProfile ? profileClip.Priority : authorClip.Priority,
                        OnCompleteClipIndex = useProfile
                            ? profileClip.OnCompleteClipIndex
                            : authorClip.OnCompleteClipIndex,
                        ComboWindowStartFrame = useProfile
                            ? profileClip.ComboWindowStartFrame
                            : authorClip.ComboWindowStartFrame,
                        ComboWindowEndFrame = useProfile
                            ? profileClip.ComboWindowEndFrame
                            : authorClip.ComboWindowEndFrame,
                        ComboWindowPriorityBoost = useProfile
                            ? profileClip.ComboWindowPriorityBoost
                            : authorClip.ComboWindowPriorityBoost,
                        FrameRate = Mathf.Max(0.1f, useProfile ? profileClip.FrameRate : authorClip.FrameRate),
                        GlobalFrameIndices = slots,
                        FrameDurationScales = useProfile ? profileClip.FrameDurationScales : authorClip.FrameDurationScales,
                        EventIds = useProfile ? profileClip.EventIds : authorClip.EventIds,
                        EventNormalizedTimes = useProfile
                            ? profileClip.EventNormalizedTimes
                            : authorClip.EventNormalizedTimes,
                        EventKeys = useProfile ? EventKeysFromProfile(profileClip) : null,
                        FrameOffsets = frameOffsets,
                        FrameScales = clipScales,
                        FrameRotations = clipRotations,
                        FrameTweenModes = clipTweens,
                        FacingGroup = useProfile ? profileClip.FacingGroup : authorClip.FacingGroup,
                        FacingDirection = useProfile ? profileClip.Facing : authorClip.FacingDirection,
                        FrameSockets = socketInputs,
                    };
                }
                var activeMotions = new List<SpriteSocketMotionTrack>();
                if (useProfile && profile.SocketMotions != null)
                {
                    for (int i = 0; i < profile.SocketMotions.Count; i++)
                    {
                        var candidate = profile.SocketMotions[i];
                        var candidateItem = candidate != null
                            ? profile.SocketCatalog.Find(candidate.SocketName)
                            : null;
                        if (candidate != null && candidate.Keys != null &&
                            candidate.Keys.Count > 0 && candidateItem != null &&
                            candidateItem.UsesOwnClock)
                            activeMotions.Add(candidate);
                    }
                }
                int motionCount = activeMotions.Count;
                var motionInputs = new SpriteAnimSetBuilder.SocketMotionInput[motionCount];
                for (int i = 0; i < motionCount; i++)
                {
                    var motion = activeMotions[i];
                    var motionSheet = profile.SheetAt(motion.ReferenceSheetIndex);
                    float motionPpu = SpriteSheetProfile.GetPixelsPerUnit(motionSheet);
                    var catalogItem = profile.SocketCatalog.Find(motion.SocketName);
                    int keyCount = motion.Keys?.Count ?? 0;
                    var keys =
                        new SpriteAnimSetBuilder.SocketMotionInput.SocketMotionPointInput[keyCount];
                    for (int k = 0; k < keyCount; k++)
                    {
                        var key = motion.Keys[k];
                        Vector2 resolvedScale = SpriteSocketKeys.ResolvedScale(key.LocalScale);
                        keys[k] =
                            new SpriteAnimSetBuilder.SocketMotionInput.SocketMotionPointInput
                            {
                                NormalizedTime = key.NormalizedTime,
                                LocalPosition = new float2(
                                    key.LocalPosition.x / motionPpu,
                                    key.LocalPosition.y / motionPpu),
                                LocalAngle = key.LocalAngle,
                                LocalScale = new float2(resolvedScale.x, resolvedScale.y),
                                EaseMode = key.EaseMode,
                                PathMode = key.PathMode,
                                UseCustomEase = key.UseCustomEase ? (byte)1 : (byte)0,
                                CustomEaseSamplesA = new float4(
                                    key.CustomEaseSamplesA.x, key.CustomEaseSamplesA.y,
                                    key.CustomEaseSamplesA.z, key.CustomEaseSamplesA.w),
                                CustomEaseSamplesB = new float4(
                                    key.CustomEaseSamplesB.x, key.CustomEaseSamplesB.y,
                                    key.CustomEaseSamplesB.z, key.CustomEaseSamplesB.w),
                                AllowOvershoot = key.AllowOvershoot ? (byte)1 : (byte)0,
                                InTangent = new float2(
                                    key.InTangent.x / motionPpu,
                                    key.InTangent.y / motionPpu),
                                OutTangent = new float2(
                                    key.OutTangent.x / motionPpu,
                                    key.OutTangent.y / motionPpu),
                                ArcBulge = key.ArcBulge / motionPpu,
                                ArcClockwise = key.ArcClockwise ? (byte)1 : (byte)0,
                                RotationMode = key.RotationMode,
                                RotationTurns = key.RotationTurns,
                                FacingAngleOffset = key.FacingAngleOffset,
                            };
                    }
                    int triggerCount = motion.Triggers?.Count ?? 0;
                    var triggers =
                        new SpriteAnimSetBuilder.SocketMotionInput.SocketTriggerInput[triggerCount];
                    for (int t = 0; t < triggerCount; t++)
                    {
                        triggers[t] = new SpriteAnimSetBuilder.SocketMotionInput.SocketTriggerInput
                        {
                            NormalizedTime = motion.Triggers[t].NormalizedTime,
                            EventId = motion.Triggers[t].EventId,
                        };
                    }
                    motionInputs[i] = new SpriteAnimSetBuilder.SocketMotionInput
                    {
                        Name = motion.SocketName,
                        SocketId = catalogItem?.SocketId,
                        Duration = profile.IndependentMotionDuration,
                        Speed = 1f,
                        Loop = profile.IndependentMotionLoop,
                        AnchorSpace = motion.AnchorSpace,
                        Keys = keys,
                        Triggers = triggers,
                    };
                }



                var inventoryInputs = BuildSocketInventoryInputs(profile);
                var (setRef, player) = SpriteAnimSetBuilder.Build(
                    Allocator.Persistent, inputs, motionInputs, inventoryInputs);
                AddComponent(entity, setRef);
                var playerAuthoring = GetComponent<SpriteAnimPlayerAuthoring>();
                int initialClip;
                byte flipX = 0;
                byte flipY = 0;
                if (playerAuthoring != null)
                {
                    DependsOn(playerAuthoring);
                    initialClip = Mathf.Clamp(playerAuthoring.ClipIndex, 0, clipCount - 1);
                    player.ClipIndex = initialClip;
                    player.Speed = playerAuthoring.Speed; // allow 0 (freeze) and negative (rewind)
                    player.Playing = playerAuthoring.Playing ? (byte)1 : (byte)0;
                    player.QueuedClipIndex = playerAuthoring.QueuedClipIndex;
                    player.QueuedForce = playerAuthoring.QueuedForce;
                    player.ResumeClipIndex = playerAuthoring.ResumeClipIndex;
                    player.OneShotActive = playerAuthoring.OneShotActive;
                    player.CrossfadeDuration = Mathf.Max(0f, playerAuthoring.CrossfadeDuration);
                    player.BlendOutTime = 0f;
                    player.BlendDuration = 0f;
                    player.Blend = 0f;
                    flipX = playerAuthoring.FlipX ? (byte)1 : (byte)0;
                    flipY = playerAuthoring.FlipY ? (byte)1 : (byte)0;
                }
                else
                {
                    initialClip = Mathf.Clamp(authoring.InitialClipIndex, 0, clipCount - 1);
                    player.ClipIndex = initialClip;
                }
                AddComponent(entity, player);
                ref var initialDef = ref setRef.Set.Value.Clips[initialClip];
                int initialFrame = initialDef.WrapMode == SpriteAnimWrap.ReverseLoop
                    || initialDef.WrapMode == SpriteAnimWrap.ReverseOnce
                    ? Mathf.Max(0, initialDef.FrameCount - 1)
                    : 0;
                float4 firstFrame = setRef.Set.Value.Frames[initialDef.FirstFrame + initialFrame];
                AddComponent(entity, new SpriteAnimFrame
                {
                    Slot = (int)firstFrame.x,
                    Offset = firstFrame.yz,
                    Scale = initialDef.FrameScales.Length > initialFrame
                        ? initialDef.FrameScales[initialFrame]
                        : new float2(1f, 1f),
                    Rotation = initialDef.FrameRotations.Length > initialFrame
                        ? initialDef.FrameRotations[initialFrame]
                        : 0f,
                });
                AddComponent(entity, new SpriteTint { Value = new float4(
                    authoring.Tint.r, authoring.Tint.g, authoring.Tint.b, authoring.Tint.a) });
                AddComponent(entity, new SpriteAnimEnabled());
                float2 bakePivot = new float2(0.5f, 0.5f);
                if (profile != null)
                {
                    string pivotClipName = null;
                    if (playerAuthoring != null && authoring.Clips != null &&
                        playerAuthoring.ClipIndex >= 0 &&
                        playerAuthoring.ClipIndex < authoring.Clips.Length)
                        pivotClipName = authoring.Clips[playerAuthoring.ClipIndex].Name;
                    var pivotSheet = SpriteSocketWorld.DisplaySheet(profile, pivotClipName);
                    var resolved = SpriteSocketWorld.ResolvePivot(profile, pivotSheet);
                    bakePivot = new float2(resolved.x, resolved.y);
                }
                AddComponent(entity, new SpriteFlip { X = flipX, Y = flipY, Pivot = bakePivot });

                // ---- per-sheet batching: one sheet-definition entity per
                // distinct clip sheet, so multi-atlas profiles render each
                // clip from its own texture. The sprite binds to its initial
                // clip's sheet; SpriteClipSheetSystem swaps the binding when
                // the clip changes. ----
                {
                    var sheetEntityByIndex = new Dictionary<int, Entity>();
                    var clipSheetEntities = new Entity[clipCount];
                    for (int i = 0; i < clipCount; i++)
                    {
                        int sheetIdx = 0;
                        SpriteSheetDef clipDef = null;
                        if (useProfile)
                        {
                            var profileClip = profile.Clips[i];
                            profileClip.EnsureFrameData();
                            sheetIdx = Mathf.Clamp(profileClip.SheetIndex, 0,
                                Mathf.Max(0, profile.Sheets.Count - 1));
                            clipDef = profile.SheetAt(sheetIdx);
                        }

                        if (!sheetEntityByIndex.TryGetValue(sheetIdx, out var sheetEntity))
                        {
                            Texture2D sTex = clipDef != null && clipDef.Texture != null
                                ? clipDef.Texture : sheet;
                            int sCols = Mathf.Max(1, useProfile && clipDef != null
                                ? clipDef.Columns : authoring.Columns);
                            int sRows = Mathf.Max(1, useProfile && clipDef != null
                                ? clipDef.Rows : authoring.Rows);
                            float sAspect = SpriteSheetProfile.GetCellAspect(sTex, sCols, sRows);
                            float4[] sCrops = null;
                            byte sUseCrops = 0;
                            if (useProfile && clipDef != null &&
                                clipDef.CellLayoutMode == SpriteSheetCellLayoutMode.Cropped &&
                                SpriteSheetProfile.HasCroppedCellData(clipDef))
                            {
                                var cropVecs = SpriteSheetProfile.BuildCellCropSTArray(clipDef);
                                if (cropVecs != null && cropVecs.Length == sCols * sRows)
                                {
                                    sCrops = new float4[cropVecs.Length];
                                    for (int c = 0; c < cropVecs.Length; c++)
                                        sCrops[c] = new float4(cropVecs[c].x, cropVecs[c].y,
                                                               cropVecs[c].z, cropVecs[c].w);
                                    sUseCrops = 1;
                                }
                            }

                            sheetEntity = CreateAdditionalEntity(TransformUsageFlags.None);
                            AddComponent(sheetEntity, new SpriteSheetDefinition
                            {
                                Cols = sCols,
                                Rows = sRows,
                                CellAspect = sAspect > 0.01f ? sAspect : 1f,
                                UseCellCrops = sUseCrops,
                            });
                            AddComponentObject(sheetEntity, new SpriteSheetAsset { Texture = sTex });
                            DependsOn(sTex);
                            if (sUseCrops != 0)
                            {
                                var cropBuffer = AddBuffer<SpriteAnimCellCrop>(sheetEntity);
                                foreach (var crop in sCrops)
                                    cropBuffer.Add(new SpriteAnimCellCrop { Value = crop });
                            }
                            sheetEntityByIndex[sheetIdx] = sheetEntity;
                        }

                        clipSheetEntities[i] = sheetEntity;
                    }

                    var initialSheet = clipSheetEntities[
                        Mathf.Clamp(initialClip, 0, clipCount - 1)];
                    AddComponent(entity, new SpriteSheetBinding { Sheet = initialSheet });
                    var clipSheets = AddBuffer<SpriteClipSheetBindingEntry>(entity);
                    foreach (var clipSheet in clipSheetEntities)
                        clipSheets.Add(new SpriteClipSheetBindingEntry { Sheet = clipSheet });
                }

                AddBuffer<SpriteAnimEventBuffer>(entity);
                AddComponent(entity, new SpriteAnimEventsPending());
                AddBuffer<SpriteSocketBuffer>(entity);
                if (inventoryInputs != null && inventoryInputs.Length > 0)
                {
                    AddComponent<SpriteSocketInventoryTag>(entity);
                    var inventoryBuffer = AddBuffer<SpriteSocketInventoryMember>(entity);
                    for (int i = 0; i < inventoryInputs.Length; i++)
                    {
                        var inv = inventoryInputs[i];
                        string invName = string.IsNullOrWhiteSpace(inv.Name) ? "inventory" : inv.Name.Trim();
                        uint groupHash = SpriteSockets.InventoryHash(invName);
                        int memberCount = inv.SocketIds?.Length ?? 0;
                        for (int m = 0; m < memberCount; m++)
                        {
                            string socketId = inv.SocketIds[m];
                            string socketName = inv.SocketNames != null && m < inv.SocketNames.Length
                                ? inv.SocketNames[m] : socketId;
                            inventoryBuffer.Add(new SpriteSocketInventoryMember
                            {
                                GroupHash = groupHash,
                                GroupName = invName,
                                SocketIdHash = SpriteSockets.Hash(socketId),
                                SocketId = socketId,
                                SocketName = socketName,
                                Kind = inv.Kinds != null && m < inv.Kinds.Length
                                    ? inv.Kinds[m]
                                    : (byte)SpriteSocketInventoryKind.Frame,
                            });
                        }
                    }
                }
                if (motionCount > 0)
                {
                    AddComponent(entity, new SpriteSocketMotionPlayer
                    {
                        Time = 0f,
                        Speed = profile.IndependentMotionSpeed,
                        Playing = 1,
                    });
                    AddBuffer<SpriteSocketEventBuffer>(entity);
                    AddComponent(entity, new SpriteSocketEventsPending());
                }
                if (useProfile && profile.Hitboxes != null && profile.Hitboxes.Count > 0)
                {
                    var hitboxBlob = SpriteHitboxSetBuilder.FromProfile(profile, Allocator.Persistent);
                    AddComponent(entity, new SpriteHitboxSetRef { Set = hitboxBlob });
                    AddBuffer<SpriteHitboxLive>(entity);
                }
            }



            static SpriteAnimSetBuilder.ClipInput.EventKeyInput[] EventKeysFromProfile(SpriteClipDef clip)
            {
                if (clip == null)
                    return null;
                clip.EnsureEventMarkers();
                if (clip.EventMarkers == null || clip.EventMarkers.Count == 0)
                    return null;
                int count = 0;
                for (int i = 0; i < clip.EventMarkers.Count; i++)
                {
                    if (clip.EventMarkers[i] != null && clip.EventMarkers[i].EventId != 0)
                        count++;
                }
                if (count == 0)
                    return null;
                var keys = new SpriteAnimSetBuilder.ClipInput.EventKeyInput[count];
                int write = 0;
                for (int i = 0; i < clip.EventMarkers.Count; i++)
                {
                    var marker = clip.EventMarkers[i];
                    if (marker == null || marker.EventId == 0)
                        continue;
                    marker.EnsurePayloads();
                    keys[write++] = new SpriteAnimSetBuilder.ClipInput.EventKeyInput
                    {
                        FrameIndex = marker.FrameIndex,
                        NormalizedTime = marker.NormalizedTime,
                        EventId = marker.EventId,
                        FireMode = marker.FireMode,
                        IntPayload = marker.IntPayload,
                        FloatPayload = marker.FloatPayload,
                        TextPayload = marker.TextPayload,
                        Payloads = PayloadsFromMarker(marker),
                    };
                }
                return keys;
            }



            static SpriteAnimSetBuilder.ClipInput.EventPayloadInput[] PayloadsFromMarker(
                SpriteClipEventMarker marker)
            {
                if (marker?.Payloads == null || marker.Payloads.Count == 0)
                    return null;
                int count = math.min(marker.Payloads.Count, SpriteEventPayloads.Max);
                var payloads = new SpriteAnimSetBuilder.ClipInput.EventPayloadInput[count];
                int write = 0;
                for (int i = 0; i < marker.Payloads.Count && write < count; i++)
                {
                    var entry = marker.Payloads[i];
                    if (entry == null)
                        continue;
                    payloads[write++] = new SpriteAnimSetBuilder.ClipInput.EventPayloadInput
                    {
                        Name = entry.Name,
                        Kind = entry.Kind,
                        IntValue = entry.IntValue,
                        IntY = entry.IntY,
                        IntZ = entry.IntZ,
                        IntW = entry.IntW,
                        FloatValue = entry.FloatValue,
                        FloatY = entry.FloatY,
                        FloatZ = entry.FloatZ,
                        FloatW = entry.FloatW,
                        TextValue = entry.Kind == (byte)SpriteEventPayloadKind.Asset &&
                            !string.IsNullOrEmpty(entry.AssetGuid)
                            ? entry.AssetGuid
                            : entry.TextValue,
                    };
                }
                if (write == count)
                    return payloads;
                if (write == 0)
                    return null;
                var trimmed = new SpriteAnimSetBuilder.ClipInput.EventPayloadInput[write];
                for (int i = 0; i < write; i++)
                    trimmed[i] = payloads[i];
                return trimmed;
            }
        }
    }
}



