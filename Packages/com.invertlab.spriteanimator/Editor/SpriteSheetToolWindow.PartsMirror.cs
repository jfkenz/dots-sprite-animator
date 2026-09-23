using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Mirror (symmetry) for meshes: a vertical axis in image space (0..1, default the image centre).
    //   Live Mirror   moving a vertex moves its partner the mirrored way - Warp drags, brushes, FFD, Edit Mesh
    //   Mirror Deform copy the deform of one side onto the other on the current key
    //   Mirror Copy   (Edit Mesh) duplicate the selected vertices and their edges across the axis
    // Partners are the vertices at each other's mirrored spot on the setup mesh.
    public sealed partial class SpriteSheetToolWindow
    {
        [SerializeField] bool _partsMirrorLive;
        [SerializeField] float _partsMirrorAxis = 0.5f;
        static readonly Color PartsMirrorColor = new Color(0.3f, 0.95f, 0.85f, 0.85f);

        int[] MirrorPartnersFor(string slotId)
        {
            var mesh = SpritePartsAuthoringOps.FindSlot(_profile, slotId)?.Mesh;
            return mesh != null && mesh.VertexCount > 0 ? SpritePartsMeshOps.MirrorPartners(mesh, _partsMirrorAxis) : System.Array.Empty<int>();
        }

        /// <summary>
        /// Live Mirror on a deform: every vertex that moved hands the mirrored move to its partner (x flipped),
        /// unless the partner moved too. Vertices on the axis only move up / down.
        /// </summary>
        void MirrorLatticeChanges(string slotId, in SpritePartsLattice start, ref SpritePartsLattice lattice)
        {
            if (!_partsMirrorLive)
                return;
            var partner = MirrorPartnersFor(slotId);
            int n = Mathf.Min(partner.Length, Mathf.Min(start.PointCount, lattice.PointCount));
            var change = new float2[n];
            for (int i = 0; i < n; i++)
                change[i] = lattice.GetPoint(i) - start.GetPoint(i);
            for (int i = 0; i < n; i++)
            {
                if (math.lengthsq(change[i]) < 1e-12f)
                    continue;
                int p = partner[i];
                if (p == i)
                    lattice.SetPoint(i, start.GetPoint(i) + new float2(0f, change[i].y));
                else if (p >= 0 && p < n && math.lengthsq(change[p]) < 1e-12f)
                    lattice.SetPoint(p, start.GetPoint(p) + new float2(-change[i].x, change[i].y));
            }
        }

        /// <summary>Copies the deform of one side onto the other on the current key (<paramref name="leftToRight"/>: left wins).</summary>
        void MirrorPartsDeform(bool leftToRight)
        {
            var slot = CurrentPartsSlot;
            if (slot?.Mesh == null || !slot.Mesh.HasMesh)
            {
                _status = "Mirror Deform needs a part with a mesh.";
                return;
            }
            var partner = MirrorPartnersFor(slot.SlotId);
            var pose = SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);
            var lattice = pose.Lattice;
            int n = Mathf.Min(partner.Length, lattice.PointCount);
            int pairs = 0;
            for (int i = 0; i < n; i++)
            {
                int p = partner[i];
                if (p < 0)
                    continue;
                float2 off = lattice.GetPoint(i) - lattice.GetRest(i);
                if (p == i)
                {
                    lattice.SetPoint(i, lattice.GetRest(i) + new float2(0f, off.y)); // the axis stays on the axis
                    continue;
                }
                bool left = slot.Mesh.Vertices[i].x < _partsMirrorAxis;
                if (left != leftToRight)
                    continue; // i is on the side that gets written
                lattice.SetPoint(p, lattice.GetRest(p) + new float2(-off.x, off.y));
                pairs++;
            }
            if (pairs == 0)
            {
                _status = "No mirror pairs: no vertex sits at another's mirrored spot. Check the axis, or use Mirror Copy in Edit Mesh.";
                return;
            }
            pose.Lattice = lattice;
            RecordPartsUndo(leftToRight ? "Mirror Deform Left To Right" : "Mirror Deform Right To Left");
            ApplyPartsPoseEdit(slot.SlotId, pose);
            _status = "Mirrored " + pairs + " vertex pairs " + (leftToRight ? "left → right." : "right → left.");
        }

        /// <summary>Edit Mesh drag: partners of the dragged vertices follow the mirrored way.</summary>
        void MirrorMeshDrag(SpritePartMeshDef start, List<int> indices, List<Vector2> positions)
        {
            if (!_partsMirrorLive || start == null)
                return;
            var partner = SpritePartsMeshOps.MirrorPartners(start, _partsMirrorAxis);
            int count = indices.Count;
            for (int k = 0; k < count; k++)
            {
                int i = indices[k];
                int p = (uint)i < (uint)partner.Length ? partner[i] : -1;
                if (p == i)
                    positions[k] = new Vector2(_partsMirrorAxis, positions[k].y);
                else if (p >= 0 && !indices.Contains(p))
                {
                    indices.Add(p);
                    positions.Add(SpritePartsMeshOps.MirrorPoint(positions[k], _partsMirrorAxis));
                }
            }
        }

        /// <summary>Edit Mesh: duplicate the selected vertices and their edges across the axis.</summary>
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
            List<int> added = null;
            if (!EditMeshDraft("Mirror Copy", w =>
                {
                    added = SpritePartsMeshOps.MirrorCopyGraph(w, sel, _partsMirrorAxis);
                    return added.Count > 0 || w.Edges.Length != (mesh.Edges?.Length ?? 0) ? IdentityRemap(n) : null;
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

        /// <summary>The axis line in Edit Mesh (image space).</summary>
        void DrawMeshMirrorAxis(Rect sprite)
        {
            if (!_partsMirrorLive || Event.current.type != EventType.Repaint)
                return;
            Handles.BeginGUI();
            Handles.color = PartsMirrorColor;
            Handles.DrawDottedLine(MeshUvToGui(sprite, new Vector2(_partsMirrorAxis, SpritePartsMeshOps.MaxUv)),
                MeshUvToGui(sprite, new Vector2(_partsMirrorAxis, SpritePartsMeshOps.MinUv)), 5f);
            Handles.EndGUI();
        }

        /// <summary>The axis line through the part in Warp.</summary>
        void DrawWarpMirrorAxis(Rect rect, Vector2 joint, float guiDeg, bool flipX, bool flipY)
        {
            if (!_partsMirrorLive || Event.current.type != EventType.Repaint)
                return;
            float x = _partsMirrorAxis - 0.5f;
            Handles.color = PartsMirrorColor;
            Handles.DrawDottedLine(PartsWarpPointGui(rect, joint, guiDeg, flipX, flipY, new float2(x, 0.65f)),
                PartsWarpPointGui(rect, joint, guiDeg, flipX, flipY, new float2(x, -0.65f)), 5f);
        }

        void DrawPanelMirrorSection(SpritePartSlotDef slot)
        {
            GUILayout.Space(6f);
            EditorGUILayout.LabelField("MIRROR", _sectionStyle);
            bool live = EditorGUILayout.ToggleLeft(new GUIContent("Live Mirror",
                "Moving a vertex moves its partner (the vertex at its mirrored spot) the mirrored way: drags, brushes, FFD and Edit Mesh."),
                _partsMirrorLive);
            EditorGUILayout.BeginHorizontal();
            float axis = EditorGUILayout.Slider(new GUIContent("Axis", "Vertical mirror line in image space (0.5 = centre)"), _partsMirrorAxis, 0f, 1f);
            if (GUILayout.Button(new GUIContent("C", "Back to the image centre"), GUILayout.Width(22f)))
                axis = 0.5f;
            EditorGUILayout.EndHorizontal();
            if (live != _partsMirrorLive || !Mathf.Approximately(axis, _partsMirrorAxis))
            {
                _partsMirrorLive = live;
                _partsMirrorAxis = axis;
                Repaint();
            }
            var mesh = slot?.Mesh;
            if (mesh != null && mesh.VertexCount > 0)
            {
                var partner = SpritePartsMeshOps.MirrorPartners(mesh, _partsMirrorAxis);
                int pairs = 0, lone = 0;
                for (int i = 0; i < partner.Length; i++)
                {
                    if (partner[i] > i)
                        pairs++;
                    else if (partner[i] < 0)
                        lone++;
                }
                EditorGUILayout.LabelField(pairs + " pairs, " + lone + " without a partner.", EditorStyles.wordWrappedMiniLabel);
            }
            if (IsPartsMeshEdit())
            {
                using (new EditorGUI.DisabledScope(_partsWarpSelection.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent("Mirror Copy Selected", "Duplicate the selected vertices and their edges across the axis")))
                        MirrorCopySelectedMesh();
                }
                return;
            }
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(mesh == null || !mesh.HasMesh))
            {
                if (GUILayout.Button(new GUIContent("Deform L → R", "Copy the left side's deform onto the right on this key")))
                    MirrorPartsDeform(true);
                if (GUILayout.Button(new GUIContent("R → L", "Copy the right side's deform onto the left on this key")))
                    MirrorPartsDeform(false);
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
