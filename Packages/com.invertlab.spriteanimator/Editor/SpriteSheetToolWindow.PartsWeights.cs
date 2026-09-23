using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Spine weights (esotericsoftware.com/spine-weights) for Parts meshes: bind a mesh to part
    // joints ("bones"), compute automatic weights, then fix them with the Add / Remove brush,
    // Direct values, Smooth and Prune. Painting happens in Edit Mesh with the 4 Weights tool.
    public sealed partial class SpriteSheetToolWindow
    {
        static readonly Color[] PartsBoneColors =
        {
            new Color(1f, 0.35f, 0.3f), new Color(0.3f, 0.75f, 1f), new Color(0.45f, 0.95f, 0.4f),
            new Color(1f, 0.8f, 0.25f), new Color(0.8f, 0.45f, 1f), new Color(1f, 0.55f, 0.85f),
            new Color(0.35f, 1f, 0.9f), new Color(0.95f, 0.6f, 0.3f),
        };

        int _partsWeightBone;
        [SerializeField] float _partsWeightStrength = 0.2f;
        [SerializeField] float _partsWeightPruneAt = 0.05f;
        bool _partsWeightPainting;
        bool _partsWarpSkinReady;
        SpritePartsLattice _partsWarpShownStart;
        float2x2[] _partsWarpSkinInverse;
        float2 _partsWarpSkinSize;

        struct PartsBoneGuide
        {
            public string Id;
            public string Name;
            public float2 RootStart;
            public float2 RootEnd;
        }

        static Color BoneColor(int bone) => PartsBoneColors[((bone % PartsBoneColors.Length) + PartsBoneColors.Length) % PartsBoneColors.Length];

        // ------------------------------------------------------------------ skinned sampling for Warp

        /// <summary>The mesh as drawn at the playhead (deform keys, then weights).</summary>
        SpritePartsLattice ShownLattice(string slotId)
            => TrySampleWarpLattices(slotId, out _, out var shown, out _, out _)
                ? shown
                : SampleLocalPoseForSlot(slotId, _partsPreviewTime).Lattice;

        /// <summary>
        /// Pre-skin lattice (what keys store), the drawn lattice, and per-vertex inverse of the bone blend
        /// (null when the mesh is not weighted). <paramref name="size"/> is the bind quad size.
        /// </summary>
        bool TrySampleWarpLattices(
            string slotId, out SpritePartsLattice pre, out SpritePartsLattice shown, out float2x2[] inverse, out float2 size)
        {
            pre = default;
            shown = default;
            inverse = null;
            size = float2.zero;
            if (string.IsNullOrEmpty(slotId) || !SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(),
                    _partsPreviewTime, Allocator.Temp, out var blob, out var poses, out var matrices, out _))
                return false;
            try
            {
                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                int idx = BlobSlotIndex(ref blob.Value, slotId);
                if (idx < 0 || idx >= poses.Length)
                    return false;
                pre = poses[idx].Lattice;
                shown = pre;
                ref var slot = ref blob.Value.Slots[idx];
                if (!SpritePartsSkinning.IsSkinned(ref slot) || pre.PointCount != slot.Mesh.PointCount)
                    return true;
                SpritePartsSkinning.Apply(ref blob.Value, idx, matrices, ref shown);
                size = slot.SkinQuadSize;
                inverse = new float2x2[pre.PointCount];
                for (int v = 0; v < inverse.Length; v++)
                {
                    var m = SpritePartsSkinning.VertexLinear(ref blob.Value, idx, matrices, v);
                    inverse[v] = math.abs(math.determinant(m)) > 1e-8f ? math.inverse(m) : float2x2.identity;
                }
                return true;
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        // ------------------------------------------------------------------ bind space

        /// <summary>
        /// Rest (setup) pose of the mesh part and of every joint, in character-root space.
        /// Bone guides run from a joint to its first child joint.
        /// </summary>
        bool TryGetBindSpace(SpritePartSlotDef slot, out float4x4 meshRest, out float2 size, out float2 pivot,
            out List<PartsBoneGuide> guides)
        {
            meshRest = float4x4.identity;
            guides = new List<PartsBoneGuide>();
            if (!SpritePartsSkinning.TryResolveQuad(_profile, slot, out size, out pivot))
                return false;
            if (!SpritePartsOnion.TrySampleCharacter(_profile, -1, 0f, Allocator.Temp,
                    out var blob, out var poses, out var matrices, out _))
                return false;
            try
            {
                int n = blob.Value.Slots.Length;
                int self = BlobSlotIndex(ref blob.Value, slot.SlotId);
                if (self < 0)
                    return false;
                meshRest = matrices[self];
                for (int j = 0; j < n; j++)
                {
                    float2 start = matrices[j].c3.xy;
                    float2 end = start;
                    var def = SpritePartsAuthoringOps.FindSlot(_profile, blob.Value.Slots[j].SlotId.ToString());
                    if (def != null && def.IsBone)
                    {
                        // A bone runs along its own +X axis for its length.
                        end = math.mul(matrices[j], new float4(def.BoneLength > 1e-4f ? def.BoneLength : 1f, 0f, 0f, 1f)).xy;
                    }
                    else
                    {
                        for (int k = 0; k < n; k++)
                        {
                            if (blob.Value.Slots[k].ParentSlotIndex == j)
                            {
                                end = matrices[k].c3.xy;
                                break;
                            }
                        }
                    }
                    guides.Add(new PartsBoneGuide
                    {
                        Id = SpritePartIdUtility.Canonical(blob.Value.Slots[j].SlotId.ToString()),
                        Name = blob.Value.Slots[j].Name.ToString(),
                        RootStart = start,
                        RootEnd = end,
                    });
                }
                return true;
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        static Vector2 RootToMeshUv(float4x4 meshRest, float2 size, float2 pivot, float2 root)
        {
            float2 local = math.mul(math.inverse(meshRest), new float4(root, 0f, 1f)).xy;
            float2 uv = local / size + pivot;
            return new Vector2(uv.x, uv.y);
        }

        static float2 MeshUvToRoot(float4x4 meshRest, float2 size, float2 pivot, Vector2 uv)
        {
            float2 local = (new float2(uv.x, uv.y) - pivot) * size;
            return math.mul(meshRest, new float4(local, 0f, 1f)).xy;
        }

        // ------------------------------------------------------------------ bind / auto / tools

        SpritePartSlotDef WeightsSlot() => IsPartsMeshEdit() ? MeshEditSlot() : CurrentPartsSlot;

        bool RecomputeAutoWeights(SpritePartSlotDef slot)
        {
            var mesh = slot?.Mesh;
            if (mesh == null || !mesh.HasMesh || mesh.BoneCount == 0)
                return false;
            if (!TryGetBindSpace(slot, out var meshRest, out var size, out var pivot, out var guides))
            {
                _status = "Weights need the part's default appearance (its size and pivot).";
                return false;
            }
            var verts = new List<float2>(mesh.VertexCount);
            foreach (var uv in mesh.Vertices)
                verts.Add(MeshUvToRoot(meshRest, size, pivot, uv));
            var starts = new List<float2>();
            var ends = new List<float2>();
            foreach (string id in mesh.Bones)
            {
                var guide = guides.Find(g => g.Id == SpritePartIdUtility.Canonical(id));
                starts.Add(guide.RootStart);
                ends.Add(guide.RootEnd);
            }
            mesh.Weights = SpritePartsSkinning.AutoWeights(verts, starts, ends);
            return true;
        }

        void BindWeightBone(string boneId) => BindWeightBones(new List<string> { boneId }, "Bind Mesh Bone");

        /// <summary>Binds several joints at once (existing binds stay), then recomputes automatic weights.</summary>
        void BindWeightBones(List<string> boneIds, string undoName)
        {
            var slot = WeightsSlot();
            var mesh = slot?.Mesh;
            if (mesh == null || !mesh.HasMesh)
            {
                _status = "Make a mesh first (Edit Mesh).";
                return;
            }
            var bones = new List<string>(mesh.Bones ?? System.Array.Empty<string>());
            var adding = new List<string>();
            foreach (string boneId in boneIds)
            {
                string id = SpritePartIdUtility.Canonical(boneId);
                if (!string.IsNullOrEmpty(id) && !bones.Contains(id) && !adding.Contains(id))
                    adding.Add(id);
            }
            if (adding.Count == 0)
            {
                _status = "Those bones are already bound.";
                return;
            }
            // The first bind also binds the part itself, so the mesh keeps following its own joint.
            string self = SpritePartIdUtility.Canonical(slot.SlotId);
            if (bones.Count == 0 && !adding.Contains(self))
                bones.Add(self);
            int room = SpritePartsSkinning.MaxBones - bones.Count;
            if (room <= 0)
            {
                _status = "A mesh can use up to " + SpritePartsSkinning.MaxBones + " bones.";
                return;
            }
            bool clipped = adding.Count > room;
            if (clipped)
                adding.RemoveRange(room, adding.Count - room);
            RecordPartsUndo(undoName);
            var previousBones = mesh.Bones;
            var previousWeights = mesh.Weights;
            bones.AddRange(adding);
            mesh.Bones = bones.ToArray();
            if (!RecomputeAutoWeights(slot))
            {
                mesh.Bones = previousBones;
                mesh.Weights = previousWeights;
                return;
            }
            _partsWeightBone = bones.IndexOf(adding[0]);
            SaveDirty();
            _status = "Bound " + string.Join(", ", adding) + (clipped ? " (bone limit " + SpritePartsSkinning.MaxBones + " reached)" : "")
                      + ". Weights computed automatically; paint to adjust.";
        }

        /// <summary>Binds the bones whose segments lie on or near the mesh at rest (up to 4), then Auto.</summary>
        void BindNearestBones()
        {
            var slot = WeightsSlot();
            var mesh = slot?.Mesh;
            if (mesh == null || !mesh.HasMesh)
            {
                _status = "Make a mesh first (Edit Mesh).";
                return;
            }
            if (!TryGetBindSpace(slot, out var meshRest, out var size, out var pivot, out var guides))
            {
                _status = "Weights need the part's default appearance (its size and pivot).";
                return;
            }
            // Prefer real bones; with no bones in the rig, any joint counts.
            bool anyBone = false;
            foreach (var s in _profile.PartsSlots)
                anyBone |= s != null && s.IsBone;
            string self = SpritePartIdUtility.Canonical(slot.SlotId);
            var pool = guides.FindAll(g => g.Id != self && (!anyBone || (SpritePartsAuthoringOps.FindSlot(_profile, g.Id)?.IsBone ?? false)));
            var verts = new List<float2>(mesh.VertexCount);
            foreach (var uv in mesh.Vertices)
                verts.Add(MeshUvToRoot(meshRest, size, pivot, uv));
            // Near = within a tenth of the mesh's size.
            float reach = math.max(size.x, size.y) * 0.1f;
            var nearest = SpritePartsSkinning.NearestBones(verts, pool.ConvertAll(g => g.RootStart), pool.ConvertAll(g => g.RootEnd), reach, 4, mesh.Triangles);
            if (nearest.Count == 0)
            {
                _status = "No " + (anyBone ? "bone" : "joint") + " lies on this mesh. Place bones over the art, or use Bind Bone.";
                return;
            }
            BindWeightBones(nearest.ConvertAll(i => pool[i].Id), "Bind Nearest Bones");
        }

        /// <summary>Copies weights from one side of the Mirror axis to the other, swapping Left / Right bones.</summary>
        void MirrorWeightsCommand(bool leftToRight)
        {
            var mesh = WeightsSlot()?.Mesh;
            if (mesh == null || !mesh.HasWeights)
                return;
            var swap = new List<int>(mesh.BoneCount);
            int unmatched = 0;
            for (int b = 0; b < mesh.BoneCount; b++)
            {
                string other = SpritePartsSkinning.MirrorSideName(SpritePartsAuthoringOps.FindSlot(_profile, mesh.Bones[b])?.Name);
                int match = -1;
                if (other != null)
                {
                    for (int k = 0; k < mesh.BoneCount && match < 0; k++)
                    {
                        if (SpritePartsAuthoringOps.FindSlot(_profile, mesh.Bones[k])?.Name == other)
                            match = k;
                    }
                    if (match < 0)
                        unmatched++;
                }
                swap.Add(match >= 0 ? match : b);
            }
            RecordPartsUndo(leftToRight ? "Mirror Weights Left To Right" : "Mirror Weights Right To Left");
            int pairs = SpritePartsSkinning.MirrorWeights(mesh, _partsMirrorAxis, leftToRight, swap);
            if (pairs == 0)
            {
                _status = "No mirror pairs: no vertex sits at another's mirrored spot. Check the Mirror axis, or use Mirror Copy.";
                return;
            }
            SaveDirty();
            _status = "Mirrored weights on " + pairs + " vertex pairs " + (leftToRight ? "left → right" : "right → left")
                      + (unmatched > 0 ? ". " + unmatched + " sided bone(s) have no bound twin: bind the other side's bone too." : ".");
        }

        void UnbindWeightBone(int bone)
        {
            var mesh = WeightsSlot()?.Mesh;
            if (mesh == null || !mesh.HasWeights || (uint)bone >= (uint)mesh.BoneCount)
                return;
            RecordPartsUndo("Unbind Mesh Bone");
            int bones = mesh.BoneCount;
            if (bones == 1)
            {
                mesh.Bones = null;
                mesh.Weights = null;
            }
            else
            {
                var names = new List<string>(mesh.Bones);
                names.RemoveAt(bone);
                var next = new float[mesh.VertexCount * (bones - 1)];
                for (int v = 0; v < mesh.VertexCount; v++)
                {
                    for (int b = 0, o = 0; b < bones; b++)
                    {
                        if (b != bone)
                            next[v * (bones - 1) + o++] = mesh.Weights[v * bones + b];
                    }
                }
                SpritePartsSkinning.Normalize(next, bones - 1);
                mesh.Bones = names.ToArray();
                mesh.Weights = next;
            }
            _partsWeightBone = Mathf.Clamp(_partsWeightBone, 0, Mathf.Max(0, mesh.BoneCount - 1));
            SaveDirty();
            _status = "Bone unbound.";
        }

        void UnbindAllWeights()
        {
            var mesh = WeightsSlot()?.Mesh;
            if (mesh == null || mesh.BoneCount == 0)
                return;
            RecordPartsUndo("Unbind Mesh");
            mesh.Bones = null;
            mesh.Weights = null;
            SaveDirty();
            _status = "Mesh unweighted. It follows its own part only.";
        }

        void AutoWeightsCommand()
        {
            var slot = WeightsSlot();
            if (slot?.Mesh == null || !slot.Mesh.HasWeights)
                return;
            RecordPartsUndo("Auto Weights");
            if (RecomputeAutoWeights(slot))
            {
                SaveDirty();
                _status = "Weights recomputed.";
            }
        }

        List<int> WeightTargets()
        {
            string slotId = WeightsSlot()?.SlotId;
            bool mine = slotId != null && SpritePartIdUtility.Canonical(slotId) == _partsWarpSelectionSlotId;
            return mine && _partsWarpSelection.Count > 0 ? new List<int>(_partsWarpSelection) : null;
        }

        void SmoothWeightsCommand()
        {
            var mesh = WeightsSlot()?.Mesh;
            if (mesh == null || !mesh.HasWeights)
                return;
            RecordPartsUndo("Smooth Weights");
            SpritePartsSkinning.Smooth(mesh.Weights, mesh.BoneCount, mesh.Triangles, WeightTargets());
            SaveDirty();
            _status = "Weights smoothed.";
        }

        void PruneWeightsCommand()
        {
            var mesh = WeightsSlot()?.Mesh;
            if (mesh == null || !mesh.HasWeights)
                return;
            RecordPartsUndo("Prune Weights");
            SpritePartsSkinning.Prune(mesh.Weights, mesh.BoneCount, _partsWeightPruneAt);
            SaveDirty();
            _status = "Pruned weights under " + Mathf.RoundToInt(_partsWeightPruneAt * 100f) + "%.";
        }

        /// <summary>Add (or remove with Shift) weight for the chosen bone under the brush.</summary>
        void PaintWeights(Rect sprite, Vector2 mouse, bool remove)
        {
            var mesh = MeshEditSlot()?.Mesh;
            if (mesh == null || !mesh.HasWeights || (uint)_partsWeightBone >= (uint)mesh.BoneCount)
                return;
            float size = Mathf.Max(1f, _partsSoftSize);
            float inner = size * (1f - Mathf.Clamp01(_partsSoftFeather));
            int bones = mesh.BoneCount;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                float d = Vector2.Distance(MeshUvToGui(sprite, mesh.Vertices[v]), mouse);
                if (d >= size)
                    continue;
                float falloff = d <= inner ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (d - inner) / Mathf.Max(1e-4f, size - inner));
                float amount = _partsWeightStrength * falloff * (remove ? -1f : 1f);
                float current = mesh.Weights[v * bones + _partsWeightBone];
                SpritePartsSkinning.SetWeight(mesh.Weights, bones, v, _partsWeightBone, current + amount);
            }
            if (_asset != null)
                EditorUtility.SetDirty(_asset);
        }

        void SetSelectedWeight(float value)
        {
            var mesh = WeightsSlot()?.Mesh;
            var targets = WeightTargets();
            if (mesh == null || !mesh.HasWeights || targets == null)
                return;
            RecordPartsUndo("Set Weights");
            foreach (int v in targets)
                SpritePartsSkinning.SetWeight(mesh.Weights, mesh.BoneCount, v, _partsWeightBone, value);
            SaveDirty();
        }

        // ------------------------------------------------------------------ Edit Mesh: Weights tool

        /// <summary>Bone guides in Edit Mesh image space: (uv start, uv end, bone index or -1 when unbound).</summary>
        List<(Vector2 a, Vector2 b, int bone, string id, string name)> MeshEditBoneGuides(SpritePartSlotDef slot)
        {
            var result = new List<(Vector2, Vector2, int, string, string)>();
            if (slot?.Mesh == null || !TryGetBindSpace(slot, out var meshRest, out var size, out var pivot, out var guides))
                return result;
            foreach (var g in guides)
            {
                int bone = slot.Mesh.Bones == null ? -1 : System.Array.IndexOf(slot.Mesh.Bones, g.Id);
                result.Add((RootToMeshUv(meshRest, size, pivot, g.RootStart),
                    RootToMeshUv(meshRest, size, pivot, g.RootEnd), bone, g.Id, g.Name));
            }
            return result;
        }

        void DrawPartsMeshWeights(Rect sprite, SpritePartMeshDef mesh)
        {
            var slot = MeshEditSlot();
            var guides = MeshEditBoneGuides(slot);
            int bones = mesh?.BoneCount ?? 0;

            if (mesh != null && mesh.HasWeights && (uint)_partsWeightBone < (uint)bones)
            {
                // Heat map of the chosen bone: blue = 0%, red = 100%.
                Handles.BeginGUI();
                for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
                {
                    int a = mesh.Triangles[t], b = mesh.Triangles[t + 1], c = mesh.Triangles[t + 2];
                    float w = (mesh.Weights[a * bones + _partsWeightBone] + mesh.Weights[b * bones + _partsWeightBone]
                               + mesh.Weights[c * bones + _partsWeightBone]) / 3f;
                    var col = Color.Lerp(new Color(0.1f, 0.25f, 1f), new Color(1f, 0.15f, 0.1f), w);
                    col.a = 0.45f;
                    Handles.color = col;
                    Handles.DrawAAConvexPolygon(
                        MeshUvToGui(sprite, mesh.Vertices[a]), MeshUvToGui(sprite, mesh.Vertices[b]), MeshUvToGui(sprite, mesh.Vertices[c]));
                }
                Handles.EndGUI();
            }

            foreach (var g in guides)
            {
                if (g.bone < 0 && g.id != SpritePartIdUtility.Canonical(slot?.SlotId))
                {
                    // Unbound joints stay faint so they can still be clicked to bind.
                    Vector2 p = MeshUvToGui(sprite, g.a);
                    DrawGuiRectOutline(new Rect(p.x - 4f, p.y - 4f, 8f, 8f), new Color(1f, 1f, 1f, 0.35f), 1f);
                    continue;
                }
                var color = g.bone >= 0 ? BoneColor(g.bone) : Color.white;
                bool chosen = g.bone == _partsWeightBone && g.bone >= 0;
                Vector2 a = MeshUvToGui(sprite, g.a);
                Vector2 b = MeshUvToGui(sprite, g.b);
                if ((a - b).sqrMagnitude > 1f)
                    DrawGuiLine(a, b, color, chosen ? 4f : 2f);
                EditorGUI.DrawRect(new Rect(a.x - 5f, a.y - 5f, 10f, 10f), color);
                if (chosen)
                    DrawGuiRectOutline(new Rect(a.x - 8f, a.y - 8f, 16f, 16f), Color.white, 1.5f);
                GUI.Label(new Rect(a.x + 8f, a.y - 8f, 120f, 16f), g.name, _mutedStyle);
            }

            if (mesh != null && mesh.HasWeights && (uint)_partsWarpHover < (uint)mesh.VertexCount)
            {
                var parts = new List<string>();
                for (int b = 0; b < bones; b++)
                {
                    float w = mesh.Weights[_partsWarpHover * bones + b];
                    if (w >= 0.005f)
                        parts.Add(mesh.Bones[b] + " " + Mathf.RoundToInt(w * 100f) + "%");
                }
                Vector2 p = MeshUvToGui(sprite, mesh.Vertices[_partsWarpHover]);
                GUI.Label(new Rect(p.x + 10f, p.y + 6f, 260f, 16f), string.Join("  ", parts), _mutedStyle);
            }
        }

        /// <summary>Weights tool mouse-down: pick a joint (bind / choose bone) or start painting.</summary>
        void BeginMeshWeightsInput(Rect sprite, Event evt, int controlId)
        {
            var slot = MeshEditSlot();
            var mesh = slot?.Mesh;
            if (mesh == null || !mesh.HasMesh)
            {
                _status = "Make a mesh first.";
                return;
            }
            foreach (var g in MeshEditBoneGuides(slot))
            {
                if ((MeshUvToGui(sprite, g.a) - evt.mousePosition).sqrMagnitude > 100f)
                    continue;
                if (g.bone >= 0)
                {
                    _partsWeightBone = g.bone;
                    _status = "Painting weights for " + g.name + ". Shift removes.";
                }
                else
                    BindWeightBone(g.id);
                return;
            }
            if (!mesh.HasWeights)
            {
                _status = "Click a joint square to bind it to this mesh.";
                return;
            }
            BeginPartsDragUndo("Paint Weights");
            _partsWeightPainting = true;
            CapturePartsMeshDrag(controlId);
            PaintWeights(sprite, evt.mousePosition, evt.shift);
        }

        void DrawPartsWeightsInspector(SpritePartSlotDef slot)
        {
            var mesh = slot.Mesh;
            if (mesh == null || !mesh.HasMesh)
                return;
            GUILayout.Space(6f);
            GUILayout.Label("WEIGHTS", _sectionStyle);
            if (!mesh.HasWeights)
                EditorGUILayout.LabelField("Unweighted: the mesh follows this part only.", _mutedStyle);
            for (int b = 0; b < mesh.BoneCount; b++)
            {
                EditorGUILayout.BeginHorizontal();
                var swatch = GUILayoutUtility.GetRect(12f, 18f, GUILayout.Width(12f));
                EditorGUI.DrawRect(new Rect(swatch.x, swatch.y + 4f, 10f, 10f), BoneColor(b));
                bool on = b == _partsWeightBone;
                if (GUILayout.Toggle(on, mesh.Bones[b], EditorStyles.miniButton) && !on)
                    _partsWeightBone = b;
                if (GUILayout.Button(new GUIContent("x", "Unbind this bone"), GUILayout.Width(22f)))
                {
                    UnbindWeightBone(b);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Bind Bone", "Bind a part joint; weights are computed automatically."), GUILayout.Height(20f)))
            {
                var menu = new GenericMenu();
                foreach (var s in _profile.PartsSlots)
                {
                    if (s == null)
                        continue;
                    string id = SpritePartIdUtility.Canonical(s.SlotId);
                    bool bound = mesh.Bones != null && System.Array.IndexOf(mesh.Bones, id) >= 0;
                    if (bound)
                        menu.AddDisabledItem(new GUIContent(s.Name + " (" + id + ")"), true);
                    else
                        menu.AddItem(new GUIContent(s.Name + " (" + id + ")"), false, () => BindWeightBone(id));
                }
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Nearest Bones"), false, BindNearestBones);
                foreach (var s in _profile.PartsSlots)
                {
                    if (s == null)
                        continue;
                    string id = SpritePartIdUtility.Canonical(s.SlotId);
                    string label = (s.IsBone ? "◇ " : "") + s.Name + " (" + id + ")";
                    menu.AddItem(new GUIContent("Chain Down/" + label), false,
                        () => BindWeightBones(SpritePartsSkinning.ChainBones(_profile, id, true), "Bind Bone Chain"));
                    menu.AddItem(new GUIContent("Chain Up/" + label), false,
                        () => BindWeightBones(SpritePartsSkinning.ChainBones(_profile, id, false), "Bind Bone Chain"));
                }
                menu.ShowAsContext();
            }
            using (new EditorGUI.DisabledScope(!mesh.HasWeights))
            {
                if (GUILayout.Button(new GUIContent("Auto", "Recompute all weights from bone distance."), GUILayout.Height(20f)))
                    AutoWeightsCommand();
                if (GUILayout.Button(new GUIContent("Smooth", "Average selected vertices (or all) with their neighbours."), GUILayout.Height(20f)))
                    SmoothWeightsCommand();
                if (GUILayout.Button(new GUIContent("Unbind", "Remove all weights."), GUILayout.Height(20f)))
                    UnbindAllWeights();
            }
            EditorGUILayout.EndHorizontal();
            if (!mesh.HasWeights)
                return;

            EditorGUILayout.BeginHorizontal();
            _partsWeightPruneAt = EditorGUILayout.Slider(new GUIContent("Prune <", "Drop influences below this."), _partsWeightPruneAt, 0.01f, 0.5f);
            if (GUILayout.Button("Prune", GUILayout.Width(54f)))
                PruneWeightsCommand();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Mirror L → R",
                    "Copy the left side's weights onto the right across the Mirror axis (Left / Right bones swap)"), GUILayout.Height(20f)))
                MirrorWeightsCommand(true);
            if (GUILayout.Button(new GUIContent("R → L", "Copy the right side's weights onto the left"), GUILayout.Height(20f)))
                MirrorWeightsCommand(false);
            EditorGUILayout.EndHorizontal();

            _partsWeightStrength = EditorGUILayout.Slider(new GUIContent("Brush Strength", "Weight added per brush step."), _partsWeightStrength, 0.01f, 1f);
            _partsSoftSize = EditorGUILayout.Slider(new GUIContent("Brush Size", "Pixels. Shared with Soft Selection."), _partsSoftSize, 5f, 400f);

            var targets = WeightTargets();
            if (targets != null && (uint)_partsWeightBone < (uint)mesh.BoneCount)
            {
                float avg = 0f;
                int count = 0;
                foreach (int v in targets)
                {
                    if ((uint)v >= (uint)mesh.VertexCount)
                        continue;
                    avg += mesh.Weights[v * mesh.BoneCount + _partsWeightBone];
                    count++;
                }
                avg = count > 0 ? avg / count : 0f;
                EditorGUI.BeginChangeCheck();
                float value = EditorGUILayout.Slider(
                    new GUIContent("Direct: " + mesh.Bones[_partsWeightBone], "Exact weight of the chosen bone on the selected vertices."),
                    avg, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                    SetSelectedWeight(value);
            }
            EditorGUILayout.LabelField("Edit Mesh > 4 Weights: drag paints (Shift removes). Click a joint to bind or pick it.", _mutedStyle);
        }
    }
}
