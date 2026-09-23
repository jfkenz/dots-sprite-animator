using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // JIGGLE (spring physics, solved in the game and in the preview while it plays):
    //   Add Jiggle   the selected part and the chain under it (first child each) swing behind the animation
    //   per row      part / chain / stiffness / damping / gravity / mix / enabled / remove
    // Paused frames show the keyed pose, so keys never absorb the swing.
    public sealed partial class SpriteSheetToolWindow
    {
        [SerializeField] bool _partsPhysicsPreview = true;

        void AddPartsJiggle()
        {
            var slot = CurrentPartsSlot;
            if (slot == null)
            {
                _status = "Select the part that should swing (hair, tail, ear, cloth).";
                return;
            }
            // The whole first-child chain under it, up to 8 joints.
            int chain = 1;
            for (var s = FirstChildSlot(slot.SlotId); s != null && chain < 8; s = FirstChildSlot(s.SlotId))
                chain++;
            RecordPartsUndo("Add Jiggle");
            _profile.PartsJiggles ??= new List<SpritePartsJiggleDef>();
            _profile.PartsJiggles.Add(new SpritePartsJiggleDef
            {
                Name = slot.Name + " Jiggle",
                SlotId = SpritePartIdUtility.Canonical(slot.SlotId),
                ChainLength = chain,
            });
            SaveDirty();
            _status = "Jiggle added to " + slot.Name + (chain > 1 ? " and " + (chain - 1) + " parts under it" : "")
                      + ". Press Play to see it swing.";
        }

        SpritePartSlotDef FirstChildSlot(string slotId)
        {
            string id = SpritePartIdUtility.Canonical(slotId);
            foreach (var s in _profile.PartsSlots)
            {
                if (s != null && !string.IsNullOrWhiteSpace(s.ParentSlotId) && SpritePartIdUtility.Canonical(s.ParentSlotId) == id)
                    return s;
            }
            return null;
        }

        void DrawPartsJiggleInspector()
        {
            if (_profile == null)
                return;
            var list = _profile.PartsJiggles ??= new List<SpritePartsJiggleDef>();
            if (!PartsSection("JIGGLE", list.Count == 0 ? "none" : list.Count + " chain" + (list.Count == 1 ? "" : "s")))
                return;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Add Jiggle for Selected Part",
                    "The selected part and the parts under it swing behind the animation like a spring.")))
                AddPartsJiggle();
            bool preview = GUILayout.Toggle(_partsPhysicsPreview, new GUIContent("Preview",
                "Simulate while the preview plays. Paused frames always show the keyed pose."), EditorStyles.miniButton, GUILayout.Width(58f));
            if (preview != _partsPhysicsPreview)
            {
                _partsPhysicsPreview = preview;
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
            if (list.Count == 0)
            {
                EditorGUILayout.LabelField("Spring physics for hair, tails, ears and cloth, in the game too.",
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
                var j = list[c];
                if (j == null)
                    continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                bool enabled = EditorGUILayout.Toggle(j.Enabled, GUILayout.Width(16f));
                string name = EditorGUILayout.TextField(j.Name);
                bool remove = GUILayout.Button(new GUIContent("×", "Remove this jiggle"), GUILayout.Width(22f));
                EditorGUILayout.EndHorizontal();
                int slot = EditorGUILayout.Popup(new GUIContent("Part", "The first joint that swings"),
                    Mathf.Max(0, ids.IndexOf(SpritePartIdUtility.Canonical(j.SlotId ?? string.Empty))), names.ToArray());
                int chain = EditorGUILayout.IntSlider(new GUIContent("Chain", "How many joints swing: the part, then its first child, and so on"),
                    Mathf.Clamp(j.ChainLength, 1, 8), 1, 8);
                float stiffness = EditorGUILayout.Slider(new GUIContent("Stiffness", "0 = loose and slow, 1 = snaps back quickly"), j.Stiffness, 0f, 1f);
                float damping = EditorGUILayout.Slider(new GUIContent("Damping", "0 = bouncy, 1 = no overshoot"), j.Damping, 0f, 1f);
                float gravity = EditorGUILayout.FloatField(new GUIContent("Gravity", "World units / s² pulling the tips down"), j.Gravity);
                bool mixKeyed = TryKeyedValue(SpritePartsValueKind.JiggleMix, j.Name, j.Mix, out float mixShown);
                EditorGUILayout.BeginHorizontal();
                float mix = EditorGUILayout.Slider(new GUIContent("Mix", "0 = the animation only, 1 = full swing"), mixShown, 0f, 1f);
                ValueKeyButton(SpritePartsValueKind.JiggleMix, j.Name, mixShown, "Jiggle Mix");
                EditorGUILayout.EndHorizontal();
                string chainText = JiggleChainText(ids[slot], chain);
                if (!string.IsNullOrEmpty(chainText))
                    EditorGUILayout.LabelField(chainText, EditorStyles.wordWrappedMiniLabel);
                if (EditorGUI.EndChangeCheck())
                {
                    bool mixToKey = !Mathf.Approximately(mix, mixShown)
                                    && KeyEditedValue(SpritePartsValueKind.JiggleMix, j.Name, mix, "Jiggle Mix");
                    RecordPartsUndo(remove ? "Remove Jiggle" : "Edit Jiggle");
                    if (remove)
                    {
                        SpritePartsAuthoringOps.RemoveValueTracks(_profile, j.Name, SpritePartsValueKind.JiggleMix);
                        list.RemoveAt(c);
                        SaveDirty();
                        EditorGUILayout.EndVertical();
                        break;
                    }
                    if (name != j.Name)
                        SpritePartsAuthoringOps.RenameValueTarget(_profile, j.Name, name, SpritePartsValueKind.JiggleMix);
                    j.Enabled = enabled;
                    j.Name = name;
                    j.SlotId = ids[slot];
                    j.ChainLength = chain;
                    j.Stiffness = stiffness;
                    j.Damping = damping;
                    j.Gravity = gravity;
                    if (!mixKeyed && !mixToKey)
                        j.Mix = mix;
                    SaveDirty();
                }
                EditorGUILayout.EndVertical();
            }
        }

        /// <summary>"Hair → Hair 2 → Hair 3", or why nothing swings.</summary>
        string JiggleChainText(string slotId, int chain)
        {
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, slotId ?? string.Empty);
            if (slot == null)
                return "Pick a part.";
            var parts = new List<string>();
            for (var s = slot; s != null && parts.Count < chain; s = FirstChildSlot(s.SlotId))
                parts.Add(s.Name);
            string text = string.Join(" → ", parts);
            return parts.Count < chain ? text + " (the chain ends here)" : text;
        }
    }
}
