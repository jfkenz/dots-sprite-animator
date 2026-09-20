using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>
    /// Explicit Retarget Parts Clip: pick source clip + slot map + rest-delta
    /// mode, preview on the destination rest/hierarchy, Apply as one Undo.
    /// </summary>
    sealed class SpritePartsClipRetargetPopup : PopupWindowContent
    {
        readonly SpriteSheetToolWindow _host;
        ScriptableSpriteSheetProfile _source;
        int _sourceClipIndex;
        Dictionary<string, string> _slotMap = new Dictionary<string, string>(StringComparer.Ordinal);
        bool _slotMapSeeded;
        SpritePartsClipRetarget.RestDeltaMode _mode = SpritePartsClipRetarget.RestDeltaMode.RestDelta;
        Vector2 _scroll;

        public SpritePartsClipRetargetPopup(SpriteSheetToolWindow host)
        {
            _host = host;
            _source = host.ProfileAsset;
        }

        public override Vector2 GetWindowSize() => new Vector2(460f, 480f);

        public override void OnClose() => _host.ClearImportPreview();

        public override void OnGUI(Rect rect)
        {
            var inner = new Rect(8f, 8f, rect.width - 16f, rect.height - 16f);
            GUILayout.BeginArea(inner);
            GUILayout.Label("RETARGET PARTS CLIP", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Adapts keyed local TRS to a different rest/hierarchy. This is not Spine-style retargeting, IK, or mesh deformation. Destination rest and hierarchy are never rewritten.",
                MessageType.Info);

            _source = (ScriptableSpriteSheetProfile)EditorGUILayout.ObjectField(
                "Source Profile", _source, typeof(ScriptableSpriteSheetProfile), false);
            var sourceData = _source != null ? _source.Data : null;
            var dest = _host.EditingProfile;
            if (sourceData?.PartsClips == null || sourceData.PartsClips.Count == 0 || dest == null)
            {
                GUILayout.Label("Pick a source profile that has Parts clips.", EditorStyles.miniLabel);
                if (GUILayout.Button("Cancel"))
                    editorWindow.Close();
                GUILayout.EndArea();
                return;
            }

            var names = new string[sourceData.PartsClips.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = sourceData.PartsClips[i]?.Name ?? ("Clip " + i);
            _sourceClipIndex = Mathf.Clamp(_sourceClipIndex, 0, names.Length - 1);
            int nextClip = EditorGUILayout.Popup("Source Clip", _sourceClipIndex, names);
            if (nextClip != _sourceClipIndex)
            {
                _sourceClipIndex = nextClip;
                _slotMapSeeded = false;
            }

            _mode = (SpritePartsClipRetarget.RestDeltaMode)EditorGUILayout.EnumPopup(
                new GUIContent("Rest Delta",
                    "RestDelta remaps each key as destRest + (key - sourceRest). CopyLocal keeps authored parent-local values."),
                _mode);

            if (!_slotMapSeeded)
            {
                _slotMap = SpritePartsClipRetarget.SuggestSlotMap(sourceData, dest, _sourceClipIndex);
                _slotMapSeeded = true;
            }

            var plan = SpritePartsClipRetarget.PlanRetarget(
                sourceData, _sourceClipIndex, dest, _slotMap, _mode);

            _scroll = GUILayout.BeginScrollView(_scroll);
            DrawSlotMap(sourceData, dest);
            GUILayout.EndScrollView();

            if (plan.Ok)
            {
                GUILayout.Label(plan.Summary, EditorStyles.wordWrappedMiniLabel);
                for (int i = 0; i < plan.Notes.Count; i++)
                    GUILayout.Label(plan.Notes[i], EditorStyles.wordWrappedMiniLabel);
                DrawPreview(sourceData, dest, plan);
                if (GUILayout.Button("Apply", GUILayout.Height(24f)))
                {
                    _host.ApplyPartsClipRetarget(_source, _sourceClipIndex, _slotMap, _mode);
                    editorWindow.Close();
                    GUILayout.EndArea();
                    return;
                }
            }
            else
            {
                EditorGUILayout.HelpBox(plan.Reason ?? "Unresolved slot map blocks Apply.", MessageType.Error);
                if (_host.ImportPreviewing)
                    _host.ClearImportPreview("Retarget preview stopped: fix the mapping first.");
            }

            if (GUILayout.Button("Cancel"))
                editorWindow.Close();
            GUILayout.EndArea();
        }

        void DrawSlotMap(SpriteSheetProfile source, SpriteSheetProfile dest)
        {
            GUILayout.Space(4f);
            GUILayout.Label("SLOT MAP", EditorStyles.miniBoldLabel);
            var required = SpriteProfileClipImport.CollectRequiredSourceSlots(
                source, new[] { _sourceClipIndex });
            var destIds = new List<string> { "" };
            var destOptions = new List<string> { "(unmapped - blocks Apply)" };
            if (dest.PartsSlots != null)
            {
                for (int i = 0; i < dest.PartsSlots.Count; i++)
                {
                    var slot = dest.PartsSlots[i];
                    if (slot == null) continue;
                    string id = SpritePartIdUtility.Canonical(slot.SlotId, slot.Name);
                    destIds.Add(id);
                    destOptions.Add(slot.Name + " (" + id + ")");
                }
            }
            var suggestions = SpritePartsClipRetarget.SuggestSlotMapByName(source, dest, _sourceClipIndex);
            var mismatches = SpriteProfileClipImport.CollectSlotMapMismatches(source, dest, _slotMap);
            foreach (string sid in required)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(sid, EditorStyles.miniLabel, GUILayout.Width(120f));
                int selected = 0;
                if (_slotMap.TryGetValue(sid, out string mapped) && !string.IsNullOrEmpty(mapped))
                {
                    int found = destIds.IndexOf(SpritePartIdUtility.Canonical(mapped));
                    if (found > 0) selected = found;
                }
                int next = EditorGUILayout.Popup(selected, destOptions.ToArray());
                if (next != selected)
                {
                    if (next <= 0) _slotMap.Remove(sid);
                    else _slotMap[sid] = destIds[next];
                }
                EditorGUILayout.EndHorizontal();
                if ((!_slotMap.TryGetValue(sid, out string current) || string.IsNullOrEmpty(current)) &&
                    TrySuggestion(suggestions, sid, out var suggestion))
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(
                        $"    suggested: {suggestion.DestinationName} ({suggestion.DestinationSlotId})",
                        EditorStyles.miniLabel);
                    if (GUILayout.Button("Use", EditorStyles.miniButton, GUILayout.Width(40f)))
                        _slotMap[sid] = suggestion.DestinationSlotId;
                    EditorGUILayout.EndHorizontal();
                }
            }
            if (mismatches.Count > 0)
                EditorGUILayout.HelpBox(
                    mismatches.Count + " mapped slot(s) differ in rest or parent. Preview on destination rest before Apply.",
                    MessageType.Warning);
        }

        void DrawPreview(SpriteSheetProfile source, SpriteSheetProfile dest, SpritePartsClipRetarget.Plan plan)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Preview on Rig",
                    "Sample the retargeted clip on this destination rest/hierarchy. Rest is not rewritten."),
                GUILayout.Height(22f)))
            {
                var preview = SpritePartsClipRetarget.BuildPreviewClip(plan, source, dest);
                _host.StartImportPreview(
                    _source != null ? _source.name : "source",
                    preview != null ? preview.Name : plan.DestinationName,
                    preview,
                    $"Previewing retargeted '{plan.SourceClipName}' on this rest/hierarchy (keys remapped; rest not rewritten)",
                    $" RETARGET PREVIEW: '{plan.SourceClipName}' ({plan.Mode}) - keys remapped onto this rest. Rest/hierarchy not rewritten.");
            }
            if (_host.ImportPreviewing && GUILayout.Button("Stop Preview", GUILayout.Height(22f)))
                _host.ClearImportPreview("Retarget preview ended");
            EditorGUILayout.EndHorizontal();
            if (!_host.ImportPreviewing)
                return;
            EditorGUILayout.BeginHorizontal();
            bool playing = _host.ImportPreviewPlaying;
            bool nextPlaying = GUILayout.Toggle(playing, playing ? "Pause" : "Play", "Button", GUILayout.Width(56f));
            if (nextPlaying != playing)
                _host.SetImportPreviewPlaying(nextPlaying);
            float duration = Mathf.Max(1e-3f, _host.ImportPreviewDuration);
            float next = GUILayout.HorizontalSlider(_host.ImportPreviewTime, 0f, duration);
            if (Mathf.Abs(next - _host.ImportPreviewTime) > 1e-4f)
                _host.SetImportPreviewTime(next);
            GUILayout.Label($"{_host.ImportPreviewTime:F2}s / {duration:F2}s", EditorStyles.miniLabel, GUILayout.Width(90f));
            EditorGUILayout.EndHorizontal();
        }

        static bool TrySuggestion(
            List<SpriteProfileClipImport.SlotNameSuggestion> suggestions, string sourceSlotId,
            out SpriteProfileClipImport.SlotNameSuggestion suggestion)
        {
            suggestion = null;
            if (suggestions == null) return false;
            for (int i = 0; i < suggestions.Count; i++)
            {
                if (SpritePartIdUtility.Canonical(suggestions[i].SourceSlotId) ==
                    SpritePartIdUtility.Canonical(sourceSlotId))
                {
                    suggestion = suggestions[i];
                    return true;
                }
            }
            return false;
        }
    }
}
