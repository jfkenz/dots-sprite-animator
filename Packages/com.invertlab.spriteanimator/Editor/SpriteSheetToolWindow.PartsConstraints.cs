using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // TRANSFORM CONSTRAINTS (Spine): parts copy a target part's rotation, position and scale.
    //   Add for Selected Parts   the selected parts follow a Target you pick
    //   per row                  target / parts / Character or Local / Absolute or Relative / channel mixes / offsets
    // PATH CONSTRAINTS (Spine): a chain of parts sits along a path part's curve.
    //   Add Path Part            a smooth curve through points: drag, click the curve to add, right-click to delete (Rig)
    //   Add for Selected Parts   the selected parts, in tree order, follow the path
    //   per row                  path / parts / position / spacing / rotate mode / mixes
    // In Animate mode each constraint's Clip Mix (and a path's position) can be keyed with ◆.
    public sealed partial class SpriteSheetToolWindow
    {
        static readonly Color PathColor = new Color(0.35f, 0.85f, 1f, 0.95f);
        int _pathPointDrag = -1;
        int _pathDragControl;

        // ---- Shared rows ----

        /// <summary>Slot popups: every part, or only path parts.</summary>
        void PartsSlotChoices(bool pathsOnly, out string[] names, out List<string> ids)
        {
            var list = new List<string> { "(none)" };
            ids = new List<string> { string.Empty };
            foreach (var s in _profile.PartsSlots)
            {
                if (s == null || (pathsOnly && !s.IsPath))
                    continue;
                list.Add((s.IsBone ? "◇ " : "") + s.Name);
                ids.Add(SpritePartIdUtility.Canonical(s.SlotId));
            }
            names = list.ToArray();
        }

        string PartNames(List<string> ids)
        {
            if (ids == null || ids.Count == 0)
                return "No parts: select parts, then Set.";
            var names = new List<string>();
            foreach (string id in ids)
            {
                var s = SpritePartsAuthoringOps.FindSlot(_profile, id ?? string.Empty);
                if (s != null)
                    names.Add(s.Name);
            }
            return names.Count + " part" + (names.Count == 1 ? "" : "s") + ": " + string.Join(" → ", names);
        }

        /// <summary>The selected parts in tree order, skipping <paramref name="exclude"/>.</summary>
        List<string> SelectedPartsInOrder(string exclude)
        {
            var selected = SelectedSlotIdsForMask();
            var result = new List<string>();
            string skip = SpritePartIdUtility.Canonical(exclude ?? string.Empty);
            foreach (var s in _profile.PartsSlots)
            {
                if (s == null)
                    continue;
                string id = SpritePartIdUtility.Canonical(s.SlotId);
                if (id != skip && selected.Contains(id))
                    result.Add(id);
            }
            return result;
        }

        /// <summary>
        /// A keyable overall mix (Animate mode): the clip's keyed value at the playhead, 1 when unkeyed. Changing it keys it.
        /// </summary>
        void DrawClipMixRow(SpritePartsValueKind kind, string target)
        {
            if (ValueKeyClip == null)
                return;
            TryKeyedValue(kind, target, 1f, out float shown);
            EditorGUILayout.BeginHorizontal();
            float next = EditorGUILayout.Slider(new GUIContent("Clip Mix",
                "This clip's overall strength for the constraint (scales the mixes below). Changing it keys it at the playhead."),
                shown, 0f, 1f);
            ValueKeyButton(kind, target, shown, "Constraint Mix");
            EditorGUILayout.EndHorizontal();
            if (Mathf.Approximately(next, shown))
                return;
            RecordPartsUndo("Key Constraint Mix");
            SpritePartsAuthoringOps.SetValueKey(ValueKeyClip, kind, target, _partsPreviewTime, next);
            SaveDirty();
        }

        // ---- Transform constraints ----

        void DrawPartsTransformConstraintsInspector()
        {
            if (_profile == null)
                return;
            var list = _profile.PartsTransformConstraints ??= new List<SpritePartsTransformConstraintDef>();
            if (!PartsSection("TRANSFORM CONSTRAINTS", list.Count == 0 ? "none" : list.Count + " constraint" + (list.Count == 1 ? "" : "s")))
                return;
            if (GUILayout.Button(new GUIContent("Add for Selected Parts",
                    "The selected parts copy a target part's rotation / position / scale (pick the Target below).")))
            {
                RecordPartsUndo("Add Transform Constraint");
                int n = list.Count + 1;
                string name = "Transform " + n;
                while (list.Exists(c => c != null && c.Name == name))
                    name = "Transform " + ++n;
                list.Add(new SpritePartsTransformConstraintDef { Name = name, BoneSlotIds = SelectedPartsInOrder(null) });
                SaveDirty();
            }
            if (list.Count == 0)
            {
                EditorGUILayout.LabelField("Parts that copy another part: a shadow following the body, eyes turning together.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }
            PartsSlotChoices(false, out var names, out var ids);
            for (int c = 0; c < list.Count; c++)
            {
                var tc = list[c];
                if (tc == null)
                    continue;
                tc.BoneSlotIds ??= new List<string>();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                bool enabled = EditorGUILayout.Toggle(tc.Enabled, GUILayout.Width(16f));
                string name = EditorGUILayout.TextField(tc.Name);
                bool remove = GUILayout.Button(new GUIContent("×", "Remove this constraint"), GUILayout.Width(22f));
                EditorGUILayout.EndHorizontal();
                int target = EditorGUILayout.Popup(new GUIContent("Target", "The part to copy"),
                    Mathf.Max(0, ids.IndexOf(SpritePartIdUtility.Canonical(tc.TargetSlotId ?? string.Empty))), names);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(PartNames(tc.BoneSlotIds), EditorStyles.wordWrappedMiniLabel);
                bool setParts = GUILayout.Button(new GUIContent("Set", "The constrained parts become the selected parts"),
                    EditorStyles.miniButton, GUILayout.Width(34f));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                int space = GUILayout.Toolbar(tc.Local ? 1 : 0, new[]
                {
                    new GUIContent("Character", "Match where the target is on the character"),
                    new GUIContent("Local", "Copy the target's own (local) values"),
                }, EditorStyles.miniButton);
                int relative = GUILayout.Toolbar(tc.Relative ? 1 : 0, new[]
                {
                    new GUIContent("Absolute", "Match the target"),
                    new GUIContent("Relative", "Add the target's change from its setup pose"),
                }, EditorStyles.miniButton);
                EditorGUILayout.EndHorizontal();
                float mixRotate = EditorGUILayout.Slider("Rotate", tc.MixRotate, 0f, 1f);
                float mixX = EditorGUILayout.Slider("X", tc.MixX, 0f, 1f);
                float mixY = EditorGUILayout.Slider("Y", tc.MixY, 0f, 1f);
                float mixScaleX = EditorGUILayout.Slider("Scale X", tc.MixScaleX, 0f, 1f);
                float mixScaleY = EditorGUILayout.Slider("Scale Y", tc.MixScaleY, 0f, 1f);
                float offRot = EditorGUILayout.FloatField(new GUIContent("Offset Rot", "Added to the target's rotation"), tc.OffsetRotation);
                Vector2 offPos = EditorGUILayout.Vector2Field(new GUIContent("Offset Pos", "Added to the target's position"), tc.OffsetPosition);
                Vector2 offScale = EditorGUILayout.Vector2Field(new GUIContent("Offset Scale", "Added to the target's scale"), tc.OffsetScale);
                bool changed = EditorGUI.EndChangeCheck();
                DrawClipMixRow(SpritePartsValueKind.TransformMix, tc.Name);
                string hint = string.IsNullOrEmpty(ids[target]) ? "Pick a target." : tc.BoneSlotIds.Count == 0 ? "Set the parts that follow it." : null;
                if (hint != null)
                    EditorGUILayout.LabelField(hint, EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                if (!changed)
                    continue;
                RecordPartsUndo(remove ? "Remove Transform Constraint" : "Edit Transform Constraint");
                if (remove)
                {
                    SpritePartsAuthoringOps.RemoveValueTracks(_profile, tc.Name, SpritePartsValueKind.TransformMix);
                    list.RemoveAt(c);
                    SaveDirty();
                    break;
                }
                if (name != tc.Name)
                    SpritePartsAuthoringOps.RenameValueTarget(_profile, tc.Name, name, SpritePartsValueKind.TransformMix);
                tc.Enabled = enabled;
                tc.Name = name;
                tc.TargetSlotId = ids[target];
                if (setParts)
                    tc.BoneSlotIds = SelectedPartsInOrder(tc.TargetSlotId);
                tc.Local = space == 1;
                tc.Relative = relative == 1;
                tc.MixRotate = mixRotate;
                tc.MixX = mixX;
                tc.MixY = mixY;
                tc.MixScaleX = mixScaleX;
                tc.MixScaleY = mixScaleY;
                tc.OffsetRotation = offRot;
                tc.OffsetPosition = offPos;
                tc.OffsetScale = offScale;
                SaveDirty();
            }
        }

        // ---- Path constraints ----

        void AddPartsPath()
        {
            var parent = CurrentPartsSlot;
            RecordPartsUndo("Add Path");
            var result = parent != null
                ? SpritePartsAuthoringOps.TryAddChildPart(_profile, parent.SlotId, out var path)
                : SpritePartsAuthoringOps.TryAddPart(_profile, string.Empty, out path);
            if (path == null)
            {
                _status = result.Reason;
                return;
            }
            path.IsPath = true;
            path.DefaultAppearanceId = string.Empty;
            path.PathPoints = new[] { new Vector2(-1f, 0f), new Vector2(-0.35f, 0.6f), new Vector2(0.35f, 0.6f), new Vector2(1f, 0f) };
            SpritePartsAuthoringOps.TryRenameDisplayName(_profile, path.SlotId,
                SpritePartsAuthoringOps.UniqueSiblingDisplayName(_profile, path.ParentSlotId, "Path"));
            SaveDirty();
            SelectPartsSlotId(path.SlotId, false, false);
            _status = "Path added: drag its points in Rig mode (click the curve to add one, right-click to delete).";
        }

        void DrawPartsPathConstraintsInspector()
        {
            if (_profile == null)
                return;
            var list = _profile.PartsPathConstraints ??= new List<SpritePartsPathConstraintDef>();
            if (!PartsSection("PATH CONSTRAINTS", list.Count == 0 ? "none" : list.Count + " constraint" + (list.Count == 1 ? "" : "s")))
                return;
            PartsSlotChoices(true, out var pathNames, out var pathIds);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Add Path Part", "A curve (under the selected part) that parts can follow")))
                AddPartsPath();
            using (new EditorGUI.DisabledScope(pathIds.Count < 2))
            {
                if (GUILayout.Button(new GUIContent("Add for Selected Parts", "The selected parts, in tree order, sit along a path")))
                {
                    RecordPartsUndo("Add Path Constraint");
                    int n = list.Count + 1;
                    string name = "Path " + n;
                    while (list.Exists(c => c != null && c.Name == name))
                        name = "Path " + ++n;
                    string pathId = CurrentPartsSlot != null && CurrentPartsSlot.IsPath ? SpritePartIdUtility.Canonical(CurrentPartsSlot.SlotId) : pathIds[1];
                    list.Add(new SpritePartsPathConstraintDef { Name = name, PathSlotId = pathId, BoneSlotIds = SelectedPartsInOrder(pathId) });
                    SaveDirty();
                }
            }
            EditorGUILayout.EndHorizontal();
            if (list.Count == 0)
            {
                EditorGUILayout.LabelField("A chain along a curve: a snake, a tail, a rope, text on a path. Key its position to slide it.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }
            for (int c = 0; c < list.Count; c++)
            {
                var pc = list[c];
                if (pc == null)
                    continue;
                pc.BoneSlotIds ??= new List<string>();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                bool enabled = EditorGUILayout.Toggle(pc.Enabled, GUILayout.Width(16f));
                string name = EditorGUILayout.TextField(pc.Name);
                bool remove = GUILayout.Button(new GUIContent("×", "Remove this constraint (the path part stays)"), GUILayout.Width(22f));
                EditorGUILayout.EndHorizontal();
                int path = EditorGUILayout.Popup(new GUIContent("Path", "The path part to follow"),
                    Mathf.Max(0, pathIds.IndexOf(SpritePartIdUtility.Canonical(pc.PathSlotId ?? string.Empty))), pathNames);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(PartNames(pc.BoneSlotIds), EditorStyles.wordWrappedMiniLabel);
                bool setParts = GUILayout.Button(new GUIContent("Set", "The chain becomes the selected parts (in tree order)"),
                    EditorStyles.miniButton, GUILayout.Width(34f));
                EditorGUILayout.EndHorizontal();
                bool positionKeyed = TryKeyedValue(SpritePartsValueKind.PathPosition, pc.Name, pc.Position, out float positionShown);
                EditorGUILayout.BeginHorizontal();
                float position = EditorGUILayout.Slider(new GUIContent("Position", "Where the first part sits: 0 = start, 1 = end"),
                    positionShown, 0f, 1f);
                ValueKeyButton(SpritePartsValueKind.PathPosition, pc.Name, positionShown, "Path Position");
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                float spacing = EditorGUILayout.FloatField(new GUIContent("Spacing",
                    "Gap between parts: a fraction of the path (Percent) or world units (Fixed)"), pc.Spacing);
                var spacingMode = (SpritePartsPathSpacing)EditorGUILayout.EnumPopup(pc.SpacingMode, GUILayout.Width(70f));
                EditorGUILayout.EndHorizontal();
                var rotate = (SpritePartsPathRotate)GUILayout.Toolbar((int)pc.RotateMode, new[]
                {
                    new GUIContent("Tangent", "Turn with the path"),
                    new GUIContent("Chain", "Point at the next part"),
                    new GUIContent("None", "Move without turning"),
                }, EditorStyles.miniButton);
                float offRot = EditorGUILayout.FloatField(new GUIContent("Offset Rot", "Added to the path's direction"), pc.OffsetRotation);
                float mixRotate = EditorGUILayout.Slider("Rotate", pc.MixRotate, 0f, 1f);
                float mixTranslate = EditorGUILayout.Slider("Translate", pc.MixTranslate, 0f, 1f);
                bool changed = EditorGUI.EndChangeCheck();
                DrawClipMixRow(SpritePartsValueKind.PathMix, pc.Name);
                if (string.IsNullOrEmpty(pathIds[path]))
                    EditorGUILayout.LabelField("Pick a path part (Add Path Part makes one).", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                if (!changed)
                    continue;
                bool positionToKey = !Mathf.Approximately(position, positionShown)
                                     && KeyEditedValue(SpritePartsValueKind.PathPosition, pc.Name, position, "Path Position");
                RecordPartsUndo(remove ? "Remove Path Constraint" : "Edit Path Constraint");
                if (remove)
                {
                    SpritePartsAuthoringOps.RemoveValueTracks(_profile, pc.Name, SpritePartsValueKind.PathPosition, SpritePartsValueKind.PathMix);
                    list.RemoveAt(c);
                    SaveDirty();
                    break;
                }
                if (name != pc.Name)
                    SpritePartsAuthoringOps.RenameValueTarget(_profile, pc.Name, name, SpritePartsValueKind.PathPosition, SpritePartsValueKind.PathMix);
                pc.Enabled = enabled;
                pc.Name = name;
                pc.PathSlotId = pathIds[path];
                if (setParts)
                    pc.BoneSlotIds = SelectedPartsInOrder(pc.PathSlotId);
                if (!positionKeyed && !positionToKey)
                    pc.Position = position;
                pc.Spacing = spacing;
                pc.SpacingMode = spacingMode;
                pc.RotateMode = rotate;
                pc.OffsetRotation = offRot;
                pc.MixRotate = mixRotate;
                pc.MixTranslate = mixTranslate;
                SaveDirty();
            }
        }

        /// <summary>PATH section for a selected path part: closed or open, point count.</summary>
        void DrawPartsPathPartInspector(SpritePartSlotDef slot, bool partLocked)
        {
            if (slot == null || !slot.IsPath || !PartsSection("PATH", (slot.PathPoints?.Length ?? 0) + " points"))
                return;
            using (new EditorGUI.DisabledScope(partLocked))
            {
                EditorGUI.BeginChangeCheck();
                bool closed = EditorGUILayout.ToggleLeft(new GUIContent("Closed", "Join the last point back to the first (a loop)"), slot.PathClosed);
                if (EditorGUI.EndChangeCheck())
                {
                    RecordPartsUndo("Path Closed");
                    slot.PathClosed = closed;
                    SaveDirty();
                }
            }
            EditorGUILayout.LabelField("Rig mode: drag points; click the curve to add one; right-click a point to delete it.",
                EditorStyles.wordWrappedMiniLabel);
        }

        // ---- Canvas ----

        /// <summary>A path part's curve through its points in root space (sampled).</summary>
        static List<Vector2> PathCurve(float4x4 m, Vector2[] points, bool closed, out List<int> segmentOf)
        {
            var result = new List<Vector2>();
            segmentOf = new List<int>();
            int n = points?.Length ?? 0;
            if (n < 2)
                return result;
            closed &= n >= 3;
            int segments = closed ? n : n - 1;
            float2 P(int i) => closed ? (float2)points[((i % n) + n) % n] : (float2)points[Mathf.Clamp(i, 0, n - 1)];
            for (int s = 0; s < segments; s++)
            {
                for (int j = s == 0 ? 0 : 1; j <= 16; j++)
                {
                    float2 q = SpritePartsConstraints.CatmullRom(P(s - 1), P(s), P(s + 1), P(s + 2), j / 16f);
                    result.Add(math.mul(m, new float4(q, 0f, 1f)).xy);
                    segmentOf.Add(s);
                }
            }
            return result;
        }

        void DrawPartsPath(Rect canvas, float4x4 localToRoot, SpritePartSlotDef def, bool selected)
        {
            if (Event.current.type != EventType.Repaint || def.PathPoints == null || def.PathPoints.Length < 2)
                return;
            var curve = PathCurve(localToRoot, def.PathPoints, def.PathClosed, out _);
            var pts = new Vector3[curve.Count];
            for (int i = 0; i < curve.Count; i++)
                pts[i] = WorldToCanvas(canvas, curve[i]);
            Handles.BeginGUI();
            var color = selected ? PathColor : new Color(PathColor.r, PathColor.g, PathColor.b, 0.5f);
            Handles.color = color;
            Handles.DrawAAPolyLine(selected ? 2.5f : 1.5f, pts);
            if (selected)
            {
                for (int i = 0; i < def.PathPoints.Length; i++)
                {
                    var p = WorldToCanvas(canvas, math.mul(localToRoot, new float4(def.PathPoints[i].x, def.PathPoints[i].y, 0f, 1f)).xy);
                    bool hot = i == _pathPointDrag;
                    EditorGUI.DrawRect(new Rect(p.x - 4f, p.y - 4f, 8f, 8f), hot ? Color.white : i == 0 ? new Color(0.3f, 1f, 0.5f) : PathColor);
                }
            }
            Handles.EndGUI();
        }

        /// <summary>Canvas mouse on the selected path part's points (Rig). True when it used the event.</summary>
        bool HandlePathInput(Rect canvas, Event evt)
        {
            var slot = CurrentPartsSlot;
            if (slot == null || !slot.IsPath || slot.PathPoints == null || _partsMode != SpritePartsStudioMode.Rig
                || slot.EditorLocked || evt.alt)
            {
                _pathPointDrag = -1;
                return false;
            }
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (_pathPointDrag >= 0 && GUIUtility.hotControl == _pathDragControl)
            {
                if (evt.rawType == EventType.MouseDrag && TryClipShapeMatrix(slot.SlotId, out var dm))
                {
                    float2 local = math.mul(math.inverse(dm), new float4(CanvasToWorld(canvas, evt.mousePosition), 0f, 1f)).xy;
                    if ((uint)_pathPointDrag < (uint)slot.PathPoints.Length)
                        slot.PathPoints[_pathPointDrag] = new Vector2(local.x, local.y);
                    if (_asset != null)
                        EditorUtility.SetDirty(_asset);
                    evt.Use();
                    Repaint();
                    return true;
                }
                if (evt.rawType == EventType.MouseUp)
                {
                    _pathPointDrag = -1;
                    GUIUtility.hotControl = 0;
                    EndPartsDragUndo();
                    evt.Use();
                    Repaint();
                    return true;
                }
            }
            bool down = evt.type == EventType.MouseDown && (evt.button == 0 || evt.button == 1);
            bool context = evt.type == EventType.ContextClick;
            if ((!down && !context) || !canvas.Contains(evt.mousePosition) || !TryClipShapeMatrix(slot.SlotId, out var m))
                return false;
            int n = slot.PathPoints.Length;
            int point = -1;
            float best = 8f * 8f;
            for (int i = 0; i < n; i++)
            {
                Vector2 g = WorldToCanvas(canvas, math.mul(m, new float4(slot.PathPoints[i].x, slot.PathPoints[i].y, 0f, 1f)).xy);
                float d = (g - evt.mousePosition).sqrMagnitude;
                if (d <= best)
                {
                    best = d;
                    point = i;
                }
            }
            if (context || evt.button == 1)
            {
                if (point < 0)
                    return false;
                if (n <= 2)
                    _status = "A path needs at least 2 points.";
                else
                {
                    RecordPartsUndo("Delete Path Point");
                    var list = new List<Vector2>(slot.PathPoints);
                    list.RemoveAt(point);
                    slot.PathPoints = list.ToArray();
                    SaveDirty();
                }
                evt.Use();
                Repaint();
                return true;
            }
            if (point < 0)
            {
                // On the curve: add a point there (between the points of that stretch) and drag it.
                var curve = PathCurve(m, slot.PathPoints, slot.PathClosed, out var segmentOf);
                for (int i = 0; i + 1 < curve.Count && point < 0; i++)
                {
                    Vector2 a = WorldToCanvas(canvas, curve[i]), b = WorldToCanvas(canvas, curve[i + 1]);
                    if (HandleUtility.DistancePointToLineSegment(evt.mousePosition, a, b) > 5f)
                        continue;
                    RecordPartsUndo("Add Path Point");
                    float2 local = math.mul(math.inverse(m), new float4(CanvasToWorld(canvas, evt.mousePosition), 0f, 1f)).xy;
                    var list = new List<Vector2>(slot.PathPoints);
                    int at = segmentOf[i + 1] + 1;
                    list.Insert(at, new Vector2(local.x, local.y));
                    slot.PathPoints = list.ToArray();
                    SaveDirty();
                    point = at;
                }
                if (point < 0)
                    return false;
            }
            else
            {
                BeginPartsDragUndo("Move Path Point");
                FlushPartsDragUndo();
            }
            _pathPointDrag = point;
            _pathDragControl = control;
            GUIUtility.hotControl = control;
            evt.Use();
            Repaint();
            return true;
        }
    }
}
