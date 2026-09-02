using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Demo enemy with health + hurtbox identity for the collider sample.
    /// Health bar follows the synced Unity child under SpriteSockets when present.
    ///
    /// Death / respawn flow (GO / collider sample path):
    /// Resolve death clip by name "Death" (case-insensitive; fallback index 4) ->
    /// HP 0 -> force-Play death clip Once forward (Interrupt Never) ->
    /// ClipCompleted (or optional Death frame event) -> wait RespawnDelay (0.5s) ->
    /// restore HP + colliders/bake sync + sprite -> play same clip ReverseOnce
    /// (Play seeks to last frame; wrap=4) -> ClipCompleted -> idle (clip 0).
    ///
    /// Colliders are cleared/disabled during Dying (deferred cleanup); restored on respawn
    /// via BakeUnity* flags + SyncUnityColliders / SyncUnitySockets.
    /// Player hits are ignored while not Alive (Dying / WaitingRespawn / Spawning).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteAnimSetAuthoring))]
    public sealed class ColliderExampleEnemy : MonoBehaviour
    {
        public enum EnemyLifeState : byte
        {
            Alive = 0,
            Dying = 1,
            WaitingRespawn = 2,
            Spawning = 3,
        }

        [Min(1)] public int MaxHealth = 5;
        public float HealthBarWorldOffsetY = 1.15f;
        public float FlashSeconds = 0.12f;

        [Tooltip("Socket child name to follow (e.g. HealthBarSocket under SpriteSockets). Empty = try defaults, then offset.")]
        public string HealthBarSocketName = "HealthBarSocket";

        [Tooltip("Optional stable socket ID. Used when Name is empty / not found.")]
        public string HealthBarSocketId = string.Empty;

        /// <summary>
        /// Clip index for death + reverse spawn-in. Resolved by name "Death" when possible;
        /// fallback is index 4 (5th clip) if no name match.
        /// </summary>
        [Tooltip("Death/spawn clip index. Auto-resolved from clip name Death; fallback 4.")]
        public int DeadClipIndex = 4;

        [Tooltip("Idle clip after reverse spawn completes.")]
        public int IdleClipIndex = 0;

        [Tooltip("Seconds to wait after death clip finishes before reverse spawn.")]
        [Min(0f)] public float RespawnDelay = 0.5f;

        [Tooltip("Optional hurt clip (index 2). Played briefly on non-lethal hits when >= 0.")]
        public int HurtClipIndex = 2;

        [Tooltip("Optional frame-event Id that also finishes death (0 = ignore Id match).")]
        public byte DeathEventId = 0;

        [Tooltip("Optional frame-event / payload name that finishes death (case-insensitive). Empty = name match off.")]
        public string DeathEventName = "Death";

        public int CurrentHealth { get; private set; }

        public EnemyLifeState LifeState => _lifeState;

        /// <summary>True while not Alive (Dying / WaitingRespawn / Spawning) — player hits ignore.</summary>
        public bool IsDead => _lifeState != EnemyLifeState.Alive;

        /// <summary>True during the post-death respawn wait (no permanent Destroy).</summary>
        public bool IsPendingDestroy => _lifeState == EnemyLifeState.WaitingRespawn;

        SpriteAnimSetAuthoring _set;
        SpriteAnimPlayerAuthoring _player;
        Color _aliveTint = Color.white;
        Color _baseTint = Color.white;
        float _flashUntil;
        GUIStyle _barLabelStyle;
        Transform _healthBarSocketChild;
        Vector3 _healthBarWorld;
        bool _healthBarResolved;
        EnemyLifeState _lifeState = EnemyLifeState.Alive;
        bool _hurtPlaying;
        int _locomotionClipBeforeHurt;
        bool _deathSequenceFinished;
        bool _subscribedPlayer;
        int _lastPolledDeathFrame = -1;
        float _respawnAt = -1f;

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
            {
                _aliveTint = _set.Tint;
                _baseTint = _aliveTint;
            }
            ResolveDeadClipIndex();
            _healthBarSocketChild = FindHealthBarSocketChild();
        }

        /// <summary>
        /// Prefer clip named "Death" (case-insensitive) on authoring / profile;
        /// otherwise keep inspector value, clamped, with fallback index 4.
        /// </summary>
        void ResolveDeadClipIndex()
        {
            const int FallbackIndex = 4;
            int found = FindClipIndexByName("Death");
            if (found >= 0)
            {
                DeadClipIndex = found;
                return;
            }

            int count = _set != null && _set.Clips != null ? _set.Clips.Length : 0;
            if (count <= 0)
            {
                DeadClipIndex = FallbackIndex;
                return;
            }

            if (DeadClipIndex < 0 || DeadClipIndex >= count)
                DeadClipIndex = FallbackIndex < count ? FallbackIndex : count - 1;
        }

        int FindClipIndexByName(string want)
        {
            if (string.IsNullOrWhiteSpace(want))
                return -1;

            if (_set?.Clips != null)
            {
                for (int i = 0; i < _set.Clips.Length; i++)
                {
                    var name = _set.Clips[i].Name;
                    if (!string.IsNullOrWhiteSpace(name)
                        && string.Equals(name.Trim(), want, System.StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }

            var data = _set?.Profile?.Data;
            var clips = data != null ? data.Clips : null;
            if (clips != null)
            {
                for (int i = 0; i < clips.Count; i++)
                {
                    var c = clips[i];
                    if (c == null || string.IsNullOrWhiteSpace(c.Name))
                        continue;
                    if (string.Equals(c.Name.Trim(), want, System.StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }

            return -1;
        }

        void OnEnable()
        {
            EnsureKinematicBody();
            if (_set != null)
            {
                _set.BakeUnityColliders = true;
                _set.BakeFrameColliders = true;
                _set.BakeUnitySockets = true;
                if (!_set.ShowSpriteInScene)
                    _set.ShowSpriteInScene = true;
            }
            if (CurrentHealth <= 0 && _lifeState == EnemyLifeState.Alive)
                CurrentHealth = MaxHealth;

            SubscribeDeathListeners();
        }

        void OnDisable()
        {
            UnsubscribeDeathListeners();
        }

        void SubscribeDeathListeners()
        {
            if (_player != null && !_subscribedPlayer)
            {
                _player.ClipCompleted += OnPlayerClipCompleted;
                _subscribedPlayer = true;
            }
        }

        void UnsubscribeDeathListeners()
        {
            if (_player != null && _subscribedPlayer)
            {
                _player.ClipCompleted -= OnPlayerClipCompleted;
                _subscribedPlayer = false;
            }
        }

        void Update()
        {
            if (_lifeState == EnemyLifeState.WaitingRespawn)
            {
                if (_set != null)
                {
                    _set.Tint = _baseTint;
                    if (_player != null)
                        _set.ApplyQuadPreview(_player.ClipIndex, _player.Frame);
                    else
                        _set.ApplyQuadPreview();
                }

                if (_respawnAt >= 0f && Time.time >= _respawnAt)
                    BeginRespawnSpawn();
                return;
            }

            if (_set == null)
                return;

            if (_lifeState == EnemyLifeState.Dying)
            {
                _set.Tint = _baseTint;
                if (_player != null)
                    _set.ApplyQuadPreview(_player.ClipIndex, _player.Frame);
                else
                    _set.ApplyQuadPreview();

                TryPollDeathFrameEvent();

                // Fallback if ClipCompleted was missed: Once ended on death clip.
                if (!_deathSequenceFinished && _player != null &&
                    _player.ClipIndex == DeadClipIndex && !_player.Playing)
                {
                    OnDeathSequenceFinished();
                }
                return;
            }

            if (_lifeState == EnemyLifeState.Spawning)
            {
                _set.Tint = _baseTint;
                if (_player != null)
                    _set.ApplyQuadPreview(_player.ClipIndex, _player.Frame);
                else
                    _set.ApplyQuadPreview();

                // Fallback if ClipCompleted was missed during reverse Once.
                if (_player != null &&
                    _player.ClipIndex == DeadClipIndex && !_player.Playing)
                {
                    OnSpawnSequenceFinished();
                }
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
            if (_lifeState != EnemyLifeState.Alive)
                return;

            _healthBarWorld = ResolveHealthBarWorldPosition();
            _healthBarResolved = true;
        }

        /// <summary>Returns true if damage was applied.</summary>
        public bool TakeDamage(int amount)
        {
            if (amount <= 0 || CurrentHealth <= 0 || _lifeState != EnemyLifeState.Alive)
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
            _lifeState = EnemyLifeState.Dying;
            _hurtPlaying = false;
            _deathSequenceFinished = false;
            _lastPolledDeathFrame = -1;
            _respawnAt = -1f;
            _baseTint = new Color(0.55f, 0.55f, 0.55f, 1f);
            if (_set != null)
            {
                _set.Tint = _baseTint;
                if (!_set.ShowSpriteInScene)
                    _set.ShowSpriteInScene = true;
            }

            // Deferred collider cleanup for death anim; keep sockets so health bar can follow.
            if (_set != null)
            {
                _set.BakeUnityColliders = false;
                _set.BakeFrameColliders = false;
            }
            SpriteColliderWorld.ClearUnityColliders(transform);
            DisableAllColliders();

            if (_player == null)
            {
                OnDeathSequenceFinished();
                return;
            }

            if (DeadClipIndex < 0 || _set == null || _set.Clips == null ||
                DeadClipIndex >= _set.Clips.Length)
            {
                OnDeathSequenceFinished();
                return;
            }

            // Force Play death (bypass Interrupt Never on hurt) + lock death clip Once forward.
            EnsureDeathClipPlaybackForward();
            _player.SetSpeed(1f);
            _player.Play(DeadClipIndex, force: true);
        }

        void EnsureDeathClipPlaybackForward()
        {
            if (_set == null || _set.Clips == null)
                return;
            if (DeadClipIndex < 0 || DeadClipIndex >= _set.Clips.Length)
                return;

            var clip = _set.Clips[DeadClipIndex];
            clip.Loop = false;
            clip.WrapMode = SpriteAnimWrap.Once;
            clip.OnCompleteClipIndex = -1;
            clip.Interrupt = (byte)SpriteClipInterrupt.Never;
            _set.Clips[DeadClipIndex] = clip;
        }

        /// <summary>
        /// Spawn-in: same death clip with ReverseOnce wrap (last->first then Complete).
        /// Play() seeks to the last frame; no Speed -1 hack.
        /// </summary>
        void EnsureDeathClipPlaybackReverseOnce()
        {
            if (_set == null || _set.Clips == null)
                return;
            if (DeadClipIndex < 0 || DeadClipIndex >= _set.Clips.Length)
                return;

            var clip = _set.Clips[DeadClipIndex];
            clip.Loop = false;
            clip.WrapMode = SpriteAnimWrap.ReverseOnce;
            clip.OnCompleteClipIndex = -1;
            clip.Interrupt = (byte)SpriteClipInterrupt.Never;
            _set.Clips[DeadClipIndex] = clip;
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
            clip.OnCompleteClipIndex = -1;
            _set.Clips[clipIndex] = clip;
        }

        void OnPlayerClipCompleted(int clipIndex)
        {
            if (clipIndex != DeadClipIndex)
                return;

            if (_lifeState == EnemyLifeState.Dying && !_deathSequenceFinished)
            {
                OnDeathSequenceFinished();
                return;
            }

            if (_lifeState == EnemyLifeState.Spawning)
                OnSpawnSequenceFinished();
        }

        /// <summary>
        /// GO path does not raise SpriteAnimEvents; poll profile/authoring markers
        /// on the current death frame for Id / name "Death".
        /// </summary>
        void TryPollDeathFrameEvent()
        {
            if (_deathSequenceFinished || _lifeState != EnemyLifeState.Dying || _player == null)
                return;
            if (_player.ClipIndex != DeadClipIndex)
                return;

            int frame = _player.Frame;
            if (frame == _lastPolledDeathFrame)
                return;
            _lastPolledDeathFrame = frame;

            if (FrameHasDeathEvent(frame))
                OnDeathSequenceFinished();
        }

        bool FrameHasDeathEvent(int frame)
        {
            if (frame < 0)
                return false;

            string wantName = string.IsNullOrWhiteSpace(DeathEventName)
                ? null
                : DeathEventName.Trim();

            // Prefer profile EventMarkers (name + id).
            var profileClip = ResolveProfileDeathClip();
            if (profileClip != null)
            {
                profileClip.EnsureEventMarkers();
                if (profileClip.EventMarkers != null)
                {
                    for (int i = 0; i < profileClip.EventMarkers.Count; i++)
                    {
                        var marker = profileClip.EventMarkers[i];
                        if (marker == null || marker.FrameIndex != frame)
                            continue;
                        if (DeathEventId != 0 && marker.EventId == DeathEventId)
                            return true;
                        if (MarkerMatchesDeathName(marker, wantName))
                            return true;
                    }
                }

                if (DeathEventId != 0 &&
                    profileClip.EventIds != null &&
                    frame < profileClip.EventIds.Length &&
                    profileClip.EventIds[frame] == DeathEventId)
                    return true;
            }

            // Fallback: authoring clip EventIds.
            if (_set?.Clips != null &&
                DeadClipIndex >= 0 && DeadClipIndex < _set.Clips.Length)
            {
                var ids = _set.Clips[DeadClipIndex].EventIds;
                if (DeathEventId != 0 && ids != null && frame < ids.Length &&
                    ids[frame] == DeathEventId)
                    return true;
            }

            return false;
        }

        static bool MarkerMatchesDeathName(SpriteClipEventMarker marker, string wantName)
        {
            if (string.IsNullOrEmpty(wantName) || marker?.Payloads == null)
                return false;
            for (int i = 0; i < marker.Payloads.Count; i++)
            {
                var p = marker.Payloads[i];
                if (p != null && !string.IsNullOrEmpty(p.Name) &&
                    string.Equals(p.Name, wantName, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        SpriteClipDef ResolveProfileDeathClip()
        {
            var data = _set?.Profile?.Data;
            if (data?.Clips == null || DeadClipIndex < 0 || DeadClipIndex >= data.Clips.Count)
                return null;
            return data.Clips[DeadClipIndex];
        }

        /// <summary>
        /// Death clip finished (or Death frame event). Schedule respawn — do not Destroy.
        /// </summary>
        void OnDeathSequenceFinished()
        {
            if (_deathSequenceFinished || _lifeState != EnemyLifeState.Dying)
                return;

            _deathSequenceFinished = true;
            _lifeState = EnemyLifeState.WaitingRespawn;
            _respawnAt = Time.time + Mathf.Max(0f, RespawnDelay);
            DisableAllColliders();
        }

        void BeginRespawnSpawn()
        {
            _respawnAt = -1f;
            _lifeState = EnemyLifeState.Spawning;

            CurrentHealth = MaxHealth;
            _baseTint = _aliveTint;
            _flashUntil = 0f;
            _hurtPlaying = false;
            _deathSequenceFinished = false;
            _healthBarResolved = false;
            _healthBarSocketChild = FindHealthBarSocketChild();

            if (_set != null)
            {
                _set.Tint = _baseTint;
                _set.ShowSpriteInScene = true;
                _set.BakeUnityColliders = true;
                _set.BakeFrameColliders = true;
                _set.BakeUnitySockets = true;
                _set.SyncUnityColliders();
                _set.SyncUnitySockets();
            }

            if (_player == null || _set == null || _set.Clips == null ||
                DeadClipIndex < 0 || DeadClipIndex >= _set.Clips.Length)
            {
                OnSpawnSequenceFinished();
                return;
            }

            EnsureDeathClipPlaybackReverseOnce();
            _player.SetSpeed(1f);
            if (!_player.Play(DeadClipIndex, force: true))
            {
                OnSpawnSequenceFinished();
                return;
            }
            // Play() + ReverseOnce already seeks to last frame and rewinds to Complete.
        }

        void OnSpawnSequenceFinished()
        {
            if (_lifeState != EnemyLifeState.Spawning)
                return;

            if (_player != null)
            {
                _player.SetSpeed(1f);
                int idle = IdleClipIndex >= 0 ? IdleClipIndex : 0;
                _player.Play(idle, force: true);
            }

            _lifeState = EnemyLifeState.Alive;
            CurrentHealth = MaxHealth;
            _baseTint = _aliveTint;
            if (_set != null)
            {
                _set.Tint = _baseTint;
                _set.ShowSpriteInScene = true;
                _set.BakeUnityColliders = true;
                _set.BakeFrameColliders = true;
                _set.BakeUnitySockets = true;
                _set.SyncUnityColliders();
                _set.SyncUnitySockets();
            }

            _healthBarSocketChild = FindHealthBarSocketChild();
        }

        void DisableAllColliders()
        {
            var cols = GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    cols[i].enabled = false;
            }
        }

        /// <summary>
        /// Hurtbox check for player attack overlap. Any collider under this enemy counts
        /// (Character body is the intended target once BakeUnityColliders is on).
        /// </summary>
        public bool IsHurtbox(Collider2D col)
        {
            if (col == null || _lifeState != EnemyLifeState.Alive)
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
            if (_lifeState != EnemyLifeState.Alive)
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
