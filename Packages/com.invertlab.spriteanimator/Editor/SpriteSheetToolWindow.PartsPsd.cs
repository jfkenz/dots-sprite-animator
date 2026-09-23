using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // PSD import: pick a .psd / .psb, choose options in the import window, and every layer becomes a part.
    // The packed sheet is saved as "<file> Parts.png" beside the profile; importing the same file again
    // refreshes that sheet and the art of parts with the same names (their poses and keys stay).
    public sealed partial class SpriteSheetToolWindow
    {
        [SerializeField] string _partsPsdFolder;

        void OpenPsdImport()
        {
            if (_profile == null)
                return;
            if (_asset == null)
            {
                _status = "Save the profile first: the PSD sheet is saved beside it.";
                return;
            }
            string path = EditorUtility.OpenFilePanel("Import PSD", _partsPsdFolder ?? string.Empty, "psd,psb");
            if (string.IsNullOrEmpty(path))
                return;
            _partsPsdFolder = Path.GetDirectoryName(path);
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (IOException ex)
            {
                EditorUtility.DisplayDialog("Import PSD", ex.Message, "OK");
                return;
            }
            if (!SpritePsdReader.TryRead(bytes, out var doc, out string error))
            {
                EditorUtility.DisplayDialog("Import PSD", "Could not read " + Path.GetFileName(path) + ":\n\n" + error, "OK");
                return;
            }
            SpritePartsPsdImportWindow.Open(this, doc, path);
        }

        internal SpriteSheetProfile PsdTargetProfile => _profile;

        /// <summary>Writes the sheet texture, then adds / updates the parts. Returns the status line.</summary>
        internal string ImportPsd(SpritePsdReader.Document doc, string path, SpritePartsPsdImport.Options options)
        {
            _status = ImportPsdCore(doc, path, options);
            return _status;
        }

        string ImportPsdCore(SpritePsdReader.Document doc, string path, SpritePartsPsdImport.Options options)
        {
            if (_profile == null || _asset == null)
                return "No profile to import into.";
            var plan = SpritePartsPsdImport.BuildPlan(doc, options);
            if (plan.Error != null)
                return plan.Error;
            string name = Path.GetFileNameWithoutExtension(path);

            // The sheet texture beside the profile (Assets only; a profile inside a package saves to Assets/PSD Imports).
            string profilePath = AssetDatabase.GetAssetPath(_asset);
            string folder = profilePath.StartsWith("Assets/") ? Path.GetDirectoryName(profilePath)?.Replace('\\', '/') : "Assets/PSD Imports";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }
            string pngPath = folder + "/" + name + " Parts.png";
            var tex = new Texture2D(plan.AtlasWidth, plan.AtlasHeight, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(SpritePartsPsdImport.AtlasPixels(plan));
                tex.Apply(false);
                File.WriteAllBytes(pngPath, tex.EncodeToPNG());
            }
            finally
            {
                DestroyImmediate(tex);
            }
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(pngPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.isReadable = true; // Trace reads the pixels
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = Mathf.Max(2048, Mathf.NextPowerOfTwo(Mathf.Max(plan.AtlasWidth, plan.AtlasHeight)));
                importer.SaveAndReimport();
            }
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            if (atlas == null)
                return "The sheet texture could not be saved at " + pngPath + ".";

            RecordPartsUndo("Import PSD");
            var result = SpritePartsPsdImport.Apply(_profile, plan, name, atlas, options, TracePsdMask);
            SaveDirty();
            Repaint();
            if (!result.Ok)
                return result.Reason;
            string warn = plan.Warnings.Count > 0 ? " " + plan.Warnings.Count + " layer(s) skipped: " + plan.Warnings[0] : string.Empty;
            return "Imported " + name + ": " + result.Created + " new parts, " + result.Updated + " updated. Sheet: " + pngPath + "." + warn;
        }

        static SpritePartMeshDef TracePsdMask(Color32[] pixels, int w, int h)
        {
            var outline = new List<Vector2>();
            return TryTraceSpriteOutline(pixels, w, h, outline) ? SpritePartsMeshOps.FromOutline(outline) : null;
        }
    }

    /// <summary>PSD import options, a preview of what each layer becomes, and Import.</summary>
    sealed class SpritePartsPsdImportWindow : EditorWindow
    {
        static readonly float[] Scales = { 1f, 0.5f, 0.25f };
        static readonly string[] ScaleNames = { "100%", "50%", "25%" };

        SpriteSheetToolWindow _host;
        SpritePsdReader.Document _doc;
        string _path;
        readonly SpritePartsPsdImport.Options _options = new SpritePartsPsdImport.Options();
        SpritePartsPsdImport.Plan _plan;
        bool _dirty = true;
        Vector2 _scroll;

        public static void Open(SpriteSheetToolWindow host, SpritePsdReader.Document doc, string path)
        {
            var window = CreateInstance<SpritePartsPsdImportWindow>();
            window._host = host;
            window._doc = doc;
            window._path = path;
            window.titleContent = new GUIContent("Import PSD");
            window.minSize = new Vector2(440f, 520f);
            window.ShowUtility();
        }

        void OnGUI()
        {
            var profile = _host != null ? _host.PsdTargetProfile : null;
            if (_host == null || _doc == null || profile == null)
            {
                EditorGUILayout.HelpBox("The Sprite Sheet window was closed or reloaded. Open the PSD again.", MessageType.Info);
                if (GUILayout.Button("Close"))
                    Close();
                return;
            }
            EditorGUILayout.LabelField(Path.GetFileName(_path), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_doc.Width + " x " + _doc.Height + " px, " + _doc.Layers.Count + " layers and groups", EditorStyles.miniLabel);
            GUILayout.Space(4f);

            EditorGUI.BeginChangeCheck();
            _options.PixelsPerUnit = Mathf.Max(0.01f, EditorGUILayout.FloatField(new GUIContent("Pixels Per Unit", "Canvas pixels per world unit"), _options.PixelsPerUnit));
            int scale = System.Array.IndexOf(Scales, _options.Scale);
            scale = EditorGUILayout.Popup(new GUIContent("Texture Scale", "Smaller sheet, same size in the world"), Mathf.Max(0, scale), ScaleNames);
            _options.Scale = Scales[scale];
            _options.Origin = (SpritePsdOrigin)EditorGUILayout.EnumPopup(new GUIContent("Character Root", "Where the character's origin sits on the canvas"), _options.Origin);
            _options.Groups = (SpritePsdGroupMode)EditorGUILayout.EnumPopup(new GUIContent("Layer Groups",
                "Bones: a bone per group, its layers under it. Flatten: ignore groups. Merge Groups: one part per top-level group."), _options.Groups);
            _options.IncludeHidden = EditorGUILayout.Toggle(new GUIContent("Hidden Layers", "Import hidden layers as parts that are switched off"), _options.IncludeHidden);
            _options.UpdateExisting = EditorGUILayout.Toggle(new GUIContent("Update Same Names",
                "A part (or bone) with a layer's name gets the new art instead of a new part; its pose and keys stay"), _options.UpdateExisting);
            if (EditorGUI.EndChangeCheck())
                _dirty = true;
            if (_dirty)
            {
                _plan = SpritePartsPsdImport.BuildPlan(_doc, _options);
                _dirty = false;
            }

            GUILayout.Space(4f);
            int fresh = _plan.Error == null ? SpritePartsPsdImport.CountNewParts(profile, _plan, _options) : 0;
            int room = SpritePartIdUtility.MaxParts - (profile.PartsSlots?.Count ?? 0);
            if (_plan.Error != null)
                EditorGUILayout.HelpBox(_plan.Error, MessageType.Error);
            else
            {
                EditorGUILayout.LabelField("Sheet " + _plan.AtlasWidth + " x " + _plan.AtlasHeight + " px.  "
                                           + fresh + " new parts, " + (_plan.Items.Count - fresh) + " updated.  Room for " + room + ".",
                    EditorStyles.wordWrappedMiniLabel);
                if (fresh > room)
                    EditorGUILayout.HelpBox("Too many parts for the rig (" + SpritePartIdUtility.MaxParts + " max). Use Merge Groups, "
                                            + "turn off Hidden Layers, or merge layers in Photoshop.", MessageType.Warning);
            }
            foreach (string w in _plan.Warnings)
                EditorGUILayout.HelpBox(w, MessageType.Warning);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = _plan.Items.Count - 1; i >= 0; i--) // top layer first, like Photoshop
            {
                var it = _plan.Items[i];
                int depth = 0;
                for (int p = it.Parent; p >= 0 && depth < 16; p = _plan.Items[p].Parent)
                    depth++;
                string text = new string(' ', depth * 4) + (it.IsBone ? "◇ " : "") + it.Name
                              + (it.IsBone ? "" : "   " + it.Width + "x" + it.Height)
                              + (it.Visible ? "" : "   (hidden)")
                              + (it.ClipBase >= 0 ? "   clipped to " + _plan.Items[it.ClipBase].Name : "");
                EditorGUILayout.LabelField(text, it.Visible ? EditorStyles.label : EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(80f)))
                Close();
            using (new EditorGUI.DisabledScope(_plan.Error != null || fresh > room))
            {
                if (GUILayout.Button("Import", GUILayout.Width(100f)))
                {
                    string status = _host.ImportPsd(_doc, _path, _options);
                    _host.ShowNotification(new GUIContent(status.Length > 80 ? status.Substring(0, 80) + "..." : status));
                    Debug.Log("[PSD Import] " + status);
                    Close();
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
