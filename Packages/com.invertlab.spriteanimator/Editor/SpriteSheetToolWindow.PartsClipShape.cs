using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Clip shapes (Spine's clipping attachment): an invisible polygon part that clips every part drawn above it, up to
    // its End part. It follows its parent like any part and can be switched on / off with clip keys.
    //   + Clip (tree)   a clip shape fitted to the selected part's image, drawn just below it, End = that part
    //   canvas (Rig)    drag a point; click an edge to add one; right-click a point to delete it
    //   canvas (Animate) drag a point to key the outline's deform at the playhead (the setup outline stays)
    //   CLIP SHAPE      End part, Fit to End, reset
    public sealed partial class SpriteSheetToolWindow
    {
        static readonly Color ClipShapeColor = new Color(1f, 0.35f, 0.55f, 0.95f);
        static readonly Color BoundingBoxColor = new Color(1f, 0.85f, 0.25f, 0.95f);
        static readonly Color PointColor = new Color(1f, 0.6f, 0.2f, 0.95f);
        int _clipPointDrag = -1;
        int _clipDragControl;

        void AddPartsClipShape()
        {
            var target = _partsSelectedSlotIds.Count == 1 ? CurrentPartsSlot : null;
            if (target != null && (target.IsBone || target.IsClipShape))
                target = null;
            RecordPartsUndo("Add Clip Shape");
            var result = target != null
                ? SpritePartsAuthoringOps.TryAddChildPart(_profile, target.SlotId, out var clip)
                : SpritePartsAuthoringOps.TryAddPart(_profile, string.Empty, out clip);
            if (clip == null)
            {
                _status = result.Reason;
                return;
            }
            clip.IsClipShape = true;
            clip.DefaultAppearanceId = string.Empty;
            SpritePartsAuthoringOps.TryRenameDisplayName(_profile, clip.SlotId,
                SpritePartsAuthoringOps.UniqueSiblingDisplayName(_profile, clip.ParentSlotId, target != null ? target.Name + " Clip" : "Clip"));
            // Drawn just below the target, so the target (and nothing else yet) is clipped.
            int rank = target != null ? target.DrawRank : 0;
            foreach (var s in _profile.PartsSlots)
                if (s != null && !ReferenceEquals(s, clip) && s.DrawRank >= rank)
                    s.DrawRank++;
            clip.DrawRank = rank;
            clip.ClipEndSlotId = target != null ? SpritePartIdUtility.Canonical(target.SlotId) : string.Empty;
            clip.ClipPolygon = target != null && SpritePartsSkinning.TryResolveQuad(_profile, target, out var size, out var pivot)
                ? ImageRect(size, pivot)
                : new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
            if (target != null)
                _partsExpandedSlotIds.Add(SpritePartIdUtility.Canonical(target.SlotId));
            SaveDirty();
            SelectPartsSlotId(clip.SlotId, false, false);
            _status = target != null
                ? "Clip shape added: " + target.Name + " shows only inside it. Drag its points (Rig) to shape it."
                : "Clip shape added: it clips every part above it. Drag its points (Rig) to shape it.";
        }

        static Vector2[] ImageRect(float2 size, float2 pivot)
        {
            Vector2 C(float qx, float qy) => new Vector2((qx + 0.5f - pivot.x) * size.x, (qy + 0.5f - pivot.y) * size.y);
            return new[] { C(-0.5f, -0.5f), C(0.5f, -0.5f), C(0.5f, 0.5f), C(-0.5f, 0.5f) };
        }

        /// <summary>The shape on the canvas: a pink outline (points shown when selected), with its keyed deform.</summary>
        void DrawPartsClipShape(Rect canvas, float4x4 localToRoot, SpritePartSlotDef def, bool selected,
            in FixedList512Bytes<float2> offsets = default)
        {
            if (Event.current.type != EventType.Repaint || def.ClipPolygon == null || def.ClipPolygon.Length < 2)
                return;
            int n = def.ClipPolygon.Length;
            var pts = new Vector3[n + 1];
            for (int i = 0; i < n; i++)
            {
                float2 p = (float2)def.ClipPolygon[i] + (offsets.Length == n ? offsets[i] : float2.zero);
                pts[i] = WorldToCanvas(canvas, math.mul(localToRoot, new float4(p, 0f, 1f)).xy);
            }
            pts[n] = pts[0];
            Handles.BeginGUI();
            var baseColor = def.IsBoundingBox ? BoundingBoxColor : ClipShapeColor;
            var color = selected ? baseColor : new Color(baseColor.r, baseColor.g, baseColor.b, 0.45f);
            Handles.color = color;
            if (selected)
                Handles.DrawAAPolyLine(2f, pts);
            else
                for (int i = 0; i < n; i++)
                    Handles.DrawDottedLine(pts[i], pts[i + 1], 4f);
            if (selected)
            {
                for (int i = 0; i < n; i++)
                {
                    bool hot = i == _clipPointDrag;
                    EditorGUI.DrawRect(new Rect(pts[i].x - 4f, pts[i].y - 4f, 8f, 8f), hot ? Color.white : baseColor);
                }
            }
            Handles.EndGUI();
        }

        /// <summary>The selected clip shape's matrix at the playhead (root space).</summary>
        bool TryClipShapeMatrix(string slotId, out float4x4 m) => TryClipShapeMatrix(slotId, 0, out m, out _);

        /// <summary>The matrix plus the outline's keyed deform offsets at the playhead (<paramref name="points"/> of them; null = none).</summary>
        bool TryClipShapeMatrix(string slotId, int points, out float4x4 m, out Vector2[] offsets)
        {
            m = float4x4.identity;
            offsets = null;
            if (!SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), _partsPreviewTime,
                    Allocator.Temp, out var blob, out var poses, out var matrices, out _))
                return false;
            try
            {
                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                int idx = BlobSlotIndex(ref blob.Value, slotId);
                if (idx < 0 || idx >= matrices.Length)
                    return false;
                m = matrices[idx];
                if (points > 0 && SpritePartsSampler.SampleDeformOffsets(ref blob.Value, PartsEvaluationClipIndex(), idx, _partsPreviewTime,
                        points, out var keyed))
                {
                    offsets = new Vector2[points];
                    for (int i = 0; i < points; i++)
                        offsets[i] = keyed[i];
                }
                return true;
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        /// <summary>
        /// Canvas mouse on the selected clip shape's points: Rig edits the setup outline, Animate keys its deform at
        /// the playhead. True when it used the event.
        /// </summary>
        bool HandleClipShapeInput(Rect canvas, Event evt)
        {
            var slot = CurrentPartsSlot;
            bool animate = _partsMode == SpritePartsStudioMode.Animate && CurrentPartsClip != null;
            if (slot == null || !(slot.IsClipShape || slot.IsBoundingBox) || slot.ClipPolygon == null
                || (_partsMode != SpritePartsStudioMode.Rig && !animate)
                || slot.EditorLocked || evt.alt)
            {
                _clipPointDrag = -1;
                return false;
            }
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (_clipPointDrag >= 0 && GUIUtility.hotControl == _clipDragControl)
            {
                if (evt.rawType == EventType.MouseDrag && TryClipShapeMatrix(slot.SlotId, slot.ClipPolygon.Length, out var dm, out var keyed))
                {
                    float2 local = math.mul(math.inverse(dm), new float4(CanvasToWorld(canvas, evt.mousePosition), 0f, 1f)).xy;
                    if (animate && (uint)_clipPointDrag < (uint)slot.ClipPolygon.Length)
                    {
                        var offsets = keyed ?? new Vector2[slot.ClipPolygon.Length];
                        offsets[_clipPointDrag] = new Vector2(local.x, local.y) - slot.ClipPolygon[_clipPointDrag];
                        SpritePartsAuthoringOps.SetDeformKey(_profile, _partsSelectedClip, slot.SlotId, _partsPreviewTime, offsets);
                    }
                    else if ((uint)_clipPointDrag < (uint)slot.ClipPolygon.Length)
                        slot.ClipPolygon[_clipPointDrag] = new Vector2(local.x, local.y);
                    if (_asset != null)
                        EditorUtility.SetDirty(_asset);
                    evt.Use();
                    Repaint();
                    return true;
                }
                if (evt.rawType == EventType.MouseUp)
                {
                    _clipPointDrag = -1;
                    GUIUtility.hotControl = 0;
                    EndPartsDragUndo();
                    evt.Use();
                    Repaint();
                    return true;
                }
            }
            bool down = evt.type == EventType.MouseDown && (evt.button == 0 || evt.button == 1);
            bool context = evt.type == EventType.ContextClick;
            if ((!down && !context) || !canvas.Contains(evt.mousePosition)
                || !TryClipShapeMatrix(slot.SlotId, slot.ClipPolygon.Length, out var m, out var shown))
                return false;
            int n = slot.ClipPolygon.Length;
            var gui = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 p = slot.ClipPolygon[i] + (shown != null ? shown[i] : Vector2.zero);
                gui[i] = WorldToCanvas(canvas, math.mul(m, new float4(p.x, p.y, 0f, 1f)).xy);
            }
            int point = -1;
            float best = 8f * 8f;
            for (int i = 0; i < n; i++)
            {
                float d = (gui[i] - evt.mousePosition).sqrMagnitude;
                if (d <= best)
                {
                    best = d;
                    point = i;
                }
            }
            if (animate && (context || evt.button == 1 || point < 0))
                return false; // Animate only moves points (adding / deleting changes the setup outline)
            if (context || evt.button == 1)
            {
                if (point < 0)
                    return false; // not on a point: the normal canvas menu
                if (n <= 3)
                    _status = "An outline needs at least 3 points.";
                else
                {
                    RecordPartsUndo("Delete Clip Point");
                    var list = new List<Vector2>(slot.ClipPolygon);
                    list.RemoveAt(point);
                    slot.ClipPolygon = list.ToArray();
                    SaveDirty();
                }
                evt.Use();
                Repaint();
                return true;
            }
            if (point < 0)
            {
                // On an edge: add a point there and drag it.
                for (int i = 0; i < n && point < 0; i++)
                {
                    Vector2 a = gui[i], b = gui[(i + 1) % n];
                    if (HandleUtility.DistancePointToLineSegment(evt.mousePosition, a, b) > 5f)
                        continue;
                    RecordPartsUndo("Add Clip Point");
                    float2 local = math.mul(math.inverse(m), new float4(CanvasToWorld(canvas, evt.mousePosition), 0f, 1f)).xy;
                    var list = new List<Vector2>(slot.ClipPolygon);
                    list.Insert(i + 1, new Vector2(local.x, local.y));
                    slot.ClipPolygon = list.ToArray();
                    SaveDirty();
                    point = i + 1;
                }
                if (point < 0)
                    return false; // inside or outside the shape: normal selection / move
            }
            else
            {
                BeginPartsDragUndo(animate ? "Key Clip Deform" : "Move Clip Point");
                FlushPartsDragUndo();
            }
            _clipPointDrag = point;
            _clipDragControl = control;
            GUIUtility.hotControl = control;
            evt.Use();
            Repaint();
            return true;
        }

        /// <summary>A point part: a crosshair with an arrow along its X axis (its direction).</summary>
        void DrawPartsPoint(Rect canvas, float4x4 localToRoot, bool selected)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            Vector2 at = WorldToCanvas(canvas, localToRoot.c3.xy);
            float2 dir = math.normalizesafe(localToRoot.c0.xy, new float2(1f, 0f));
            Vector2 tip = at + new Vector2(dir.x, -dir.y) * 26f;
            Handles.BeginGUI();
            Handles.color = selected ? PointColor : new Color(PointColor.r, PointColor.g, PointColor.b, 0.5f);
            Handles.DrawWireDisc(at, Vector3.forward, 6f);
            Handles.DrawAAPolyLine(selected ? 2.5f : 1.5f, at, tip);
            Vector2 side = new Vector2(dir.y, dir.x) * 5f;
            Handles.DrawAAConvexPolygon(tip + new Vector2(dir.x, -dir.y) * 6f, tip + side, tip - side);
            Handles.EndGUI();
        }

        void AddPartsAttachment(bool box)
        {
            var parent = CurrentPartsSlot;
            RecordPartsUndo(box ? "Add Bounding Box" : "Add Point");
            var result = parent != null
                ? SpritePartsAuthoringOps.TryAddChildPart(_profile, parent.SlotId, out var part)
                : SpritePartsAuthoringOps.TryAddPart(_profile, string.Empty, out part);
            if (part == null)
            {
                _status = result.Reason;
                return;
            }
            part.DefaultAppearanceId = string.Empty;
            if (box)
            {
                part.IsBoundingBox = true;
                part.ClipPolygon = parent != null && SpritePartsSkinning.TryResolveQuad(_profile, parent, out var size, out var pivot)
                    ? ImageRect(size, pivot)
                    : new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
            }
            else
                part.IsPoint = true;
            string baseName = (parent != null ? parent.Name + " " : "") + (box ? "Box" : "Point");
            SpritePartsAuthoringOps.TryRenameDisplayName(_profile, part.SlotId,
                SpritePartsAuthoringOps.UniqueSiblingDisplayName(_profile, part.ParentSlotId, baseName));
            SaveDirty();
            SelectPartsSlotId(part.SlotId, false, false);
            _status = box
                ? "Bounding box added: shape it in Rig (drag points, click an edge to add). SpriteParts.BoundingBoxContains(..., \"" + part.Name + "\", point)."
                : "Point added: move and turn it like any part. SpriteParts.TryGetPoint(..., \"" + part.Name + "\", out pos, out angle).";
        }

        /// <summary>POINTS & BOXES: add them; for a selected one, how the game reads it.</summary>
        void DrawPartsAttachmentsInspector()
        {
            if (_profile == null || !PartsSection("POINTS & BOXES", "points and hit boxes"))
                return;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Add Point", "An invisible spot with a direction under the selected part (muzzle, grip, foot)")))
                AddPartsAttachment(false);
            if (GUILayout.Button(new GUIContent("Add Bounding Box", "An invisible outline under the selected part for hit tests")))
                AddPartsAttachment(true);
            EditorGUILayout.EndHorizontal();
            var slot = CurrentPartsSlot;
            if (slot != null && slot.IsPoint)
                EditorGUILayout.LabelField("In the game: SpriteParts.TryGetPoint(em, e, \"" + slot.Name + "\", out pos, out angle).", EditorStyles.wordWrappedMiniLabel);
            else if (slot != null && slot.IsBoundingBox)
                EditorGUILayout.LabelField("In the game: SpriteParts.BoundingBoxContains(em, e, \"" + slot.Name + "\", point) or GetBoundingBox. " +
                                           "Rig shapes it; Animate keys its deform.", EditorStyles.wordWrappedMiniLabel);
            else
                EditorGUILayout.LabelField("Points and boxes follow their parent part and can be keyed like any part.", EditorStyles.wordWrappedMiniLabel);
        }

        void DrawPartsClipShapeInspector(SpritePartSlotDef slot, bool partLocked)
        {
            if (slot == null || !slot.IsClipShape)
                return;
            var end = SpritePartsAuthoringOps.FindSlot(_profile, slot.ClipEndSlotId ?? string.Empty);
            if (!PartsSection("CLIP SHAPE", end != null ? "to " + end.Name : "everything above"))
                return;
            EditorGUILayout.LabelField("Invisible. Clips the parts drawn above it (Z order) up to the End part. Rig: drag a point, click an edge to add one, right-click a point to delete it.",
                EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUI.DisabledScope(partLocked))
            {
                var names = new List<string> { "(everything above)" };
                var ids = new List<string> { string.Empty };
                foreach (var s in _profile.PartsSlots)
                {
                    if (s == null || s.IsBone || s.IsClipShape)
                        continue;
                    names.Add(s.Name + "  (rank " + s.DrawRank + ")");
                    ids.Add(SpritePartIdUtility.Canonical(s.SlotId));
                }
                int current = Mathf.Max(0, ids.IndexOf(SpritePartIdUtility.Canonical(slot.ClipEndSlotId ?? string.Empty)));
                int next = EditorGUILayout.Popup(new GUIContent("End", "The last part (in draw order) this shape clips"), current, names.ToArray());
                if (next != current)
                {
                    RecordPartsUndo("Clip Shape End");
                    slot.ClipEndSlotId = ids[next];
                    SaveDirty();
                }
                if (end != null && end.DrawRank <= slot.DrawRank)
                    EditorGUILayout.HelpBox("The End part is drawn below this shape, so nothing is clipped. Move the shape below it (Z order).", MessageType.Warning);
                EditorGUILayout.LabelField("Points", (slot.ClipPolygon?.Length ?? 0).ToString());
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(end == null))
                {
                    if (GUILayout.Button(new GUIContent("Fit to End", "The End part's image rectangle, where it is now")))
                        FitClipShapeTo(slot, end);
                }
                if (GUILayout.Button(new GUIContent("Reset", "A 1 x 1 square around the joint")))
                {
                    RecordPartsUndo("Reset Clip Shape");
                    slot.ClipPolygon = new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
                    SaveDirty();
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>Sets the shape to <paramref name="target"/>'s image rectangle, in the shape's own space, at the playhead.</summary>
        void FitClipShapeTo(SpritePartSlotDef slot, SpritePartSlotDef target)
        {
            if (target == null || !SpritePartsSkinning.TryResolveQuad(_profile, target, out var size, out var pivot))
            {
                _status = "The End part needs an image to fit to.";
                return;
            }
            if (!SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), _partsPreviewTime,
                    Allocator.Temp, out var blob, out var poses, out var matrices, out _))
                return;
            try
            {
                int si = BlobSlotIndex(ref blob.Value, slot.SlotId), ti = BlobSlotIndex(ref blob.Value, target.SlotId);
                if (si < 0 || ti < 0)
                    return;
                float4x4 toShape = math.mul(math.inverse(matrices[si]), matrices[ti]);
                var rect = ImageRect(size, pivot);
                for (int i = 0; i < rect.Length; i++)
                {
                    float2 p = math.mul(toShape, new float4(rect[i].x, rect[i].y, 0f, 1f)).xy;
                    rect[i] = new Vector2(p.x, p.y);
                }
                RecordPartsUndo("Fit Clip Shape");
                slot.ClipPolygon = rect;
                SaveDirty();
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        /// <summary>KEY section row for clip shapes: a clip key switches the shape on or off from this key on.</summary>
        void DrawPartsClipKeyRow(List<SpritePartsKeyDef> keys, SpritePartSlotDef slot)
        {
            if (slot == null || !slot.IsClipShape || keys.Count == 0)
                return;
            var first = keys[0];
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            bool has = EditorGUILayout.ToggleLeft(new GUIContent("Clip", "Clip key: from this key on the shape clips (On) or not (Off). Held until the next clip key."),
                first.HasClipActive, GUILayout.Width(70f));
            bool on;
            using (new EditorGUI.DisabledScope(!first.HasClipActive))
                on = GUILayout.Toolbar(first.HasClipActive && !first.ClipActive ? 1 : 0, new[] { "On", "Off" }, EditorStyles.miniButton) == 0;
            if (EditorGUI.EndChangeCheck())
                EditKeys(keys, "Key Clip", k =>
                {
                    k.HasClipActive = has;
                    k.ClipActive = on;
                });
            EditorGUILayout.EndHorizontal();
        }
    }
}
