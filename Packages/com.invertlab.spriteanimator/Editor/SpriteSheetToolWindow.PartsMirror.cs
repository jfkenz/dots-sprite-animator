using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Mirror (symmetry) for meshes, in image space (0..1, default the image centre):
    //   Horizontal    left / right across a vertical line
    //   Vertical      top / bottom across a horizontal line
    //   Both          four ways: a vertex's left/right, top/bottom and opposite-corner partners all follow
    //   Live Mirror   moving a vertex moves its partners the mirrored way - Warp drags, brushes, FFD, Edit Mesh
    //   Mirror Deform copy one side (or one quarter) of the deform onto the others on the current key
    //   Mirror Copy   (Edit Mesh) duplicate the selected vertices and their edges across the axes
    // Partners are the vertices at each other's mirrored spots on the setup mesh; a vertex on an axis stays on it.
    public sealed partial class SpriteSheetToolWindow
    {
        [SerializeField] bool _partsMirrorLive;
        [SerializeField] float _partsMirrorAxis = 0.5f;
        [SerializeField] float _partsMirrorAxisY = 0.5f;
        [SerializeField] SpritePartsMirrorMode _partsMirrorMode = SpritePartsMirrorMode.Horizontal;
        /// <summary>How close (image fraction) a vertex must sit to another's mirrored spot to be its partner.</summary>
        [SerializeField] float _partsMirrorTolerance = 0.03f;
        static readonly Color PartsMirrorColor = new Color(0.3f, 0.95f, 0.85f, 0.85f);

        bool MirrorUsesX => _partsMirrorMode != SpritePartsMirrorMode.Vertical;
        bool MirrorUsesY => _partsMirrorMode != SpritePartsMirrorMode.Horizontal;

        SpritePartsMeshOps.MirrorMap MirrorMapFor(SpritePartMeshDef mesh)
            => SpritePartsMeshOps.BuildMirrorMap(mesh, _partsMirrorAxis, _partsMirrorAxisY, _partsMirrorMode, _partsMirrorTolerance);

        /// <summary>
        /// True when Live Mirror cannot fully reach vertex <paramref name="i"/>: a mirrored spot has no vertex
        /// (within the tolerance). A vertex on an axis is its own partner there, so it needs fewer.
        /// </summary>
        bool MirrorMissing(SpritePartsMeshOps.MirrorMap map, int i)
        {
            int onAxes = (map.LockX[i] ? 1 : 0) + (map.LockY[i] ? 1 : 0);
            int need = _partsMirrorMode == SpritePartsMirrorMode.Both
                ? (onAxes == 2 ? 0 : onAxes == 1 ? 1 : 3)
                : 1 - onAxes;
            return map.Partners[i].Count < need;
        }

        /// <summary>Red rings on the vertices Live Mirror leaves alone (no vertex at their mirrored spot).</summary>
        void DrawMirrorUnpaired(SpritePartMeshDef mesh, int count, System.Func<int, Vector2> gui)
        {
            if (!_partsMirrorLive || mesh == null || Event.current.type != EventType.Repaint)
                return;
            // Called inside Handles.BeginGUI.
            var map = MirrorMapFor(mesh);
            Handles.color = new Color(1f, 0.3f, 0.3f, 0.95f);
            for (int i = 0; i < map.Count && i < count; i++)
            {
                if (MirrorMissing(map, i))
                    Handles.DrawWireDisc(gui(i), Vector3.forward, 7f);
            }
        }

        SpritePartsMeshOps.MirrorMap MirrorMapFor(string slotId)
            => MirrorMapFor(SpritePartsAuthoringOps.FindSlot(_profile, slotId)?.Mesh);

        /// <summary>One partner per vertex for Paste Mirrored: across the vertical line (the horizontal one in Vertical mode).</summary>
        int[] MirrorPartnersFor(string slotId)
        {
            var mesh = SpritePartsAuthoringOps.FindSlot(_profile, slotId)?.Mesh;
            if (mesh == null || mesh.VertexCount == 0)
                return System.Array.Empty<int>();
            bool vertical = _partsMirrorMode == SpritePartsMirrorMode.Vertical;
            return SpritePartsMeshOps.MirrorPartners(mesh, _partsMirrorAxis, _partsMirrorAxisY, !vertical, vertical, _partsMirrorTolerance);
        }

        /// <summary>An offset pasted through <see cref="MirrorPartnersFor"/>: flipped, or kept on the axis for a vertex that is its own partner.</summary>
        Vector2 MirrorPasteOffset(Vector2 off, bool self)
        {
            bool vertical = _partsMirrorMode == SpritePartsMirrorMode.Vertical;
            if (vertical)
                return new Vector2(off.x, self ? 0f : -off.y);
            return new Vector2(self ? 0f : -off.x, off.y);
        }

        /// <summary>
        /// Live Mirror on a deform: every vertex that moved hands the mirrored move to its partners, unless a partner
        /// moved too. Vertices on an axis only move along it.
        /// </summary>
        void MirrorLatticeChanges(string slotId, in SpritePartsLattice start, ref SpritePartsLattice lattice)
        {
            if (!_partsMirrorLive)
                return;
            var map = MirrorMapFor(slotId);
            int n = Mathf.Min(map.Count, Mathf.Min(start.PointCount, lattice.PointCount));
            var change = new Vector2[n];
            var moved = new bool[n];
            for (int i = 0; i < n; i++)
            {
                float2 d = lattice.GetPoint(i) - start.GetPoint(i);
                change[i] = new Vector2(d.x, d.y);
                moved[i] = change[i].sqrMagnitude > 1e-12f;
            }
            for (int i = 0; i < n; i++)
            {
                if (!moved[i])
                    continue;
                Vector2 c = map.Constrain(i, change[i]);
                lattice.SetPoint(i, start.GetPoint(i) + new float2(c.x, c.y));
                foreach (var (p, flip) in map.Partners[i])
                {
                    if (p >= n || moved[p])
                        continue;
                    Vector2 m = map.Constrain(p, Vector2.Scale(c, flip));
                    lattice.SetPoint(p, start.GetPoint(p) + new float2(m.x, m.y));
                }
            }
        }

        /// <summary>
        /// Copies the deform of the source side onto its mirrors on the current key. <paramref name="fromLeft"/> /
        /// <paramref name="fromTop"/>: which side is the source (null = that axis is not used).
        /// </summary>
        void MirrorPartsDeform(bool? fromLeft, bool? fromTop)
        {
            var slot = CurrentPartsSlot;
            if (slot?.Mesh == null || !slot.Mesh.HasMesh)
            {
                _status = "Mirror Deform needs a part with a mesh.";
                return;
            }
            var map = MirrorMapFor(slot.Mesh);
            var pose = SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);
            var lattice = pose.Lattice;
            int n = Mathf.Min(map.Count, lattice.PointCount);
            bool InSource(int i)
            {
                Vector2 v = slot.Mesh.Vertices[i];
                bool okX = fromLeft == null || (fromLeft.Value ? v.x <= _partsMirrorAxis + 1e-4f : v.x >= _partsMirrorAxis - 1e-4f);
                bool okY = fromTop == null || (fromTop.Value ? v.y >= _partsMirrorAxisY - 1e-4f : v.y <= _partsMirrorAxisY + 1e-4f);
                return okX && okY;
            }
            int written = 0;
            for (int i = 0; i < n; i++)
            {
                if (!InSource(i))
                    continue;
                float2 raw = lattice.GetPoint(i) - lattice.GetRest(i);
                Vector2 off = map.Constrain(i, new Vector2(raw.x, raw.y)); // the axes stay on the axes
                lattice.SetPoint(i, lattice.GetRest(i) + new float2(off.x, off.y));
                foreach (var (p, flip) in map.Partners[i])
                {
                    if (p >= n || InSource(p))
                        continue;
                    Vector2 m = map.Constrain(p, Vector2.Scale(off, flip));
                    lattice.SetPoint(p, lattice.GetRest(p) + new float2(m.x, m.y));
                    written++;
                }
            }
            if (written == 0)
            {
                _status = "No mirror partners: no vertex sits at another's mirrored spot. Check the axes, or use Mirror Copy in Edit Mesh.";
                return;
            }
            string from = MirrorSideName(fromLeft, fromTop);
            pose.Lattice = lattice;
            RecordPartsUndo("Mirror Deform From " + from);
            ApplyPartsPoseEdit(slot.SlotId, pose);
            _status = "Mirrored the " + from.ToLowerInvariant() + " deform onto " + written + " vertices.";
        }

        static string MirrorSideName(bool? left, bool? top)
        {
            string y = top == null ? "" : top.Value ? "Top" : "Bottom";
            string x = left == null ? "" : left.Value ? "Left" : "Right";
            return y.Length > 0 && x.Length > 0 ? y + " " + x : y + x;
        }

        /// <summary>Edit Mesh drag: partners of the dragged vertices follow the mirrored way; vertices on an axis stay on it.</summary>
        void MirrorMeshDrag(SpritePartMeshDef start, List<int> indices, List<Vector2> positions)
        {
            if (!_partsMirrorLive || start == null)
                return;
            var map = MirrorMapFor(start);
            int count = indices.Count;
            for (int k = 0; k < count; k++)
            {
                int i = indices[k];
                if ((uint)i >= (uint)map.Count)
                    continue;
                Vector2 pos = positions[k];
                if (map.LockX[i]) pos.x = _partsMirrorAxis;
                if (map.LockY[i]) pos.y = _partsMirrorAxisY;
                positions[k] = pos;
                foreach (var (p, flip) in map.Partners[i])
                {
                    if (indices.Contains(p))
                        continue;
                    indices.Add(p);
                    positions.Add(SpritePartsMeshOps.MirrorPoint(pos, _partsMirrorAxis, _partsMirrorAxisY, flip.x < 0f, flip.y < 0f));
                }
            }
        }

        // ------------------------------------------------------------------ Live Mirror while creating (Edit Mesh)

        /// <summary>A new vertex close to an axis lands on it, so it is its own mirror instead of getting a twin.</summary>
        Vector2 SnapToMirrorAxes(Vector2 uv)
        {
            if (!_partsMirrorLive)
                return uv;
            float near = _partsMirrorTolerance * 0.5f;
            if (MirrorUsesX && Mathf.Abs(uv.x - _partsMirrorAxis) <= near)
                uv.x = _partsMirrorAxis;
            if (MirrorUsesY && Mathf.Abs(uv.y - _partsMirrorAxisY) <= near)
                uv.y = _partsMirrorAxisY;
            return uv;
        }

        /// <summary>
        /// Live Mirror in Create: the vertices in <paramref name="touched"/> get their mirrored twins (existing ones are
        /// reused) and the edges between them are mirrored too.
        /// </summary>
        void MirrorCreated(SpritePartMeshDef draft, params int[] touched)
        {
            if (!_partsMirrorLive || draft == null)
                return;
            foreach (var (fx, fy) in SpritePartsMeshOps.MirrorFlips(_partsMirrorMode))
                SpritePartsMeshOps.MirrorCopyGraph(draft, touched, _partsMirrorAxis, _partsMirrorAxisY, fx, fy, _partsMirrorTolerance);
        }

        /// <summary>
        /// For each mirror flip, the twin line of a-b, to split later at the mirrored spot. A line that is its own
        /// mirror (it crosses the axis) counts too: its other half gets the mirrored split.
        /// </summary>
        List<(int a, int b, bool fx, bool fy)> MirrorEdgeTwins(SpritePartMeshDef draft, int a, int b)
        {
            var twins = new List<(int, int, bool, bool)>();
            if (!_partsMirrorLive || draft == null)
                return twins;
            foreach (var (fx, fy) in SpritePartsMeshOps.MirrorFlips(_partsMirrorMode))
            {
                var partner = SpritePartsMeshOps.MirrorPartners(draft, _partsMirrorAxis, _partsMirrorAxisY, fx, fy, _partsMirrorTolerance);
                int ta = (uint)a < (uint)partner.Length ? partner[a] : -1;
                int tb = (uint)b < (uint)partner.Length ? partner[b] : -1;
                if (ta < 0 || tb < 0)
                    continue; // no twin line
                if (!SpritePartsMeshOps.GraphEdges(draft, false).Exists(e => (e.x == ta && e.y == tb) || (e.x == tb && e.y == ta)))
                    continue;
                if (!twins.Exists(t => (t.Item1 == ta && t.Item2 == tb) || (t.Item1 == tb && t.Item2 == ta)))
                    twins.Add((ta, tb, fx, fy));
            }
            return twins;
        }

        /// <summary>
        /// Splits each twin line at the mirrored spot of the new vertex. The twin may be the line that was just split
        /// (its own mirror): then the half that holds the mirrored spot is split.
        /// </summary>
        void MirrorSplit(SpritePartMeshDef draft, List<(int a, int b, bool fx, bool fy)> twins, int added)
        {
            if (twins == null || (uint)added >= (uint)draft.VertexCount)
                return;
            Vector2 at = draft.Vertices[added];
            foreach (var (a, b, fx, fy) in twins)
            {
                Vector2 m = SpritePartsMeshOps.MirrorPoint(at, _partsMirrorAxis, _partsMirrorAxisY, fx, fy);
                if (Vector2.Distance(m, at) <= _partsMirrorTolerance)
                    continue; // on the axis: its own mirror
                int bestA = -1, bestB = -1;
                float best = float.MaxValue;
                foreach (var e in SpritePartsMeshOps.GraphEdges(draft, false))
                {
                    bool inLine = (e.x == a || e.x == b || e.x == added) && (e.y == a || e.y == b || e.y == added);
                    if (!inLine)
                        continue;
                    float d = SpritePartsMeshOps.DistanceToSegment(m, draft.Vertices[e.x], draft.Vertices[e.y]);
                    if (d < best)
                    {
                        best = d;
                        bestA = e.x;
                        bestB = e.y;
                    }
                }
                if (bestA >= 0 && best <= _partsMirrorTolerance)
                    SpritePartsMeshOps.TrySplitGraphEdge(draft, bestA, bestB, m, out _);
            }
        }

        /// <summary>The vertex and its mirror partners (Live Mirror on), or just the vertex.</summary>
        List<int> MirrorGroup(SpritePartMeshDef mesh, int vertex)
        {
            var group = new List<int> { vertex };
            if (!_partsMirrorLive || mesh == null)
                return group;
            var map = MirrorMapFor(mesh);
            if ((uint)vertex < (uint)map.Count)
            {
                foreach (var (p, _) in map.Partners[vertex])
                    if (!group.Contains(p))
                        group.Add(p);
            }
            return group;
        }

        /// <summary>Edit Mesh: duplicate the selected vertices and their edges across the axes (three copies in Both).</summary>
        void MirrorCopySelectedMesh()
        {
            var mesh = MeshEditSlot()?.Mesh;
            int n = mesh?.VertexCount ?? 0;
            var sel = ValidSelection(n);
            if (sel.Count == 0)
            {
                _status = "Select vertices to mirror.";
                return;
            }
            var added = new List<int>();
            if (!EditMeshDraft("Mirror Copy", w =>
                {
                    int edgesBefore = w.Edges?.Length ?? 0;
                    foreach (var (fx, fy) in SpritePartsMeshOps.MirrorFlips(_partsMirrorMode))
                        added.AddRange(SpritePartsMeshOps.MirrorCopyGraph(w, sel, _partsMirrorAxis, _partsMirrorAxisY, fx, fy));
                    return added.Count > 0 || (w.Edges?.Length ?? 0) != edgesBefore ? IdentityRemap(n) : null;
                }))
            {
                _status = "Nothing to mirror: those vertices already have partners.";
                return;
            }
            foreach (int a in added)
                if (!_partsWarpSelection.Contains(a))
                    _partsWarpSelection.Add(a);
            _status = "Mirrored " + added.Count + " vertices. Close the loop, then Make Polygons.";
            Repaint();
        }

        /// <summary>The axis lines in Edit Mesh (image space).</summary>
        void DrawMeshMirrorAxis(Rect sprite)
        {
            if (!_partsMirrorLive || Event.current.type != EventType.Repaint)
                return;
            Handles.BeginGUI();
            Handles.color = PartsMirrorColor;
            if (MirrorUsesX)
                Handles.DrawDottedLine(MeshUvToGui(sprite, new Vector2(_partsMirrorAxis, SpritePartsMeshOps.MaxUv)),
                    MeshUvToGui(sprite, new Vector2(_partsMirrorAxis, SpritePartsMeshOps.MinUv)), 5f);
            if (MirrorUsesY)
                Handles.DrawDottedLine(MeshUvToGui(sprite, new Vector2(SpritePartsMeshOps.MinUv, _partsMirrorAxisY)),
                    MeshUvToGui(sprite, new Vector2(SpritePartsMeshOps.MaxUv, _partsMirrorAxisY)), 5f);
            var mesh = MeshEditSlot()?.Mesh;
            if (mesh?.Vertices != null)
                DrawMirrorUnpaired(mesh, mesh.VertexCount, i => MeshUvToGui(sprite, mesh.Vertices[i]));
            Handles.EndGUI();
        }

        /// <summary>The axis lines through the part in Warp.</summary>
        void DrawWarpMirrorAxis(Rect rect, Vector2 joint, float guiDeg, bool flipX, bool flipY)
        {
            if (!_partsMirrorLive || Event.current.type != EventType.Repaint)
                return;
            Handles.color = PartsMirrorColor;
            if (MirrorUsesX)
            {
                float x = _partsMirrorAxis - 0.5f;
                Handles.DrawDottedLine(PartsWarpPointGui(rect, joint, guiDeg, flipX, flipY, new float2(x, 0.65f)),
                    PartsWarpPointGui(rect, joint, guiDeg, flipX, flipY, new float2(x, -0.65f)), 5f);
            }
            if (MirrorUsesY)
            {
                float y = _partsMirrorAxisY - 0.5f;
                Handles.DrawDottedLine(PartsWarpPointGui(rect, joint, guiDeg, flipX, flipY, new float2(-0.65f, y)),
                    PartsWarpPointGui(rect, joint, guiDeg, flipX, flipY, new float2(0.65f, y)), 5f);
            }
        }

        void DrawPanelMirrorSection(SpritePartSlotDef slot)
        {
            GUILayout.Space(6f);
            EditorGUILayout.LabelField("MIRROR", _sectionStyle);
            bool live = EditorGUILayout.ToggleLeft(new GUIContent("Live Mirror",
                "Moving a vertex moves its partners (the vertices at its mirrored spots) the mirrored way: drags, brushes, FFD and Edit Mesh."),
                _partsMirrorLive);
            var mode = (SpritePartsMirrorMode)GUILayout.Toolbar((int)_partsMirrorMode, new[]
            {
                new GUIContent("Horizontal", "Left / right across a vertical line"),
                new GUIContent("Vertical", "Top / bottom across a horizontal line"),
                new GUIContent("Both", "Four ways: left / right, top / bottom and the opposite corner"),
            }, EditorStyles.miniButton);
            float axis = _partsMirrorAxis, axisY = _partsMirrorAxisY;
            if (mode != SpritePartsMirrorMode.Vertical)
            {
                EditorGUILayout.BeginHorizontal();
                axis = EditorGUILayout.Slider(new GUIContent("Axis X", "The vertical mirror line in image space (0.5 = centre)"), axis, 0f, 1f);
                if (GUILayout.Button(new GUIContent("C", "Back to the image centre"), GUILayout.Width(22f)))
                    axis = 0.5f;
                EditorGUILayout.EndHorizontal();
            }
            if (mode != SpritePartsMirrorMode.Horizontal)
            {
                EditorGUILayout.BeginHorizontal();
                axisY = EditorGUILayout.Slider(new GUIContent("Axis Y", "The horizontal mirror line in image space (0.5 = centre)"), axisY, 0f, 1f);
                if (GUILayout.Button(new GUIContent("C", "Back to the image centre"), GUILayout.Width(22f)))
                    axisY = 0.5f;
                EditorGUILayout.EndHorizontal();
            }
            float tolerance = EditorGUILayout.Slider(new GUIContent("Match Within",
                "How close a vertex must sit to another's mirrored spot to count as its partner (fraction of the image). "
                + "Raise it for a mesh that is only roughly symmetric."), _partsMirrorTolerance * 100f, 0.5f, 15f) / 100f;
            if (!Mathf.Approximately(tolerance, _partsMirrorTolerance))
            {
                _partsMirrorTolerance = tolerance;
                Repaint();
            }
            if (live != _partsMirrorLive || mode != _partsMirrorMode
                || !Mathf.Approximately(axis, _partsMirrorAxis) || !Mathf.Approximately(axisY, _partsMirrorAxisY))
            {
                _partsMirrorLive = live;
                _partsMirrorMode = mode;
                _partsMirrorAxis = axis;
                _partsMirrorAxisY = axisY;
                Repaint();
            }
            var mesh = slot?.Mesh;
            if (mesh != null && mesh.VertexCount > 0)
            {
                var map = MirrorMapFor(mesh);
                int full = 0, some = 0, none = 0;
                for (int i = 0; i < map.Count; i++)
                {
                    if (!MirrorMissing(map, i)) full++;
                    else if (map.Partners[i].Count > 0) some++;
                    else none++;
                }
                EditorGUILayout.LabelField(full + " vertices mirrored, " + (some > 0 ? some + " partly, " : "") + none + " without a partner"
                    + (some + none > 0 ? " (red rings: Live Mirror leaves them alone; move them onto the mirrored spot, raise Match Within, or Mirror Copy)." : "."),
                    EditorStyles.wordWrappedMiniLabel);
            }
            if (IsPartsMeshEdit())
            {
                using (new EditorGUI.DisabledScope(_partsWarpSelection.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent("Mirror Copy Selected", "Duplicate the selected vertices and their edges across the axes")))
                        MirrorCopySelectedMesh();
                }
                return;
            }
            using (new EditorGUI.DisabledScope(mesh == null || !mesh.HasMesh))
            {
                EditorGUILayout.BeginHorizontal();
                switch (_partsMirrorMode)
                {
                    case SpritePartsMirrorMode.Horizontal:
                        if (GUILayout.Button(new GUIContent("Deform L → R", "Copy the left side's deform onto the right on this key")))
                            MirrorPartsDeform(true, null);
                        if (GUILayout.Button(new GUIContent("R → L", "Copy the right side's deform onto the left on this key")))
                            MirrorPartsDeform(false, null);
                        break;
                    case SpritePartsMirrorMode.Vertical:
                        if (GUILayout.Button(new GUIContent("Deform T → B", "Copy the top half's deform onto the bottom on this key")))
                            MirrorPartsDeform(null, true);
                        if (GUILayout.Button(new GUIContent("B → T", "Copy the bottom half's deform onto the top on this key")))
                            MirrorPartsDeform(null, false);
                        break;
                    default:
                        GUILayout.Label(new GUIContent("Deform from", "Copy one quarter's deform onto the other three on this key"), GUILayout.Width(74f));
                        if (GUILayout.Button(new GUIContent("TL", "Top left to the other quarters"))) MirrorPartsDeform(true, true);
                        if (GUILayout.Button(new GUIContent("TR", "Top right to the other quarters"))) MirrorPartsDeform(false, true);
                        if (GUILayout.Button(new GUIContent("BL", "Bottom left to the other quarters"))) MirrorPartsDeform(true, false);
                        if (GUILayout.Button(new GUIContent("BR", "Bottom right to the other quarters"))) MirrorPartsDeform(false, false);
                        break;
                }
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}
