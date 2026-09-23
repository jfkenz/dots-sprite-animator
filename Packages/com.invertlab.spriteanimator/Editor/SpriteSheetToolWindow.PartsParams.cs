using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // PARAMETERS (AnyPortrait control parameters): a value from Min to Max scrubs a clip from its start to its end.
    //   Add Parameter   makes the parameter and its own 1 s clip: key the Min pose at the start, the Max pose at the end
    //   per row         clip / range / default / Add or Replace / preview slider / Edit Clip
    // In the game: SpritePartsParams.Set(entityManager, character, "Mouth", 0.7f).
    public sealed partial class SpriteSheetToolWindow
    {
        void AddPartsParam()
        {
            RecordPartsUndo("Add Parameter");
            _profile.EnsurePartsRig();
            _profile.PartsParams ??= new List<SpritePartsParamDef>();
            int n = _profile.PartsParams.Count + 1;
            string name = "Param " + n;
            while (_profile.PartsParams.Exists(p => p != null && p.Name == name))
                name = "Param " + ++n;
            string clipId = "param." + n;
            while (SpritePartsAuthoringOps.FindClipIndex(_profile, clipId) >= 0)
                clipId = "param." + ++n;
            _profile.PartsClips.Add(new SpritePartsClipDef
            {
                Name = name,
                ClipId = clipId,
                Duration = 1f,
                Speed = 1f,
                WrapMode = (byte)SpritePartsWrap.Once,
                Tracks = new List<SpritePartsTrackDef>(),
            });
            SpritePartsValidation.CanonicalizeIds(_profile);
            var clip = _profile.PartsClips[_profile.PartsClips.Count - 1];
            _profile.PartsParams.Add(new SpritePartsParamDef { Name = name, ClipId = clip.ClipId });
            SaveDirty();
            EditPartsParamClip(_profile.PartsClips.Count - 1);
            _status = "Parameter '" + name + "' added with its clip: key the Min pose at the start and the Max pose at the end.";
        }

        void EditPartsParamClip(int clipIndex)
        {
            if (clipIndex < 0 || clipIndex >= _profile.PartsClips.Count)
                return;
            _partsSelectedClip = clipIndex;
            _partsBrowserFocus = PartsBrowserFocus.Clips;
            _partsPreviewTime = 0f;
            SwitchPartsMode(SpritePartsStudioMode.Animate, "Edit Parameter Clip");
            Repaint();
        }

        void DrawPartsParamsInspector()
        {
            if (_profile == null)
                return;
            GUILayout.Space(6f);
            GUILayout.Label("PARAMETERS", _sectionStyle);
            var list = _profile.PartsParams ??= new List<SpritePartsParamDef>();
            if (GUILayout.Button(new GUIContent("Add Parameter",
                    "A slider that scrubs its own clip: mouth open / closed, head turn, blink. Set it from gameplay.")))
                AddPartsParam();
            if (list.Count == 0)
            {
                EditorGUILayout.LabelField("A value (e.g. 0..1) blends poses keyed in a clip. Gameplay sets it: SpritePartsParams.Set(...).",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }
            var clipNames = new List<string> { "(none)" };
            var clipIds = new List<string> { string.Empty };
            foreach (var c in _profile.PartsClips)
            {
                if (c == null)
                    continue;
                clipNames.Add(c.Name);
                clipIds.Add(SpritePartIdUtility.Canonical(c.ClipId));
            }
            for (int k = 0; k < list.Count; k++)
            {
                var p = list[k];
                if (p == null)
                    continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                string name = EditorGUILayout.TextField(p.Name);
                bool remove = GUILayout.Button(new GUIContent("×", "Remove this parameter (its clip stays)"), GUILayout.Width(22f));
                EditorGUILayout.EndHorizontal();
                int clip = EditorGUILayout.Popup(new GUIContent("Clip", "Start of the clip = Min, end = Max"),
                    Mathf.Max(0, clipIds.IndexOf(SpritePartIdUtility.Canonical(p.ClipId ?? string.Empty))), clipNames.ToArray());
                EditorGUILayout.BeginHorizontal();
                float oldLabel = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 30f;
                float min = EditorGUILayout.FloatField("Min", p.Min);
                float max = EditorGUILayout.FloatField("Max", p.Max);
                EditorGUIUtility.labelWidth = 48f;
                float def = EditorGUILayout.FloatField(new GUIContent("Default", "The value with no change"), p.Default);
                EditorGUIUtility.labelWidth = oldLabel;
                EditorGUILayout.EndHorizontal();
                int mode = GUILayout.Toolbar(p.Additive ? 0 : 1, new[]
                {
                    new GUIContent("Add", "Adds the change from the Default pose on top of any clip (talk while walking)"),
                    new GUIContent("Replace", "The parts this clip keys take its pose, whatever else plays"),
                }, EditorStyles.miniButton);
                bool changed = EditorGUI.EndChangeCheck();

                // Preview: not saved, only moves the canvas.
                float lo = Mathf.Min(p.Min, p.Max), hi = Mathf.Max(p.Min, p.Max);
                SpritePartsOnion.PreviewParams.TryGetValue(p.Name ?? string.Empty, out float shown);
                if (!SpritePartsOnion.PreviewParams.ContainsKey(p.Name ?? string.Empty))
                    shown = p.Default;
                EditorGUILayout.BeginHorizontal();
                float preview = EditorGUILayout.Slider(new GUIContent("Preview", "Try the value on the canvas (not saved)"),
                    Mathf.Clamp(shown, lo, hi), lo, hi);
                if (!Mathf.Approximately(preview, shown))
                {
                    SpritePartsOnion.PreviewParams[p.Name ?? string.Empty] = preview;
                    Repaint();
                }
                if (GUILayout.Button(new GUIContent("Edit Clip", "Open this parameter's clip to key its poses"), EditorStyles.miniButton, GUILayout.Width(62f)))
                    EditPartsParamClip(SpritePartsAuthoringOps.FindClipIndex(_profile, p.ClipId));
                EditorGUILayout.EndHorizontal();
                string hint = ParamHint(p);
                if (!string.IsNullOrEmpty(hint))
                    EditorGUILayout.LabelField(hint, EditorStyles.wordWrappedMiniLabel);

                if (changed)
                {
                    RecordPartsUndo(remove ? "Remove Parameter" : "Edit Parameter");
                    if (remove)
                    {
                        SpritePartsOnion.PreviewParams.Remove(p.Name ?? string.Empty);
                        list.RemoveAt(k);
                        SaveDirty();
                        EditorGUILayout.EndVertical();
                        break;
                    }
                    if (name != p.Name && SpritePartsOnion.PreviewParams.TryGetValue(p.Name ?? string.Empty, out float carried))
                    {
                        SpritePartsOnion.PreviewParams.Remove(p.Name ?? string.Empty);
                        SpritePartsOnion.PreviewParams[name] = carried;
                    }
                    p.Name = name;
                    p.ClipId = clipIds[clip];
                    p.Min = min;
                    p.Max = max;
                    p.Default = def;
                    p.Additive = mode == 0;
                    SaveDirty();
                }
                EditorGUILayout.EndVertical();
            }
        }

        string ParamHint(SpritePartsParamDef p)
        {
            if (Mathf.Abs(p.Max - p.Min) < 1e-6f)
                return "Min and Max must differ.";
            int index = SpritePartsAuthoringOps.FindClipIndex(_profile, p.ClipId ?? string.Empty);
            if (index < 0)
                return "Pick a clip. Its start is the Min pose, its end the Max pose.";
            if (index == _partsSelectedClip && _partsMode == SpritePartsStudioMode.Animate)
                return "Editing this parameter's clip: the preview slider is off here.";
            if ((_profile.PartsParams?.FindAll(q => q != null && q.Name == p.Name).Count ?? 0) > 1)
                return "Two parameters share this name: gameplay can only reach the first.";
            return null;
        }
    }
}
