using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>
    /// Toolbar Settings popup: auto-save the dirty sprite profile on an interval.
    /// Prefs live in EditorPrefs (per-user), not in the asset.
    /// </summary>
    internal sealed class SpriteSheetToolSettingsPopup : PopupWindowContent
    {
        readonly SpriteSheetToolWindow _window;

        public SpriteSheetToolSettingsPopup(SpriteSheetToolWindow window)
        {
            _window = window;
        }

        public override Vector2 GetWindowSize() => new(280f, 132f);

        public override void OnGUI(Rect rect)
        {
            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            GUILayout.Space(4f);

            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.ToggleLeft(
                new GUIContent("Auto-save profile",
                    "When on, saves the dirty profile asset on an interval. " +
                    "Only runs with a bound .asset that Unity marks dirty. " +
                    "New unsaved profiles still need Save Profile once."),
                _window.AutoSaveEnabled);
            int minutes = EditorGUILayout.IntField(
                new GUIContent("Every (minutes)",
                    "Auto-save interval. Clamped to 1-120. Default 10."),
                _window.AutoSaveIntervalMinutes);
            if (EditorGUI.EndChangeCheck())
            {
                _window.SetAutoSavePrefs(enabled, minutes);
            }

            GUILayout.Space(6f);
            string last = _window.AutoSaveLastStatus;
            EditorGUILayout.LabelField("Last auto-save",
                string.IsNullOrEmpty(last) ? "-" : last,
                EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                "Manual Save Profile still works anytime.",
                EditorStyles.miniLabel);
        }
    }
}
