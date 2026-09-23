using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Parts meshes, following the Spine workflow (esotericsoftware.com/spine-meshes):
    //   Edit Mesh (setup data on the slot): hull outline + interior vertices + optional edges,
    //   triangles always generated. Moving a vertex here slides the mesh over a flat image.
    //   Warp tool (Animate): drag vertices to deform. Keys store only per-vertex offsets.
    public sealed partial class SpriteSheetToolWindow
    {
        enum PartsMeshTool
        {
            Modify = 0,
            Create = 1,
            Delete = 2,
            Weights = 3,
        }

        enum PartsVertexTool
        {
            Translate = 0,
            Rotate = 1,
            Scale = 2,
        }

        const float PartsMeshHitRadius = 9f;
        const float PartsMeshEdgeSnap = 7f;

        string _partsMeshEditSlotId;
        [SerializeField] PartsMeshTool _partsMeshTool = PartsMeshTool.Create;
        int _partsWarpIndex = -1;
        int _partsWarpHover = -1;
        readonly List<int> _partsWarpSelection = new List<int>();
        string _partsWarpSelectionSlotId;
        bool _partsWarpActive;
        bool _partsWarpBox;
        Vector2 _partsWarpBoxStart;
        Vector2 _partsWarpBoxEnd;
        bool _partsMeshDrag;
        bool _partsMeshMoved;
        SpritePartMeshDef _partsMeshDragStart;
        int _partsMeshEdgeFrom = -1;
        Vector2 _partsMeshMouseUv;
        bool _partsWarpNeedsMesh;
        // Spine Mesh Tools view: how a vertex drag transforms the selection, and soft selection.
        [SerializeField] PartsVertexTool _partsVertexTool = PartsVertexTool.Translate;
        [SerializeField] bool _partsSoftSelect = true;
        [SerializeField] float _partsSoftSize = 60f;
        [SerializeField] float _partsSoftFeather = 0.6f;
        [SerializeField] bool _partsSoftHull = true;
        bool _partsMeshMenuShown;
        Material _partsWarpMaterial;

        // ------------------------------------------------------------------ shared

        Material PartsWarpMaterial()
        {
            if (_partsWarpMaterial != null && _partsWarpMaterial.shader != null)
                return _partsWarpMaterial;
            if (_partsWarpMaterial != null)
                Object.DestroyImmediate(_partsWarpMaterial);
            var shader = Shader.Find("Hidden/InvertLab/Parts Warp Preview");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
                return null;
            _partsWarpMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return _partsWarpMaterial;
        }

        void SetWarpSelectionSlot(string slotId)
        {
            string id = SpritePartIdUtility.Canonical(slotId);
            if (id == _partsWarpSelectionSlotId)
                return;
            _partsWarpSelectionSlotId = id;
            _partsWarpSelection.Clear();
            _partsWarpIndex = -1;
            _partsWarpHover = -1;
        }

        void SelectWarpVertex(int index, bool toggle)
        {
            if (toggle)
            {
                if (!_partsWarpSelection.Remove(index))
                    _partsWarpSelection.Add(index);
            }
            else if (!_partsWarpSelection.Contains(index))
            {
                _partsWarpSelection.Clear();
                _partsWarpSelection.Add(index);
            }
            _partsWarpIndex = _partsWarpSelection.Contains(index)
                ? index
                : _partsWarpSelection.Count > 0 ? _partsWarpSelection[_partsWarpSelection.Count - 1] : -1;
        }

        string MeshTargetSlotId()
            => IsPartsMeshEdit() ? _partsMeshEditSlotId : CurrentPartsSlot?.SlotId;

        bool SlotHasDeformKeys(string slotId)
        {
            if (_profile?.PartsClips == null)
                return false;
            string id = SpritePartIdUtility.Canonical(slotId);
            foreach (var clip in _profile.PartsClips)
            {
                if (clip?.Tracks == null)
                    continue;
                foreach (var track in clip.Tracks)
                {
                    if (track?.Keys == null || SpritePartIdUtility.Canonical(track.SlotId) != id)
                        continue;
                    foreach (var key in track.Keys)
                    {
                        if (key?.Deform != null)
                            return true;
                    }
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ canvas geometry

        static Vector2 LatticeGui(Rect unrotated, float2 p)
        {
            return new Vector2(
                Mathf.Lerp(unrotated.xMin, unrotated.xMax, p.x + 0.5f),
                Mathf.Lerp(unrotated.yMax, unrotated.yMin, p.y + 0.5f));
        }

        static Vector2 PartsWarpPointGui(
            Rect unrotated, Vector2 joint, float guiDeg, bool flipX, bool flipY, float2 p)
        {
            Vector2 g = LatticeGui(unrotated, p);
            if (flipX) g.x = joint.x - (g.x - joint.x);
            if (flipY) g.y = joint.y - (g.y - joint.y);
            return RotateAround(g, joint, guiDeg);
        }

        static Vector2 UnflipAround(Vector2 point, Vector2 joint, bool flipX, bool flipY)
        {
            if (flipX) point.x = joint.x - (point.x - joint.x);
            if (flipY) point.y = joint.y - (point.y - joint.y);
            return point;
        }

        bool TryGetPartsWarpLayout(
            Rect canvas, string slotId, out Rect rect, out Vector2 joint, out float guiDeg,
            out bool flipX, out bool flipY)
        {
            rect = default;
            joint = default;
            guiDeg = 0f;
            flipX = false;
            flipY = false;
            if (!SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), _partsPreviewTime,
                    Allocator.Temp, out var blob, out var poses, out var matrices, out _))
                return false;
            try
            {
                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                int idx = BlobSlotIndex(ref blob.Value, slotId);
                if (!TryGetPartsSlotDrawRect(canvas, ref blob.Value, matrices, idx, _partsPreviewTime,
                        out rect, out joint, out float worldDeg, out _, out _, poses))
                    return false;
                guiDeg = -worldDeg;
                if (poses.IsCreated && idx >= 0 && idx < poses.Length)
                {
                    flipX = poses[idx].Scale.x < 0f;
                    flipY = poses[idx].Scale.y < 0f;
                }
                return true;
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        /// <summary>The posed mesh, or the image corners of a part that has no mesh yet.</summary>
        static SpritePartsLattice DeformTarget(SpritePartsLattice posed, out bool virtualQuad)
        {
            virtualQuad = !posed.HasMesh;
            return virtualQuad ? SpritePartsLattice.FromMesh(SpritePartsMeshOps.CreateQuad()) : posed;
        }

        // ------------------------------------------------------------------ rendering

        void DrawPartsWarpedSprite(
            Texture2D tex, SpriteSheetDef sheet, int cell, Rect rect, SpritePartsLattice lattice, Color tint)
        {
            if (Event.current.type != EventType.Repaint || tex == null)
                return;
            var mat = PartsWarpMaterial();
            if (mat == null)
                return;
            Rect uv = sheet != null && sheet.Texture == tex
                ? SpriteSheetProfile.GetCellUvRect(sheet, cell)
                : SpriteSheetProfile.GetUniformCellUvRect(1, 1, 0);
            float padU = 0.5f / Mathf.Max(1, tex.width);
            float padV = 0.5f / Mathf.Max(1, tex.height);
            uv = new Rect(uv.x + padU, uv.y + padV,
                Mathf.Max(0.0001f, uv.width - padU * 2f), Mathf.Max(0.0001f, uv.height - padV * 2f));
            mat.mainTexture = tex;
            mat.color = Color.white;
            if (!mat.SetPass(0))
                return;

            float ppp = EditorGUIUtility.pixelsPerPoint;
            Vector2 origin = position.position * ppp;
            bool prevWrite = GL.sRGBWrite;
            // The preview shader already writes gamma. Leaving sRGBWrite on encodes it a second time.
            GL.sRGBWrite = false;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, position.width * ppp, position.height * ppp, 0f);
            GL.Begin(GL.TRIANGLES);
            int triangles = lattice.IndexCount - lattice.IndexCount % 3;
            int verts = lattice.PointCount;
            for (int i = 0; i < triangles; i++)
            {
                int v = lattice.GetIndex(i);
                if ((uint)v >= (uint)verts)
                    continue;
                Vector2 gui = GUI.matrix.MultiplyPoint(LatticeGui(rect, lattice.GetPoint(v)));
                Vector2 screen = GUIUtility.GUIToScreenPoint(gui);
                float2 t = lattice.GetUv(v);
                GL.Color(tint);
                GL.TexCoord(new Vector2(Mathf.Lerp(uv.xMin, uv.xMax, t.x), Mathf.Lerp(uv.yMin, uv.yMax, t.y)));
                GL.Vertex3(screen.x - origin.x, screen.y - origin.y, 0f);
            }
            GL.End();
            GL.PopMatrix();
            GL.sRGBWrite = prevWrite;
        }

        /// <summary>Meshed parts are skipped by the flat quad pass and drawn here, then the deform gizmo on top.</summary>
        void DrawPartsPoseWarpOverlay(Rect canvas)
        {
            if (!SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), _partsPreviewTime,
                    Allocator.Temp, out var blob, out var poses, out var matrices, out _))
                return;
            try
            {
                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                if (_partsShowArt)
                {
                    int n = blob.Value.Slots.Length;
                    var order = new int[n];
                    var ranks = new int[n];
                    for (int i = 0; i < n; i++)
                    {
                        order[i] = i;
                        ranks[i] = blob.Value.Slots[i].DrawRank;
                    }
                    System.Array.Sort(ranks, order);
                    for (int o = 0; o < order.Length; o++)
                    {
                        int i = order[o];
                        string sid = blob.Value.Slots[i].SlotId.ToString();
                        if (SpritePartsAuthoringOps.SlotOrAncestorHidden(_profile, sid))
                            continue;
                        var lattice = poses.IsCreated && i < poses.Length ? poses[i].Lattice : default;
                        SpritePartsSkinning.Apply(ref blob.Value, i, matrices, ref lattice);
                        if (!lattice.HasMesh)
                            continue;
                        if (!TryGetPartsSlotDrawRect(canvas, ref blob.Value, matrices, i, _partsPreviewTime,
                                out var r, out var joint, out float worldDeg, out var app, out var sheet, poses))
                            continue;
                        Texture2D tex = sheet?.Texture;
                        if (tex == null || app == null)
                            continue;
                        Matrix4x4 prev = GUI.matrix;
                        GUIUtility.RotateAroundPivot(-worldDeg, joint);
                        bool fx = poses[i].Scale.x < 0f;
                        bool fy = poses[i].Scale.y < 0f;
                        if (fx || fy)
                            GUIUtility.ScaleAroundPivot(new Vector2(fx ? -1f : 1f, fy ? -1f : 1f), joint);
                        DrawPartsWarpedSprite(tex, sheet, app.CellIndex, r, lattice, Color.white);
                        GUI.matrix = prev;
                    }
                }
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }

            if (!_partsShowDebug || _partsCanvasTool != PartsCanvasTool.Warp || _partsMode != SpritePartsStudioMode.Animate)
                return;
            GUI.BeginClip(canvas);
            try
            {
                DrawPartsDeformGizmo(new Rect(0f, 0f, canvas.width, canvas.height));
            }
            finally
            {
                GUI.EndClip();
            }
        }

        void DrawPartsDeformGizmo(Rect canvas)
        {
            var slot = CurrentPartsSlot;
            if (slot == null)
                return;
            if (!TryGetPartsWarpLayout(canvas, slot.SlotId, out var rect, out var joint, out float guiDeg,
                    out bool flipX, out bool flipY))
                return;
            var lattice = DeformTarget(ShownLattice(slot.SlotId), out bool virtualQuad);
            // Vertex selection belongs to one part; picking another part starts a fresh selection.
            SetWarpSelectionSlot(slot.SlotId);
            int hull = virtualQuad ? 4 : Mathf.Clamp(slot.Mesh?.HullCount ?? 0, 0, lattice.PointCount);
            var pts = new Vector3[lattice.PointCount];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = PartsWarpPointGui(rect, joint, guiDeg, flipX, flipY, lattice.GetPoint(i));

            Handles.BeginGUI();
            if (!virtualQuad)
            {
                Handles.color = new Color(1f, 0.6f, 0.2f, 0.45f);
                int tris = lattice.IndexCount - lattice.IndexCount % 3;
                for (int t = 0; t < tris; t += 3)
                {
                    int a = lattice.GetIndex(t), b = lattice.GetIndex(t + 1), c = lattice.GetIndex(t + 2);
                    if ((uint)a >= (uint)pts.Length || (uint)b >= (uint)pts.Length || (uint)c >= (uint)pts.Length)
                        continue;
                    Handles.DrawAAPolyLine(1f, pts[a], pts[b], pts[c], pts[a]);
                }
            }
            Handles.color = virtualQuad ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 0.6f, 0.2f, 0.95f);
            for (int i = 0; i < hull; i++)
                Handles.DrawAAPolyLine(virtualQuad ? 1f : 2f, pts[i], pts[(i + 1) % hull]);

            var red = new Color(0.9f, 0.2f, 0.15f, 1f);
            var green = new Color(0.2f, 0.95f, 0.35f, 1f);
            var flat = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++)
                flat[i] = pts[i];
            var weights = ComputeSoftWeights(flat, hull, _partsWarpSelection);

            // The selection box: drag inside it to translate / rotate / scale the selection.
            if (_partsWarpSelection.Count >= 2)
            {
                bool any = false;
                Vector2 min = Vector2.zero, max = Vector2.zero;
                foreach (int s in _partsWarpSelection)
                {
                    if ((uint)s >= (uint)flat.Length)
                        continue;
                    min = any ? Vector2.Min(min, flat[s]) : flat[s];
                    max = any ? Vector2.Max(max, flat[s]) : flat[s];
                    any = true;
                }
                if (any)
                {
                    var box = Rect.MinMaxRect(min.x - 8f, min.y - 8f, max.x + 8f, max.y + 8f);
                    Handles.color = new Color(0.35f, 0.9f, 0.55f, 0.9f);
                    Handles.DrawAAPolyLine(1.5f,
                        new Vector3(box.xMin, box.yMin), new Vector3(box.xMax, box.yMin),
                        new Vector3(box.xMax, box.yMax), new Vector3(box.xMin, box.yMax), new Vector3(box.xMin, box.yMin));
                    Vector2 c = SelectionCentroid(flat, _partsWarpSelection);
                    Handles.DrawAAPolyLine(1.5f, c + new Vector2(-5f, 0f), c + new Vector2(5f, 0f));
                    Handles.DrawAAPolyLine(1.5f, c + new Vector2(0f, -5f), c + new Vector2(0f, 5f));
                    EditorGUIUtility.AddCursorRect(box, _partsVertexTool == PartsVertexTool.Rotate
                        ? MouseCursor.RotateArrow
                        : _partsVertexTool == PartsVertexTool.Scale ? MouseCursor.ScaleArrow : MouseCursor.MoveArrow);
                }
            }

            if (_partsSoftSelect && canvas.Contains(Event.current.mousePosition))
            {
                // Spine shows the soft radius at the cursor: outer = Size, inner = fully moved.
                Vector2 m = Event.current.mousePosition;
                Handles.color = new Color(0.3f, 0.85f, 1f, 0.6f);
                Handles.DrawWireDisc(m, Vector3.forward, _partsSoftSize);
                Handles.color = new Color(0.3f, 0.85f, 1f, 0.3f);
                Handles.DrawWireDisc(m, Vector3.forward, _partsSoftSize * (1f - Mathf.Clamp01(_partsSoftFeather)));
            }

            for (int i = 0; i < pts.Length; i++)
            {
                bool selected = _partsWarpSelection.Contains(i);
                bool hover = i == _partsWarpHover;
                Handles.color = selected ? green
                    : weights[i] > 0f ? Color.Lerp(new Color(0.08f, 0.15f, 0.55f, 1f), new Color(0.25f, 0.9f, 1f, 1f), weights[i])
                    : virtualQuad ? Color.white : red;
                if (virtualQuad)
                    Handles.DrawWireDisc(pts[i], Vector3.forward, 5f);
                else
                    Handles.DrawSolidDisc(pts[i], Vector3.forward, 4f);
                if (hover)
                {
                    Handles.color = Color.white;
                    Handles.DrawWireDisc(pts[i], Vector3.forward, 6.5f);
                }
                EditorGUIUtility.AddCursorRect(HandleCursorRect(pts[i], 16f), MouseCursor.MoveArrow);
            }
            Handles.color = Color.yellow;
            Handles.DrawWireDisc(joint, Vector3.forward, 6f);
            Handles.EndGUI();

            if (virtualQuad)
                GUI.Label(new Rect(joint.x - 90f, rect.yMax + 6f, 260f, 16f),
                    "Drag a corner to make a 4-vertex mesh. Double-click to edit the mesh.", _mutedStyle);
        }

        // ------------------------------------------------------------------ Warp tool (animate deform)

        void UpdatePartsWarpHover(Rect canvas, Vector2 mouse)
        {
            int next = -1;
            var selected = CurrentPartsSlot;
            if (selected != null
                && TryPickPartsWarpVertex(canvas, mouse, out string slotId, out int point)
                && SpritePartIdUtility.Canonical(slotId) == SpritePartIdUtility.Canonical(selected.SlotId))
                next = point;
            if (next == _partsWarpHover)
                return;
            _partsWarpHover = next;
            Repaint();
        }

        /// <summary>
        /// Selected part first (mesh vertices, or image corners when it has no mesh yet),
        /// then vertices of any other meshed part.
        /// </summary>
        bool TryPickPartsWarpVertex(Rect canvas, Vector2 mouse, out string slotId, out int point)
        {
            slotId = null;
            point = -1;
            if (!SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), _partsPreviewTime,
                    Allocator.Temp, out var blob, out var poses, out var matrices, out _))
                return false;
            try
            {
                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                var selected = CurrentPartsSlot;
                if (selected != null && PickWarpOnSlot(canvas, ref blob.Value, poses, matrices, selected.SlotId, mouse, true, out point))
                {
                    slotId = selected.SlotId;
                    return true;
                }
                for (int i = 0; i < blob.Value.Slots.Length; i++)
                {
                    string id = blob.Value.Slots[i].SlotId.ToString();
                    if (SpritePartsAuthoringOps.SlotOrAncestorHidden(_profile, id)
                        || SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, id))
                        continue;
                    if (!PickWarpOnSlot(canvas, ref blob.Value, poses, matrices, id, mouse, false, out point))
                        continue;
                    slotId = id;
                    return true;
                }
                return false;
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        bool PickWarpOnSlot(
            Rect canvas, ref SpritePartsSetBlob set, NativeArray<SpritePartsSampler.Pose> poses,
            NativeArray<float4x4> matrices, string slotId, Vector2 mouse, bool allowVirtual, out int point)
        {
            point = -1;
            int idx = BlobSlotIndex(ref set, slotId);
            if (idx < 0 || !poses.IsCreated || idx >= poses.Length)
                return false;
            if (!allowVirtual && !poses[idx].Lattice.HasMesh)
                return false;
            if (!TryGetPartsSlotDrawRect(canvas, ref set, matrices, idx, _partsPreviewTime,
                    out var rect, out var joint, out float worldDeg, out _, out _, poses))
                return false;
            var shown = poses[idx].Lattice;
            SpritePartsSkinning.Apply(ref set, idx, matrices, ref shown);
            var lattice = DeformTarget(shown, out _);
            bool flipX = poses[idx].Scale.x < 0f;
            bool flipY = poses[idx].Scale.y < 0f;
            float best = PartsMeshHitRadius * PartsMeshHitRadius * 2f;
            for (int i = 0; i < lattice.PointCount; i++)
            {
                float d = (mouse - PartsWarpPointGui(rect, joint, -worldDeg, flipX, flipY, lattice.GetPoint(i))).sqrMagnitude;
                if (d > best)
                    continue;
                best = d;
                point = i;
            }
            return point >= 0;
        }

        void BeginPartsWarpDrag(int controlId, string slotId, int point, Vector2 mouse, bool shift)
        {
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, slotId);
            if (slot == null || slot.EditorLocked || SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, slot.SlotId))
            {
                _status = "Part is locked.";
                return;
            }
            var selected = CurrentPartsSlot;
            if (selected == null || SpritePartIdUtility.Canonical(selected.SlotId) != SpritePartIdUtility.Canonical(slotId))
                SelectPartsCanvasClicked(slotId, false, false, false);
            SetWarpSelectionSlot(slot.SlotId);

            BeginPartsDragUndo("Deform Mesh");
            // A part without a mesh shows its image corners; the mesh is made on the first real move.
            _partsWarpNeedsMesh = slot.Mesh == null || !slot.Mesh.HasMesh;

            SelectWarpVertex(point, shift);
            _partsWarpHover = point;
            _partsWarpActive = true;
            _partsTransformHandle = ColliderHandleKind.Body;
            _partsDragActive = true;
            _partsCanvasHotControl = controlId;
            GUIUtility.hotControl = controlId;
            _partsDragSlotId = slot.SlotId;
            _partsDragStartMouse = mouse;
            _partsDragStartPose = SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);
            _partsWarpSkinReady = false;
            _status = _partsWarpSelection.Count > 1
                ? "Deform " + _partsWarpSelection.Count + " vertices"
                : "Deform vertex " + point;
        }

        void ApplyPartsWarpDrag(Rect canvas, Vector2 mouse)
        {
            if (!_partsWarpActive || _partsWarpSelection.Count == 0)
                return;
            if ((mouse - _partsDragStartMouse).sqrMagnitude < 4f)
                return;
            if (_partsWarpNeedsMesh)
            {
                _partsWarpNeedsMesh = false;
                var slot = SpritePartsAuthoringOps.FindSlot(_profile, _partsDragSlotId);
                if (slot == null)
                    return;
                // Spine converts a region to a mesh with the four image corners.
                slot.Mesh = SpritePartsMeshOps.CreateQuad();
                SpritePartsAuthoringOps.ClearSlotDeforms(_profile, slot.SlotId);
                _partsDragStartPose = SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);
                _status = "Made a 4-vertex mesh. Double-click the part to add vertices.";
            }
            if (!TryGetPartsWarpLayout(canvas, _partsDragSlotId, out var rect, out var joint, out float guiDeg,
                    out bool flipX, out bool flipY))
                return;
            // Work in the part's own unrotated, unflipped rect (pixels) so rotate/scale keep the aspect.
            Vector2 a = UnflipAround(UnrotateAround(_partsDragStartMouse, joint, guiDeg), joint, flipX, flipY);
            Vector2 b = UnflipAround(UnrotateAround(mouse, joint, guiDeg), joint, flipX, flipY);
            var pose = _partsDragStartPose;
            var start = pose.Lattice;
            if (!start.HasMesh)
                return;
            // Weighted meshes: the handles are where the skinned vertices are drawn; the key stores
            // the pre-skin offset, so each screen move goes through the inverse bone blend (Spine does the same).
            if (!_partsWarpSkinReady)
            {
                _partsWarpSkinReady = true;
                if (!TrySampleWarpLattices(_partsDragSlotId, out _, out _partsWarpShownStart, out _partsWarpSkinInverse, out _partsWarpSkinSize))
                {
                    _partsWarpShownStart = start;
                    _partsWarpSkinInverse = null;
                }
            }
            var shownStart = _partsWarpShownStart.PointCount == start.PointCount ? _partsWarpShownStart : start;
            int n = start.PointCount;
            var local = new Vector2[n];
            for (int i = 0; i < n; i++)
                local[i] = LatticeGui(rect, shownStart.GetPoint(i));
            var slotDef = SpritePartsAuthoringOps.FindSlot(_profile, _partsDragSlotId);
            var weights = ComputeSoftWeights(local, slotDef?.Mesh?.HullCount ?? n, _partsWarpSelection);
            Vector2 pivot = SelectionCentroid(local, _partsWarpSelection);

            float angle = 0f;
            float scale = 1f;
            if (_partsVertexTool == PartsVertexTool.Rotate && (a - pivot).sqrMagnitude > 16f)
                angle = Vector2.SignedAngle(a - pivot, b - pivot);
            else if (_partsVertexTool == PartsVertexTool.Scale && (a - pivot).sqrMagnitude > 16f)
                scale = (b - pivot).magnitude / (a - pivot).magnitude;

            var lattice = start;
            for (int i = 0; i < n; i++)
            {
                float w = weights[i];
                if (w <= 0f)
                    continue;
                Vector2 p = local[i];
                Vector2 moved = _partsVertexTool switch
                {
                    PartsVertexTool.Rotate => pivot + (Vector2)(Quaternion.Euler(0f, 0f, angle * w) * (p - pivot)),
                    PartsVertexTool.Scale => pivot + (p - pivot) * (1f + (scale - 1f) * w),
                    _ => p + (b - a) * w,
                };
                var target = new float2(
                    (moved.x - rect.xMin) / Mathf.Max(1f, rect.width) - 0.5f,
                    (rect.yMax - moved.y) / Mathf.Max(1f, rect.height) - 0.5f);
                float2 delta = target - shownStart.GetPoint(i);
                if (_partsWarpSkinInverse != null && i < _partsWarpSkinInverse.Length
                    && _partsWarpSkinSize.x > 1e-6f && _partsWarpSkinSize.y > 1e-6f)
                    delta = math.mul(_partsWarpSkinInverse[i], delta * _partsWarpSkinSize) / _partsWarpSkinSize;
                lattice.SetPoint(i, start.GetPoint(i) + delta);
            }
            pose.Lattice = lattice;
            ApplyPartsPoseEdit(_partsDragSlotId, pose);
        }

        /// <summary>
        /// Spine soft selection: selected vertices weigh 1; others fall off with the distance (pixels)
        /// to the nearest selected vertex. Inside Size * (1 - Feather) they move fully, past Size not at all.
        /// </summary>
        float[] ComputeSoftWeights(IReadOnlyList<Vector2> points, int hullCount, List<int> selection)
        {
            var weights = new float[points.Count];
            foreach (int s in selection)
            {
                if ((uint)s < (uint)weights.Length)
                    weights[s] = 1f;
            }
            if (!_partsSoftSelect || selection.Count == 0)
                return weights;
            float size = Mathf.Max(1f, _partsSoftSize);
            float inner = size * (1f - Mathf.Clamp01(_partsSoftFeather));
            for (int i = 0; i < points.Count; i++)
            {
                if (weights[i] >= 1f || (!_partsSoftHull && i < hullCount))
                    continue;
                float d = float.MaxValue;
                foreach (int s in selection)
                {
                    if ((uint)s < (uint)points.Count)
                        d = Mathf.Min(d, Vector2.Distance(points[i], points[s]));
                }
                if (d <= inner)
                    weights[i] = 1f;
                else if (d < size)
                {
                    float t = (d - inner) / Mathf.Max(1e-4f, size - inner);
                    weights[i] = 1f - t * t * (3f - 2f * t);
                }
            }
            return weights;
        }

        static Vector2 SelectionCentroid(IReadOnlyList<Vector2> points, List<int> selection)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (int s in selection)
            {
                if ((uint)s >= (uint)points.Count)
                    continue;
                sum += points[s];
                count++;
            }
            return count > 0 ? sum / count : Vector2.zero;
        }

        /// <summary>Screen box around the selected vertices of the current part (the draggable "square").</summary>
        bool TryGetWarpSelectionBox(Rect canvas, out Rect box)
        {
            box = default;
            var slot = CurrentPartsSlot;
            if (slot == null || _partsWarpSelection.Count < 2
                || SpritePartIdUtility.Canonical(slot.SlotId) != _partsWarpSelectionSlotId)
                return false;
            if (!TryGetPartsWarpLayout(canvas, slot.SlotId, out var rect, out var joint, out float guiDeg,
                    out bool flipX, out bool flipY))
                return false;
            var lattice = DeformTarget(ShownLattice(slot.SlotId), out _);
            bool any = false;
            Vector2 min = Vector2.zero, max = Vector2.zero;
            foreach (int s in _partsWarpSelection)
            {
                if ((uint)s >= (uint)lattice.PointCount)
                    continue;
                Vector2 p = PartsWarpPointGui(rect, joint, guiDeg, flipX, flipY, lattice.GetPoint(s));
                min = any ? Vector2.Min(min, p) : p;
                max = any ? Vector2.Max(max, p) : p;
                any = true;
            }
            if (!any)
                return false;
            box = Rect.MinMaxRect(min.x - 8f, min.y - 8f, max.x + 8f, max.y + 8f);
            return true;
        }

        void FinishPartsWarpBox(Rect canvas, Vector2 mouse, bool shift)
        {
            if ((mouse - _partsWarpBoxStart).sqrMagnitude < 36f)
            {
                if (!shift)
                {
                    _partsWarpSelection.Clear();
                    _partsWarpIndex = -1;
                }
                _status = "Warp: drag a vertex. Double-click the part to edit its mesh.";
                return;
            }
            if (!TryGetPartsWarpLayout(canvas, _partsDragSlotId, out var rect, out var joint, out float guiDeg,
                    out bool flipX, out bool flipY))
                return;
            SetWarpSelectionSlot(_partsDragSlotId);
            var lattice = DeformTarget(ShownLattice(_partsDragSlotId), out _);
            var area = Rect.MinMaxRect(
                Mathf.Min(_partsWarpBoxStart.x, mouse.x), Mathf.Min(_partsWarpBoxStart.y, mouse.y),
                Mathf.Max(_partsWarpBoxStart.x, mouse.x), Mathf.Max(_partsWarpBoxStart.y, mouse.y));
            if (!shift)
                _partsWarpSelection.Clear();
            for (int i = 0; i < lattice.PointCount; i++)
            {
                if (area.Contains(PartsWarpPointGui(rect, joint, guiDeg, flipX, flipY, lattice.GetPoint(i)))
                    && !_partsWarpSelection.Contains(i))
                    _partsWarpSelection.Add(i);
            }
            _partsWarpIndex = _partsWarpSelection.Count > 0 ? _partsWarpSelection[_partsWarpSelection.Count - 1] : -1;
            _status = _partsWarpSelection.Count + " vertices selected";
        }

        /// <summary>Puts vertices back to the setup mesh on the current key. Null = every vertex.</summary>
        void ResetPartsDeform(string slotId, List<int> only)
        {
            if (_partsMode != SpritePartsStudioMode.Animate)
            {
                _status = "Deform is stored on clip keys. Switch to Animate.";
                return;
            }
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, slotId);
            if (slot == null)
                return;
            var pose = SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);
            var lattice = pose.Lattice;
            if (!lattice.HasMesh)
            {
                _status = "This part has no mesh.";
                return;
            }
            int count = 0;
            for (int i = 0; i < lattice.PointCount; i++)
            {
                if (only != null && !only.Contains(i))
                    continue;
                lattice.SetPoint(i, lattice.GetRest(i));
                count++;
            }
            pose.Lattice = lattice;
            RecordPartsUndo("Reset Deform");
            ApplyPartsPoseEdit(slot.SlotId, pose);
            _status = only == null ? "Deform reset" : "Reset " + count + " vertices";
        }

        void ResetSelectedPartsDeform()
        {
            string slotId = CurrentPartsSlot?.SlotId;
            if (string.IsNullOrEmpty(slotId))
                return;
            bool mine = SpritePartIdUtility.Canonical(slotId) == _partsWarpSelectionSlotId;
            ResetPartsDeform(slotId, mine && _partsWarpSelection.Count > 0 ? new List<int>(_partsWarpSelection) : null);
        }

        // ------------------------------------------------------------------ Edit Mesh (setup)

        bool IsPartsMeshEdit() => !string.IsNullOrEmpty(_partsMeshEditSlotId);

        SpritePartSlotDef MeshEditSlot() => SpritePartsAuthoringOps.FindSlot(_profile, _partsMeshEditSlotId);

        void EnterPartsMeshEdit(string slotId)
        {
            if (_profile == null || string.IsNullOrEmpty(slotId) || ImportPreviewActive)
                return;
            string id = SpritePartIdUtility.Canonical(slotId);
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, id);
            if (slot == null)
                return;
            if (ResolvePartsPreviewAppearance(slot, _partsPreviewTime) == null)
            {
                _status = "Assign art before editing the mesh.";
                return;
            }
            ReleasePartsCanvasCapture();
            TryExitPartsPivotFocus();
            SelectPartsSlotId(id, false, false);
            _partsMeshEditSlotId = id;
            SetWarpSelectionSlot(id);
            _partsWarpSelection.Clear();
            _partsWarpIndex = -1;
            if (slot.Mesh == null)
                slot.Mesh = new SpritePartMeshDef();
            _partsMeshTool = slot.Mesh.HasMesh ? PartsMeshTool.Modify : PartsMeshTool.Create;
            _status = slot.Mesh.HasMesh
                ? "Edit Mesh. 1 Modify, 2 Create, 3 Delete. Esc returns."
                : "No mesh yet. Click around the image to draw the hull, or press New / Trace.";
            Repaint();
        }

        bool TryExitPartsMeshEdit()
        {
            if (!IsPartsMeshEdit())
                return false;
            _partsMeshEditSlotId = null;
            _partsMeshPen = -1;
            _partsMeshDrag = false;
            _partsMeshMoved = false;
            _partsMeshEdgeFrom = -1;
            _partsWarpBox = false;
            _partsWarpSelection.Clear();
            _partsWarpIndex = -1;
            _status = "Left Edit Mesh.";
            Repaint();
            return true;
        }

        void SetPartsMeshTool(PartsMeshTool tool)
        {
            _partsMeshTool = tool;
            _partsMeshEdgeFrom = -1;
            _partsMeshPen = -1;
            _status = tool == PartsMeshTool.Modify
                ? "Modify: drag vertices to fit the image. The image stays flat."
                : tool == PartsMeshTool.Create
                    ? "Create (pen): click to add points joined by edges, click the first point to close. Shift cuts, Ctrl snaps, Enter ends."
                    : tool == PartsMeshTool.Delete
                        ? "Delete: click a vertex or an edge."
                        : "Weights: click a joint square to bind or pick it, drag to paint (Shift removes).";
            Repaint();
        }

        bool TryGetPartsMeshEditLayout(Rect canvas, out Rect sprite, out SpritePartAppearanceDef app, out SpriteSheetDef sheet)
        {
            sprite = default;
            app = null;
            sheet = null;
            var slot = MeshEditSlot();
            if (slot == null)
                return false;
            app = ResolvePartsPreviewAppearance(slot, _partsPreviewTime);
            if (app == null)
                return false;
            sheet = _profile.SheetAt(app.SheetIndex);
            float aspect = 1f;
            if (SpritePartsGeometry.TryResolve(_profile, app, false, out var geo, out _))
                aspect = geo.LogicalWorldSize.x / math.max(1e-4f, geo.LogicalWorldSize.y);
            aspect = Mathf.Max(0.05f, aspect);
            const float pad = 36f;
            // Keep the image clear of the Mesh panel docked on the right.
            var panel = PartsMeshPanelRect(canvas);
            float reserve = panel.width > 0f ? panel.width + 8f : 0f;
            float availW = Mathf.Max(32f, canvas.width - reserve - pad * 2f);
            float availH = Mathf.Max(32f, canvas.height - pad * 2f - 70f);
            float w = availW;
            float h = w / aspect;
            if (h > availH)
            {
                h = availH;
                w = h * aspect;
            }
            sprite = new Rect(canvas.x + (canvas.width - reserve - w) * 0.5f, canvas.y + 62f + (availH - h) * 0.5f, w, h);
            return true;
        }

        static Rect PartsMeshBanner(Rect canvas)
            => new Rect(canvas.x + 8f, canvas.y + 8f, Mathf.Min(canvas.width - 16f, 690f), 46f);

        static Vector2 MeshUvToGui(Rect sprite, Vector2 uv)
            => new Vector2(Mathf.Lerp(sprite.xMin, sprite.xMax, uv.x), Mathf.Lerp(sprite.yMax, sprite.yMin, uv.y));

        static Vector2 MeshGuiToUvFree(Rect sprite, Vector2 gui)
            => new Vector2(
                (gui.x - sprite.xMin) / Mathf.Max(1f, sprite.width),
                (sprite.yMax - gui.y) / Mathf.Max(1f, sprite.height));

        static Vector2 MeshGuiToUv(Rect sprite, Vector2 gui)
            => new Vector2(
                Mathf.Clamp01((gui.x - sprite.xMin) / Mathf.Max(1f, sprite.width)),
                Mathf.Clamp01((sprite.yMax - gui.y) / Mathf.Max(1f, sprite.height)));

        static int HitMeshVertex(Rect sprite, SpritePartMeshDef mesh, Vector2 mouse, int except = -1)
        {
            int best = -1;
            float bestD = PartsMeshHitRadius * PartsMeshHitRadius;
            int n = mesh?.VertexCount ?? 0;
            for (int i = 0; i < n; i++)
            {
                if (i == except)
                    continue;
                float d = (mouse - MeshUvToGui(sprite, mesh.Vertices[i])).sqrMagnitude;
                if (d > bestD)
                    continue;
                bestD = d;
                best = i;
            }
            return best;
        }

        static bool HitMeshUserEdge(Rect sprite, SpritePartMeshDef mesh, Vector2 mouse, out int a, out int b)
        {
            a = b = -1;
            if (mesh?.Edges == null)
                return false;
            float best = PartsMeshEdgeSnap;
            for (int i = 0; i + 1 < mesh.Edges.Length; i += 2)
            {
                int ea = mesh.Edges[i], eb = mesh.Edges[i + 1];
                if ((uint)ea >= (uint)mesh.VertexCount || (uint)eb >= (uint)mesh.VertexCount)
                    continue;
                float d = SpritePartsMeshOps.DistanceToSegment(mouse,
                    MeshUvToGui(sprite, mesh.Vertices[ea]), MeshUvToGui(sprite, mesh.Vertices[eb]));
                if (d > best)
                    continue;
                best = d;
                a = ea;
                b = eb;
            }
            return a >= 0;
        }

        void DrawPartsMeshEdit(Rect canvas)
        {
            var slot = MeshEditSlot();
            string name = slot != null && !string.IsNullOrEmpty(slot.Name) ? slot.Name : _partsMeshEditSlotId;
            var banner = PartsMeshBanner(canvas);
            EditorGUI.DrawRect(banner, new Color(0.12f, 0.18f, 0.28f, 0.94f));
            if (GUI.Button(new Rect(banner.x + 4f, banner.y + 3f, 46f, 18f), "Back", _partsTabStyle))
                TryExitPartsMeshEdit();
            GUI.Label(new Rect(banner.x + 54f, banner.y + 4f, banner.width - 60f, 16f),
                "Edit Mesh: " + name + "   Del deletes   Right-click for more   Esc returns", _mutedStyle);
            float bx = banner.x + 4f;
            float by = banner.y + 24f;
            MeshToolButton(ref bx, by, 62f, "1 Modify", PartsMeshTool.Modify);
            MeshToolButton(ref bx, by, 62f, "2 Create", PartsMeshTool.Create);
            MeshToolButton(ref bx, by, 62f, "3 Delete", PartsMeshTool.Delete);
            MeshToolButton(ref bx, by, 68f, "4 Weights", PartsMeshTool.Weights);
            bx += 10f;
            if (GUI.Button(new Rect(bx, by, 44f, 18f), new GUIContent("New", "Reset to the four image corners."), _partsTabStyle))
                ResetPartsMeshToQuad();
            bx += 46f;
            if (GUI.Button(new Rect(bx, by, 50f, 18f), new GUIContent("Trace", "Hull that follows the image alpha."), _partsTabStyle))
                TracePartsMesh();
            bx += 52f;
            if (GUI.Button(new Rect(bx, by, 70f, 18f), new GUIContent("Generate", "Fill the hull with interior vertices."), _partsTabStyle))
                GeneratePartsMeshInterior();
            bx += 72f;
            if (GUI.Button(new Rect(bx, by, 62f, 18f), new GUIContent("Remove", "Back to a rigid rectangle."), _partsTabStyle))
                RemovePartsMesh();

            if (!TryGetPartsMeshEditLayout(canvas, out var sprite, out var app, out var sheet))
            {
                GUI.Label(new Rect(canvas.x + 12f, canvas.y + 62f, canvas.width - 24f, 20f), "This part has no art.", _mutedStyle);
                return;
            }
            EditorGUI.DrawRect(sprite, new Color(0.12f, 0.13f, 0.16f, 1f));
            if (sheet?.Texture != null && app != null)
                DrawPartsSheetCell(sheet.Texture, sheet, sheet.Columns, sheet.Rows, app.CellIndex, sprite, Color.white);
            DrawGuiRectOutline(sprite, new Color(1f, 1f, 1f, 0.25f), 1f);

            var mesh = slot?.Mesh;
            int n = mesh?.VertexCount ?? 0;
            int hull = Mathf.Clamp(mesh?.HullCount ?? 0, 0, n);
            var orange = new Color(1f, 0.6f, 0.2f, 1f);
            if (mesh != null && mesh.HasMesh)
            {
                var faint = new Color(1f, 0.6f, 0.2f, 0.45f);
                for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
                {
                    int a = mesh.Triangles[t], b = mesh.Triangles[t + 1], c = mesh.Triangles[t + 2];
                    if ((uint)a >= (uint)n || (uint)b >= (uint)n || (uint)c >= (uint)n)
                        continue;
                    Vector2 pa = MeshUvToGui(sprite, mesh.Vertices[a]);
                    Vector2 pb = MeshUvToGui(sprite, mesh.Vertices[b]);
                    Vector2 pc = MeshUvToGui(sprite, mesh.Vertices[c]);
                    DrawGuiLine(pa, pb, faint, 1f);
                    DrawGuiLine(pb, pc, faint, 1f);
                    DrawGuiLine(pc, pa, faint, 1f);
                }
            }
            for (int i = 0; i < hull; i++)
            {
                if (i == hull - 1 && hull < 3)
                    break;
                DrawGuiLine(MeshUvToGui(sprite, mesh.Vertices[i]), MeshUvToGui(sprite, mesh.Vertices[(i + 1) % hull]), orange, 2f);
            }
            if (mesh?.Edges != null)
            {
                var cyan = new Color(0.3f, 0.85f, 1f, 1f);
                for (int e = 0; e + 1 < mesh.Edges.Length; e += 2)
                {
                    int a = mesh.Edges[e], b = mesh.Edges[e + 1];
                    if ((uint)a < (uint)n && (uint)b < (uint)n)
                        DrawGuiLine(MeshUvToGui(sprite, mesh.Vertices[a]), MeshUvToGui(sprite, mesh.Vertices[b]), cyan, 2f);
                }
            }
            if (_partsMeshTool == PartsMeshTool.Weights)
                DrawPartsMeshWeights(sprite, mesh);
            if (_partsMeshEdgeFrom >= 0 && _partsMeshEdgeFrom < n)
                DrawGuiLine(MeshUvToGui(sprite, mesh.Vertices[_partsMeshEdgeFrom]), MeshUvToGui(sprite, _partsMeshMouseUv), new Color(0.3f, 0.85f, 1f, 0.8f), 1.5f);
            else
                DrawMeshPenPreview(sprite, mesh);

            for (int i = 0; i < n; i++)
            {
                Vector2 p = MeshUvToGui(sprite, mesh.Vertices[i]);
                bool selected = _partsWarpSelection.Contains(i);
                var color = selected ? new Color(0.2f, 0.95f, 0.35f, 1f) : new Color(0.9f, 0.2f, 0.15f, 1f);
                float s = i < hull ? 7f : 6f;
                EditorGUI.DrawRect(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), color);
                if (i == _partsWarpHover)
                    DrawGuiRectOutline(new Rect(p.x - s * 0.5f - 2f, p.y - s * 0.5f - 2f, s + 4f, s + 4f), Color.white, 1f);
            }
            string stats = mesh != null && mesh.HasMesh
                ? n + " vertices   " + hull + " hull   " + mesh.Triangles.Length / 3 + " triangles"
                : n > 0 ? n + " hull points. Keep clicking around the image." : "No mesh. The part draws as a rectangle.";
            GUI.Label(new Rect(sprite.x, sprite.yMax + 6f, sprite.width, 16f), stats, _mutedStyle);
        }

        void MeshToolButton(ref float x, float y, float w, string label, PartsMeshTool tool)
        {
            var r = new Rect(x, y, w, 18f);
            bool on = _partsMeshTool == tool;
            if (on)
                EditorGUI.DrawRect(r, new Color(0.18f, 0.48f, 0.52f, 0.95f));
            if (GUI.Toggle(r, on, label, on ? _primaryStyle : _partsTabStyle) && !on)
                SetPartsMeshTool(tool);
            x += w + 2f;
        }

        void HandlePartsMeshEditInput(Rect canvas, Event evt, int controlId)
        {
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                ReleasePartsCanvasCapture();
                TryExitPartsMeshEdit();
                evt.Use();
                return;
            }
            if (evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                && !IsEditingAnyTextField())
            {
                EndMeshPen();
                evt.Use();
                return;
            }
            if (!TryGetPartsMeshEditLayout(canvas, out var sprite, out _, out _))
                return;
            var slot = MeshEditSlot();
            if (slot == null)
                return;
            slot.Mesh ??= new SpritePartMeshDef();
            var mesh = slot.Mesh;

            if (evt.type == EventType.MouseMove || evt.type == EventType.MouseDrag)
            {
                _partsMeshMouseUv = MeshGuiToUvFree(sprite, evt.mousePosition);
                int hover = HitMeshVertex(sprite, mesh, evt.mousePosition);
                if (hover != _partsWarpHover || _partsMeshEdgeFrom >= 0 || _partsMeshTool == PartsMeshTool.Create)
                {
                    _partsWarpHover = hover;
                    Repaint();
                }
            }

            bool rightClick = evt.type == EventType.ContextClick
                || (evt.button == 1 && evt.type == EventType.MouseUp);
            if (rightClick && canvas.Contains(evt.mousePosition))
            {
                if (!_partsMeshMenuShown)
                    ShowPartsMeshContextMenu(sprite, evt.mousePosition);
                _partsMeshMenuShown = true;
                evt.Use();
                return;
            }
            if (evt.type == EventType.MouseDown && evt.button == 1)
            {
                _partsMeshMenuShown = false;
                evt.Use();
                return;
            }

            if (evt.type != EventType.MouseDown || evt.button != 0 || !canvas.Contains(evt.mousePosition))
                return;
            if (PartsMeshBanner(canvas).Contains(evt.mousePosition))
                return;
            GUIUtility.keyboardControl = 0;
            GUI.FocusControl(null);

            int hit = HitMeshVertex(sprite, mesh, evt.mousePosition);
            if (_partsMeshTool == PartsMeshTool.Delete)
            {
                if (hit >= 0)
                    DeleteMeshVertices(new List<int> { hit });
                else if (HitMeshUserEdge(sprite, mesh, evt.mousePosition, out int ea, out int eb))
                    RemoveMeshEdge(ea, eb);
                evt.Use();
                Repaint();
                return;
            }

            if (_partsMeshTool == PartsMeshTool.Weights)
            {
                BeginMeshWeightsInput(sprite, evt, controlId);
                evt.Use();
                Repaint();
                return;
            }

            if (_partsMeshTool == PartsMeshTool.Create)
            {
                bool ctrl = evt.control || evt.command;
                bool penLive = (uint)_partsMeshPen < (uint)mesh.VertexCount;
                if (hit >= 0 && !penLive && !ctrl && mesh.HasMesh)
                {
                    // Start the chain here; dragging to another vertex also draws an edge.
                    SelectOnly(hit);
                    _partsMeshPen = hit;
                    _partsMeshEdgeFrom = hit;
                    CapturePartsMeshDrag(controlId);
                    _status = "Pen at vertex " + hit + ". Click to draw from here, Enter ends.";
                }
                else
                    PenClick(sprite, evt.mousePosition, hit, evt.shift && !ctrl, ctrl);
                evt.Use();
                Repaint();
                return;
            }

            // Modify
            if (hit >= 0)
            {
                SelectWarpVertex(hit, evt.shift);
                if (_partsWarpSelection.Count > 0)
                {
                    _partsMeshDrag = true;
                    _partsMeshMoved = false;
                    _partsMeshDragStart = mesh.Clone();
                    _partsDragStartMouse = evt.mousePosition;
                    CapturePartsMeshDrag(controlId);
                }
            }
            else
            {
                _partsWarpBox = true;
                _partsWarpBoxStart = evt.mousePosition;
                _partsWarpBoxEnd = evt.mousePosition;
                CapturePartsMeshDrag(controlId);
            }
            evt.Use();
            Repaint();
        }

        void CapturePartsMeshDrag(int controlId)
        {
            _partsDragActive = true;
            _partsDragSlotId = _partsMeshEditSlotId;
            _partsCanvasHotControl = controlId;
            GUIUtility.hotControl = controlId;
        }

        /// <summary>Called by the active-drag router while Edit Mesh owns the pointer. True = handled.</summary>
        bool HandlePartsMeshEditDrag(Rect canvas, Event evt, EventType raw)
        {
            if (!_partsMeshDrag && !_partsWarpBox && _partsMeshEdgeFrom < 0 && !_partsWeightPainting)
                return false;
            var slot = MeshEditSlot();
            TryGetPartsMeshEditLayout(canvas, out var sprite, out _, out _);

            if (raw == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                if (_partsMeshMoved && slot != null && _partsMeshDragStart != null)
                    slot.Mesh = _partsMeshDragStart.Clone();
                if (_partsMeshMoved)
                    EndPartsDragUndo();
                ReleasePartsCanvasCapture();
                _status = "Cancelled";
                evt.Use();
                Repaint();
                return true;
            }
            if (raw == EventType.MouseDrag)
            {
                _partsMeshMouseUv = MeshGuiToUvFree(sprite, evt.mousePosition);
                if (_partsMeshDrag)
                    ApplyMeshVertexDrag(sprite, evt.mousePosition);
                else if (_partsWeightPainting)
                    PaintWeights(sprite, evt.mousePosition, evt.shift);
                else if (_partsWarpBox)
                    _partsWarpBoxEnd = evt.mousePosition;
                evt.Use();
                Repaint();
                return true;
            }
            if (raw != EventType.MouseUp && raw != EventType.MouseLeaveWindow)
                return false;

            if (_partsWeightPainting)
            {
                EndPartsDragUndo();
                _status = "Weights painted.";
            }
            if (_partsWarpBox && raw == EventType.MouseUp)
                FinishMeshBox(sprite, evt.mousePosition, evt.shift);
            if (_partsMeshEdgeFrom >= 0 && raw == EventType.MouseUp && slot?.Mesh != null)
            {
                int to = HitMeshVertex(sprite, slot.Mesh, evt.mousePosition, _partsMeshEdgeFrom);
                if (to >= 0)
                {
                    AddMeshEdge(_partsMeshEdgeFrom, to);
                    _partsMeshPen = to;
                    SelectOnly(to);
                }
            }
            if (_partsMeshMoved)
            {
                EndPartsDragUndo();
                _status = "Moved " + _partsWarpSelection.Count + " vertices";
            }
            ReleasePartsCanvasCapture();
            evt.Use();
            Repaint();
            return true;
        }

        void ApplyMeshVertexDrag(Rect sprite, Vector2 mouse)
        {
            var slot = MeshEditSlot();
            if (slot == null || _partsMeshDragStart?.Vertices == null)
                return;
            if (!_partsMeshMoved && (mouse - _partsDragStartMouse).sqrMagnitude < 9f)
                return;
            if (!_partsMeshMoved)
            {
                BeginPartsDragUndo("Move Mesh Vertices");
                _partsMeshMoved = true;
            }
            var delta = new Vector2(
                (mouse.x - _partsDragStartMouse.x) / Mathf.Max(1f, sprite.width),
                -(mouse.y - _partsDragStartMouse.y) / Mathf.Max(1f, sprite.height));
            var indices = new List<int>();
            var positions = new List<Vector2>();
            foreach (int i in _partsWarpSelection)
            {
                if ((uint)i >= (uint)_partsMeshDragStart.Vertices.Length)
                    continue;
                indices.Add(i);
                positions.Add(_partsMeshDragStart.Vertices[i] + delta);
            }
            var next = _partsMeshDragStart.Clone();
            if (!next.HasMesh)
            {
                // Hull still being drawn: nothing to triangulate yet.
                for (int k = 0; k < indices.Count; k++)
                    next.Vertices[indices[k]] = new Vector2(Mathf.Clamp01(positions[k].x), Mathf.Clamp01(positions[k].y));
                slot.Mesh = next;
            }
            else if (SpritePartsMeshOps.TrySetVertices(next, indices, positions))
                slot.Mesh = next;
            else
                _status = "The hull cannot cross itself.";
            if (_asset != null)
                EditorUtility.SetDirty(_asset);
        }

        void FinishMeshBox(Rect sprite, Vector2 mouse, bool shift)
        {
            var mesh = MeshEditSlot()?.Mesh;
            if ((mouse - _partsWarpBoxStart).sqrMagnitude < 36f)
            {
                if (!shift)
                {
                    _partsWarpSelection.Clear();
                    _partsWarpIndex = -1;
                }
                return;
            }
            var area = Rect.MinMaxRect(
                Mathf.Min(_partsWarpBoxStart.x, mouse.x), Mathf.Min(_partsWarpBoxStart.y, mouse.y),
                Mathf.Max(_partsWarpBoxStart.x, mouse.x), Mathf.Max(_partsWarpBoxStart.y, mouse.y));
            if (!shift)
                _partsWarpSelection.Clear();
            for (int i = 0; i < (mesh?.VertexCount ?? 0); i++)
            {
                if (area.Contains(MeshUvToGui(sprite, mesh.Vertices[i])) && !_partsWarpSelection.Contains(i))
                    _partsWarpSelection.Add(i);
            }
            _partsWarpIndex = _partsWarpSelection.Count > 0 ? _partsWarpSelection[_partsWarpSelection.Count - 1] : -1;
            _status = _partsWarpSelection.Count + " selected";
        }

        void AddMeshEdge(int a, int b)
        {
            var slot = MeshEditSlot();
            if (slot?.Mesh == null)
                return;
            if (SpritePartsMeshOps.IsHullEdge(slot.Mesh, a, b) || SpritePartsMeshOps.HasEdge(slot.Mesh, a, b))
            {
                _status = "Already connected.";
                return;
            }
            var work = slot.Mesh.Clone();
            if (!SpritePartsMeshOps.TryAddEdge(work, a, b))
            {
                _status = "Could not add that edge.";
                return;
            }
            RecordPartsUndo("Add Mesh Edge");
            slot.Mesh = work;
            SaveDirty();
            _status = "Edge " + a + " - " + b;
        }

        void RemoveMeshEdge(int a, int b)
        {
            var slot = MeshEditSlot();
            if (slot?.Mesh == null)
                return;
            var work = slot.Mesh.Clone();
            if (!SpritePartsMeshOps.TryRemoveEdge(work, a, b))
                return;
            RecordPartsUndo("Remove Mesh Edge");
            slot.Mesh = work;
            SaveDirty();
            _status = "Edge removed";
        }

        void DeleteMeshVertices(List<int> indices)
        {
            var slot = MeshEditSlot();
            if (slot?.Mesh == null || indices == null || indices.Count == 0)
                return;
            bool hadMesh = slot.Mesh.HasMesh;
            var work = slot.Mesh.Clone();
            if (!SpritePartsMeshOps.TryRemoveVertices(work, indices, out var remap))
            {
                _status = "A mesh needs at least 3 hull vertices. Use Remove to drop the mesh.";
                return;
            }
            RecordPartsUndo("Delete Mesh Vertices");
            slot.Mesh = work;
            _partsMeshPen = -1;
            if (hadMesh)
                SpritePartsAuthoringOps.RemapSlotDeforms(_profile, slot.SlotId, remap, work.VertexCount);
            _partsWarpSelection.Clear();
            _partsWarpIndex = -1;
            _partsWarpHover = -1;
            SaveDirty();
            _status = "Deleted " + indices.Count + " vertices";
        }

        void DeleteSelectedMeshVertices()
        {
            if (!IsPartsMeshEdit())
                return;
            if (_partsWarpSelection.Count == 0)
            {
                _status = "Select vertices to delete.";
                return;
            }
            DeleteMeshVertices(new List<int>(_partsWarpSelection));
        }

        void SelectAllMeshVertices()
        {
            string slotId = MeshTargetSlotId();
            if (string.IsNullOrEmpty(slotId))
                return;
            SetWarpSelectionSlot(slotId);
            int n = IsPartsMeshEdit()
                ? MeshEditSlot()?.Mesh?.VertexCount ?? 0
                : DeformTarget(SampleLocalPoseForSlot(slotId, _partsPreviewTime).Lattice, out _).PointCount;
            _partsWarpSelection.Clear();
            for (int i = 0; i < n; i++)
                _partsWarpSelection.Add(i);
            _partsWarpIndex = n - 1;
            _status = n + " vertices";
        }

        bool ConfirmMeshReplace(SpritePartSlotDef slot, string action)
        {
            if (!SlotHasDeformKeys(slot.SlotId))
                return true;
            return EditorUtility.DisplayDialog(action,
                "This part has deform keys. " + action + " changes every vertex, so those deform keys will be cleared.",
                action, "Cancel");
        }

        void ReplacePartsMesh(SpritePartSlotDef slot, SpritePartMeshDef mesh, string label)
        {
            RecordPartsUndo(label);
            slot.Mesh = mesh ?? new SpritePartMeshDef();
            SpritePartsAuthoringOps.ClearSlotDeforms(_profile, slot.SlotId);
            _partsWarpSelection.Clear();
            _partsWarpIndex = -1;
            _partsWarpHover = -1;
            SaveDirty();
            Repaint();
        }

        void ResetPartsMeshToQuad()
        {
            var slot = MeshEditSlot() ?? CurrentPartsSlot;
            if (slot == null || !ConfirmMeshReplace(slot, "New Mesh"))
                return;
            ReplacePartsMesh(slot, SpritePartsMeshOps.CreateQuad(), "New Mesh");
            _status = "Mesh reset to the image corners.";
        }

        void RemovePartsMesh()
        {
            var slot = MeshEditSlot() ?? CurrentPartsSlot;
            if (slot == null || (slot.Mesh?.VertexCount ?? 0) == 0 || !ConfirmMeshReplace(slot, "Remove Mesh"))
                return;
            ReplacePartsMesh(slot, null, "Remove Mesh");
            _status = "Mesh removed. The part draws as a rectangle.";
        }

        void GeneratePartsMeshInterior()
        {
            var slot = MeshEditSlot();
            if (slot?.Mesh == null || !slot.Mesh.HasMesh)
            {
                _status = "Draw or trace the hull first.";
                return;
            }
            var work = slot.Mesh.Clone();
            int budget = Mathf.Min(12, SpritePartsMeshOps.MaxVertices - work.VertexCount);
            int added = SpritePartsMeshOps.GenerateInterior(work, budget, out var remap);
            if (added == 0)
            {
                _status = "No room for interior vertices.";
                return;
            }
            RecordPartsUndo("Generate Mesh");
            slot.Mesh = work;
            SpritePartsAuthoringOps.RemapSlotDeforms(_profile, slot.SlotId, remap, work.VertexCount);
            SaveDirty();
            _status = "Added " + added + " interior vertices.";
        }

        void TracePartsMesh()
        {
            var slot = MeshEditSlot() ?? CurrentPartsSlot;
            if (slot == null)
                return;
            var app = ResolvePartsPreviewAppearance(slot, _partsPreviewTime);
            var sheet = app != null && _profile != null ? _profile.SheetAt(app.SheetIndex) : null;
            var tex = sheet?.Texture;
            if (tex == null || !TryReadCellPixels(tex, sheet, app.CellIndex, out var pixels, out int w, out int h))
            {
                _status = "Sprite pixels could not be read.";
                return;
            }
            var outline = new List<Vector2>();
            if (!TryTraceSpriteOutline(pixels, w, h, outline))
            {
                _status = "No opaque pixels to trace.";
                return;
            }
            var mesh = SpritePartsMeshOps.FromOutline(outline);
            if (mesh == null)
            {
                _status = "The traced outline could not be triangulated.";
                return;
            }
            if (!ConfirmMeshReplace(slot, "Trace"))
                return;
            ReplacePartsMesh(slot, mesh, "Trace Mesh");
            _status = "Traced " + mesh.HullCount + " hull vertices. Generate adds interior vertices.";
        }

        void ShowPartsMeshContextMenu(Rect sprite, Vector2 mouse)
        {
            var slot = MeshEditSlot();
            var mesh = slot?.Mesh;
            var menu = new GenericMenu();
            int hit = HitMeshVertex(sprite, mesh, mouse);
            if (hit >= 0 && !_partsWarpSelection.Contains(hit))
            {
                _partsWarpSelection.Clear();
                _partsWarpSelection.Add(hit);
                _partsWarpIndex = hit;
            }
            if (_partsWarpSelection.Count == 2 && mesh != null && mesh.HasMesh)
            {
                int a = _partsWarpSelection[0];
                int b = _partsWarpSelection[1];
                if (SpritePartsMeshOps.HasEdge(mesh, a, b))
                    menu.AddItem(new GUIContent("Remove Edge"), false, () => RemoveMeshEdge(a, b));
                else if (!SpritePartsMeshOps.IsHullEdge(mesh, a, b))
                    menu.AddItem(new GUIContent("Connect With Edge"), false, () => AddMeshEdge(a, b));
            }
            if (HitMeshUserEdge(sprite, mesh, mouse, out int ea, out int eb))
                menu.AddItem(new GUIContent("Remove This Edge"), false, () => RemoveMeshEdge(ea, eb));
            if (_partsWarpSelection.Count > 0)
                menu.AddItem(new GUIContent("Delete Vertices"), false, DeleteSelectedMeshVertices);
            if (_partsWarpSelection.Count == 2 && mesh != null && mesh.HasMesh)
            {
                menu.AddItem(new GUIContent("Merge"), false, MergeSelectedMeshPair);
                int sa = _partsWarpSelection[0], sb = _partsWarpSelection[1];
                if (SpritePartsMeshOps.HasEdge(mesh, sa, sb) || SpritePartsMeshOps.IsHullEdge(mesh, sa, sb))
                    menu.AddItem(new GUIContent("Split"), false, SplitSelectedMeshPair);
                else
                    menu.AddDisabledItem(new GUIContent("Split"));
            }
            if ((uint)_partsMeshPen < (uint)(mesh?.VertexCount ?? 0))
                menu.AddItem(new GUIContent("End Pen (Enter)"), false, EndMeshPen);
            menu.AddItem(new GUIContent("Select All"), false, SelectAllMeshVertices);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Tool/Modify (1)"), _partsMeshTool == PartsMeshTool.Modify, () => SetPartsMeshTool(PartsMeshTool.Modify));
            menu.AddItem(new GUIContent("Tool/Create (2)"), _partsMeshTool == PartsMeshTool.Create, () => SetPartsMeshTool(PartsMeshTool.Create));
            menu.AddItem(new GUIContent("Tool/Delete (3)"), _partsMeshTool == PartsMeshTool.Delete, () => SetPartsMeshTool(PartsMeshTool.Delete));
            menu.AddItem(new GUIContent("Generate Interior"), false, GeneratePartsMeshInterior);
            menu.AddItem(new GUIContent("Trace Outline"), false, TracePartsMesh);
            menu.AddItem(new GUIContent("New (Image Corners)"), false, ResetPartsMeshToQuad);
            menu.AddItem(new GUIContent("Remove Mesh"), false, RemovePartsMesh);
            menu.ShowAsContext();
        }

        /// <summary>Vertex rows under the part in the tree while Edit Mesh is open.</summary>
        void DrawMeshVertexRows(int depth)
        {
            var mesh = MeshEditSlot()?.Mesh;
            int n = mesh?.VertexCount ?? 0;
            for (int i = 0; i < n; i++)
            {
                bool on = _partsWarpSelection.Contains(i);
                var row = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
                if (on)
                    EditorGUI.DrawRect(row, new Color(0.15f, 0.45f, 0.28f, 0.85f));
                else if (row.Contains(Event.current.mousePosition))
                    EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.06f));
                float x = row.x + 8f + depth * 14f;
                EditorGUI.DrawRect(new Rect(x, row.y + 5f, 8f, 8f),
                    on ? new Color(0.2f, 0.95f, 0.35f, 1f) : new Color(0.9f, 0.2f, 0.15f, 1f));
                string label = (i < mesh.HullCount ? "Hull " : "Vertex ") + i;
                if (GUI.Button(new Rect(x + 12f, row.y, row.width - (x - row.x) - 12f, row.height), label, EditorStyles.miniLabel))
                {
                    SelectWarpVertex(i, Event.current.shift);
                    Repaint();
                }
            }
        }

        void DrawPartsMeshInspector(SpritePartSlotDef slot)
        {
            GUILayout.Space(6f);
            GUILayout.Label("MESH", _sectionStyle);
            var mesh = slot.Mesh;
            EditorGUILayout.LabelField(mesh != null && mesh.HasMesh
                ? mesh.VertexCount + " vertices, " + mesh.HullCount + " hull, " + mesh.Triangles.Length / 3 + " triangles"
                : "None. The part draws as a rectangle.", _mutedStyle);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Edit Mesh", "Hull, interior vertices and edges. Double-click the part with Warp (R)."), GUILayout.Height(20f)))
                EnterPartsMeshEdit(slot.SlotId);
            using (new EditorGUI.DisabledScope(mesh == null || !mesh.HasMesh || _partsMode != SpritePartsStudioMode.Animate))
            {
                if (GUILayout.Button(new GUIContent("Reset Deform", "Selected vertices, or all, back to the setup mesh on this key."), GUILayout.Height(20f)))
                    ResetSelectedPartsDeform();
            }
            EditorGUILayout.EndHorizontal();

            if (PartsMeshPanelVisible())
                EditorGUILayout.LabelField("Tools, vertex values and weights: Mesh panel on the canvas.", _mutedStyle);
        }

        void DrawPartsMeshToolsSection()
        {
            GUILayout.Space(6f);
            GUILayout.Label("MESH TOOLS", _sectionStyle);
            _partsVertexTool = (PartsVertexTool)GUILayout.Toolbar((int)_partsVertexTool,
                new[] { new GUIContent("Translate", "Drag moves the selection."),
                        new GUIContent("Rotate", "Drag rotates the selection around its centre."),
                        new GUIContent("Scale", "Drag scales the selection from its centre.") },
                GUILayout.Height(20f));
            _partsSoftSelect = EditorGUILayout.ToggleLeft(
                new GUIContent("Soft Selection", "Neighbours of the selected vertices follow with a falloff."), _partsSoftSelect);
            using (new EditorGUI.DisabledScope(!_partsSoftSelect))
            {
                _partsSoftSize = EditorGUILayout.Slider(
                    new GUIContent("Size", "Radius of influence in screen pixels."), _partsSoftSize, 5f, 400f);
                _partsSoftFeather = EditorGUILayout.Slider(
                    new GUIContent("Feather", "Share of Size that fades out. 0 = everything in range moves fully."),
                    _partsSoftFeather * 100f, 0f, 100f) / 100f;
                _partsSoftHull = EditorGUILayout.ToggleLeft(
                    new GUIContent("Hull Vertices", "Off keeps the outline still while inner vertices bend."), _partsSoftHull);
            }
        }

        // ------------------------------------------------------------------ trace

        static bool TryTraceSpriteOutline(Color32[] pixels, int w, int h, List<Vector2> outline)
        {
            outline.Clear();
            int sx = -1;
            int sy = -1;
            for (int y = h - 1; y >= 0 && sx < 0; y--)
            {
                for (int x = 0; x < w; x++)
                {
                    if (pixels[y * w + x].a <= 24)
                        continue;
                    sx = x;
                    sy = y;
                    break;
                }
            }
            if (sx < 0)
                return false;

            var contour = new List<Vector2Int>(64);
            int cx = sx;
            int cy = sy;
            int cameFrom = 2;
            int guard = w * h + 2;
            do
            {
                contour.Add(new Vector2Int(cx, cy));
                int look = (cameFrom + 1) % 8;
                bool found = false;
                for (int k = 0; k < 8; k++)
                {
                    int dir = (look + k) % 8;
                    int nx = cx + MooreX[dir];
                    int ny = cy + MooreY[dir];
                    if ((uint)nx >= (uint)w || (uint)ny >= (uint)h || pixels[ny * w + nx].a <= 24)
                        continue;
                    cameFrom = (dir + 4) % 8;
                    cx = nx;
                    cy = ny;
                    found = true;
                    break;
                }
                if (!found)
                    break;
            }
            while ((cx != sx || cy != sy) && --guard > 0 && contour.Count < w * h);
            if (contour.Count < 3)
                return false;

            ResampleClosedContour(contour, 16, outline);
            // Pixel centres cut the outer half pixel off; push each point out by ~1.5 px.
            Vector2 centre = Vector2.zero;
            foreach (var p in outline)
                centre += p;
            centre /= outline.Count;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 px = new Vector2(outline[i].x + 0.5f, outline[i].y + 0.5f);
                Vector2 dir = (px - centre).normalized;
                px += dir * 1.5f;
                outline[i] = new Vector2(Mathf.Clamp01(px.x / w), Mathf.Clamp01(px.y / h));
            }
            return outline.Count >= 3;
        }

        static readonly int[] MooreX = { 1, 1, 0, -1, -1, -1, 0, 1 };
        static readonly int[] MooreY = { 0, 1, 1, 1, 0, -1, -1, -1 };

        /// <summary>Evenly spaced contour points, in pixels.</summary>
        static void ResampleClosedContour(List<Vector2Int> contour, int maxPoints, List<Vector2> outline)
        {
            int n = contour.Count;
            float length = 0f;
            var edge = new float[n];
            for (int i = 0; i < n; i++)
            {
                edge[i] = Vector2.Distance(contour[i], contour[(i + 1) % n]);
                length += edge[i];
            }
            int count = Mathf.Clamp(Mathf.RoundToInt(length / 6f), 4, maxPoints);
            if (n <= count)
                count = Mathf.Min(n, maxPoints);
            float step = length / count;
            float walked = 0f;
            float next = 0f;
            for (int i = 0; i < n && outline.Count < count; i++)
            {
                if (walked + 0.001f < next && i != 0)
                {
                    walked += edge[i];
                    continue;
                }
                outline.Add(contour[i]);
                next += step;
                walked += edge[i];
            }
        }

        static bool TryReadCellPixels(Texture2D tex, SpriteSheetDef sheet, int cell, out Color32[] pixels, out int w, out int h)
        {
            pixels = null;
            w = 0;
            h = 0;
            if (!TryGetCellPixelRect(tex, sheet, cell, out var pixel))
                return false;
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            Texture2D tmp = null;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                tmp = new Texture2D(pixel.width, pixel.height, TextureFormat.RGBA32, false, true);
                tmp.ReadPixels(new Rect(pixel.x, pixel.y, pixel.width, pixel.height), 0, 0);
                tmp.Apply();
                pixels = tmp.GetPixels32();
                w = pixel.width;
                h = pixel.height;
                return pixels != null && pixels.Length == w * h;
            }
            catch (System.Exception)
            {
                return false;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (tmp != null)
                    Object.DestroyImmediate(tmp);
            }
        }

        static bool TryGetCellPixelRect(Texture2D tex, SpriteSheetDef sheet, int cell, out RectInt pixel)
        {
            pixel = default;
            if (tex == null || tex.width < 2 || tex.height < 2)
                return false;
            if (SpriteSheetProfile.TryGetCroppedCellPixelRect(sheet, cell, out pixel))
                return pixel.width > 1 && pixel.height > 1;
            int columns = Mathf.Max(1, sheet != null ? sheet.Columns : 1);
            int rows = Mathf.Max(1, sheet != null ? sheet.Rows : 1);
            int count = columns * rows;
            cell = ((cell % count) + count) % count;
            int cw = Mathf.Max(1, tex.width / columns);
            int ch = Mathf.Max(1, tex.height / rows);
            pixel = new RectInt((cell % columns) * cw, tex.height - (cell / columns + 1) * ch, cw, ch);
            return pixel.width > 1 && pixel.height > 1;
        }
    }
}
