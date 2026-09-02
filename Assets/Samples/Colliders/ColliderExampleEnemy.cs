using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Demo enemy with health + hurtbox identity for the collider sample.
    /// Health bar follows the synced Unity child under SpriteSockets when present.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteAnimSetAuthoring))]
    public sealed class ColliderExampleEnemy : MonoBehaviour
    {
        [Min(1)] public int MaxHealth = 5;
        public float HealthBarWorldOffsetY = 1.15f;
        public float FlashSeconds = 0.12f;

        [Tooltip("Socket child name to follow (e.g. HealthBarSocket under SpriteSockets). Empty = try defaults, then offset.")]
        public string HealthBarSocketName = "HealthBarSocket";

        [Tooltip("Optional stable socket ID. Used when Name is empty / not found.")]
        public string HealthBarSocketId = string.Empty;

        [Tooltip("Clip index played when health reaches 0 (Dead).")]
        public int DeadClipIndex = 3;

        [Tooltip("Optional hurt clip (index 2). Played briefly on non-lethal hits when >= 0.")]
        public int HurtClipIndex = 2;

        public int CurrentHealth { get; private set; }

        SpriteAnimSetAuthoring _set;
        SpriteAnimPlayerAuthoring _player;
        Color _baseTint = Color.white;
        float _flashUntil;
        GUIStyle _barLabelStyle;
        Transform _healthBarSocketChild;
        Vector3 _healthBarWorld;
        bool _healthBarResolved;
        bool _dead;
        bool _hurtPlaying;
        int _locomotionClipBeforeHurt;

        static readonly string[] DefaultSocketNames =
        {
            "HealthBarSocket", "Health", "HealthBar", "UI",
        };

        void Awake()
        {
            _set = GetComponent<SpriteAnimSetAuthoring>();
            _player = GetComponent<SpriteAnimPlayerAuthoring>();
            CurrentHealth = MaxHealth;
            if (_set != null)
                _baseTint = _set.Tint;
            _healthBarSocketChild = FindHealthBarSocketChild();
        }

        void OnEnable()
        {
            EnsureKinematicBody();
            if (_set != null)
            {
                _set.BakeUnityColliders = true;
                _set.BakeFrameColliders = true;
                _set.BakeUnitySockets = true;
            }
            if (CurrentHealth <= 0 && !_dead)
                CurrentHealth = MaxHealth;
        }

        void Update()
        {
            if (_set == null)
                return;

            if (_dead)
            {
                _set.Tint = _baseTint;
                if (_player != null)
                    _set.ApplyQuadPreview(_player.ClipIndex, _player.Frame);
                else
                    _set.ApplyQuadPreview();
                return;
            }

            if (_hurtPlaying && _player != null)
            {
                if (!_player.Playing || _player.ClipIndex != HurtClipIndex)
                {
                    _hurtPlaying = false;
                    if (_locomotionClipBeforeHurt >= 0)
                        _player.Play(_locomotionClipBeforeHurt);
                }
            }

            if (Time.time < _flashUntil)
                _set.Tint = Color.Lerp(_baseTint, new Color(1f, 0.25f, 0.25f, 1f), 0.85f);
            else
                _set.Tint = _baseTint;

            if (_player != null)
                _set.ApplyQuadPreview(_player.ClipIndex, _player.Frame);
            else
                _set.ApplyQuadPreview();
        }

        void LateUpdate()
        {
            _healthBarWorld = ResolveHealthBarWorldPosition();
            _healthBarResolved = true;
        }

        /// <summary>Returns true if damage was applied.</summary>
        public bool TakeDamage(int amount)
        {
            if (amount <= 0 || CurrentHealth <= 0 || _dead)
                return false;

            CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
            _flashUntil = Time.time + FlashSeconds;

            if (CurrentHealth <= 0)
            {
                BeginDeath();
            }
            else if (HurtClipIndex >= 0 && _player != null && !_hurtPlaying)
            {
                _locomotionClipBeforeHurt = _player.ClipIndex;
                EnsureClipOnce(HurtClipIndex);
                if (_player.Play(HurtClipIndex))
                    _hurtPlaying = true;
            }

            return true;
        }

        void BeginDeath()
        {
            _dead = true;
            _hurtPlaying = false;
            _baseTint = new Color(0.55f, 0.55f, 0.55f, 1f);
            if (_set != null)
                _set.Tint = _baseTint;

            if (_player == null)
                return;

            // Pause locomotion by locking onto the death clip (Once).
            EnsureClipOnce(DeadClipIndex);
            _player.Play(DeadClipIndex);
        }

        void EnsureClipOnce(int clipIndex)
        {
            if (_set == null || _set.Clips == null)
                return;
            if (clipIndex < 0 || clipIndex >= _set.Clips.Length)
                return;
            var clip = _set.Clips[clipIndex];
            clip.Loop = false;
            clip.WrapMode = SpriteAnimWrap.Once;
            _set.Clips[clipIndex] = clip;
        }

        /// <summary>
        /// Hurtbox check for player attack overlap. Any collider under this enemy counts
        /// (Character body is the intended target once BakeUnityColliders is on).
        /// </summary>
        public bool IsHurtbox(Collider2D col)
        {
            if (col == null)
                return false;
            return col.transform == transform || col.transform.IsChildOf(transform);
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
            var cam = Camera.main;
            if (cam == null)
                return;

            Vector3 world = _healthBarResolved
                ? _healthBarWorld
                : ResolveHealthBarWorldPosition();

            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f)
                return;

            float guiX = screen.x;
            float guiY = Screen.height - screen.y;
            float width = 90f;
            float height = 12f;
            var bg = new Rect(guiX - width * 0.5f, guiY - height * 0.5f, width, height);
            float pct = MaxHealth > 0 ? (float)CurrentHealth / MaxHealth : 0f;
            var fill = new Rect(bg.x, bg.y, width * Mathf.Clamp01(pct), height);

            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(bg, Texture2D.whiteTexture);
            GUI.color = CurrentHealth > 0 ? new Color(0.2f, 0.85f, 0.25f, 0.95f) : new Color(0.5f, 0.1f, 0.1f, 0.9f);
            GUI.DrawTexture(fill, Texture2D.whiteTexture);
            GUI.color = Color.white;

            _barLabelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                normal = { textColor = Color.white },
            };
            GUI.Label(new Rect(bg.x, bg.y - 16f, width, 16f), $"{CurrentHealth}/{MaxHealth}", _barLabelStyle);
        }
    }
}
