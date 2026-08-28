using System.Collections.Generic;
using System.Linq;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InvertLab.Sprites.DOTS.Editor
{
    public static class SocketsExampleBuilder
    {
        const string Root = "Assets/Samples/Sockets";
        const string ProfilePath = Root + "/SocketsProfile.asset";
        const string ScenePath = Root + "/SocketsExample.unity";
        const string SubScenePath = Root + "/SocketsExample_SubScene.unity";
        const string CharacterPath = "Assets/Samples/Showcase/Sword Character Prototype_All Frames.png";
        const string SwordPath = "Assets/Samples/Showcase/sword_angles.png";

        [MenuItem("Tools/DOTS Sprite Animator/Build Sockets Sample")]
        public static void Build()
        {
            var character = AssetDatabase.LoadAssetAtPath<Texture2D>(CharacterPath);
            if (character == null)
                throw new System.InvalidOperationException("Missing Showcase character texture: " + CharacterPath);

            var profile = BuildProfile(character);
            BuildSubScene(profile);
            BuildMainScene(profile);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Debug.Log("[Sockets Sample] Built " + ScenePath + ". Enter Play; use [ ], 1, or 2.");
        }

        static ScriptableSpriteSheetProfile BuildProfile(Texture2D character)
        {
            var profile = AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<ScriptableSpriteSheetProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            var sheet = new SpriteSheetDef
            {
                Name = "Sword Character Prototype",
                Texture = character,
                Columns = 8,
                Rows = 8,
                PixelsPerUnit = 100f,
                Pivot = new Vector2(0.5f, 0.5f),
            };

            profile.Data = new SpriteSheetProfile
            {
                Sheet = character,
                Columns = sheet.Columns,
                Rows = sheet.Rows,
                PixelsPerUnit = sheet.PixelsPerUnit,
                Pivot = sheet.Pivot,
                Sheets = new List<SpriteSheetDef> { sheet },
                Clips = new List<SpriteClipDef>
                {
                    MakeClip("Idle", 0, 6f, IdlePositions, IdleAngles),
                    MakeClip("Attack", 1, 10f, AttackPositions, AttackAngles),
                },
            };

            EditorUtility.SetDirty(profile);
            return profile;
        }

        static SpriteClipDef MakeClip(
            string name,
            int row,
            float frameRate,
            IReadOnlyList<Vector2> positions,
            IReadOnlyList<float> angles)
        {
            var clip = new SpriteClipDef
            {
                Name = name,
                SheetIndex = 0,
                Row = row,
                Frames = Enumerable.Range(0, 8).ToArray(),
                FrameRate = frameRate,
                WrapMode = SpriteAnimWrap.Loop,
                Sockets = new List<FrameSocketDef>(),
            };
            clip.EnsureFrameData();

            for (int frame = 0; frame < clip.Frames.Length; frame++)
            {
                clip.Sockets.Add(new FrameSocketDef
                {
                    Name = "Weapon",
                    FrameIndex = frame,
                    LocalPosition = positions[frame],
                    LocalAngle = angles[frame],
                });
            }

            return clip;
        }

        static void BuildSubScene(ScriptableSpriteSheetProfile profile)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Socket Character (CPU Playback)";
            Object.DestroyImmediate(quad.GetComponent<Collider>());

            var set = quad.AddComponent<SpriteAnimSetAuthoring>();
            set.Profile = profile;
            set.InitialClipIndex = 0;
            set.ShowScenePreview = true;
            set.ApplyFromProfile();

            var player = quad.AddComponent<SpriteAnimPlayerAuthoring>();
            player.ClipIndex = 0;
            player.Speed = 1f;
            player.Playing = true;
            player.PlayOnEnable = true;

            var sheet = profile.Data.SheetForClip(profile.Data.Clips[0]);
            float worldSize = SpriteSheetProfile.GetWorldHeight(sheet);
            quad.transform.localScale = new Vector3(worldSize, worldSize, worldSize);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SubScenePath);
        }

        static void BuildMainScene(ScriptableSpriteSheetProfile profile)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 1.65f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.075f, 1f);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            var subSceneObject = new GameObject("Sockets Character SubScene");
            var subScene = subSceneObject.AddComponent<SubScene>();
            subScene.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SubScenePath);
            subScene.AutoLoadScene = true;

            var itemRoot = new GameObject("Weapon Socket Item");
            itemRoot.transform.position = new Vector3(0f, 0f, -0.1f);

            var swordVisual = new GameObject("Sword Visual");
            swordVisual.transform.SetParent(itemRoot.transform, false);
            swordVisual.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            swordVisual.transform.localScale = Vector3.one * 0.38f;
            var swordRenderer = swordVisual.AddComponent<SpriteRenderer>();
            swordRenderer.sprite = AssetDatabase.LoadAllAssetsAtPath(SwordPath)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.name)
                .FirstOrDefault();
            swordRenderer.sortingOrder = 10;

            if (swordRenderer.sprite == null)
                throw new System.InvalidOperationException("Missing sliced sword sprite: " + SwordPath);

            var controllerObject = new GameObject("Sockets Sample Controller");
            var controller = controllerObject.AddComponent<SocketsExampleController>();
            controller.Profile = profile;
            controller.SocketItem = itemRoot.transform;
            controller.SocketName = "Weapon";
            controller.ClipIndex = 0;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        // Source pixels from cell pivot (+x right, +y up), authored per frame.
        static readonly Vector2[] IdlePositions =
        {
            new(-5f, -4f), new(-5f, -4f), new(-5f, -5f), new(-5f, -5f),
            new(-5f, -5f), new(-5f, -5f), new(-5f, -4f), new(-5f, -4f),
        };

        static readonly float[] IdleAngles =
        {
            -82f, -80f, -78f, -76f, -78f, -80f, -82f, -84f,
        };

        static readonly Vector2[] AttackPositions =
        {
            new(-9f, 1f), new(14f, -2f), new(12f, -4f), new(8f, -4f),
            new(7f, -5f), new(6f, -5f), new(6f, -5f), new(7f, -5f),
        };

        static readonly float[] AttackAngles =
        {
            -100f, -45f, -5f, 30f, 62f, 92f, 118f, 145f,
        };
    }
}
