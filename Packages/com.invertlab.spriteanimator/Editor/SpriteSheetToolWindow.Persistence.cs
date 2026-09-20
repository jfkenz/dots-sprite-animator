using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    public sealed partial class SpriteSheetToolWindow
    {

        void LoadAutoSavePrefs()
        {
            _autoSaveEnabled = EditorPrefs.GetBool(AutoSaveEnabledPrefsKey, true);
            _autoSaveIntervalMinutes = Mathf.Clamp(
                EditorPrefs.GetInt(AutoSaveMinutesPrefsKey, AutoSaveMinutesDefault),
                AutoSaveMinutesMin, AutoSaveMinutesMax);
            ScheduleNextAutoSave(EditorApplication.timeSinceStartup);
        }

        internal void SetAutoSavePrefs(bool enabled, int minutes)
        {
            _autoSaveEnabled = enabled;
            _autoSaveIntervalMinutes = Mathf.Clamp(minutes, AutoSaveMinutesMin, AutoSaveMinutesMax);
            EditorPrefs.SetBool(AutoSaveEnabledPrefsKey, _autoSaveEnabled);
            EditorPrefs.SetInt(AutoSaveMinutesPrefsKey, _autoSaveIntervalMinutes);
            ScheduleNextAutoSave(EditorApplication.timeSinceStartup);
            Repaint();
        }

        void ScheduleNextAutoSave(double now)
        {
            _autoSaveNextDue = now + _autoSaveIntervalMinutes * 60.0;
        }

        void NoteProfileSaved(bool auto)
        {
            ScheduleNextAutoSave(EditorApplication.timeSinceStartup);
            if (auto)
            {
                _autoSaveLastStatus = System.DateTime.Now.ToString("HH:mm");
                _status = "Auto-saved profile at " + _autoSaveLastStatus;
            }
        }

        void TickAutoSave(double now)
        {
            if (!_autoSaveEnabled)
                return;
            if (now < _autoSaveNextDue)
                return;
            // Always reschedule so a skipped tick does not spin every frame.
            ScheduleNextAutoSave(now);
            TryAutoSaveProfile();
        }

        void TryAutoSaveProfile()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (EditorUtility.scriptCompilationFailed)
                return;
            if (IsRenamingAnything())
                return;
            if (!CanSaveProfile())
                return;
            // Require a bound asset that Unity considers dirty - do not create
            // a new .asset from autosave alone.
            if (_asset == null || !EditorUtility.IsDirty(_asset))
                return;

            SaveProfile(quiet: true);
        }

        void SaveDirty()
        {
            if (_asset != null)
                EditorUtility.SetDirty(_asset);
        }

        /// <summary>
        /// EditorWindow holds <see cref="_profile"/> while the SO holds Data.
        /// They must stay the same reference after loads/undos or frame edits
        /// (e.g. RemoveFrames) can mutate one copy while Save writes the other.
        /// </summary>
        void SyncWorkingProfileToAsset()
        {
            if (_asset == null || _profile == null)
                return;
            if (!ReferenceEquals(_asset.Data, _profile))
            {
                // The profile inspector may change the runtime mode while this
                // window survives a domain reload with a serialized working copy.
                // Preserve that external choice instead of writing the stale copy
                // back over it on the next Parts edit/save.
                if (_asset.Data != null)
                    _profile.AnimKind = _asset.Data.AnimKind;
                _asset.Data = _profile;
            }
            EditorUtility.SetDirty(_asset);
        }

        bool CanSaveProfile()
        {
            // Existing .asset can always be saved (Parts + Frames dirty state).
            // New profiles need at least one sheet texture to choose the save folder.
            if (_asset != null)
                return true;
            return ResolveSaveTexture() != null;
        }

        Texture2D ResolveSaveTexture()
        {
            if (_profile == null)
                return null;
            _profile.EnsureSheets(_selectedSheet);
            // Prefer the active sheet, then any sheet with a texture (Parts art
            // often lives on sheet 1+ while sheet 0 stays empty).
            var active = _profile.SheetAt(_selectedSheet);
            if (active?.Texture != null)
                return active.Texture;
            if (_profile.Sheets != null)
            {
                for (int i = 0; i < _profile.Sheets.Count; i++)
                {
                    var sheet = _profile.Sheets[i];
                    if (sheet != null && sheet.Texture != null)
                        return sheet.Texture;
                }
            }
            return _profile.Sheet;
        }

        void SaveProfile(bool quiet = false)
        {
            Texture2D saveTex = ResolveSaveTexture();
            if (saveTex == null)
            {
                _status = "Assign a sprite sheet texture before saving a new profile (or Open an existing .asset).";
                if (!quiet)
                    ShowNotification(new GUIContent(_status));
                return;
            }

            _profile.EnsureSheets(_selectedSheet);
            WriteActiveSheetFromLegacy();
            string texturePath = AssetDatabase.GetAssetPath(saveTex);
            string directory = Path.GetDirectoryName(texturePath)?.Replace('\\', '/');
            // Bind an existing .asset without LoadAsset - LoadAsset would replace
            // _profile with disk Data and discard in-memory frame deletions.
            if (_asset == null && !_createSeparateProfileOnSave)
                TryBindExistingAssetWithoutReload();
            if (_asset == null)
            {
                string assetPath = UniqueProfileAssetPath(directory, saveTex.name);
                _asset = CreateInstance<ScriptableSpriteSheetProfile>();
                // This is a new asset, so its default runtime mode must not
                // override the mode chosen for the editing document.
                _asset.Data = _profile;
                AssetDatabase.CreateAsset(_asset, assetPath);
            }
            _createSeparateProfileOnSave = false;
            SyncWorkingProfileToAsset();
            AssetDatabase.SaveAssets();
            // After save, stay bound to whatever nested Data the SO retained.
            if (_asset.Data != null)
                _profile = _asset.Data;

            string savedPath = AssetDatabase.GetAssetPath(_asset);
            string jsonPath = savedPath.Replace(".asset", ".json");
            File.WriteAllText(jsonPath, _profile.ToJson());
            // Do not ImportAsset the sidecar: reimport refresh can reload the
            // related .asset and resurface a stale Frames length (8 after 8->7).

            var clip = CurrentClip;
            int frames = clip?.Frames?.Length ?? 0;
            if (quiet)
            {
                NoteProfileSaved(auto: true);
            }
            else
            {
                _status = clip != null
                    ? $"Saved {_asset.name}  *  {clip.Name}: {frames} frames"
                    : $"Saved {_asset.name}";
                ShowNotification(new GUIContent("Profile saved"));
                NoteProfileSaved(auto: false);
            }
            SpriteSheetProfileRecents.Remember(_asset);
            SpritePartsSceneSync.RefreshCharactersUsing(_asset);
            foreach (var still in UnityEngine.Object.FindObjectsByType<SpriteStaticAuthoring>(FindObjectsInactive.Include))
                if (still.Profile == _asset) still.UpdatePreview();
        }

        static string UniqueProfileAssetPath(string directory, string sheetName)
        {
            string path = $"{directory}/{sheetName}_profile.asset";
            int n = 2;
            while (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
            {
                path = $"{directory}/{sheetName}_profile {n}.asset";
                n++;
            }
            return path;
        }

        void TryLoadExistingAsset()
        {
            if (_profile.Sheet == null) return;
            string path = AssetDatabase.GetAssetPath(_profile.Sheet);
            string directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string assetPath = $"{directory}/{_profile.Sheet.name}_profile.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(assetPath);
            if (existing != null)
                LoadAsset(existing);
        }

        /// <summary>
        /// Point <see cref="_asset"/> at an existing sibling profile without
        /// replacing the in-memory <see cref="_profile"/> (preserves edits).
        /// </summary>
        void TryBindExistingAssetWithoutReload()
        {
            if (_profile?.Sheet == null)
                return;
            string path = AssetDatabase.GetAssetPath(_profile.Sheet);
            string directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string assetPath = $"{directory}/{_profile.Sheet.name}_profile.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(assetPath);
            if (existing != null)
                _asset = existing;
        }

        void LoadAsset(ScriptableSpriteSheetProfile asset)
        {
            ClearImportPreview();
            ClearProfileRename();
            _asset = asset;
            _profile = asset.Data ?? new SpriteSheetProfile();
            if (asset.Data == null)
                asset.Data = _profile;
            _sheetFoldInitialized = false;
            EnsureProfile();
            // Open shows the profile's runtime workspace first; later tab switches
            // are pure workspace changes and never touch AnimKind.
            _studioTab = _profile.AnimKind switch
            {
                SpriteAnimKind.Parts => StudioTab.Parts,
                SpriteAnimKind.Static => StudioTab.Static,
                _ => StudioTab.Clips,
            };
            if (_studioTab == StudioTab.Parts)
            {
                _partsPlaying = false;
                _partsPreviewTime = 0f;
                EnsurePartsSession();
            }
            else if (_studioTab == StudioTab.Static)
                EnsureStaticSession();
            if (_profile.Clips != null && _profile.Clips.Count > 0 && _profile.Clips[0] != null)
            {
                _selectedClip = 0;
                _selectedSheet = _profile.Clips[0].SheetIndex;
            }
            else
            {
                _selectedClip = -1;
                _selectedSheet = 0;
            }
            _profile.EnsureSheets(_selectedSheet);
            if (_profile.SheetsWorldHeightsDiffer())
            {
                int source = _selectedSheet;
                var selected = _profile.SheetAt(_selectedSheet);
                if (selected?.Texture == null)
                    source = 0;
                RecordDiscreteUndo("Match Sheets World Size");
                RematchSheetsWorldSize(source);
                SealUndoGroup();
                SaveDirty();
            }
            InvalidateSheetPixelCache();
            SelectOnlyFrame(0);
            ClearColliderSelection();
            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
            _selectedOnionFrame = -1;
            _previewTime = 0f;
            _createSeparateProfileOnSave = false;
            _status = $"Loaded {asset.name}";
            SpriteSheetProfileRecents.Remember(asset);
        }

        void EnsureProfile()
        {
            _profile ??= new SpriteSheetProfile();
            _profile.Clips ??= new List<SpriteClipDef>();
            _profile.Events ??= new List<SpriteEventDef>();
            _profile.Hitboxes ??= new List<FrameBoxDef>();
            _profile.EnsureSheets(_selectedSheet);
            if (_profile.Sheets != null && _profile.Sheets.Count > 0)
                _selectedSheet = Mathf.Clamp(_selectedSheet, 0, _profile.Sheets.Count - 1);
            _profile.EnsureTimelineHitPolygon();
            _profile.EnsureSocketCatalog();
            _profile.EnsureSocketMotions();
            _profile.EnsurePartsRig();
            if (!_profile.OnionSettingsInitialized)
            {
                _profile.OnionSettingsInitialized = true;
                _profile.OnionPastFrames = SpriteSheetProfile.DefaultOnionFrameCount;
                _profile.OnionFutureFrames = SpriteSheetProfile.DefaultOnionFrameCount;
                _profile.ShowOnionLayerNumbers = true;
            }
            _profile.OnionPastFrames = Mathf.Clamp(_profile.OnionPastFrames, 0, 4096);
            _profile.OnionFutureFrames = Mathf.Clamp(_profile.OnionFutureFrames, 0, 4096);
            foreach (var clip in _profile.Clips)
                clip?.EnsureFrameData();
            if (_profile.Events.Count == 0)
            {
                _profile.Events.Add(new SpriteEventDef { Id = 1, Name = "Footstep" });
                _profile.Events.Add(new SpriteEventDef
                {
                    Id = 2,
                    Name = "Attack",
                    Color = new Color(1f, 0.35f, 0.3f),
                });
            }
        }

        void RenameHitboxClip(string oldName, string newName)
        {
            foreach (var box in _profile.Hitboxes)
            {
                if (box == null)
                    continue;
                if (box.ClipName == oldName)
                    box.ClipName = newName;
                if (box.IsCharacter)
                    box.RenameCharacterClipFilter(oldName, newName);
            }
        }

        IEnumerable<FrameBoxDef> BoxesFor(SpriteClipDef clip, int frame)
        {
            if (_profile?.Hitboxes == null || clip == null)
                yield break;
            foreach (var box in SpriteColliderWorld.VisibleOn(_profile.Hitboxes, clip.Name, frame))
                yield return box;
        }

        List<FrameBoxDef> CurrentFrameColliders(SpriteClipDef clip, int frame)
        {
            var result = new List<FrameBoxDef>();
            if (clip == null)
                return result;
            foreach (var box in BoxesFor(clip, frame))
                result.Add(box);
            return result;
        }


        void DrawToolbarProfileTitle(Rect toolbarRect, float leftBound, float rightBound)
        {
            _ = toolbarRect;
            float gap = rightBound - leftBound;
            if (gap < 100f)
            {
                _hasProfileNameRect = false;
                _profileNameHovered = false;
                return;
            }

            float width = Mathf.Min(340f, gap - 24f);
            float x = leftBound + (gap - width) * 0.5f;
            var nameRect = new Rect(x, 12f, width, 24f);
            _profileNameRect = nameRect;
            _hasProfileNameRect = true;

            var e = Event.current;
            _profileNameHovered = nameRect.Contains(e.mousePosition);

            if (_renamingProfile && _asset != null)
            {
                // Click outside the field commits (OS-style), even on Parts where
                // the clip-browser rename key handler never runs.
                if (e.type == EventType.MouseDown && e.button == 0 &&
                    !nameRect.Contains(e.mousePosition))
                {
                    CommitProfileRename();
                    // Don't Use() - let the click reach whatever was under it.
                    return;
                }

                DrawInlineRenameField(nameRect, ProfileRenameControl,
                    ref _renameProfileValue, ref _focusProfileRename, EditorStyles.toolbarTextField);

                // Enter / Esc while the toolbar field has focus (Parts tab safe).
                if (e.type == EventType.KeyDown)
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    {
                        CommitProfileRename();
                        e.Use();
                    }
                    else if (e.keyCode == KeyCode.Escape)
                    {
                        CancelProfileRename();
                        e.Use();
                    }
                }
                return;
            }

            string label;
            string tip;
            if (_asset == null)
            {
                label = "Unsaved profile";
                tip = "Save Profile once to create a .asset, then double-click or press F2 to rename.";
            }
            else
            {
                bool dirty = EditorUtility.IsDirty(_asset);
                label = dirty ? _asset.name + "  *" : _asset.name;
                string path = AssetDatabase.GetAssetPath(_asset);
                tip = string.IsNullOrEmpty(path)
                    ? "Double-click or F2 to rename."
                    : path + "\nDouble-click or F2 to rename.";
            }

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
            };
            if (_asset == null)
                style.normal.textColor = new Color(0.55f, 0.6f, 0.66f, 1f);
            GUI.Label(nameRect, new GUIContent(label, tip), style);

            if (_asset != null &&
                e.type == EventType.MouseDown && e.button == 0 &&
                nameRect.Contains(e.mousePosition) && e.clickCount >= 2)
            {
                BeginProfileRename();
                e.Use();
            }
        }

        void BeginProfileRename()
        {
            if (_asset == null)
            {
                _status = "Save the profile once before renaming the .asset.";
                ShowNotification(new GUIContent(_status));
                return;
            }

            CommitAllRenames();
            _renamingProfile = true;
            _renameProfileOriginal = _asset.name;
            _renameProfileValue = _asset.name;
            _focusProfileRename = true;
            Repaint();
        }

        void CommitProfileRename()
        {
            if (!_renamingProfile)
                return;

            if (_asset == null)
            {
                ClearProfileRename();
                return;
            }

            string desired = SanitizeProfileAssetName(_renameProfileValue);
            if (string.IsNullOrEmpty(desired))
            {
                _status = "Profile name cannot be empty.";
                ClearProfileRename();
                Repaint();
                return;
            }

            if (string.Equals(desired, _asset.name, StringComparison.Ordinal))
            {
                ClearProfileRename();
                return;
            }

            string oldPath = AssetDatabase.GetAssetPath(_asset);
            if (string.IsNullOrEmpty(oldPath))
            {
                _status = "Cannot rename: profile is not a project asset yet.";
                ClearProfileRename();
                return;
            }

            string oldJson = Path.ChangeExtension(oldPath, ".json");
            string error = AssetDatabase.RenameAsset(oldPath, desired);
            if (!string.IsNullOrEmpty(error))
            {
                _status = "Rename failed: " + error;
                ShowNotification(new GUIContent(_status));
                ClearProfileRename();
                Repaint();
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Keep the optional JSON sidecar next to the renamed .asset.
            string newPath = AssetDatabase.GetAssetPath(_asset);
            if (!string.IsNullOrEmpty(oldJson) && File.Exists(oldJson) && !string.IsNullOrEmpty(newPath))
            {
                string newJson = Path.ChangeExtension(newPath, ".json");
                if (!string.Equals(oldJson, newJson, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (File.Exists(newJson))
                            File.Delete(newJson);
                        File.Move(oldJson, newJson);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[DOTS Sprite Animator] Renamed profile asset but sidecar JSON move failed: " + ex.Message);
                    }
                }
            }

            SpriteSheetProfileRecents.Remember(_asset);
            _status = $"Renamed profile to '{_asset.name}'";
            ClearProfileRename();
            Repaint();
        }

        void CancelProfileRename()
        {
            if (_renamingProfile)
                _status = $"Kept profile name '{_renameProfileOriginal}'";
            ClearProfileRename();
        }

        void ClearProfileRename()
        {
            _renamingProfile = false;
            _renameProfileValue = string.Empty;
            _renameProfileOriginal = string.Empty;
            _focusProfileRename = false;
            GUI.FocusControl(null);
            Repaint();
        }

        static string SanitizeProfileAssetName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;
            string name = raw.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c.ToString(), string.Empty);
            name = name.Replace('/', ' ').Replace('\\', ' ').Trim();
            while (name.Contains("  "))
                name = name.Replace("  ", " ");
            return name;
        }


    }
}
