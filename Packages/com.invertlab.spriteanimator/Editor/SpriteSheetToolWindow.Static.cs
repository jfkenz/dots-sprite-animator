using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    public sealed partial class SpriteSheetToolWindow
    {
        [SerializeField] float _staticCanvasZoom = 1f;
        [SerializeField] Vector2 _staticCanvasPan;
        bool _staticDragPivot;
        Vector2 _staticDragPivotStart;

        void EnsureStaticSession()
        {
            if (_profile == null)
                return;
            _profile.EnsureStaticSprite();
            _profile.EnsureSheets();
            var def = _profile.StaticSprite;
            def.SheetIndex = Mathf.Max(0, def.SheetIndex);
            if (_profile.Sheets.Count > 0)
                def.SheetIndex = Mathf.Clamp(def.SheetIndex, 0, _profile.Sheets.Count - 1);
            var sheet = _profile.SheetAt(def.SheetIndex);
            if (sheet != null)
            {
                int cols = Mathf.Max(1, sheet.Columns);
                int rows = Mathf.Max(1, sheet.Rows);
                def.Row = Mathf.Clamp(def.Row, 0, rows - 1);
                def.Column = Mathf.Clamp(def.Column, 0, cols - 1);
            }
            SpriteStaticAuthoringOps.ReadStaticPivotFromCell(_profile);
        }

        void DrawStaticBrowser(Rect rect)
        {
            EnsureStaticSession();
            GUILayout.BeginArea(rect);
            _partsBrowserScroll = EditorGUILayout.BeginScrollView(_partsBrowserScroll);
            GUILayout.Label("STATIC SPRITE", _sectionStyle);

            if (_profile.AnimKind != SpriteAnimKind.Static)
            {
                EditorGUILayout.HelpBox(
                    "Preview only. Runtime mode is not Static. Use the button below to bake this profile with Sprite Static Authoring.",
                    MessageType.Info);
                if (GUILayout.Button("Use Static for Character", GUILayout.Height(22f)))
                    SwitchRuntimeKind(SpriteAnimKind.Static, "Use Static for Character");
                GUILayout.Space(8f);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Runtime: Static. Place with Sprite Static Authoring in a SubScene.",
                    MessageType.None);
            }

            _profile.EnsureSheets();
            int sheetCount = _profile.Sheets?.Count ?? 0;
            GUILayout.Label($"{sheetCount} sheet{(sheetCount == 1 ? "" : "s")}", _mutedStyle);
            if (GUILayout.Button("Assign Sheet Texture…"))
                PromptAssignSheetTexture();

            GUILayout.Space(8f);
            if (GUILayout.Button("New Static Profile From Active Sheet"))
                CreateStaticFromActiveSheet();

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void CreateStaticFromActiveSheet()
        {
            RecordWindowUndo("New Static Profile");
            _profile.EnsureSheets(_selectedSheet);
            _profile.EnsureStaticSprite();
            var def = _profile.StaticSprite;
            def.SheetIndex = Mathf.Clamp(_selectedSheet, 0, Mathf.Max(0, _profile.Sheets.Count - 1));
            def.Row = 0;
            def.Column = 0;
            def.SizeUnits = 1f;
            def.DrawRank = 0;
            SpriteStaticAuthoringOps.ReadStaticPivotFromCell(_profile);
            var result = SpritePartsAuthoringOps.TrySetAnimKind(_profile, SpriteAnimKind.Static);
            if (!result.Ok)
                _status = result.Reason;
            else
            {
                SaveDirty();
                _studioTab = StudioTab.Static;
                _status = "Static sprite initialized on active sheet";
            }
        }

        void DrawStaticInspector(Rect rect)
        {
            EnsureStaticSession();
            GUILayout.BeginArea(rect);
            _partsInspectorScroll = EditorGUILayout.BeginScrollView(_partsInspectorScroll);
            GUILayout.Label("STATIC", _sectionStyle);
            var def = _profile.StaticSprite;
            if (def == null)
            {
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            EditorGUI.BeginChangeCheck();
            int sheetIndex = EditorGUILayout.IntField("Sheet Index", def.SheetIndex);
            int row = EditorGUILayout.IntField("Row", def.Row);
            int col = EditorGUILayout.IntField("Column", def.Column);
            var pivot = EditorGUILayout.Vector2Field("Pivot", def.Pivot);
            float size = EditorGUILayout.FloatField("Size (world units)", def.SizeUnits);
            int drawRank = EditorGUILayout.IntField(
                new GUIContent("Draw Rank",
                    "Used with Parts CharacterOrder: drawIndex = order×64 + rank."),
                def.DrawRank);
            var tint = EditorGUILayout.ColorField("Tint", def.Tint);
            if (EditorGUI.EndChangeCheck())
            {
                RecordWindowUndo("Edit Static Sprite");
                def.SheetIndex = Mathf.Max(0, sheetIndex);
                def.Row = Mathf.Max(0, row);
                def.Column = Mathf.Max(0, col);
                def.Pivot = new Vector2(Mathf.Clamp01(pivot.x), Mathf.Clamp01(pivot.y));
                def.SizeUnits = Mathf.Max(0.001f, size);
                def.DrawRank = Mathf.Clamp(drawRank, 0, SpritePartIdUtility.MaxParts - 1);
                def.Tint = tint;
                SpriteStaticAuthoringOps.SyncStaticPivotToCell(_profile);
                SaveDirty();
            }

            GUILayout.Space(8f);
            if (GUILayout.Button("Pick Cell From Sheet"))
                OpenStaticCellPicker();

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void OpenStaticCellPicker()
        {
            _showSheetCellPicker = true;
            _sheetCellPickerSelection.Clear();
            var def = _profile.StaticSprite;
            if (def != null)
            {
                _profile.EnsureSheets(def.SheetIndex);
                var sheet = _profile.SheetAt(def.SheetIndex);
                if (sheet != null)
                {
                    int cols = Mathf.Max(1, sheet.Columns);
                    int cell = Mathf.Clamp(def.Row, 0, sheet.Rows - 1) * cols
                               + Mathf.Clamp(def.Column, 0, cols - 1);
                    _sheetCellPickerSelection.Add(cell);
                }
            }
            _sheetCellPickerAnchor = _sheetCellPickerSelection.Count > 0
                ? _sheetCellPickerSelection[0]
                : -1;
            _sheetCellPickerScroll = Vector2.zero;
            _sheetCellPickerRect = new Rect(80f, 56f, 520f, 460f);
        }

        internal void ApplyStaticCellPickerSelection(int cell)
        {
            if (_profile?.StaticSprite == null)
                return;
            RecordWindowUndo("Pick Static Cell");
            var def = _profile.StaticSprite;
            _profile.EnsureSheets(def.SheetIndex);
            var sheet = _profile.SheetAt(def.SheetIndex);
            if (sheet == null)
                return;
            int cols = Mathf.Max(1, sheet.Columns);
            def.Row = cell / cols;
            def.Column = cell % cols;
            SpriteStaticAuthoringOps.ReadStaticPivotFromCell(_profile);
            SaveDirty();
            _status = $"Static cell R{def.Row} C{def.Column}";
        }

        void DrawStaticPreview(Rect rect, int controlId)
        {
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 20f), "STATIC PREVIEW", _sectionStyle);
            var canvas = new Rect(rect.x + 10f, rect.y + 40f, rect.width - 20f, rect.height - 52f);
            DrawCheckerboard(canvas, 18f);
            EditorGUI.DrawRect(new Rect(canvas.x, canvas.y, canvas.width, 1f), BorderColor);

            if (!TryResolveStaticSheet(out var texture, out var sheet, out int cols, out int rows))
            {
                GUI.Label(new Rect(canvas.x + 12f, canvas.y + 12f, canvas.width - 24f, 40f),
                    "Assign a sheet texture to preview the static sprite.", _mutedStyle);
                return;
            }

            var def = _profile.StaticSprite;
            int row = Mathf.Clamp(def.Row, 0, rows - 1);
            int col = Mathf.Clamp(def.Column, 0, cols - 1);
            float cellW = texture.width / (float)cols;
            float cellH = texture.height / (float)rows;
            float aspect = cellW / Mathf.Max(1f, cellH);
            float displayH = Mathf.Min(canvas.height * 0.75f, 320f) * _staticCanvasZoom;
            float displayW = displayH * aspect;
            var cellRect = new Rect(
                canvas.x + (canvas.width - displayW) * 0.5f + _staticCanvasPan.x,
                canvas.y + (canvas.height - displayH) * 0.5f + _staticCanvasPan.y,
                displayW,
                displayH);

            var uv = new Rect(col * cellW / texture.width, 1f - (row + 1) * cellH / texture.height,
                cellW / texture.width, cellH / texture.height);
            GUI.DrawTextureWithTexCoords(cellRect, texture, uv, true);

            Vector2 pivotPx = new Vector2(
                cellRect.x + def.Pivot.x * cellRect.width,
                cellRect.y + (1f - def.Pivot.y) * cellRect.height);
            const float handle = 8f;
            var pivotHandle = new Rect(pivotPx.x - handle, pivotPx.y - handle, handle * 2f, handle * 2f);
            EditorGUI.DrawRect(pivotHandle, new Color(0.2f, 0.85f, 0.35f, 1f));

            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && pivotHandle.Contains(evt.mousePosition))
            {
                _staticDragPivot = true;
                _staticDragPivotStart = pivotPx;
                evt.Use();
            }
            if (_staticDragPivot && evt.type == EventType.MouseDrag)
            {
                Vector2 local = evt.mousePosition;
                def.Pivot = new Vector2(
                    Mathf.Clamp01((local.x - cellRect.x) / Mathf.Max(1f, cellRect.width)),
                    Mathf.Clamp01(1f - (local.y - cellRect.y) / Mathf.Max(1f, cellRect.height)));
                SpriteStaticAuthoringOps.SyncStaticPivotToCell(_profile);
                SaveDirty();
                evt.Use();
                Repaint();
            }
            if (evt.type == EventType.MouseUp && _staticDragPivot)
            {
                _staticDragPivot = false;
                RecordWindowUndo("Move Static Pivot");
                evt.Use();
            }

            if (evt.type == EventType.ScrollWheel && canvas.Contains(evt.mousePosition))
            {
                _staticCanvasZoom = Mathf.Clamp(_staticCanvasZoom - evt.delta.y * 0.05f, 0.25f, 8f);
                evt.Use();
                Repaint();
            }
        }

        void DrawStaticTimeline(Rect rect)
        {
            GUI.Label(new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, 20f),
                "Static sprites have no timeline.", _mutedStyle);
        }

        bool TryResolveStaticSheet(out Texture2D texture, out SpriteSheetDef sheet, out int cols, out int rows)
        {
            texture = null;
            sheet = null;
            cols = rows = 1;
            if (_profile == null)
                return false;
            _profile.EnsureStaticSprite();
            _profile.EnsureSheets(_profile.StaticSprite.SheetIndex);
            sheet = _profile.SheetAt(_profile.StaticSprite.SheetIndex);
            if (sheet == null)
                return false;
            texture = sheet.Texture != null ? sheet.Texture : _profile.Sheet;
            if (texture == null)
                return false;
            cols = Mathf.Max(1, sheet.Columns);
            rows = Mathf.Max(1, sheet.Rows);
            return true;
        }

        void PromptAssignSheetTexture()
        {
            string path = EditorUtility.OpenFilePanel(
                "Assign sheet texture", Application.dataPath, "png,jpg,jpeg");
            if (string.IsNullOrEmpty(path))
                return;
            if (path.StartsWith(Application.dataPath))
                path = "Assets" + path.Substring(Application.dataPath.Length);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                _status = "Could not load texture from project Assets";
                return;
            }
            ApplySheetTexture(texture);
            _status = $"Assigned sheet {texture.name}";
        }

        void DrawPartsLinkedStaticProfile()
        {
            if (_profile == null)
                return;
            GUILayout.Label("LINKED STATIC BODY", _sectionStyle);
#if UNITY_EDITOR
            var linked = SpriteProfileLinkOps.LoadByGuid(_profile.LinkedStaticProfileGuid);
            EditorGUI.BeginChangeCheck();
            var next = (ScriptableSpriteSheetProfile)EditorGUILayout.ObjectField(
                "Static Profile", linked, typeof(ScriptableSpriteSheetProfile), false);
            if (EditorGUI.EndChangeCheck())
            {
                RecordPartsUndo("Linked Static Profile");
                _profile.LinkedStaticProfileGuid = SpriteProfileLinkOps.GuidFor(next);
                SaveDirty();
            }
            if (linked?.Data != null)
            {
                linked.Data.EnsureStaticSprite();
                EditorGUILayout.LabelField("Body draw rank", linked.Data.StaticSprite.DrawRank.ToString());
            }
            EditorGUILayout.HelpBox(
                "Shared sort space: drawIndex = Parts CharacterOrder×64 + static Draw Rank. " +
                "Point Sprite Static Authoring.Parts Sort Anchor at the feet Parts character.",
                MessageType.None);
#endif
            GUILayout.Space(6f);
        }

        void StashWorkspaceCamera(StudioTab fromTab)
        {
            if (fromTab == StudioTab.Clips)
            {
                _framesPreviewZoom = _previewZoom;
                _framesPreviewPan = _previewPan;
            }
            else if (fromTab == StudioTab.Parts)
            {
                _partsCanvasZoom = _previewZoom;
                _partsCanvasPan = _previewPan;
            }
            else if (fromTab == StudioTab.Static)
            {
                _staticCanvasZoom = _previewZoom;
                _staticCanvasPan = _previewPan;
            }
        }

        void RestoreWorkspaceCamera(StudioTab toTab)
        {
            if (toTab == StudioTab.Clips)
            {
                _previewZoom = _framesPreviewZoom;
                _previewPan = _framesPreviewPan;
            }
            else if (toTab == StudioTab.Parts)
            {
                _previewZoom = _partsCanvasZoom;
                _previewPan = _partsCanvasPan;
            }
            else if (toTab == StudioTab.Static)
            {
                _previewZoom = _staticCanvasZoom;
                _previewPan = _staticCanvasPan;
            }
        }
    }
}
