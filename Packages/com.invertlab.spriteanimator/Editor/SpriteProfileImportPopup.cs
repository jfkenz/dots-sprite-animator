using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>
    /// Import-from-profile picker: art, frame clips, and Parts clips. Stage 1
    /// chooses a read-only source; stage 2 selects items, maps Parts SlotIds,
    /// previews the plan, and commits one Undo on the host. Cancel changes nothing.
    /// </summary>
    sealed class SpriteProfileImportPopup : PopupWindowContent
    {
        readonly SpriteSheetToolWindow _host;
        string _search = string.Empty;
        Vector2 _scroll;
        bool _focusSearch = true;
        ScriptableSpriteSheetProfile _source;
        string _sourcePath;
        List<int> _selectedSheets = new();
        List<int> _selectedAppearances = new();
        List<int> _selectedFrameClips = new();
        List<int> _selectedPartsClips = new();
        Dictionary<string, string> _slotMap = new(StringComparer.Ordinal);
        HashSet<string> _excludedSlots = new(StringComparer.Ordinal);
        bool _slotMapSeeded;
        // Fresh popup = fresh policy: re-importing defaults to another copy.
        SpriteProfileImportConflictPolicy _conflictPolicy = SpriteProfileImportConflictPolicy.UniqueCopy;
        // Rebuilt every GUI pass; read-only against both profiles.
        SpriteProfileClipImport.Plan _currentPlan;
        int _previewClipIndex;

        public SpriteProfileImportPopup(SpriteSheetToolWindow host)
        {
            _host = host;
        }

        public override Vector2 GetWindowSize() => new Vector2(520f, 560f);

        public override void OnClose()
        {
            // Any exit (commit, cancel, focus loss) ends the rig preview.
            _host.ClearImportPreview();
        }

        public override void OnGUI(Rect rect)
        {
            var inner = new Rect(8f, 8f, rect.width - 16f, rect.height - 16f);
            GUILayout.BeginArea(inner);
            if (_source == null)
                DrawSourcePicker();
            else
                DrawItemPicker();
            GUILayout.EndArea();
        }

        void DrawSourcePicker()
        {
            GUILayout.Label("IMPORT FROM PROFILE", EditorStyles.boldLabel);
            GUILayout.Label(
                "Art, frame clips, and Parts clips are copied into the open profile as local definitions. Texture assets are reused. The source profile is never modified and never becomes a dependency.",
                EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(4f);

            GUI.SetNextControlName("SpriteAnimatorImportSourceSearch");
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            if (_focusSearch)
            {
                EditorGUI.FocusTextInControl("SpriteAnimatorImportSourceSearch");
                _focusSearch = false;
            }

            GUILayout.Space(6f);
            _scroll = GUILayout.BeginScrollView(_scroll);
            var assets = FindSourceAssets();
            if (assets.Count == 0)
                GUILayout.Label("No other profiles found.", EditorStyles.miniLabel);
            for (int i = 0; i < assets.Count; i++)
                DrawSourceRow(assets[i]);
            GUILayout.EndScrollView();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel"))
                editorWindow.Close();
        }

        void DrawSourceRow(ScriptableSpriteSheetProfile asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            var row = GUILayoutUtility.GetRect(1f, 38f);
            var evt = Event.current;
            bool hover = row.Contains(evt.mousePosition);
            if (evt.type == EventType.Repaint)
                EditorGUI.DrawRect(row, hover
                    ? new Color(0.18f, 0.55f, 0.82f, 0.22f)
                    : new Color(0.12f, 0.13f, 0.16f, 0.9f));

            var nameRect = new Rect(row.x + 8f, row.y + 3f, row.width - 16f, 18f);
            var pathRect = new Rect(row.x + 8f, row.y + 20f, row.width - 16f, 14f);
            bool parts = asset.Data != null && asset.Data.AnimKind == SpriteAnimKind.Parts;
            GUI.Label(nameRect, $"{asset.name}  |  {(parts ? "Parts" : "Frames")}", EditorStyles.label);
            GUI.Label(pathRect, path, EditorStyles.miniLabel);

            if (evt.type == EventType.MouseDown && row.Contains(evt.mousePosition) && evt.button == 0)
            {
                evt.Use();
                _source = asset;
                _sourcePath = path;
                ClearSelections();
            }
        }

        void ClearSelections()
        {
            _selectedSheets.Clear();
            _selectedAppearances.Clear();
            _selectedFrameClips.Clear();
            _selectedPartsClips.Clear();
            _slotMap.Clear();
            _excludedSlots.Clear();
            _slotMapSeeded = false;
        }

        List<ScriptableSpriteSheetProfile> FindSourceAssets()
        {
            var result = new List<ScriptableSpriteSheetProfile>();
            string query = _search?.Trim() ?? string.Empty;
            string[] guids = AssetDatabase.FindAssets("t:ScriptableSpriteSheetProfile");
            for (int i = 0; i < (guids?.Length ?? 0); i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(path);
                if (asset == null || asset.Data == null)
                    continue;
                if (asset == _host.ProfileAsset)
                    continue;
                if (query.Length > 0 &&
                    asset.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    path.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                result.Add(asset);
            }
            result.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        void DrawItemPicker()
        {
            GUILayout.Label($"SOURCE: {_source.name}", EditorStyles.boldLabel);
            GUILayout.Label(_sourcePath, EditorStyles.miniLabel);
            GUILayout.Space(4f);

            var data = _source.Data;
            var sheets = data.Sheets != null && data.Sheets.Count > 0
                ? data.Sheets
                : LegacySheetView(data);
            var appearances = data.PartsAppearances ?? new List<SpritePartAppearanceDef>();
            var frameClips = data.Clips ?? new List<SpriteClipDef>();
            var partsClips = data.PartsClips ?? new List<SpritePartsClipDef>();
            _currentPlan = BuildPlan(sheets, appearances);

            if (_selectedPartsClips.Count > 0 && !_slotMapSeeded)
            {
                _slotMap = SpriteProfileClipImport.SuggestSlotMap(data, _host.EditingProfile, _selectedPartsClips);
                _slotMapSeeded = true;
            }

            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("SHEETS", EditorStyles.miniBoldLabel);
            if (sheets.Count == 0)
                GUILayout.Label("Source has no usable sheets.", EditorStyles.miniLabel);
            for (int i = 0; i < sheets.Count; i++)
                DrawSheetRow(sheets, i);

            GUILayout.Space(8f);
            GUILayout.Label("APPEARANCES", EditorStyles.miniBoldLabel);
            if (appearances.Count == 0)
                GUILayout.Label("Source has no Parts appearances.", EditorStyles.miniLabel);
            for (int i = 0; i < appearances.Count; i++)
                DrawAppearanceRow(sheets, appearances, i);

            GUILayout.Space(8f);
            GUILayout.Label("FRAME CLIPS", EditorStyles.miniBoldLabel);
            if (frameClips.Count == 0)
                GUILayout.Label("Source has no frame clips.", EditorStyles.miniLabel);
            for (int i = 0; i < frameClips.Count; i++)
                DrawFrameClipRow(frameClips, i);

            GUILayout.Space(8f);
            GUILayout.Label("PARTS CLIPS", EditorStyles.miniBoldLabel);
            if (partsClips.Count == 0)
                GUILayout.Label("Source has no Parts clips.", EditorStyles.miniLabel);
            for (int i = 0; i < partsClips.Count; i++)
                DrawPartsClipRow(partsClips, i);

            if (_selectedPartsClips.Count > 0)
                DrawSlotMapSection(data);

            GUILayout.EndScrollView();

            GUILayout.Space(4f);
            DrawConflictPolicySelector();
            GUILayout.Space(4f);
            var plan = _currentPlan;
            if (plan.Ok)
            {
                GUILayout.Label($"Import summary: {plan.Summary}", EditorStyles.wordWrappedMiniLabel);
                if (plan.Notes.Count > 0)
                    GUILayout.Label(string.Join("\n", plan.Notes.ToArray()), EditorStyles.wordWrappedMiniLabel);
                DrawReplacePreview(plan);
                DrawRigPreviewControls(data);
                if (GUILayout.Button("Import", GUILayout.Height(24f)))
                {
                    _host.ImportFromProfile(
                        _source,
                        new List<int>(_selectedSheets),
                        new List<int>(_selectedAppearances),
                        new List<int>(_selectedFrameClips),
                        new List<int>(_selectedPartsClips),
                        new Dictionary<string, string>(_slotMap),
                        new HashSet<string>(_excludedSlots),
                        _conflictPolicy);
                    editorWindow.Close();
                    return;
                }
            }
            else
            {
                EditorGUILayout.HelpBox(plan.Reason ?? "Invalid selection.", MessageType.Error);
                // Unresolved mappings block everything: tear the preview down so
                // the canvas cannot keep showing a stale mapped clip.
                if (_host.ImportPreviewing)
                {
                    _host.ClearImportPreview("Rig preview stopped: fix the mapping first.");
                    GUILayout.Label("Rig preview stopped: fix the mapping first.", EditorStyles.miniLabel);
                }
            }
            GUILayout.Label(
                "Default policy adds copies with unique names/IDs; exact art definition matches are reused. Parts clips need SlotId mapping. Frame clip closure imports the Event and socket catalog entries it needs; profile-level socket motion tracks are not clip data and stay unimported.",
                EditorStyles.wordWrappedMiniLabel);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Back"))
            {
                _source = null;
                ClearSelections();
            }
            if (GUILayout.Button("Cancel"))
                editorWindow.Close();
            GUILayout.EndHorizontal();
        }

        void DrawConflictPolicySelector()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Same ID/name exists:", EditorStyles.miniLabel, GUILayout.Width(110f));
            bool replace = _conflictPolicy == SpriteProfileImportConflictPolicy.ReplaceExisting;
            int next = GUILayout.Toolbar(
                replace ? 1 : 0,
                new[] { "Add unique copy", "Replace existing" },
                GUILayout.Width(220f));
            bool nextReplace = next == 1;
            if (nextReplace != replace)
                _conflictPolicy = nextReplace
                    ? SpriteProfileImportConflictPolicy.ReplaceExisting
                    : SpriteProfileImportConflictPolicy.UniqueCopy;
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Before-commit preview of every replace the current plan would perform,
        /// including what each replaced item is used by. Only shown for the
        /// Replace policy; nothing here mutates data.
        /// </summary>
        void DrawReplacePreview(SpriteProfileClipImport.Plan plan)
        {
            if (_conflictPolicy != SpriteProfileImportConflictPolicy.ReplaceExisting)
                return;
            var lines = new List<string>();
            for (int i = 0; i < plan.Art.Appearances.Count; i++)
            {
                var a = plan.Art.Appearances[i];
                if (a.Resolution == SpriteProfileArtImport.AppearanceResolution.ReplaceExisting)
                    lines.Add(ReplaceLine("appearance", a.DestinationAppearanceId, a.UsedBy));
            }
            for (int i = 0; i < plan.FrameClips.Count; i++)
            {
                var f = plan.FrameClips[i];
                if (f.ReplaceExisting)
                    lines.Add(ReplaceLine("frame clip", f.DestinationName, f.UsedBy));
            }
            for (int i = 0; i < plan.PartsClips.Count; i++)
            {
                var p = plan.PartsClips[i];
                if (p.ReplaceExisting)
                    lines.Add(ReplaceLine("Parts clip", p.DestinationName, p.UsedBy));
            }
            if (lines.Count == 0)
            {
                GUILayout.Label(
                    "No ID/name conflicts in this selection; everything imports as a copy.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }
            EditorGUILayout.HelpBox(
                "Replace overwrites the destination items below. Their IDs/names/list slots stay; content comes from the source. This is a one-time copy, not a live link.",
                MessageType.Warning);
            GUILayout.Label(string.Join("\n", lines.ToArray()), EditorStyles.wordWrappedMiniLabel);
        }

        static string ReplaceLine(string kind, string identity, List<string> usedBy)
        {
            if (usedBy == null || usedBy.Count == 0)
                return $"REPLACE {kind} '{identity}' (no other references found)";
            return $"REPLACE {kind} '{identity}' -- used by: {string.Join("; ", usedBy.ToArray())}";
        }

        void DrawSlotMapSection(SpriteSheetProfile source)
        {
            GUILayout.Space(8f);
            GUILayout.Label("PARTS SLOT MAP", EditorStyles.miniBoldLabel);
            GUILayout.Label(
                "Map each source SlotId to a destination slot, or exclude the track. Exact id matches auto-fill; name matches appear as suggestions you confirm with Use.",
                EditorStyles.wordWrappedMiniLabel);
            var required = SpriteProfileClipImport.CollectRequiredSourceSlots(source, _selectedPartsClips);
            var destSlots = _host.EditingProfile?.PartsSlots ?? new List<SpritePartSlotDef>();
            var destOptions = new List<string> { "(exclude track)" };
            var destIds = new List<string> { "" };
            for (int i = 0; i < destSlots.Count; i++)
            {
                var s = destSlots[i];
                if (s == null) continue;
                string id = SpritePartIdUtility.Canonical(s.SlotId, s.Name);
                destOptions.Add($"{s.Name}  ({id})");
                destIds.Add(id);
            }
            // Preview mismatches from the LIVE map being edited (required,
            // mapped, not excluded) so warnings show while the user is still
            // mapping - not only once the whole plan becomes valid.
            var activeMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string sid in required)
            {
                if (_excludedSlots.Contains(sid)) continue;
                if (_slotMap.TryGetValue(sid, out string mapped) && !string.IsNullOrWhiteSpace(mapped))
                    activeMap[sid] = mapped;
            }
            var mismatches = SpriteProfileClipImport.CollectSlotMapMismatches(
                source, _host.EditingProfile, activeMap);
            var nameSuggestions = SpriteProfileClipImport.SuggestSlotMapByName(
                source, _host.EditingProfile, _selectedPartsClips);
            foreach (string sid in required)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(sid, EditorStyles.miniLabel, GUILayout.Width(120f));
                int selected = 0;
                if (!_excludedSlots.Contains(sid) &&
                    _slotMap.TryGetValue(sid, out string mapped) && !string.IsNullOrEmpty(mapped))
                {
                    int found = destIds.IndexOf(SpritePartIdUtility.Canonical(mapped));
                    if (found > 0) selected = found;
                }
                int next = EditorGUILayout.Popup(selected, destOptions.ToArray());
                if (next != selected || !_slotMapSeeded)
                {
                    if (next <= 0)
                    {
                        _excludedSlots.Add(sid);
                        _slotMap.Remove(sid);
                    }
                    else
                    {
                        _excludedSlots.Remove(sid);
                        _slotMap[sid] = destIds[next];
                    }
                }
                EditorGUILayout.EndHorizontal();
                // Name matches are suggestions only: shown with a Use button,
                // applied solely when the user confirms. Exact id matches
                // auto-fill through SuggestSlotMap above.
                bool mappedNow = !_excludedSlots.Contains(sid) &&
                                 _slotMap.TryGetValue(sid, out string current) &&
                                 !string.IsNullOrEmpty(current);
                if (!mappedNow && TryGetNameSuggestion(nameSuggestions, sid, out var suggestion))
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(
                        $"    suggested: {suggestion.DestinationName}  ({suggestion.DestinationSlotId})",
                        EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("Use", EditorStyles.miniButton, GUILayout.Width(40f)))
                    {
                        _excludedSlots.Remove(sid);
                        _slotMap[sid] = suggestion.DestinationSlotId;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                if (MismatchesFor(mismatches, sid, out string joined))
                    GUILayout.Label(joined, EditorStyles.wordWrappedMiniLabel);
            }
            if (mismatches != null && mismatches.Count > 0)
            {
                GUILayout.Space(2f);
                EditorGUILayout.HelpBox(
                    $"Motion is copied, not retargeted: {mismatches.Count} mapped slot(s) differ in rest pose or parent. Check Preview on Rig before importing.",
                    MessageType.Warning);
            }
        }

        static bool TryGetNameSuggestion(
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

        static bool MismatchesFor(
            List<SpriteProfileClipImport.SlotMapMismatch> mismatches, string sourceSlotId, out string joined)
        {
            joined = null;
            if (mismatches == null) return false;
            var lines = new List<string>();
            for (int i = 0; i < mismatches.Count; i++)
            {
                var m = mismatches[i];
                if (SpritePartIdUtility.Canonical(m.SourceSlotId) == SpritePartIdUtility.Canonical(sourceSlotId))
                    lines.Add($"! {m.Kind}: {m.Detail}");
            }
            if (lines.Count == 0) return false;
            joined = string.Join("\n", lines.ToArray());
            return true;
        }

        /// <summary>
        /// Rig preview controls: sample the planned clip(s) on the destination
        /// rig before commit. The canvas behind shows the motion; scrub and
        /// playback live here so the popup (and the preview) stay open.
        /// </summary>
        void DrawRigPreviewControls(SpriteSheetProfile source)
        {
            if (_currentPlan == null || !_currentPlan.Ok || _currentPlan.PartsClips.Count == 0)
                return;
            GUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            var previews = SpriteProfileClipImport.BuildPreviewClips(
                _currentPlan, source, _host.EditingProfile);
            if (previews.Count == 0)
            {
                EditorGUILayout.EndHorizontal();
                return;
            }
            _previewClipIndex = Mathf.Clamp(_previewClipIndex, 0, previews.Count - 1);
            if (previews.Count > 1)
            {
                var names = new string[previews.Count];
                for (int i = 0; i < previews.Count; i++)
                    names[i] = previews[i].Name;
                _previewClipIndex = EditorGUILayout.Popup(_previewClipIndex, names, GUILayout.Width(140f));
            }
            bool previewingThis = _host.ImportPreviewing;
            if (GUILayout.Button(
                    new GUIContent("Preview on Rig",
                        "Sample the mapped clip on this rig's rest pose and hierarchy (read-only). Editing pauses while the preview runs."),
                    GUILayout.Height(22f)))
            {
                var chosen = previews[Mathf.Clamp(_previewClipIndex, 0, previews.Count - 1)];
                _host.StartImportPreview(_source.name, chosen.Name, chosen);
            }
            if (previewingThis && GUILayout.Button("Stop Preview", GUILayout.Height(22f)))
                _host.ClearImportPreview("Import preview ended");
            EditorGUILayout.EndHorizontal();

            if (_host.ImportPreviewing)
            {
                EditorGUILayout.BeginHorizontal();
                bool playing = _host.ImportPreviewPlaying;
                bool nextPlaying = GUILayout.Toggle(playing, playing ? "Pause" : "Play", "Button",
                    GUILayout.Width(56f));
                if (nextPlaying != playing)
                    _host.SetImportPreviewPlaying(nextPlaying);
                float duration = Mathf.Max(1e-3f, _host.ImportPreviewDuration);
                float next = GUILayout.HorizontalSlider(_host.ImportPreviewTime, 0f, duration);
                if (Mathf.Abs(next - _host.ImportPreviewTime) > 1e-4f)
                    _host.SetImportPreviewTime(next);
                GUILayout.Label(
                    $"{_host.ImportPreviewTime:F2}s / {duration:F2}s",
                    EditorStyles.miniLabel, GUILayout.Width(90f));
                EditorGUILayout.EndHorizontal();
                GUILayout.Label(
                    "Keys stay local to their mapped slots: different rest poses or parents change the world motion. Change the map, then press Preview on Rig again.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        void DrawSheetRow(List<SpriteSheetDef> sheets, int index)
        {
            var sheet = sheets[index];
            if (sheet == null || sheet.Texture == null)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label($"o {sheet?.Name ?? ("Sheet " + index)} - no texture, skipped.", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
                return;
            }
            bool required = IsSheetRequiredByAppearance(index) || IsSheetRequiredByFrameClip(index);
            bool selected = required || _selectedSheets.Contains(index);
            string info = $"{sheet.Texture.name} | {Mathf.Max(1, sheet.Columns)}x{Mathf.Max(1, sheet.Rows)}" +
                          $" | {sheet.PixelsPerUnit} PPU";
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(required))
            {
                bool next = GUILayout.Toggle(selected, GUIContent.none, GUILayout.Width(16f));
                if (!required && next != selected)
                {
                    if (next) _selectedSheets.Add(index);
                    else _selectedSheets.Remove(index);
                }
            }
            GUILayout.Label($"{(selected ? "#" : "o")} {sheet.Name}", EditorStyles.miniLabel, GUILayout.Width(110f));
            GUILayout.Label(info, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        void DrawAppearanceRow(List<SpriteSheetDef> sheets, List<SpritePartAppearanceDef> appearances, int index)
        {
            var app = appearances[index];
            if (app == null || app.SheetIndex < 0 || app.SheetIndex >= sheets.Count ||
                sheets[app.SheetIndex] == null || sheets[app.SheetIndex].Texture == null)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label($"o {app?.Name ?? ("Appearance " + index)} - unusable sheet, skipped.", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
                return;
            }
            bool selected = _selectedAppearances.Contains(index);
            var sheet = sheets[app.SheetIndex];
            EditorGUILayout.BeginHorizontal();
            bool next = GUILayout.Toggle(selected, GUIContent.none, GUILayout.Width(16f));
            if (next != selected)
            {
                if (next)
                {
                    _selectedAppearances.Add(index);
                    if (!_selectedSheets.Contains(app.SheetIndex))
                        _selectedSheets.Add(app.SheetIndex);
                }
                else _selectedAppearances.Remove(index);
            }
            GUILayout.Label($"{(selected ? "#" : "o")} {app.Name}", EditorStyles.miniLabel, GUILayout.Width(110f));
            GUILayout.Label(
                $"id '{SpritePartIdUtility.Canonical(app.AppearanceId, app.Name)}' | {sheet.Name} cell {app.CellIndex}",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        void DrawFrameClipRow(List<SpriteClipDef> clips, int index)
        {
            var clip = clips[index];
            if (clip == null || clip.Frames == null || clip.Frames.Length == 0)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label($"o clip {index} - empty, skipped.", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
                return;
            }
            bool selected = _selectedFrameClips.Contains(index);
            EditorGUILayout.BeginHorizontal();
            bool next = GUILayout.Toggle(selected, GUIContent.none, GUILayout.Width(16f));
            if (next != selected)
            {
                if (next)
                {
                    _selectedFrameClips.Add(index);
                    int sheet = Mathf.Max(0, clip.SheetIndex);
                    if (!_selectedSheets.Contains(sheet))
                        _selectedSheets.Add(sheet);
                }
                else _selectedFrameClips.Remove(index);
            }
            GUILayout.Label($"{(selected ? "#" : "o")} {clip.Name}", EditorStyles.miniLabel, GUILayout.Width(110f));
            GUILayout.Label($"{clip.Frames.Length} frames | sheet {clip.SheetIndex} | {clip.FrameRate} fps",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        void DrawPartsClipRow(List<SpritePartsClipDef> clips, int index)
        {
            var clip = clips[index];
            if (clip == null)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label($"o Parts clip {index} - null, skipped.", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
                return;
            }
            bool selected = _selectedPartsClips.Contains(index);
            int tracks = clip.Tracks?.Count ?? 0;
            EditorGUILayout.BeginHorizontal();
            bool next = GUILayout.Toggle(selected, GUIContent.none, GUILayout.Width(16f));
            if (next != selected)
            {
                if (next)
                {
                    _selectedPartsClips.Add(index);
                    _slotMapSeeded = false;
                }
                else
                {
                    _selectedPartsClips.Remove(index);
                    _slotMapSeeded = false;
                }
            }
            GUILayout.Label($"{(selected ? "#" : "o")} {clip.Name}", EditorStyles.miniLabel, GUILayout.Width(110f));
            GUILayout.Label(
                $"id '{SpritePartIdUtility.Canonical(clip.ClipId, clip.Name)}' | {tracks} tracks | {clip.Duration:F2}s",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        bool IsSheetRequiredByAppearance(int sheetIndex)
        {
            var appearances = _source.Data.PartsAppearances;
            if (appearances == null) return false;
            for (int i = 0; i < _selectedAppearances.Count; i++)
            {
                int appIndex = _selectedAppearances[i];
                if (appIndex >= 0 && appIndex < appearances.Count &&
                    appearances[appIndex] != null && appearances[appIndex].SheetIndex == sheetIndex)
                    return true;
            }
            return false;
        }

        bool IsSheetRequiredByFrameClip(int sheetIndex)
        {
            var clips = _source.Data.Clips;
            if (clips == null) return false;
            for (int i = 0; i < _selectedFrameClips.Count; i++)
            {
                int index = _selectedFrameClips[i];
                if (index >= 0 && index < clips.Count && clips[index] != null &&
                    Mathf.Max(0, clips[index].SheetIndex) == sheetIndex)
                    return true;
            }
            return false;
        }

        SpriteProfileClipImport.Plan BuildPlan(List<SpriteSheetDef> sheets, List<SpritePartAppearanceDef> appearances)
        {
            var sheetIndices = new List<int>();
            for (int i = 0; i < _selectedSheets.Count; i++)
            {
                int index = _selectedSheets[i];
                if (index >= 0 && index < sheets.Count && sheets[index]?.Texture != null)
                    sheetIndices.Add(index);
            }
            var appIndices = new List<int>();
            for (int i = 0; i < _selectedAppearances.Count; i++)
            {
                int index = _selectedAppearances[i];
                if (index >= 0 && index < appearances.Count && appearances[index] != null &&
                    appearances[index].SheetIndex >= 0 && appearances[index].SheetIndex < sheets.Count &&
                    sheets[appearances[index].SheetIndex]?.Texture != null)
                    appIndices.Add(index);
            }
            return SpriteProfileClipImport.PlanImport(
                _source.Data, _host.EditingProfile,
                sheetIndices, appIndices,
                new List<int>(_selectedFrameClips),
                new List<int>(_selectedPartsClips),
                _slotMap, _excludedSlots, _conflictPolicy);
        }

        static List<SpriteSheetDef> LegacySheetView(SpriteSheetProfile data)
        {
            if (data.Sheet == null)
                return new List<SpriteSheetDef>();
            return new List<SpriteSheetDef>
            {
                new SpriteSheetDef
                {
                    Name = data.Sheet.name,
                    Texture = data.Sheet,
                    Columns = data.Columns,
                    Rows = data.Rows,
                    PixelsPerUnit = data.PixelsPerUnit,
                    Pivot = data.Pivot,
                    CellLayoutMode = data.CellLayoutMode,
                },
            };
        }
    }
}
