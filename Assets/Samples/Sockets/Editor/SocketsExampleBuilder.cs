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
        const string SwordMaterialPath = Root + "/SocketsSword.mat";
        const string ScenePath = Root + "/SocketsExample.unity";
        const string SubScenePath = Root + "/SocketsExample_SubScene.unity";
        const string CharacterPath = "Assets/Samples/Showcase/Sword Character Prototype_All Frames.png";

        [MenuItem("Tools/DOTS Sprite Animator/Build Sockets Sample")]
        public static void Build()
        {
            var character = AssetDatabase.LoadAssetAtPath<Texture2D>(CharacterPath);
            if (character == null)
                throw new System.InvalidOperationException("Missing Showcase character texture: " + CharacterPath);

            var profile = BuildProfile(character);
            var swordMaterial = BuildSwordMaterial();
            BuildSubScene(profile, swordMaterial);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                SubScenePath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            BuildMainScene(profile);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
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

        static Material BuildSwordMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SwordMaterialPath);
            var shader = Shader.Find("DOTS Sprite Animator/Sprite Unlit 2D");
            if (shader == null)
                throw new System.InvalidOperationException("Missing DOTS Sprite Animator sprite shader.");

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, SwordMaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetColor("_Color", new Color(0.72f, 0.86f, 1f, 1f));
            EditorUtility.SetDirty(material);
            return material;
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

        static void BuildSubScene(ScriptableSpriteSheetProfile profile, Material swordMaterial)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Socket Character (CPU Playback)";
            Object.DestroyImmediate(quad.GetComponent<Collider>());

            var set = quad.AddComponent<SpriteAnimSetAuthoring>();
            set.Profile = profile;
            set.InitialClipIndex = 0;
            set.ShowSpriteInScene = true;
            set.ApplyFromProfile();

            var player = quad.AddComponent<SpriteAnimPlayerAuthoring>();
            player.ClipIndex = 0;
            player.Speed = 1f;
            player.Playing = true;
            player.PlayOnEnable = true;

            var sheet = profile.Data.SheetForClip(profile.Data.Clips[0]);
            float worldSize = SpriteSheetProfile.GetWorldHeight(sheet);
            quad.transform.localScale = new Vector3(worldSize, worldSize, worldSize);

            var itemRoot = new GameObject("Weapon Socket Item");
            itemRoot.transform.SetParent(quad.transform, false);
            itemRoot.transform.localPosition = new Vector3(0f, 0f, -0.1f);
            var attachment = itemRoot.AddComponent<SpriteSocketAttachmentAuthoring>();
            attachment.Player = set;
            attachment.SocketName = "Weapon";

            var swordVisual = GameObject.CreatePrimitive(PrimitiveType.Quad);
            swordVisual.name = "Sword Visual";
            Object.DestroyImmediate(swordVisual.GetComponent<Collider>());
            swordVisual.transform.SetParent(itemRoot.transform, false);
            swordVisual.transform.localPosition = new Vector3(0f, 0.32f, 0f);
            swordVisual.transform.localScale = new Vector3(0.08f, 0.72f, 1f);
            swordVisual.GetComponent<MeshRenderer>().sharedMaterial = swordMaterial;

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

            var controllerObject = new GameObject("Sockets Sample Controller");
            var controller = controllerObject.AddComponent<SocketsExampleController>();
            controller.Profile = profile;
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
