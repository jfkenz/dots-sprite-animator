using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // IK CONSTRAINTS (runtime IK, solved in the game and the editor preview):
    //   Add IK       the selected part becomes the effector; a target bone is made at its current spot
    //   per row      effector / target / chain 1-2 / bend / mix / enabled / remove
    // Move or key the target bone to plant a foot or aim a hand; gameplay can drive the target too.
    public sealed partial class SpriteSheetToolWindow
    {
        Vector2 _partsIkScroll;

        void AddPartsIkConstraint()
        {
            var effector = CurrentPartsSlot;
            if (effector == null)
            {
                _status = "Select the part whose joint should reach the target (a hand, a foot).";
                return;
            }
            if (string.IsNullOrWhiteSpace(effector.ParentSlotId))
            {
                _status = "The effector needs a parent to turn (IK turns the parent chain).";
                return;
            }
            // The target bone starts where the effector's joint is now, at the root (so the body does not carry it).
            float2 at = float2.zero;
            if (SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), _partsPreviewTime,
                    Allocator.Temp, out var blob, out var poses, out var matrices, out _))
            {
                try
                {
                    int idx = BlobSlotIndex(ref blob.Value, effector.SlotId);
                    if (idx >= 0 && idx < matrices.Length)
                        at = matrices[idx].c3.xy;
                }
                finally
                {
                    SpritePartsOnion.DisposeSample(blob, poses, matrices);
                }
            }
            RecordPartsUndo("Add IK");
            var result = SpritePartsAuthoringOps.TryAddBone(_profile, null, out var target);
            if (!result.Ok || target == null)
            {
                _status = result.Reason;
                return;
            }
            target.Name = SpritePartsAuthoringOps.UniqueSiblingDisplayName(_profile, string.Empty, effector.Name + " IK");
            target.RestPosition = new Vector2(at.x, at.y);
            target.BoneLength = 0.3f;
            _profile.PartsIkConstraints ??= new List<SpritePartsIkConstraintDef>();
            _profile.PartsIkConstraints.Add(new SpritePartsIkConstraintDef
            {
                Name = effector.Name + " IK",
                EffectorSlotId = SpritePartIdUtility.Canonical(effector.SlotId),
                TargetSlotId = SpritePartIdUtility.Canonical(target.SlotId),
                ChainLength = string.IsNullOrWhiteSpace(SpritePartsAuthoringOps.FindSlot(_profile, effector.ParentSlotId)?.ParentSlotId) ? 1 : 2,
            });
            SaveDirty();
            SelectPartsSlotId(target.SlotId, false, false);
            _status = "IK added: move or key the target bone '" + target.Name + "' and " + effector.Name + " follows.";
        }

        void DrawPartsIkInspector()
        {
            if (_profile == null)
                return;
            var list = _profile.PartsIkConstraints ??= new List<SpritePartsIkConstraintDef>();
            if (!PartsSection("IK CONSTRAINTS", list.Count == 0 ? "none" : list.Count + " IK"))
                return;
            if (GUILayout.Button(new GUIContent("Add IK for Selected Part",
                    "The selected part (hand / foot) reaches a new target bone by turning its parent (and grandparent).")))
                AddPartsIkConstraint();
            if (list.Count == 0)
            {
                EditorGUILayout.LabelField("Runtime IK: a hand or foot reaches a target bone, in the game too.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }
            var names = new List<string> { "(none)" };
            var ids = new List<string> { string.Empty };
            foreach (var s in _profile.PartsSlots)
            {
                if (s == null)
                    continue;
                names.Add((s.IsBone ? "◇ " : "") + s.Name);
                ids.Add(SpritePartIdUtility.Canonical(s.SlotId));
            }
            for (int c = 0; c < list.Count; c++)
            {
                var ik = list[c];
                if (ik == null)
                    continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                bool enabled = EditorGUILayout.Toggle(ik.Enabled, GUILayout.Width(16f));
                string name = EditorGUILayout.TextField(ik.Name);
                bool remove = GUILayout.Button(new GUIContent("×", "Remove this IK (the target bone stays)"), GUILayout.Width(22f));
                EditorGUILayout.EndHorizontal();
                int eff = EditorGUILayout.Popup(new GUIContent("Effector", "The joint that reaches the target"),
                    Mathf.Max(0, ids.IndexOf(SpritePartIdUtility.Canonical(ik.EffectorSlotId ?? string.Empty))), names.ToArray());
                int tgt = EditorGUILayout.Popup(new GUIContent("Target", "Usually a bone; move or key it"),
                    Mathf.Max(0, ids.IndexOf(SpritePartIdUtility.Canonical(ik.TargetSlotId ?? string.Empty))), names.ToArray());
                // Mix and bend show the open clip's keys at the playhead (Animate mode), else the setup values.
                bool mixKeyed = TryKeyedValue(SpritePartsValueKind.IkMix, ik.Name, ik.Mix, out float mixShown);
                bool bendKeyed = TryKeyedValue(SpritePartsValueKind.IkBend, ik.Name, ik.BendPositive ? 1f : -1f, out float bendValue);
                bool bendShown = bendValue >= 0f;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(new GUIContent("Chain", "1 = turn the parent only, 2 = parent + grandparent (elbow + shoulder)"), GUILayout.Width(40f));
                int chain = GUILayout.Toolbar(Mathf.Clamp(ik.ChainLength, 1, 2) - 1, new[] { "1", "2" }, GUILayout.Width(60f)) + 1;
                bool bend = GUILayout.Toggle(bendShown, new GUIContent(bendShown ? "Bend +" : "Bend −", "Which way the middle joint bends"),
                    EditorStyles.miniButton, GUILayout.Width(56f));
                ValueKeyButton(SpritePartsValueKind.IkBend, ik.Name, bendShown ? 1f : -1f, "IK Bend");
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                float mix = EditorGUILayout.Slider(new GUIContent("Mix", "0 = clip pose, 1 = fully solved"), mixShown, 0f, 1f);
                ValueKeyButton(SpritePartsValueKind.IkMix, ik.Name, mixShown, "IK Mix");
                EditorGUILayout.EndHorizontal();
                string chainText = IkChainText(ik);
                if (!string.IsNullOrEmpty(chainText))
                    EditorGUILayout.LabelField(chainText, EditorStyles.miniLabel);
                if (EditorGUI.EndChangeCheck())
                {
                    // A changed Mix / bend the open clip keys goes into a key at the playhead, not the setup.
                    bool mixToKey = !Mathf.Approximately(mix, mixShown)
                                    && KeyEditedValue(SpritePartsValueKind.IkMix, ik.Name, mix, "IK Mix");
                    bool bendToKey = bend != bendShown
                                     && KeyEditedValue(SpritePartsValueKind.IkBend, ik.Name, bend ? 1f : -1f, "IK Bend");
                    RecordPartsUndo(remove ? "Remove IK" : "Edit IK");
                    if (remove)
                    {
                        SpritePartsAuthoringOps.RemoveValueTracks(_profile, ik.Name, SpritePartsValueKind.IkMix, SpritePartsValueKind.IkBend);
                        list.RemoveAt(c);
                        SaveDirty();
                        EditorGUILayout.EndVertical();
                        break;
                    }
                    if (name != ik.Name)
                        SpritePartsAuthoringOps.RenameValueTarget(_profile, ik.Name, name, SpritePartsValueKind.IkMix, SpritePartsValueKind.IkBend);
                    ik.Enabled = enabled;
                    ik.Name = name;
                    ik.EffectorSlotId = ids[eff];
                    ik.TargetSlotId = ids[tgt];
                    ik.ChainLength = chain;
                    if (!bendKeyed && !bendToKey)
                        ik.BendPositive = bend;
                    if (!mixKeyed && !mixToKey)
                        ik.Mix = mix;
                    SaveDirty();
                }
                EditorGUILayout.EndVertical();
            }
        }

        /// <summary>"Upper → Lower → Effector" for the row, or why it cannot solve.</summary>
        string IkChainText(SpritePartsIkConstraintDef ik)
        {
            var eff = SpritePartsAuthoringOps.FindSlot(_profile, ik.EffectorSlotId ?? string.Empty);
            if (eff == null)
                return "Pick an effector.";
            if (SpritePartsAuthoringOps.FindSlot(_profile, ik.TargetSlotId ?? string.Empty) == null)
                return "Pick a target.";
            var lower = SpritePartsAuthoringOps.FindSlot(_profile, eff.ParentSlotId ?? string.Empty);
            if (lower == null)
                return "The effector has no parent to turn.";
            var upper = ik.ChainLength >= 2 ? SpritePartsAuthoringOps.FindSlot(_profile, lower.ParentSlotId ?? string.Empty) : null;
            if (IsDescendantOf(ik.TargetSlotId, lower.SlotId))
                return "The target is inside the chain: it moves with it. Put the target at the root.";
            return (upper != null ? upper.Name + " → " : "") + lower.Name + " → " + eff.Name;
        }

        bool IsDescendantOf(string slotId, string ancestorId)
        {
            string a = SpritePartIdUtility.Canonical(ancestorId);
            var cur = SpritePartsAuthoringOps.FindSlot(_profile, slotId ?? string.Empty);
            for (int guard = 0; cur != null && guard < 256; guard++)
            {
                if (SpritePartIdUtility.Canonical(cur.SlotId) == a)
                    return true;
                cur = string.IsNullOrWhiteSpace(cur.ParentSlotId) ? null : SpritePartsAuthoringOps.FindSlot(_profile, cur.ParentSlotId);
            }
            return false;
        }

        /// <summary>Canvas: each IK's target as a crosshair ring, dotted to its effector.</summary>
        void DrawPartsIkTargets(Rect canvas)
        {
            if (_profile?.PartsIkConstraints == null || _profile.PartsIkConstraints.Count == 0
                || Event.current.type != EventType.Repaint || IsPartsMeshEdit() || IsPartsPivotFocus())
                return;
            if (!SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), _partsPreviewTime,
                    Allocator.Temp, out var blob, out var poses, out var matrices, out _))
                return;
            try
            {
                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                Handles.BeginGUI();
                foreach (var ik in _profile.PartsIkConstraints)
                {
                    if (ik == null || !ik.Enabled)
                        continue;
                    int e = BlobSlotIndex(ref blob.Value, ik.EffectorSlotId ?? string.Empty);
                    int t = BlobSlotIndex(ref blob.Value, ik.TargetSlotId ?? string.Empty);
                    if (e < 0 || t < 0 || e >= matrices.Length || t >= matrices.Length)
                        continue;
                    Vector2 ej = WorldToCanvas(canvas, matrices[e].c3.xy);
                    Vector2 tj = WorldToCanvas(canvas, matrices[t].c3.xy);
                    var color = new Color(1f, 0.45f, 0.75f, 0.95f);
                    Handles.color = color;
                    Handles.DrawDottedLine(ej, tj, 4f);
                    Handles.DrawWireDisc(tj, Vector3.forward, 9f);
                    Handles.DrawAAPolyLine(2f, tj + new Vector2(-13f, 0f), tj + new Vector2(13f, 0f));
                    Handles.DrawAAPolyLine(2f, tj + new Vector2(0f, -13f), tj + new Vector2(0f, 13f));
                }
                Handles.EndGUI();
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }
    }
}
