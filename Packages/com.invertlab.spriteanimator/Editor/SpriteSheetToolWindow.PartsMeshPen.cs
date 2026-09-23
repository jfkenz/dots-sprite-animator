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

        void CreateLeftClick(Rect sprite, Event evt, int controlId, SpritePartMeshDef mesh)
        {
            bool ctrl = evt.control || evt.command;
            bool cut = evt.shift && !ctrl;
            Vector2 uv = MeshGuiToUv(sprite, evt.mousePosition);
            int n = mesh.VertexCount;
            int pen = (uint)_partsMeshPen < (uint)n ? _partsMeshPen : -1;
            int hit = ctrl && n > 0 ? NearestMeshVertex(mesh, uv, pen) : HitMeshVertex(sprite, mesh, evt.mousePosition);
            int ea = -1, eb = -1;
            bool onEdge = hit < 0 && HitMeshGraphEdge(sprite, mesh, evt.mousePosition, out ea, out eb);

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
                // Pick the vertex: the chain goes on from it, and dragging moves it.
                SelectOnly(hit);
                _partsMeshPen = _partsCreateMode == PartsCreateMode.VertexEdge ? hit : -1;
                BeginMeshVertexDrag(evt, controlId, mesh);
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

        void CreateRightClick(Rect sprite, Event evt, SpritePartMeshDef mesh)
        {
            int n = mesh.VertexCount;
            int hit = HitMeshVertex(sprite, mesh, evt.mousePosition);
            if (hit >= 0 && _partsCreateMode != PartsCreateMode.Edge)
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
            _partsMeshPen = -1;
            _partsWarpSelection.Clear();
            _partsWarpIndex = -1;
            _status = "Chain ended.";
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

        void BeginMeshVertexDrag(Event evt, int controlId, SpritePartMeshDef mesh)
        {
            _partsMeshDrag = true;
            _partsMeshMoved = false;
            _partsMeshDragStart = mesh.Clone();
            _partsDragStartMouse = evt.mousePosition;
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
                || _partsMeshDrag)
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
