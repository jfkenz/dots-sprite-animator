using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    internal enum SpriteSheetSliceType
    {
        GridByCellCount,
        GridByCellSize,
    }

    /// <summary>Everything the Slice action needs, collected by the popup.</summary>
    internal struct SpriteSheetSliceRequest
    {
        public SpriteSheetSliceType Type;
        public Vector2Int CellSize;      // GridByCellSize
        public int Columns;              // GridByCellCount
        public int Rows;                 // GridByCellCount
        public Vector2Int Offset;
        public Vector2Int Padding;
        public bool KeepEmptyRects;
        public Vector2 PivotNormalized;  // already converted (0-1)
    }

    /// <summary>
    /// Unity Sprite Editor-style slice dialog: Automatic / Grid By Cell Size /
    /// Grid By Cell Count, with offset, padding, empty-rect handling, pivot
    /// presets and unit mode. Slicing writes the current sheet's grid +
    /// cropped cell rects in the profile (never Unity .meta data).
    /// </summary>
    internal sealed class SpriteSheetSlicePopup : PopupWindowContent
    {
        enum PivotPreset
        {
            Center, Top, Bottom, Left, Right,
            TopLeft, TopRight, BottomLeft, BottomRight, Custom,
        }

        enum PivotUnitMode { Normalized, Pixels }

        static readonly GUIContent TypeLabel = new("Type");
        static readonly GUIContent MethodLabel = new("Method");

        readonly ISpriteSheetSliceHost _window;
        SpriteSheetSliceType _type = SpriteSheetSliceType.GridByCellCount;
        Vector2Int _cellSize = new(64, 64);
        int _columns = 4, _rows = 4;
        Vector2Int _offset;
        Vector2Int _padding;
        bool _keepEmptyRects;
        PivotPreset _pivotPreset = PivotPreset.Center;
        PivotUnitMode _pivotUnit = PivotUnitMode.Normalized;
        Vector2 _customPivot = new(0.5f, 0.5f);
        float _pivotX = 0.5f, _pivotY = 0.5f;

        public SpriteSheetSlicePopup(ISpriteSheetSliceHost window)
        {
            _window = window;
        }

        public override Vector2 GetWindowSize() => new(340f, 440f);

        public override void OnGUI(Rect rect)
        {
            GUILayout.Space(6f);

            // ---- type ----
            EditorGUILayout.LabelField(TypeLabel, EditorStyles.miniBoldLabel);
            _type = (SpriteSheetSliceType)EditorGUILayout.EnumPopup(
                _type, GUILayout.Width(150f));

            EditorGUILayout.Space(2f);
            if (_type == SpriteSheetSliceType.GridByCellSize)
            {
                _cellSize = IntVectorField("Pixel Size", _cellSize);
                _offset = IntVectorField("Offset", _offset);
                _padding = IntVectorField("Padding", _padding);
                _keepEmptyRects = EditorGUILayout.Toggle("Keep Empty Rects", _keepEmptyRects);
            }
            else
            {
                EditorGUILayout.LabelField("Column & Row", EditorStyles.label);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("C", GUILayout.Width(16f));
                    _columns = Mathf.Max(1, EditorGUILayout.IntField(_columns));
                    GUILayout.Space(8f);
                    EditorGUILayout.LabelField("R", GUILayout.Width(16f));
                    _rows = Mathf.Max(1, EditorGUILayout.IntField(_rows));
                }
                _offset = IntVectorField("Offset", _offset);
                _padding = IntVectorField("Padding", _padding);
                _keepEmptyRects = EditorGUILayout.Toggle("Keep Empty Rects", _keepEmptyRects);
            }

            // ---- pivot ----
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Pivot", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            _pivotPreset = (PivotPreset)EditorGUILayout.EnumPopup(_pivotPreset, GUILayout.Width(150f));
            if (EditorGUI.EndChangeCheck() && _pivotPreset != PivotPreset.Custom)
                (_pivotX, _pivotY) = PresetVector(_pivotPreset);

            EditorGUILayout.LabelField("Pivot Unit Mode", EditorStyles.label);
            _pivotUnit = (PivotUnitMode)EditorGUILayout.EnumPopup(_pivotUnit, GUILayout.Width(150f));

            using (new EditorGUI.DisabledScope(_pivotPreset != PivotPreset.Custom))
            {
                EditorGUILayout.LabelField("Custom Pivot", EditorStyles.label);
                if (_pivotUnit == PivotUnitMode.Pixels)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _pivotX = EditorGUILayout.FloatField("X", _pivotX);
                        _pivotY = EditorGUILayout.FloatField("Y", _pivotY);
                    }
                }
                else
                {
                    _customPivot = EditorGUILayout.Vector2Field(string.Empty, _customPivot);
                    _pivotX = _customPivot.x;
                    _pivotY = _customPivot.y;
                }
            }

            // ---- method ----
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(MethodLabel, EditorStyles.miniBoldLabel);
            EditorGUILayout.Popup(0, new[] { "Delete Existing" }, GUILayout.Width(150f));
            EditorGUILayout.HelpBox(
                "Delete Existing removes all existing Sprites and recreates them from scratch.",
                MessageType.Info);

            GUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Slice", GUILayout.Width(140f), GUILayout.Height(22f)))
                {
                    if (TryBuildRequest(out var request))
                    {
                        _window.RunSheetSlice(request);
                        editorWindow.Close();
                    }
                }
                GUILayout.Space(8f);
            }
            GUILayout.Space(4f);
        }

        static Vector2Int IntVectorField(string label, Vector2Int value)
        {
            EditorGUILayout.LabelField(label, EditorStyles.label);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("X", GUILayout.Width(16f));
                value.x = Mathf.Max(0, EditorGUILayout.IntField(value.x));
                GUILayout.Space(8f);
                EditorGUILayout.LabelField("Y", GUILayout.Width(16f));
                value.y = Mathf.Max(0, EditorGUILayout.IntField(value.y));
            }
            return value;
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

        bool TryBuildRequest(out SpriteSheetSliceRequest request)
        {
            request = new SpriteSheetSliceRequest
            {
                Type = _type,
                CellSize = new Vector2Int(Mathf.Max(1, _cellSize.x), Mathf.Max(1, _cellSize.y)),
                Columns = Mathf.Max(1, _columns),
                Rows = Mathf.Max(1, _rows),
                Offset = _offset,
                Padding = _padding,
                KeepEmptyRects = _keepEmptyRects,
            };

            if (_pivotPreset == PivotPreset.Custom && _pivotUnit == PivotUnitMode.Pixels)
            {
                if (!_window.TryGetSliceCellMetrics(request,
                        out int texW, out int texH, out int cellW, out int cellH) ||
                    cellW < 1 || cellH < 1)
                {
                    editorWindow.Close();
                    return false;
                }
                // Unity pixel-pivot convention: from the sprite's bottom-left
                request.PivotNormalized = new Vector2(
                    cellW > 0 ? Mathf.Clamp01(_pivotX / cellW) : 0.5f,
                    cellH > 0 ? Mathf.Clamp01(_pivotY / cellH) : 0.5f);
            }
            else
            {
                request.PivotNormalized = new Vector2(
                    Mathf.Clamp01(_pivotX), Mathf.Clamp01(_pivotY));
            }

            if (_type == SpriteSheetSliceType.GridByCellSize)
            {
                var tex = _window.SliceTargetTexture;
                if (tex == null)
                    return false;
                request.Columns = Mathf.Max(1,
                    (tex.width - request.Offset.x + request.Padding.x) /
                    Mathf.Max(1, request.CellSize.x + request.Padding.x));
                request.Rows = Mathf.Max(1,
                    (tex.height - request.Offset.y + request.Padding.y) /
                    Mathf.Max(1, request.CellSize.y + request.Padding.y));
            }

            return true;
        }
    }
}
