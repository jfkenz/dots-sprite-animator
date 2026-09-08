using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// GameObject animation authoring. Play(clip) drives the Quad preview in
    /// edit mode and Play mode, matching <see cref="SpriteAnims.Play"/>.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("DOTS Sprite Animator/SpriteAnimPlayerAuthoring")]
    [DisallowMultipleComponent]
    public class SpriteAnimPlayerAuthoring : MonoBehaviour
    {
#if UNITY_EDITOR
        void Reset() => SpriteAuthoringBundle.Ensure(gameObject);
#endif

        [Tooltip("Play this clip when the component enables.")]
        public bool PlayOnEnable = true;

        [Tooltip("Playback rate. Negative rewinds; 0 freezes the clock while Playing may stay true.")]
        public float Speed = 1f;

        public bool Playing = true;

        [Tooltip("Index into SpriteAnimSetAuthoring.Clips")]
        public int ClipIndex;

        [Header("Facing")]
        [Tooltip("Mirror this instance left-right. Does not change the sheet or clips.")]
        public bool FlipX;

        [Tooltip("Mirror this instance top-bottom. Does not change the sheet or clips.")]
        public bool FlipY;

        [Header("Playback follow-ups")]
        [Tooltip("Default crossfade seconds when Play(crossfadeSeconds:0). Blend goes 1→0; no dual draw.")]
        [Min(0f)]
        public float CrossfadeDuration;

        [Tooltip("1→0 during crossfade. Sample from gameplay / shaders.")]
        public float Blend;

        [HideInInspector] [SerializeField] float _time;
        [HideInInspector] [SerializeField] int _frame;
        [HideInInspector] [SerializeField] int _queuedClip = -1;
        [HideInInspector] [SerializeField] byte _queuedForce;
        [HideInInspector] [SerializeField] int _resumeClip = -1;
        [HideInInspector] [SerializeField] byte _oneShotActive;
        [HideInInspector] [SerializeField] float _blendOutTime;
        [HideInInspector] [SerializeField] float _blendDuration;
        [HideInInspector] [SerializeField] float _hitstopRemaining;
        [HideInInspector] [SerializeField] float _hitstopRestoreSpeed = 1f;
        [HideInInspector] [SerializeField] byte _hitstopActive;

        /// <summary>Clip index when Play begins (authoring preview / Play Mode GO path).</summary>
        public event Action<int> ClipStarted;

        /// <summary>Clip index when Once finishes (forward end or reverse-to-start). Not on Loop wraps.</summary>
        public event Action<int> ClipCompleted;

        public int Frame => _frame;
        public float Time => _time;
        public int QueuedClipIndex
        {
            get => _queuedClip;
            set => _queuedClip = value;
        }
        public byte QueuedForce
        {
            get => _queuedForce;
            set => _queuedForce = value;
        }
        public int ResumeClipIndex
        {
            get => _resumeClip;
            set => _resumeClip = value;
        }
        public byte OneShotActive
        {
            get => _oneShotActive;
            set => _oneShotActive = value;
        }
        public float BlendOutTime => _blendOutTime;

#if UNITY_EDITOR
        double _lastEditorTime;
#endif

        public bool Play(string clipName, bool force = false, float crossfadeSeconds = 0f)
        {
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set == null || set.Clips == null || string.IsNullOrWhiteSpace(clipName))
                return false;

            for (int i = 0; i < set.Clips.Length; i++)
            {
                if (set.Clips[i].Name == clipName)
                    return Play(i, force, crossfadeSeconds);
            }

            return false;
        }

        public bool Play(int clipIndex, bool force = false, float crossfadeSeconds = 0f)
        {
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set == null || set.Clips == null ||
                clipIndex < 0 || clipIndex >= set.Clips.Length)
                return false;

            if (!force)
            {
                if (!CanPlayByPriority(set, clipIndex))
                    return false;
                if (!CanInterruptCurrent(set))
                    return false;
            }

            if (_oneShotActive != 0)
            {
                _oneShotActive = 0;
                _resumeClip = -1;
            }

            ClipIndex = clipIndex;
            _time = 0f;
            _frame = 0;
            Playing = true;
            BeginCrossfade(crossfadeSeconds);
