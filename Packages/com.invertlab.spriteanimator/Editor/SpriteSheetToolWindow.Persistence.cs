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
            // Require a bound asset that Unity considers dirty — do not create
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
                _asset.Data = _profile;
            EditorUtility.SetDirty(_asset);
        }

        bool CanSaveProfile()
        {
            return ResolveSaveTexture() != null;
        }

        Texture2D ResolveSaveTexture()
        {
            if (_profile == null)
                return null;
            _profile.EnsureSheets(_selectedSheet);
            if (_profile.Sheets != null && _profile.Sheets.Count > 0 &&
                _profile.Sheets[0] != null && _profile.Sheets[0].Texture != null)
                return _profile.Sheets[0].Texture;
            var active = _profile.SheetAt(_selectedSheet);
            if (active?.Texture != null)
                return active.Texture;
            return _profile.Sheet;
        }

        void SaveProfile(bool quiet = false)
        {
            Texture2D saveTex = ResolveSaveTexture();
            if (saveTex == null)
            {
                _status = "Assign a sprite sheet before saving";
                if (!quiet)
                    ShowNotification(new GUIContent(_status));
                return;
            }

            _profile.EnsureSheets(_selectedSheet);
            WriteActiveSheetFromLegacy();
            string texturePath = AssetDatabase.GetAssetPath(saveTex);
            string directory = Path.GetDirectoryName(texturePath)?.Replace('\\', '/');
            // Bind an existing .asset without LoadAsset — LoadAsset would replace
            // _profile with disk Data and discard in-memory frame deletions.
            if (_asset == null && !_createSeparateProfileOnSave)
                TryBindExistingAssetWithoutReload();
            if (_asset == null)
            {
                string assetPath = UniqueProfileAssetPath(directory, saveTex.name);
                _asset = CreateInstance<ScriptableSpriteSheetProfile>();
                AssetDatabase.CreateAsset(_asset, assetPath);
            }
            _createSeparateProfileOnSave = false;
            if (!ReferenceEquals(_asset.Data, _profile))
                _asset.Data = _profile;
            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssets();
            // After save, stay bound to whatever nested Data the SO retained.
            if (_asset.Data != null)
                _profile = _asset.Data;

            string savedPath = AssetDatabase.GetAssetPath(_asset);
            string jsonPath = savedPath.Replace(".asset", ".json");
            File.WriteAllText(jsonPath, _profile.ToJson());
            // Do not ImportAsset the sidecar: reimport refresh can reload the
            // related .asset and resurface a stale Frames length (8 after 8→7).

            var clip = CurrentClip;
            int frames = clip?.Frames?.Length ?? 0;
            if (quiet)
            {
                NoteProfileSaved(auto: true);
            }
            else
            {
                _status = clip != null
                    ? $"Saved {_asset.name}  •  {clip.Name}: {frames} frames"
                    : $"Saved {_asset.name}";
                ShowNotification(new GUIContent("Profile saved"));
                NoteProfileSaved(auto: false);
            }
            SpriteSheetProfileRecents.Remember(_asset);
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
            _asset = asset;
            _profile = asset.Data ?? new SpriteSheetProfile();
            if (asset.Data == null)
                asset.Data = _profile;
            _sheetFoldInitialized = false;
            EnsureProfile();
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

    }
}
