using UnityEngine;
using UnityEngine.InputSystem;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Playback API sample driver. Keys 1–6 demo Play, PlayOneShot, Queue / PlayOrQueue,
    /// Priority interrupt, Hitstop, and Hold on SpriteAnimPlayerAuthoring.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteAnimPlayerAuthoring))]
    [RequireComponent(typeof(SpriteAnimSetAuthoring))]
    public sealed class PlaybackApiExampleDriver : MonoBehaviour
    {
        [Min(0)] public int IdleClipIndex = 0;
        [Min(0)] public int WalkClipIndex = 1;
        [Min(0)] public int AttackClipIndex = 13;

        [Min(0.05f)] public float HitstopSeconds = 0.25f;
        [Min(0.05f)] public float HoldSeconds = 0.5f;

        public bool ShowHelpOverlay = true;

        SpriteAnimPlayerAuthoring _player;
        SpriteAnimSetAuthoring _set;
        PlaybackApiExampleBootstrap _bootstrap;
        string _lastAction = "Idle baseline";
        GUIStyle _helpStyle;
        bool _queueDemoActive;

        void Awake()
        {
            _player = GetComponent<SpriteAnimPlayerAuthoring>();
            _set = GetComponent<SpriteAnimSetAuthoring>();
            _bootstrap = GetComponent<PlaybackApiExampleBootstrap>();
        }

        void Start()
        {
            SyncIndicesFromBootstrap();
            if (_player != null)
                _player.Play(IdleClipIndex, force: true);
            _lastAction = "Start → Play(Idle)";
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
            MaybeFinishQueueDemo();

            if (WasPressed(KeyCode.Alpha1) || WasPressed(KeyCode.Keypad1))
            {
                RestoreWalkIfNeeded();
                bool ok = _player.Play(WalkClipIndex, force: true);
                _lastAction = ok
                    ? $"1 Play(Walk[{WalkClipIndex}]) force"
                    : "1 Play(Walk) FAILED";
            }
            else if (WasPressed(KeyCode.Alpha2) || WasPressed(KeyCode.Keypad2))
            {
                RestoreWalkIfNeeded();
                // Ensure Idle is current resume target, then one-shot Attack.
                if (_player.ClipIndex != IdleClipIndex || !_player.Playing)
                    _player.Play(IdleClipIndex, force: true);
                bool ok = _player.PlayOneShot(AttackClipIndex);
                _lastAction = ok
                    ? $"2 PlayOneShot(Attack[{AttackClipIndex}]) → resume Idle"
                    : "2 PlayOneShot(Attack) FAILED";
            }
            else if (WasPressed(KeyCode.Alpha3) || WasPressed(KeyCode.Keypad3))
            {
                // Walk Once + Interrupt Never so PlayOrQueue queues Attack until Walk ends.
                _bootstrap?.PrepareWalkForQueueDemo();
                _queueDemoActive = true;
                _player.Play(WalkClipIndex, force: true);
                bool ok = _player.PlayOrQueue(AttackClipIndex, force: false, queueIfBlocked: true);
                int queued = _player.QueuedClipIndex;
                _lastAction = ok
                    ? $"3 PlayOrQueue(Attack) while Walk — queued={queued}"
                    : "3 PlayOrQueue(Attack) FAILED";
            }
            else if (WasPressed(KeyCode.Alpha4) || WasPressed(KeyCode.Keypad4))
            {
                RestoreWalkIfNeeded();
                // Low-priority Walk, then Attack (higher Priority) without force.
                _player.Play(WalkClipIndex, force: true);
                bool ok = _player.Play(AttackClipIndex, force: false);
                _lastAction = ok
                    ? $"4 Priority Play(Attack[{AttackClipIndex}]) interrupted Walk"
                    : "4 Priority Play(Attack) BLOCKED (unexpected)";
            }
            else if (WasPressed(KeyCode.Alpha5) || WasPressed(KeyCode.Keypad5))
            {
                RestoreWalkIfNeeded();
                if (_player.ClipIndex != AttackClipIndex || !_player.Playing)
                    _player.Play(AttackClipIndex, force: true);
                _player.Hitstop(HitstopSeconds);
                _lastAction = $"5 Hitstop({HitstopSeconds:0.##}s) during Attack";
            }
            else if (WasPressed(KeyCode.Alpha6) || WasPressed(KeyCode.Keypad6))
            {
                _player.Hold(HoldSeconds);
                _lastAction = $"6 Hold({HoldSeconds:0.##}s) freeze";
            }
            else if (WasPressed(KeyCode.Alpha0) || WasPressed(KeyCode.Keypad0))
            {
                RestoreWalkIfNeeded();
                _player.Play(IdleClipIndex, force: true);
                _lastAction = "0 Play(Idle) reset";
            }
        }

        void MaybeFinishQueueDemo()
        {
            if (!_queueDemoActive)
                return;
            // After Walk Once ends, queue drains to Attack; restore Walk once Attack starts or Idle returns.
            if (_player.ClipIndex == AttackClipIndex || _player.ClipIndex == IdleClipIndex)
            {
                if (_player.QueuedClipIndex < 0)
                {
                    RestoreWalkIfNeeded();
                }
            }
        }

        void RestoreWalkIfNeeded()
        {
            if (!_queueDemoActive)
                return;
            _bootstrap?.RestoreWalkLocomotion();
            _queueDemoActive = false;
        }

        void OnGUI()
        {
            if (!ShowHelpOverlay)
                return;

            _helpStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 13,
                normal = { textColor = Color.white },
                padding = new RectOffset(12, 12, 8, 8),
            };

            string clipName = _bootstrap != null
                ? _bootstrap.ClipName(_player != null ? _player.ClipIndex : -1)
                : ClipNameFallback(_player != null ? _player.ClipIndex : -1);
            int clip = _player != null ? _player.ClipIndex : -1;
            int queued = _player != null ? _player.QueuedClipIndex : -1;
            bool playing = _player != null && _player.Playing;
            byte oneShot = _player != null ? _player.OneShotActive : (byte)0;

            GUI.Box(
                new Rect(16f, 12f, 520f, 168f),
                "Playback APIs\n" +
                "1 Play Walk · 2 PlayOneShot Attack→Idle · 3 Queue Attack on Walk\n" +
                "4 Priority Attack interrupts Walk · 5 Hitstop · 6 Hold · 0 Idle reset\n" +
                $"Last: {_lastAction}\n" +
                $"Clip: [{clip}] {clipName}  playing={playing}  queued={queued}  oneShot={oneShot}",
                _helpStyle);
        }

        string ClipNameFallback(int index)
        {
            if (_set?.Clips == null || index < 0 || index >= _set.Clips.Length)
                return "(none)";
            string name = _set.Clips[index].Name;
            return string.IsNullOrEmpty(name) ? $"Clip {index}" : name;
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
                KeyCode.Alpha0 => keyboard.digit0Key,
                KeyCode.Alpha1 => keyboard.digit1Key,
                KeyCode.Alpha2 => keyboard.digit2Key,
                KeyCode.Alpha3 => keyboard.digit3Key,
                KeyCode.Alpha4 => keyboard.digit4Key,
                KeyCode.Alpha5 => keyboard.digit5Key,
                KeyCode.Alpha6 => keyboard.digit6Key,
                KeyCode.Keypad0 => keyboard.numpad0Key,
                KeyCode.Keypad1 => keyboard.numpad1Key,
                KeyCode.Keypad2 => keyboard.numpad2Key,
                KeyCode.Keypad3 => keyboard.numpad3Key,
                KeyCode.Keypad4 => keyboard.numpad4Key,
                KeyCode.Keypad5 => keyboard.numpad5Key,
                KeyCode.Keypad6 => keyboard.numpad6Key,
                _ => null,
            };
        }
    }
}