#if UNITY_EDITOR
            _lastEditorTime = EditorApplication.timeSinceStartup;
#endif
            // ReverseOnce: start at last frame (Play would otherwise reset to 0).
            var playDef = ToPreviewDef(set.Clips[clipIndex]);
            if (playDef.WrapMode == SpriteAnimWrap.ReverseOnce
                && playDef.Frames != null && playDef.Frames.Length > 0)
            {
                int last = playDef.Frames.Length - 1;
                _frame = last;
                _time = SpriteAnimPlayback.AuthoredStartTime(playDef, last)
                    + SpriteAnimPlayback.FrameDuration(playDef, last) * 0.999f;
            }
            SampleAndApply(set);
            ClipStarted?.Invoke(clipIndex);
            return true;
        }

        public bool PlayFacing(string facingGroup, SpriteFacingDirection facingDirection,
            bool force = false, float crossfadeSeconds = 0f)
        {
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set == null || set.Clips == null || string.IsNullOrWhiteSpace(facingGroup))
                return false;

            string group = facingGroup.Trim();
            int fallbackIndex = -1;
            for (int i = 0; i < set.Clips.Length; i++)
            {
                var clip = set.Clips[i];
                if (string.IsNullOrWhiteSpace(clip.FacingGroup))
                    continue;
                if (clip.FacingGroup.Trim() != group)
                    continue;
                if (fallbackIndex < 0)
                    fallbackIndex = i;
                if (clip.FacingDirection == facingDirection)
                    return Play(i, force, crossfadeSeconds);
            }

            return fallbackIndex >= 0 && Play(fallbackIndex, force, crossfadeSeconds);
        }

        public bool Queue(string clipName, bool force = true)
        {
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set == null || set.Clips == null || string.IsNullOrWhiteSpace(clipName))
                return false;
            for (int i = 0; i < set.Clips.Length; i++)
            {
                if (set.Clips[i].Name == clipName)
                    return Queue(i, force);
            }
            return false;
        }

        public bool Queue(int clipIndex, bool force = true)
        {
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set == null || set.Clips == null ||
                clipIndex < 0 || clipIndex >= set.Clips.Length)
                return false;
            if (!Playing)
                return Play(clipIndex, force: true);
            _queuedClip = clipIndex;
            _queuedForce = force ? (byte)1 : (byte)0;
            return true;
        }

        public bool PlayOrQueue(string clipName, bool force = false, bool queueIfBlocked = true,
            float crossfadeSeconds = 0f)
        {
            if (Play(clipName, force, crossfadeSeconds))
                return true;
            return queueIfBlocked && Queue(clipName, force: true);
        }

        public bool PlayOrQueue(int clipIndex, bool force = false, bool queueIfBlocked = true,
            float crossfadeSeconds = 0f)
        {
            if (Play(clipIndex, force, crossfadeSeconds))
                return true;
            return queueIfBlocked && Queue(clipIndex, force: true);
        }

        public bool PlayOneShot(string clipName)
        {
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set == null || set.Clips == null || string.IsNullOrWhiteSpace(clipName))
                return false;
            for (int i = 0; i < set.Clips.Length; i++)
            {
                if (set.Clips[i].Name == clipName)
                    return PlayOneShot(i);
            }
            return false;
        }

        public bool PlayOneShot(int clipIndex)
        {
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set == null || set.Clips == null ||
                clipIndex < 0 || clipIndex >= set.Clips.Length)
                return false;

            if (_oneShotActive == 0)
                _resumeClip = ClipIndex;
            _oneShotActive = 1;

            // Force play without clearing one-shot bookkeeping.
            byte keepOneShot = _oneShotActive;
            int keepResume = _resumeClip;
            bool ok = Play(clipIndex, force: true);
            _oneShotActive = keepOneShot;
            _resumeClip = keepResume;
            return ok;
        }

        /// <summary>
        /// Whether Play() may replace the current clip under its interrupt policy.
        /// <see cref="Stop"/> and force=true bypass this.
        /// </summary>
        public bool CanInterruptCurrent()
        {
            return CanInterruptCurrent(GetComponent<SpriteAnimSetAuthoring>());
        }

        bool CanInterruptCurrent(SpriteAnimSetAuthoring set)
        {
            if (!Playing || set?.Clips == null || set.Clips.Length == 0)
                return true;
            if (ClipIndex < 0 || ClipIndex >= set.Clips.Length)
                return true;

            var clip = set.Clips[ClipIndex];
            if (clip.ComboWindowEndFrame >= 0)
            {
                int start = clip.ComboWindowStartFrame;
                int end = clip.ComboWindowEndFrame;
                if (end < start) { int t = start; start = end; end = t; }
                if (_frame >= start && _frame <= end)
                    return true;
            }
            byte mode = clip.Interrupt;
            if (mode == (byte)SpriteClipInterrupt.Always)
                return true;
            if (mode == (byte)SpriteClipInterrupt.Never)
                return false;
            if (mode == (byte)SpriteClipInterrupt.AfterTime)
            {
                var def = ToPreviewDef(clip);
                float total = SpriteAnimPlayback.TotalAuthoredDuration(def);
                float normalized = total > 1e-6f ? Mathf.Clamp01(_time / total) : 1f;
                return normalized >= Mathf.Clamp01(clip.CancelAfter);
            }
            return true;
        }

        bool CanPlayByPriority(SpriteAnimSetAuthoring set, int targetIndex)
        {
            if (!Playing || set?.Clips == null || set.Clips.Length == 0)
                return true;
            if (ClipIndex < 0 || ClipIndex >= set.Clips.Length)
                return true;
            if (targetIndex < 0 || targetIndex >= set.Clips.Length)
                return true;
            int currentPriority = set.Clips[ClipIndex].Priority;
            var cur = set.Clips[ClipIndex];
            if (cur.ComboWindowEndFrame >= 0)
            {
                int start = cur.ComboWindowStartFrame;
                int end = cur.ComboWindowEndFrame;
                if (end < start) { int t = start; start = end; end = t; }
                if (_frame >= start && _frame <= end)
                    currentPriority -= cur.ComboWindowPriorityBoost;
            }
            return set.Clips[targetIndex].Priority >= currentPriority;
        }

        void BeginCrossfade(float crossfadeSeconds)
        {
            float fade = crossfadeSeconds > 0f ? crossfadeSeconds : CrossfadeDuration;
            if (fade > 0f)
            {
                _blendDuration = fade;
                _blendOutTime = fade;
                Blend = 1f;
            }
            else
            {
                _blendDuration = 0f;
                _blendOutTime = 0f;
                Blend = 0f;
            }
        }

        void TickBlend(float dt)
        {
            if (_blendOutTime <= 0f)
            {
                if (Blend != 0f)
                    Blend = 0f;
                return;
            }
            _blendOutTime = Mathf.Max(0f, _blendOutTime - Mathf.Max(0f, dt));
            Blend = _blendDuration > 1e-5f ? Mathf.Clamp01(_blendOutTime / _blendDuration) : 0f;
        }

        public void Pause()
        {
            Playing = false;
        }

        /// <summary>Playing = true. Does not restart a finished Once clip by itself.</summary>
        public void Resume()
        {
            Playing = true;
        }

        /// <summary>Same as Pause.</summary>
        public void Freeze() => Pause();

        /// <summary>Same as Resume.</summary>
        public void Unfreeze() => Resume();

        public void SetSpeed(float speed)
        {
            if (_hitstopActive != 0)
            {
                _hitstopRestoreSpeed = speed;
                Speed = 0f;
            }
            else
            {
                Speed = speed;
            }
        }

        public float GetSpeed() => Speed;

        /// <summary>Seek to frame. Clamps. Does not force Play.</summary>
        public void SeekFrame(int frame)
        {
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set == null || set.Clips == null ||
                ClipIndex < 0 || ClipIndex >= set.Clips.Length)
                return;
            var def = ToPreviewDef(set.Clips[ClipIndex]);
            def.EnsureFrameData();
            int n = def.Frames != null ? def.Frames.Length : 1;
            int clamped = Mathf.Clamp(frame, 0, Mathf.Max(0, n - 1));
            _time = SpriteAnimPlayback.AuthoredStartTime(def, clamped);
            _frame = clamped;
            if (set.ShowSpriteInScene)
            {
                set.ApplyQuadPreview(ClipIndex, _frame);
                set.SyncUnityColliders();
                set.SyncUnitySockets();
            }
        }

        /// <summary>Seek to normalized 0–1 authored progress. Does not force Play.</summary>
        public void SeekNormalized(float t01)
        {
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set == null || set.Clips == null ||
                ClipIndex < 0 || ClipIndex >= set.Clips.Length)
                return;
            var def = ToPreviewDef(set.Clips[ClipIndex]);
            float total = SpriteAnimPlayback.TotalAuthoredDuration(def);
            _time = Mathf.Clamp01(t01) * total;
            SampleAndApply(set);
        }

        /// <summary>
        /// Set phase in frames (mirrors <see cref="SpriteAnims.SetTime"/>).
        /// Converts to the authoring seconds clock via frame durations.
        /// </summary>
        public void SetTime(float phaseInFrames)
        {
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set == null || set.Clips == null ||
                ClipIndex < 0 || ClipIndex >= set.Clips.Length)
                return;
            var def = ToPreviewDef(set.Clips[ClipIndex]);
            def.EnsureFrameData();
            int n = def.Frames != null ? def.Frames.Length : 0;
            if (n <= 0)
                return;
            float phase = Mathf.Max(0f, phaseInFrames);
            if (def.WrapMode == SpriteAnimWrap.Once || def.WrapMode == SpriteAnimWrap.ReverseOnce)
                phase = Mathf.Min(phase, Mathf.Max(0, n - 1) + 0.999f);
            int step = Mathf.Clamp(Mathf.FloorToInt(phase), 0, n - 1);
            float frac = Mathf.Clamp01(phase - step);
            float start = SpriteAnimPlayback.AuthoredStartTime(def, step);
            float dur = SpriteAnimPlayback.FrameDuration(def, step);
            _time = start + frac * dur;
            SampleAndApply(set);
        }

        public void Stop()
        {
            Playing = false;
            _time = 0f;
            _frame = 0;
            _oneShotActive = 0;
            _resumeClip = -1;
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set != null)
            {
                set.ApplyQuadPreview(ClipIndex, _frame);
                set.SyncUnityColliders();
                set.SyncUnitySockets();
            }
        }

        // --- Hold / Hitstop (shared timer; simulation/authoring delta) ---

        /// <summary>Freeze Speed=0 for duration (authoring Tick delta), then restore. Same as Hold.</summary>
        public void Hitstop(float seconds) => Hold(seconds);

        /// <summary>Freeze clock for duration then restore. Shares timer with Hitstop.</summary>
        public void Hold(float seconds)
        {
            if (seconds <= 0f)
                return;
            if (_hitstopActive == 0)
            {
                _hitstopRestoreSpeed = Speed;
                _hitstopActive = 1;
                _hitstopRemaining = seconds;
            }
            else
            {
                _hitstopRemaining = Mathf.Max(_hitstopRemaining, seconds);
            }
            Speed = 0f;
        }

        /// <summary>SeekFrame then Hold.</summary>
        public void HoldAtFrame(int frame, float seconds)
        {
            SeekFrame(frame);
            Hold(seconds);
        }

        void TickHoldHitstop(float dt)
        {
            if (_hitstopActive == 0)
                return;
            _hitstopRemaining -= Mathf.Max(0f, dt);
            if (_hitstopRemaining <= 0f)
            {
                _hitstopRemaining = 0f;
                _hitstopActive = 0;
                Speed = _hitstopRestoreSpeed;
            }
            else
            {
                Speed = 0f;
            }
        }

        // --- Combo window ---

        public bool InComboWindow()
        {
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set?.Clips == null || set.Clips.Length == 0)
                return false;
            if (ClipIndex < 0 || ClipIndex >= set.Clips.Length)
                return false;
            var clip = set.Clips[ClipIndex];
            if (clip.ComboWindowEndFrame < 0)
                return false;
            int start = clip.ComboWindowStartFrame;
            int end = clip.ComboWindowEndFrame;
            if (end < start)
            {
                int tmp = start;
                start = end;
                end = tmp;
            }
            return _frame >= start && _frame <= end;
        }

        public bool TryComboPlay(string clipName, bool force = false, float crossfadeSeconds = 0f)
        {
            if (!InComboWindow())
                return false;
            return Play(clipName, force, crossfadeSeconds);
        }

        public bool TryComboPlay(int clipIndex, bool force = false, float crossfadeSeconds = 0f)
        {
            if (!InComboWindow())
                return false;
            return Play(clipIndex, force, crossfadeSeconds);
        }

        // --- Facing / mirror ---

        /// <summary>Set FlipX only (keeps FlipY). Mirror = flipX true.</summary>
        public void SetFacing(bool flipX) => SetFlip(flipX, FlipY);

        public bool Play(int clipIndex, bool force, float crossfadeSeconds, bool forceFlipX)
        {
            SetFacing(forceFlipX);
            return Play(clipIndex, force, crossfadeSeconds);
        }

        public bool Play(string clipName, bool force, float crossfadeSeconds, bool forceFlipX)
        {
            SetFacing(forceFlipX);
            return Play(clipName, force, crossfadeSeconds);
        }

        public bool PlayFacing(string facingGroup, SpriteFacingDirection facingDirection,
            bool flipX, bool force = false, float crossfadeSeconds = 0f)
        {
            SetFacing(flipX);
            return PlayFacing(facingGroup, facingDirection, force, crossfadeSeconds);
        }

        public bool PlayMirrored(int clipIndex, bool mirrored = true, bool force = false,
            float crossfadeSeconds = 0f)
            => Play(clipIndex, force, crossfadeSeconds, forceFlipX: mirrored);

        public bool PlayMirrored(string clipName, bool mirrored = true, bool force = false,
            float crossfadeSeconds = 0f)
            => Play(clipName, force, crossfadeSeconds, forceFlipX: mirrored);

        // --- Random / weighted ---

        public bool PlayRandomStart(int clipIndex, bool force = false, float crossfadeSeconds = 0f)
        {
            if (!Play(clipIndex, force, crossfadeSeconds))
                return false;
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set?.Clips == null || clipIndex < 0 || clipIndex >= set.Clips.Length)
                return true;
            var frames = set.Clips[clipIndex].Frames;
            int n = frames != null && frames.Length > 0 ? frames.Length : 1;
            if (n <= 1)
                return true;
            SeekFrame(UnityEngine.Random.Range(0, n));
            return true;
        }

        public bool PlayWeighted(int[] clips, float[] weights, bool force = false,
            float crossfadeSeconds = 0f)
        {
            if (clips == null || weights == null)
                return false;
            int count = Mathf.Min(clips.Length, weights.Length);
            if (count <= 0)
                return false;
            float total = 0f;
            for (int i = 0; i < count; i++)
                total += Mathf.Max(0f, weights[i]);
            int pick;
            if (total <= 1e-8f)
            {
                pick = UnityEngine.Random.Range(0, count);
            }
            else
            {
                float r = UnityEngine.Random.value * total;
                float acc = 0f;
                pick = count - 1;
                for (int i = 0; i < count; i++)
                {
                    acc += Mathf.Max(0f, weights[i]);
                    if (r <= acc)
                    {
                        pick = i;
                        break;
                    }
                }
            }
            return Play(clips[pick], force, crossfadeSeconds);
        }

                public void SetFlip(bool flipX, bool flipY)
        {
            FlipX = flipX;
            FlipY = flipY;
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set != null)
            {
                set.ApplyQuadPreview(ClipIndex, _frame);
                set.SyncUnityColliders();
                set.SyncUnitySockets();
            }
        }

        void OnValidate()
        {
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set != null && set.isActiveAndEnabled)
            {
                set.ApplyQuadPreview(ClipIndex, _frame);
#if UNITY_EDITOR
                set.ScheduleUnityColliderSync();
#else
                set.SyncUnityColliders();
                set.SyncUnitySockets();
#endif
            }
        }

        void OnEnable()
        {
            if (PlayOnEnable)
            {
                var set = GetComponent<SpriteAnimSetAuthoring>();
                int index = ClipIndex;
                if (set != null && set.Clips != null && set.Clips.Length > 0)
                {
                    if (index < 0 || index >= set.Clips.Length)
                        index = Mathf.Clamp(set.InitialClipIndex, 0, set.Clips.Length - 1);
                }

                Play(index);
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorApplication.update -= EditorTick;
                EditorApplication.update += EditorTick;
                _lastEditorTime = EditorApplication.timeSinceStartup;
            }
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= EditorTick;
#endif
        }

        void Update()
        {
            if (!Application.isPlaying)
                return;
            Tick(UnityEngine.Time.deltaTime);
        }

