using System.Text;
using Unity.Entities;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Sample consumer: Footstep / Attack markers plus ClipStarted / ClipCompleted.
    /// Listens via SpriteAnimPlayerAuthoring callbacks, SpriteAnimEvents.Raised (ECS),
    /// and GO frame-crossing against EventIds when baking is not present.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteAnimPlayerAuthoring))]
    [RequireComponent(typeof(SpriteAnimSetAuthoring))]
    public sealed class EventExampleListener : MonoBehaviour
    {
        public bool LogToConsole = true;
        public bool ShowOverlay = true;

        SpriteAnimPlayerAuthoring _player;
        SpriteAnimSetAuthoring _set;
        EventExampleBootstrap _bootstrap;

        int _lastClip = -1;
        int _lastFrame = -1;
        string _lastFrameEvent = "—";
        string _lastClipEvent = "—";
        string _lastSource = "—";
        float _lastEventTime = -999f;
        long _lastHandledKey = long.MinValue;
        readonly StringBuilder _overlay = new(256);
        GUIStyle _boxStyle;

        // Optional SFX / VFX hooks — wire AudioSource / ParticleSystem in the inspector.
        // public AudioSource FootstepSfx;
        // public ParticleSystem FootstepDust;

        void Awake()
        {
            _player = GetComponent<SpriteAnimPlayerAuthoring>();
            _set = GetComponent<SpriteAnimSetAuthoring>();
            _bootstrap = GetComponent<EventExampleBootstrap>();
        }

        void OnEnable()
        {
            if (_player != null)
            {
                _player.ClipStarted += OnClipStarted;
                _player.ClipCompleted += OnClipCompleted;
            }
            SpriteAnimEvents.Raised += OnAnimEventRaised;
            SpriteAnimEvents.ClipStarted += OnEcsClipStarted;
            SpriteAnimEvents.ClipCompleted += OnEcsClipCompleted;
        }

        void OnDisable()
        {
            if (_player != null)
            {
                _player.ClipStarted -= OnClipStarted;
                _player.ClipCompleted -= OnClipCompleted;
            }
            SpriteAnimEvents.Raised -= OnAnimEventRaised;
            SpriteAnimEvents.ClipStarted -= OnEcsClipStarted;
            SpriteAnimEvents.ClipCompleted -= OnEcsClipCompleted;
        }

        void LateUpdate()
        {
            // GO path: detect EventIds[] crossings when no ECS baker is driving Raised.
            if (_player == null || !_player.Playing || _set?.Clips == null)
                return;

            int clip = _player.ClipIndex;
            int frame = _player.Frame;
            if (clip == _lastClip && frame == _lastFrame)
                return;

            bool clipChanged = clip != _lastClip;
            int previousFrame = _lastFrame;
            _lastClip = clip;
            _lastFrame = frame;

            if (clip < 0 || clip >= _set.Clips.Length)
                return;

            var eventIds = _set.Clips[clip].EventIds;
            if (eventIds == null || eventIds.Length == 0)
                return;

            if (clipChanged)
            {
                // Fire markers at/near frame 0 after a Play() reset.
                if (frame >= 0 && frame < eventIds.Length && eventIds[frame] != 0)
                    HandleFrameEvent(eventIds[frame], clip, frame, "GO EventIds");
                return;
            }

            if (frame < 0)
                return;

            // Advance forward across any skipped frames in this tick.
            int from = Mathf.Max(0, previousFrame + 1);
            int to = frame;
            if (to < from)
            {
                // Loop wrap: drain end, then start..to
                for (int f = from; f < eventIds.Length; f++)
                {
                    if (eventIds[f] != 0)
                        HandleFrameEvent(eventIds[f], clip, f, "GO EventIds");
                }
                from = 0;
            }

            for (int f = from; f <= to && f < eventIds.Length; f++)
            {
                if (eventIds[f] != 0)
                    HandleFrameEvent(eventIds[f], clip, f, "GO EventIds");
            }
        }

        void OnClipStarted(int clipIndex) =>
            HandleClipLifecycle("ClipStarted", clipIndex, "PlayerAuthoring");

        void OnClipCompleted(int clipIndex) =>
            HandleClipLifecycle("ClipCompleted", clipIndex, "PlayerAuthoring");

        void OnEcsClipStarted(Entity entity, int clipIndex) =>
            HandleClipLifecycle("ClipStarted", clipIndex, "SpriteAnimEvents");

        void OnEcsClipCompleted(Entity entity, int clipIndex) =>
            HandleClipLifecycle("ClipCompleted", clipIndex, "SpriteAnimEvents");

        void OnAnimEventRaised(Entity entity, SpriteAnimEventBuffer evt)
        {
            if (evt.Id == SpriteAnimLifecycleId.Start || evt.Id == SpriteAnimLifecycleId.Complete)
                return; // surfaced via ClipStarted / ClipCompleted
            HandleFrameEvent(evt.Id, evt.ClipIndex, evt.FrameIndex, "SpriteAnimEvents.Raised");
        }

        void HandleClipLifecycle(string label, int clipIndex, string source)
        {
            string clipName = ClipName(clipIndex);
            _lastClipEvent = $"{label}  clip={clipIndex} ({clipName})";
            _lastSource = source;
            _lastEventTime = Time.unscaledTime;
            if (LogToConsole)
                Debug.Log($"[EventsExample] {label} clip={clipIndex} ({clipName}) via {source}", this);
        }

        void HandleFrameEvent(byte id, int clipIndex, int frameIndex, string source)
        {
            // Collapse GO EventIds + SpriteAnimEvents.Raised doubles within the same tick.
            long key = ((long)id << 48) | ((long)(clipIndex & 0xffff) << 32) | (uint)frameIndex;
            if (key == _lastHandledKey && Time.unscaledTime - _lastEventTime < 0.05f)
                return;
            _lastHandledKey = key;

            string name = ResolveName(id);
            _lastFrameEvent = $"{name} (id={id})  clip={clipIndex} frame={frameIndex}";
            _lastSource = source;
            _lastEventTime = Time.unscaledTime;

            if (id == EventExampleBootstrap.FootstepId)
            {
                if (LogToConsole)
                    Debug.Log($"[EventsExample] Footstep clip={clipIndex} frame={frameIndex} via {source}", this);
                // Optional: FootstepSfx?.PlayOneShot(...); FootstepDust?.Play();
            }
            else if (id == EventExampleBootstrap.AttackId)
            {
                if (LogToConsole)
                    Debug.Log($"[EventsExample] Attack clip={clipIndex} frame={frameIndex} via {source}", this);
            }
            else if (LogToConsole)
            {
                Debug.Log($"[EventsExample] {name} id={id} clip={clipIndex} frame={frameIndex} via {source}", this);
            }
        }

        string ResolveName(byte id)
        {
            if (_bootstrap != null)
                return _bootstrap.ResolveEventName(id);
            return id switch
            {
                EventExampleBootstrap.FootstepId => "Footstep",
                EventExampleBootstrap.AttackId => "Attack",
                _ => "Event " + id,
            };
        }

        string ClipName(int clipIndex)
        {
            if (_set?.Clips != null && clipIndex >= 0 && clipIndex < _set.Clips.Length)
            {
                string name = _set.Clips[clipIndex].Name;
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            return "?";
        }

        void OnGUI()
        {
            if (!ShowOverlay)
                return;

            _boxStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 15,
                normal = { textColor = Color.white },
                padding = new RectOffset(14, 14, 10, 10),
            };

            float age = Time.unscaledTime - _lastEventTime;
            string freshness = age < 1.25f ? "●" : "○";

            _overlay.Clear();
            _overlay.Append("DOTS Sprite Animator — Events\n");
            _overlay.Append("Author: Footstep (id 1) on Walk · Attack (id 2) on Attack\n");
            _overlay.Append(freshness).Append(" Frame: ").Append(_lastFrameEvent).Append('\n');
            _overlay.Append("Clip: ").Append(_lastClipEvent).Append('\n');
            _overlay.Append("Source: ").Append(_lastSource);

            GUI.Box(new Rect(16f, 16f, 520f, 110f), _overlay.ToString(), _boxStyle);
        }
    }
}
