using System.Text;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    static class SpriteAnimatorToolsMenu
    {
        const string QuickStartPath = "Packages/com.invertlab.spriteanimator/Documentation~/QuickStart.md";
        const string ReadmePath = "Packages/com.invertlab.spriteanimator/README.md";

        [MenuItem("Tools/DOTS Sprite Animator/Validate Installation")]
        public static void ValidateInstallation()
        {
            var message = new StringBuilder();
            bool ok = true;
            ok &= RequirePackage("com.unity.entities", message);
            ok &= RequirePackage("com.unity.entities.graphics", message);
            ok &= RequirePackage("com.unity.render-pipelines.universal", message);

            if (!SpriteShaderLibrary.TryFindAll(out string shaderMessage))
            {
                ok = false;
                message.AppendLine(shaderMessage);
            }
            else
            {
                message.AppendLine(shaderMessage);
            }

            string title = ok
                ? "DOTS Sprite Animator — Validation Succeeded"
                : "DOTS Sprite Animator — Validation Failed";
            string body = message.ToString().Trim();
            if (string.IsNullOrWhiteSpace(body))
                body = ok ? "Package dependencies are installed." : "Validation failed.";

            if (ok)
                Debug.Log(body);
            else
                Debug.LogError(body);
            EditorUtility.DisplayDialog(title, body, "OK");
        }

        [MenuItem("Tools/DOTS Sprite Animator/Help")]
        public static void OpenHelp()
        {
            if (TryOpenProjectAsset(QuickStartPath))
                return;
            if (TryOpenProjectAsset(ReadmePath))
                return;

            string fallback = $"file://{Application.dataPath}/../Packages/com.invertlab.spriteanimator/README.md";
            Application.OpenURL(fallback);
        }

        static bool RequirePackage(string id, StringBuilder message)
        {
            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            if (packages == null)
            {
                message.AppendLine($"[UNKNOWN] Could not query Unity package registry for {id}");
                return false;
            }
            for (int i = 0; i < packages.Length; i++)
            {
                if (packages[i].name != id)
                    continue;
                message.AppendLine($"[OK] {id} ({packages[i].version})");
                return true;
            }
            message.AppendLine($"[MISSING] {id}");
            return false;
        }

        static bool TryOpenProjectAsset(string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (asset == null)
                return false;
            AssetDatabase.OpenAsset(asset);
            return true;
        }
    }
}