#if UNITY_EDITOR
        void EditorTick()
        {
            if (this == null || !isActiveAndEnabled || Application.isPlaying)
                return;

            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Min(0.1f, (float)(now - _lastEditorTime));
            _lastEditorTime = now;
            Tick(dt);
        }
#endif

        void Tick(float dt)
        {
            if (!isActiveAndEnabled)
                return;

            TickHoldHitstop(dt);
            TickBlend(dt);

            if (!Playing)
                return;

            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set == null || !set.ShowSpriteInScene)
                return;
            if (set.Clips == null || set.Clips.Length == 0)
                return;
            if (ClipIndex < 0 || ClipIndex >= set.Clips.Length)
                return;

            // Speed ~ 0 freezes the clock; negative rewinds.
            if (Mathf.Abs(Speed) > 1e-6f)
            {
                var def = ToPreviewDef(set.Clips[ClipIndex]);
                float total = SpriteAnimPlayback.TotalAuthoredDuration(def);
                byte wrap = def.WrapMode;
                float signedSpeed = Speed;
                // ReverseOnce plays backward with positive Speed (no Speed=-1 hack).
                if (wrap == SpriteAnimWrap.ReverseOnce)
                    signedSpeed = -Mathf.Abs(Speed);
                _time += dt * signedSpeed;

                if (wrap == SpriteAnimWrap.Once || wrap == SpriteAnimWrap.ReverseOnce)
                {
                    if (signedSpeed > 0f && _time >= total)
                    {
                        _time = total;
                        SampleAndApply(set);
                        return;
                    }
                    if (signedSpeed < 0f && _time <= 0f)
                    {
                        _time = 0f;
                        _frame = 0;
                        Playing = false;
                        set.ApplyQuadPreview(ClipIndex, _frame);
                        set.SyncUnityColliders();
                        set.SyncUnitySockets();
                        int finishing = ClipIndex;
                        ClipCompleted?.Invoke(finishing);
                        TryDrainCompletion(set);
                        return;
                    }
                }
                else if (wrap == SpriteAnimWrap.PingPong)
                {
                    float cycle = Mathf.Max(0.001f, total * 2f);
                    _time = Mathf.Repeat(_time, cycle);
                }
                else
                {
                    _time = Mathf.Repeat(_time, Mathf.Max(0.001f, total));
                }
            }

            SampleAndApply(set);
        }

        void SampleAndApply(SpriteAnimSetAuthoring set)
        {
            if (set == null || set.Clips == null)
                return;
            if (ClipIndex < 0 || ClipIndex >= set.Clips.Length)
                return;

            var def = ToPreviewDef(set.Clips[ClipIndex]);
            bool previewLoop = def.WrapMode == SpriteAnimWrap.Loop
                || def.WrapMode == SpriteAnimWrap.PingPong
                || def.WrapMode == SpriteAnimWrap.ReverseLoop;
            var sample = SpriteAnimPlayback.EvaluatePreview(def, _time, previewLoop);
            _frame = sample.Frame;
            if (sample.Ended)
            {
                Playing = false;
                set.ApplyQuadPreview(ClipIndex, _frame);
                set.SyncUnityColliders();
                set.SyncUnitySockets();
                int finishing = ClipIndex;
                ClipCompleted?.Invoke(finishing);
                TryDrainCompletion(set);
                return;
            }
            set.ApplyQuadPreview(ClipIndex, _frame);
            set.SyncUnityColliders();
            set.SyncUnitySockets();
        }

        void TryDrainCompletion(SpriteAnimSetAuthoring set)
        {
            if (set?.Clips == null || set.Clips.Length == 0)
                return;

            int next = -1;
            if (_oneShotActive != 0 && _resumeClip >= 0 && _resumeClip < set.Clips.Length)
            {
                next = _resumeClip;
                _oneShotActive = 0;
                _resumeClip = -1;
            }
            else if (_queuedClip >= 0 && _queuedClip < set.Clips.Length)
            {
                next = _queuedClip;
                _queuedClip = -1;
                _queuedForce = 0;
            }
            else
            {
                int onComplete = set.Clips[ClipIndex].OnCompleteClipIndex;
                if (onComplete >= 0 && onComplete < set.Clips.Length)
                    next = onComplete;
            }

            if (next >= 0)
                Play(next, force: true);
        }

        static SpriteClipDef ToPreviewDef(SpriteAnimSetAuthoring.ClipAuthoring clip)
        {
            var def = new SpriteClipDef
            {
                Name = clip.Name,
                Row = clip.Row,
                Frames = clip.Frames != null && clip.Frames.Length > 0
                    ? (int[])clip.Frames.Clone()
                    : new[] { 0 },
                FrameRows = clip.FrameRows != null && clip.FrameRows.Length > 0
                    ? (int[])clip.FrameRows.Clone()
                    : null,
                FrameRate = Mathf.Max(0.1f, clip.FrameRate),
                WrapMode = ResolveWrapMode(clip),
                FrameDurationScales = clip.FrameDurationScales != null
                    ? (float[])clip.FrameDurationScales.Clone()
                    : null,
                Priority = clip.Priority,
                OnCompleteClipIndex = clip.OnCompleteClipIndex,
            };
            def.EnsureFrameData();
            return def;
        }

        static byte ResolveWrapMode(SpriteAnimSetAuthoring.ClipAuthoring clip)
        {
            bool loop = clip.Loop || clip.WrapMode == SpriteAnimWrap.ReverseLoop;
            if (loop)
                return clip.WrapMode == SpriteAnimWrap.ReverseLoop
                    ? SpriteAnimWrap.ReverseLoop
                    : SpriteAnimWrap.Loop;
            if (clip.WrapMode == SpriteAnimWrap.Once
                || clip.WrapMode == SpriteAnimWrap.PingPong
                || clip.WrapMode == SpriteAnimWrap.ReverseOnce)
                return clip.WrapMode;
            return SpriteAnimWrap.Once;
        }
    }

#if UNITY_EDITOR
    static class SpriteAuthoringBundle
    {
        static bool _adding;

        public static void Ensure(GameObject gameObject)
        {
            if (_adding || gameObject == null || Application.isPlaying)
                return;

            // static-sprite context: keep only the sort authoring — dragging
            // the animation stack (Set + Player) onto a static prop is never
            // intended. (During RequireComponent races the static authoring
            // may briefly be absent; mutual exclusion cleans that up after.)
            if (gameObject.GetComponent<SpriteStaticAuthoring>() != null)
            {
                AddIfMissing<SpriteSortAuthoring>(gameObject);
                return;
            }

            _adding = true;
            try
            {
                AddIfMissing<SpriteAnimSetAuthoring>(gameObject);
                AddIfMissing<SpriteAnimPlayerAuthoring>(gameObject);
                AddIfMissing<SpriteSortAuthoring>(gameObject);
                AddIfMissing<SpriteColliderAuthoring>(gameObject);
            }
            finally
            {
                _adding = false;
            }
        }

        static void AddIfMissing<T>(GameObject gameObject) where T : Component
        {
            if (gameObject.GetComponent<T>() == null)
                Undo.AddComponent<T>(gameObject);
        }
    }
#endif
}
