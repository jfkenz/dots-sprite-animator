using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Simple left/right mover + attack that deals damage when attack frame colliders overlap an enemy hurtbox.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteAnimPlayerAuthoring))]
    [RequireComponent(typeof(SpriteAnimSetAuthoring))]
    public sealed class ColliderExamplePlayer : MonoBehaviour
    {
        [Min(0.1f)] public float MoveSpeed = 3f;

        [Tooltip("Default KeyCode.J â€” mapped through the Input System keyboard.")]
        public KeyCode AttackKey = KeyCode.J;
        public KeyCode LeftKey = KeyCode.A;
        public KeyCode RightKey = KeyCode.D;
        public KeyCode LeftArrow = KeyCode.LeftArrow;
        public KeyCode RightArrow = KeyCode.RightArrow;

        [Min(0)] public int IdleClipIndex = 0;
        [Min(0)] public int WalkClipIndex = 1;
        [Min(0)] public int AttackClipIndex = 13;

        [Tooltip("When true, draws the A/D/J help box in the corner.")]
        public bool ShowHelpOverlay = false;

        SpriteAnimPlayerAuthoring _player;
        SpriteAnimSetAuthoring _set;
        bool _attacking;
        bool _facingLeft;
        int _lastHitAttackId = -1;
        int _attackId;
        GUIStyle _helpStyle;
        readonly List<Collider2D> _overlapScratch = new(16);
        Collider2D[] _cachedColliders;

        void Awake()
        {
            _player = GetComponent<SpriteAnimPlayerAuthoring>();
            _set = GetComponent<SpriteAnimSetAuthoring>();
        }

        void OnEnable()
        {
            EnsureKinematicBody();
            if (_set != null)
            {
                _set.BakeUnityColliders = true;
                _set.BakeFrameColliders = true;
            }
            _cachedColliders = null;
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

            if (WasPressed(AttackKey))
            {
                BeginAttack();
                return;
            }

            if (Mathf.Abs(axis) > 0.01f)
            {
                _facingLeft = axis < 0f;
                _player.SetFlip(_facingLeft, false);
                var p = transform.position;
                p.x += axis * MoveSpeed * Time.deltaTime;
                transform.position = p;
            }

            PlayLocomotion(axis);
        }

        void BeginAttack()
        {
            EnsureAttackIsOnce();
            if (!_player.Play(AttackClipIndex))
                return;

            _attacking = true;
            _attackId++;
            _lastHitAttackId = -1;
            _cachedColliders = null;
        }

        void PlayLocomotion(float axis)
        {
            int desired = Mathf.Abs(axis) > 0.01f ? WalkClipIndex : IdleClipIndex;
            if (_player.ClipIndex != desired || !_player.Playing)
                _player.Play(desired);
        }

        void EnsureAttackIsOnce()
        {
            if (_set == null || _set.Clips == null)
                return;
            if (AttackClipIndex < 0 || AttackClipIndex >= _set.Clips.Length)
                return;

            var clip = _set.Clips[AttackClipIndex];
            clip.Loop = false;
            clip.WrapMode = SpriteAnimWrap.Once;
            _set.Clips[AttackClipIndex] = clip;
        }

        void EnsureKinematicBody()
        {
            var rb = GetComponent<Rigidbody2D>();
            if (rb == null)
                rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.simulated = true;
            rb.useFullKinematicContacts = true;
        }

        void OnTriggerEnter2D(Collider2D other) => TryHit(other);
        void OnTriggerStay2D(Collider2D other) => TryHit(other);

        /// <summary>
        /// Fallback when kinematic-vs-kinematic triggers are flaky: overlap-check
        /// enabled child colliders against the world while attacking.
        /// </summary>
        void TryOverlapAttackHits()
        {
            if (_lastHitAttackId == _attackId)
                return;
            if (_player == null || _player.ClipIndex != AttackClipIndex)
                return;

            if (_cachedColliders == null || _cachedColliders.Length == 0)
                _cachedColliders = GetComponentsInChildren<Collider2D>(true);

            var filter = new ContactFilter2D
            {
                useTriggers = true,
                useLayerMask = false,
                useDepth = false,
            };

            for (int i = 0; i < _cachedColliders.Length; i++)
            {
                var col = _cachedColliders[i];
                if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
                    continue;
                // Prefer attack hitboxes (triggers); skip non-trigger body if present.
                if (!col.isTrigger)
                    continue;

                _overlapScratch.Clear();
                int n = col.Overlap(filter, _overlapScratch);
                for (int j = 0; j < n; j++)
                {
                    TryHit(_overlapScratch[j]);
                    if (_lastHitAttackId == _attackId)
                        return;
                }
            }
        }

        void TryHit(Collider2D other)
        {
            if (!_attacking || other == null)
                return;
            if (_player == null || _player.ClipIndex != AttackClipIndex)
                return;
            if (_lastHitAttackId == _attackId)
                return;
            // Unity fake-null: collider/transform may already be destroyed mid-overlap.
            if (other.transform == null)
                return;
            if (other.transform == transform || other.transform.IsChildOf(transform))
                return;

            var enemy = other.GetComponentInParent<ColliderExampleEnemy>();
            // Enemy may be pending-destroy / already Destroyed this frame.
            if (enemy == null || enemy.IsDead || enemy.IsPendingDestroy)
                return;
            if (!enemy.IsHurtbox(other))
                return;

            if (enemy.TakeDamage(1))
                _lastHitAttackId = _attackId;
        }

        static bool IsHeld(KeyCode code)
        {
            var control = ControlFor(code);
            return control != null && control.isPressed;
        }

        static bool WasPressed(KeyCode code)
        {
            var control = ControlFor(code);
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
            if (!ShowHelpOverlay)
                return;

            _helpStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 15,
                normal = { textColor = Color.white },
                padding = new RectOffset(12, 12, 10, 10),
            };

            string state = _attacking ? $"Attack (clip {AttackClipIndex})" :
                (_player != null && _player.ClipIndex == WalkClipIndex ? "Walk" : "Idle");
            GUI.Box(
                new Rect(16f, 16f, 460f, 110f),
                "DOTS Sprite Animator - Collider Example\n" +
                "A/D or Arrows: move   J: attack\n" +
                $"Clips: idle={IdleClipIndex}  walk={WalkClipIndex}  attack={AttackClipIndex}\n" +
                $"State: {state}",
                _helpStyle);
        }
    }
}

