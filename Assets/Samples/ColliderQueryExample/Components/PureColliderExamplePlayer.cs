using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Pure variant of ColliderExamplePlayer: movement on the transform, and
    /// attack hits detected purely with SpriteHitboxQuery bounds overlap
    /// against enemy hurtboxes — no Rigidbody2D, no Collider2D, no triggers.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteAnimPlayerAuthoring))]
    [RequireComponent(typeof(SpriteAnimSetAuthoring))]
    public sealed class PureColliderExamplePlayer : MonoBehaviour
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
        [Tooltip("Show the A/D/J help box in the corner.")]
        public bool ShowHelpOverlay = false;
        [Tooltip("On-screen readout of the attack query while attacking.")]
        public bool ShowQueryDebug = true;

        SpriteAnimPlayerAuthoring _player;
        SpriteAnimSetAuthoring _set;
        bool _attacking;
        bool _facingLeft;
        int _attackId;
        string _debugInfo = "";
        GUIStyle _helpStyle;

        void Awake()
        {
            _player = GetComponent<SpriteAnimPlayerAuthoring>();
            _set = GetComponent<SpriteAnimSetAuthoring>();
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
            else
            {
                if (_player.ClipIndex != IdleClipIndex)
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

            string hitLog = "";
            var enemies = FindObjectsByType<PureColliderExampleEnemy>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                var enemy = enemies[i];
                if (enemy == null || enemy.gameObject == gameObject)
                    continue;
                bool hasHurt = enemy.TryGetHurtBounds(out var hurt);
                bool overlap = hasHurt && SpriteHitboxQuery.Overlaps(attackBounds, hurt);
                hitLog += "\n" + enemy.name + ": hurt=" +
                          (hasHurt ? hurt.ToString() : "none") + " overlap=" + overlap;
                if (overlap && enemy.LastHitAttackId != _attackId)
                    enemy.ReceiveHit(AttackDamage, _attackId);
            }

            _debugInfo = "clip='" + (clipName ?? "NULL") + "' frame=" + _player.Frame +
                         " flip=" + _facingLeft + "\nbounds=" +
                         (gotBounds ? attackBounds.ToString() : "NONE") + hitLog;
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
                "A / D move   •   J attack (pure query)", _helpStyle);
        }

        string ClipName(int index)
        {
            // authoring clips (non-profile sets) first...
            if (_set != null && _set.Clips != null &&
                index >= 0 && index < _set.Clips.Length)
                return _set.Clips[index].Name;
            // ...then profile clips (profile-driven sets keep their clips there)
            var data = _set != null && _set.Profile != null ? _set.Profile.Data : null;
            if (data != null && data.Clips != null &&
                index >= 0 && index < data.Clips.Count)
                return data.Clips[index].Name;
            return null;
        }

        void OnDrawGizmos()
        {
            if (!_attacking)
                return;
            string clipName = ClipName(AttackClipIndex);
            if (SpriteHitboxQuery.TryGetBounds(_set, clipName, _player.Frame,
                    SpriteHitboxQuery.FrameBoxes | SpriteHitboxQuery.ClipBoxes,
                    _facingLeft, out var attackBounds))
            {
                Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.9f);
                Gizmos.DrawWireCube(attackBounds.center, attackBounds.size);
            }
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
    }
}
