using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>
    /// From-This-Profile art picker for one Part slot: existing appearances,
    /// sheet cells, and static frames of frame clips. A frame clip supplies one
    /// chosen cell — "Use This Frame" — and never implies the clip is playing.
    /// </summary>
    sealed class SpritePartsArtPickerPopup : PopupWindowContent
    {
        readonly SpriteSheetToolWindow _host;
        readonly string _slotId;
        Vector2 _scroll;
        bool _batchMode;
        int _batchSheetIndex = -1;
        readonly List<int> _batchCells = new();

        public SpritePartsArtPickerPopup(SpriteSheetToolWindow host, string slotId)
        {
            _host = host;
            _slotId = slotId;
        }

        public override Vector2 GetWindowSize() => new Vector2(360f, 420f);

        public override void OnGUI(Rect rect)
        {
            var profile = _host.EditingProfile;
            if (profile == null)
            {
                editorWindow.Close();
                return;
            }

            var inner = new Rect(8f, 8f, rect.width - 16f, rect.height - 16f);
            GUILayout.BeginArea(inner);
            GUILayout.Label("ART IN THIS PROFILE", EditorStyles.boldLabel);
            GUILayout.Space(4f);

            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("APPEARANCES", EditorStyles.miniBoldLabel);
            bool anyAppearance = false;
            var appearances = profile.PartsAppearances ?? new List<SpritePartAppearanceDef>();
            for (int i = 0; i < appearances.Count; i++)
            {
                var app = appearances[i];
                if (app == null)
                    continue;
                var sheet = profile.SheetAt(app.SheetIndex);
                if (sheet?.Texture == null)
                    continue;
                anyAppearance = true;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(
                    $"{app.Name}  ·  '{SpritePartIdUtility.Canonical(app.AppearanceId, app.Name)}'  ·  {sheet.Name} cell {app.CellIndex}",
                    EditorStyles.miniLabel,
                    GUILayout.ExpandWidth(true), GUILayout.MinWidth(80f));
                if (GUILayout.Button("Bind", GUILayout.Width(70f)))
                {
                    _host.BindSlotAppearanceFromPicker(_slotId, app.AppearanceId);
                    editorWindow.Close();
                    return;
                }
                EditorGUILayout.EndHorizontal();
            }
            if (!anyAppearance)
                GUILayout.Label("No appearances yet. Assign or import art first.", EditorStyles.miniLabel);

            GUILayout.Space(8f);
            GUILayout.Label("SHEET CELLS", EditorStyles.miniBoldLabel);
            DrawBatchBar();
            bool anySheet = false;
            var sheets = profile.Sheets;
            if (sheets != null)
            {
                for (int s = 0; s < sheets.Count; s++)
                    anySheet |= DrawSheetCellGrid(profile, s);
            }
            if (!anySheet)
                GUILayout.Label("No textured sheets in this profile.", EditorStyles.miniLabel);

            GUILayout.Space(8f);
            GUILayout.Label("FRAME CLIPS (STATIC FRAME)", EditorStyles.miniBoldLabel);
            bool anyClip = false;
            var clips = profile.Clips ?? new List<SpriteClipDef>();
            for (int c = 0; c < clips.Count; c++)
                anyClip |= DrawFrameClipRow(profile, clips[c]);
            if (!anyClip)
                GUILayout.Label("No frame clips with a textured sheet.", EditorStyles.miniLabel);

            GUILayout.EndScrollView();
            GUILayout.Space(4f);
            if (GUILayout.Button("Cancel"))
                editorWindow.Close();
            GUILayout.EndArea();
        }

        void DrawBatchBar()
        {
            EditorGUILayout.BeginHorizontal();
            bool nextBatch = GUILayout.Toggle(_batchMode, "Batch select cells", "Button",
                GUILayout.Width(130f));
            if (nextBatch != _batchMode)
            {
                _batchMode = nextBatch;
                _batchCells.Clear();
                _batchSheetIndex = -1;
            }
            if (_batchMode)
            {
                GUILayout.Label($"{_batchCells.Count} selected", EditorStyles.miniLabel, GUILayout.Width(70f));
                using (new EditorGUI.DisabledScope(_batchCells.Count == 0))
                {
                    if (GUILayout.Button($"Create {_batchCells.Count} Appearance(s)", EditorStyles.miniButton))
                    {
                        _host.CreateAppearancesFromPicker(_slotId, _batchSheetIndex,
                            new List<int>(_batchCells), bindLast: false);
                        _batchCells.Clear();
                    }
                    if (GUILayout.Button("Create + Bind Last", EditorStyles.miniButton))
                    {
                        _host.CreateAppearancesFromPicker(_slotId, _batchSheetIndex,
                            new List<int>(_batchCells), bindLast: true);
                        _batchCells.Clear();
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
            if (_batchMode)
                GUILayout.Label(
                    "Batch: click cells to toggle selection on one sheet. Create makes one appearance per cell (exact sheet+cell matches are reused, ids are unique). Create + Bind Last also sets the last selected cell as this slot's default art.",
                    EditorStyles.wordWrappedMiniLabel);
        }

        bool DrawSheetCellGrid(SpriteSheetProfile profile, int sheetIndex)
        {
            var sheet = profile.Sheets[sheetIndex];
            if (sheet?.Texture == null)
                return false;
            int columns = Mathf.Max(1, sheet.Columns);
            int rows = Mathf.Max(1, sheet.Rows);
            int cells = columns * rows;
            const int perRow = 8;
            const float cellSize = 30f;

            GUILayout.Label($"{sheet.Name} ({columns}×{rows}, {sheet.PixelsPerUnit} PPU)", EditorStyles.miniLabel);
            int fullRows = (cells + perRow - 1) / perRow;
            for (int r = 0; r < fullRows; r++)
            {
                EditorGUILayout.BeginHorizontal();
                for (int c = 0; c < perRow; c++)
                {
                    int cell = r * perRow + c;
                    if (cell >= cells)
                    {
                        GUILayout.Space(cellSize + 4f);
                        continue;
                    }
                    var buttonRect = GUILayoutUtility.GetRect(cellSize, cellSize, GUILayout.Width(cellSize));
                    bool batchSelected = _batchMode && _batchSheetIndex == sheetIndex &&
                                         _batchCells.Contains(cell);
                    if (DrawCellButton(buttonRect, sheet, cell, batchSelected))
                    {
                        if (_batchMode)
                        {
                            if (_batchSheetIndex != sheetIndex)
                            {
                                _batchSheetIndex = sheetIndex;
                                _batchCells.Clear();
                            }
                            if (!_batchCells.Remove(cell))
                                _batchCells.Add(cell);
                        }
                        else
                        {
                            _host.BindSlotArtCellFromPicker(_slotId, sheetIndex, cell,
                                $"Bound {sheet.Name} cell {cell}");
                            editorWindow.Close();
                            return true;
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            return true;
        }

        bool DrawCellButton(Rect rect, SpriteSheetDef sheet, int cell, bool selected = false)
        {
            bool clicked = GUI.Button(rect, GUIContent.none);
            if (selected)
                EditorGUI.DrawRect(rect, new Color(0.25f, 0.65f, 0.3f, 0.55f));
            Rect uv = SpriteSheetProfile.GetCellUvRect(sheet, cell);
            GUI.DrawTextureWithTexCoords(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f),
                sheet.Texture, uv, true);
            return clicked;
        }

        bool DrawFrameClipRow(SpriteSheetProfile profile, SpriteClipDef clip)
        {
            if (clip == null || clip.Frames == null || clip.Frames.Length == 0)
                return false;
            var sheet = profile.SheetForClip(clip);
            if (sheet == null || sheet.Texture == null)
                return false;
            int columns = Mathf.Max(1, sheet.Columns);
            int rows = Mathf.Max(1, sheet.Rows);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent($"{clip.Name} ({clip.Frames.Length} frames)",
                    "Uses one static frame's sheet cell as this part's art. The clip itself is not played."),
                EditorStyles.miniLabel,
                GUILayout.ExpandWidth(true), GUILayout.MinWidth(60f));
            for (int f = 0; f < clip.Frames.Length; f++)
            {
                int cell = clip.SheetCellIndex(f, columns, rows);
                if (GUILayout.Button(new GUIContent($"F{f}",
                        $"Use This Frame: {clip.Name} frame {f} (sheet cell {cell})."),
                        EditorStyles.miniButton, GUILayout.Width(30f)))
                {
                    _host.BindSlotArtCellFromPicker(_slotId, clip.SheetIndex, cell,
                        $"Used frame {f} of clip '{clip.Name}' (static, clip not played)");
                    editorWindow.Close();
                    return true;
                }
            }
            EditorGUILayout.EndHorizontal();
            return true;
        }
    }
}
