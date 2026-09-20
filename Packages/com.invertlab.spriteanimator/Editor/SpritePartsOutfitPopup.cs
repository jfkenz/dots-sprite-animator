using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>Apply Outfit: map a source skin/outfit onto this profile by semantic role.</summary>
    sealed class SpritePartsOutfitPopup : PopupWindowContent
    {
        readonly SpriteSheetToolWindow _host;
        ScriptableSpriteSheetProfile _source;
        int _skinIndex;
        Vector2 _scroll;

        public SpritePartsOutfitPopup(SpriteSheetToolWindow host)
        {
            _host = host;
            _source = host.ProfileAsset;
        }

        public override Vector2 GetWindowSize() => new Vector2(420f, 380f);

        public override void OnGUI(Rect rect)
        {
            var inner = new Rect(8f, 8f, rect.width - 16f, rect.height - 16f);
            GUILayout.BeginArea(inner);
            GUILayout.Label("APPLY OUTFIT", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Maps a source skin/outfit onto this profile by semantic role (Body, Head, Weapon, Offhand), not only SlotId. Appearance binding only - this does not retarget motion curves.",
                MessageType.Info);

            _source = (ScriptableSpriteSheetProfile)EditorGUILayout.ObjectField(
                "Source Profile", _source, typeof(ScriptableSpriteSheetProfile), false);
            var data = _source != null ? _source.Data : null;
            var dest = _host.EditingProfile;
            if (data == null || dest == null)
            {
                if (GUILayout.Button("Cancel"))
                    editorWindow.Close();
                GUILayout.EndArea();
                return;
            }

            var skins = data.PartsSkins ?? new List<SpritePartsSkinDef>();
            var options = new List<string> { "(role-tagged appearances)" };
            for (int i = 0; i < skins.Count; i++)
                options.Add(skins[i]?.Name ?? ("Skin " + i));
            int popup = _skinIndex < 0 ? 0 : Mathf.Clamp(_skinIndex + 1, 0, options.Count - 1);
            int next = EditorGUILayout.Popup("Source Skin", popup, options.ToArray());
            _skinIndex = next <= 0 ? -1 : next - 1;

            var plan = SpritePartsOutfitMapping.PlanApply(data, _skinIndex, dest);
            _scroll = GUILayout.BeginScrollView(_scroll);
            if (plan.Ok)
            {
                GUILayout.Label(plan.Summary, EditorStyles.wordWrappedMiniLabel);
                for (int i = 0; i < plan.Bindings.Count; i++)
                {
                    var b = plan.Bindings[i];
                    string line = b.Unmapped
                        ? $"{b.Role}  {b.DestinationSlotId}  - unmapped (unchanged)"
                        : $"{b.Role}  {b.DestinationSlotId}  <-  {b.SourceAppearanceId}";
                    GUILayout.Label(line, EditorStyles.miniLabel);
                }
                if (plan.Unmapped.Count > 0)
                {
                    GUILayout.Space(4f);
                    GUILayout.Label("Unmapped (destination unchanged):", EditorStyles.miniBoldLabel);
                    for (int i = 0; i < plan.Unmapped.Count; i++)
                        GUILayout.Label("* " + plan.Unmapped[i], EditorStyles.miniLabel);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(plan.Reason ?? "Cannot apply.", MessageType.Error);
                for (int i = 0; i < plan.Conflicts.Count; i++)
                    GUILayout.Label("* " + plan.Conflicts[i], EditorStyles.wordWrappedMiniLabel);
            }
            GUILayout.EndScrollView();

            using (new EditorGUI.DisabledScope(!plan.Ok))
            {
                if (GUILayout.Button("Apply", GUILayout.Height(24f)))
                {
                    _host.ApplyOutfitMapping(_source, _skinIndex);
                    editorWindow.Close();
                    GUILayout.EndArea();
                    return;
                }
            }
            if (GUILayout.Button("Cancel"))
                editorWindow.Close();
            GUILayout.EndArea();
        }
    }
}
