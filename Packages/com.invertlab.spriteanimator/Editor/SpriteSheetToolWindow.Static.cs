using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    public sealed partial class SpriteSheetToolWindow
    {
        Vector2 _staticBrowserScroll;
        Vector2 _staticCellScroll;
        Vector2 _staticInspectorScroll;
        SpriteSheetDef _staticGridSheet;
        bool _staticAdvanced;
        bool _staticScaleTool = true;
        bool _staticScaling;
        float _staticScaleStartHeight;
        float _staticScaleStartDist;

        bool StaticShowsGrid(SpriteSheetDef sheet)
            => sheet != null && (sheet.Columns > 1 || sheet.Rows > 1 ||
                sheet.CellLayoutMode != SpriteSheetCellLayoutMode.Grid ||
                ReferenceEquals(sheet, _staticGridSheet));

        void EnsureStaticSession()
        {
            if (_profile == null)
                return;
            _profile.EnsureSheets(_selectedSheet);
            if (_profile.Sheets == null || _profile.Sheets.Count == 0)
                return;
            int index = Mathf.Clamp(_profile.StaticSheetIndex, 0, _profile.Sheets.Count - 1);
            if (_selectedSheet != index)
            {
                _selectedSheet = index;
                _profile.SyncLegacyFromSheet(index);
            }
        }

        void ClampStaticCellToSheet()
        {
            var sheet = CurrentStaticSheet();
            int columns = Mathf.Max(1, sheet != null ? sheet.Columns : 1);
            int rows = Mathf.Max(1, sheet != null ? sheet.Rows : 1);
            _profile.StaticRow = Mathf.Clamp(_profile.StaticRow, 0, rows - 1);
            _profile.StaticColumn = Mathf.Clamp(_profile.StaticColumn, 0, columns - 1);
        }

        SpriteSheetDef CurrentStaticSheet()
        {
            if (_profile == null)
                return null;
            return _profile.SheetAt(_profile.StaticSheetIndex) ?? _profile.SheetAt(_selectedSheet);
        }

        void SelectStaticSheet(int index)
        {
            if (_profile == null)
                return;
            _profile.EnsureSheets(index);
            if (_profile.Sheets == null || _profile.Sheets.Count == 0)
                return;
            index = Mathf.Clamp(index, 0, _profile.Sheets.Count - 1);
            if (index == _profile.StaticSheetIndex && index == _selectedSheet)
                return;
            RecordDiscreteUndo("Select Static Sheet");
            _profile.StaticSheetIndex = index;
            _selectedSheet = index;
            ClampStaticCellToSheet();
            _profile.SyncLegacyFromSheet(_selectedSheet);
            SaveDirty();
        }

        void SetStaticCell(int row, int column)
        {
            if (_profile == null)
                return;
            var sheet = CurrentStaticSheet();
            int columns = Mathf.Max(1, sheet != null ? sheet.Columns : 1);
            int rows = Mathf.Max(1, sheet != null ? sheet.Rows : 1);
            row = Mathf.Clamp(row, 0, rows - 1);
            column = Mathf.Clamp(column, 0, columns - 1);
            if (row == _profile.StaticRow && column == _profile.StaticColumn)
                return;
            RecordDiscreteUndo("Set Static Cell");
            _profile.StaticRow = row;
            _profile.StaticColumn = column;
            SaveDirty();
        }

        void DrawStaticBrowser(Rect rect)
        {
            EnsureStaticSession();
            GUILayout.BeginArea(rect);
            _staticBrowserScroll = EditorGUILayout.BeginScrollView(_staticBrowserScroll);
            GUILayout.Label("IMAGES", _sectionStyle);
            GUILayout.Label("Choose the image to display.", _mutedStyle);

            if (_profile.AnimKind != SpriteAnimKind.Static)
            {
                string runtimeName = _profile.AnimKind == SpriteAnimKind.Parts ? "Parts" : "Frames";
                EditorGUILayout.HelpBox(
                    $"Preview only. Character currently uses {runtimeName}. Pick a cell here, then activate Static runtime when ready.",
                    MessageType.Info);
                using (new EditorGUI.DisabledScope(!SpritePartsAuthoringOps.HasUsableStaticSheet(_profile)))
                {
                    if (GUILayout.Button("Use Static for Character", GUILayout.Height(22f)))
                        SwitchRuntimeKind(SpriteAnimKind.Static, "Use Static for Character");
                }
                GUILayout.Space(8f);
            }

            int sheetCount = _profile.Sheets != null ? _profile.Sheets.Count : 0;
            if (sheetCount == 0)
            {
                EditorGUILayout.HelpBox("Add a sheet, then click a cell in the grid.", MessageType.Info);
            }
            else
            {
                var input = Event.current;
                for (int i = 0; i < sheetCount; i++)
                {
                    var sheet = _profile.Sheets[i];
                    if (sheet == null)
                        continue;
                    bool selected = i == _profile.StaticSheetIndex;
                    string label = string.IsNullOrWhiteSpace(sheet.Name) ? $"Sheet {i + 1}" : sheet.Name;
                    if (sheet.Texture == null)
                        label += " (no texture)";

                    var rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(24f));
                    if (input.type == EventType.Repaint && rowRect.width > 1f)
                    {
                        EditorGUI.DrawRect(rowRect, selected
                            ? new Color(0.18f, 0.32f, 0.36f, 1f)
                            : new Color(0.155f, 0.172f, 0.205f, 1f));
                        DrawBorder(rowRect, selected ? AccentColor : BorderColor, selected ? 2f : 1f);
                    }

                    if (GUILayout.Button(label, selected ? EditorStyles.boldLabel : EditorStyles.label,
                            GUILayout.Height(22f)))
                        SelectStaticSheet(i);

                    bool canDeleteSheet = sheetCount > 1;
                    using (new EditorGUI.DisabledScope(!canDeleteSheet))
                    {
                        Color prev = GUI.color;
                        var xRect = GUILayoutUtility.GetRect(ClipRowDeleteWidth, 16f,
                            GUILayout.Width(ClipRowDeleteWidth), GUILayout.Height(16f));
                        xRect.y += 4f;
                        if (canDeleteSheet && xRect.Contains(input.mousePosition))
                            GUI.color = new Color(1f, 0.42f, 0.42f, 1f);
                        if (GUI.Button(xRect, new GUIContent("✕",
                                canDeleteSheet
                                    ? "Delete this image (Undo supported)."
                                    : "Keep at least one image."),
                            EditorStyles.miniButton))
                            TryDeleteStaticImageAt(i);
                        GUI.color = prev;
                    }
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(2f);
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Image", GUILayout.Width(70f)))
            {
                AddSheet();
                SelectStaticSheet(_selectedSheet);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawStaticInspector(Rect rect)
        {
            EnsureStaticSession();
            GUILayout.BeginArea(rect);
            _staticInspectorScroll = EditorGUILayout.BeginScrollView(_staticInspectorScroll);
            GUILayout.Label("STATIC SPRITE", _sectionStyle);
            var sheet = CurrentStaticSheet();
            if (sheet == null)
            {
                EditorGUILayout.HelpBox("Add an image in the left list or drop one on the canvas.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            GUILayout.Space(8f);
            GUILayout.Label("1  Choose image", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var texture = (Texture2D)EditorGUILayout.ObjectField(
                "Image", sheet.Texture, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck())
                ApplySheetTexture(texture);

            GUILayout.Space(12f);
            GUILayout.Label("2  Choose what to show", EditorStyles.boldLabel);
            bool showGrid = StaticShowsGrid(sheet);
            int source = GUILayout.Toolbar(showGrid ? 1 : 0, new[] { "Whole Image", "Sheet Cell" });
            if (source != (showGrid ? 1 : 0))
            {
                if (source == 1)
                    _staticGridSheet = sheet; // Reveal slicing controls without changing the image.
                else if (ConfirmSharedSheetEdit())
                {
                    RecordDiscreteUndo("Use Whole Image");
                    sheet.Columns = sheet.Rows = 1;
                    sheet.CellLayoutMode = SpriteSheetCellLayoutMode.Grid;
                    _profile.StaticRow = _profile.StaticColumn = 0;
                    _staticGridSheet = null;
                    _profile.SyncLegacyFromSheet(_selectedSheet);
                    SaveDirty();
                }
                showGrid = StaticShowsGrid(sheet);
            }

            if (showGrid)
            {
                bool cropped = sheet.CellLayoutMode != SpriteSheetCellLayoutMode.Grid;
                using (new EditorGUI.DisabledScope(cropped))
                {
                    EditorGUI.BeginChangeCheck();
                    int columns = EditorGUILayout.DelayedIntField("Columns", Mathf.Max(1, sheet.Columns));
                    int rows = EditorGUILayout.DelayedIntField("Rows", Mathf.Max(1, sheet.Rows));
                    if (EditorGUI.EndChangeCheck() && ConfirmSharedSheetEdit())
                    {
                        RecordDiscreteUndo("Edit Static Sheet Grid");
                        sheet.Columns = Mathf.Max(1, columns);
                        sheet.Rows = Mathf.Max(1, rows);
                        ClampStaticCellToSheet();
                        _profile.SyncLegacyFromSheet(_selectedSheet);
                        SaveDirty();
                    }
                }
                EditorGUILayout.HelpBox(cropped
                    ? "Imported cells keep their slicing. Click a cell below the preview."
                    : "Set the grid, then click a cell below the preview.", MessageType.None);
                int cell = _profile.StaticRow * Mathf.Max(1, sheet.Columns) + _profile.StaticColumn;
                EditorGUILayout.LabelField("Selected cell", $"{cell + 1} of {Mathf.Max(1, sheet.Columns) * Mathf.Max(1, sheet.Rows)}");
            }
            else
                GUILayout.Label("The entire image will be displayed.", EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(12f);
            GUILayout.Label("Pivot", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Green circle on the preview. Shared with Frames and Parts that use this image.",
                MessageType.None);
            using (new EditorGUI.DisabledScope(_pivotLocked))
            {
                EditorGUI.BeginChangeCheck();
                float px = EditorGUILayout.DelayedFloatField("Pivot X", _profile.Pivot.x);
                float py = EditorGUILayout.DelayedFloatField("Pivot Y", _profile.Pivot.y);
                if (EditorGUI.EndChangeCheck() && ConfirmSharedSheetEdit())
                    SetProfilePivot(new Vector2(px, py), "Edit Sprite Pivot");
            }
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_pivotLocked))
            {
                if (GUILayout.Button(new GUIContent("Center", "Snap pivot to cell center (0.5, 0.5).")))
                {
                    if (ConfirmSharedSheetEdit())
                        SetProfilePivot(SpriteSheetProfile.DefaultPivot, "Snap Pivot to Cell Center");
                }
                if (GUILayout.Button(new GUIContent("Feet", "Snap pivot to bottom-center (0.5, 0).")))
                {
                    if (ConfirmSharedSheetEdit())
                        SetProfilePivot(new Vector2(0.5f, 0f), "Snap Pivot to Bottom Center");
                }
            }
            if (GUILayout.Button(PivotLockContent(_pivotLocked), GUILayout.Width(28f), GUILayout.Height(18f)))
                SetPivotLocked(!_pivotLocked);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(12f);
            GUILayout.Label("Actual Size", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "World size of the baked sprite. The faint box is the original cell (pixels / PPU). Scale % and the Scale tool resize this size - they do not change PPU or GameObject scale.",
                MessageType.None);
            float naturalH = Mathf.Max(0.001f, SpriteSheetProfile.GetWorldHeight(sheet, 1f));
            float actualH = SpriteProfileSheetOps.ResolveStaticSizeUnits(_profile);
            float sizeAspect = Mathf.Max(0.01f, SpriteSheetProfile.GetCellAspect(sheet));
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                float nextH = EditorGUILayout.DelayedFloatField("Height", actualH);
                if (EditorGUI.EndChangeCheck())
                    SetStaticSizeUnits(nextH);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                float nextW = EditorGUILayout.DelayedFloatField("Width", actualH * sizeAspect);
                if (EditorGUI.EndChangeCheck())
                    SetStaticSizeUnits(nextW / sizeAspect);
            }
            EditorGUI.BeginChangeCheck();
            float pct = EditorGUILayout.DelayedFloatField(
                new GUIContent("Scale %", "Percent of the original PPU cell height. 100 = original box."),
                actualH / naturalH * 100f);
            if (EditorGUI.EndChangeCheck())
                SetStaticSizeUnits(naturalH * (pct / 100f));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Reset to Original",
                        "Clear the override so actual size matches the sheet cell (pixels / PPU).")))
                {
                    RecordDiscreteUndo("Reset Static Size");
                    _profile.StaticSizeUnits = 0f;
                    SaveDirty();
                }
                GUILayout.Label($"{actualH:0.###} u tall", _mutedStyle);
            }

            GUILayout.Space(12f);
            _staticAdvanced = EditorGUILayout.Foldout(_staticAdvanced, "Advanced: size", true);
            if (_staticAdvanced)
            {
                EditorGUILayout.HelpBox("These image settings are shared with Frames and Parts that use this sheet.", MessageType.None);
                EditorGUI.BeginChangeCheck();
                float ppu = EditorGUILayout.DelayedFloatField("Pixels Per Unit", sheet.PixelsPerUnit);
                if (EditorGUI.EndChangeCheck() && ConfirmSharedSheetEdit())
                {
                    RecordDiscreteUndo("Edit Shared Sheet Geometry");
                    sheet.PixelsPerUnit = Mathf.Max(0.001f, ppu);
                    _profile.SyncLegacyFromSheet(_selectedSheet);
                    SaveDirty();
                }
                if (showGrid)
                {
                    EditorGUI.BeginChangeCheck();
                    int row = EditorGUILayout.DelayedIntField("Row (0-based)", _profile.StaticRow);
                    int column = EditorGUILayout.DelayedIntField("Column (0-based)", _profile.StaticColumn);
                    if (EditorGUI.EndChangeCheck()) SetStaticCell(row, column);
                }
            }

            GUILayout.Space(16f);
            GUILayout.Label("3  Place in scene", EditorStyles.boldLabel);
            bool ready = SpritePartsAuthoringOps.HasUsableStaticSheet(_profile);
            if (!ready)
                EditorGUILayout.HelpBox("Choose an image and a valid cell to continue.", MessageType.Info);
            if (_profile.AnimKind != SpriteAnimKind.Static)
            {
                using (new EditorGUI.DisabledScope(!ready))
                    if (GUILayout.Button("Use Static for Character", GUILayout.Height(24f)))
                        SwitchRuntimeKind(SpriteAnimKind.Static, "Use Static for Character");
            }
            using (new EditorGUI.DisabledScope(!ready || _profile.AnimKind != SpriteAnimKind.Static))
            {
                if (GUILayout.Button("Create Scene Object", _primaryStyle, GUILayout.Height(28f)))
                    SetupSceneObject(true);
                using (new EditorGUI.DisabledScope(Selection.activeGameObject == null))
                    if (GUILayout.Button("Apply to Selected Object", GUILayout.Height(24f)))
                        SetupSceneObject(false);
            }
            GUILayout.Label("Saves the profile before placing it in an open SubScene.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawStaticPreview(Rect rect)
        {
            EnsureStaticSession();
            int pivotId = GUIUtility.GetControlID(
                "InvertLab.StaticPivot".GetHashCode(), FocusType.Passive, rect);
            int scaleId = GUIUtility.GetControlID(
                "InvertLab.StaticScale".GetHashCode(), FocusType.Passive, rect);
            HandlePreviewSheetDragDrop(rect);
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.09f, 0.11f));

            var sheet = CurrentStaticSheet();
            var tex = sheet?.Texture;
            if (tex == null)
            {
                GUI.Label(new Rect(rect.x + 16f, rect.y + 16f, rect.width - 32f, 40f),
                    "Drop an image here to get started, or choose Image on the right.", _mutedStyle);
                return;
            }

            int columns = Mathf.Max(1, sheet.Columns);
            int rows = Mathf.Max(1, sheet.Rows);
            int selectedCell = _profile.StaticRow * columns + _profile.StaticColumn;

            bool showGrid = StaticShowsGrid(sheet);
            float previewH = showGrid
                ? Mathf.Clamp(rect.height * 0.48f, 96f, 320f)
                : Mathf.Max(48f, rect.height - 20f);
            var previewRect = new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, previewH);
            EditorGUI.DrawRect(previewRect, new Color(0.06f, 0.07f, 0.09f, 1f));
            DrawCheckerboard(previewRect, 12f);
            float cellAspect = 1f;
            if (SpriteSheetProfile.TryGetCellPixels(sheet, out float srcW, out float srcH) && srcH > 0.01f)
                cellAspect = srcW / srcH;
            else
                cellAspect = (tex.width / (float)columns) / Mathf.Max(1f, tex.height / (float)rows);
            cellAspect = Mathf.Max(0.01f, cellAspect);

            float naturalH = Mathf.Max(0.001f, SpriteSheetProfile.GetWorldHeight(sheet, 1f));
            float actualH = SpriteProfileSheetOps.ResolveStaticSizeUnits(_profile);
            float naturalW = naturalH * cellAspect;
            float actualW = actualH * cellAspect;
            float viewW = Mathf.Max(naturalW, actualW, 0.25f);
            float viewH = Mathf.Max(naturalH, actualH, 0.25f);
            float pxPerUnit = Mathf.Min(
                (previewRect.width - 36f) / viewW,
                (previewRect.height - 52f) / viewH);
            pxPerUnit = Mathf.Max(8f, pxPerUnit);
            Vector2 pivotScreen = previewRect.center;
            // Pin the sprite by its center so dragging the pivot only moves the
            // handle, not the image. Pivot UV still maps onto this rect.
            var layoutPivot = new Vector2(0.5f, 0.5f);
            var originalRect = WorldBoxToScreen(pivotScreen, layoutPivot, naturalW, naturalH, pxPerUnit);
            var fitted = WorldBoxToScreen(pivotScreen, layoutPivot, actualW, actualH, pxPerUnit);

            if (_showPreviewSize)
            {
                Color orig = new(0.28f, 0.55f, 0.52f, 0.55f);
                DrawBorder(originalRect, orig, 1f);
                DrawPreviewSizeCorners(originalRect, 8f, orig);
            }
            DrawCellTinted(tex, selectedCell, fitted, Color.white, columns, rows);
            DrawBorder(previewRect, BorderColor, 1f);
            if (_showPreviewSize)
                DrawStaticSpriteSizeBox(sheet, selectedCell, fitted, showGrid, actualH, naturalH);
            GUI.Label(new Rect(previewRect.x + 8f, previewRect.yMax - 20f, previewRect.width - 190f, 16f),
                showGrid ? $"{sheet.Name}  /  Cell {selectedCell + 1}" : $"{sheet.Name}  /  Whole image", _mutedStyle);

            HandleStaticPivot(pivotId, scaleId, fitted, previewRect);

            if (showGrid)
            {
                var gridViewport = new Rect(
                    rect.x + 12f,
                    previewRect.yMax + 10f,
                    rect.width - 24f,
                    Mathf.Max(48f, rect.yMax - previewRect.yMax - 20f));
                DrawStaticCellGrid(gridViewport, tex, sheet, columns, rows, cellAspect);
            }
        }

        void HandleStaticPivot(int pivotId, int scaleId, Rect fitted, Rect canvas)
        {
            var scaleToggle = new Rect(canvas.xMax - 264f, canvas.yMax - 30f, 80f, 22f);
            var sizeToggle = new Rect(canvas.xMax - 176f, canvas.yMax - 30f, 80f, 22f);
            var pivotToggle = new Rect(canvas.xMax - 88f, canvas.yMax - 30f, 80f, 22f);
            var evt = Event.current;
            bool overToggle = scaleToggle.Contains(evt.mousePosition) ||
                              sizeToggle.Contains(evt.mousePosition) ||
                              pivotToggle.Contains(evt.mousePosition);
            bool overScale = _staticScaleTool && _showPreviewSize &&
                             StaticScaleHandleHit(fitted, evt.mousePosition);

            if (!overToggle)
                HandleStaticScaleInput(scaleId, fitted);

            bool overHandle = _showPivot && PivotHandleHitTest(fitted, evt.mousePosition);
            if (_showPivot && !overToggle && !overScale && !_staticScaling &&
                (_draggingPivot || overHandle || canvas.Contains(evt.mousePosition)))
            {
                if (evt.type == EventType.MouseDown && evt.button == 0 && overHandle &&
                    !ConfirmSharedSheetEdit())
                {
                    evt.Use();
                }
                else
                    HandlePivotInput(pivotId, fitted);
            }
            DrawPivot(fitted);
            DrawStaticScaleToggle(scaleToggle);
            DrawPreviewSizeToggle(new Rect(canvas.x, canvas.y, canvas.width + 88f, canvas.height));
            DrawPreviewPivotToggle(new Rect(canvas.x, canvas.y, canvas.width + 88f, canvas.height));
        }

        void DrawStaticScaleToggle(Rect rect)
        {
            if (!GUI.Button(rect,
                    new GUIContent(_staticScaleTool ? "Scale: On" : "Scale: Off",
                        "Drag the cyan box corners to change actual world size. Does not change PPU or GameObject scale."),
                    EditorStyles.miniButton))
                return;
            RecordWindowUndo("Toggle Static Scale Tool");
            _staticScaleTool = !_staticScaleTool;
            if (!_staticScaleTool)
                _staticScaling = false;
            _status = _staticScaleTool
                ? "Drag the cyan box to edit actual size"
                : "Scale tool off";
            Repaint();
        }

        static Rect WorldBoxToScreen(Vector2 pivotScreen, Vector2 pivotUv, float worldW, float worldH, float pxPerUnit)
        {
            pivotUv = new Vector2(Mathf.Clamp01(pivotUv.x), Mathf.Clamp01(pivotUv.y));
            return new Rect(
                pivotScreen.x - worldW * pxPerUnit * pivotUv.x,
                pivotScreen.y - worldH * pxPerUnit * (1f - pivotUv.y),
                worldW * pxPerUnit,
                worldH * pxPerUnit);
        }

        void SetStaticSizeUnits(float height)
        {
            if (_profile == null)
                return;
            height = Mathf.Max(0.01f, height);
            if (Mathf.Abs(height - SpriteProfileSheetOps.ResolveStaticSizeUnits(_profile)) < 0.0005f)
                return;
            RecordDiscreteUndo("Edit Static Size");
            _profile.StaticSizeUnits = height;
            _status = $"Actual size {height:0.###} u";
            SaveDirty();
            Repaint();
        }

        static Vector2[] StaticScaleHandlePoints(Rect sprite)
        {
            return new[]
            {
                new Vector2(sprite.xMin, sprite.yMin),
                new Vector2(sprite.xMax, sprite.yMin),
                new Vector2(sprite.xMax, sprite.yMax),
                new Vector2(sprite.xMin, sprite.yMax),
                new Vector2(sprite.center.x, sprite.yMin),
                new Vector2(sprite.xMax, sprite.center.y),
                new Vector2(sprite.center.x, sprite.yMax),
                new Vector2(sprite.xMin, sprite.center.y),
            };
        }

        static bool StaticScaleHandleHit(Rect sprite, Vector2 mouse)
        {
            const float hit = 10f * 10f;
            var pts = StaticScaleHandlePoints(sprite);
            for (int i = 0; i < pts.Length; i++)
            {
                if ((mouse - pts[i]).sqrMagnitude <= hit)
                    return true;
            }
            return false;
        }

        void HandleStaticScaleInput(int controlId, Rect sprite)
        {
            if (!_staticScaleTool || !_showPreviewSize || _profile == null)
                return;
            var evt = Event.current;
            Vector2 pivot = new(
                sprite.x + Mathf.Clamp01(_profile.Pivot.x) * sprite.width,
                sprite.y + (1f - Mathf.Clamp01(_profile.Pivot.y)) * sprite.height);
            bool over = StaticScaleHandleHit(sprite, evt.mousePosition);
            if (_staticScaling && GUIUtility.hotControl != 0 && GUIUtility.hotControl != controlId)
                GUIUtility.hotControl = controlId;
            bool owns = _staticScaling && GUIUtility.hotControl == controlId;

            if (evt.type == EventType.MouseDown && evt.button == 0 && over)
            {
                RecordDiscreteUndo("Scale Static Size");
                _staticScaling = true;
                _staticScaleStartHeight = SpriteProfileSheetOps.ResolveStaticSizeUnits(_profile);
                _staticScaleStartDist = Mathf.Max(4f, Vector2.Distance(evt.mousePosition, pivot));
                GUIUtility.hotControl = controlId;
                evt.Use();
                Repaint();
                return;
            }
            if (evt.type == EventType.MouseDrag && owns)
            {
                float dist = Mathf.Max(4f, Vector2.Distance(evt.mousePosition, pivot));
                float height = _staticScaleStartHeight * (dist / _staticScaleStartDist);
                _profile.StaticSizeUnits = Mathf.Max(0.01f, height);
                _status = $"Actual size {_profile.StaticSizeUnits:0.###} u";
                evt.Use();
                Repaint();
                return;
            }
            if (evt.type == EventType.MouseUp && evt.button == 0 && owns)
            {
                _staticScaling = false;
                if (GUIUtility.hotControl == controlId)
                    GUIUtility.hotControl = 0;
                SaveDirty();
                evt.Use();
                Repaint();
            }
        }

        void DrawStaticSpriteSizeBox(
            SpriteSheetDef sheet, int cellIndex, Rect spriteRect,
            bool showGrid, float actualH, float naturalH)
        {
            if (!SpriteSheetProfile.TryGetActiveCellPixels(sheet, cellIndex, out float cellW, out float cellH))
                return;
            Color box = new(0.28f, 0.92f, 0.82f, 0.95f);
            DrawBorder(spriteRect, box, 1.5f);
            DrawPreviewSizeCorners(spriteRect, 11f, box);

            Vector2 pivot = new(
                spriteRect.x + Mathf.Clamp01(_profile.Pivot.x) * spriteRect.width,
                spriteRect.y + (1f - Mathf.Clamp01(_profile.Pivot.y)) * spriteRect.height);
            EditorGUI.DrawRect(new Rect(pivot.x - 6f, pivot.y - 0.5f, 12f, 1f), box);
            EditorGUI.DrawRect(new Rect(pivot.x - 0.5f, pivot.y - 6f, 1f, 12f), box);

            if (_staticScaleTool)
            {
                var pts = StaticScaleHandlePoints(spriteRect);
                for (int i = 0; i < 4; i++)
                    DrawHandleKnob(pts[i]);
                for (int i = 4; i < pts.Length; i++)
                    DrawHandleKnob(pts[i], true);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(pts[0], 10f), MouseCursor.ResizeUpLeft);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(pts[1], 10f), MouseCursor.ResizeUpRight);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(pts[2], 10f), MouseCursor.ResizeUpLeft);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(pts[3], 10f), MouseCursor.ResizeUpRight);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(pts[4], 10f), MouseCursor.ResizeVertical);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(pts[5], 10f), MouseCursor.ResizeHorizontal);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(pts[6], 10f), MouseCursor.ResizeVertical);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(pts[7], 10f), MouseCursor.ResizeHorizontal);
            }

            int columns = Mathf.Max(1, sheet.Columns);
            int row = cellIndex / columns;
            int column = cellIndex - row * columns;
            float aspect = Mathf.Max(0.01f, cellH > 0.01f ? cellW / cellH : 1f);
            string sizeText =
                $"{cellW:0.#} x {cellH:0.#} px   *   {actualH * aspect:0.###} x {actualH:0.###} u";
            string cellText = showGrid
                ? FormatSheetCellFull(column, row, cellIndex)
                : $"Whole image  *  orig {naturalH:0.###} u";
            if (!showGrid || Mathf.Abs(actualH - naturalH) > 0.001f)
                cellText += showGrid ? $"  *  orig {naturalH:0.###} u" : string.Empty;
            var label = new Rect(spriteRect.x, spriteRect.y - 32f, 280f, 30f);
            if (label.y < 2f)
                label.y = spriteRect.yMax + 2f;
            EditorGUI.DrawRect(label, new Color(0.05f, 0.07f, 0.09f, 0.72f));
            GUI.Label(new Rect(label.x + 4f, label.y, label.width - 6f, 14f),
                new GUIContent(sizeText, "Pixel size stays the same. World size is Size Units, not scale."),
                _mutedStyle);
            GUI.Label(new Rect(label.x + 4f, label.y + 13f, label.width - 6f, 14f),
                new GUIContent(cellText, "Faint box = original PPU cell. Cyan box = actual baked size."),
                _mutedStyle);
        }

        void TryDeleteSelectedStaticImage()
            => TryDeleteStaticImageAt(_profile != null ? _profile.StaticSheetIndex : -1);

        void TryDeleteStaticImageAt(int index)
        {
            if (_profile?.Sheets == null || _profile.Sheets.Count <= 1)
            {
                _status = "Keep at least one image.";
                return;
            }
            if (index < 0 || index >= _profile.Sheets.Count)
                return;
            var def = _profile.Sheets[index];
            string delName = string.IsNullOrWhiteSpace(def?.Name)
                ? $"Sheet {index + 1}"
                : def.Name;
            if (!EditorUtility.DisplayDialog("Delete Image",
                    $"Delete image '{delName}'?", "Delete", "Cancel"))
                return;
            DeleteSheetAt(index);
            EnsureStaticSession();
        }

        void DrawStaticCellGrid(
            Rect gridViewport, Texture2D tex, SpriteSheetDef sheet, int columns, int rows, float cellAspect)
        {
            const float gap = 2f;
            float cellW = Mathf.Clamp((gridViewport.width - 18f - gap * (columns - 1)) / columns, 28f, 96f);
            float cellH = cellW / Mathf.Max(0.01f, cellAspect);
            float contentW = columns * cellW + gap * (columns - 1);
            float contentH = rows * cellH + gap * (rows - 1);
            _staticCellScroll = GUI.BeginScrollView(
                gridViewport, _staticCellScroll,
                new Rect(0f, 0f, Mathf.Max(contentW, gridViewport.width - 18f), contentH));

            var evt = Event.current;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    int cell = r * columns + c;
                    var cellRect = new Rect(c * (cellW + gap), r * (cellH + gap), cellW, cellH);
                    DrawCheckerboard(cellRect, 8f);
                    DrawCellTinted(tex, cell, FitAspectRect(cellRect, cellAspect), Color.white, columns, rows);
                    bool selected = r == _profile.StaticRow && c == _profile.StaticColumn;
                    DrawBorder(cellRect, selected ? AccentColor : BorderColor, selected ? 2f : 1f);
                    if (selected)
                    {
                        var badge = new Rect(cellRect.x + 2f, cellRect.y + 2f, 18f, 14f);
                        EditorGUI.DrawRect(badge, AccentColor);
                        GUI.Label(badge, "S", EditorStyles.miniLabel);
                    }
                    if (evt.type == EventType.MouseDown && evt.button == 0 &&
                        !_draggingPivot && !_staticScaling &&
                        cellRect.Contains(evt.mousePosition))
                    {
                        SetStaticCell(r, c);
                        evt.Use();
                        Repaint();
                    }
                }
            }

            GUI.EndScrollView();
        }

        bool ConfirmSharedSheetEdit()
        {
            // A new static image has no other animation users to warn about.
            int index = _profile.StaticSheetIndex;
            bool usedElsewhere = (_profile.Clips?.Exists(c => c != null && c.SheetIndex == index) ?? false) ||
                (_profile.PartsAppearances?.Exists(a => a != null && a.SheetIndex == index) ?? false) ||
                (_profile.SocketMotions?.Exists(m => m != null && m.ReferenceSheetIndex == index) ?? false);
            if (!usedElsewhere) return true;
            return EditorUtility.DisplayDialog("Edit Shared Sheet",
                "This changes the sheet used by Frames, Parts and Static. Existing cell references may show different art. Continue?",
                "Change Shared Sheet", "Cancel");
        }

        void SetupSceneObject(bool create)
        {
            if (!TryResolveTempPoseForSwitch()) return;
            if (!SpritePartsAuthoringOps.CanSetAnimKind(_profile, _profile.AnimKind, out string reason))
            { _status = reason; return; }
            SaveProfile();
            if (_asset == null) return;
            if (create)
                SpriteProfileSceneSetup.Create(_asset);
            else if (Selection.activeGameObject != null)
            {
                if (EditorUtility.DisplayDialog("Apply Runtime Mode",
                    "Configure the selected object for " + _profile.AnimKind +
                    ". Other animation authoring components will be removed. Gameplay and collider components are preserved. Undo is available.",
                    "Apply", "Cancel"))
                    SpriteProfileSceneSetup.Apply(Selection.activeGameObject, _asset, _profile.AnimKind);
            }
            else _status = "Select a scene object first.";
        }

        void DrawStaticTimeline(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.09f, 0.1f, 0.12f));
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 18f),
                "STATIC", _sectionStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 32f, rect.width - 24f, 36f),
                "Choose image  >  Whole image or sheet cell  >  Create Scene Object",
                _mutedStyle);
        }
    }
}
