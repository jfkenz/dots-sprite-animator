using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>EditorPrefs-backed recent and favorite lists for sprite animator profiles.</summary>
    static class SpriteSheetProfileRecents
    {
        const string RecentPrefsKey = "InvertLab.SpriteAnimator.RecentProfileGuids";
        const string FavoritePrefsKey = "InvertLab.SpriteAnimator.FavoriteProfileGuids";
        const int MaxRecent = 12;

        public static void Remember(ScriptableSpriteSheetProfile asset)
            => PushGuid(RecentPrefsKey, asset, MaxRecent);

        public static List<ScriptableSpriteSheetProfile> LoadAssets()
            => LoadFromKey(RecentPrefsKey);

        public static bool IsFavorite(ScriptableSpriteSheetProfile asset)
        {
            string guid = GuidOf(asset);
            return !string.IsNullOrEmpty(guid) && ReadGuids(FavoritePrefsKey).Contains(guid);
        }

        public static void ToggleFavorite(ScriptableSpriteSheetProfile asset)
        {
            string guid = GuidOf(asset);
            if (string.IsNullOrEmpty(guid))
                return;
            var list = ReadGuids(FavoritePrefsKey);
            if (!list.Remove(guid))
                list.Insert(0, guid);
            EditorPrefs.SetString(FavoritePrefsKey, string.Join("|", list));
        }

        public static List<ScriptableSpriteSheetProfile> LoadFavorites()
            => LoadFromKey(FavoritePrefsKey);

        public static ScriptableSpriteSheetProfile FindSibling(Texture2D texture)
        {
            if (texture == null)
                return null;
            string texturePath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(texturePath))
                return null;
            string directory = Path.GetDirectoryName(texturePath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory))
                return null;
            return AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(
                $"{directory}/{texture.name}_profile.asset");
        }

        public static Texture2D PreviewSheet(ScriptableSpriteSheetProfile asset)
        {
            var data = asset?.Data;
            if (data == null)
                return null;
            data.EnsureSheets();
            return data.SheetAt(0)?.Texture ?? data.Sheet;
        }

        static void PushGuid(string key, ScriptableSpriteSheetProfile asset, int maxCount)
        {
            string guid = GuidOf(asset);
            if (string.IsNullOrEmpty(guid))
                return;
            var list = ReadGuids(key);
            list.RemoveAll(entry => entry == guid);
            list.Insert(0, guid);
            if (maxCount > 0 && list.Count > maxCount)
                list.RemoveRange(maxCount, list.Count - maxCount);
            EditorPrefs.SetString(key, string.Join("|", list));
        }

        static List<ScriptableSpriteSheetProfile> LoadFromKey(string key)
        {
            var result = new List<ScriptableSpriteSheetProfile>();
            var guids = ReadGuids(key);
            bool pruned = false;
            for (int i = 0; i < guids.Count; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = string.IsNullOrEmpty(path)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(path);
                if (asset == null)
                {
                    pruned = true;
                    continue;
                }
                result.Add(asset);
            }

            if (pruned)
                WriteGuids(key, result);
            return result;
        }

        static string GuidOf(ScriptableSpriteSheetProfile asset)
        {
            if (asset == null)
                return null;
            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
        }

        static List<string> ReadGuids(string key)
        {
            var list = new List<string>();
            string stored = EditorPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(stored))
                return list;
            string[] parts = stored.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (!list.Contains(parts[i]))
                    list.Add(parts[i]);
            }
            return list;
        }

        static void WriteGuids(string key, List<ScriptableSpriteSheetProfile> assets)
        {
            var guids = new List<string>(assets.Count);
            for (int i = 0; i < assets.Count; i++)
            {
                string guid = GuidOf(assets[i]);
                if (!string.IsNullOrEmpty(guid))
                    guids.Add(guid);
            }
            EditorPrefs.SetString(key, string.Join("|", guids));
        }
    }

    /// <summary>
    /// Load-profile picker: related folder matches, recents, project search, Browse last.
    /// </summary>
    sealed class SpriteSheetProfileLoadPopup : PopupWindowContent
    {
        readonly SpriteSheetToolWindow _host;
        string _search = string.Empty;
        Vector2 _scroll;
        bool _focusSearch = true;

        public SpriteSheetProfileLoadPopup(SpriteSheetToolWindow host)
        {
            _host = host;
        }

        public override Vector2 GetWindowSize() => new Vector2(360f, 420f);

        public override void OnGUI(Rect rect)
        {
            var inner = new Rect(8f, 8f, rect.width - 16f, rect.height - 16f);
            GUILayout.BeginArea(inner);

            GUILayout.Label("LOAD PROFILE", EditorStyles.boldLabel);
            var current = _host.ProfileAsset;
            GUILayout.Label(current != null ? $"Current  {current.name}" : "No profile loaded",
                EditorStyles.miniLabel);

            GUI.SetNextControlName("SpriteAnimatorLoadProfileSearch");
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            if (_focusSearch)
            {
                EditorGUI.FocusTextInControl("SpriteAnimatorLoadProfileSearch");
                _focusSearch = false;
            }

            GUILayout.Space(6f);
            _scroll = GUILayout.BeginScrollView(_scroll);

            var related = CollectRelated(_host);
            var recent = SpriteSheetProfileRecents.LoadAssets();
            var favorites = SpriteSheetProfileRecents.LoadFavorites();
            string query = _search?.Trim() ?? string.Empty;
            bool searching = query.Length > 0;

            if (searching)
            {
                DrawSection("SEARCH");
                var hits = SearchProject(query);
                if (hits.Count == 0)
                    GUILayout.Label("No profiles match.", EditorStyles.miniLabel);
                else
                    DrawAssetList(hits, current);
            }
            else
            {
                DrawSection("FAVORITES");
                if (favorites.Count == 0)
                    GUILayout.Label("Star a profile to pin it here.", EditorStyles.miniLabel);
                else
                    DrawAssetList(favorites, current);

                GUILayout.Space(8f);
                DrawSection("RELATED");
                var relatedOnly = Except(related, favorites);
                if (relatedOnly.Count == 0)
                    GUILayout.Label("No other profiles next to the current sheet or asset.", EditorStyles.miniLabel);
                else
                    DrawAssetList(relatedOnly, current);

                GUILayout.Space(8f);
                DrawSection("RECENT");
                var recentOnly = Except(Except(recent, related), favorites);
                if (recentOnly.Count == 0)
                    GUILayout.Label("Open a profile to build this list.", EditorStyles.miniLabel);
                else
                    DrawAssetList(recentOnly, current);
            }

            GUILayout.EndScrollView();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Browse…", "Open a file picker as a last resort.")))
            {
                editorWindow.Close();
                _host.BrowseAndLoadProfile();
            }

            GUILayout.EndArea();
        }

        void DrawSection(string title)
        {
            GUILayout.Label(title, EditorStyles.miniBoldLabel);
        }

        void DrawAssetList(List<ScriptableSpriteSheetProfile> assets, ScriptableSpriteSheetProfile current)
        {
            for (int i = 0; i < assets.Count; i++)
            {
                var asset = assets[i];
                if (asset == null)
                    continue;
                DrawAssetRow(asset, current != null && current == asset);
            }
        }

        void DrawAssetRow(ScriptableSpriteSheetProfile asset, bool isCurrent)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            var row = GUILayoutUtility.GetRect(1f, 40f);
            var evt = Event.current;
            bool hover = row.Contains(evt.mousePosition);
            if (evt.type == EventType.Repaint)
            {
                if (isCurrent)
                    EditorGUI.DrawRect(row, new Color(0.18f, 0.38f, 0.22f, 0.45f));
                else if (hover)
                    EditorGUI.DrawRect(row, new Color(0.18f, 0.55f, 0.82f, 0.22f));
                else
                    EditorGUI.DrawRect(row, new Color(0.12f, 0.13f, 0.16f, 0.9f));
            }

            var starRect = new Rect(row.x + 4f, row.y + 10f, 18f, 20f);
            var thumbRect = new Rect(starRect.xMax + 4f, row.y + 4f, 32f, 32f);
            var nameRect = new Rect(thumbRect.xMax + 8f, row.y + 4f, row.xMax - thumbRect.xMax - 14f, 18f);
            var pathRect = new Rect(nameRect.x, row.y + 22f, nameRect.width, 14f);

            bool favorite = SpriteSheetProfileRecents.IsFavorite(asset);
            if (GUI.Button(starRect, new GUIContent(favorite ? "★" : "☆",
                    favorite ? "Unpin favorite" : "Pin favorite"), EditorStyles.label))
            {
                SpriteSheetProfileRecents.ToggleFavorite(asset);
                evt.Use();
                editorWindow.Repaint();
                return;
            }

            var preview = SpriteSheetProfileRecents.PreviewSheet(asset);
            EditorGUI.DrawRect(thumbRect, new Color(0.08f, 0.09f, 0.11f, 1f));
            if (preview != null)
                GUI.DrawTexture(thumbRect, preview, ScaleMode.ScaleToFit, true);

            string label = isCurrent ? $"{asset.name}  (loaded)" : asset.name;
            GUI.Label(nameRect, label, EditorStyles.label);
            GUI.Label(pathRect, ShortPath(path), EditorStyles.miniLabel);

            if (evt.type == EventType.MouseDown && row.Contains(evt.mousePosition) &&
                !starRect.Contains(evt.mousePosition))
            {
                if (evt.button == 1)
                {
                    ShowRowMenu(asset);
                    evt.Use();
                    return;
                }

                if (evt.button == 0 && !isCurrent)
                {
                    evt.Use();
                    editorWindow.Close();
                    _host.ApplyLoadedProfile(asset);
                }
                else if (evt.button == 0)
                {
                    EditorGUIUtility.PingObject(asset);
                    evt.Use();
                }
            }
        }

        static void ShowRowMenu(ScriptableSpriteSheetProfile asset)
        {
            bool favorite = SpriteSheetProfileRecents.IsFavorite(asset);
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(favorite ? "Unpin Favorite" : "Pin Favorite"), false,
                () => SpriteSheetProfileRecents.ToggleFavorite(asset));
            menu.AddItem(new GUIContent("Ping in Project"), false, () => EditorGUIUtility.PingObject(asset));
            menu.AddItem(new GUIContent("Select in Project"), false, () => Selection.activeObject = asset);
            menu.ShowAsContext();
        }

        static string ShortPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            const string prefix = "Assets/";
            return path.StartsWith(prefix, StringComparison.Ordinal) ? path.Substring(prefix.Length) : path;
        }

        static List<ScriptableSpriteSheetProfile> CollectRelated(SpriteSheetToolWindow host)
        {
            var list = new List<ScriptableSpriteSheetProfile>();
            var seen = new HashSet<EntityId>();

            void Add(ScriptableSpriteSheetProfile asset)
            {
                if (asset == null || !seen.Add(asset.GetEntityId()))
                    return;
                list.Add(asset);
            }

            var folders = new List<string>();
            void AddFolder(string folder)
            {
                if (string.IsNullOrEmpty(folder))
                    return;
                folder = folder.Replace('\\', '/');
                if (!folder.StartsWith("Assets", StringComparison.Ordinal))
                    return;
                if (!folders.Contains(folder))
                    folders.Add(folder);
            }

            string currentPath = host.ProfileAsset != null
                ? AssetDatabase.GetAssetPath(host.ProfileAsset)
                : string.Empty;
            AddFolder(string.IsNullOrEmpty(currentPath) ? null : Path.GetDirectoryName(currentPath));

            var textures = host.ProfileSheetTextures();
            for (int i = 0; i < textures.Count; i++)
            {
                string texturePath = AssetDatabase.GetAssetPath(textures[i]);
                if (string.IsNullOrEmpty(texturePath))
                    continue;
                AddFolder(Path.GetDirectoryName(texturePath));
                string directory = Path.GetDirectoryName(texturePath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(directory))
                    Add(AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(
                        $"{directory}/{textures[i].name}_profile.asset"));
            }

            for (int i = 0; i < folders.Count; i++)
            {
                if (!AssetDatabase.IsValidFolder(folders[i]))
                    continue;
                string[] guids = AssetDatabase.FindAssets("t:ScriptableSpriteSheetProfile", new[] { folders[i] });
                for (int g = 0; g < (guids?.Length ?? 0); g++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[g]);
                    Add(AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(path));
                }
            }

            if (Selection.activeObject is ScriptableSpriteSheetProfile selected)
                Add(selected);

            Add(host.ProfileAsset);
            return list;
        }

        static List<ScriptableSpriteSheetProfile> SearchProject(string query)
        {
            var list = new List<ScriptableSpriteSheetProfile>();
            string[] guids = AssetDatabase.FindAssets("t:ScriptableSpriteSheetProfile");
            for (int i = 0; i < (guids?.Length ?? 0); i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(path);
                if (asset == null)
                    continue;
                if (asset.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    path.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                list.Add(asset);
            }
            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        static List<ScriptableSpriteSheetProfile> Except(
            List<ScriptableSpriteSheetProfile> source, List<ScriptableSpriteSheetProfile> skip)
        {
            var ids = new HashSet<EntityId>();
            for (int i = 0; i < skip.Count; i++)
            {
                if (skip[i] != null)
                    ids.Add(skip[i].GetEntityId());
            }

            var result = new List<ScriptableSpriteSheetProfile>();
            for (int i = 0; i < source.Count; i++)
            {
                var asset = source[i];
                if (asset != null && !ids.Contains(asset.GetEntityId()))
                    result.Add(asset);
            }
            return result;
        }
    }
}
