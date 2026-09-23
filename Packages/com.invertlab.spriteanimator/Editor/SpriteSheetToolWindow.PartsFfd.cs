using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // FFD (Free Form Deformation, as in AnyPortrait) for the Warp tool: a grid of control points around
    // the selected vertices (all of them when fewer than two are selected). Dragging a point, or a grid
    // line (both its points), bends every vertex inside; each vertex follows its grid cell (bilinear).
    // The result is an ordinary deform key. Start, each drag, Reset, Apply and Cancel are undo steps:
    // the grid lives in PartsFfdSession, recorded together with the profile.
    public sealed partial class SpriteSheetToolWindow
    {
        [SerializeField] int _partsFfdCols = 3;
        [SerializeField] int _partsFfdRows = 3;
        [SerializeField] PartsFfdSession _partsFfdSession;
        int _partsFfdDrag = -1;      // grid point being dragged, or -1
        int _partsFfdDragB = -1;     // second point when a grid line is dragged
        Vector2 _partsFfdDragMouse;
        Vector2[] _partsFfdDragCtrl; // grid at drag start
        SpritePartsAuthoringOps.PoseEdit _partsFfdDragPose;
        float2x2[] _partsFfdSkinInverse;
        float2 _partsFfdSkinSize;

        const float PartsFfdHitRadius = 8f;
        // Violet: apart from the orange mesh, red / green vertices, cyan edges and the yellow hover.
        static readonly Color PartsFfdLine = new Color(0.72f, 0.45f, 1f, 0.95f);
        static readonly Color PartsFfdFill = new Color(0.32f, 0.16f, 0.52f, 1f);

        PartsFfdSession Ffd
        {
            get
            {
                if (_partsFfdSession == null)
                {
                    _partsFfdSession = CreateInstance<PartsFfdSession>();
                    _partsFfdSession.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                }
                return _partsFfdSession;
            }
        }

        bool PartsFfdActive()
        {
            var s = _partsFfdSession;
            return s != null && s.On && s.Ctrl != null && s.Rest != null && s.Ctrl.Length == s.Rest.Length
                   && _partsCanvasTool == PartsCanvasTool.Warp && _partsMode == SpritePartsStudioMode.Animate
                   && CurrentPartsSlot != null
                   && SpritePartIdUtility.Canonical(CurrentPartsSlot.SlotId) == s.SlotId
                   && Mathf.Approximately(s.Time, _partsPreviewTime);
        }

        /// <summary>One undo step that covers the profile (deform key) and the FFD grid.</summary>
        void RecordPartsFfdUndo(string operation)
        {
            RecordPartsUndo(operation);
            Undo.RegisterCompleteObjectUndo(Ffd, operation);
        }

        /// <summary>Starts FFD on the current part: a grid around the selected vertices, or the whole mesh.</summary>
        void BeginPartsFfd()
        {
            var slot = CurrentPartsSlot;
            if (slot == null || _partsMode != SpritePartsStudioMode.Animate)
            {
                _status = "FFD works in Animate with a part selected.";
                return;
            }
            if (ImportPreviewActive)
            {
                _status = "Import preview active - editing paused. Commit or stop the preview first.";
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
            if (!TrySampleWarpLattices(id, out _, out var shown, out _, out _) || shown.PointCount != pose.Lattice.PointCount)
                shown = pose.Lattice;
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
            RecordPartsFfdUndo("Start FFD");
            var f = Ffd;
            f.Rest = new Vector2[cols * rows];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float2 p = math.lerp(min, max, new float2(c / (float)(cols - 1), r / (float)(rows - 1)));
                    f.Rest[r * cols + c] = new Vector2(p.x, p.y);
                }
            }
            f.Ctrl = (Vector2[])f.Rest.Clone();
            f.Verts = verts.ToArray();
            f.Param = new Vector2[verts.Count];
            for (int k = 0; k < verts.Count; k++)
            {
                float2 t = (shown.GetPoint(verts[k]) - min) / (max - min);
                f.Param[k] = new Vector2(t.x, t.y);
            }
            f.Cols = cols;
            f.Rows = rows;
            f.SlotId = id;
            f.Time = _partsPreviewTime;
            f.On = true;
            _partsFfdCols = cols;
            _partsFfdRows = rows;
            _partsFfdDrag = -1;
            _status = "FFD: drag the violet points or lines to bend " + verts.Count + " vertices. Enter applies, Esc cancels.";
            Repaint();
        }

        void ApplyPartsFfd()
        {
            if (_partsFfdSession == null || !_partsFfdSession.On)
                return;
            RecordPartsFfdUndo("Apply FFD");
            Ffd.On = false;
            _partsFfdDrag = -1;
            _status = "FFD applied.";
            Repaint();
        }

        /// <summary>Reset: bends everything back to how it was when FFD started, and stays in FFD.</summary>
        void ResetPartsFfd()
        {
            if (!PartsFfdActive())
                return;
            RecordPartsFfdUndo("Reset FFD");
            BakeFfdChange(Ffd.Ctrl, Ffd.Rest);
            Ffd.Ctrl = (Vector2[])Ffd.Rest.Clone();
            _status = "FFD reset: the vertices are back where they were before this FFD.";
            Repaint();
        }

        /// <summary>Cancel: Reset and leave FFD, as one undo step.</summary>
        void CancelPartsFfd()
        {
            if (_partsFfdSession == null || !_partsFfdSession.On)
                return;
            RecordPartsFfdUndo("Cancel FFD");
            if (PartsFfdActive())
                BakeFfdChange(Ffd.Ctrl, Ffd.Rest);
            Ffd.Ctrl = (Vector2[])Ffd.Rest.Clone();
            Ffd.On = false;
            _partsFfdDrag = -1;
            _status = "FFD cancelled.";
            Repaint();
        }

        /// <summary>Moves the vertices by the change of the grid from <paramref name="from"/> to <paramref name="to"/>, on the current key.</summary>
        void BakeFfdChange(Vector2[] from, Vector2[] to)
        {
            var f = Ffd;
            var pose = SampleLocalPoseForSlot(f.SlotId, _partsPreviewTime);
            if (!pose.Lattice.HasMesh)
                return;
            SampleFfdSkin(f.SlotId, pose.Lattice.PointCount);
            ApplyFfdDelta(pose, from, to);
        }

        void SampleFfdSkin(string slotId, int points)
        {
            if (!TrySampleWarpLattices(slotId, out _, out var shown, out _partsFfdSkinInverse, out _partsFfdSkinSize)
                || shown.PointCount != points)
                _partsFfdSkinInverse = null;
        }

        /// <summary>
        /// The grid is bilinear per cell, so each vertex moves by FFD(to) - FFD(from). Screen moves become
        /// pre-skin offsets on weighted meshes, like the vertex drag.
        /// </summary>
        void ApplyFfdDelta(SpritePartsAuthoringOps.PoseEdit pose, Vector2[] from, Vector2[] to)
        {
            var f = Ffd;
            var start = pose.Lattice;
            var lattice = start;
            for (int k = 0; k < f.Verts.Length; k++)
            {
                int i = f.Verts[k];
                if ((uint)i >= (uint)start.PointCount)
                    continue;
                float2 delta = FfdSample(to, f.Param[k]) - FfdSample(from, f.Param[k]);
                if (_partsFfdSkinInverse != null && i < _partsFfdSkinInverse.Length
                    && _partsFfdSkinSize.x > 1e-6f && _partsFfdSkinSize.y > 1e-6f)
                    delta = math.mul(_partsFfdSkinInverse[i], delta * _partsFfdSkinSize) / _partsFfdSkinSize;
                lattice.SetPoint(i, start.GetPoint(i) + delta);
            }
            pose.Lattice = lattice;
            ApplyPartsPoseEdit(f.SlotId, pose);
        }

        /// <summary>Bilinear inside the grid cell that holds <paramref name="uv"/> (0..1 over the grid).</summary>
        float2 FfdSample(Vector2[] grid, Vector2 uv)
        {
            int cols = Ffd.Cols, rows = Ffd.Rows;
            float fx = Mathf.Clamp01(uv.x) * (cols - 1);
            float fy = Mathf.Clamp01(uv.y) * (rows - 1);
            int cx = Mathf.Min((int)fx, cols - 2);
            int cy = Mathf.Min((int)fy, rows - 2);
            float tx = fx - cx, ty = fy - cy;
            Vector2 p = Vector2.Lerp(
                Vector2.Lerp(grid[cy * cols + cx], grid[cy * cols + cx + 1], tx),
                Vector2.Lerp(grid[(cy + 1) * cols + cx], grid[(cy + 1) * cols + cx + 1], tx), ty);
            return new float2(p.x, p.y);
        }

        /// <summary>Lattice space to screen and back, for the part being deformed.</summary>
        bool TryPartsFfdScreen(Rect canvas, out System.Func<Vector2, Vector2> toScreen, out System.Func<Vector2, Vector2> toLattice)
        {
            toScreen = null;
            toLattice = null;
            if (!TryGetPartsWarpLayout(canvas, Ffd.SlotId, out var rect, out var joint, out float guiDeg,
                    out bool flipX, out bool flipY))
                return false;
            toScreen = p => PartsWarpPointGui(rect, joint, guiDeg, flipX, flipY, new float2(p.x, p.y));
            toLattice = m =>
            {
                Vector2 q = UnflipAround(UnrotateAround(m, joint, guiDeg), joint, flipX, flipY);
                return new Vector2(
                    (q.x - rect.xMin) / Mathf.Max(1f, rect.width) - 0.5f,
                    (rect.yMax - q.y) / Mathf.Max(1f, rect.height) - 0.5f);
            };
            return true;
        }

        /// <summary>Grid point under the mouse, else a grid line (both of its points). False when neither.</summary>
        bool HitPartsFfd(System.Func<Vector2, Vector2> toScreen, Vector2 mouse, out int a, out int b)
        {
            var f = Ffd;
            a = b = -1;
            float bestD = PartsFfdHitRadius * PartsFfdHitRadius;
            for (int i = 0; i < f.Ctrl.Length; i++)
            {
                float d = (mouse - toScreen(f.Ctrl[i])).sqrMagnitude;
                if (d > bestD)
                    continue;
                bestD = d;
                a = i;
            }
            if (a >= 0)
                return true;
            float bestLine = 6f;
            for (int r = 0; r < f.Rows; r++)
            {
                for (int c = 0; c < f.Cols; c++)
                {
                    int i = r * f.Cols + c;
                    for (int dir = 0; dir < 2; dir++)
                    {
                        int j = dir == 0 ? (c + 1 < f.Cols ? i + 1 : -1) : (r + 1 < f.Rows ? i + f.Cols : -1);
                        if (j < 0)
                            continue;
                        float d = SpritePartsMeshOps.DistanceToSegment(mouse, toScreen(f.Ctrl[i]), toScreen(f.Ctrl[j]));
                        if (d > bestLine)
                            continue;
                        bestLine = d;
                        a = i;
                        b = j;
                    }
                }
            }
            return a >= 0;
        }

        /// <summary>Mouse down in Warp while FFD is on. True when a grid point or line was grabbed.</summary>
        bool TryBeginPartsFfdDrag(Rect canvas, Vector2 mouse, int controlId)
        {
            if (!PartsFfdActive() || !TryPartsFfdScreen(canvas, out var toScreen, out _)
                || !HitPartsFfd(toScreen, mouse, out int a, out int b))
                return false;
            var f = Ffd;
            BeginPartsDragUndo("FFD");
            Undo.RegisterCompleteObjectUndo(f, "FFD");
            _partsFfdDrag = a;
            _partsFfdDragB = b;
            _partsFfdDragMouse = mouse;
            _partsFfdDragCtrl = (Vector2[])f.Ctrl.Clone();
            _partsFfdDragPose = SampleLocalPoseForSlot(f.SlotId, _partsPreviewTime);
            SampleFfdSkin(f.SlotId, _partsFfdDragPose.Lattice.PointCount);
            _partsDragStartPose = _partsFfdDragPose; // Esc during the drag restores this
            _partsDragActive = true;
            _partsCanvasHotControl = controlId;
            GUIUtility.hotControl = controlId;
            _partsDragSlotId = f.SlotId;
            _partsWarpActive = true; // routes MouseDrag to ApplyPartsWarpDrag -> ApplyPartsFfdDrag
            return true;
        }

        void ApplyPartsFfdDrag(Rect canvas, Vector2 mouse)
        {
            if (_partsFfdDrag < 0 || _partsFfdDragCtrl == null || !TryPartsFfdScreen(canvas, out _, out var toLattice))
                return;
            var f = Ffd;
            mouse = ConstrainPartsAxis(_partsFfdDragMouse, mouse);
            Vector2 move = toLattice(mouse) - toLattice(_partsFfdDragMouse);
            var ctrl = (Vector2[])_partsFfdDragCtrl.Clone();
            ctrl[_partsFfdDrag] += move;
            if (_partsFfdDragB >= 0)
                ctrl[_partsFfdDragB] += move;
            f.Ctrl = ctrl;
            ApplyFfdDelta(_partsFfdDragPose, _partsFfdDragCtrl, ctrl);
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
                if (GUILayout.Button(new GUIContent("Start FFD", "A grid around the selected vertices (or the whole mesh). Drag its points or lines to bend.")))
                    BeginPartsFfd();
                return;
            }
            EditorGUILayout.LabelField(Ffd.Verts.Length + " vertices inside the grid. Drag a point, or a line to move both its points.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Apply", "Keep the bend (Enter)"), _primaryStyle))
                ApplyPartsFfd();
            if (GUILayout.Button(new GUIContent("Reset", "Back to how the vertices were before this FFD; stay in FFD")))
                ResetPartsFfd();
            if (GUILayout.Button(new GUIContent("Cancel", "Reset and leave FFD (Esc)")))
                CancelPartsFfd();
            EditorGUILayout.EndHorizontal();
        }

        void DrawPartsFfd(Rect canvas)
        {
            if (!PartsFfdActive() || Event.current.type != EventType.Repaint || !TryPartsFfdScreen(canvas, out var toScreen, out _))
                return;
            var f = Ffd;
            int cols = f.Cols, rows = f.Rows;
            var pts = new Vector3[f.Ctrl.Length];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = toScreen(f.Ctrl[i]);
            Vector2 mouse = Event.current.mousePosition;
            int hotA = _partsFfdDrag, hotB = _partsFfdDragB;
            if (hotA < 0)
                HitPartsFfd(toScreen, mouse, out hotA, out hotB);
            var hot = new Color(1f, 0.85f, 0.3f, 1f);

            Handles.BeginGUI();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    for (int dir = 0; dir < 2; dir++)
                    {
                        int j = dir == 0 ? (c + 1 < cols ? i + 1 : -1) : (r + 1 < rows ? i + cols : -1);
                        if (j < 0)
                            continue;
                        bool lineHot = hotB >= 0 && ((hotA == i && hotB == j) || (hotA == j && hotB == i));
                        Handles.color = lineHot ? hot : PartsFfdLine;
                        Handles.DrawAAPolyLine(lineHot ? 4f : 2.5f, pts[i], pts[j]);
                    }
                }
            }
            for (int i = 0; i < pts.Length; i++)
            {
                bool pointHot = i == hotA || i == hotB;
                Vector3 p = pts[i];
                var diamond = new[] { p + new Vector3(0f, -6f), p + new Vector3(6f, 0f), p + new Vector3(0f, 6f), p + new Vector3(-6f, 0f) };
                Handles.color = pointHot ? hot : PartsFfdFill;
                Handles.DrawAAConvexPolygon(diamond);
                Handles.color = Color.white;
                Handles.DrawAAPolyLine(2f, diamond[0], diamond[1], diamond[2], diamond[3], diamond[0]);
            }
            Handles.EndGUI();
        }
    }
}
