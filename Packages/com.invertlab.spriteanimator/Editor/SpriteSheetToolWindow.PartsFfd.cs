using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // FFD (Free Form Deformation, as in AnyPortrait) for the Warp tool: a grid of control points around
    // the selected vertices (all of them when fewer than two are selected). Dragging a control point bends
    // every vertex inside the grid; each vertex follows the grid cell it sits in (bilinear).
    // The result is an ordinary deform key. Apply keeps it, Cancel puts the key back as it was.
    public sealed partial class SpriteSheetToolWindow
    {
        [SerializeField] int _partsFfdCols = 3;
        [SerializeField] int _partsFfdRows = 3;
        bool _partsFfdOn;
        string _partsFfdSlotId;
        float _partsFfdTime;
        SpritePartsAuthoringOps.PoseEdit _partsFfdStartPose;
        SpritePartsLattice _partsFfdShownStart;
        float2x2[] _partsFfdSkinInverse;
        float2 _partsFfdSkinSize;
        // Grid in the part's lattice space (x, y in -0.5..0.5 over the image), row-major from bottom-left.
        float2[] _partsFfdRest;
        float2[] _partsFfdCtrl;
        int[] _partsFfdVerts;
        float2[] _partsFfdParam;
        int _partsFfdDrag = -1;
        Vector2 _partsFfdDragMouse;
        float2 _partsFfdDragFrom;

        const float PartsFfdHitRadius = 8f;

        bool PartsFfdActive()
            => _partsFfdOn && _partsCanvasTool == PartsCanvasTool.Warp && _partsMode == SpritePartsStudioMode.Animate
               && CurrentPartsSlot != null
               && SpritePartIdUtility.Canonical(CurrentPartsSlot.SlotId) == _partsFfdSlotId
               && Mathf.Approximately(_partsFfdTime, _partsPreviewTime);

        /// <summary>Starts FFD on the current part: a grid around the selected vertices, or the whole mesh.</summary>
        void BeginPartsFfd()
        {
            var slot = CurrentPartsSlot;
            if (slot == null || _partsMode != SpritePartsStudioMode.Animate)
            {
                _status = "FFD works in Animate with a part selected.";
                return;
            }
            if (slot.Mesh == null || !slot.Mesh.HasMesh)
            {
                _status = "FFD needs a mesh. Double-click the part to make one.";
                return;
            }
            string id = SpritePartIdUtility.Canonical(slot.SlotId);
            var pose = SampleLocalPoseForSlot(id, _partsPreviewTime);
            if (!pose.Lattice.HasMesh)
                return;
            if (!TrySampleWarpLattices(id, out _, out var shown, out var inverse, out var size) || shown.PointCount != pose.Lattice.PointCount)
            {
                shown = pose.Lattice;
                inverse = null;
                size = default;
            }
            SetWarpSelectionSlot(id);
            var verts = new List<int>();
            foreach (int s in _partsWarpSelection)
            {
                if ((uint)s < (uint)shown.PointCount)
                    verts.Add(s);
            }
            if (verts.Count < 2)
            {
                verts.Clear();
                for (int i = 0; i < shown.PointCount; i++)
                    verts.Add(i);
            }
            float2 min = shown.GetPoint(verts[0]), max = min;
            foreach (int v in verts)
            {
                min = math.min(min, shown.GetPoint(v));
                max = math.max(max, shown.GetPoint(v));
            }
            // A little margin so edge vertices sit inside the grid, and never a zero-size box.
            float2 pad = math.max((max - min) * 0.06f, new float2(0.02f));
            min -= pad;
            max += pad;

            int cols = Mathf.Clamp(_partsFfdCols, 2, 6);
            int rows = Mathf.Clamp(_partsFfdRows, 2, 6);
            _partsFfdRest = new float2[cols * rows];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                    _partsFfdRest[r * cols + c] = math.lerp(min, max, new float2(c / (float)(cols - 1), r / (float)(rows - 1)));
            }
            _partsFfdCtrl = (float2[])_partsFfdRest.Clone();
            _partsFfdVerts = verts.ToArray();
            _partsFfdParam = new float2[verts.Count];
            for (int k = 0; k < verts.Count; k++)
                _partsFfdParam[k] = (shown.GetPoint(verts[k]) - min) / (max - min);
            _partsFfdCols = cols;
            _partsFfdRows = rows;
            _partsFfdSlotId = id;
            _partsFfdTime = _partsPreviewTime;
            _partsFfdStartPose = pose;
            _partsFfdShownStart = shown;
            _partsFfdSkinInverse = inverse;
            _partsFfdSkinSize = size;
            _partsFfdDrag = -1;
            _partsFfdOn = true;
            _status = "FFD: drag the white points to bend " + verts.Count + " vertices. Enter applies, Esc cancels.";
            Repaint();
        }

        void ApplyPartsFfd()
        {
            _partsFfdOn = false;
            _partsFfdDrag = -1;
            _status = "FFD applied.";
            Repaint();
        }

        void CancelPartsFfd()
        {
            if (_partsFfdOn && !string.IsNullOrEmpty(_partsFfdSlotId))
            {
                RecordPartsUndo("Cancel FFD");
                ApplyPartsPoseEdit(_partsFfdSlotId, _partsFfdStartPose);
            }
            _partsFfdOn = false;
            _partsFfdDrag = -1;
            _status = "FFD cancelled.";
            Repaint();
        }

        /// <summary>Where a lattice-space point is on screen, for the part being deformed.</summary>
        bool TryPartsFfdScreen(Rect canvas, out System.Func<float2, Vector2> toScreen, out System.Func<Vector2, float2> toLattice)
        {
            toScreen = null;
            toLattice = null;
            if (!TryGetPartsWarpLayout(canvas, _partsFfdSlotId, out var rect, out var joint, out float guiDeg,
                    out bool flipX, out bool flipY))
                return false;
            toScreen = p => PartsWarpPointGui(rect, joint, guiDeg, flipX, flipY, p);
            toLattice = m =>
            {
                Vector2 q = UnflipAround(UnrotateAround(m, joint, guiDeg), joint, flipX, flipY);
                return new float2(
                    (q.x - rect.xMin) / Mathf.Max(1f, rect.width) - 0.5f,
                    (rect.yMax - q.y) / Mathf.Max(1f, rect.height) - 0.5f);
            };
            return true;
        }

        /// <summary>Mouse down in Warp while FFD is on. True when a control point was grabbed.</summary>
        bool TryBeginPartsFfdDrag(Rect canvas, Vector2 mouse, int controlId)
        {
            if (!PartsFfdActive() || !TryPartsFfdScreen(canvas, out var toScreen, out _))
                return false;
            int best = -1;
            float bestD = PartsFfdHitRadius * PartsFfdHitRadius;
            for (int i = 0; i < _partsFfdCtrl.Length; i++)
            {
                float d = (mouse - toScreen(_partsFfdCtrl[i])).sqrMagnitude;
                if (d > bestD)
                    continue;
                bestD = d;
                best = i;
            }
            if (best < 0)
                return false;
            BeginPartsDragUndo("FFD");
            _partsFfdDrag = best;
            _partsFfdDragMouse = mouse;
            _partsFfdDragFrom = _partsFfdCtrl[best];
            _partsDragActive = true;
            _partsCanvasHotControl = controlId;
            GUIUtility.hotControl = controlId;
            _partsDragSlotId = _partsFfdSlotId;
            _partsWarpActive = true; // routes MouseDrag to ApplyPartsWarpDrag -> ApplyPartsFfdDrag
            return true;
        }

        void ApplyPartsFfdDrag(Rect canvas, Vector2 mouse)
        {
            if (_partsFfdDrag < 0 || !TryPartsFfdScreen(canvas, out _, out var toLattice))
                return;
            mouse = ConstrainPartsAxis(_partsFfdDragMouse, mouse);
            _partsFfdCtrl[_partsFfdDrag] = _partsFfdDragFrom + (toLattice(mouse) - toLattice(_partsFfdDragMouse));
            var pose = _partsFfdStartPose;
            var start = pose.Lattice;
            var lattice = start;
            for (int k = 0; k < _partsFfdVerts.Length; k++)
            {
                int i = _partsFfdVerts[k];
                float2 target = FfdPoint(_partsFfdParam[k]);
                float2 delta = target - FfdRestPoint(_partsFfdParam[k]);
                // Screen moves become pre-skin offsets on weighted meshes (same as the vertex drag).
                if (_partsFfdSkinInverse != null && i < _partsFfdSkinInverse.Length
                    && _partsFfdSkinSize.x > 1e-6f && _partsFfdSkinSize.y > 1e-6f)
                    delta = math.mul(_partsFfdSkinInverse[i], delta * _partsFfdSkinSize) / _partsFfdSkinSize;
                lattice.SetPoint(i, start.GetPoint(i) + delta);
            }
            pose.Lattice = lattice;
            ApplyPartsPoseEdit(_partsFfdSlotId, pose);
        }

        float2 FfdPoint(float2 uv) => FfdSample(_partsFfdCtrl, uv);
        float2 FfdRestPoint(float2 uv) => FfdSample(_partsFfdRest, uv);

        /// <summary>Bilinear inside the grid cell that holds <paramref name="uv"/> (0..1 over the grid).</summary>
        float2 FfdSample(float2[] grid, float2 uv)
        {
            int cols = _partsFfdCols, rows = _partsFfdRows;
            float fx = math.clamp(uv.x, 0f, 1f) * (cols - 1);
            float fy = math.clamp(uv.y, 0f, 1f) * (rows - 1);
            int cx = math.min((int)fx, cols - 2);
            int cy = math.min((int)fy, rows - 2);
            float tx = fx - cx, ty = fy - cy;
            float2 p00 = grid[cy * cols + cx], p10 = grid[cy * cols + cx + 1];
            float2 p01 = grid[(cy + 1) * cols + cx], p11 = grid[(cy + 1) * cols + cx + 1];
            return math.lerp(math.lerp(p00, p10, tx), math.lerp(p01, p11, tx), ty);
        }

        void DrawPanelFfdSection()
        {
            GUILayout.Space(6f);
            EditorGUILayout.LabelField("FFD (FREE FORM)", _sectionStyle);
            bool on = PartsFfdActive();
            using (new EditorGUI.DisabledScope(on))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(new GUIContent("Grid", "Control points across x down (2-6 each)."), GUILayout.Width(40f));
                _partsFfdCols = Mathf.Clamp(EditorGUILayout.IntField(_partsFfdCols, GUILayout.Width(36f)), 2, 6);
                GUILayout.Label("x", GUILayout.Width(10f));
                _partsFfdRows = Mathf.Clamp(EditorGUILayout.IntField(_partsFfdRows, GUILayout.Width(36f)), 2, 6);
                EditorGUILayout.EndHorizontal();
            }
            if (!on)
            {
                if (GUILayout.Button(new GUIContent("Start FFD", "A grid around the selected vertices (or the whole mesh). Drag its points to bend.")))
                    BeginPartsFfd();
                return;
            }
            EditorGUILayout.LabelField(_partsFfdVerts.Length + " vertices inside the grid.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Apply", "Keep the bend (Enter)"), _primaryStyle))
                ApplyPartsFfd();
            if (GUILayout.Button(new GUIContent("Cancel", "Take the bend back (Esc)")))
                CancelPartsFfd();
            EditorGUILayout.EndHorizontal();
        }

        void DrawPartsFfd(Rect canvas)
        {
            if (!PartsFfdActive() || Event.current.type != EventType.Repaint || !TryPartsFfdScreen(canvas, out var toScreen, out _))
                return;
            int cols = _partsFfdCols, rows = _partsFfdRows;
            var pts = new Vector3[_partsFfdCtrl.Length];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = toScreen(_partsFfdCtrl[i]);
            Handles.BeginGUI();
            Handles.color = new Color(1f, 0.55f, 0.15f, 0.95f);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    if (c + 1 < cols)
                        Handles.DrawAAPolyLine(2.5f, pts[i], pts[i + 1]);
                    if (r + 1 < rows)
                        Handles.DrawAAPolyLine(2.5f, pts[i], pts[i + cols]);
                }
            }
            Vector2 mouse = Event.current.mousePosition;
            for (int i = 0; i < pts.Length; i++)
            {
                bool hot = i == _partsFfdDrag || ((Vector2)pts[i] - mouse).sqrMagnitude <= PartsFfdHitRadius * PartsFfdHitRadius;
                Vector3 p = pts[i];
                var diamond = new[] { p + new Vector3(0f, -6f), p + new Vector3(6f, 0f), p + new Vector3(0f, 6f), p + new Vector3(-6f, 0f) };
                Handles.color = hot ? new Color(1f, 0.85f, 0.3f, 1f) : new Color(0.15f, 0.15f, 0.18f, 1f);
                Handles.DrawAAConvexPolygon(diamond);
                Handles.color = Color.white;
                Handles.DrawAAPolyLine(2f, diamond[0], diamond[1], diamond[2], diamond[3], diamond[0]);
            }
            Handles.EndGUI();
        }
    }
}
