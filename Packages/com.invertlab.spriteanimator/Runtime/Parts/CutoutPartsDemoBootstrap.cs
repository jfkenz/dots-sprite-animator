using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// One-click Cutout Parts demo: generated art, Walk clip, sword/spear skins,
    /// pointer/touch + OnGUI. Pure DOTS entities via SpritePartsEntityFactory.
    /// </summary>
    [AddComponentMenu("DOTS Sprite Animator/Cutout Parts Demo")]
    public sealed class CutoutPartsDemoBootstrap : MonoBehaviour
    {
        World _world;
        EntityManager _em;
        Entity _root;
        BlobAssetReference<SpritePartsSetBlob> _blob;
        NativeArray<Entity> _parts;
        readonly List<Texture2D> _textures = new();
        readonly List<Entity> _sheetEntities = new();
        bool _paused;
        int _weaponMode; // 0 sword, 1 spear
        bool _flipX;

        void Start()
        {
            _world = World.DefaultGameObjectInjectionWorld;
            if (_world == null || !_world.IsCreated)
            {
                Debug.LogError("[CutoutPartsDemo] Default Entities world missing.");
                enabled = false;
                return;
            }

            EnsureCamera();
            SpriteBatchSpawner.LayoutXy = true;
            _em = _world.EntityManager;
            _blob = BuildDemoBlob();
            var created = SpritePartsEntityFactory.Create(_em, _blob, new float3(0f, 0f, 0f),
                characterOrder: 1, flipX: false, playing: true, clipIndex: 0);
            _root = created.Root;
            _parts = created.Parts;
            RegisterDemoSheets();
            SpriteParts.Play(_em, _root, "Walk");
            SpriteParts.ApplySkin(_em, _root, "weapon.sword");
        }

        void Update()
        {
            if (_root == Entity.Null || !_em.Exists(_root))
                return;

            // Pointer / touch first — no Input System package required.
            if (Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began))
            {
                var pos = Input.touchCount > 0
                    ? (Vector2)Input.GetTouch(0).position
                    : (Vector2)Input.mousePosition;
                // Left half: swap weapon. Right half: flip facing.
                if (pos.x < Screen.width * 0.5f)
                    ToggleWeapon();
                else
                    ToggleFacing();
            }
        }

        void OnGUI()
        {
            if (_root == Entity.Null || !_em.Exists(_root))
                return;
            GUILayout.BeginArea(new Rect(16, 16, 360, 260), GUI.skin.box);
            GUILayout.Label("Cutout Parts Demo (pure DOTS)");
            GUILayout.Label("Walk playing. Tap left: swap weapon. Tap right: flip.");
            GUILayout.Label($"Weapon: {(_weaponMode == 0 ? "sword.iron" : "spear.oak")}");
            if (GUILayout.Button(_paused ? "Resume Walk" : "Pause Walk"))
            {
                _paused = !_paused;
                if (_paused) SpriteParts.Pause(_em, _root);
                else SpriteParts.Resume(_em, _root);
            }
            if (GUILayout.Button("Swap Weapon (sword / spear)"))
                ToggleWeapon();
            if (GUILayout.Button("Flip Facing"))
                ToggleFacing();
            if (GUILayout.Button("Reset Skin (defaults)"))
            {
                SpriteParts.ResetSkin(_em, _root);
                _weaponMode = 0;
            }
            GUILayout.EndArea();
        }

        void ToggleWeapon()
        {
            _weaponMode = _weaponMode == 0 ? 1 : 0;
            string skin = _weaponMode == 0 ? "weapon.sword" : "weapon.spear";
            SpriteParts.ApplySkin(_em, _root, skin);
        }

        void ToggleFacing()
        {
            _flipX = !_flipX;
            SpriteParts.SetFacing(_em, _root, _flipX);
        }

        void RegisterDemoSheets()
        {
            // Create world-local sheet entities matching appearance SheetTableIndex 0/1.
            var swordSheet = CreateSheetEntity(_textures[0], 1, 1);
            var spearSheet = CreateSheetEntity(_textures[1], 1, 1);
            var buf = _em.GetBuffer<SpritePartSheetEntry>(_root);
            buf.Clear();
            buf.Add(new SpritePartSheetEntry { Sheet = swordSheet, SheetTableIndex = 0 });
            buf.Add(new SpritePartSheetEntry { Sheet = spearSheet, SheetTableIndex = 1 });
            // Re-apply current skin so bindings pick up sheet entities.
            SpriteParts.ApplySkin(_em, _root, _weaponMode == 0 ? "weapon.sword" : "weapon.spear");
        }

        Entity CreateSheetEntity(Texture2D tex, int cols, int rows)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new SpriteSheetDefinition
            {
                Cols = cols,
                Rows = rows,
                CellAspect = 1f,
                UseCellCrops = 0,
            });
            _em.AddComponentObject(e, new SpriteSheetAsset { Texture = tex });
            return e;
        }

        BlobAssetReference<SpritePartsSetBlob> BuildDemoBlob()
        {
            // Generated art: 32px/32PPU sword, 96px/48PPU spear.
            var swordTex = MakeSolidTexture(32, 32, new Color(0.75f, 0.78f, 0.85f), "sword");
            var spearTex = MakeSolidTexture(96, 96, new Color(0.55f, 0.4f, 0.25f), "spear");
            _textures.Add(swordTex);
            _textures.Add(spearTex);

            float2 swordSize = new float2(32f / 32f, 32f / 32f);
            float2 spearSize = new float2(96f / 48f, 96f / 48f);
            float2 swordPivot = new float2(0.2f, 0.1f);
            float2 spearPivot = new float2(0.15f, 0.05f);
            SpritePartsGeometry.TryResolveExplicit(swordSize, swordPivot, 0, 0, out var swordGeo, out _);
            SpritePartsGeometry.TryResolveExplicit(spearSize, spearPivot, 1, 0, out var spearGeo, out _);
            SpritePartsGeometry.TryResolveExplicit(new float2(1f, 1.2f), new float2(0.5f, 0.4f), 0, 0, out var bodyGeo, out _);
            SpritePartsGeometry.TryResolveExplicit(new float2(0.45f, 0.45f), new float2(0.5f, 0.5f), 0, 0, out var handGeo, out _);

            var appearances = new[]
            {
                App("body.default", bodyGeo),
                App("hand.default", handGeo),
                App("sword.iron", swordGeo),
                App("spear.oak", spearGeo),
            };
            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Body", SlotId = "body",
                    RestPosition = float2.zero, RestScale = new float2(1f, 1f),
                    DefaultAppearanceId = "body.default", DrawRank = 0,
                },
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Hand L", SlotId = "hand.l", ParentSlotId = "body",
                    RestPosition = new float2(-0.35f, 0.1f), RestScale = new float2(1f, 1f),
                    DefaultAppearanceId = "hand.default", DrawRank = 1,
                },
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Hand R", SlotId = "hand.r", ParentSlotId = "body",
                    RestPosition = new float2(0.35f, 0.1f), RestScale = new float2(1f, 1f),
                    DefaultAppearanceId = "hand.default", DrawRank = 2,
                },
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Weapon", SlotId = "weapon", ParentSlotId = "hand.r",
                    RestPosition = new float2(0.25f, 0f), RestScale = new float2(1f, 1f),
                    DefaultAppearanceId = "sword.iron", DrawRank = 3,
                },
            };
            var tracks = new[]
            {
                new SpritePartsSetBuilder.TrackInput
                {
                    SlotId = "body",
                    Keys = new[]
                    {
                        Key(0f, float2.zero, 0f),
                        Key(0.3f, new float2(0f, 0.05f), 0f),
                        Key(0.6f, float2.zero, 0f),
                    },
                },
                new SpritePartsSetBuilder.TrackInput
                {
                    SlotId = "hand.r",
                    Keys = new[]
                    {
                        Key(0f, new float2(0.35f, 0.1f), 0f),
                        Key(0.3f, new float2(0.4f, 0.15f), 20f),
                        Key(0.6f, new float2(0.35f, 0.1f), 0f),
                    },
                },
            };
            var clips = new[]
            {
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "Walk", ClipId = "walk", Duration = 0.6f, SpeedMultiplier = 1f,
                    WrapMode = (byte)SpritePartsWrap.Loop, Tracks = tracks,
                },
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "Idle", ClipId = "idle", Duration = 1f, SpeedMultiplier = 1f,
                    WrapMode = (byte)SpritePartsWrap.Loop,
                    Tracks = System.Array.Empty<SpritePartsSetBuilder.TrackInput>(),
                },
            };
            var skins = new[]
            {
                new SpritePartsSetBuilder.SkinInput
                {
                    SkinId = "weapon.sword",
                    Bindings = new[]
                    {
                        new SpritePartsSetBuilder.SkinBindingInput
                            { SlotId = "weapon", AppearanceId = "sword.iron" },
                    },
                },
                new SpritePartsSetBuilder.SkinInput
                {
                    SkinId = "weapon.spear",
                    Bindings = new[]
                    {
                        new SpritePartsSetBuilder.SkinBindingInput
                            { SlotId = "weapon", AppearanceId = "spear.oak" },
                    },
                },
            };
            return SpritePartsSetBuilder.Build(Allocator.Persistent, slots, appearances, clips, skins);
        }

        static SpritePartsSetBuilder.AppearanceInput App(string id, SpritePartsGeometry.Resolved geo)
            => new SpritePartsSetBuilder.AppearanceInput
            {
                AppearanceId = id,
                SheetTableIndex = geo.SheetIndex,
                CellIndex = geo.CellIndex,
                LogicalWorldSize = geo.LogicalWorldSize,
                Pivot = geo.Pivot,
                FrameOffset = geo.FrameOffset,
                FrameScale = geo.FrameScale,
            };

        static SpritePartsSetBuilder.KeyInput Key(float t, float2 pos, float rot)
            => new SpritePartsSetBuilder.KeyInput
            {
                Time = t, Position = pos, Rotation = rot,
                Scale = new float2(1f, 1f), EaseMode = (byte)SpriteEaseMode.Linear,
            };

        static Texture2D MakeSolidTexture(int w, int h, Color color, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[w * h];
            var c = (Color32)color;
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = c;
            // Grip mark (darker corner) so pivot offset is visible.
            for (int y = 0; y < Mathf.Max(2, h / 8); y++)
                for (int x = 0; x < Mathf.Max(2, w / 8); x++)
                    pixels[y * w + x] = new Color32(40, 40, 40, 255);
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        static void EnsureCamera()
        {
            if (Camera.main != null) return;
            var camGo = new GameObject("Main Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 3f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.05f, 0.08f);
        }

        void OnDestroy()
        {
            if (_world != null && _world.IsCreated && _root != Entity.Null && _em.Exists(_root))
                _em.DestroyEntity(_root);
            if (_parts.IsCreated)
                _parts.Dispose();
            if (_blob.IsCreated)
                _blob.Dispose();
            for (int i = 0; i < _textures.Count; i++)
            {
                if (_textures[i] != null)
                    Destroy(_textures[i]);
            }
            _textures.Clear();
        }
    }
}

