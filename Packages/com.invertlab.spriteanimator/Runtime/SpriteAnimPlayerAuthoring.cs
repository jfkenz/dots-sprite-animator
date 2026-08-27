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
    [AddComponentMenu("DOTS Sprite Animator/Sprite Anim Player")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteAnimSetAuthoring))]
    public class SpriteAnimPlayerAuthoring : MonoBehaviour
    {
        [Tooltip("Play this clip when the component enables.")]
        public bool PlayOnEnable = true;

        [Min(0.01f)]
        public float Speed = 1f;

        public bool Playing = true;

        [Tooltip("Index into SpriteAnimSetAuthoring.Clips")]
        public int ClipIndex;

        [HideInInspector] [SerializeField] float _time;
        [HideInInspector] [SerializeField] int _frame;

        public int Frame => _frame;
        public float Time => _time;

#if UNITY_EDITOR
        double _lastEditorTime;
#endif

        public bool Play(string clipName)
        {
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set == null || set.Clips == null || string.IsNullOrWhiteSpace(clipName))
                return false;

            for (int i = 0; i < set.Clips.Length; i++)
            {
                if (set.Clips[i].Name == clipName)
                    return Play(i);
            }

            return false;
        }

        public bool Play(int clipIndex)
        {
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set == null || set.Clips == null ||
                clipIndex < 0 || clipIndex >= set.Clips.Length)
                return false;

            ClipIndex = clipIndex;
            _time = 0f;
            _frame = 0;
            Playing = true;
#if UNITY_EDITOR
            _lastEditorTime = EditorApplication.timeSinceStartup;
#endif
            SampleAndApply(set);
            return true;
        }

        public bool PlayFacing(string facingGroup, SpriteFacingDirection facingDirection)
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
                    return Play(i);
            }

            return fallbackIndex >= 0 && Play(fallbackIndex);
        }

        public void Pause()
        {
            Playing = false;
        }

        public void Stop()
        {
            Playing = false;
            _time = 0f;
            _frame = 0;
            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set != null)
                set.ApplyQuadPreview(ClipIndex, _frame);
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
            if (!isActiveAndEnabled || !Playing)
                return;

            var set = GetComponent<SpriteAnimSetAuthoring>();
            if (set == null || !set.ShowScenePreview)
                return;
            if (set.Clips == null || set.Clips.Length == 0)
                return;
            if (ClipIndex < 0 || ClipIndex >= set.Clips.Length)
                return;

            _time += dt * Mathf.Max(0.01f, Speed);
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
            if (sample.Ended)
                Playing = false;
            _frame = sample.Frame;
            set.ApplyQuadPreview(ClipIndex, _frame);
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
                FrameRate = Mathf.Max(0.1f, clip.FrameRate),
                WrapMode = ResolveWrapMode(clip),
                FrameDurationScales = clip.FrameDurationScales != null
                    ? (float[])clip.FrameDurationScales.Clone()
                    : null,
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
            if (clip.WrapMode == SpriteAnimWrap.Once || clip.WrapMode == SpriteAnimWrap.PingPong)
                return clip.WrapMode;
            return SpriteAnimWrap.Once;
        }
    }
}
