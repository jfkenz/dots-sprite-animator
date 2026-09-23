using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Clipping mask: "Clip to" another part and this part only shows inside it (the mask's mesh outline, or its
    // rectangle). Give the mask a traced mesh to clip to the shape of its art. Cut in the game and on the canvas.
    public sealed partial class SpriteSheetToolWindow
    {
        void DrawPartsClipMaskInspector(SpritePartSlotDef slot, bool partLocked)
        {
            if (slot == null || slot.IsBone || slot.IsClipShape || _profile?.PartsSlots == null)
                return;
            var names = new List<string> { "(none)" };
            var ids = new List<string> { string.Empty };
            string self = SpritePartIdUtility.Canonical(slot.SlotId);
            foreach (var s in _profile.PartsSlots)
            {
                if (s == null || s.IsBone || s.IsClipShape || SpritePartIdUtility.Canonical(s.SlotId) == self)
                    continue; // bones and clip shapes have no image shape to clip to
                names.Add(s.Name);
                ids.Add(SpritePartIdUtility.Canonical(s.SlotId));
            }
            int current = Mathf.Max(0, ids.IndexOf(SpritePartIdUtility.Canonical(slot.ClipMaskSlotId ?? string.Empty)));
            using (new EditorGUI.DisabledScope(partLocked))
            {
                int next = EditorGUILayout.Popup(new GUIContent("Clip To",
                    "Clipping mask: this part only shows inside the chosen part (its mesh outline, or its rectangle). " +
                    "Trace a mesh on the mask to clip to the shape of its art."), current, names.ToArray());
                if (next != current)
                {
                    RecordPartsUndo(next == 0 ? "Remove Clipping Mask" : "Set Clipping Mask");
                    slot.ClipMaskSlotId = ids[next];
                    SaveDirty();
                    _status = next == 0 ? slot.Name + " is no longer clipped."
                        : slot.Name + " now shows only inside " + names[next] + ".";
                }
            }
            string hint = ClipMaskHint(slot);
            if (!string.IsNullOrEmpty(hint))
                EditorGUILayout.LabelField(hint, EditorStyles.wordWrappedMiniLabel);
        }

        string ClipMaskHint(SpritePartSlotDef slot)
        {
            if (string.IsNullOrWhiteSpace(slot.ClipMaskSlotId))
                return null;
            var mask = SpritePartsAuthoringOps.FindSlot(_profile, slot.ClipMaskSlotId);
            if (mask == null)
                return "The mask part is gone: pick another.";
            if (!SpritePartsSkinning.TryResolveQuad(_profile, slot, out _, out _) || !SpritePartsSkinning.TryResolveQuad(_profile, mask, out _, out _))
                return "Both parts need a default image for the clip to know their size.";
            return mask.Mesh != null && mask.Mesh.HasMesh
                ? "Clipped to the outline of " + mask.Name + "'s mesh."
                : "Clipped to " + mask.Name + "'s rectangle. Give it a mesh (Edit Mesh > Trace) to follow its shape.";
        }
    }
}
