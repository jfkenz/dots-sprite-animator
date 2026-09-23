using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Liquify brushes for the Warp tool (neither Spine nor AnyPortrait has these): paint over the part
    // and its mesh vertices move. Each stroke is one undo step and writes an ordinary deform key.
    //   Twist   hold to swirl vertices around the brush centre (Shift: the other way) - inner turns most
    //   Push    drag to smear vertices along the stroke
    //   Pinch   hold to pull vertices toward the centre; Bloat pushes them out
    //   Smooth  hold to relax vertices toward their neighbours (removes creases and bunching)
    // Pins: pinned vertices never move - not by brushes, vertex drags, soft selection or FFD.
    public sealed partial class SpriteSheetToolWindow
    {
        enum PartsWarpBrush
        {
            Off = 0,
            Twist = 1,
            Push = 2,
            Pinch = 3,
            Bloat = 4,
            Smooth = 5,
        }

        [SerializeField] PartsWarpBrush _partsWarpBrush = PartsWarpBrush.Off;
        [SerializeField] float _partsBrushSize = 80f;      // radius, screen pixels
        [SerializeField] float _partsBrushStrength = 0.5f; // 0..1
        bool _partsBrushActive;
        Rect _partsBrushCanvas;
        Vector2 _partsBrushMouse;
        Vector2 _partsBrushLastMouse;
        bool _partsBrushShift;
        double _partsBrushLastTime;
        SpritePartsAuthoringOps.PoseEdit _partsBrushStartPose;
        SpritePartsLattice _partsBrushShownStart;
        float2x2[] _partsBrushSkinInverse;
        float2 _partsBrushSkinSize;
        Vector2[] _partsBrushWork; // vertex positions in the part's unrotated rect (pixels), updated by the stroke
        List<int>[] _partsBrushNeighbours;

        // Pins (Warp): per part, for this editor session.
        readonly HashSet<int> _partsWarpPins = new HashSet<int>();
        string _partsWarpPinSlot;

        HashSet<int> WarpPinsFor(string slotId)
        {
            string id = SpritePartIdUtility.Canonical(slotId);
            if (_partsWarpPinSlot != id)
            {
                _partsWarpPins.Clear();
                _partsWarpPinSlot = id;
            }
            return _partsWarpPins;
        }

        bool IsWarpPinned(string slotId, int vertex) => WarpPinsFor(slotId).Contains(vertex);

        void PinSelectedWarpVertices(bool pin)
        {
            var slot = CurrentPartsSlot;
            if (slot == null)
                return;
            var pins = WarpPinsFor(slot.SlotId);
            if (!pin && _partsWarpSelection.Count == 0)
                pins.Clear();
            foreach (int v in _partsWarpSelection)
            {
                if (pin)
                    pins.Add(v);
                else
                    pins.Remove(v);
            }
            _status = pins.Count + " pinned vertices. Pinned vertices never move.";
            Repaint();
        }

        bool PartsBrushOn()
            => _partsWarpBrush != PartsWarpBrush.Off && _partsCanvasTool == PartsCanvasTool.Warp
               && _partsMode == SpritePartsStudioMode.Animate && !IsPartsMeshEdit() && !PartsFfdActive();

        /// <summary>Mouse down in Warp with a brush on: starts a stroke on the current part.</summary>
        bool TryBeginPartsBrush(Rect canvas, Event evt, int controlId)
        {
            var slot = CurrentPartsSlot;
            if (!PartsBrushOn() || slot?.Mesh == null || !slot.Mesh.HasMesh
                || slot.EditorLocked || SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, slot.SlotId))
                return false;
            if (ImportPreviewActive)
            {
                _status = "Import preview active - editing paused. Commit or stop the preview first.";
                return true;
            }
            string id = SpritePartIdUtility.Canonical(slot.SlotId);
            var pose = SampleLocalPoseForSlot(id, _partsPreviewTime);
            if (!pose.Lattice.HasMesh || !TryGetPartsWarpLayout(canvas, id, out var rect, out _, out _, out _, out _))
                return false;
            if (!TrySampleWarpLattices(id, out _, out var shown, out _partsBrushSkinInverse, out _partsBrushSkinSize)
                || shown.PointCount != pose.Lattice.PointCount)
            {
                shown = pose.Lattice;
                _partsBrushSkinInverse = null;
            }
            BeginPartsDragUndo("Brush " + _partsWarpBrush);
            _partsBrushStartPose = pose;
            _partsBrushShownStart = shown;
            _partsBrushWork = new Vector2[shown.PointCount];
            for (int i = 0; i < shown.PointCount; i++)
                _partsBrushWork[i] = LatticeGui(rect, shown.GetPoint(i));
            _partsBrushNeighbours = _partsWarpBrush == PartsWarpBrush.Smooth ? MeshNeighbours(slot.Mesh) : null;
            _partsBrushActive = true;
            _partsBrushCanvas = canvas;
            _partsBrushMouse = _partsBrushLastMouse = evt.mousePosition;
            _partsBrushShift = evt.shift;
            _partsBrushLastTime = EditorApplication.timeSinceStartup;
            _partsDragActive = true;
            _partsCanvasHotControl = controlId;
            GUIUtility.hotControl = controlId;
            _partsDragSlotId = id;
            _partsDragStartMouse = evt.mousePosition;
            _partsDragStartPose = pose; // Esc during the stroke restores this
            _partsWarpActive = true;    // routes MouseDrag to ApplyPartsWarpDrag -> PartsBrushDrag
            EditorApplication.update -= PartsBrushTick;
            EditorApplication.update += PartsBrushTick; // Twist / Pinch / Bloat / Smooth keep working while held
            SetWarpSelectionSlot(id);
            _status = _partsWarpBrush + " brush: " + (_partsWarpBrush == PartsWarpBrush.Push ? "drag to smear." : "hold to apply, move to paint.")
                      + (_partsWarpBrush == PartsWarpBrush.Twist ? " Shift turns the other way." : string.Empty);
            return true;
        }

        void PartsBrushDrag(Rect canvas, Vector2 mouse, bool shift)
        {
            _partsBrushCanvas = canvas;
            _partsBrushShift = shift;
            _partsBrushMouse = mouse;
            StepPartsBrush();
        }

        void PartsBrushTick()
        {
            if (!_partsBrushActive)
            {
                EditorApplication.update -= PartsBrushTick;
                return;
            }
            if (EditorApplication.timeSinceStartup - _partsBrushLastTime < 1.0 / 30.0)
                return;
            StepPartsBrush();
            Repaint();
        }

        /// <summary>One brush step: time since the last step drives Twist / Pinch / Bloat / Smooth, mouse movement drives Push.</summary>
        void StepPartsBrush()
        {
            if (!_partsBrushActive || _partsBrushWork == null
                || !TryGetPartsWarpLayout(_partsBrushCanvas, _partsDragSlotId, out var rect, out var joint, out float guiDeg,
                    out bool flipX, out bool flipY))
                return;
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - _partsBrushLastTime), 0f, 0.1f);
            _partsBrushLastTime = now;
            Vector2 m = UnflipAround(UnrotateAround(_partsBrushMouse, joint, guiDeg), joint, flipX, flipY);
            Vector2 last = UnflipAround(UnrotateAround(_partsBrushLastMouse, joint, guiDeg), joint, flipX, flipY);
            _partsBrushLastMouse = _partsBrushMouse;
            float radius = Mathf.Max(4f, _partsBrushSize);
            float strength = Mathf.Clamp01(_partsBrushStrength);
            var work = _partsBrushWork;
            var before = (Vector2[])work.Clone(); // Smooth reads the positions from before this step
            for (int i = 0; i < work.Length; i++)
            {
                if (IsWarpPinned(_partsDragSlotId, i))
                    continue;
                float d = Vector2.Distance(work[i], m);
                if (d >= radius)
                    continue;
                float t = 1f - d / radius;
                float f = t * t * (3f - 2f * t); // smooth falloff: full at the centre, none at the edge
                switch (_partsWarpBrush)
                {
                    case PartsWarpBrush.Twist:
                    {
                        // Inner vertices turn the most, so straight lines become a spiral.
                        float angle = strength * 3f * dt * f * (_partsBrushShift ? 1f : -1f);
                        float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
                        Vector2 r = work[i] - m;
                        work[i] = m + new Vector2(r.x * cs - r.y * sn, r.x * sn + r.y * cs);
                        break;
                    }
                    case PartsWarpBrush.Push:
                        work[i] += (m - last) * f * Mathf.Lerp(0.3f, 1f, strength);
                        break;
                    case PartsWarpBrush.Pinch:
                        work[i] += (m - work[i]) * Mathf.Min(0.5f, strength * 2f * dt * f);
                        break;
                    case PartsWarpBrush.Bloat:
                        work[i] -= (m - work[i]) * Mathf.Min(0.5f, strength * 2f * dt * f);
                        break;
                    case PartsWarpBrush.Smooth:
                    {
                        var nb = _partsBrushNeighbours != null && i < _partsBrushNeighbours.Length ? _partsBrushNeighbours[i] : null;
                        if (nb == null || nb.Count == 0)
                            break;
                        Vector2 avg = Vector2.zero;
                        foreach (int j in nb)
                            avg += before[j];
                        avg /= nb.Count;
                        work[i] += (avg - before[i]) * Mathf.Min(1f, strength * 4f * dt * f);
                        break;
                    }
                }
            }
            WritePartsBrushPose(rect);
        }

        /// <summary>Stroke positions (pixels, unrotated rect) to a deform key, through the inverse skin on weighted meshes.</summary>
        void WritePartsBrushPose(Rect rect)
        {
            var pose = _partsBrushStartPose;
            var start = pose.Lattice;
            var lattice = start;
            var shown = _partsBrushShownStart;
            for (int i = 0; i < _partsBrushWork.Length && i < start.PointCount; i++)
            {
                var target = new float2(
                    (_partsBrushWork[i].x - rect.xMin) / Mathf.Max(1f, rect.width) - 0.5f,
                    (rect.yMax - _partsBrushWork[i].y) / Mathf.Max(1f, rect.height) - 0.5f);
                float2 delta = target - shown.GetPoint(i);
                if (_partsBrushSkinInverse != null && i < _partsBrushSkinInverse.Length
                    && _partsBrushSkinSize.x > 1e-6f && _partsBrushSkinSize.y > 1e-6f)
                    delta = math.mul(_partsBrushSkinInverse[i], delta * _partsBrushSkinSize) / _partsBrushSkinSize;
                lattice.SetPoint(i, start.GetPoint(i) + delta);
            }
            pose.Lattice = lattice;
            ApplyPartsPoseEdit(_partsDragSlotId, pose);
        }

        static List<int>[] MeshNeighbours(SpritePartMeshDef mesh)
        {
            int n = mesh.VertexCount;
            var list = new List<int>[n];
            for (int i = 0; i < n; i++)
                list[i] = new List<int>();
            foreach (var e in SpritePartsMeshOps.GraphEdges(mesh))
            {
                if (!list[e.x].Contains(e.y))
                    list[e.x].Add(e.y);
                if (!list[e.y].Contains(e.x))
                    list[e.y].Add(e.x);
            }
            return list;
        }

        void EndPartsBrush()
        {
            _partsBrushActive = false;
            _partsBrushWork = null;
            EditorApplication.update -= PartsBrushTick;
        }

        /// <summary>Brush circle at the mouse, in the brush's colour.</summary>
        void DrawPartsBrushCursor(Rect canvas)
        {
            if (!PartsBrushOn() || Event.current.type != EventType.Repaint || !canvas.Contains(Event.current.mousePosition))
                return;
            Vector2 m = Event.current.mousePosition;
            var color = _partsWarpBrush switch
            {
                PartsWarpBrush.Twist => new Color(0.72f, 0.45f, 1f, 0.95f),
                PartsWarpBrush.Push => new Color(0.35f, 0.9f, 1f, 0.95f),
                PartsWarpBrush.Pinch => new Color(1f, 0.55f, 0.3f, 0.95f),
                PartsWarpBrush.Bloat => new Color(1f, 0.85f, 0.3f, 0.95f),
                _ => new Color(0.5f, 1f, 0.6f, 0.95f),
            };
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawWireDisc(m, Vector3.forward, Mathf.Max(4f, _partsBrushSize));
            Handles.color = new Color(color.r, color.g, color.b, 0.35f);
            Handles.DrawWireDisc(m, Vector3.forward, Mathf.Max(4f, _partsBrushSize) * 0.5f);
            Handles.EndGUI();
            GUI.Label(new Rect(m.x + 8f, m.y - Mathf.Max(4f, _partsBrushSize) - 18f, 80f, 16f), _partsWarpBrush.ToString(), _mutedStyle);
        }

        void DrawPanelBrushSection()
        {
            GUILayout.Space(6f);
            EditorGUILayout.LabelField("BRUSHES (LIQUIFY)", _sectionStyle);
            var brush = (PartsWarpBrush)GUILayout.SelectionGrid((int)_partsWarpBrush,
                new[]
                {
                    new GUIContent("Off", "Normal vertex editing"),
                    new GUIContent("Twist", "Hold to swirl vertices around the brush centre; inner ones turn most. Shift: other way."),
                    new GUIContent("Push", "Drag to smear vertices along the stroke."),
                    new GUIContent("Pinch", "Hold to pull vertices toward the centre."),
                    new GUIContent("Bloat", "Hold to push vertices away from the centre."),
                    new GUIContent("Smooth", "Hold to relax vertices toward their neighbours."),
                }, 3);
            if (brush != _partsWarpBrush)
            {
                _partsWarpBrush = brush;
                _status = brush == PartsWarpBrush.Off ? "Brush off." : brush + " brush on: paint over the selected part.";
                Repaint();
            }
            using (new EditorGUI.DisabledScope(_partsWarpBrush == PartsWarpBrush.Off))
            {
                _partsBrushSize = EditorGUILayout.Slider(new GUIContent("Size", "Brush radius in screen pixels"), _partsBrushSize, 10f, 400f);
                _partsBrushStrength = EditorGUILayout.Slider(new GUIContent("Strength"), _partsBrushStrength, 0.05f, 1f);
            }
            EditorGUILayout.BeginHorizontal();
            var slot = CurrentPartsSlot;
            int pinned = slot != null ? WarpPinsFor(slot.SlotId).Count : 0;
            using (new EditorGUI.DisabledScope(_partsWarpSelection.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("Pin", "Pinned vertices never move: not by brushes, drags, soft selection or FFD.")))
                    PinSelectedWarpVertices(true);
            }
            using (new EditorGUI.DisabledScope(pinned == 0))
            {
                if (GUILayout.Button(new GUIContent(_partsWarpSelection.Count > 0 ? "Unpin" : "Unpin All", "Selected vertices, or all when none is selected")))
                    PinSelectedWarpVertices(false);
            }
            EditorGUILayout.EndHorizontal();
            if (pinned > 0)
                EditorGUILayout.LabelField(pinned + " pinned (white ring).", EditorStyles.wordWrappedMiniLabel);
        }
    }
}
