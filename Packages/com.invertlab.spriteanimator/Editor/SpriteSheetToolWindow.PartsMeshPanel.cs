using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Floating Mesh panel docked on the right of the pose canvas (Warp tool and Edit Mesh).
    // Everything needed while shaping a mesh sits next to it: tools, exact vertex values, soft
    // selection and weights, instead of the narrow inspector column.
    public sealed partial class SpriteSheetToolWindow
    {
        const float PartsMeshPanelWidth = 280f;
        [SerializeField] bool _partsMeshPanelOpen = true;
        Vector2 _partsMeshPanelScroll;

        bool PartsMeshPanelVisible()
            => _profile != null && !IsPartsPivotFocus() && PanelSlot() != null
               && (IsPartsMeshEdit() || (_partsCanvasTool == PartsCanvasTool.Warp && _partsMode == SpritePartsStudioMode.Animate));

        SpritePartSlotDef PanelSlot() => IsPartsMeshEdit() ? MeshEditSlot() : CurrentPartsSlot;

        /// <summary>Panel bounds in the same space as <paramref name="canvas"/>. Zero when hidden.</summary>
        Rect PartsMeshPanelRect(Rect canvas)
        {
            if (!PartsMeshPanelVisible())
                return Rect.zero;
            float w = _partsMeshPanelOpen ? PartsMeshPanelWidth : 110f;
            float h = _partsMeshPanelOpen ? canvas.height - 16f - 40f : 24f;
            return new Rect(canvas.xMax - w - 8f, canvas.y + 8f, w, Mathf.Max(24f, h));
        }

        void DrawPartsMeshPanel(Rect r)
        {
            if (r.width <= 0f || r.height <= 0f)
                return;
            var slot = PanelSlot();
            if (slot == null)
                return;
            EditorGUI.DrawRect(r, new Color(0.09f, 0.1f, 0.13f, 0.97f));
            DrawGuiRectOutline(r, new Color(0.35f, 0.85f, 0.9f, 0.45f), 1f);

            var header = new Rect(r.x, r.y, r.width, 22f);
            EditorGUI.DrawRect(header, new Color(0.12f, 0.18f, 0.28f, 1f));
            string title = IsPartsMeshEdit() ? "EDIT MESH" : "DEFORM";
            GUI.Label(new Rect(header.x + 6f, header.y + 3f, header.width - 40f, 16f),
                _partsMeshPanelOpen ? title + "  " + slot.Name : "MESH", _sectionStyle);
            if (GUI.Button(new Rect(header.xMax - 26f, header.y + 2f, 22f, 18f),
                    new GUIContent(_partsMeshPanelOpen ? "-" : "+", "Collapse / expand the Mesh panel"), _partsTabStyle))
            {
                _partsMeshPanelOpen = !_partsMeshPanelOpen;
                Repaint();
            }
            if (!_partsMeshPanelOpen)
                return;

            float oldLabel = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 92f;
            GUILayout.BeginArea(new Rect(r.x + 6f, r.y + 26f, r.width - 12f, r.height - 30f));
            _partsMeshPanelScroll = EditorGUILayout.BeginScrollView(_partsMeshPanelScroll);
            try
            {
                DrawPanelMeshSection(slot);
                DrawPanelSelectionSection(slot);
                if (!IsPartsMeshEdit())
                    DrawPartsMeshToolsSection();
                else if (_partsMeshTool == PartsMeshTool.Modify)
                    DrawPanelSoftForModifyHint();
                DrawPartsWeightsInspector(slot);
                DrawPanelHelp();
            }
            finally
            {
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
                EditorGUIUtility.labelWidth = oldLabel;
            }
        }

        void DrawPanelMeshSection(SpritePartSlotDef slot)
        {
            var mesh = slot.Mesh;
            EditorGUILayout.LabelField(mesh != null && mesh.HasMesh
                ? mesh.VertexCount + " vertices   " + mesh.HullCount + " hull   " + mesh.Triangles.Length / 3 + " triangles"
                    + (mesh.HasWeights ? "   " + mesh.BoneCount + " bones" : string.Empty)
                : "No mesh. The part draws as a rectangle.", _mutedStyle);

            if (IsPartsMeshEdit())
            {
                var tool = (PartsMeshTool)GUILayout.Toolbar((int)_partsMeshTool,
                    new[] { new GUIContent("Modify", "1: drag vertices; the image stays flat"),
                            new GUIContent("Create", "2: draw vertices and edges, then Make Polygons"),
                            new GUIContent("Delete", "3: click a vertex or edge"),
                            new GUIContent("Weights", "4: bind bones and paint weights") },
                    GUILayout.Height(20f));
                if (tool != _partsMeshTool)
                    SetPartsMeshTool(tool);
                if (_partsMeshTool == PartsMeshTool.Create)
                    DrawPanelCreateSection(mesh);
                else if (mesh != null && SpritePartsMeshOps.IsDraft(mesh) && mesh.VertexCount > 0)
                {
                    EditorGUILayout.LabelField("No polygons yet: the part draws as a rectangle.", EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button(new GUIContent("Make Polygons", "Fill every closed loop of edges with triangles"), _primaryStyle))
                        MakeMeshPolygons();
                }
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("New", "The four image corners")))
                    ResetPartsMeshToQuad();
                if (GUILayout.Button(new GUIContent("Trace", "Hull that follows the image alpha")))
                    TracePartsMesh();
                if (GUILayout.Button(new GUIContent("Generate", "Fill the hull with interior vertices")))
                    GeneratePartsMeshInterior();
                if (GUILayout.Button(new GUIContent("Remove", "Back to a rigid rectangle")))
                    RemovePartsMesh();
                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button(new GUIContent("Done (Esc)", "Leave Edit Mesh and go back to posing")))
                    TryExitPartsMeshEdit();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Edit Mesh", "Setup mesh: hull, interior vertices, edges, weights")))
                    EnterPartsMeshEdit(slot.SlotId);
                using (new EditorGUI.DisabledScope(mesh == null || !mesh.HasMesh))
                {
                    if (GUILayout.Button(new GUIContent("Reset Deform", "Selected vertices, or all, back to the setup mesh on this key")))
                        ResetSelectedPartsDeform();
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        void DrawPanelCreateSection(SpritePartMeshDef mesh)
        {
            _partsCreateMode = (PartsCreateMode)GUILayout.Toolbar((int)_partsCreateMode,
                new[] { new GUIContent("Vertex+Edge", "Click: add a vertex joined to the last one. Click a vertex: join it. Click an edge: add a vertex on it."),
                        new GUIContent("Vertex", "Click: add a vertex, no edge. Right-click: delete a vertex."),
                        new GUIContent("Edge", "Click two vertices to join them. Click an edge to turn it. Right-click: delete an edge.") },
                GUILayout.Height(20f));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Auto Link", "Add edges between the vertices so the shape can be filled. Check them after.")))
                AutoLinkMeshEdges();
            bool draft = mesh != null && SpritePartsMeshOps.IsDraft(mesh) && mesh.VertexCount > 0;
            if (GUILayout.Button(new GUIContent("Make Polygons", "Fill every closed loop of edges with triangles"), draft ? _primaryStyle : GUI.skin.button))
                MakeMeshPolygons();
            EditorGUILayout.EndHorizontal();
            if (draft)
                EditorGUILayout.LabelField("No polygons yet. Close the loop (click the first vertex), then Make Polygons.",
                    EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(
                "Drag on empty space: select. Shift+click: add to selection. Right-click a vertex/edge: delete; " +
                "on a selection or empty space: Connect, Merge, Delete... Shift: vertex at each crossing. Ctrl: snap.",
                EditorStyles.wordWrappedMiniLabel);
        }

        // ------------------------------------------------------------------ selection values

        /// <summary>Pixel size of the part image, so values read like the art. Falls back to percent.</summary>
        bool TryGetPanelPixelSize(SpritePartSlotDef slot, out Vector2 pixels)
        {
            pixels = new Vector2(100f, 100f);
            var app = ResolvePartsPreviewAppearance(slot, _partsPreviewTime);
            var sheet = app != null ? _profile.SheetAt(app.SheetIndex) : null;
            if (sheet?.Texture == null || !TryGetCellPixelRect(sheet.Texture, sheet, app.CellIndex, out var cell))
                return false;
            pixels = new Vector2(cell.width, cell.height);
            return true;
        }

        void DrawPanelSelectionSection(SpritePartSlotDef slot)
        {
            GUILayout.Space(6f);
            GUILayout.Label("SELECTION", _sectionStyle);
            bool mine = SpritePartIdUtility.Canonical(slot.SlotId) == _partsWarpSelectionSlotId;
            int count = mine ? _partsWarpSelection.Count : 0;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(count == 0 ? "No vertices selected" : count == 1 ? "Vertex " + _partsWarpSelection[0] : count + " vertices", _mutedStyle);
            if (GUILayout.Button("All", GUILayout.Width(36f)))
                SelectAllMeshVertices();
            if (GUILayout.Button("None", GUILayout.Width(44f)))
            {
                _partsWarpSelection.Clear();
                _partsWarpIndex = -1;
            }
            EditorGUILayout.EndHorizontal();
            if (count == 0)
                return;

            bool pixels = TryGetPanelPixelSize(slot, out var scale);
            string unit = pixels ? " (px)" : " (%)";
            if (IsPartsMeshEdit())
                DrawSetupVertexFields(slot, scale, unit);
            else
                DrawDeformVertexFields(slot, scale, unit);
        }

        /// <summary>Edit Mesh: the rest position on the image (moves the mesh over the flat picture).</summary>
        void DrawSetupVertexFields(SpritePartSlotDef slot, Vector2 scale, string unit)
        {
            var mesh = slot.Mesh;
            if (mesh?.Vertices == null)
                return;
            var ids = ValidSelection(mesh.VertexCount);
            if (ids.Count == 0)
                return;
            Vector2 centre = Vector2.zero;
            foreach (int i in ids)
                centre += mesh.Vertices[i];
            centre /= ids.Count;
            EditorGUI.BeginChangeCheck();
            Vector2 shown = Vector2.Scale(centre, scale);
            Vector2 edited = EditorGUILayout.Vector2Field(
                new GUIContent(ids.Count == 1 ? "Position" + unit : "Centre" + unit,
                    "Where the vertex sits on the image, from the bottom-left corner."), shown);
            if (!EditorGUI.EndChangeCheck())
                return;
            Vector2 delta = new Vector2(
                (edited.x - shown.x) / Mathf.Max(1e-4f, scale.x),
                (edited.y - shown.y) / Mathf.Max(1e-4f, scale.y));
            var work = mesh.Clone();
            var positions = new List<Vector2>(ids.Count);
            foreach (int i in ids)
                positions.Add(mesh.Vertices[i] + delta);
            bool ok = work.HasMesh
                ? SpritePartsMeshOps.TrySetVertices(work, ids, positions)
                : SetPendingVertices(work, ids, positions);
            if (!ok)
            {
                _status = "The hull cannot cross itself.";
                return;
            }
            RecordPartsUndo("Set Mesh Vertex");
            slot.Mesh = work;
            SaveDirty();
        }

        static bool SetPendingVertices(SpritePartMeshDef mesh, List<int> ids, List<Vector2> positions)
        {
            for (int k = 0; k < ids.Count; k++)
                mesh.Vertices[ids[k]] = new Vector2(Mathf.Clamp01(positions[k].x), Mathf.Clamp01(positions[k].y));
            return true;
        }

        /// <summary>Warp: the deform offset on the current key (pre-skin, like Spine).</summary>
        void DrawDeformVertexFields(SpritePartSlotDef slot, Vector2 scale, string unit)
        {
            var pose = SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);
            var lattice = pose.Lattice;
            if (!lattice.HasMesh)
            {
                EditorGUILayout.LabelField("Drag a corner on the canvas to make a mesh.", _mutedStyle);
                return;
            }
            var ids = ValidSelection(lattice.PointCount);
            if (ids.Count == 0)
                return;
            float2 avg = float2.zero;
            foreach (int i in ids)
                avg += lattice.GetOffset(i);
            avg /= ids.Count;
            Vector2 shown = new Vector2(avg.x * scale.x, avg.y * scale.y);
            EditorGUI.BeginChangeCheck();
            Vector2 edited = EditorGUILayout.Vector2Field(
                new GUIContent("Offset" + unit, "How far the vertex is moved from the setup mesh on this key."), shown);
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUILayout.BeginHorizontal();
            bool reset = GUILayout.Button(new GUIContent("Reset Selected", "Back to the setup mesh on this key"));
            bool nudge = false;
            Vector2 step = Vector2.zero;
            if (GUILayout.Button("←", GUILayout.Width(24f))) { nudge = true; step = new Vector2(-1f, 0f); }
            if (GUILayout.Button("→", GUILayout.Width(24f))) { nudge = true; step = new Vector2(1f, 0f); }
            if (GUILayout.Button("↑", GUILayout.Width(24f))) { nudge = true; step = new Vector2(0f, 1f); }
            if (GUILayout.Button("↓", GUILayout.Width(24f))) { nudge = true; step = new Vector2(0f, -1f); }
            EditorGUILayout.EndHorizontal();
            if (reset)
            {
                ResetPartsDeform(slot.SlotId, ids);
                return;
            }
            if (!changed && !nudge)
                return;
            Vector2 deltaShown = nudge ? step : edited - shown;
            var delta = new float2(deltaShown.x / Mathf.Max(1e-4f, scale.x), deltaShown.y / Mathf.Max(1e-4f, scale.y));
            foreach (int i in ids)
                lattice.SetPoint(i, lattice.GetPoint(i) + delta);
            pose.Lattice = lattice;
            RecordPartsUndo("Set Deform");
            ApplyPartsPoseEdit(slot.SlotId, pose);
        }

        List<int> ValidSelection(int count)
        {
            var ids = new List<int>(_partsWarpSelection.Count);
            foreach (int i in _partsWarpSelection)
            {
                if ((uint)i < (uint)count && !ids.Contains(i))
                    ids.Add(i);
            }
            return ids;
        }

        void DrawPanelSoftForModifyHint()
        {
            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Modify moves the setup mesh; the image stays flat. Deform is posed with Warp.", _mutedStyle);
        }

        void DrawPanelHelp()
        {
            GUILayout.Space(8f);
            string help = IsPartsMeshEdit()
                ? _partsMeshTool == PartsMeshTool.Create
                    ? "Keys: 1 Modify  2 Create  3 Delete  4 Weights  Enter ends the chain  Esc done.\nRight-click a line: add a vertex in the middle, and more."
                    : "Keys: 1 Modify  2 Create  3 Delete  4 Weights  Del delete  Ctrl+A all  Esc done.\nRight-click: edges, generate, trace."
                : "Drag a vertex, or drag inside the green box to move the selection.\nDouble-click a part to edit its mesh. Q moves the whole part.";
            EditorGUILayout.LabelField(help, EditorStyles.wordWrappedMiniLabel);
        }
    }
}
