using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // SPRITE GROUPS: parts whose sprites change together (both eyes open / half / closed; a mouth set).
    //   Add Group      the selected parts, with a first state holding the sprites they show now
    //   per state      Show (preview on the canvas) / ◆ key it at the playhead (Animate) / Capture the sprites shown now
    //   In the game    clips key the state; SpriteParts.SetSpriteGroup(em, e, "Eyes", "Closed") overrides it
    // LAYER PREVIEW: play another clip over the open one on the canvas, like SpriteParts.PlayLayer.
    // Sprite shown on the canvas: group Show > layer preview key > the part's own key > group key > skin / default.
    public sealed partial class SpriteSheetToolWindow
    {
        /// <summary>Canvas-only group states (Show buttons): group name -> state index.</summary>
        readonly Dictionary<string, int> _partsGroupPreview = new Dictionary<string, int>();

        /// <summary>The sprite the canvas shows for a part, in the game's order (see the header).</summary>
        string ResolvePartsPreviewAppearanceId(SpritePartSlotDef slot, float sampleTime)
        {
            string id = SpritePartIdUtility.Canonical(slot.SlotId);
            var groups = _profile.PartsSpriteGroups;
            if (groups != null)
            {
                foreach (var g in groups)
                    if (g != null && _partsGroupPreview.TryGetValue(g.Name ?? string.Empty, out int s) && GroupBinding(g, s, id, out string forced))
                        return forced;
            }
            var layer = SpritePartsOnion.PreviewLayer;
            if (layer.Active && _partsMode == SpritePartsStudioMode.Animate && PreviewLayerCovers(layer, id))
            {
                string layered = SpritePartsAuthoringOps.SampleKeyedAppearanceId(_profile, layer.ClipIndex, slot.SlotId,
                    Mathf.Max(0f, sampleTime - layer.Start));
                if (!string.IsNullOrEmpty(layered))
                    return layered;
            }
            string own = SpritePartsAuthoringOps.SampleKeyedAppearanceId(_profile, _partsSelectedClip, slot.SlotId, sampleTime);
            if (!string.IsNullOrEmpty(own))
                return own;
            var clip = _partsMode == SpritePartsStudioMode.Animate ? CurrentPartsClip : null;
            if (clip != null && groups != null)
            {
                foreach (var g in groups)
                    if (g != null && SpritePartsAuthoringOps.TrySampleValue(clip, SpritePartsValueKind.SpriteGroup, g.Name, sampleTime, out float v)
                        && GroupBinding(g, Mathf.RoundToInt(v), id, out string keyed))
                        return keyed;
            }
            return SpritePartsAuthoringOps.ResolveAppearanceId(slot, _partsSkinPreviewOverrides);
        }

        bool PreviewLayerCovers(SpritePartsOnion.LayerPreviewState layer, string slotId)
        {
            if (string.IsNullOrEmpty(layer.Mask))
                return true;
            var mask = _profile.PartsMasks?.Find(m => m != null && m.Name == layer.Mask);
            return mask?.SlotIds == null || mask.SlotIds.Exists(s => SpritePartIdUtility.Canonical(s ?? string.Empty) == slotId);
        }

        static bool GroupBinding(SpritePartsSpriteGroupDef group, int state, string slotId, out string appearanceId)
        {
            appearanceId = null;
            if (group.States == null || state < 0 || state >= group.States.Count || group.States[state]?.Bindings == null)
                return false;
            var b = group.States[state].Bindings.Find(x => x != null && SpritePartIdUtility.Canonical(x.SlotId ?? string.Empty) == slotId);
            if (b == null || string.IsNullOrEmpty(b.AppearanceId))
                return false;
            appearanceId = b.AppearanceId;
            return true;
        }

        /// <summary>The sprites the group's parts show now, without group previews (keys, skin, default).</summary>
        List<SpritePartsSkinBindingDef> CaptureGroupSprites(SpritePartsSpriteGroupDef group)
        {
            var result = new List<SpritePartsSkinBindingDef>();
            foreach (string sid in group.SlotIds ?? new List<string>())
            {
                var slot = SpritePartsAuthoringOps.FindSlot(_profile, sid ?? string.Empty);
                if (slot == null)
                    continue;
                string app = SpritePartsAuthoringOps.ResolvePreviewAppearanceId(_profile, slot,
                    _partsMode == SpritePartsStudioMode.Animate ? _partsSelectedClip : -1, _partsPreviewTime, _partsSkinPreviewOverrides);
                if (!string.IsNullOrEmpty(app))
                    result.Add(new SpritePartsSkinBindingDef { SlotId = SpritePartIdUtility.Canonical(slot.SlotId), AppearanceId = app });
            }
            return result;
        }

        void DrawPartsSpriteGroupsInspector()
        {
            if (_profile == null)
                return;
            var groups = _profile.PartsSpriteGroups ??= new List<SpritePartsSpriteGroupDef>();
            if (!PartsSection("SPRITE GROUPS", groups.Count == 0 ? "none" : groups.Count + " group" + (groups.Count == 1 ? "" : "s")))
                return;
            if (GUILayout.Button(new GUIContent("Add Group from Selected Parts",
                    "Parts whose sprites change together (both eyes). Its first state holds the sprites they show now.")))
            {
                RecordPartsUndo("Add Sprite Group");
                int n = groups.Count + 1;
                string name = "Group " + n;
                while (groups.Exists(g => g != null && g.Name == name))
                    name = "Group " + ++n;
                var group = new SpritePartsSpriteGroupDef { Name = name, SlotIds = SelectedPartsInOrder(null) };
                group.States.Add(new SpritePartsSpriteGroupStateDef { Name = "Default", Bindings = CaptureGroupSprites(group) });
                groups.Add(group);
                SaveDirty();
            }
            if (groups.Count == 0)
            {
                EditorGUILayout.LabelField("Eyes open / closed, mouth shapes: one key or one call switches every part's sprite.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }
            var clip = ValueKeyClip;
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                if (group == null)
                    continue;
                group.SlotIds ??= new List<string>();
                group.States ??= new List<SpritePartsSpriteGroupStateDef>();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                string name = EditorGUILayout.TextField(group.Name);
                bool remove = GUILayout.Button(new GUIContent("×", "Remove this group (the sprites stay)"), GUILayout.Width(22f));
                EditorGUILayout.EndHorizontal();
                bool nameChanged = EditorGUI.EndChangeCheck();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(PartNames(group.SlotIds), EditorStyles.wordWrappedMiniLabel);
                bool setParts = GUILayout.Button(new GUIContent("Set", "The group becomes the selected parts"), EditorStyles.miniButton, GUILayout.Width(34f));
                EditorGUILayout.EndHorizontal();

                int keyedState = -1;
                if (clip != null && SpritePartsAuthoringOps.TrySampleValue(clip, SpritePartsValueKind.SpriteGroup, group.Name, _partsPreviewTime, out float kv))
                    keyedState = Mathf.RoundToInt(kv);
                _partsGroupPreview.TryGetValue(group.Name ?? string.Empty, out int shownState);
                bool showing = _partsGroupPreview.ContainsKey(group.Name ?? string.Empty);
                int removeState = -1, captureState = -1, keyState = -1, showState = -2;
                var stateNames = new string[group.States.Count];
                for (int s = 0; s < group.States.Count; s++)
                {
                    var state = group.States[s] ??= new SpritePartsSpriteGroupStateDef();
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(keyedState == s ? "●" : " ", GUILayout.Width(10f));
                    stateNames[s] = EditorGUILayout.TextField(state.Name);
                    bool show = GUILayout.Toggle(showing && shownState == s, new GUIContent("Show", "Preview this state on the canvas (not saved)"),
                        EditorStyles.miniButtonLeft, GUILayout.Width(42f));
                    if (show != (showing && shownState == s))
                        showState = show ? s : -1;
                    if (GUILayout.Button(new GUIContent("Capture", "This state takes the sprites the parts show now (set them, then Capture)"),
                            EditorStyles.miniButtonMid, GUILayout.Width(56f)))
                        captureState = s;
                    if (clip != null)
                    {
                        bool here = SpritePartsAuthoringOps.ValueKeyAt(clip, SpritePartsValueKind.SpriteGroup, group.Name, _partsPreviewTime) is var k
                                    && k != null && Mathf.RoundToInt(k.Value) == s;
                        if (GUILayout.Button(new GUIContent(here ? "◆" : "◇", here ? "Remove this key" : "Key this state at the playhead"),
                                EditorStyles.miniButtonMid, GUILayout.Width(22f)))
                            keyState = s;
                    }
                    if (GUILayout.Button(new GUIContent("×", "Remove this state"), EditorStyles.miniButtonRight, GUILayout.Width(20f)))
                        removeState = s;
                    EditorGUILayout.EndHorizontal();
                }
                bool addState = GUILayout.Button(new GUIContent("Add State", "A new state holding the sprites the parts show now"), EditorStyles.miniButton);
                // Auto blink: a state shown briefly at random times, in the game.
                EditorGUI.BeginChangeCheck();
                var blinkNames = new List<string> { "Auto blink: off" };
                foreach (var st in group.States)
                    blinkNames.Add("Blink to " + (st?.Name ?? "?"));
                int blinkChoice = EditorGUILayout.Popup(Mathf.Max(0, group.States.FindIndex(st => st != null && st.Name == group.BlinkState) + 1),
                    blinkNames.ToArray());
                Vector2 every = group.BlinkEvery;
                float close = group.BlinkClose;
                if (blinkChoice > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    float old = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 40f;
                    every.x = EditorGUILayout.FloatField(new GUIContent("Every", "Seconds between blinks: random between these"), every.x);
                    EditorGUIUtility.labelWidth = 14f;
                    every.y = EditorGUILayout.FloatField(new GUIContent("-"), every.y);
                    EditorGUIUtility.labelWidth = 40f;
                    close = EditorGUILayout.FloatField(new GUIContent("Hold", "Seconds the blink state shows"), close);
                    EditorGUIUtility.labelWidth = old;
                    EditorGUILayout.EndHorizontal();
                }
                if (EditorGUI.EndChangeCheck())
                {
                    RecordPartsUndo("Auto Blink");
                    group.BlinkState = blinkChoice > 0 ? group.States[blinkChoice - 1].Name : string.Empty;
                    group.BlinkEvery = new Vector2(Mathf.Max(0.05f, every.x), Mathf.Max(0.05f, every.y));
                    group.BlinkClose = Mathf.Max(0.01f, close);
                    SaveDirty();
                }
                EditorGUILayout.EndVertical();

                if (showState != -2)
                {
                    if (showState < 0)
                        _partsGroupPreview.Remove(group.Name ?? string.Empty);
                    else
                        _partsGroupPreview[group.Name ?? string.Empty] = showState;
                    Repaint();
                }
                bool statesRenamed = false;
                for (int s = 0; s < stateNames.Length; s++)
                    statesRenamed |= stateNames[s] != group.States[s].Name;
                if (!nameChanged && !remove && !setParts && !statesRenamed && removeState < 0 && captureState < 0 && keyState < 0 && !addState)
                    continue;
                RecordPartsUndo(remove ? "Remove Sprite Group" : "Edit Sprite Group");
                if (remove)
                {
                    SpritePartsAuthoringOps.RemoveValueTracks(_profile, group.Name, SpritePartsValueKind.SpriteGroup);
                    _partsGroupPreview.Remove(group.Name ?? string.Empty);
                    groups.RemoveAt(g);
                    SaveDirty();
                    break;
                }
                if (name != group.Name)
                {
                    SpritePartsAuthoringOps.RenameValueTarget(_profile, group.Name, name, SpritePartsValueKind.SpriteGroup);
                    if (_partsGroupPreview.TryGetValue(group.Name ?? string.Empty, out int carried))
                    {
                        _partsGroupPreview.Remove(group.Name ?? string.Empty);
                        _partsGroupPreview[name] = carried;
                    }
                    group.Name = name;
                }
                for (int s = 0; s < stateNames.Length; s++)
                    group.States[s].Name = stateNames[s];
                if (setParts)
                    group.SlotIds = SelectedPartsInOrder(null);
                if (captureState >= 0)
                {
                    _partsGroupPreview.Remove(group.Name ?? string.Empty);
                    group.States[captureState].Bindings = CaptureGroupSprites(group);
                }
                if (addState)
                {
                    _partsGroupPreview.Remove(group.Name ?? string.Empty);
                    group.States.Add(new SpritePartsSpriteGroupStateDef { Name = "State " + (group.States.Count + 1), Bindings = CaptureGroupSprites(group) });
                }
                if (keyState >= 0 && clip != null)
                {
                    var existing = SpritePartsAuthoringOps.ValueKeyAt(clip, SpritePartsValueKind.SpriteGroup, group.Name, _partsPreviewTime);
                    if (existing != null && Mathf.RoundToInt(existing.Value) == keyState)
                        SpritePartsAuthoringOps.RemoveValueKey(clip, SpritePartsValueKind.SpriteGroup, group.Name, _partsPreviewTime);
                    else
                        SpritePartsAuthoringOps.SetValueKey(clip, SpritePartsValueKind.SpriteGroup, group.Name, _partsPreviewTime, keyState);
                }
                if (removeState >= 0)
                    RemoveGroupState(group, removeState);
                SaveDirty();
                Repaint();
            }
        }

        /// <summary>Removes a state; keys of it go, keys of later states shift down so they keep their state.</summary>
        void RemoveGroupState(SpritePartsSpriteGroupDef group, int state)
        {
            group.States.RemoveAt(state);
            _partsGroupPreview.Remove(group.Name ?? string.Empty);
            foreach (var clip in _profile.PartsClips)
            {
                var track = SpritePartsAuthoringOps.FindValueTrack(clip, SpritePartsValueKind.SpriteGroup, group.Name);
                if (track?.Keys == null)
                    continue;
                track.Keys.RemoveAll(k => k == null || Mathf.RoundToInt(k.Value) == state);
                foreach (var k in track.Keys)
                    if (Mathf.RoundToInt(k.Value) > state)
                        k.Value -= 1f;
                if (track.Keys.Count == 0)
                    clip.ValueTracks.Remove(track);
            }
        }

        void DrawPartsLayerPreviewInspector()
        {
            if (_profile == null || _partsMode != SpritePartsStudioMode.Animate)
            {
                SpritePartsOnion.PreviewLayer.Active = false;
                return;
            }
            ref var layer = ref SpritePartsOnion.PreviewLayer;
            if (!PartsSection("LAYER PREVIEW", layer.Active && layer.ClipIndex >= 0 && layer.ClipIndex < _profile.PartsClips.Count
                    ? _profile.PartsClips[layer.ClipIndex]?.Name + " on top"
                    : "off"))
                return;
            var clipNames = new List<string> { "(off)" };
            foreach (var c in _profile.PartsClips)
                clipNames.Add(c != null ? c.Name : "?");
            var maskNames = new List<string> { "(the parts it keys)" };
            foreach (var m in _profile.PartsMasks ?? new List<SpritePartsMaskDef>())
                if (m != null)
                    maskNames.Add(m.Name);
            EditorGUI.BeginChangeCheck();
            int clipChoice = EditorGUILayout.Popup(new GUIContent("Clip", "Played over the open clip like SpriteParts.PlayLayer (Shoot over Run)"),
                layer.Active ? layer.ClipIndex + 1 : 0, clipNames.ToArray());
            int maskChoice = EditorGUILayout.Popup(new GUIContent("Mask", "Limit it to a named part set"),
                Mathf.Max(0, maskNames.IndexOf(layer.Mask ?? string.Empty)), maskNames.ToArray());
            float start = EditorGUILayout.FloatField(new GUIContent("Starts at", "Seconds into the open clip where it starts"), layer.Start);
            bool additive = EditorGUILayout.ToggleLeft(new GUIContent("Additive", "Adds its change from the setup pose"), layer.Additive);
            if (EditorGUI.EndChangeCheck())
            {
                layer.Active = clipChoice > 0;
                layer.ClipIndex = clipChoice - 1;
                layer.Mask = maskChoice > 0 ? maskNames[maskChoice] : string.Empty;
                layer.Start = Mathf.Max(0f, start);
                layer.Additive = additive;
                Repaint();
            }
            if (layer.Active)
                EditorGUILayout.LabelField("Preview only: keys you set still go to the open clip.", EditorStyles.wordWrappedMiniLabel);
        }
    }
}
