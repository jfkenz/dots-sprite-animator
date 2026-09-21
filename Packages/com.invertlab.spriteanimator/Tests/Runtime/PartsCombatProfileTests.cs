using System;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    public class PartsCombatProfileTests
    {
        [Test]
        public void AnimatorLoadsEditsSavesAndReloadsCombatProfile()
        {
            const string template = "Packages/com.invertlab.spriteanimator/Runtime/DemoArt/PartsCombatProfile.asset";
            string folderName = "__CombatProfile_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName);
            string folder = "Assets/" + folderName;
            string path = folder + "/CombatProfile.asset";
            var type = Type.GetType("InvertLab.Sprites.DOTS.Editor.SpriteSheetToolWindow, InvertLab.SpriteAnimator.Editor");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            EditorWindow window = null;
            try
            {
                Assert.IsTrue(AssetDatabase.CopyAsset(template, path));
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(path);
                Assert.IsNotNull(type);
                window = (EditorWindow)ScriptableObject.CreateInstance(type);
                type.GetMethod("LoadAsset", flags).Invoke(window, new object[] { asset });
                var editing = (SpriteSheetProfile)type.GetField("_profile", flags).GetValue(window);
                Assert.AreEqual("Parts", type.GetField("_studioTab", flags).GetValue(window).ToString());
                Assert.AreEqual(4, editing.PartsSlots.Count);
                Assert.AreEqual(2, editing.PartsClips.Count);
                Assert.AreEqual(2, editing.PartsSkins.Count);
                Assert.AreEqual("hand.r", editing.PartsSlots.Find(s => s.SlotId == "weapon").ParentSlotId);
                var crop = SpriteSheetProfile.GetCellUvRect(editing.Sheets[0], 0);
                Assert.Less(crop.width, 0.5f, "Editor uses the cropped body, not the whole atlas.");
                editing.PartsClips.Find(c => c.Name == "Walk").Tracks[0].Keys[1].Rotation = 23f;
                type.GetMethod("SaveProfile", flags).Invoke(window, new object[] { true });
                type.GetMethod("LoadAsset", flags).Invoke(window, new object[] { asset });
                var reloaded = (SpriteSheetProfile)type.GetField("_profile", flags).GetValue(window);
                Assert.AreEqual(23f, reloaded.PartsClips.Find(c => c.Name == "Walk").Tracks[0].Keys[1].Rotation);
                Assert.IsNotNull(reloaded.Sheets[0].Texture);
                Assert.IsTrue(SpritePartsClipConversion.TryBuildBlob(reloaded, Allocator.Temp, out var blob, out var error), error);
                blob.Dispose();
                var source = AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(template);
                Assert.AreEqual(0f, source.Data.PartsClips.Find(c => c.Name == "Walk").Tracks[0].Keys[1].Rotation);
            }
            finally
            {
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
