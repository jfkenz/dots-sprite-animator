using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Create tool, the AnyPortrait mesh workflow: draw vertices and edges freely, then Make Polygons
    // fills every closed loop with triangles. Until then the mesh is a draft (no triangles).
    //   Vertex+Edge  click: add a vertex joined to the last one; click a vertex: join it; click an edge: add a vertex on it
    //   Vertex       click: add a vertex (no edge)
    //   Edge         click two vertices: join them; click an edge: turn it
    //   Right-click  delete a vertex or an edge; on empty space, end the chain
    //   Shift        click: a vertex where the new edge crosses each edge; Shift+right-click keeps the edges
    //   Ctrl         snap to the nearest vertex
    public sealed partial class SpriteSheetToolWindow
    {
        enum PartsCreateMode
        {
            VertexEdge = 0,
            Vertex = 1,
            Edge = 2,
        }

        int _partsMeshPen = -1;
        bool _partsCreateClickPending;
        bool _partsCreateClickShift;
        [SerializeField] PartsCreateMode _partsCreateMode = PartsCreateMode.VertexEdge;

        string MeshFullMessage()
            => "Mesh is full (" + SpritePartsMeshOps.MaxVertices + " vertices). Delete some vertices first.";

        void EndMeshPen()
        {
            _partsMeshPen = -1;
            _status = "Chain ended. Click to start a new one.";
            Repaint();
        }

        static int[] IdentityRemap(int n)
        {
            var map = new int[n];
            for (int i = 0; i < n; i++)
                map[i] = i;
            return map;
        }

        /// <summary>
        /// One Create edit: runs on a draft copy of the mesh, then commits it (undo, deform keys, save).
        /// <paramref name="edit"/> returns the remap from old to new vertices, or null when nothing changed.
        /// </summary>
        bool EditMeshDraft(string label, System.Func<SpritePartMeshDef, int[]> edit)
        {
            var slot = MeshEditSlot();
            if (slot == null)
                return false;
            var before = slot.Mesh ?? new SpritePartMeshDef();
            var work = before.Clone();
            SpritePartsMeshOps.ToDraft(work);
            var remap = edit(work);
            if (remap == null)
                return false;
            RecordPartsUndo(label);
            slot.Mesh = work;
            SpritePartsAuthoringOps.RemapSlotDeforms(_profile, slot.SlotId, remap, work.VertexCount);
            SaveDirty();
            return true;
        }

        /// <summary>
        /// Left press in Create. On empty space it waits: a drag draws a selection box, a plain click
        /// adds the vertex on release (<see cref="FinishCreateBoxOrClick"/>).
        /// </summary>
        void CreateMouseDown(Rect sprite, Event evt, int controlId, SpritePartMeshDef mesh)
        {
            bool ctrl = evt.control || evt.command;
            if (!ctrl && HitMeshVertex(sprite, mesh, evt.mousePosition) < 0
                && !HitMeshGraphEdge(sprite, mesh, evt.mousePosition, out _, out _))
            {
                _partsWarpBox = true;
                _partsWarpBoxStart = evt.mousePosition;
                _partsWarpBoxEnd = evt.mousePosition;
                _partsCreateClickPending = true;
                _partsCreateClickShift = evt.shift;
                CapturePartsMeshDrag(controlId);
                return;
            }
            CreateLeftClick(sprite, evt.mousePosition, evt.shift, ctrl, controlId, mesh);
        }

        /// <summary>Release after a press on empty space: a click adds a vertex, a drag selects.</summary>
        void FinishCreateBoxOrClick(Rect sprite, Vector2 mouse, bool shift)
        {
            _partsCreateClickPending = false;
            var mesh = MeshEditSlot()?.Mesh;
            if (mesh == null)
                return;
            if ((mouse - _partsWarpBoxStart).sqrMagnitude < 36f)
            {
                CreateLeftClick(sprite, _partsWarpBoxStart, _partsCreateClickShift, false, 0, mesh);
                return;
            }
            FinishMeshBox(sprite, mouse, shift);
            _partsMeshPen = -1; // a selection ends the chain; right-click for Connect / Merge
            if (_partsWarpSelection.Count > 1)
                _status = _partsWarpSelection.Count + " selected. Right-click: connect, merge, delete. Drag one to move them all.";
        }

        void CreateLeftClick(Rect sprite, Vector2 mouse, bool shift, bool ctrl, int controlId, SpritePartMeshDef mesh)
        {
            bool cut = shift && !ctrl;
            Vector2 uv = MeshGuiToUv(sprite, mouse);
            int n = mesh.VertexCount;
            int pen = (uint)_partsMeshPen < (uint)n ? _partsMeshPen : -1;
            int hit = ctrl && n > 0 ? NearestMeshVertex(mesh, uv, pen) : HitMeshVertex(sprite, mesh, mouse);
            int ea = -1, eb = -1;
            bool onEdge = hit < 0 && HitMeshGraphEdge(sprite, mesh, mouse, out ea, out eb);

            if (_partsCreateMode == PartsCreateMode.Edge)
            {
                if (hit >= 0 && pen >= 0 && pen != hit)
                {
                    int from = pen, to = hit;
                    if (EditMeshDraft(cut ? "Cut Mesh Edge" : "Add Mesh Edge",
                            w => SpritePartsMeshOps.TryConnectGraph(w, from, to, cut) ? IdentityRemap(n) : null))
                        _status = "Edge " + from + " - " + to + ". Click another vertex to go on, right-click to stop.";
                    _partsMeshPen = to;
                    SelectOnly(to);
                }
                else if (hit >= 0)
                {
                    _partsMeshPen = hit;
                    SelectOnly(hit);
                    _status = "From vertex " + hit + ". Click the vertex to join it to.";
                }
                else if (onEdge)
                {
                    _status = EditMeshDraft("Turn Mesh Edge", w => SpritePartsMeshOps.TryTurnGraphEdge(w, ea, eb) ? IdentityRemap(n) : null)
                        ? "Edge turned."
                        : "That edge cannot turn: it needs a triangle on both sides.";
                }
                else
                {
                    _partsMeshPen = -1;
                    _status = "Edge: click a vertex, then the vertex to join it to.";
                }
                return;
            }

            if (hit >= 0)
            {
                if (shift && (pen < 0 || _partsCreateMode == PartsCreateMode.Vertex))
                {
                    // No chain to cut from: Shift adds to (or removes from) the selection.
                    SelectWarpVertex(hit, true);
                    _partsMeshPen = -1;
                    _status = _partsWarpSelection.Count + " selected. Right-click for Connect / Merge.";
                    return;
                }
                if (_partsCreateMode == PartsCreateMode.VertexEdge && pen >= 0 && pen != hit)
                {
                    int from = pen, to = hit;
                    if (EditMeshDraft(cut ? "Cut Mesh Edge" : "Add Mesh Edge",
                            w => SpritePartsMeshOps.TryConnectGraph(w, from, to, cut) ? IdentityRemap(n) : null))
                        _status = "Joined " + from + " - " + to + ". Right-click empty space to stop the chain.";
                    _partsMeshPen = to;
                    SelectOnly(to);
                    return;
                }
                // Pick the vertex: the chain goes on from it, and dragging moves it (with the rest of a selection).
                if (!_partsWarpSelection.Contains(hit) || _partsWarpSelection.Count < 2)
                    SelectOnly(hit);
                _partsMeshPen = _partsCreateMode == PartsCreateMode.VertexEdge && _partsWarpSelection.Count == 1 ? hit : -1;
                BeginMeshVertexDrag(mouse, controlId, mesh);
                _status = "Vertex " + hit + ". Drag to move" + (_partsMeshPen >= 0 ? ", or click to draw from it." : ".");
                return;
            }

            if (n >= SpritePartsMeshOps.MaxVertices)
            {
                _status = MeshFullMessage();
                return;
            }
            bool chain = _partsCreateMode == PartsCreateMode.VertexEdge && pen >= 0;
            int added = -1;
            bool ok = EditMeshDraft(onEdge ? "Add Vertex On Edge" : "Add Mesh Vertex", w =>
            {
                bool placed = onEdge
                    ? SpritePartsMeshOps.TrySplitGraphEdge(w, ea, eb, uv, out added)
                    : SpritePartsMeshOps.TryAddGraphVertex(w, uv, out added);
                if (!placed)
                    return null;
                if (chain)
                    SpritePartsMeshOps.TryConnectGraph(w, pen, added, cut);
                return IdentityRemap(n);
            });
            if (!ok)
            {
                _status = "Could not add a vertex there.";
                return;
            }
            SelectOnly(added);
            _partsMeshPen = _partsCreateMode == PartsCreateMode.VertexEdge ? added : -1;
            var now = MeshEditSlot()?.Mesh;
            _status = "Vertex " + added + (onEdge ? " on the edge" : string.Empty)
                + (chain ? ", joined to " + pen : string.Empty)
                + ". " + (now != null && now.VertexCount >= 3 ? "Close the loop, then Make Polygons." : "Keep clicking.");
        }

        /// <summary>
        /// Right-click in Create: deletes the vertex or edge under the mouse (AnyPortrait). On a selected
        /// vertex of a multi-selection, or on empty space, it opens the menu (Connect, Merge, ...).
        /// </summary>
        void CreateRightClick(Rect sprite, Event evt, SpritePartMeshDef mesh)
        {
            int n = mesh.VertexCount;
            int hit = HitMeshVertex(sprite, mesh, evt.mousePosition);
            bool inSelection = hit >= 0 && _partsWarpSelection.Count > 1 && _partsWarpSelection.Contains(hit);
            if (hit >= 0 && !inSelection && _partsCreateMode != PartsCreateMode.Edge)
            {
                bool keep = evt.shift;
                int[] removed = null;
                if (EditMeshDraft("Delete Mesh Vertex", w =>
                    {
                        SpritePartsMeshOps.TryRemoveGraphVertex(w, hit, keep, out removed);
                        return removed;
                    }))
                {
                    _partsWarpSelection.Clear();
                    _partsWarpIndex = -1;
                    _partsWarpHover = -1;
                    _partsMeshPen = (uint)_partsMeshPen < (uint)n && removed != null ? removed[_partsMeshPen] : -1;
                    _status = "Vertex deleted" + (keep ? ", its edges joined." : ".");
                }
                return;
            }
            if (hit < 0 && _partsCreateMode != PartsCreateMode.Vertex
                && HitMeshGraphEdge(sprite, mesh, evt.mousePosition, out int ea, out int eb))
            {
                if (EditMeshDraft("Delete Mesh Edge", w => SpritePartsMeshOps.TryRemoveEdge(w, ea, eb) ? IdentityRemap(n) : null))
                    _status = "Edge deleted.";
                return;
            }
            if (hit >= 0 && !inSelection)
                SelectOnly(hit);
            ShowCreateContextMenu(mesh);
        }

        void ShowCreateContextMenu(SpritePartMeshDef mesh)
        {
            var menu = new GenericMenu();
            int n = mesh.VertexCount;
            var sel = ValidSelection(n);
            if (sel.Count == 2)
            {
                int a = sel[0], b = sel[1];
                bool joined = SpritePartsMeshOps.GraphEdges(mesh).Exists(e => (e.x == a && e.y == b) || (e.x == b && e.y == a));
                if (joined)
                {
                    menu.AddItem(new GUIContent("Disconnect"), false, DisconnectSelectedGraph);
                    menu.AddItem(new GUIContent("Add Vertex Between"), false, () => AddVertexBetween(a, b));
                }
                else
                    menu.AddItem(new GUIContent("Connect"), false, () => ConnectSelectedGraph(false));
                menu.AddItem(new GUIContent("Merge"), false, MergeSelectedGraph);
            }
            else if (sel.Count > 2)
            {
                menu.AddItem(new GUIContent("Connect As Chain"), false, () => ConnectSelectedGraph(false));
                menu.AddItem(new GUIContent("Connect As Loop"), false, () => ConnectSelectedGraph(true));
                menu.AddItem(new GUIContent("Disconnect"), false, DisconnectSelectedGraph);
                menu.AddItem(new GUIContent("Merge"), false, MergeSelectedGraph);
            }
            if (sel.Count > 0)
            {
                string what = sel.Count == 1 ? "Vertex" : sel.Count + " Vertices";
                menu.AddItem(new GUIContent("Delete " + what), false, () => DeleteSelectedGraph(false));
                menu.AddItem(new GUIContent("Delete " + what + ", Keep Edges"), false, () => DeleteSelectedGraph(true));
                menu.AddSeparator(string.Empty);
            }
            if ((uint)_partsMeshPen < (uint)n)
                menu.AddItem(new GUIContent("End Chain (Enter)"), false, EndMeshPen);
            menu.AddItem(new GUIContent("Select All (Ctrl+A)"), false, SelectAllMeshVertices);
            if (sel.Count > 0)
                menu.AddItem(new GUIContent("Deselect"), false, () =>
                {
                    _partsWarpSelection.Clear();
                    _partsWarpIndex = -1;
                    _partsMeshPen = -1;
                    Repaint();
                });
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Auto Link"), false, AutoLinkMeshEdges);
            menu.AddItem(new GUIContent("Make Polygons"), false, MakeMeshPolygons);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Mode/Vertex+Edge"), _partsCreateMode == PartsCreateMode.VertexEdge, () => _partsCreateMode = PartsCreateMode.VertexEdge);
            menu.AddItem(new GUIContent("Mode/Vertex"), _partsCreateMode == PartsCreateMode.Vertex, () => _partsCreateMode = PartsCreateMode.Vertex);
            menu.AddItem(new GUIContent("Mode/Edge"), _partsCreateMode == PartsCreateMode.Edge, () => _partsCreateMode = PartsCreateMode.Edge);
            menu.ShowAsContext();
        }

        /// <summary>Joins the selected vertices in the order they were picked (loop: last back to first).</summary>
        void ConnectSelectedGraph(bool loop)
        {
            int n = MeshEditSlot()?.Mesh?.VertexCount ?? 0;
            var sel = ValidSelection(n);
            if (sel.Count < 2)
                return;
            int made = 0;
            EditMeshDraft(loop ? "Connect As Loop" : "Connect Vertices", w =>
            {
                for (int i = 0; i + 1 < sel.Count; i++)
                    made += SpritePartsMeshOps.TryConnectGraph(w, sel[i], sel[i + 1], false) ? 1 : 0;
                if (loop && sel.Count > 2)
                    made += SpritePartsMeshOps.TryConnectGraph(w, sel[sel.Count - 1], sel[0], false) ? 1 : 0;
                return made > 0 ? IdentityRemap(n) : null;
            });
            _status = made > 0 ? "Connected " + sel.Count + " vertices." : "Already connected.";
            Repaint();
        }

        void DisconnectSelectedGraph()
        {
            int n = MeshEditSlot()?.Mesh?.VertexCount ?? 0;
            var sel = ValidSelection(n);
            int removed = 0;
            EditMeshDraft("Disconnect Vertices", w =>
            {
                for (int i = 0; i < sel.Count; i++)
                {
                    for (int j = i + 1; j < sel.Count; j++)
                        removed += SpritePartsMeshOps.TryRemoveEdge(w, sel[i], sel[j]) ? 1 : 0;
                }
                return removed > 0 ? IdentityRemap(n) : null;
            });
            _status = removed > 0 ? "Removed " + removed + " edges." : "No edges between them.";
            Repaint();
        }

        void MergeSelectedGraph()
        {
            int n = MeshEditSlot()?.Mesh?.VertexCount ?? 0;
            var sel = ValidSelection(n);
            if (sel.Count < 2)
                return;
            int survivor = -1;
            if (!EditMeshDraft("Merge Mesh Vertices", w =>
                    SpritePartsMeshOps.TryMergeGraphVertices(w, sel, out survivor, out var remap) ? remap : null))
            {
                _status = "Could not merge those vertices.";
                return;
            }
            SelectOnly(survivor);
            _partsMeshPen = _partsCreateMode == PartsCreateMode.VertexEdge ? survivor : -1;
            _partsWarpHover = -1;
            _status = "Merged " + sel.Count + " vertices into vertex " + survivor + ".";
            Repaint();
        }

        void AddVertexBetween(int a, int b)
        {
            var mesh = MeshEditSlot()?.Mesh;
            int n = mesh?.VertexCount ?? 0;
            if ((uint)a >= (uint)n || (uint)b >= (uint)n)
                return;
            Vector2 mid = (mesh.Vertices[a] + mesh.Vertices[b]) * 0.5f;
            int added = -1;
            if (!EditMeshDraft("Add Vertex Between", w =>
                    SpritePartsMeshOps.TrySplitGraphEdge(w, a, b, mid, out added) ? IdentityRemap(n) : null))
            {
                _status = n >= SpritePartsMeshOps.MaxVertices ? MeshFullMessage() : "Could not add a vertex there.";
                return;
            }
            SelectOnly(added);
            _status = "Vertex " + added + " between " + a + " and " + b + ".";
            Repaint();
        }

        void DeleteSelectedGraph(bool keepEdges)
        {
            int n = MeshEditSlot()?.Mesh?.VertexCount ?? 0;
            var sel = ValidSelection(n);
            if (sel.Count == 0)
                return;
            if (!EditMeshDraft("Delete Mesh Vertices", w =>
                    SpritePartsMeshOps.TryRemoveGraphVertices(w, sel, keepEdges, out var remap) ? remap : null))
                return;
            _partsWarpSelection.Clear();
            _partsWarpIndex = -1;
            _partsWarpHover = -1;
            _partsMeshPen = -1;
            _status = "Deleted " + sel.Count + " vertices" + (keepEdges ? ", their edges joined." : ".");
            Repaint();
        }

        /// <summary>Make Polygons: triangles in every closed loop of edges; the mesh is usable again.</summary>
        void MakeMeshPolygons()
        {
            var slot = MeshEditSlot();
            if (slot?.Mesh == null || slot.Mesh.VertexCount == 0)
            {
                _status = "Add vertices and edges first.";
                return;
            }
            var work = slot.Mesh.Clone();
            SpritePartsMeshOps.ToDraft(work);
            if (!SpritePartsMeshOps.TryMakePolygons(work, out var remap, out string message))
            {
                _status = message;
                return;
            }
            RecordPartsUndo("Make Polygons");
            slot.Mesh = work;
            SpritePartsAuthoringOps.RemapSlotDeforms(_profile, slot.SlotId, remap, work.VertexCount);
            _partsMeshPen = -1;
            _partsWarpSelection.Clear();
            _partsWarpIndex = -1;
            _partsWarpHover = -1;
            SaveDirty();
            _status = message;
            Repaint();
        }

        /// <summary>Leaving Create with a draft: fill it, like pressing Make Polygons. Quiet when there is nothing yet.</summary>
        void MakeMeshPolygonsIfDraft()
        {
            var mesh = MeshEditSlot()?.Mesh;
            if (mesh != null && SpritePartsMeshOps.IsDraft(mesh) && mesh.VertexCount >= 3 && (mesh.Edges?.Length ?? 0) >= 6)
                MakeMeshPolygons();
        }

        void AutoLinkMeshEdges()
        {
            var slot = MeshEditSlot();
            int n = slot?.Mesh?.VertexCount ?? 0;
            if (n < 3)
            {
                _status = "Auto Link needs at least 3 vertices.";
                return;
            }
            int added = 0;
            EditMeshDraft("Auto Link Edges", w =>
            {
                added = SpritePartsMeshOps.AutoLinkGraph(w);
                return added > 0 ? IdentityRemap(n) : null;
            });
            _status = added > 0 ? "Auto Link added " + added + " edges. Check them, then Make Polygons." : "No edges to add.";
        }

        void BeginMeshVertexDrag(Vector2 mouse, int controlId, SpritePartMeshDef mesh)
        {
            _partsMeshDrag = true;
            _partsMeshMoved = false;
            _partsMeshDragStart = mesh.Clone();
            _partsDragStartMouse = mouse;
            CapturePartsMeshDrag(controlId);
        }

        static bool HitMeshGraphEdge(Rect sprite, SpritePartMeshDef mesh, Vector2 mouse, out int a, out int b)
        {
            a = b = -1;
            float best = PartsMeshEdgeSnap;
            foreach (var e in SpritePartsMeshOps.GraphEdges(mesh))
            {
                float d = SpritePartsMeshOps.DistanceToSegment(mouse,
                    MeshUvToGui(sprite, mesh.Vertices[e.x]), MeshUvToGui(sprite, mesh.Vertices[e.y]));
                if (d > best)
                    continue;
                best = d;
                a = e.x;
                b = e.y;
            }
            return a >= 0;
        }

        void SelectOnly(int index)
        {
            _partsWarpSelection.Clear();
            _partsWarpSelection.Add(index);
            _partsWarpIndex = index;
        }

        static int NearestMeshVertex(SpritePartMeshDef mesh, Vector2 uv, int except)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                if (i == except)
                    continue;
                float d = (mesh.Vertices[i] - uv).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Hovered edge, and the line from the last vertex to the mouse (cut markers with Shift).</summary>
        void DrawMeshCreatePreview(Rect sprite, SpritePartMeshDef mesh)
        {
            if (_partsMeshTool != PartsMeshTool.Create || mesh?.Vertices == null || Event.current.type != EventType.Repaint
                || _partsMeshDrag || _partsWarpBox)
                return;
            Vector2 mouseUv = _partsMeshMouseUv;
            if (mouseUv.x < -0.2f || mouseUv.x > 1.2f || mouseUv.y < -0.2f || mouseUv.y > 1.2f)
                return;
            Vector2 mouse = MeshUvToGui(sprite, mouseUv);
            int n = mesh.VertexCount;
            bool ctrl = Event.current.control || Event.current.command;
            bool cut = Event.current.shift && !ctrl;
            int pen = (uint)_partsMeshPen < (uint)n ? _partsMeshPen : -1;

            if (_partsWarpHover < 0 && !ctrl && HitMeshGraphEdge(sprite, mesh, mouse, out int ha, out int hb))
                DrawGuiLine(MeshUvToGui(sprite, mesh.Vertices[ha]), MeshUvToGui(sprite, mesh.Vertices[hb]), Color.white, 3f);

            if (pen < 0 || _partsCreateMode == PartsCreateMode.Vertex)
                return;
            Vector2 target = mouseUv;
            if (ctrl)
            {
                int near = NearestMeshVertex(mesh, mouseUv, pen);
                if (near >= 0)
                    target = mesh.Vertices[near];
            }
            else if (_partsWarpHover >= 0)
                target = mesh.Vertices[_partsWarpHover];
            else if (_partsCreateMode == PartsCreateMode.Edge)
                return; // the Edge tool only joins existing vertices
            Vector2 from = mesh.Vertices[pen];
            var line = ctrl ? new Color(0.35f, 0.95f, 1f, 0.95f) : new Color(1f, 0.72f, 0.25f, 0.9f);
            DrawGuiLine(MeshUvToGui(sprite, from), MeshUvToGui(sprite, target), line, 1.5f);
            if (!cut)
                return;
            foreach (var e in SpritePartsMeshOps.GraphEdges(mesh))
            {
                if (e.x == pen || e.y == pen)
                    continue;
                if (!SpritePartsMeshOps.TrySegmentHit(from, target, mesh.Vertices[e.x], mesh.Vertices[e.y], out _, out var p))
                    continue;
                Vector2 g = MeshUvToGui(sprite, p);
                EditorGUI.DrawRect(new Rect(g.x - 3f, g.y - 3f, 6f, 6f), new Color(1f, 0.95f, 0.4f, 1f));
            }
        }

        // ------------------------------------------------------------------ Merge / Split (right-click)

        void MergeSelectedMeshPair()
        {
            var slot = MeshEditSlot();
            if (slot?.Mesh == null || _partsWarpSelection.Count != 2)
                return;
            int a = Mathf.Min(_partsWarpSelection[0], _partsWarpSelection[1]);
            int b = Mathf.Max(_partsWarpSelection[0], _partsWarpSelection[1]);
            bool hadMesh = slot.Mesh.HasMesh;
            var work = slot.Mesh.Clone();
            if (!SpritePartsMeshOps.TryMergeVertices(work, a, b, out int survivor, out var remap))
            {
                _status = "Could not merge those vertices (a hull needs 3 vertices and cannot cross itself).";
                return;
            }
            RecordPartsUndo("Merge Mesh Vertices");
            slot.Mesh = work;
            if (hadMesh)
                SpritePartsAuthoringOps.RemapSlotDeforms(_profile, slot.SlotId, remap, work.VertexCount);
            SelectOnly(survivor);
            _partsMeshPen = -1;
            SaveDirty();
            _status = "Merged into vertex " + survivor;
        }

        void SplitSelectedMeshPair()
        {
            var slot = MeshEditSlot();
            if (slot?.Mesh == null || _partsWarpSelection.Count != 2)
                return;
            int a = _partsWarpSelection[0];
            int b = _partsWarpSelection[1];
            var work = slot.Mesh.Clone();
            Vector2 mid = (work.Vertices[a] + work.Vertices[b]) * 0.5f;
            if (!SpritePartsMeshOps.TrySplitEdgeAt(work, a, b, mid, out int index, out var remap))
            {
                _status = "Split needs an edge between the two vertices.";
                return;
            }
            RecordPartsUndo("Split Mesh Edge");
            slot.Mesh = work;
            SpritePartsAuthoringOps.RemapSlotDeforms(_profile, slot.SlotId, remap, work.VertexCount);
            SelectOnly(index);
            SaveDirty();
            _status = "Split. Vertex " + index + " is in the middle.";
        }
    }
}
