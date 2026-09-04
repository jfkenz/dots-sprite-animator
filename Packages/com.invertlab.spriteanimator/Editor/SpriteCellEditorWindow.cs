using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>
    /// Unity Sprite Editor-style cell editor for one profile sheet: full-sheet
    /// canvas (checkerboard, grid lines, crop-rect outlines), click-select a
    /// cell, drag its pivot handle, and a Sprite inspector (Name, Position,
    /// Pivot preset/unit/custom). Per-cell pivots and rect edits write into
    /// the profile asset (undo-able) — not Unity .meta data.
    /// </summary>
    public sealed class SpriteCellEditorWindow : EditorWindow, ISpriteSheetSliceHost
    {
        enum PivotPreset
        {
            Center, Top, Bottom, Left, Right,
            TopLeft, TopRight, BottomLeft, BottomRight, Custom,
        }

        static readonly Color Accent = new(0.25f, 0.75f, 1f, 1f);
        static readonly Color GridLine = new(1f, 1f, 1f, 0.45f);
        static readonly Color PivotDot = new(0.2f, 0.85f, 0.35f, 1f);
        static readonly Color CheckerA = new(0.28f, 0.28f, 0.28f, 1f);
        static readonly Color CheckerB = new(0.42f, 0.42f, 0.42f, 1f);

        SpriteSheetToolWindow _host;
        Vector2 _scroll;
        float _zoom = 1f;
        int _selectedCell = -1;
        bool _dragPivot;

        PivotPreset _preset = PivotPreset.Center;
        PivotUnitModeProxy _unit = PivotUnitModeProxy.Normalized;
        bool _globalPivot;

        enum PivotUnitModeProxy { Normalized, Pixels }

        public static void Show(SpriteSheetToolWindow host)
        {
            var window = CreateInstance<SpriteCellEditorWindow>();
            window._host = host;
            window.titleContent = new GUIContent("Cell Editor");
            window.minSize = new Vector2(860f, 520f);
            window.Show();
        }

        SpriteSheetProfile Data => _host != null && _host.SliceProfileAsset != null
            ? _host.SliceProfileAsset.Data
            : null;

        SpriteSheetDef Sheet
        {
            get
            {
                var data = Data;
                if (data == null || data.Sheets == null || data.Sheets.Count == 0)
                    return null;
                return data.SheetAt(Mathf.Clamp(_host.SliceActiveSheetIndex, 0, data.Sheets.Count - 1));
            }
        }

        void OnGUI()
        {
            var data = Data;
            var sheet = Sheet;
            var texture = sheet?.Texture ?? data?.Sheet;
            if (sheet == null || texture == null)
            {
                EditorGUILayout.HelpBox("Open a profile with a sheet in the Sprite Animator " +
                    "window, then reopen the Cell Editor.", MessageType.Warning);
                return;
            }

            int cols = Mathf.Max(1, sheet.Columns);
            int rows = Mathf.Max(1, sheet.Rows);

            // grid is the only layout for now: force it so editor, bake, and
            // slice all agree (stale Cropped flags on the profile are reset)
            if (sheet.CellLayoutMode != SpriteSheetCellLayoutMode.Grid)
            {
                Undo.RecordObject(_host.SliceProfileAsset, "Force Grid Layout");
                sheet.CellLayoutMode = SpriteSheetCellLayoutMode.Grid;
                EditorUtility.SetDirty(_host.SliceProfileAsset);
                _host.SliceNotifyProfileEdited();
            }

            // ---- toolbar ----
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (EditorGUILayout.DropdownButton(
                        new GUIContent("Slice ▾",
                            "Unity Sprite Editor-style slicing: Automatic, Grid By Cell Size, " +
                            "Grid By Cell Count. Writes into this sheet."),
                        FocusType.Passive, EditorStyles.toolbarButton, GUILayout.Width(64f)))
                {
                    PopupWindow.Show(GUILayoutUtility.GetLastRect(),
                        new SpriteSheetSlicePopup(this));
                }
                GUILayout.Space(6f);
                if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(26f)))
                    _host.SliceStepSheet(-1);
                GUILayout.Label($"sheet {_host.SliceActiveSheetIndex + 1}/{data.Sheets.Count}",
                    EditorStyles.miniLabel, GUILayout.Width(70f));
                if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(26f)))
                    _host.SliceStepSheet(1);

                GUILayout.Label(texture.name, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label("Zoom", EditorStyles.miniLabel);
                _zoom = GUILayout.HorizontalSlider(_zoom, 0.1f, 8f, GUILayout.Width(140f));
                GUILayout.Space(8f);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawCanvas(sheet, texture, cols, rows);
            }

            // Sprite panel pinned bottom-right (Unity Sprite Editor placement)
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                DrawInspector(sheet, texture, cols, rows);
            }
        }

        // ---- canvas ----

        void DrawCanvas(SpriteSheetDef sheet, Texture2D texture, int cols, int rows)
        {
            float drawW = texture.width * _zoom;
            float drawH = texture.height * _zoom;

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            var canvas = GUILayoutUtility.GetRect(drawW, drawH, GUILayout.ExpandWidth(false),
                GUILayout.ExpandHeight(false));
            var evt = Event.current;

            // checkerboard + texture
            DrawCheckerboard(canvas, 12f);
            GUI.DrawTexture(canvas, texture, ScaleMode.StretchToFill, true);

            float cw = canvas.width / cols;
            float ch = canvas.height / rows;

            // grid lines (2px, clearly visible at any zoom)
            for (int c = 0; c <= cols; c++)
                EditorGUI.DrawRect(new Rect(canvas.x + c * cw, canvas.y, 2f, canvas.height), GridLine);
            for (int r = 0; r <= rows; r++)
                EditorGUI.DrawRect(new Rect(canvas.x, canvas.y + r * ch, canvas.width, 2f), GridLine);

            // selection
            if (_selectedCell >= 0 && _selectedCell < cols * rows)
            {
                int sc = _selectedCell % cols;
                int sr = _selectedCell / cols;
                var world = CellToCanvas(canvas, cols, rows, sc, sr);
                DrawRectOutline(world, Accent, 2f);

                // pivot handle: effective pivot (override ?? sheet pivot)
                Vector2 pivot = ResolveEffectivePivot(sheet, _selectedCell);
                var handle = new Vector2(
                    world.x + pivot.x * world.width,
                    world.y + (1f - pivot.y) * world.height);
                var handle3 = new Vector3(handle.x, handle.y, 0f);
                Handles.BeginGUI();
                Handles.color = PivotDot;
                Handles.DrawSolidDisc(handle3, Vector3.forward, 6f);
                Handles.color = Color.white;
                Handles.DrawWireDisc(handle3, Vector3.forward, 6f);
                Handles.DrawLine(handle3 + new Vector3(-10f, 0f, 0f), handle3 + new Vector3(10f, 0f, 0f));
                Handles.DrawLine(handle3 + new Vector3(0f, -10f, 0f), handle3 + new Vector3(0f, 10f, 0f));
                Handles.EndGUI();

                float handleDist = Vector2.Distance(handle, evt.mousePosition);
                if (handleDist < 12f)
                    EditorGUIUtility.AddCursorRect(new Rect(handle.x - 12f, handle.y - 12f,
                        24f, 24f), MouseCursor.MoveArrow);

                // pivot drag
                if (evt.type == EventType.MouseDown && evt.button == 0 &&
                    handleDist < 12f)
                {
                    Undo.RecordObject(_host.SliceProfileAsset, "Edit Cell Pivot");
                    _dragPivot = true;
                    _preset = PivotPreset.Custom;
                    evt.Use();
                }
                else if (_dragPivot && evt.type == EventType.MouseDrag && evt.button == 0)
                {
                    Vector2 local = new Vector2(
                        (evt.mousePosition.x - world.x) / world.width,
                        1f - (evt.mousePosition.y - world.y) / world.height);
                    ApplyPivot(sheet, _selectedCell,
                        new Vector2(Mathf.Clamp01(local.x), Mathf.Clamp01(local.y)));
                    SyncInspectorPivot(local);
                    evt.Use();
                    Repaint();
                }
                else if (_dragPivot && evt.type == EventType.MouseUp)
                {
                    _dragPivot = false;
                    RefreshDependentPreviews();
                    evt.Use();
                }
            }

            // click select (ignore when the drag just started)
            if (!_dragPivot && evt.type == EventType.MouseDown && evt.button == 0 &&
                canvas.Contains(evt.mousePosition))
            {
                int c = Mathf.Clamp((int)((evt.mousePosition.x - canvas.x) / cw), 0, cols - 1);
                int r = Mathf.Clamp((int)((evt.mousePosition.y - canvas.y) / ch), 0, rows - 1);
                _selectedCell = r * cols + c;
                SyncInspectorFromEffective(sheet, _selectedCell);
                evt.Use();
                Repaint();
            }

            GUI.EndScrollView();
        }

        static Texture2D _panelBackground;

        static Texture2D PanelBackgroundTexture()
        {
            if (_panelBackground != null)
                return _panelBackground;
            var color = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.16f, 0.16f, 1f)
                : new Color(0.835f, 0.835f, 0.835f, 1f);
            _panelBackground = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _panelBackground.SetPixel(0, 0, color);
            _panelBackground.Apply(false, true);
            _panelBackground.hideFlags = HideFlags.HideAndDontSave;
            return _panelBackground;
        }

        static Rect CellToCanvas(Rect canvas, int cols, int rows, int col, int row)
            => new(canvas.x + col * (canvas.width / cols),
                   canvas.y + row * (canvas.height / rows),
                   canvas.width / cols, canvas.height / rows);

        // ---- inspector ----

        void DrawInspector(SpriteSheetDef sheet, Texture2D texture, int cols, int rows)
        {
            // opaque panel: the canvas must never bleed through, whatever the
            // zoom or scroll position. Scope-based layout only — no BeginArea,
            // so layout state can never go mismatched.
            var panelBg = new GUIStyle(GUIStyle.none);
            panelBg.normal.background = PanelBackgroundTexture();
            panelBg.padding = new RectOffset(8, 8, 8, 8);

            using (new EditorGUILayout.VerticalScope(panelBg,
                GUILayout.Width(258f), GUILayout.ExpandHeight(true)))
            {
            GUILayout.Label("Sprite", EditorStyles.boldLabel);
            EditorGUILayout.Space(2f);

                if (_selectedCell < 0 || _selectedCell >= cols * rows)
                {
                    EditorGUILayout.HelpBox("Click a cell on the sheet to edit it.",
                        MessageType.None);
                    return;
                }

                int slot = _selectedCell;
                int c = slot % cols;
                int r = slot / cols;
                EditorGUILayout.LabelField("Name", $"{sheet.Name ?? "Sheet"}_{slot}");
                EditorGUILayout.LabelField("Cell", $"row {r} · col {c}");

                // ---- position / rect (CroppedCellRects) ----
                var rects = sheet.CroppedCellRects;
                RectInt rect = GetOrCreateCellRect(sheet, texture, cols, rows, slot);
                using (new EditorGUI.DisabledScope(true))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.IntField("X", rect.x);
                        EditorGUILayout.IntField("Y", rect.y);
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.IntField("W", rect.width);
                        EditorGUILayout.IntField("H", rect.height);
                    }
                }
                EditorGUILayout.LabelField("Derived from Columns x Rows — change them or use Slice.",
                    EditorStyles.miniLabel);

                EditorGUILayout.Space(6f);

                // ---- pivot ----
                bool hasOverride = SpriteSheetProfile.TryGetCellPivot(sheet, slot, out _);
                Vector2 effective = ResolveEffectivePivot(sheet, slot);
                EditorGUILayout.LabelField("Pivot" + (_globalPivot
                    ? "  (all cells)"
                    : hasOverride ? "  (override)" : ""), EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                _globalPivot = GUILayout.Toggle(_globalPivot, new GUIContent(
                    "Apply To All Cells",
                    "ON: moving the pivot (drag, preset, or fields) moves the pivot of EVERY " +
                    "cell at once — per-cell overrides are cleared and the sheet pivot is set. " +
                    "OFF: only the selected cell changes."));
                if (EditorGUI.EndChangeCheck() && _globalPivot)
                {
                    Undo.RecordObject(_host.SliceProfileAsset, "Global Cell Pivot");
                    sheet.CellPivots?.Clear();
                    EditorUtility.SetDirty(_host.SliceProfileAsset);
                    _host.SliceNotifyProfileEdited();
                    RefreshDependentPreviews();
                }

                EditorGUI.BeginChangeCheck();
                _preset = (PivotPreset)EditorGUILayout.EnumPopup("Preset", _preset);
                _unit = (PivotUnitModeProxy)EditorGUILayout.EnumPopup("Pivot Unit Mode", _unit);
                if (EditorGUI.EndChangeCheck() && _preset != PivotPreset.Custom)
                {
                    var (px, py) = PresetVector(_preset);
                    ApplyPivot(sheet, slot, new Vector2(px, py));
                }

                EditorGUI.BeginChangeCheck();
                Vector2 edited;
                if (_unit == PivotUnitModeProxy.Pixels)
                {
                    float cellW = texture.width / (float)cols;
                    float cellH = texture.height / (float)rows;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        float ex = EditorGUILayout.FloatField("X", effective.x * cellW);
                        float ey = EditorGUILayout.FloatField("Y", effective.y * cellH);
                        edited = new Vector2(
                            cellW > 0 ? ex / cellW : 0.5f,
                            cellH > 0 ? ey / cellH : 0.5f);
                    }
                }
                else
                {
                    edited = EditorGUILayout.Vector2Field("Custom Pivot", effective);
                }
                if (EditorGUI.EndChangeCheck() &&
                    (edited - effective).sqrMagnitude > 1e-10f)
                {
                    ApplyPivot(sheet, slot,
                        new Vector2(Mathf.Clamp01(edited.x), Mathf.Clamp01(edited.y)));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!hasOverride))
                    {
                        if (GUILayout.Button("Reset to Sheet Pivot"))
                        {
                            Undo.RecordObject(_host.SliceProfileAsset, "Reset Cell Pivot");
                            SpriteSheetProfile.ClearCellPivot(sheet, slot);
                            SyncInspectorFromEffective(sheet, slot);
                            EditorUtility.SetDirty(_host.SliceProfileAsset);
                            _host.SliceNotifyProfileEdited();
                            RefreshDependentPreviews();
                        }
                    }
                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox(
                    "Drag the green pivot dot on the sheet. Position fields edit the " +
                    "cropped rect (Cropped layout). Pivots and rects live in the profile.",
                    MessageType.None);
            }
        }

        // ---- slice host ----

        Texture2D ISpriteSheetSliceHost.SliceTargetTexture => Sheet?.Texture;

        bool ISpriteSheetSliceHost.TryGetSliceCellMetrics(SpriteSheetSliceRequest request,
            out int texW, out int texH, out int cellW, out int cellH)
        {
            var tex = Sheet?.Texture;
            if (tex == null)
            {
                texW = texH = cellW = cellH = 0;
                return false;
            }
            texW = tex.width;
            texH = tex.height;
            if (request.Type == SpriteSheetSliceType.GridByCellSize)
            {
                cellW = request.CellSize.x;
                cellH = request.CellSize.y;
                return true;
            }
            cellW = Mathf.Max(1, (texW - request.Offset.x -
                                  request.Padding.x * (request.Columns - 1)) / request.Columns);
            cellH = Mathf.Max(1, (texH - request.Offset.y -
                                  request.Padding.y * (request.Rows - 1)) / request.Rows);
            return true;
        }

        void ISpriteSheetSliceHost.RunSheetSlice(SpriteSheetSliceRequest request)
        {
            var sheet = Sheet;
            var texture = sheet?.Texture;
            if (sheet == null || texture == null)
                return;

            Texture2D owned = null;
            try
            {
                var pixels = SpriteSheetSlicing.GetPixels32(texture, out owned);

                var (rects, cols, rows, _) = SpriteSheetSlicing.SliceGrid(
                    pixels, texture.width, texture.height, request);

                Undo.RecordObject(_host.SliceProfileAsset, "Slice Sheet");
                sheet.Columns = cols;
                sheet.Rows = rows;
                sheet.CroppedCellRects = rects; // stored for stats/reuse, dormant in Grid
                sheet.CellLayoutMode = SpriteSheetCellLayoutMode.Grid;
                sheet.Pivot = new Vector2(
                    Mathf.Clamp01(request.PivotNormalized.x),
                    Mathf.Clamp01(request.PivotNormalized.y));
                _selectedCell = -1;
                EditorUtility.SetDirty(_host.SliceProfileAsset);
                _host.SliceNotifyProfileEdited();
                _host.SliceReloadLegacyFromActiveSheet();
                RefreshDependentPreviews();
            }
            finally
            {
                if (owned != null)
                    DestroyImmediate(owned);
            }
        }

        // ---- helpers ----

        /// <summary>
        /// Profile edits (pivots) are baked into preview meshes — rebuild the
        /// scene previews of every authoring that references this profile.
        /// </summary>
        void RefreshDependentPreviews()
        {
            var asset = _host.SliceProfileAsset;
            if (asset == null)
                return;
            foreach (var authoring in Object.FindObjectsByType<SpriteStaticAuthoring>(
                         FindObjectsSortMode.None))
            {
                if (authoring.Profile == asset)
                    authoring.UpdatePreview();
            }
            foreach (var set in Object.FindObjectsByType<SpriteAnimSetAuthoring>(
                         FindObjectsSortMode.None))
            {
                if (set.Profile == asset)
                    set.ApplyQuadPreview();
            }
        }

        Vector2 ResolveEffectivePivot(SpriteSheetDef sheet, int slot)
        {
            if (SpriteSheetProfile.TryGetCellPivot(sheet, slot, out var pivot))
                return pivot;
            return sheet.Pivot;
        }

        void ApplyPivot(SpriteSheetDef sheet, int slot, Vector2 pivot)
        {
            Undo.RecordObject(_host.SliceProfileAsset, "Edit Cell Pivot");
            if (_globalPivot)
            {
                // one pivot for every cell: set the sheet pivot and drop all
                // per-cell overrides so nothing stays behind
                sheet.Pivot = pivot;
                sheet.CellPivots?.Clear();
            }
            else
            {
                SpriteSheetProfile.SetCellPivot(sheet, slot, pivot);
            }
            EditorUtility.SetDirty(_host.SliceProfileAsset);
            _host.SliceNotifyProfileEdited();
            if (!_dragPivot) // drag refreshes once on MouseUp instead
                RefreshDependentPreviews();
        }

        RectInt GetOrCreateCellRect(SpriteSheetDef sheet, Texture2D texture,
            int cols, int rows, int slot)
        {
            // grid-only: the position IS the uniform grid cell derived from
            // Columns x Rows — stored crop data is ignored entirely
            int cw = texture.width / cols;
            int ch = texture.height / rows;
            int c = slot % cols;
            int r = slot / cols;
            // row 0 = top (profile convention) → pixel y from bottom
            return new RectInt(c * cw, texture.height - (r + 1) * ch, cw, ch);
        }

        void WriteCellRect(SpriteSheetDef sheet, Texture2D texture,
            int cols, int rows, int slot, RectInt rect)
        {
            Undo.RecordObject(_host.SliceProfileAsset, "Edit Cell Rect");
            var rects = sheet.CroppedCellRects;
            if (rects == null || rects.Length != cols * rows)
            {
                rects = new RectInt[cols * rows];
                for (int i = 0; i < rects.Length; i++)
                    rects[i] = GetOrCreateCellRect(sheet, texture, cols, rows, i);
            }
            rect.x = Mathf.Clamp(rect.x, 0, texture.width - 1);
            rect.y = Mathf.Clamp(rect.y, 0, texture.height - 1);
            rect.width = Mathf.Clamp(rect.width, 1, texture.width - rect.x);
            rect.height = Mathf.Clamp(rect.height, 1, texture.height - rect.y);
            rects[slot] = rect;
            sheet.CroppedCellRects = rects;
            sheet.CellLayoutMode = SpriteSheetCellLayoutMode.Cropped;
            EditorUtility.SetDirty(_host.SliceProfileAsset);
            _host.SliceNotifyProfileEdited();
            _host.SliceReloadLegacyFromActiveSheet();
        }

        void SyncInspectorFromEffective(SpriteSheetDef sheet, int slot)
        {
            _preset = MatchPreset(ResolveEffectivePivot(sheet, slot));
        }

        void SyncInspectorPivot(Vector2 normalized)
        {
            _preset = PivotPreset.Custom;
        }

        static PivotPreset MatchPreset(Vector2 p)
        {
            if (p == new Vector2(0.5f, 0.5f)) return PivotPreset.Center;
            if (p == new Vector2(0.5f, 1f)) return PivotPreset.Top;
            if (p == new Vector2(0.5f, 0f)) return PivotPreset.Bottom;
            if (p == new Vector2(0f, 0.5f)) return PivotPreset.Left;
            if (p == new Vector2(1f, 0.5f)) return PivotPreset.Right;
            if (p == new Vector2(0f, 1f)) return PivotPreset.TopLeft;
            if (p == new Vector2(1f, 1f)) return PivotPreset.TopRight;
            if (p == new Vector2(0f, 0f)) return PivotPreset.BottomLeft;
            if (p == new Vector2(1f, 0f)) return PivotPreset.BottomRight;
            return PivotPreset.Custom;
        }

        static (float x, float y) PresetVector(PivotPreset preset) => preset switch
        {
            PivotPreset.Top => (0.5f, 1f),
            PivotPreset.Bottom => (0.5f, 0f),
            PivotPreset.Left => (0f, 0.5f),
            PivotPreset.Right => (1f, 0.5f),
            PivotPreset.TopLeft => (0f, 1f),
            PivotPreset.TopRight => (1f, 1f),
            PivotPreset.BottomLeft => (0f, 0f),
            PivotPreset.BottomRight => (1f, 0f),
            _ => (0.5f, 0.5f),
        };

        static void DrawRectOutline(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        static void DrawCheckerboard(Rect rect, float square)
        {
            int xCount = Mathf.Max(1, Mathf.CeilToInt(rect.width / square));
            int yCount = Mathf.Max(1, Mathf.CeilToInt(rect.height / square));
            float sw = rect.width / xCount;
            float sh = rect.height / yCount;
            for (int y = 0; y < yCount; y++)
                for (int x = 0; x < xCount; x++)
                    EditorGUI.DrawRect(
                        new Rect(rect.x + x * sw, rect.y + y * sh, sw, sh),
                        (x + y) % 2 == 0 ? CheckerA : CheckerB);
        }
    }
}
