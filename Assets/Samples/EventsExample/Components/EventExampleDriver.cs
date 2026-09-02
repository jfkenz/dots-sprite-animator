using UnityEngine;
using UnityEngine.InputSystem;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Plays Walk / Attack so Footstep and Attack markers fire.
    /// Hold A/D (or arrows) to walk; press J / Space to attack; T toggles auto-cycle.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteAnimPlayerAuthoring))]
    [RequireComponent(typeof(SpriteAnimSetAuthoring))]
    public sealed class EventExampleDriver : MonoBehaviour
    {
        public KeyCode AttackKey = KeyCode.J;
        public KeyCode AttackKeyAlt = KeyCode.Space;
        public KeyCode LeftKey = KeyCode.A;
        public KeyCode RightKey = KeyCode.D;
        public KeyCode LeftArrow = KeyCode.LeftArrow;
        public KeyCode RightArrow = KeyCode.RightArrow;
        public KeyCode AutoCycleKey = KeyCode.T;

        [Min(0)] public int IdleClipIndex = 0;
        [Min(0)] public int WalkClipIndex = 1;
        [Min(0)] public int AttackClipIndex = 13;

        [Tooltip("When enabled, cycles Idle → Walk → Attack automatically.")]
        public bool AutoCycle;

        [Min(0.25f)] public float AutoWalkSeconds = 2.5f;
        [Min(0.25f)] public float AutoIdleSeconds = 1.0f;

        public bool ShowHelpOverlay = true;

        SpriteAnimPlayerAuthoring _player;
        SpriteAnimSetAuthoring _set;
        EventExampleBootstrap _bootstrap;
        bool _attacking;
        bool _facingLeft;
        float _autoTimer;
        int _autoPhase; // 0 idle, 1 walk, 2 attack
        GUIStyle _helpStyle;

        void Awake()
        {
            _player = GetComponent<SpriteAnimPlayerAuthoring>();
            _set = GetComponent<SpriteAnimSetAuthoring>();
            _bootstrap = GetComponent<EventExampleBootstrap>();
        }

        void Start()
        {
            SyncIndicesFromBootstrap();
            if (_player != null)
                _player.Play(IdleClipIndex);
        }

        void SyncIndicesFromBootstrap()
        {
            if (_bootstrap == null)
                return;
            IdleClipIndex = _bootstrap.IdleClipIndex;
            WalkClipIndex = _bootstrap.WalkClipIndex;
            AttackClipIndex = _bootstrap.AttackClipIndex;
        }

        void Update()
        {
            if (_player == null)
                return;

            SyncIndicesFromBootstrap();

            var keyboard = Keyboard.current;
            if (keyboard != null && WasPressed(AutoCycleKey))
            {
                AutoCycle = !AutoCycle;
                _autoTimer = 0f;
                _autoPhase = 0;
            }

            if (_attacking)
            {
                if (!_player.Playing || _player.ClipIndex != AttackClipIndex)
                {
                    _attacking = false;
                    if (!AutoCycle)
                        PlayLocomotion(0f);
                }
                return;
            }

            if (AutoCycle)
            {
                TickAutoCycle();
                return;
            }

            float axis = 0f;
            if (IsHeld(LeftKey) || IsHeld(LeftArrow))
                axis -= 1f;
            if (IsHeld(RightKey) || IsHeld(RightArrow))
                axis += 1f;

            if (WasPressed(AttackKey) || WasPressed(AttackKeyAlt))
            {
                BeginAttack();
                return;
            }

            if (Mathf.Abs(axis) > 0.01f)
                _facingLeft = axis < 0f;

            if (_player != null)
                _player.SetFlip(_facingLeft, false);

            PlayLocomotion(axis);
        }

        void TickAutoCycle()
        {
            _autoTimer += Time.deltaTime;
            switch (_autoPhase)
            {
                case 0: // idle
                    if (_player.ClipIndex != IdleClipIndex || !_player.Playing)
                        _player.Play(IdleClipIndex);
                    if (_autoTimer >= AutoIdleSeconds)
                    {
                        _autoTimer = 0f;
                        _autoPhase = 1;
                    }
                    break;
                case 1: // walk
                    if (_player.ClipIndex != WalkClipIndex || !_player.Playing)
                        _player.Play(WalkClipIndex);
                    if (_autoTimer >= AutoWalkSeconds)
                    {
                        _autoTimer = 0f;
                        _autoPhase = 2;
                        BeginAttack();
                    }
                    break;
                case 2: // wait for attack to finish
                    if (!_attacking)
                    {
                        _autoTimer = 0f;
                        _autoPhase = 0;
                    }
                    break;
            }
        }

        void BeginAttack()
        {
            EnsureAttackIsOnce();
            if (!_player.Play(AttackClipIndex, force: true))
                return;
            _attacking = true;
        }

        void PlayLocomotion(float axis)
        {
            int desired = Mathf.Abs(axis) > 0.01f ? WalkClipIndex : IdleClipIndex;
            if (_player.ClipIndex != desired || !_player.Playing)
                _player.Play(desired);
        }

        void EnsureAttackIsOnce()
        {
            if (_set?.Clips == null)
                return;
            if (AttackClipIndex < 0 || AttackClipIndex >= _set.Clips.Length)
                return;

            var clip = _set.Clips[AttackClipIndex];
            clip.Loop = false;
            clip.WrapMode = SpriteAnimWrap.Once;
            _set.Clips[AttackClipIndex] = clip;
        }

        void OnGUI()
        {
            if (!ShowHelpOverlay)
                return;

            _helpStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 14,
                normal = { textColor = Color.white },
                padding = new RectOffset(12, 12, 8, 8),
            };

            string mode = AutoCycle ? "AUTO" : "MANUAL";
            GUI.Box(
                new Rect(16f, 140f, 420f, 78f),
                $"Controls [{mode}]\nA/D or ←/→ walk · J / Space attack · T auto-cycle\nWalk fires Footstep · Attack fires Attack",
                _helpStyle);
        }

        static bool IsHeld(KeyCode key)
        {
            var control = KeyControl(key);
            return control != null && control.isPressed;
        }

        static bool WasPressed(KeyCode key)
        {
            var control = KeyControl(key);
            return control != null && control.wasPressedThisFrame;
        }

        static UnityEngine.InputSystem.Controls.KeyControl KeyControl(KeyCode key)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return null;
            return key switch
            {
                KeyCode.A => keyboard.aKey,
                KeyCode.D => keyboard.dKey,
                KeyCode.J => keyboard.jKey,
                KeyCode.T => keyboard.tKey,
                KeyCode.Space => keyboard.spaceKey,
                KeyCode.LeftArrow => keyboard.leftArrowKey,
                KeyCode.RightArrow => keyboard.rightArrowKey,
                _ => null,
            };
        }
    }
}
