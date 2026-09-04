using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    [CustomEditor(typeof(SpriteStaticAuthoring))]
    [CanEditMultipleObjects]
    public sealed class SpriteStaticAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // everything except Pivot (drawn last, gated by Override Pivot)
            UnityEditor.Editor.DrawPropertiesExcluding(serializedObject, "m_Script", "Pivot");
            var overrideProp = serializedObject.FindProperty("OverridePivot");
            var pivotProp = serializedObject.FindProperty("Pivot");

            // enabling the override seeds the field with the profile pivot so
            // the sprite does not jump the moment you check it
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(overrideProp);
            if (EditorGUI.EndChangeCheck() && overrideProp.boolValue)
            {
                var source = (SpriteStaticAuthoring)target;
                var data = source.Profile != null ? source.Profile.Data : null;
                if (data != null)
                {
                    data.EnsureSheets();
                    var sheetDef = data.SheetAt(Mathf.Max(0, source.SheetIndex));
                    if (sheetDef != null)
                    {
                        var profilePivot = SpriteSocketWorld.ResolvePivot(data, sheetDef);
                        pivotProp.vector2Value = profilePivot;
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!overrideProp.boolValue))
            {
                EditorGUILayout.PropertyField(pivotProp);
            }
            serializedObject.ApplyModifiedProperties();

            var authoring = (SpriteStaticAuthoring)target;
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(authoring.Profile == null))
            {
                if (GUILayout.Button(new GUIContent("Pick Cell From Sheet",
                        "Open the sheet with its grid overlay; click a cell to set Row/Column.")))
                    SpriteStaticCellPicker.Show(authoring);
            }

            if (authoring.Profile == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a Profile (Window > DOTS Sprite Animator). Profiles without " +
                    "clips are fine — the sheet grid is all this component needs.",
                    MessageType.Info);
                return;
            }

            if (!authoring.ResolveSheet(out _, out _, out _, out _))
            {
                EditorGUILayout.HelpBox(
                    "Profile has no usable sheet texture on this sheet index.",
                    MessageType.Warning);
            }
        }
    }

    /// <summary>
    /// Cell picker for SpriteStaticAuthoring, styled after the sheet tool's
    /// "1×1 from texture" picker: a tile grid (checkerboard + per-cell
    /// texture, 2px gaps), accent border + slot badge on the selection, and
    /// a muted footer. Click a cell to write Row/Column (undo-able).
    /// </summary>
    sealed class SpriteStaticCellPicker : EditorWindow
    {
        static readonly Color Accent = new Color(0.25f, 0.75f, 1f, 1f);
        static readonly Color CellBorder = new Color(1f, 1f, 1f, 0.22f);
        static readonly Color CheckerA = new Color(0.28f, 0.28f, 0.28f, 1f);
        static readonly Color CheckerB = new Color(0.42f, 0.42f, 0.42f, 1f);

        SpriteStaticAuthoring _target;
        Vector2 _scroll;

        public static void Show(SpriteStaticAuthoring target)
        {
            var window = CreateInstance<SpriteStaticCellPicker>();
            window._target = target;
            window.titleContent = new GUIContent("Pick Cell");
            window.minSize = new Vector2(360f, 300f);
            window.ShowUtility();
        }

        void OnGUI()
        {
            if (_target == null)
            {
                Close();
                return;
            }

            Texture2D texture;
            int cols, rows;

            // ---- toolbar ----
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Sheet", EditorStyles.miniLabel, GUILayout.Width(34f));
                if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(26f)))
                    ShiftSheet(-1);
                if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(26f)))
                    ShiftSheet(+1);

                if (_target.ResolveSheet(out texture, out cols, out rows, out _))
                    GUILayout.Label(
                        texture != null ? $"{texture.name} ({cols}×{rows})" : "no texture",
                        EditorStyles.miniLabel);

                GUILayout.FlexibleSpace();
            }

            if (!_target.ResolveSheet(out texture, out cols, out rows, out _))
            {
                EditorGUILayout.HelpBox("No sheet texture on this SheetIndex.", MessageType.Warning);
                return;
            }

            var evt = Event.current;
            int selRow = Mathf.Clamp(_target.Row, 0, rows - 1);
            int selCol = Mathf.Clamp(_target.Column, 0, cols - 1);

            // ---- tile grid (like the tool's 1×1 picker) ----
            const float gap = 2f;
            float cellAspect = (texture.width / (float)cols) / Mathf.Max(1f, texture.height / (float)rows);
            cellAspect = Mathf.Max(0.01f, cellAspect);

            var viewport = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            float cellW = Mathf.Clamp((viewport.width - 18f - gap * (cols - 1)) / cols, 28f, 96f);
            float cellH = cellW / cellAspect;
            float contentW = cols * cellW + gap * (cols - 1);
            float contentH = rows * cellH + gap * (rows - 1);
            _scroll = GUI.BeginScrollView(viewport, _scroll,
                new Rect(0f, 0f, Mathf.Max(contentW, viewport.width - 18f),
                    Mathf.Max(contentH, viewport.height - 18f)));

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int cell = r * cols + c;
                    var cellRect = new Rect(c * (cellW + gap), r * (cellH + gap), cellW, cellH);
                    bool selected = r == selRow && c == selCol;

                    DrawCheckerboard(cellRect, 8f);
                    var uv = new Rect(c / (float)cols, 1f - (r + 1) / (float)rows,
                        1f / cols, 1f / rows);
                    GUI.DrawTextureWithTexCoords(FitAspectRect(cellRect, cellAspect), texture, uv, true);

                    if (!selected && cellRect.Contains(evt.mousePosition))
                        EditorGUI.DrawRect(cellRect, new Color(0.3f, 0.7f, 1f, 0.18f));
                    DrawBorder(cellRect, selected ? Accent : CellBorder, selected ? 2f : 1f);

                    if (selected)
                    {
                        var badge = new Rect(cellRect.x + 2f, cellRect.y + 2f, 22f, 14f);
                        EditorGUI.DrawRect(badge, Accent);
                        GUI.Label(badge, cell.ToString(), EditorStyles.miniLabel);
                    }

                    if (evt.type == EventType.MouseDown && evt.button == 0 &&
                        cellRect.Contains(evt.mousePosition))
                    {
                        Undo.RecordObject(_target, "Pick Sprite Cell");
                        _target.Row = r;
                        _target.Column = c;
                        EditorUtility.SetDirty(_target);
                        evt.Use();
                        Repaint();
                    }
                }
            }
            GUI.EndScrollView();

            // ---- footer ----
            EditorGUILayout.Space(2f);
            GUILayout.Label(
                $"row {selRow} · col {selCol} → slot {selRow * cols + selCol}" +
                "   •   click = pick   •   Esc = close",
                EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Close", GUILayout.Width(72f)))
                    Close();
            }

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                Close();
                evt.Use();
            }
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

        static Rect FitAspectRect(Rect rect, float aspect)
        {
            float w = rect.width;
            float h = w / aspect;
            if (h > rect.height)
            {
                h = rect.height;
                w = h * aspect;
            }
            return new Rect(rect.x + (rect.width - w) * 0.5f, rect.y + (rect.height - h) * 0.5f, w, h);
        }

        static void DrawBorder(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        void ShiftSheet(int delta)
        {
            Undo.RecordObject(_target, "Switch Sheet");
            _target.SheetIndex = Mathf.Max(0, _target.SheetIndex + delta);
            EditorUtility.SetDirty(_target);
            _scroll = Vector2.zero;
        }
    }
}
