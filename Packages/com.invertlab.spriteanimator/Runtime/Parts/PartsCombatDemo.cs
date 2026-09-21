using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Small playable integration example. Character and shots are DOTS sprites;
    /// a Unity 2D rigidbody demonstrates external ownership of a detached weapon.</summary>
    [AddComponentMenu("DOTS Sprite Animator/Parts Combat Demo")]
    public sealed class PartsCombatDemo : MonoBehaviour
    {
        [Tooltip("Editable Parts profile. Save changes in the animator, then restart Play to rebuild the character.")]
        public ScriptableSpriteSheetProfile Profile;
        [Tooltip("Used only by the advanced code-only fallback when no Profile is assigned.")]
        public Texture2D Atlas;
        public bool ShowControls = true;
        public bool AutoAim = true;
        public Camera ViewCamera;
        public Entity Root { get; private set; }
        public Entity Weapon { get; private set; }
        public bool Ready => _world != null && _world.IsCreated && Root != Entity.Null && _em.Exists(Root);
        public bool Paused { get; private set; }
        public bool WeaponDetached => _physical != null;
        public int Hits { get; private set; }
        public int ShotsFired { get; private set; }
        public int LiveShots => _shots.Count;
        public int WeaponStyle { get; private set; }
        public float2 AimTarget { get; private set; } = new float2(3f, 0.7f);
        public float2 Velocity { get; private set; }

        World _world;
        EntityManager _em;
        BlobAssetReference<SpritePartsSetBlob> _blob;
        readonly List<Entity> _owned = new List<Entity>();
        readonly List<Shot> _shots = new List<Shot>(64);
        readonly List<GameObject> _objects = new List<GameObject>();
        readonly float2[] _targets = { new float2(3f, 0.7f), new float2(1.8f, 2f), new float2(-3f, 1.4f) };
        Entity _whiteSheet, _weaponParent, _reticle;
        int _aimSlot = 2, _weaponSlot = 3;
        SpriteSheetProfile _profileData;
        Entity[] _targetEntities;
        Texture2D _white;
        Rigidbody2D _physical;
        PhysicsMaterial2D _bounce;
        float2 _move;
        readonly HashSet<KeyCode> _heldKeys = new HashSet<KeyCode>();
        float _recoil, _shotCooldown;
        bool _shoot, _moving;
        GUIStyle _title, _muted;
        Rect Panel => new Rect(20, 20, 300, Mathf.Min(480, Screen.height - 40));
        struct Shot { public Entity Entity; public float2 Position, Velocity; public float Life; }

        void Start()
        {
            if (!Ready) Initialize(World.DefaultGameObjectInjectionWorld, Atlas);
        }

        public void Initialize(World world, Texture2D atlas)
        {
            if (Ready) return;
            if (world == null || !world.IsCreated || (Profile == null && atlas == null))
            {
                Debug.LogError("[PartsCombatDemo] Assign a Parts profile (or the code-only atlas) and use a valid Entities world.", this);
                enabled = false;
                return;
            }
            _world = world;
            _em = world.EntityManager;
            Atlas = atlas;
            Paused = false;
            Hits = ShotsFired = WeaponStyle = 0;
            _move = Velocity = float2.zero;
            _heldKeys.Clear();
            _moving = _shoot = false;
            _recoil = _shotCooldown = 0f;
            SpriteBatchSpawner.LayoutXy = true;
            SpriteInstanceRenderSystem.Install(_em);
            if (Profile != null)
            {
                // Work on a copy: runtime canonicalization must not edit the source asset.
                _profileData = JsonUtility.FromJson<SpriteSheetProfile>(JsonUtility.ToJson(Profile.Data));
                string error = null;
                if (_profileData == null || _profileData.AnimKind != SpriteAnimKind.Parts ||
                    !SpritePartsClipConversion.TryBuildBlob(_profileData, Allocator.Persistent, out _blob, out error))
                {
                    Debug.LogError("[PartsCombatDemo] Invalid Parts profile: " + (error ?? "select a Parts profile"), this);
                    enabled = false;
                    return;
                }
            }
            else
            {
                _profileData = null;
                _blob = PartsCombatDemoRig.Build();
            }
            _aimSlot = SpritePartsPlayback.FindSlotIndexById(ref _blob.Value, "hand.r");
            _weaponSlot = SpritePartsPlayback.FindSlotIndexById(ref _blob.Value, "weapon");
            if (_aimSlot < 0 || _weaponSlot < 0 || _blob.Value.Slots[_weaponSlot].ParentSlotIndex < 0 ||
                SpritePartsPlayback.FindClipIndexByName(ref _blob.Value, "Idle") < 0 ||
                SpritePartsPlayback.FindClipIndexByName(ref _blob.Value, "Walk") < 0 ||
                SpritePartsPlayback.FindSkinIndex(ref _blob.Value, "blaster") < 0 ||
                SpritePartsPlayback.FindSkinIndex(ref _blob.Value, "rifle") < 0)
            {
                Debug.LogError("[PartsCombatDemo] Keep slot IDs hand.r and weapon (with a parent), clip names Idle/Walk, and skin IDs blaster/rifle for this gameplay controller.", this);
                _blob.Dispose();
                _blob = default;
                enabled = false;
                return;
            }
            SpawnCharacter();
            _white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "Combat demo shapes" };
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
            _whiteSheet = Sheet(_white, 1f, new float4(1f, 1f, 0f, 0f));
            BuildArena();
        }

        void SpawnCharacter()
        {
            var result = SpritePartsEntityFactory.Create(_em, _blob, new float3(-0.8f, -0.7f, 0f),
                clipIndex: SpritePartsPlayback.FindClipIndexByName(ref _blob.Value, "Idle"));
            Root = result.Root;
            Weapon = result.Parts[_weaponSlot];
            _weaponParent = _em.GetComponentData<Parent>(Weapon).Value;
            result.Parts.Dispose();
            _em.AddComponentObject(Root, new PartsCombatDemoLink { Demo = this });
            // Separate crop records preserve each part's aspect on the same atlas.
            var crops = PartsCombatDemoRig.Crops;
            for (int i = 0; i < (_profileData?.Sheets.Count ?? 4); i++)
            {
                Entity sheet;
                if (_profileData != null) sheet = ProfileSheet(_profileData.Sheets[i]);
                else
                {
                    float2 size = PartsCombatDemoRig.Sizes[i];
                    sheet = Sheet(Atlas, size.x / size.y, crops[i]);
                }
                _em.GetBuffer<SpritePartSheetEntry>(Root).Add(new SpritePartSheetEntry { Sheet = sheet, SheetTableIndex = i });
            }
            // The factory cannot bind textures until the sheet table exists.
            // Bind every default first; the weapon skin is only a partial patch.
            SpriteParts.ResetSkin(_em, Root);
            SpriteParts.ApplySkin(_em, Root, WeaponStyle == 0 ? "blaster" : "rifle");
            BindMuzzle();
            SpritePartsPoseWriter.Apply(_em, Root);
        }

        Entity ProfileSheet(SpriteSheetDef definition)
        {
            if (definition?.Texture == null) return Entity.Null;
            Entity sheet = _em.CreateEntity();
            _owned.Add(sheet);
            int columns = Mathf.Max(1, definition.Columns), rows = Mathf.Max(1, definition.Rows);
            bool cropped = definition.CellLayoutMode == SpriteSheetCellLayoutMode.Cropped && SpriteSheetProfile.HasCroppedCellData(definition);
            _em.AddComponentData(sheet, new SpriteSheetDefinition
            {
                Cols = columns, Rows = rows,
                CellAspect = SpriteSheetProfile.GetCellAspect(definition.Texture, columns, rows),
                UseCellCrops = cropped ? (byte)1 : (byte)0,
            });
            _em.AddComponentObject(sheet, new SpriteSheetAsset { Texture = definition.Texture });
            if (cropped)
            {
                var buffer = _em.AddBuffer<SpriteAnimCellCrop>(sheet);
                foreach (var crop in SpriteSheetProfile.BuildCellCropSTArray(definition))
                    buffer.Add(new SpriteAnimCellCrop { Value = new float4(crop.x, crop.y, crop.z, crop.w) });
            }
            return sheet;
        }

        Entity Sheet(Texture2D texture, float aspect, float4 crop)
        {
            Entity sheet = _em.CreateEntity();
            _em.AddComponentData(sheet, new SpriteSheetDefinition { Cols = 1, Rows = 1, CellAspect = aspect, UseCellCrops = 1 });
            _em.AddBuffer<SpriteAnimCellCrop>(sheet).Add(new SpriteAnimCellCrop { Value = crop });
            _em.AddComponentObject(sheet, new SpriteSheetAsset { Texture = texture });
            _owned.Add(sheet);
            return sheet;
        }

        Entity Shape(float2 position, float2 size, Color color, float z = 1f)
        {
            Entity entity = _em.CreateEntity();
            var lt = LocalTransform.FromPosition(new float3(position, z));
            _em.AddComponentData(entity, lt);
            _em.AddComponentData(entity, new LocalToWorld { Value = lt.ToMatrix() });
            _em.AddComponentData(entity, new SpriteAnimFrame { Scale = size });
            _em.AddComponentData(entity, new SpriteSheetBinding { Sheet = _whiteSheet });
            _em.AddComponentData(entity, new SpriteTint { Value = new float4(color.r, color.g, color.b, color.a) });
            _em.AddComponentData(entity, new SpriteFlip { Pivot = new float2(0.5f) });
            _em.AddComponentData(entity, new SpriteAnimEnabled());
            return entity;
        }

        void BuildArena()
        {
            Color grid = new Color(0.10f, 0.16f, 0.20f);
            for (int x = -6; x <= 6; x++) _owned.Add(Shape(new float2(x, 0), new float2(0.012f, 7f), grid, 4f));
            for (int y = -3; y <= 3; y++) _owned.Add(Shape(new float2(0, y), new float2(14f, 0.012f), grid, 4f));
            _owned.Add(Shape(new float2(0, -2.1f), new float2(14f, 0.08f), new Color(0.18f, 0.55f, 0.59f), 2f));
            _targetEntities = new Entity[_targets.Length];
            for (int i = 0; i < _targets.Length; i++)
            {
                _targetEntities[i] = Shape(_targets[i], new float2(0.48f), new Color(1f, 0.45f, 0.32f));
                _owned.Add(_targetEntities[i]);
                _owned.Add(Shape(_targets[i], new float2(0.16f), new Color(0.15f, 0.22f, 0.26f), 0.9f));
            }
            _reticle = Shape(AimTarget, new float2(0.07f), Color.white, -0.1f);
            _owned.Add(_reticle);
            var floor = new GameObject("Weapon physics floor", typeof(BoxCollider2D));
            floor.transform.position = new Vector3(0, -2.25f, 0);
            floor.GetComponent<BoxCollider2D>().size = new Vector2(14, 0.25f);
            _objects.Add(floor);
            _bounce = new PhysicsMaterial2D("Demo weapon bounce") { bounciness = 0.45f, friction = 0.5f };
        }

        public void SetMove(float2 input) => _move = math.normalizesafe(input) * math.min(1f, math.length(input));
        public void AimAt(float2 target) { AutoAim = false; AimTarget = target; }
        public void Fire() { if (!Paused && !WeaponDetached) _shoot = true; }

        public void SetPaused(bool paused)
        {
            if (!Ready) return;
            Paused = paused;
            if (paused) Velocity = float2.zero;
            if (paused) SpriteParts.Pause(_em, Root); else SpriteParts.Resume(_em, Root);
            if (_physical != null) _physical.simulated = !paused;
        }

        public void SwapWeapon()
        {
            if (!Ready || WeaponDetached) return;
            WeaponStyle = 1 - WeaponStyle;
            SpriteParts.ApplySkin(_em, Root, WeaponStyle == 0 ? "blaster" : "rifle");
            BindMuzzle();
        }

        void BindMuzzle()
        {
            var art = _em.GetComponentData<SpritePartAppearanceState>(Weapon);
            SpriteParts.BindSocket(_em, Root, "muzzle", "weapon",
                art.LogicalWorldSize * (new float2(0.94f, 0.55f) - art.Pivot));
        }

        public void TogglePhysics()
        {
            if (!Ready || Paused) return;
            if (_physical != null)
            {
                _physical.simulated = false;
                Destroy(_physical.gameObject);
                _physical = null;
                _em.AddComponentData(Weapon, new Parent { Value = _weaponParent });
                _em.RemoveComponent<SpritePartPhysicsOwned>(Weapon);
                SpritePartsPoseWriter.Apply(_em, Root);
                return;
            }
            float4x4 matrix = _em.GetComponentData<LocalToWorld>(Weapon).Value;
            float rotation = math.degrees(math.atan2(matrix.c0.y, matrix.c0.x));
            var proxy = new GameObject("Weapon physics owner", typeof(Rigidbody2D), typeof(BoxCollider2D));
            _objects.Add(proxy);
            _physical = proxy.GetComponent<Rigidbody2D>();
            _physical.position = matrix.c3.xy;
            _physical.rotation = rotation;
            _physical.linearVelocity = new Vector2(2.2f, 3.5f);
            _physical.angularVelocity = 180f;
            _physical.simulated = true;
            float2 scale = new float2(math.length(matrix.c0.xyz), math.length(matrix.c1.xyz));
            if (math.determinant(new float3x3(matrix.c0.xyz, matrix.c1.xyz, matrix.c2.xyz)) < 0f) scale.y = -scale.y;
            var collider = proxy.GetComponent<BoxCollider2D>();
            var art = _em.GetComponentData<SpritePartAppearanceState>(Weapon);
            float2 size = art.LogicalWorldSize;
            collider.size = size * math.abs(scale);
            collider.offset = (new float2(0.5f) - art.Pivot) * size * scale;
            collider.sharedMaterial = _bounce;
            _em.AddComponentData(Weapon, new SpritePartPhysicsOwned());
            _em.RemoveComponent<Parent>(Weapon);
            _em.SetComponentData(Weapon, new PostTransformMatrix { Value = float4x4.Scale(new float3(scale, 1f)) });
            CopyPhysicsPose();
        }

        void CopyPhysicsPose()
        {
            if (_physical == null) return;
            _em.SetComponentData(Weapon, LocalTransform.FromPositionRotationScale(
                new float3((float2)_physical.position, 0f), quaternion.RotateZ(math.radians(_physical.rotation)), 1f));
        }

        internal void BeforePose(float dt)
        {
            if (!Ready || Paused) return;
            dt = math.clamp(dt, 0f, 0.1f);
            if (dt <= 1e-6f) return;
            bool left = Held(KeyCode.A, KeyCode.LeftArrow), right = Held(KeyCode.D, KeyCode.RightArrow);
            bool up = Held(KeyCode.W, KeyCode.UpArrow), down = Held(KeyCode.S, KeyCode.DownArrow);
            float2 keyboard = new float2((right ? 1 : 0) - (left ? 1 : 0), (up ? 1 : 0) - (down ? 1 : 0));
            float2 move = math.clamp(_move + keyboard, -1f, 1f);
            if (math.lengthsq(move) > 1f) move = math.normalize(move);
            var root = _em.GetComponentData<LocalTransform>(Root);
            float2 previousPosition = root.Position.xy;
            root.Position.xy = math.clamp(previousPosition + move * (2.2f * dt), new float2(-4f, -1.15f), new float2(4f, 2.2f));
            _em.SetComponentData(Root, root);
            // Measure resolved movement, including arena limits. Held input against
            // a wall is idle; sideways/diagonal movement still plays Walk.
            Velocity = (root.Position.xy - previousPosition) / dt;
            float threshold = _moving ? 0.02f : 0.04f;
            bool moving = math.lengthsq(Velocity) > threshold * threshold;
            if (moving != _moving)
            {
                SpriteParts.Play(_em, Root, moving ? "Walk" : "Idle", crossfadeSeconds: 0.18f);
                _moving = moving;
            }
            if (AutoAim) AimTarget = _targets[(Hits / 4) % _targets.Length];
            var reticle = _em.GetComponentData<LocalTransform>(_reticle);
            reticle.Position.xy = AimTarget;
            _em.SetComponentData(_reticle, reticle);
            // Aim the existing hand; do not mirror the whole rig across the body.
            SpriteParts.SetOverride(_em, Root, new SpritePartsPoseOverride
            {
                Id = 20001, SlotIndex = _aimSlot, Mode = (byte)SpritePartsPoseMode.LookAt,
                Channels = (byte)SpritePartsPoseChannel.Rotation, Space = (byte)SpritePartsPoseSpace.World,
                Target = AimTarget, Weight = 1f,
            });
            _recoil = math.max(0f, _recoil - dt * 5f);
            SpriteParts.SetOverride(_em, Root, SpritePartsMotion.Recoil(_weaponSlot, new float2(-0.16f, 0f), _recoil));
            _shotCooldown = math.max(0f, _shotCooldown - dt);
            CopyPhysicsPose();
        }

        internal void AfterPose(float dt)
        {
            if (!Ready || Paused) return;
            dt = math.clamp(dt, 0f, 0.1f);
            if (_shoot && _shotCooldown <= 0f && !WeaponDetached &&
                SpriteParts.TryGetSocketWorld(_em, Root, "muzzle", out var muzzle, out var degrees))
            {
                float angle = math.radians(degrees);
                Entity shot = Shape(muzzle, new float2(0.22f, 0.07f), new Color(1f, 0.88f, 0.53f), -0.05f);
                _em.SetComponentData(shot, LocalTransform.FromPositionRotationScale(new float3(muzzle, -0.05f), quaternion.RotateZ(angle), 1f));
                _shots.Add(new Shot { Entity = shot, Position = muzzle, Velocity = new float2(math.cos(angle), math.sin(angle)) * 9f, Life = 2f });
                ShotsFired++;
                _recoil = 1f;
                _shotCooldown = WeaponStyle == 0 ? 0.2f : 0.1f;
            }
            _shoot = false;
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                Shot shot = _shots[i];
                float2 next = shot.Position + shot.Velocity * dt;
                shot.Life -= dt;
                for (int target = 0; target < _targets.Length; target++)
                {
                    float2 segment = next - shot.Position;
                    float t = math.saturate(math.dot(_targets[target] - shot.Position, segment) / math.max(1e-8f, math.lengthsq(segment)));
                    if (math.distance(shot.Position + segment * t, _targets[target]) < 0.33f)
                    { Hits++; shot.Life = 0f; break; }
                }
                if (shot.Life <= 0f)
                {
                    if (_em.Exists(shot.Entity)) _em.DestroyEntity(shot.Entity);
                    _shots.RemoveAt(i);
                    continue;
                }
                shot.Position = next;
                var transform = _em.GetComponentData<LocalTransform>(shot.Entity);
                transform.Position.xy = next;
                _em.SetComponentData(shot.Entity, transform);
                _shots[i] = shot;
            }
        }

        bool Held(KeyCode first, KeyCode second) => _heldKeys.Contains(first) || _heldKeys.Contains(second);

        internal bool HandleKey(KeyCode key, bool pressed)
        {
            switch (key)
            {
                case KeyCode.A: case KeyCode.LeftArrow:
                case KeyCode.D: case KeyCode.RightArrow:
                case KeyCode.W: case KeyCode.UpArrow:
                case KeyCode.S: case KeyCode.DownArrow:
                    if (pressed) _heldKeys.Add(key); else _heldKeys.Remove(key);
                    return true;
                case KeyCode.Space:
                    if (pressed) Fire();
                    return true;
            }
            return false;
        }

        void OnGUI()
        {
            if (!Ready) return;
            Event evt = Event.current;
            if ((evt.type == EventType.KeyDown || evt.type == EventType.KeyUp) &&
                HandleKey(evt.keyCode, evt.type == EventType.KeyDown)) evt.Use();
            if (!ShowControls) return;
            if (!Panel.Contains(evt.mousePosition))
            {
                if (!AutoAim && ViewCamera != null)
                {
                    Vector3 p = ViewCamera.ScreenToWorldPoint(new Vector3(evt.mousePosition.x, Screen.height - evt.mousePosition.y, -ViewCamera.transform.position.z));
                    AimTarget = new float2(p.x, p.y);
                }
                if (evt.type == EventType.MouseDown && evt.button == 0) { Fire(); evt.Use(); }
            }
            _title ??= new GUIStyle(GUI.skin.label) { fontSize = 23, fontStyle = FontStyle.Bold };
            _muted ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 13 };
            GUILayout.BeginArea(Panel, GUI.skin.box);
            GUILayout.Space(10);
            GUILayout.Label("PARTS / PLAYGROUND", _title);
            GUILayout.Label("Walk. Aim. Fire. Let go.", _muted);
            GUILayout.Space(14);
            GUILayout.Label($"{Hits:00} HITS   /   {ShotsFired:00} SHOTS");
            GUILayout.Label(WeaponStyle == 0 ? "Equipped: Coral blaster" : "Equipped: Ion rifle");
            GUILayout.Space(10);
            GUILayout.Label("WASD / arrows to move · click / space to fire", _muted);
            AutoAim = GUILayout.Toggle(AutoAim, "Aim at practice targets");
            if (GUILayout.RepeatButton("FIRE", GUILayout.Height(38))) Fire();
            GUI.enabled = !WeaponDetached;
            if (GUILayout.Button("Swap weapon", GUILayout.Height(30))) SwapWeapon();
            GUI.enabled = !Paused;
            if (GUILayout.Button(WeaponDetached ? "Return weapon to rig" : "Drop weapon → physics", GUILayout.Height(30))) TogglePhysics();
            GUI.enabled = true;
            if (GUILayout.Button(Paused ? "Resume" : "Pause", GUILayout.Height(30))) SetPaused(!Paused);
            GUILayout.Space(8);
            GUILayout.Label(WeaponDetached ? "Physics owns the weapon. Animation keeps the body moving." : "Body clip + independent aim + additive recoil.", _muted);
            GUILayout.EndArea();
        }

        void OnApplicationFocus(bool focus) { if (!focus) _heldKeys.Clear(); }
        void OnDisable() => _heldKeys.Clear();
        void OnDestroy() => Shutdown();
        public void Shutdown()
        {
            if (_world != null && _world.IsCreated)
            {
                if (Root != Entity.Null && _em.Exists(Root)) _em.DestroyEntity(Root);
                foreach (var shot in _shots) if (_em.Exists(shot.Entity)) _em.DestroyEntity(shot.Entity);
                foreach (var entity in _owned) if (_em.Exists(entity)) _em.DestroyEntity(entity);
            }
            Root = Weapon = Entity.Null;
            _shots.Clear(); _owned.Clear();
            foreach (var obj in _objects) if (obj != null) Destroy(obj);
            _objects.Clear(); _physical = null;
            if (_blob.IsCreated) { _blob.Dispose(); _blob = default; }
            if (_white != null) Destroy(_white);
            if (_bounce != null) Destroy(_bounce);
            _white = null; _bounce = null;
        }
    }

    public sealed class PartsCombatDemoLink : IComponentData { public PartsCombatDemo Demo; }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(SpritePartsPlayerSystem))]
    public partial class PartsCombatDemoInputSystem : SystemBase
    {
        EntityQuery _demos;
        protected override void OnCreate()
        {
            _demos = GetEntityQuery(typeof(PartsCombatDemoLink));
            RequireForUpdate(_demos);
        }
        protected override void OnUpdate()
        {
            using var roots = _demos.ToEntityArray(Allocator.Temp);
            foreach (Entity root in roots)
            {
                var demo = EntityManager.GetComponentObject<PartsCombatDemoLink>(root).Demo;
                if (demo != null && demo.isActiveAndEnabled) demo.BeforePose(SystemAPI.Time.DeltaTime);
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpritePartsPlayerSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial class PartsCombatDemoFireSystem : SystemBase
    {
        EntityQuery _demos;
        protected override void OnCreate()
        {
            _demos = GetEntityQuery(typeof(PartsCombatDemoLink));
            RequireForUpdate(_demos);
        }
        protected override void OnUpdate()
        {
            using var roots = _demos.ToEntityArray(Allocator.Temp);
            foreach (Entity root in roots)
            {
                var demo = EntityManager.GetComponentObject<PartsCombatDemoLink>(root).Demo;
                if (demo != null && demo.isActiveAndEnabled) demo.AfterPose(SystemAPI.Time.DeltaTime);
            }
        }
    }
}
