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
        /// <summary>On: a pen click joins the new vertex to the previous one. Off: free vertices.</summary>
        [SerializeField] bool _partsMeshAutoConnect = true;

        string MeshFullMessage()
            => "Mesh is full (" + SpritePartsMeshOps.MaxVertices + " vertices). Delete or merge some vertices first.";

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
                if (!_partsMeshAutoConnect)
                {
                    PlaceFreePoint(slot, mesh, uv);
                    return;
                }
                var pending = mesh.Clone();
                if (!SpritePartsMeshOps.TryAppendHullVertex(pending, uv, out int added))
                {
                    _status = MeshFullMessage();
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

            // Auto Connect off: every click is a free vertex (Ctrl / Shift still draw from the pen).
            int pen = (uint)_partsMeshPen < (uint)mesh.VertexCount && (_partsMeshAutoConnect || snap || cut)
                ? _partsMeshPen
                : -1;
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
                    _status = work.VertexCount >= SpritePartsMeshOps.MaxVertices
                        ? MeshFullMessage()
                        : "A vertex there would make the hull cross itself.";
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
                _status = _partsMeshAutoConnect
                    ? what + ". Next click draws an edge from it."
                    : what + " (free, Auto Connect off).";
                return;
            }

            int from = pen;
            if (cut)
                from = CutAcross(work, from, ref target, ref total);
            if (from != target && !SpritePartsMeshOps.IsHullEdge(work, from, target)
                && !SpritePartsMeshOps.TryAddEdge(work, from, target))
                _status = "Could not add that edge.";
            else
                _status = (cut ? "Cut to " : "Edge to ") + target;
            CommitPen(slot, mesh, work, total, cut ? "Pen Cut" : "Pen Edge");
            _partsMeshPen = target;
            SelectOnly(target);
        }

        /// <summary>Auto Connect off, no mesh yet: free points; the outermost ones become the outline.</summary>
        void PlaceFreePoint(SpritePartSlotDef slot, SpritePartMeshDef mesh, Vector2 uv)
        {
            if (mesh.VertexCount >= SpritePartsMeshOps.MaxVertices)
            {
                _status = MeshFullMessage();
                return;
            }
            var points = new List<Vector2>(mesh.Vertices ?? System.Array.Empty<Vector2>()) { uv };
            var next = SpritePartsMeshOps.FromPoints(points, out var remap);
            RecordPartsUndo("Place Mesh Point");
            slot.Mesh = next;
            _partsMeshPen = -1;
            SelectOnly(remap[points.Count - 1]);
            SaveDirty();
            _status = next.HasMesh
                ? "Mesh around " + next.VertexCount + " points (" + next.HullCount + " outline). Keep clicking to add more."
                : points.Count + " free points. The mesh appears at 3.";
        }

        /// <summary>
        /// Cut: every outline (hull) or user edge crossed by from-&gt;to gains a vertex at the crossing,
        /// and the chain runs through them. Splitting a hull edge shifts indices, so the next
        /// crossing is searched again on the updated mesh each time.
        /// </summary>
        int CutAcross(SpritePartMeshDef work, int from, ref int to, ref int[] total)
        {
            int prev = from;
            for (int guard = 0; guard < SpritePartsMeshOps.MaxVertices; guard++)
            {
                if (!TryFirstCrossing(work, prev, to, out int ea, out int eb, out var p))
                    break;
                if (work.VertexCount >= SpritePartsMeshOps.MaxVertices)
                {
                    _status = MeshFullMessage() + " The cut stopped early.";
                    break;
                }
                // Hull and user edges are split (keeping them as edges); a triangle line just gets a vertex.
                bool stored = SpritePartsMeshOps.IsHullEdge(work, ea, eb) || SpritePartsMeshOps.HasEdge(work, ea, eb);
                int x;
                int[] step;
                bool ok = stored
                    ? SpritePartsMeshOps.TrySplitEdgeAt(work, ea, eb, p, out x, out step)
                    : SpritePartsMeshOps.TryAddInteriorVertex(work, p, out x, out step);
                if (!ok)
                    break;
                total = ComposeRemap(total, step);
                if (step != null)
                {
                    prev = step[prev];
                    to = step[to];
                }
                SpritePartsMeshOps.TryAddEdge(work, prev, x);
                prev = x;
            }
            return prev;
        }

        /// <summary>Nearest drawn line (hull, user edge or triangle side) crossed by from-&gt;to, skipping lines that touch either end.</summary>
        static bool TryFirstCrossing(SpritePartMeshDef mesh, int from, int to, out int ea, out int eb, out Vector2 point)
        {
            ea = eb = -1;
            point = default;
            float best = float.MaxValue;
            foreach (var (a, b) in CuttableEdges(mesh))
            {
                if (a == from || b == from || a == to || b == to)
                    continue;
                if (!SpritePartsMeshOps.TrySegmentHit(mesh.Vertices[from], mesh.Vertices[to], mesh.Vertices[a], mesh.Vertices[b], out float t, out var p)
                    || t >= best)
                    continue;
                best = t;
                ea = a;
                eb = b;
                point = p;
            }
            return ea >= 0;
        }

        static IEnumerable<(int a, int b)> CuttableEdges(SpritePartMeshDef mesh)
        {
            int n = mesh?.VertexCount ?? 0;
            int h = Mathf.Min(mesh?.HullCount ?? 0, n);
            for (int i = 0; h >= 3 && i < h; i++)
                yield return (i, (i + 1) % h);
            for (int i = 0; mesh?.Edges != null && i + 1 < mesh.Edges.Length; i += 2)
            {
                if ((uint)mesh.Edges[i] < (uint)n && (uint)mesh.Edges[i + 1] < (uint)n)
                    yield return (mesh.Edges[i], mesh.Edges[i + 1]);
            }
            // Triangle sides (each interior side appears twice; the duplicate just finds the same crossing).
            for (int t = 0; mesh?.Triangles != null && t + 2 < mesh.Triangles.Length; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = mesh.Triangles[t + e];
                    int b = mesh.Triangles[t + (e + 1) % 3];
                    if (a < b && (uint)b < (uint)n)
                        yield return (a, b);
                }
            }
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
                if (n == 0 || !_partsMeshAutoConnect)
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
            if (!_partsMeshAutoConnect && !ctrl && !cut)
                return; // free vertices: no rubber band
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
            if (!cut)
                return;
            foreach (var (ea, eb) in CuttableEdges(mesh))
            {
                if (ea == _partsMeshPen || eb == _partsMeshPen)
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
