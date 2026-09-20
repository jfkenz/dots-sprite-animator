using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>
    /// Bake Parts Clip to Frame Clip: sample at configurable FPS into a new
    /// frame clip on the same profile. Does not switch AnimKind.
    /// </summary>
    sealed class SpritePartsToFrameBakePopup : PopupWindowContent
    {
        readonly SpriteSheetToolWindow _host;
        int _clipIndex;
        float _fps = SpritePartsToFrameBake.DefaultFps;

        public SpritePartsToFrameBakePopup(SpriteSheetToolWindow host, int clipIndex)
        {
            _host = host;
            _clipIndex = clipIndex;
        }

        public override Vector2 GetWindowSize() => new Vector2(400f, 280f);

        public override void OnGUI(Rect rect)
        {
            var inner = new Rect(8f, 8f, rect.width - 16f, rect.height - 16f);
            GUILayout.BeginArea(inner);
            GUILayout.Label("BAKE PARTS CLIP TO FRAME CLIP", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Export/bake: samples the Parts clip over time into a new frame clip and sheet. Runtime AnimKind is not changed. Switch to Frames separately if you want the character to play the baked clip.",
                MessageType.Info);

            var profile = _host.EditingProfile;
            if (profile?.PartsClips == null || profile.PartsClips.Count == 0)
            {
                GUILayout.Label("No Parts clips on this profile.", EditorStyles.miniLabel);
                if (GUILayout.Button("Cancel"))
                    editorWindow.Close();
                GUILayout.EndArea();
                return;
            }

            var names = new string[profile.PartsClips.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = profile.PartsClips[i]?.Name ?? ("Clip " + i);
            _clipIndex = Mathf.Clamp(_clipIndex, 0, names.Length - 1);
            _clipIndex = EditorGUILayout.Popup("Parts Clip", _clipIndex, names);
            _fps = EditorGUILayout.FloatField("FPS", _fps);

            var plan = SpritePartsToFrameBake.PlanBake(profile, _clipIndex, _fps);
            if (plan.Ok)
            {
                GUILayout.Label(plan.Summary, EditorStyles.wordWrappedMiniLabel);
                GUILayout.Label(
                    $"{plan.FrameCount} frame(s) | {plan.Columns}x{plan.Rows} cells | {plan.CellWidth}x{plan.CellHeight} px",
                    EditorStyles.miniLabel);
                for (int i = 0; i < plan.Notes.Count; i++)
                    GUILayout.Label(plan.Notes[i], EditorStyles.wordWrappedMiniLabel);
                if (plan.Unsupported.Count > 0)
                {
                    GUILayout.Space(4f);
                    GUILayout.Label("Not transferred / not full fidelity:", EditorStyles.miniBoldLabel);
                    for (int i = 0; i < plan.Unsupported.Count; i++)
                        GUILayout.Label("* " + plan.Unsupported[i], EditorStyles.wordWrappedMiniLabel);
                }
                if (GUILayout.Button("Bake", GUILayout.Height(24f)))
                {
                    _host.BakePartsClipToFrame(_clipIndex, _fps);
                    editorWindow.Close();
                    GUILayout.EndArea();
                    return;
                }
            }
            else
            {
                EditorGUILayout.HelpBox(plan.Reason ?? "Cannot bake.", MessageType.Error);
            }

            if (GUILayout.Button("Cancel"))
                editorWindow.Close();
            GUILayout.EndArea();
        }
    }
}
