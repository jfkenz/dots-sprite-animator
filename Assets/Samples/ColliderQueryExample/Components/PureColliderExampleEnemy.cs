using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Pure variant of ColliderExampleEnemy: health, hurt/death clips, tint
    /// flash, and respawn — hurt detection is pure SpriteHitboxQuery bounds
    /// overlap driven by the player (no Rigidbody2D, no trigger callbacks).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteAnimPlayerAuthoring))]
    [RequireComponent(typeof(SpriteAnimSetAuthoring))]
    public sealed class PureColliderExampleEnemy : MonoBehaviour
    {
        [Min(1)] public int MaxHealth = 5;
        public float HealthBarWorldOffsetY = 1.15f;
        [Min(0.01f)] public float FlashSeconds = 0.12f;

        [Tooltip("Health bar anchor socket (optional — falls back to this transform).")]
        public string HealthBarSocketName = "HealthBarSocket";
        public string HealthBarSocketId = "";

        [Min(0)] public int DeathClipIndex = 4;
        [Min(0)] public int IdleClipIndex = 0;
        [Min(0.05f)] public float RespawnDelay = 0.5f;
        [Tooltip("ON: bar only draws after the enemy takes damage. OFF (like the original example): the bar is always visible while alive.")]
        public bool ShowOnlyWhenDamaged;
        [Min(0)] public int HurtClipIndex = 2;
        public int DeathEventId;
        public string DeathEventName = "Death";

        public int Health { get; private set; }
        public int LastHitAttackId { get; private set; } = -1;

        SpriteAnimPlayerAuthoring _player;
        SpriteAnimSetAuthoring _set;
        bool _dying;
        float _respawnAt;
        Vector3 _spawnPosition;
        Transform _healthBarSocketChild;

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
        }

        void Update()
        {
            if (_dying && Time.time >= _respawnAt)
                Respawn();
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
                var underRoot = transform.Find(
                    SpriteSocketWorld.RootName + "/" + DefaultSocketNames[i]);
                if (underRoot != null)
                    return underRoot;
                var direct = transform.Find(DefaultSocketNames[i]);
                if (direct != null)
                    return direct;
            }

            var root = transform.Find(SpriteSocketWorld.RootName);
            if (root != null && root.childCount > 0)
                return root.GetChild(0);
            return null;
        }

        public bool TryGetHurtBounds(out Rect bounds)
        {
            if (_dying)
            {
                bounds = default;
                return false;
            }

            // character body boxes first; fall back to the whole cell rect
            if (SpriteHitboxQuery.TryGetBounds(_set, CurrentClipName(), _player.Frame,
                    SpriteHitboxQuery.CharacterBoxes | SpriteHitboxQuery.ClipBoxes,
                    _player.FlipX, out bounds))
                return true;

            var data = _set != null && _set.Profile != null ? _set.Profile.Data : null;
            var sheet = data != null ? SpriteSocketWorld.DisplaySheet(data, null) : null;
            if (sheet == null)
                return false;
            if (!SpriteSheetProfile.TryGetCellPixels(sheet, out float cw, out float ch))
            {
                cw = 100f;
                ch = 100f;
            }
            float ppu = SpriteSheetProfile.GetPixelsPerUnit(sheet);
            float w = cw / ppu;
            float h = ch / ppu;
            bounds = Rect.MinMaxRect(
                transform.position.x - w * 0.5f,
                transform.position.y,
                transform.position.x + w * 0.5f,
                transform.position.y + h);
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

        string CurrentClipName()
        {
            if (_set != null && _set.Clips != null &&
                _player.ClipIndex >= 0 && _player.ClipIndex < _set.Clips.Length)
                return _set.Clips[_player.ClipIndex].Name;
            var data = _set != null && _set.Profile != null ? _set.Profile.Data : null;
            if (data != null && data.Clips != null &&
                _player.ClipIndex >= 0 && _player.ClipIndex < data.Clips.Count)
                return data.Clips[_player.ClipIndex].Name;
            return null;
        }

        void Respawn()
        {
            _dying = false;
            Health = MaxHealth;
            transform.position = _spawnPosition;
            _player.Play(IdleClipIndex, force: true);
        }

        void OnGUI()
        {
            if (_dying || (ShowOnlyWhenDamaged && Health >= MaxHealth))
                return;
            var cam = Camera.main;
            if (cam == null)
                return;
            Vector3 world = ResolveHealthBarWorldPosition();
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f)
                return;
            var rect = new Rect(screen.x - 24f, Screen.height - screen.y - 6f, 48f, 6f);
            GUI.Label(rect, string.Empty);
            EditorGUI_drawBar(rect, Health / (float)MaxHealth);
        }

        static void EditorGUI_drawBar(Rect rect, float fill)
        {
            GUI.color = Color.black;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            rect.x += 1f;
            rect.y += 1f;
            rect.width = (rect.width - 2f) * Mathf.Clamp01(fill);
            rect.height -= 2f;
            GUI.color = new Color(0.2f, 0.9f, 0.3f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

    }
}
