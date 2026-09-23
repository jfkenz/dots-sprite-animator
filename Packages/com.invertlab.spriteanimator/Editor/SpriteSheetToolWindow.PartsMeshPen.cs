using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Create tool = the pen from the old lattice editor, on top of the Spine mesh data:
    //   click        add a point, joined by an edge to the previous one (the pen)
    //   click point  connect the pen to it; with no pen, start the chain there
    //   first point  closes the outline while the hull is still being drawn
    //   Shift        cut: every user edge the stroke crosses gains a vertex
    //   Ctrl         snap: connect to the nearest vertex
    //   Enter        end the chain
    // Outside the hull a click adds a hull vertex, inside it an interior vertex.
    public sealed partial class SpriteSheetToolWindow
    {
        int _partsMeshPen = -1;

        void EndMeshPen()
        {
            _partsMeshPen = -1;
            _status = "Pen ended. Click to start a new chain.";
            Repaint();
        }

        static int[] ComposeRemap(int[] total, int[] step)
        {
            if (step == null)
                return total;
            var result = new int[total.Length];
            for (int i = 0; i < total.Length; i++)
                result[i] = total[i] >= 0 && total[i] < step.Length ? step[total[i]] : -1;
            return result;
        }

        static int[] IdentityRemap(int n)
        {
            var map = new int[n];
            for (int i = 0; i < n; i++)
                map[i] = i;
            return map;
        }

        /// <summary>One pen click in Create. <paramref name="hit"/> = vertex under the mouse or -1.</summary>
        void PenClick(Rect sprite, Vector2 mouse, int hit, bool cut, bool snap)
        {
            var slot = MeshEditSlot();
            if (slot == null)
                return;
            var mesh = slot.Mesh ??= new SpritePartMeshDef();
            Vector2 uv = MeshGuiToUv(sprite, mouse);

            // Outline still open: points go around the image in click order.
            if (!mesh.HasMesh)
            {
                if (hit == 0 && mesh.VertexCount >= 3)
                {
                    _partsMeshPen = -1;
                    _status = "Outline closed. Click inside for interior vertices.";
                    return;
                }
                if (hit >= 0)
                {
                    SelectWarpVertex(hit, false);
                    return;
                }
                var pending = mesh.Clone();
                if (!SpritePartsMeshOps.TryAppendHullVertex(pending, uv, out int added))
                {
                    _status = "Mesh is full (" + SpritePartsMeshOps.MaxVertices + " vertices).";
                    return;
                }
                RecordPartsUndo("Pen Hull Point");
                slot.Mesh = pending;
                _partsMeshPen = added;
                SelectOnly(added);
                SaveDirty();
                _status = pending.HasMesh
                    ? "Hull point " + added + ". Click the first point to close, or keep going."
                    : "Hull point " + added + ". Keep clicking around the image.";
                return;
            }

            int pen = (uint)_partsMeshPen < (uint)mesh.VertexCount ? _partsMeshPen : -1;
            if (snap && pen >= 0)
                hit = NearestMeshVertex(mesh, uv, pen);

            var work = mesh.Clone();
            int[] total = IdentityRemap(mesh.VertexCount);
            int target = hit;
            string what;
            if (target < 0)
            {
                if (!TryPlaceMeshVertex(work, sprite, uv, out target, out var placed, out what))
                {
                    _status = "A vertex there would make the hull cross itself.";
                    return;
                }
                total = ComposeRemap(total, placed);
                if (pen >= 0)
                    pen = placed != null ? placed[pen] : pen;
            }
            else
                what = "Vertex " + target;

            if (pen < 0 || pen == target)
            {
                if (hit >= 0 && target == hit)
                {
                    // Clicking a point with no pen starts the chain there.
                    _partsMeshPen = target;
                    SelectOnly(target);
                    _status = "Pen at vertex " + target + ". Click to draw from here.";
                    return;
                }
                CommitPen(slot, mesh, work, total, "Pen Point");
                _partsMeshPen = target;
                SelectOnly(target);
                _status = what + ". Next click draws an edge from it.";
                return;
            }

            int from = pen;
            if (cut)
                from = CutAcross(work, from, target, ref total);
            if (from != target && !SpritePartsMeshOps.IsHullEdge(work, from, target)
                && !SpritePartsMeshOps.TryAddEdge(work, from, target))
                _status = "Could not add that edge.";
            else
                _status = (cut ? "Cut to " : "Edge to ") + target;
            CommitPen(slot, mesh, work, total, cut ? "Pen Cut" : "Pen Edge");
            _partsMeshPen = target;
            SelectOnly(target);
        }

        /// <summary>Splits every user edge crossed by from-to and chains edges through the new vertices.</summary>
        int CutAcross(SpritePartMeshDef work, int from, int to, ref int[] total)
        {
            Vector2 a = work.Vertices[from];
            Vector2 b = work.Vertices[to];
            var hits = new List<(float t, int ea, int eb, Vector2 p)>();
            for (int i = 0; work.Edges != null && i + 1 < work.Edges.Length; i += 2)
            {
                int ea = work.Edges[i], eb = work.Edges[i + 1];
                if (ea == from || eb == from || ea == to || eb == to)
                    continue;
                if (SpritePartsMeshOps.TrySegmentHit(a, b, work.Vertices[ea], work.Vertices[eb], out float t, out var p))
                    hits.Add((t, ea, eb, p));
            }
            hits.Sort((x, y) => x.t.CompareTo(y.t));
            int prev = from;
            foreach (var h in hits)
            {
                // Interior splits append the new vertex, so earlier indices stay valid.
                if (!SpritePartsMeshOps.TrySplitEdgeAt(work, h.ea, h.eb, h.p, out int x, out var step))
                    continue;
                total = ComposeRemap(total, step);
                SpritePartsMeshOps.TryAddEdge(work, prev, x);
                prev = x;
            }
            return prev;
        }

        /// <summary>Hull edge snap, interior vertex, or a new hull vertex outside the outline.</summary>
        bool TryPlaceMeshVertex(SpritePartMeshDef work, Rect sprite, Vector2 uv, out int index, out int[] remap, out string what)
        {
            index = -1;
            remap = null;
            what = "Vertex";
            if (work.VertexCount >= SpritePartsMeshOps.MaxVertices)
                return false;
            float px = 1f / Mathf.Max(1f, Mathf.Min(sprite.width, sprite.height));
            int edge = SpritePartsMeshOps.NearestHullEdge(work, uv, out float distance);
            if (edge >= 0 && distance <= PartsMeshEdgeSnap * px)
            {
                int next = (edge + 1) % work.HullCount;
                what = "Hull vertex";
                return SpritePartsMeshOps.TrySplitEdgeAt(work, edge, next, uv, out index, out remap);
            }
            if (SpritePartsMeshOps.IsInsideHull(work, uv))
            {
                what = "Interior vertex";
                return SpritePartsMeshOps.TryAddInteriorVertex(work, uv, out index, out remap);
            }
            what = "Hull vertex";
            return SpritePartsMeshOps.TryInsertHullVertexAuto(work, uv, out index, out remap);
        }

        void CommitPen(SpritePartSlotDef slot, SpritePartMeshDef before, SpritePartMeshDef after, int[] total, string label)
        {
            RecordPartsUndo(label);
            slot.Mesh = after;
            if (before.HasMesh && after.VertexCount != before.VertexCount)
                SpritePartsAuthoringOps.RemapSlotDeforms(_profile, slot.SlotId, total, after.VertexCount);
            SaveDirty();
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

        /// <summary>Rubber band from the pen to the mouse, with cut markers while Shift is held.</summary>
        void DrawMeshPenPreview(Rect sprite, SpritePartMeshDef mesh)
        {
            if (_partsMeshTool != PartsMeshTool.Create || mesh?.Vertices == null || Event.current.type != EventType.Repaint)
                return;
            Vector2 mouseUv = _partsMeshMouseUv;
            if (mouseUv.x < -0.2f || mouseUv.x > 1.2f || mouseUv.y < -0.2f || mouseUv.y > 1.2f)
                return;
            int n = mesh.VertexCount;
            if (!mesh.HasMesh)
            {
                if (n == 0)
                    return;
                var faint = new Color(1f, 0.72f, 0.25f, 0.8f);
                DrawGuiLine(MeshUvToGui(sprite, mesh.Vertices[n - 1]), MeshUvToGui(sprite, mouseUv), faint, 1.5f);
                if (n >= 2)
                    DrawGuiLine(MeshUvToGui(sprite, mouseUv), MeshUvToGui(sprite, mesh.Vertices[0]), new Color(1f, 0.72f, 0.25f, 0.3f), 1f);
                return;
            }
            if ((uint)_partsMeshPen >= (uint)n)
                return;
            bool ctrl = Event.current.control || Event.current.command;
            bool cut = Event.current.shift && !ctrl;
            Vector2 target = mouseUv;
            if (ctrl)
            {
                int near = NearestMeshVertex(mesh, mouseUv, _partsMeshPen);
                if (near >= 0)
                    target = mesh.Vertices[near];
            }
            Vector2 from = mesh.Vertices[_partsMeshPen];
            var line = ctrl ? new Color(0.35f, 0.95f, 1f, 0.95f) : new Color(1f, 0.72f, 0.25f, 0.9f);
            DrawGuiLine(MeshUvToGui(sprite, from), MeshUvToGui(sprite, target), line, 1.5f);
            if (!cut || mesh.Edges == null)
                return;
            for (int i = 0; i + 1 < mesh.Edges.Length; i += 2)
            {
                int ea = mesh.Edges[i], eb = mesh.Edges[i + 1];
                if (ea == _partsMeshPen || eb == _partsMeshPen || (uint)ea >= (uint)n || (uint)eb >= (uint)n)
                    continue;
                if (!SpritePartsMeshOps.TrySegmentHit(from, target, mesh.Vertices[ea], mesh.Vertices[eb], out _, out var p))
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
