using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>Creates the initial sample asset only. Playback reads the saved profile,
    /// so user edits are never replaced by this template.</summary>
    public static class PartsCombatDemoProfile
    {
        public const string AssetPath = "Packages/com.invertlab.spriteanimator/Runtime/DemoArt/PartsCombatProfile.asset";

        public static ScriptableSpriteSheetProfile EnsureTemplate(Texture2D atlas)
        {
            var existing = AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(AssetPath);
            if (existing != null) return existing;
            var asset = ScriptableObject.CreateInstance<ScriptableSpriteSheetProfile>();
            var data = asset.Data;
            data.AnimKind = SpriteAnimKind.Parts;
            data.PartsSchemaVersion = 1;
            data.PartsDefaultClipId = "idle";
            data.PartsDefaultSkinId = "blaster";
            data.PartsRootBoundsSize = new Vector2(2.6f, 2f);
            string[] names = { "Body", "Glove", "Blaster", "Rifle" };
            for (int i = 0; i < PartsCombatDemoRig.Crops.Length; i++)
            {
                var crop = PartsCombatDemoRig.Crops[i] * 1254f;
                data.Sheets.Add(new SpriteSheetDef
                {
                    Name = names[i], Texture = atlas, Columns = 1, Rows = 1,
                    PixelsPerUnit = 400f, Pivot = i >= 2 ? (Vector2)PartsCombatDemoRig.Grip : new Vector2(0.5f, 0.5f),
                    CellLayoutMode = SpriteSheetCellLayoutMode.Cropped,
                    CroppedCellRects = new[] { new RectInt(Mathf.RoundToInt(crop.z), Mathf.RoundToInt(crop.w),
                        Mathf.RoundToInt(crop.x), Mathf.RoundToInt(crop.y)) },
                });
            }
            // Export the code-only demo's seed once, preserving its exact poses and art.
            using (var blob = PartsCombatDemoRig.Build())
            {
                ref var set = ref blob.Value;
                for (int i = 0; i < set.Slots.Length; i++)
                {
                    ref var slot = ref set.Slots[i];
                    data.PartsSlots.Add(new SpritePartSlotDef
                    {
                        Name = slot.Name.ToString(), SlotId = slot.SlotId.ToString(), SiblingOrder = i,
                        ParentSlotId = slot.ParentSlotIndex < 0 ? "" : set.Slots[slot.ParentSlotIndex].SlotId.ToString(),
                        RestPosition = slot.RestPosition, RestRotation = slot.RestRotation, RestScale = slot.RestScale,
                        DrawRank = slot.DrawRank, DefaultAppearanceId = set.Appearances[slot.DefaultAppearanceIndex].AppearanceId.ToString(),
                    });
                }
                for (int i = 0; i < set.Appearances.Length; i++)
                {
                    ref var art = ref set.Appearances[i];
                    data.PartsAppearances.Add(new SpritePartAppearanceDef
                    {
                        Name = names[i], AppearanceId = art.AppearanceId.ToString(), SheetIndex = art.SheetTableIndex,
                        CellIndex = art.CellIndex, LogicalWorldSize = art.LogicalWorldSize,
                        PivotSource = SpritePartPivotSource.Override, PivotOverride = art.Pivot,
                    });
                }
                for (int i = 0; i < set.Clips.Length; i++)
                {
                    ref var source = ref set.Clips[i];
                    var clip = new SpritePartsClipDef { Name = source.Name.ToString(), ClipId = source.ClipId.ToString(),
                        Duration = source.Duration, Speed = source.SpeedMultiplier, WrapMode = source.WrapMode };
                    for (int t = 0; t < source.Tracks.Length; t++)
                    {
                        ref var track = ref source.Tracks[t];
                        var target = new SpritePartsTrackDef { SlotId = set.Slots[track.SlotIndex].SlotId.ToString() };
                        for (int k = 0; k < track.Keys.Length; k++)
                        {
                            ref var key = ref track.Keys[k];
                            target.Keys.Add(new SpritePartsKeyDef { Time = key.Time, Position = key.Position,
                                Rotation = key.Rotation, Scale = key.Scale, EaseMode = key.EaseMode });
                        }
                        clip.Tracks.Add(target);
                    }
                    data.PartsClips.Add(clip);
                }
                for (int i = 0; i < set.SkinPatches.Length; i++)
                {
                    ref var source = ref set.SkinPatches[i];
                    var skin = new SpritePartsSkinDef { Name = source.SkinId.ToString(), SkinId = source.SkinId.ToString() };
                    for (int b = 0; b < source.Bindings.Length; b++)
                    {
                        ref var binding = ref source.Bindings[b];
                        skin.Bindings.Add(new SpritePartsSkinBindingDef { SlotId = set.Slots[binding.SlotIndex].SlotId.ToString(),
                            AppearanceId = set.Appearances[binding.AppearanceIndex].AppearanceId.ToString() });
                    }
                    data.PartsSkins.Add(skin);
                }
            }
            data.SyncLegacyFromSheet(0);
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        public static ScriptableSpriteSheetProfile CopyToAssets(ScriptableSpriteSheetProfile source)
        {
            if (!AssetDatabase.IsValidFolder("Assets/PartsCombatExample"))
                AssetDatabase.CreateFolder("Assets", "PartsCombatExample");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/PartsCombatExample/PartsCombatProfile.asset");
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path))
                throw new System.InvalidOperationException("Could not create the editable example profile.");
            return AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(path);
        }

        public static void Open(ScriptableSpriteSheetProfile profile)
        {
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
            EditorWindow.GetWindow<SpriteSheetToolWindow>().ApplyLoadedProfile(profile);
        }
    }

    [CustomEditor(typeof(PartsCombatDemo))]
    public sealed class PartsCombatDemoEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var demo = (PartsCombatDemo)target;
            EditorGUILayout.HelpBox("Edit Idle/Walk, the hierarchy, and skins in the animator. Save Profile, then restart Play. " +
                "Gameplay controls hand.r aiming and weapon recoil. Keep those slot IDs, clip names Idle/Walk, and skin IDs blaster/rifle.", MessageType.Info);
            using (new EditorGUI.DisabledScope(demo.Profile == null || Application.isPlaying))
            {
                if (GUILayout.Button("Open Profile in Animator")) PartsCombatDemoProfile.Open(demo.Profile);
                if (GUILayout.Button("Create Editable Profile Copy"))
                {
                    var copy = PartsCombatDemoProfile.CopyToAssets(demo.Profile);
                    Undo.RecordObject(demo, "Assign editable Parts profile");
                    demo.Profile = copy;
                    EditorUtility.SetDirty(demo);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(demo.gameObject.scene);
                    PartsCombatDemoProfile.Open(copy);
                }
            }
        }
    }
}
