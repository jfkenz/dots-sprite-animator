using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // TRANSITIONS (Spine mix times): how clips crossfade when gameplay switches them.
    //   Default / Ease     every clip change fades this long unless a pair below says otherwise
    //   Mix rows           From (or Any) -> To: seconds and ease
    //   Fading-out events  the clip fading out keeps firing its events
    //   Preview            From / To / start: scrub or play the crossfade on the canvas
    // MASKS: named part sets for layers  -> SpriteParts.SetLayer(em, e, "Wave", 1f, "Arms").
    // BLEND SPACES: clips on a value line -> SpriteParts.PlayBlend(em, e, "Locomotion", speed).
    public sealed partial class SpriteSheetToolWindow
    {
        const float MixPreviewLead = 0.4f;
        const float MixPreviewTail = 0.4f;

        static string[] s_easeNames;
        static byte[] s_easeValues;

        [NonSerialized] bool _mixPreviewPlaying;
        [NonSerialized] string _mixPreviewFromId = string.Empty;
        [NonSerialized] string _mixPreviewToId = string.Empty;
        [NonSerialized] float _mixPreviewStart;

        static void EnsureEaseNames()
        {
            if (s_easeNames != null)
                return;
            var names = new List<string>();
            var values = new List<byte>();
            foreach (SpriteEaseMode mode in Enum.GetValues(typeof(SpriteEaseMode)))
            {
                if (mode == SpriteEaseMode.Bezier)
                    continue;
                names.Add(ObjectNames.NicifyVariableName(mode.ToString()));
                values.Add((byte)mode);
            }
            s_easeNames = names.ToArray();
            s_easeValues = values.ToArray();
        }

        static byte EasePopup(byte ease, params GUILayoutOption[] options)
        {
            EnsureEaseNames();
            int index = Mathf.Max(0, Array.IndexOf(s_easeValues, ease));
            return s_easeValues[EditorGUILayout.Popup(index, s_easeNames, options)];
        }

        /// <summary>Clip names and canonical ids for popups; with <paramref name="anyLabel"/> first (empty id).</summary>
        void PartsClipChoices(string anyLabel, out string[] names, out List<string> ids)
        {
            var list = new List<string>();
            ids = new List<string>();
            if (anyLabel != null)
            {
                list.Add(anyLabel);
                ids.Add(string.Empty);
            }
            foreach (var c in _profile.PartsClips)
            {
                if (c == null)
                    continue;
                list.Add(c.Name);
                ids.Add(SpritePartIdUtility.Canonical(c.ClipId ?? string.Empty));
            }
            names = list.ToArray();
        }

        static int ChoiceIndex(List<string> ids, string id)
            => Mathf.Max(0, ids.IndexOf(SpritePartIdUtility.Canonical(id ?? string.Empty)));

        /// <summary>The crossfade the game uses from one clip into another (pair, then any, then the default).</summary>
        void FindProfileMix(string fromId, string toId, out float seconds, out byte ease, out string source)
        {
            string from = SpritePartIdUtility.Canonical(fromId ?? string.Empty);
            string to = SpritePartIdUtility.Canonical(toId ?? string.Empty);
            SpritePartsMixDef any = null;
            foreach (var m in _profile.PartsMixes)
            {
                if (m == null || SpritePartIdUtility.Canonical(m.ToClipId ?? string.Empty) != to)
                    continue;
                string mFrom = SpritePartIdUtility.Canonical(m.FromClipId ?? string.Empty);
                if (mFrom.Length > 0 && mFrom == from)
                {
                    seconds = m.Duration;
                    ease = m.Ease;
                    source = "pair";
                    return;
                }
                if (mFrom.Length == 0 && any == null)
                    any = m;
            }
            if (any != null)
            {
                seconds = any.Duration;
                ease = any.Ease;
                source = "any";
                return;
            }
            seconds = _profile.PartsDefaultMix;
            ease = _profile.PartsDefaultMixEase;
            source = "default";
        }

        void DrawPartsTransitionsInspector()
        {
            if (_profile == null)
                return;
            var mixes = _profile.PartsMixes ??= new List<SpritePartsMixDef>();
            string summary = "default " + _profile.PartsDefaultMix.ToString("0.##") + "s"
                             + (mixes.Count > 0 ? ", " + mixes.Count + " pair" + (mixes.Count == 1 ? "" : "s") : "");
            if (!PartsSection("TRANSITIONS", summary))
            {
                StopMixPreview();
                return;
            }
            PartsClipChoices("Any clip", out var fromNames, out var fromIds);
            PartsClipChoices(null, out var toNames, out var toIds);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            float defaultMix = EditorGUILayout.FloatField(new GUIContent("Default Mix",
                "Seconds every clip change crossfades when no pair below matches. 0 = cut."), _profile.PartsDefaultMix);
            byte defaultEase = EasePopup(_profile.PartsDefaultMixEase, GUILayout.Width(84f));
            EditorGUILayout.EndHorizontal();
            bool fadeOutEvents = EditorGUILayout.ToggleLeft(new GUIContent("Fading-out clip fires events",
                "During a crossfade the clip fading out still fires its events (footsteps keep sounding)."),
                _profile.PartsFadeOutEvents);
            if (EditorGUI.EndChangeCheck())
            {
                RecordPartsUndo("Edit Default Mix");
                _profile.PartsDefaultMix = Mathf.Max(0f, defaultMix);
                _profile.PartsDefaultMixEase = defaultEase;
                _profile.PartsFadeOutEvents = fadeOutEvents;
                SaveDirty();
            }

            for (int k = 0; k < mixes.Count; k++)
            {
                var m = mixes[k];
                if (m == null)
                    continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                int from = EditorGUILayout.Popup(ChoiceIndex(fromIds, m.FromClipId), fromNames);
                GUILayout.Label("→", GUILayout.Width(14f));
                int to = EditorGUILayout.Popup(ChoiceIndex(toIds, m.ToClipId), toNames);
                bool remove = GUILayout.Button(new GUIContent("×", "Remove this mix"), GUILayout.Width(22f));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                float old = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 52f;
                float seconds = EditorGUILayout.FloatField(new GUIContent("Seconds", "Crossfade length"), m.Duration);
                EditorGUIUtility.labelWidth = old;
                byte ease = EasePopup(m.Ease, GUILayout.Width(84f));
                if (GUILayout.Button(new GUIContent("Preview", "Show this crossfade on the canvas"), EditorStyles.miniButton,
                        GUILayout.Width(52f)))
                {
                    _mixPreviewFromId = m.FromClipId;
                    _mixPreviewToId = m.ToClipId;
                    StartMixPreview();
                }
                EditorGUILayout.EndHorizontal();
                bool changed = EditorGUI.EndChangeCheck();
                EditorGUILayout.EndVertical();
                if (!changed)
                    continue;
                RecordPartsUndo(remove ? "Remove Mix" : "Edit Mix");
                if (remove)
                {
                    mixes.RemoveAt(k);
                    SaveDirty();
                    break;
                }
                m.FromClipId = fromIds[from];
                m.ToClipId = toIds.Count > 0 ? toIds[to] : string.Empty;
                m.Duration = Mathf.Max(0f, seconds);
                m.Ease = ease;
                SaveDirty();
            }
            using (new EditorGUI.DisabledScope(toIds.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("Add Mix", "A crossfade time for one clip pair (Idle → Run 0.15 s)")))
                {
                    RecordPartsUndo("Add Mix");
                    mixes.Add(new SpritePartsMixDef
                    {
                        FromClipId = CurrentPartsClip != null ? SpritePartIdUtility.Canonical(CurrentPartsClip.ClipId) : string.Empty,
                        ToClipId = toIds[0],
                        Duration = _profile.PartsDefaultMix > 0f ? _profile.PartsDefaultMix : 0.2f,
                    });
                    SaveDirty();
                }
            }
            EditorGUILayout.LabelField("SpriteParts.Play(em, e, \"Run\") uses these. A time passed in replaces them; 0 cuts.",
                EditorStyles.wordWrappedMiniLabel);
            DrawPartsMixPreview(fromNames, fromIds, toNames, toIds);
        }

        void DrawPartsMixPreview(string[] fromNames, List<string> fromIds, string[] toNames, List<string> toIds)
        {
            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Preview", EditorStyles.miniBoldLabel);
            if (toIds.Count < 1)
            {
                EditorGUILayout.LabelField("Add clips to preview a crossfade.", EditorStyles.wordWrappedMiniLabel);
                return;
            }
            if (string.IsNullOrEmpty(_mixPreviewToId) || !toIds.Contains(SpritePartIdUtility.Canonical(_mixPreviewToId)))
                _mixPreviewToId = toIds[Mathf.Min(1, toIds.Count - 1)];
            if (string.IsNullOrEmpty(_mixPreviewFromId) || !toIds.Contains(SpritePartIdUtility.Canonical(_mixPreviewFromId)))
                _mixPreviewFromId = toIds[0];
            EditorGUILayout.BeginHorizontal();
            int from = EditorGUILayout.Popup(ChoiceIndex(toIds, _mixPreviewFromId), toNames);
            GUILayout.Label("→", GUILayout.Width(14f));
            int to = EditorGUILayout.Popup(ChoiceIndex(toIds, _mixPreviewToId), toNames);
            EditorGUILayout.EndHorizontal();
            _mixPreviewFromId = toIds[from];
            _mixPreviewToId = toIds[to];
            _mixPreviewStart = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Start in From",
                "Where the first clip is when the switch happens (seconds)"), _mixPreviewStart));
            FindProfileMix(_mixPreviewFromId, _mixPreviewToId, out float seconds, out byte ease, out string source);
            EditorGUILayout.LabelField("Fade", seconds.ToString("0.###") + " s (" + source + ")", EditorStyles.miniLabel);

            bool active = SpritePartsOnion.PreviewMix.Active && _partsMode == SpritePartsStudioMode.Animate;
            bool playing = _mixPreviewPlaying && active;
            EditorGUILayout.BeginHorizontal();
            bool show = GUILayout.Toggle(active, new GUIContent("Show", "Show the crossfade on the canvas instead of the clip"),
                EditorStyles.miniButtonLeft, GUILayout.Width(48f));
            bool play = GUILayout.Toggle(playing, new GUIContent(playing ? "Pause" : "Play",
                "Play the switch: the first clip, the fade, then the second clip"), EditorStyles.miniButtonRight, GUILayout.Width(48f));
            float shownTime = SpritePartsOnion.PreviewMix.Time;
            float time = GUILayout.HorizontalSlider(shownTime, -MixPreviewLead, seconds + MixPreviewTail);
            EditorGUILayout.EndHorizontal();

            if (show != active)
            {
                if (show)
                    StartMixPreview();
                else
                    StopMixPreview();
            }
            else if (play != playing)
            {
                if (!active)
                    StartMixPreview();
                else
                    _mixPreviewPlaying = play;
            }
            else if (active && !Mathf.Approximately(time, shownTime))
            {
                _mixPreviewPlaying = false;
                SpritePartsOnion.PreviewMix.Time = time;
                Repaint();
            }
            if (SpritePartsOnion.PreviewMix.Active)
            {
                SyncMixPreview(seconds, ease);
                EditorGUILayout.LabelField("The canvas shows the crossfade. Turn Show off to edit keys again.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        void SyncMixPreview(float seconds, byte ease)
        {
            ref var mix = ref SpritePartsOnion.PreviewMix;
            mix.From = SpritePartsAuthoringOps.FindClipIndex(_profile, _mixPreviewFromId);
            mix.To = SpritePartsAuthoringOps.FindClipIndex(_profile, _mixPreviewToId);
            mix.FromStart = _mixPreviewStart;
            mix.Duration = seconds;
            mix.Ease = ease;
        }

        void StartMixPreview()
        {
            if (_partsMode != SpritePartsStudioMode.Animate)
                SwitchPartsMode(SpritePartsStudioMode.Animate, "Preview Mix");
            FindProfileMix(_mixPreviewFromId, _mixPreviewToId, out float seconds, out byte ease, out _);
            ref var mix = ref SpritePartsOnion.PreviewMix;
            mix.Active = true;
            mix.Time = -MixPreviewLead;
            SyncMixPreview(seconds, ease);
            _partsPlaying = false;
            _mixPreviewPlaying = true;
            Repaint();
        }

        void StopMixPreview()
        {
            if (!SpritePartsOnion.PreviewMix.Active && !_mixPreviewPlaying)
                return;
            SpritePartsOnion.PreviewMix.Active = false;
            _mixPreviewPlaying = false;
            Repaint();
        }

        /// <summary>Advances a playing crossfade preview; loops with a short lead-in and tail. True when it moved.</summary>
        bool TickPartsMixPreview(float delta)
        {
            ref var mix = ref SpritePartsOnion.PreviewMix;
            if (!mix.Active)
                return false;
            if (_partsMode != SpritePartsStudioMode.Animate || _profile == null)
            {
                StopMixPreview();
                return true;
            }
            if (!_mixPreviewPlaying)
                return false;
            mix.Time += delta * Mathf.Max(0.05f, _speed);
            if (mix.Time > mix.Duration + MixPreviewTail)
                mix.Time = -MixPreviewLead;
            return true;
        }

        // ---- Masks ----

        void DrawPartsMasksInspector()
        {
            if (_profile == null)
                return;
            var masks = _profile.PartsMasks ??= new List<SpritePartsMaskDef>();
            if (!PartsSection("MASKS", masks.Count == 0 ? "none" : masks.Count + " mask" + (masks.Count == 1 ? "" : "s")))
                return;
            var selection = SelectedSlotIdsForMask();
            for (int k = 0; k < masks.Count; k++)
            {
                var mask = masks[k];
                if (mask == null)
                    continue;
                mask.SlotIds ??= new List<string>();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                string name = EditorGUILayout.TextField(mask.Name);
                bool remove = GUILayout.Button(new GUIContent("×", "Remove this mask"), GUILayout.Width(22f));
                EditorGUILayout.EndHorizontal();
                bool nameChanged = EditorGUI.EndChangeCheck();
                EditorGUILayout.LabelField(MaskPartsLabel(mask), EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.BeginHorizontal();
                bool set, add, sub;
                using (new EditorGUI.DisabledScope(selection.Count == 0))
                {
                    set = GUILayout.Button(new GUIContent("Set", "The mask becomes the selected parts"), EditorStyles.miniButtonLeft);
                    add = GUILayout.Button(new GUIContent("Add", "Add the selected parts"), EditorStyles.miniButtonMid);
                    sub = GUILayout.Button(new GUIContent("Remove", "Take the selected parts out"), EditorStyles.miniButtonMid);
                }
                bool select = GUILayout.Button(new GUIContent("Select", "Select the mask's parts in the tree"), EditorStyles.miniButtonRight);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();

                if (select)
                    SelectMaskParts(mask);
                if (!nameChanged && !set && !add && !sub)
                    continue;
                RecordPartsUndo(remove ? "Remove Mask" : "Edit Mask");
                if (remove)
                {
                    masks.RemoveAt(k);
                    SaveDirty();
                    break;
                }
                mask.Name = name;
                if (set)
                    mask.SlotIds.Clear();
                foreach (string id in selection)
                {
                    if (sub)
                        mask.SlotIds.RemoveAll(s => SpritePartIdUtility.Canonical(s ?? string.Empty) == id);
                    else if (!mask.SlotIds.Exists(s => SpritePartIdUtility.Canonical(s ?? string.Empty) == id))
                        mask.SlotIds.Add(id);
                }
                SaveDirty();
            }
            if (GUILayout.Button(new GUIContent("Add Mask", "A named set of parts: a layer can play on just these (arms, upper body)")))
            {
                RecordPartsUndo("Add Mask");
                int n = masks.Count + 1;
                string name = "Mask " + n;
                while (masks.Exists(m => m != null && m.Name == name))
                    name = "Mask " + ++n;
                masks.Add(new SpritePartsMaskDef { Name = name, SlotIds = new List<string>(selection) });
                SaveDirty();
            }
            EditorGUILayout.LabelField("Select parts, then Set. Layers: SpriteParts.SetLayer(em, e, \"Wave\", 1f, \"Arms\").",
                EditorStyles.wordWrappedMiniLabel);
        }

        List<string> SelectedSlotIdsForMask()
        {
            var ids = new List<string>();
            foreach (string id in _partsSelectedSlotIds)
                ids.Add(SpritePartIdUtility.Canonical(id));
            var current = CurrentPartsSlot;
            if (current != null)
            {
                string id = SpritePartIdUtility.Canonical(current.SlotId);
                if (!ids.Contains(id))
                    ids.Add(id);
            }
            return ids;
        }

        string MaskPartsLabel(SpritePartsMaskDef mask)
        {
            if (mask.SlotIds.Count == 0)
                return "No parts yet: select parts and press Set.";
            var names = new List<string>();
            bool beyond = false;
            foreach (string id in mask.SlotIds)
            {
                int index = _profile.PartsSlots.FindIndex(s => s != null
                    && SpritePartIdUtility.Canonical(s.SlotId) == SpritePartIdUtility.Canonical(id ?? string.Empty));
                if (index < 0)
                    continue;
                names.Add(_profile.PartsSlots[index].Name);
                beyond |= index >= 32;
            }
            string label = names.Count + " part" + (names.Count == 1 ? "" : "s") + ": " + string.Join(", ", names);
            return beyond ? label + "\nOnly the first 32 parts can be masked; later ones are ignored." : label;
        }

        void SelectMaskParts(SpritePartsMaskDef mask)
        {
            _partsSelectedSlotIds.Clear();
            foreach (string id in mask.SlotIds)
                if (!string.IsNullOrEmpty(id))
                    _partsSelectedSlotIds.Add(SpritePartIdUtility.Canonical(id));
            Repaint();
        }

        // ---- Blend spaces ----

        void DrawPartsBlendSpacesInspector()
        {
            if (_profile == null)
                return;
            var spaces = _profile.PartsBlendSpaces ??= new List<SpritePartsBlendSpaceDef>();
            if (!PartsSection("BLEND SPACES", spaces.Count == 0 ? "none" : spaces.Count + " space" + (spaces.Count == 1 ? "" : "s")))
                return;
            PartsClipChoices(null, out var clipNames, out var clipIds);
            for (int k = 0; k < spaces.Count; k++)
            {
                var space = spaces[k];
                if (space == null)
                    continue;
                space.Points ??= new List<SpritePartsBlendPointDef>();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                string name = EditorGUILayout.TextField(space.Name);
                bool remove = GUILayout.Button(new GUIContent("×", "Remove this blend space"), GUILayout.Width(22f));
                EditorGUILayout.EndHorizontal();
                int removePoint = -1;
                var clips = new int[space.Points.Count];
                var values = new float[space.Points.Count];
                for (int p = 0; p < space.Points.Count; p++)
                {
                    var point = space.Points[p] ??= new SpritePartsBlendPointDef();
                    EditorGUILayout.BeginHorizontal();
                    clips[p] = clipIds.Count > 0 ? EditorGUILayout.Popup(ChoiceIndex(clipIds, point.ClipId), clipNames) : 0;
                    float old = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 18f;
                    values[p] = EditorGUILayout.FloatField(new GUIContent("at", "The value where this clip plays alone"),
                        point.Value, GUILayout.Width(70f));
                    EditorGUIUtility.labelWidth = old;
                    if (GUILayout.Button(new GUIContent("×", "Remove this clip"), GUILayout.Width(22f)))
                        removePoint = p;
                    EditorGUILayout.EndHorizontal();
                }
                bool addPoint = GUILayout.Button(new GUIContent("Add Clip", "Place another clip on the value line"), EditorStyles.miniButton);
                bool changed = EditorGUI.EndChangeCheck();
                string hint = BlendSpaceHint(space);
                if (!string.IsNullOrEmpty(hint))
                    EditorGUILayout.LabelField(hint, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
                if (!changed && !addPoint && removePoint < 0)
                    continue;
                RecordPartsUndo(remove ? "Remove Blend Space" : "Edit Blend Space");
                if (remove)
                {
                    spaces.RemoveAt(k);
                    SaveDirty();
                    break;
                }
                space.Name = name;
                for (int p = 0; p < space.Points.Count; p++)
                {
                    if (clipIds.Count > 0)
                        space.Points[p].ClipId = clipIds[clips[p]];
                    space.Points[p].Value = values[p];
                }
                if (removePoint >= 0)
                    space.Points.RemoveAt(removePoint);
                if (addPoint && clipIds.Count > 0)
                {
                    float next = space.Points.Count == 0 ? 0f : space.Points[space.Points.Count - 1].Value + 1f;
                    string clip = CurrentPartsClip != null ? SpritePartIdUtility.Canonical(CurrentPartsClip.ClipId) : clipIds[0];
                    space.Points.Add(new SpritePartsBlendPointDef { ClipId = clip, Value = next });
                }
                SaveDirty();
            }
            if (GUILayout.Button(new GUIContent("Add Blend Space",
                    "Clips on a value line: walk at 0, run at 1. A speed between blends them with their steps in sync.")))
            {
                RecordPartsUndo("Add Blend Space");
                int n = spaces.Count + 1;
                string name = "Blend " + n;
                while (spaces.Exists(s => s != null && s.Name == name))
                    name = "Blend " + ++n;
                spaces.Add(new SpritePartsBlendSpaceDef { Name = name });
                SaveDirty();
            }
            EditorGUILayout.LabelField("SpriteParts.PlayBlend(em, e, \"Locomotion\", speed), then SetBlendValue each frame.",
                EditorStyles.wordWrappedMiniLabel);
        }

        string BlendSpaceHint(SpritePartsBlendSpaceDef space)
        {
            if (space.Points.Count < 2)
                return "Add two or more clips (walk at 0, run at 1).";
            var seen = new HashSet<float>();
            foreach (var p in space.Points)
                if (p != null && !seen.Add(p.Value))
                    return "Two clips share a value: give each its own.";
            foreach (var p in space.Points)
            {
                int index = p != null ? SpritePartsAuthoringOps.FindClipIndex(_profile, p.ClipId ?? string.Empty) : -1;
                if (index >= 0 && _profile.PartsClips[index].WrapMode == (byte)SpritePartsWrap.Once)
                    return "'" + _profile.PartsClips[index].Name + "' plays once: blend spaces are for loops.";
            }
            return null;
        }
    }
}
