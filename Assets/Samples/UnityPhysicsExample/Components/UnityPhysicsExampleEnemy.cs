using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Unity Physics example enemy. Health bar follows SpriteSockets.
    /// Hurtbox is a runtime DOTS PhysicsCollider (open-scene GOs are not
    /// SubScene-baked, so the SpriteColliderAuthoring baker never runs here).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteAnimPlayerAuthoring))]
    [RequireComponent(typeof(SpriteAnimSetAuthoring))]
    public sealed class UnityPhysicsExampleEnemy : MonoBehaviour
    {
        [Min(1)] public int MaxHealth = 5;
        public float HealthBarWorldOffsetY = 1.15f;

        [Tooltip("Socket child name to follow (e.g. HealthBarSocket under SpriteSockets). Empty = try defaults, then offset.")]
        public string HealthBarSocketName = "HealthBarSocket";

        [Tooltip("Optional stable socket ID. Used when Name is empty / not found.")]
        public string HealthBarSocketId = string.Empty;

        [Min(0)] public int DeathClipIndex = 4;
        [Min(0)] public int IdleClipIndex = 0;
        [Min(0.05f)] public float RespawnDelay = 0.5f;
        [Min(0)] public int HurtClipIndex = 2;

        public int Health { get; private set; }
        public int LastHitAttackId { get; private set; } = -1;
        public Entity BakedEntity { get; private set; }

        SpriteAnimPlayerAuthoring _player;
        SpriteAnimSetAuthoring _set;
        bool _dying;
        float _respawnAt;
        Vector3 _spawnPosition;
        bool _ownedEntity;
        Transform _healthBarSocketChild;
        Vector3 _healthBarWorld;
        bool _healthBarResolved;

        static readonly string[] DefaultSocketNames =
        {
            "HealthBarSocket", "Health", "HealthBar", "UI",
        };

        void Awake()
        {
            _player = GetComponent<SpriteAnimPlayerAuthoring>();
            _set = GetComponent<SpriteAnimSetAuthoring>();
            _spawnPosition = transform.position;
            Health = MaxHealth;
            EnsureSocketsEnabled();
            _healthBarSocketChild = FindHealthBarSocketChild();
        }

        void OnEnable()
        {
            EnsureSocketsEnabled();
            _healthBarSocketChild = FindHealthBarSocketChild();
        }

        void OnDestroy()
        {
            DestroyOwnedEntity();
        }

        void EnsureSocketsEnabled()
        {
            if (_set == null)
                return;
            _set.BakeUnitySockets = true;
            if (Application.isPlaying)
                _set.SyncUnitySockets();
        }

        void Update()
        {
            EnsurePhysicsHurtbox();
            if (_dying && Time.time >= _respawnAt)
                Respawn();
        }

        void LateUpdate()
        {
            SyncHurtboxTransform();
            if (_dying)
                return;
            _healthBarWorld = ResolveHealthBarWorldPosition();
            _healthBarResolved = true;
        }

        /// <summary>
        /// Ensure a PhysicsWorld body exists for OverlapAabb hits.
        /// Prefers a baked SpriteHurtbox entity when present; otherwise creates
        /// a runtime kinematic body (needed because this sample lives outside
        /// the SubScene, so authoring bakers do not run).
        /// </summary>
        public bool EnsurePhysicsHurtbox()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            var em = world.EntityManager;

            if (BakedEntity == Entity.Null || !em.Exists(BakedEntity))
            {
                BakedEntity = Entity.Null;
                _ownedEntity = false;
                TryRegisterBakedHurtbox(em);
            }

            if (BakedEntity == Entity.Null)
                CreateRuntimeHurtbox(em);

            if (BakedEntity == Entity.Null || !em.Exists(BakedEntity))
                return false;

            if (!em.HasComponent<PhysicsCollider>(BakedEntity))
                return ApplyBodyCollider(em, BakedEntity);

            // already has collider — treat as attached for UI / AnyColliderAttached
            return true;
        }

        void TryRegisterBakedHurtbox(EntityManager em)
        {
            var query = em.CreateEntityQuery(typeof(SpriteHurtbox), typeof(LocalToWorld));
            var entities = query.ToEntityArray(Allocator.Temp);
            float best = float.MaxValue;
            Entity found = Entity.Null;
            for (int i = 0; i < entities.Length; i++)
            {
                var ltw = em.GetComponentData<LocalToWorld>(entities[i]);
                float d = math.distancesq(ltw.Position, (float3)transform.position);
                if (d < best)
                {
                    best = d;
                    found = entities[i];
                }
            }
            entities.Dispose();
            // only claim a nearby baked body (avoid stealing another enemy's)
            if (found != Entity.Null && best < 0.25f)
            {
                BakedEntity = found;
                _ownedEntity = false;
            }
        }

        void CreateRuntimeHurtbox(EntityManager em)
        {
            var entity = em.CreateEntity();
            em.AddComponentData(entity, LocalTransform.FromPositionRotationScale(
                transform.position, quaternion.identity, 1f));
            em.AddComponentData(entity, new LocalToWorld
            {
                Value = float4x4.TRS(transform.position, quaternion.identity, new float3(1f)),
            });
            em.AddComponentData(entity, new SpriteHurtbox());
            em.AddSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });
            BakedEntity = entity;
            _ownedEntity = true;
            ApplyBodyCollider(em, entity);
        }

        /// <summary>
        /// Convert the profile character body box into a Unity Physics collider
        /// on the hurtbox entity.
        /// </summary>
        public bool TryAttachUnityPhysicsCollider()
        {
            return EnsurePhysicsHurtbox() && ApplyBodyCollider(
                World.DefaultGameObjectInjectionWorld.EntityManager, BakedEntity);
        }

        bool ApplyBodyCollider(EntityManager em, Entity entity)
        {
            if (entity == Entity.Null || !em.Exists(entity))
                return false;

            var set = _set != null ? _set : GetComponent<SpriteAnimSetAuthoring>();
            var data = set != null && set.Profile != null ? set.Profile.Data : null;
            if (data == null)
                return false;

            var bodies = new System.Collections.Generic.List<FrameBoxDef>();
            SpriteUnityPhysicsShape.CollectCharacterBodyBoxes(data, bodies);
            var pivot = data.Pivot;
            float sizeUnits = set != null ? set.SizeUnits : 1f;
            var blob = SpriteUnityPhysicsShape.CreateBodyCollider(
                bodies, pivot, sizeUnits, false, false);

            if (em.HasComponent<PhysicsCollider>(entity))
                em.SetComponentData(entity, new PhysicsCollider { Value = blob });
            else
                em.AddComponentData(entity, new PhysicsCollider { Value = blob });

            EnsureKinematicBody(em, entity, blob);
            SyncHurtboxTransform();
            return true;
        }

        static void EnsureKinematicBody(
            EntityManager em, Entity entity,
            BlobAssetReference<Unity.Physics.Collider> blob)
        {
            var mass = PhysicsMass.CreateKinematic(blob.Value.MassProperties);
            if (em.HasComponent<PhysicsMass>(entity))
                em.SetComponentData(entity, mass);
            else
                em.AddComponentData(entity, mass);

            if (!em.HasComponent<PhysicsVelocity>(entity))
                em.AddComponentData(entity, new PhysicsVelocity());

            var gravity = new PhysicsGravityFactor { Value = 0f };
            if (em.HasComponent<PhysicsGravityFactor>(entity))
                em.SetComponentData(entity, gravity);
            else
                em.AddComponentData(entity, gravity);

            em.SetSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });
        }

        void SyncHurtboxTransform()
        {
            if (BakedEntity == Entity.Null)
                return;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;
            var em = world.EntityManager;
            if (!em.Exists(BakedEntity))
                return;

            var pos = (float3)transform.position;
            if (em.HasComponent<LocalTransform>(BakedEntity))
            {
                var lt = em.GetComponentData<LocalTransform>(BakedEntity);
                lt.Position = pos;
                em.SetComponentData(BakedEntity, lt);
            }
            if (em.HasComponent<LocalToWorld>(BakedEntity))
            {
                em.SetComponentData(BakedEntity, new LocalToWorld
                {
                    Value = float4x4.TRS(pos, quaternion.identity, new float3(1f)),
                });
            }
        }

        void DestroyOwnedEntity()
        {
            if (!_ownedEntity || BakedEntity == Entity.Null)
                return;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                var em = world.EntityManager;
                if (em.Exists(BakedEntity))
                    em.DestroyEntity(BakedEntity);
            }
            BakedEntity = Entity.Null;
            _ownedEntity = false;
        }

        /// <summary>Remove the PhysicsCollider (keeps the entity / SpriteHurtbox).</summary>
        public void ClearUnityPhysicsCollider()
        {
            if (BakedEntity == Entity.Null)
                return;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;
            var em = world.EntityManager;
            if (em.Exists(BakedEntity) && em.HasComponent<PhysicsCollider>(BakedEntity))
                em.RemoveComponent<PhysicsCollider>(BakedEntity);
        }

        public bool HasColliderAttached
        {
            get
            {
                if (BakedEntity == Entity.Null)
                    return false;
                var world = World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated)
                    return false;
                var em = world.EntityManager;
                return em.Exists(BakedEntity) && em.HasComponent<PhysicsCollider>(BakedEntity);
            }
        }

        public static bool AnyColliderAttached()
        {
            foreach (var enemy in FindObjectsByType<UnityPhysicsExampleEnemy>(FindObjectsSortMode.None))
            {
                if (enemy != null && enemy.HasColliderAttached)
                    return true;
            }
            return false;
        }

        public bool TryGetHurtBounds(out Rect bounds)
        {
            if (_dying)
            {
                bounds = default;
                return false;
            }
            var world = transform.position;
            bounds = Rect.MinMaxRect(world.x - 0.5f, world.y, world.x + 0.5f, world.y + 1f);
            return true;
        }

        public void ReceiveHit(int damage, int attackId)
        {
            if (_dying || attackId == LastHitAttackId)
                return;
            LastHitAttackId = attackId;
            Health = Mathf.Max(0, Health - Mathf.Max(1, damage));

            if (Health == 0)
            {
                _dying = true;
                _respawnAt = Time.time + RespawnDelay;
                if (_set != null && DeathClipIndex < _set.Clips.Length)
                    _player.Play(DeathClipIndex, force: true);
                return;
            }
            if (HurtClipIndex < _set.Clips.Length && _player.ClipIndex != HurtClipIndex)
                _player.Play(HurtClipIndex, force: true);
        }

        void Respawn()
        {
            _dying = false;
            Health = MaxHealth;
            transform.position = _spawnPosition;
            _player.Play(IdleClipIndex, force: true);
            EnsureSocketsEnabled();
            _healthBarSocketChild = FindHealthBarSocketChild();
            _healthBarResolved = false;
            EnsurePhysicsHurtbox();
        }

        Vector3 ResolveHealthBarWorldPosition()
        {
            if (_healthBarSocketChild == null)
                _healthBarSocketChild = FindHealthBarSocketChild();
            if (_healthBarSocketChild != null)
                return _healthBarSocketChild.position;

            return transform.position + Vector3.up * HealthBarWorldOffsetY;
        }

        Transform FindHealthBarSocketChild()
        {
            string preferred = string.IsNullOrWhiteSpace(HealthBarSocketName)
                ? null
                : HealthBarSocketName.Trim();

            if (!string.IsNullOrEmpty(preferred))
            {
                var underRoot = transform.Find(SpriteSocketWorld.RootName + "/" + preferred);
                if (underRoot != null)
                    return underRoot;
                var direct = transform.Find(preferred);
                if (direct != null)
                    return direct;
            }

            for (int i = 0; i < DefaultSocketNames.Length; i++)
            {
                string name = DefaultSocketNames[i];
                var underRoot = transform.Find(SpriteSocketWorld.RootName + "/" + name);
                if (underRoot != null)
                    return underRoot;
                var direct = transform.Find(name);
                if (direct != null)
                    return direct;
            }

            var root = transform.Find(SpriteSocketWorld.RootName);
            if (root != null && root.childCount > 0)
                return root.GetChild(0);

            return null;
        }

        void OnGUI()
        {
            if (_dying)
                return;
            var cam = Camera.main;
            if (cam == null)
                return;

            Vector3 world = _healthBarResolved
                ? _healthBarWorld
                : ResolveHealthBarWorldPosition();

            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f)
                return;

            var bg = new Rect(screen.x - 45f, Screen.height - screen.y - 6f, 90f, 12f);
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(bg, Texture2D.whiteTexture);
            var fill = new Rect(bg.x, bg.y, bg.width * Mathf.Clamp01(Health / (float)MaxHealth), bg.height);
            GUI.color = new Color(0.2f, 0.9f, 0.3f);
            GUI.DrawTexture(fill, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
