using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Click-and-set dial for Twist and Pinch / Bloat (instead of holding the mouse):
    //   click on the part     a dial is placed there; drag left / right to set the amount right away
    //   the bar under it      -1..1 slider, - 0 + steps, number, Size, Apply / Cancel
    //   click elsewhere       keeps this one and starts a new dial there
    // Every change is worked out again from the pose at the click, so the value can go back and forth.
    // One undo step per dial; Cancel (Esc) puts the key back.
    public sealed partial class SpriteSheetToolWindow
    {
        bool _partsDialOn;
        PartsWarpBrush _partsDialKind;
        string _partsDialSlot;
        float _partsDialTime;
        float2 _partsDialCentre;      // lattice space, so it follows zoom / pan
        float _partsDialRadiusFrac;   // radius as a fraction of the part's on-screen width
        float _partsDialAmount;       // -1..1
        bool _partsDialRecorded;      // the undo step is taken on the first change
        bool _partsDialDragging;
        float _partsDialDragStartX;
        float _partsDialDragStartAmount;

        const float PartsDialBarWidth = 300f;
        const float PartsDialBarHeight = 48f;

        static bool PartsDialBrush(PartsWarpBrush b)
            => b == PartsWarpBrush.Twist || b == PartsWarpBrush.Pinch || b == PartsWarpBrush.Bloat;

        bool PartsDialActive()
            => _partsDialOn && _partsCanvasTool == PartsCanvasTool.Warp && _partsMode == SpritePartsStudioMode.Animate
               && !IsPartsMeshEdit() && CurrentPartsSlot != null
               && SpritePartIdUtility.Canonical(CurrentPartsSlot.SlotId) == _partsDialSlot
               && Mathf.Approximately(_partsDialTime, _partsPreviewTime);

        /// <summary>Mouse down in Warp with Twist / Pinch / Bloat: place a dial (the old one is kept).</summary>
        bool TryBeginPartsDial(Rect canvas, Event evt, int controlId)
        {
            var slot = CurrentPartsSlot;
            if (!PartsBrushOn() || !PartsDialBrush(_partsWarpBrush) || slot?.Mesh == null || !slot.Mesh.HasMesh
                || slot.EditorLocked || SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, slot.SlotId))
                return false;
            string id = SpritePartIdUtility.Canonical(slot.SlotId);
            if (evt.control || evt.command || !BrushReachesPart(canvas, id, evt.mousePosition))
                return false; // box-select instead
            if (ImportPreviewActive)
            {
                _status = "Import preview active - editing paused. Commit or stop the preview first.";
                return true;
            }
            if (_partsDialOn)
                ApplyPartsDial(); // keep the previous one
            var pose = SampleLocalPoseForSlot(id, _partsPreviewTime);
            if (!pose.Lattice.HasMesh || !TryGetPartsWarpLayout(canvas, id, out var rect, out var joint, out float guiDeg,
                    out bool flipX, out bool flipY))
                return false;
            if (!TrySampleWarpLattices(id, out _, out var shown, out _partsBrushSkinInverse, out _partsBrushSkinSize)
                || shown.PointCount != pose.Lattice.PointCount)
            {
                shown = pose.Lattice;
                _partsBrushSkinInverse = null;
            }
            _partsBrushStartPose = pose;
            _partsBrushShownStart = shown;
            _partsBrushWork = new Vector2[shown.PointCount];
            Vector2 local = UnflipAround(UnrotateAround(evt.mousePosition, joint, guiDeg), joint, flipX, flipY);
            _partsDialCentre = new float2(
                (local.x - rect.xMin) / Mathf.Max(1f, rect.width) - 0.5f,
                (rect.yMax - local.y) / Mathf.Max(1f, rect.height) - 0.5f);
            _partsDialRadiusFrac = Mathf.Max(4f, _partsBrushSize) / Mathf.Max(1f, rect.width);
            _partsDialKind = _partsWarpBrush;
            _partsDialSlot = id;
            _partsDialTime = _partsPreviewTime;
            _partsDialAmount = 0f;
            _partsDialRecorded = false;
            _partsDialOn = true;
            SetWarpSelectionSlot(id);

            // Drag left / right right away to set the amount; the bar stays for fine tuning.
            _partsDialDragging = true;
            _partsDialDragStartX = evt.mousePosition.x;
            _partsDialDragStartAmount = 0f;
            _partsDragActive = true;
            _partsWarpActive = true; // MouseDrag -> ApplyPartsWarpDrag -> PartsDialDrag
            _partsCanvasHotControl = controlId;
            GUIUtility.hotControl = controlId;
            _partsDragSlotId = id;
            _partsDragStartMouse = evt.mousePosition;
            _status = PartsDialName() + ": drag left / right, or use the bar. Enter applies, Esc cancels.";
            return true;
        }

        void PartsDialDrag(Rect canvas, Vector2 mouse)
        {
            if (!_partsDialDragging)
                return;
            SetPartsDialAmount(canvas, _partsDialDragStartAmount + (mouse.x - _partsDialDragStartX) / 160f);
        }

        string PartsDialName() => _partsDialKind == PartsWarpBrush.Twist ? "Twist" : "Pinch / Bloat";

        void SetPartsDialAmount(Rect canvas, float amount)
        {
            _partsDialAmount = Mathf.Clamp(amount, -1f, 1f);
            ApplyPartsDialNow(canvas);
            Repaint();
        }

        /// <summary>Works the whole dial out again from the pose at the click.</summary>
        void ApplyPartsDialNow(Rect canvas)
        {
            if (!_partsDialOn || !TryGetPartsWarpLayout(canvas, _partsDialSlot, out var rect, out _, out _, out _, out _))
                return;
            var shown = _partsBrushShownStart;
            int n = Mathf.Min(shown.PointCount, _partsBrushWork?.Length ?? 0);
            Vector2 c = LatticeGui(rect, _partsDialCentre);
            float radius = Mathf.Max(4f, _partsDialRadiusFrac * rect.width);
            bool onlySelected = _partsWarpSelection.Count > 0 && _partsWarpSelectionSlotId == _partsDialSlot;
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = LatticeGui(rect, shown.GetPoint(i));
                _partsBrushWork[i] = p;
                if (IsWarpPinned(_partsDialSlot, i) || (onlySelected && !_partsWarpSelection.Contains(i)))
                    continue;
                float d = Vector2.Distance(p, c);
                if (d >= radius)
                    continue;
                float t = 1f - d / radius;
                float f = t * t * (3f - 2f * t);
                if (_partsDialKind == PartsWarpBrush.Twist)
                {
                    // +1 = one full clockwise turn at the centre, fading out to the edge: a spiral.
                    float angle = -_partsDialAmount * Mathf.PI * 2f * f;
                    float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
                    Vector2 r = p - c;
                    _partsBrushWork[i] = c + new Vector2(r.x * cs - r.y * sn, r.x * sn + r.y * cs);
                }
                else
                {
                    // +1 = pinch (pull in), -1 = bloat (push out).
                    float k = _partsDialAmount * 0.9f * f;
                    _partsBrushWork[i] = p + (c - p) * k;
                }
                changed |= (_partsBrushWork[i] - p).sqrMagnitude > 1e-8f;
            }
            if (!changed && !_partsDialRecorded)
                return;
            if (!_partsDialRecorded)
            {
                RecordPartsUndo(PartsDialName()); // one step for the whole dial
                _partsDialRecorded = true;
            }
            WritePartsBrushPose(rect, _partsDialSlot);
        }

        void ApplyPartsDial()
        {
            if (!_partsDialOn)
                return;
            _partsDialOn = false;
            _partsDialDragging = false;
            if (_partsDialRecorded)
                SaveDirty();
            _status = PartsDialName() + " applied.";
            Repaint();
        }

        void CancelPartsDial()
        {
            if (!_partsDialOn)
                return;
            bool recorded = _partsDialRecorded;
            _partsDialOn = false;
            _partsDialDragging = false;
            // Our step is still the newest real one (sealed empty groups do not count): undo it.
            if (recorded && _undoNames.Count > 0 && _undoNames[_undoNames.Count - 1] == PartsDialName())
                Undo.PerformUndo(); // the dial's own step is the latest: back to the pose at the click
            else if (recorded && !string.IsNullOrEmpty(_partsDialSlot))
            {
                RecordPartsUndo("Cancel " + PartsDialName()); // something else was recorded since: restore by hand
                ApplyPartsPoseEdit(_partsDialSlot, _partsBrushStartPose);
            }
            _status = PartsDialName() + " cancelled.";
            Repaint();
        }

        /// <summary>The bar under the dial, in the same space as <paramref name="canvas"/>. Zero when there is no dial.</summary>
        Rect PartsDialBarRect(Rect canvas)
        {
            if (!PartsDialActive() || !TryGetPartsWarpLayout(canvas, _partsDialSlot, out var rect, out var joint, out float guiDeg,
                    out bool flipX, out bool flipY))
                return Rect.zero;
            Vector2 c = PartsWarpPointGui(rect, joint, guiDeg, flipX, flipY, _partsDialCentre);
            float radius = _partsDialRadiusFrac * rect.width;
            var bar = new Rect(c.x - PartsDialBarWidth * 0.5f, c.y + radius + 10f, PartsDialBarWidth, PartsDialBarHeight);
            bar.x = Mathf.Clamp(bar.x, canvas.xMin + 4f, canvas.xMax - bar.width - 4f);
            bar.y = Mathf.Clamp(bar.y, canvas.yMin + 4f, canvas.yMax - bar.height - 4f);
            return bar;
        }

        /// <summary>The dial ring (in the canvas overlay) - drawn on Repaint only.</summary>
        void DrawPartsDialRing(Rect canvas)
        {
            if (!PartsDialActive() || Event.current.type != EventType.Repaint
                || !TryGetPartsWarpLayout(canvas, _partsDialSlot, out var rect, out var joint, out float guiDeg, out bool flipX, out bool flipY))
                return;
            Vector2 c = PartsWarpPointGui(rect, joint, guiDeg, flipX, flipY, _partsDialCentre);
            float radius = _partsDialRadiusFrac * rect.width;
            var color = _partsDialKind == PartsWarpBrush.Twist ? new Color(0.72f, 0.45f, 1f, 0.95f) : new Color(1f, 0.55f, 0.3f, 0.95f);
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawWireDisc(c, Vector3.forward, radius);
            Handles.DrawSolidDisc(c, Vector3.forward, 3f);
            // The amount as an arc around the centre (Twist: how far it turns).
            if (Mathf.Abs(_partsDialAmount) > 1e-3f)
            {
                Handles.color = new Color(color.r, color.g, color.b, 0.25f);
                Vector3 from = Vector3.up;
                float sweep = _partsDialKind == PartsWarpBrush.Twist ? _partsDialAmount * 360f : _partsDialAmount * 180f;
                Handles.DrawSolidArc(c, Vector3.forward, from, sweep, radius * 0.35f);
            }
            Handles.EndGUI();
        }

        /// <summary>The -1..1 bar. Drawn in window space after the canvas so its clicks reach it.</summary>
        void DrawPartsDialBar(Rect canvas)
        {
            var bar = PartsDialBarRect(canvas);
            if (bar.width <= 0f)
                return;
            EditorGUI.DrawRect(bar, new Color(0.08f, 0.09f, 0.12f, 0.96f));
            DrawGuiRectOutline(bar, _partsDialKind == PartsWarpBrush.Twist ? new Color(0.72f, 0.45f, 1f, 0.9f) : new Color(1f, 0.55f, 0.3f, 0.9f), 1f);
            float x = bar.x + 6f, y = bar.y + 4f;
            bool twist = _partsDialKind == PartsWarpBrush.Twist;
            var left = new GUIContent("-1", twist ? "-1: a full turn counter-clockwise" : "-1: full Bloat (push out)");
            var right = new GUIContent("1", twist ? "1: a full turn clockwise" : "1: full Pinch (pull in)");
            float amount = _partsDialAmount;
            // Reset: back to 0 (no change), the same as the "0" button.
            var reset = EditorGUIUtility.IconContent("Refresh");
            reset = reset?.image != null ? new GUIContent(reset.image, "Reset to 0 (no change)") : new GUIContent("↺", "Reset to 0 (no change)");
            if (GUI.Button(new Rect(x, y - 1f, 22f, 20f), reset, _partsTabStyle))
                amount = 0f;
            float labelW = 16f;
            GUI.Label(new Rect(x + 26f, y, labelW, 18f), left, _mutedStyle);
            float sliderX = x + 28f + labelW;
            float sliderW = bar.xMax - 66f - labelW - 4f - sliderX;
            amount = GUI.HorizontalSlider(new Rect(sliderX, y + 3f, sliderW, 16f), amount, -1f, 1f);
            GUI.Label(new Rect(sliderX + sliderW + 4f, y, labelW, 18f), right, _mutedStyle);
            float typed = EditorGUI.FloatField(new Rect(bar.xMax - 62f, y, 56f, 18f), (float)System.Math.Round(amount, 2));
            if (!Mathf.Approximately(typed, (float)System.Math.Round(amount, 2)))
                amount = typed;
            y += 22f;
            if (GUI.Button(new Rect(x, y, 26f, 18f), "−", _partsTabStyle))
                amount -= 0.1f;
            if (GUI.Button(new Rect(x + 28f, y, 26f, 18f), "0", _partsTabStyle))
                amount = 0f;
            if (GUI.Button(new Rect(x + 56f, y, 26f, 18f), "+", _partsTabStyle))
                amount += 0.1f;
            GUI.Label(new Rect(x + 88f, y, 30f, 18f), "Size", _mutedStyle);
            if (TryGetPartsWarpLayout(canvas, _partsDialSlot, out var rect, out _, out _, out _, out _))
            {
                float size = GUI.HorizontalSlider(new Rect(x + 118f, y + 3f, 60f, 16f), _partsDialRadiusFrac * rect.width, 10f, 400f);
                float frac = size / Mathf.Max(1f, rect.width);
                if (!Mathf.Approximately(frac, _partsDialRadiusFrac))
                {
                    _partsDialRadiusFrac = frac;
                    _partsBrushSize = size;
                    ApplyPartsDialNow(canvas);
                }
            }
            if (GUI.Button(new Rect(bar.xMax - 112f, y, 50f, 18f), "Apply", _primaryStyle))
            {
                ApplyPartsDial();
                return;
            }
            if (GUI.Button(new Rect(bar.xMax - 60f, y, 54f, 18f), "Cancel", _partsTabStyle))
            {
                CancelPartsDial();
                return;
            }
            if (!Mathf.Approximately(amount, _partsDialAmount))
                SetPartsDialAmount(canvas, amount);
        }
    }
}
