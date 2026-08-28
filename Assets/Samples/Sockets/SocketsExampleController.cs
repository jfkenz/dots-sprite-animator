using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Small GameObject bridge for the sockets sample. Animation remains ECS-driven;
    /// this component reads the current SpriteSocketBuffer and moves one presentation object.
    /// </summary>
    public sealed class SocketsExampleController : MonoBehaviour
    {
        [Header("Sample assets")]
        public ScriptableSpriteSheetProfile Profile;
        public Transform SocketItem;

        [Header("Runtime controls")]
        [Min(0)] public int ClipIndex;
        public string SocketName = "Weapon";

        EntityQuery _playerQuery;
        EntityManager _entityManager;
        Entity _playerEntity;
        World _world;
        bool _queryCreated;
        int _appliedClipIndex = -1;
        bool _hasPose;
        float2 _lastPosition;
        float _lastAngle;
        GUIStyle _labelStyle;

        void Update()
        {
            if (!TryBindPlayer())
                return;

            int clipCount = Profile?.Data?.Clips?.Count ?? 0;
            if (clipCount == 0)
                return;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftBracketKey.wasPressedThisFrame)
                    ClipIndex = PositiveMod(ClipIndex - 1, clipCount);
                if (keyboard.rightBracketKey.wasPressedThisFrame)
                    ClipIndex = PositiveMod(ClipIndex + 1, clipCount);
                if (keyboard.digit1Key.wasPressedThisFrame)
                    ClipIndex = 0;
                if (keyboard.digit2Key.wasPressedThisFrame && clipCount > 1)
                    ClipIndex = 1;
            }

            ClipIndex = Mathf.Clamp(ClipIndex, 0, clipCount - 1);
            if (_appliedClipIndex != ClipIndex)
                PlayClip(ClipIndex);
        }

        void LateUpdate()
        {
            if (!TryBindPlayer() || SocketItem == null)
                return;

            var sockets = _entityManager.GetBuffer<SpriteSocketBuffer>(_playerEntity, true);
            var wanted = new FixedString64Bytes(SpriteSocketKeys.CanonicalName(SocketName));
            for (int i = 0; i < sockets.Length; i++)
            {
                var socket = sockets[i];
                if (!socket.Name.Equals(wanted))
                    continue;

                _lastPosition = socket.LocalPosition;
                _lastAngle = socket.LocalAngle;
                _hasPose = true;
                break;
            }

            // Missing frame keys intentionally retain the previous pose.
            if (!_hasPose || !_entityManager.HasComponent<LocalToWorld>(_playerEntity))
                return;

            var matrix = _entityManager.GetComponentData<LocalToWorld>(_playerEntity).Value;
            float3 right = math.normalizesafe(matrix.c0.xyz, new float3(1f, 0f, 0f));
            float3 up = math.normalizesafe(matrix.c1.xyz, new float3(0f, 1f, 0f));
            float3 origin = matrix.c3.xyz;

            // Socket positions were converted from source pixels with the clip sheet's
            // PPU during baking. Remove entity scale from basis vectors to avoid scaling twice.
            float3 worldPosition = origin + right * _lastPosition.x + up * _lastPosition.y;
            float entityAngle = math.degrees(math.atan2(right.y, right.x));
            SocketItem.SetPositionAndRotation(
                new Vector3(worldPosition.x, worldPosition.y, SocketItem.position.z),
                Quaternion.Euler(0f, 0f, entityAngle + _lastAngle));
        }

        bool TryBindPlayer()
        {
            var currentWorld = World.DefaultGameObjectInjectionWorld;
            if (currentWorld == null || !currentWorld.IsCreated)
                return false;

            if (_world != currentWorld)
            {
                ReleaseQuery();
                _world = currentWorld;
                _entityManager = currentWorld.EntityManager;
                _playerQuery = _entityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<SpriteAnimSetRef>(),
                    ComponentType.ReadWrite<SpriteAnimPlayer>(),
                    ComponentType.ReadOnly<SpriteSocketBuffer>());
                _queryCreated = true;
                _playerEntity = Entity.Null;
                _appliedClipIndex = -1;
            }

            if (_playerEntity != Entity.Null && _entityManager.Exists(_playerEntity))
                return true;

            using var entities = _playerQuery.ToEntityArray(Allocator.Temp);
            if (entities.Length == 0)
                return false;

            _playerEntity = entities[0];
            _appliedClipIndex = -1;
            return true;
        }

        void PlayClip(int clipIndex)
        {
            if (!SpriteAnims.Play(_entityManager, _playerEntity, clipIndex))
                return;

            ConfigureCpuRenderer(clipIndex);
            _appliedClipIndex = clipIndex;
        }

        void ConfigureCpuRenderer(int clipIndex)
        {
            var data = Profile?.Data;
            if (data?.Clips == null || clipIndex < 0 || clipIndex >= data.Clips.Count)
                return;

            data.EnsureSheets();
            var sheet = data.SheetForClip(data.Clips[clipIndex]);
            if (sheet?.Texture == null)
                return;

            // CPU animation path. Sheet/grid follow each clip's SheetIndex.
            SpriteBatchSpawner.LayoutXy = true;
            SpriteInstanceRenderSystem.Install(_entityManager);
            SpriteInstanceRenderSystem.SetSheet(sheet.Texture);
            SpriteInstanceRenderSystem.SetGrid(
                _entityManager,
                Mathf.Max(1, sheet.Columns),
                Mathf.Max(1, sheet.Rows));

            if (_entityManager.HasComponent<LocalTransform>(_playerEntity))
            {
                var transform = _entityManager.GetComponentData<LocalTransform>(_playerEntity);
                transform.Scale = SpriteSheetProfile.GetWorldHeight(sheet);
                _entityManager.SetComponentData(_playerEntity, transform);
            }
        }

        void OnGUI()
        {
            _labelStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 16,
                normal = { textColor = Color.white },
                padding = new RectOffset(14, 14, 10, 10),
            };

            string clipName = "waiting for baked entity";
            if (Profile?.Data?.Clips != null && Profile.Data.Clips.Count > 0)
            {
                int index = Mathf.Clamp(ClipIndex, 0, Profile.Data.Clips.Count - 1);
                clipName = Profile.Data.Clips[index].Name;
            }

            GUI.Box(
                new Rect(16f, 16f, 390f, 92f),
                $"DOTS Sprite Animator - Sockets\nClip: {clipName}   Socket: {SocketName}\n[ / ] or 1 / 2: switch Idle / Attack",
                _labelStyle);
        }

        void OnDestroy() => ReleaseQuery();

        void ReleaseQuery()
        {
            if (_queryCreated)
            {
                _playerQuery.Dispose();
                _queryCreated = false;
            }
            _world = null;
            _playerEntity = Entity.Null;
        }

        static int PositiveMod(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }
    }
}
