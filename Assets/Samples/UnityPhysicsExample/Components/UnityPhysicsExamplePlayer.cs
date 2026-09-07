using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Caches the physics world singleton so MonoBehaviour gameplay can run
    /// Unity Physics overlap queries.
    /// </summary>
    /// <summary>
    /// Unity Physics variant of the example player: transform movement and
    /// J attacks â€” the slash window is computed with SpriteHitboxQuery and
    /// enemies are detected with a Unity Physics OverlapAabb query against
    /// their baked hurtbox colliders.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteAnimPlayerAuthoring))]
    [RequireComponent(typeof(SpriteAnimSetAuthoring))]
    public sealed class UnityPhysicsExamplePlayer : MonoBehaviour
    {
        [Min(0.1f)] public float MoveSpeed = 3f;

        public KeyCode AttackKey = KeyCode.J;
        public KeyCode LeftKey = KeyCode.A;
        public KeyCode RightKey = KeyCode.D;
        public KeyCode LeftArrow = KeyCode.LeftArrow;
        public KeyCode RightArrow = KeyCode.RightArrow;

        [Min(0)] public int IdleClipIndex = 0;
        [Min(0)] public int WalkClipIndex = 1;
        [Min(0)] public int AttackClipIndex = 13;

        [Min(1)] public int AttackDamage = 1;
        [Tooltip("Extra reach added to the attack box on the facing side.")]
        [Min(0f)] public float AttackReachPadding = 0.3f;
        [Tooltip("On-screen readout of the attack query while attacking.")]
        public bool ShowQueryDebug = true;
        [Tooltip("Show the A/D/J help box in the corner.")]
        public bool ShowHelpOverlay = false;

        SpriteAnimPlayerAuthoring _player;
        SpriteAnimSetAuthoring _set;
        bool _attacking;
        bool _facingLeft;
        int _attackId;
        string _hitLog = "";
        string hitLog
        {
            get => _hitLog;
            set => _hitLog = value;
        }
        string _debugInfo = "";
        GUIStyle _helpStyle;

        void Awake()
        {
            _player = GetComponent<SpriteAnimPlayerAuthoring>();
            _set = GetComponent<SpriteAnimSetAuthoring>();
            UnityPhysicsOverlapBridge.EntityHit += OnBridgeEntityHit;
        }

        void OnDestroy()
        {
            UnityPhysicsOverlapBridge.EntityHit -= OnBridgeEntityHit;
        }

        void OnBridgeEntityHit(Entity entity, int attackId, int damage)
        {
            var enemy = FindEnemyByEntity(entity);
            if (enemy == null)
                return;
            if (enemy.LastHitAttackId == attackId)
                return;
            _hitLog = "\n" + enemy.name + ": HIT";
            enemy.ReceiveHit(damage, attackId);
        }

        void Update()
        {
            if (_player == null)
                return;

            if (_attacking)
            {
                TryOverlapAttackHits();
                if (!_player.Playing || _player.ClipIndex != AttackClipIndex)
                {
                    _attacking = false;
                    PlayLocomotion(0f);
                }
                return;
            }

            float axis = 0f;
            if (IsHeld(LeftKey) || IsHeld(LeftArrow))
                axis -= 1f;
            if (IsHeld(RightKey) || IsHeld(RightArrow))
                axis += 1f;

            if (axis != 0f)
            {
                transform.position += new Vector3(axis * MoveSpeed * Time.deltaTime, 0f, 0f);
                SetFacing(axis < 0f);
                if (_player.ClipIndex != WalkClipIndex)
                    _player.Play(WalkClipIndex);
            }
            else if (_player.ClipIndex != IdleClipIndex || !_player.Playing)
            {
                _player.Play(IdleClipIndex);
            }

            if (IsPressed(AttackKey))
                BeginAttack();
        }

        void BeginAttack()
        {
            EnsureAttackIsOnce();
            if (!_player.Play(AttackClipIndex))
                return;

            _attacking = true;
            _attackId++;
        }

        void EnsureAttackIsOnce()
        {
            if (_set == null || _set.Clips == null)
                return;
            if (AttackClipIndex < 0 || AttackClipIndex >= _set.Clips.Length)
                return;

            // profile attack clips are authored looping; the attack must end
            // on its own or the player stays in the attack state forever
            var clip = _set.Clips[AttackClipIndex];
            clip.Loop = false;
            clip.WrapMode = SpriteAnimWrap.Once;
            _set.Clips[AttackClipIndex] = clip;
        }

        void TryOverlapAttackHits()
        {
            if (_set == null)
                return;
            string clipName = ClipName(AttackClipIndex);
            bool gotBounds = SpriteHitboxQuery.TryGetBounds(_set, clipName, _player.Frame,
                SpriteHitboxQuery.FrameBoxes | SpriteHitboxQuery.ClipBoxes,
                _facingLeft, out var attackBounds);

            if (gotBounds && AttackReachPadding > 0f)
            {
                if (_facingLeft) attackBounds.xMin -= AttackReachPadding;
                else attackBounds.xMax += AttackReachPadding;
            }

            string hitLog = "";
            if (gotBounds && UnityPhysicsOverlapBridge.Ready)
            {
                // executed inside the bridge system update, against the live
                // physics world (never the stale editor-loop copy)
                UnityPhysicsOverlapBridge.Queue(new Aabb
                {
                    Min = new float3(attackBounds.xMin, attackBounds.yMin, -0.5f),
                    Max = new float3(attackBounds.xMax, attackBounds.yMax, 0.5f),
                }, _attackId, AttackDamage);
                hitLog = "\nattack queued (result on next frame)";
            }
            else if (!gotBounds)
            {
                hitLog = "\nbounds=NONE (no slash boxes on this frame)";
            }

            int enemyBodies = 0;
            foreach (var e in FindObjectsByType<UnityPhysicsExampleEnemy>(FindObjectsSortMode.None))
            {
                if (e != null && e.HasColliderAttached)
                    enemyBodies++;
            }
            _debugInfo = "clip='" + (clipName ?? "NULL") + "' frame=" + _player.Frame +
                         " flip=" + _facingLeft + "\nbounds=" +
                         (gotBounds ? attackBounds.ToString() : "NONE") +
                         "\nphysics ready=" + UnityPhysicsOverlapBridge.Ready +
                         " bodies=" + UnityPhysicsOverlapBridge.BodyCount +
                         " enemyHurtboxes=" + enemyBodies + hitLog;
        }

        UnityPhysicsExampleEnemy FindEnemyByEntity(Entity entity)
        {
            var enemies = FindObjectsByType<UnityPhysicsExampleEnemy>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                if (enemies[i] != null && enemies[i].BakedEntity == entity)
                    return enemies[i];
            }
            return null;
        }

        string ClipName(int index)
        {
            if (_set != null && _set.Clips != null &&
                index >= 0 && index < _set.Clips.Length)
                return _set.Clips[index].Name;
            var data = _set != null && _set.Profile != null ? _set.Profile.Data : null;
            if (data != null && data.Clips != null &&
                index >= 0 && index < data.Clips.Count)
                return data.Clips[index].Name;
            return null;
        }

        void PlayLocomotion(float axis)
        {
            int desired = Mathf.Abs(axis) > 0.01f ? WalkClipIndex : IdleClipIndex;
            if (_player.ClipIndex != desired || !_player.Playing)
                _player.Play(desired);
        }

        void SetFacing(bool flipX)
        {
            _facingLeft = flipX;
            _player.SetFacing(flipX);
        }

        static bool IsHeld(KeyCode key)
        {
            var control = ControlFor(key);
            return control != null && control.isPressed;
        }

        static bool IsPressed(KeyCode key)
        {
            var control = ControlFor(key);
            return control != null && control.wasPressedThisFrame;
        }

        static KeyControl ControlFor(KeyCode code)
        {
            var kb = Keyboard.current;
            if (kb == null)
                return null;

            switch (code)
            {
                case KeyCode.A: return kb.aKey;
                case KeyCode.D: return kb.dKey;
                case KeyCode.J: return kb.jKey;
                case KeyCode.K: return kb.kKey;
                case KeyCode.LeftArrow: return kb.leftArrowKey;
                case KeyCode.RightArrow: return kb.rightArrowKey;
                case KeyCode.UpArrow: return kb.upArrowKey;
                case KeyCode.DownArrow: return kb.downArrowKey;
                case KeyCode.Space: return kb.spaceKey;
                default: return null;
            }
        }

        void OnGUI()
        {
            if (ShowQueryDebug && _attacking && !string.IsNullOrEmpty(_debugInfo))
            {
                var style = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                GUI.Label(new Rect(12f, 60f, 900f, 120f), _debugInfo, style);
            }
            if (!ShowHelpOverlay)
                return;
            _helpStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14 };
            GUI.Label(new Rect(12f, 12f, 320f, 40f),
                "A / D move   â€¢   J attack (Unity Physics)", _helpStyle);
        }
    }
}
