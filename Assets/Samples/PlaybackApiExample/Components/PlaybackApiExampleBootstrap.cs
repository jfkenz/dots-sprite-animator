using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Wires the Playback API sample character to Idle / Walk / Attack clips on the
    /// Warrior Showcase profile. Clones the profile at runtime so Showcase assets
    /// stay untouched, then sets Priority / Interrupt / OnComplete for demos.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteAnimSetAuthoring))]
    [RequireComponent(typeof(SpriteAnimPlayerAuthoring))]
    public sealed class PlaybackApiExampleBootstrap : MonoBehaviour
    {
        [Tooltip("Preferred Showcase profile (Warrior). Builder assigns when present.")]
        public ScriptableSpriteSheetProfile PreferredProfile;

        [Min(0)] public int IdleClipIndex = 0;
        [Min(0)] public int WalkClipIndex = 1;
        [Min(0)] public int AttackClipIndex = 13;

        [Tooltip("Baseline priority for Idle / Walk (low).")]
        public int LocomotionPriority = 0;

        [Tooltip("Attack priority used by the Priority interrupt demo (key 4).")]
        public int AttackPriority = 10;

        ScriptableSpriteSheetProfile _runtimeProfile;
        SpriteAnimSetAuthoring _set;

        void Awake()
        {
            DestroySpriteStatsHud();
            _set = GetComponent<SpriteAnimSetAuthoring>();
            if (_set == null)
                return;

            EnsureProfile();
            ResolveClipIndices();
            ConfigureClipPolicies();
        }

        void Start() => DestroySpriteStatsHud();

        void EnsureProfile()
        {
            if (_set.Profile == null && PreferredProfile != null)
                _set.Profile = PreferredProfile;

            if (_set.Profile == null)
            {
                Debug.LogWarning(
                    "[PlaybackApiExample] No SpriteAnimSetAuthoring.Profile. Assign Warrior showcase profile.");
                return;
            }

            // Clone so Priority / Interrupt / Wrap tweaks never dirty Showcase assets.
            _runtimeProfile = Instantiate(_set.Profile);
            _runtimeProfile.name = _set.Profile.name + " (Playback API Sample Runtime)";
            _set.Profile = _runtimeProfile;
            _set.ApplyFromProfile();
            _set.ShowSpriteInScene = true;
        }

        void ResolveClipIndices()
        {
            var data = _set.Profile?.Data;
            if (data?.Clips == null || data.Clips.Count == 0)
                return;

            int idle = FindClipIndex(data, "idle", "row 1");
            int walk = FindClipIndex(data, "walk", "run", "row 2");
            int attack = FindClipIndex(data, "attack", "hurt", "row 14");

            if (idle >= 0) IdleClipIndex = idle;
            if (walk >= 0) WalkClipIndex = walk;
            if (attack >= 0) AttackClipIndex = attack;

            IdleClipIndex = Mathf.Clamp(IdleClipIndex, 0, data.Clips.Count - 1);
            WalkClipIndex = Mathf.Clamp(WalkClipIndex, 0, data.Clips.Count - 1);
            AttackClipIndex = Mathf.Clamp(AttackClipIndex, 0, data.Clips.Count - 1);
        }

        void ConfigureClipPolicies()
        {
            if (_set?.Clips == null || _set.Clips.Length == 0)
                return;

            ApplyLocomotion(IdleClipIndex, loop: true);
            ApplyLocomotion(WalkClipIndex, loop: true);
            ApplyAttack(AttackClipIndex);
        }

        void ApplyLocomotion(int index, bool loop)
        {
            if (index < 0 || index >= _set.Clips.Length)
                return;
            var clip = _set.Clips[index];
            clip.Loop = loop;
            clip.WrapMode = loop ? SpriteAnimWrap.Loop : SpriteAnimWrap.Once;
            clip.Priority = LocomotionPriority;
            clip.Interrupt = (byte)SpriteClipInterrupt.Always;
            clip.OnCompleteClipIndex = -1;
            _set.Clips[index] = clip;
        }

        void ApplyAttack(int index)
        {
            if (index < 0 || index >= _set.Clips.Length)
                return;
            var clip = _set.Clips[index];
            clip.Loop = false;
            clip.WrapMode = SpriteAnimWrap.Once;
            clip.Priority = AttackPriority;
            // Locked cast so low-priority Play cannot cancel mid-attack (force / higher prio still can).
            clip.Interrupt = (byte)SpriteClipInterrupt.Never;
            clip.OnCompleteClipIndex = IdleClipIndex;
            _set.Clips[index] = clip;
        }

        /// <summary>Temporarily make Walk Once + non-interruptible so Queue can drain.</summary>
        public void PrepareWalkForQueueDemo()
        {
            if (_set?.Clips == null || WalkClipIndex < 0 || WalkClipIndex >= _set.Clips.Length)
                return;
            var clip = _set.Clips[WalkClipIndex];
            clip.Loop = false;
            clip.WrapMode = SpriteAnimWrap.Once;
            clip.Interrupt = (byte)SpriteClipInterrupt.Never;
            clip.Priority = Mathf.Max(LocomotionPriority, AttackPriority);
            clip.OnCompleteClipIndex = IdleClipIndex;
            _set.Clips[WalkClipIndex] = clip;
        }

        /// <summary>Restore Walk looping / Always after a Queue demo finishes.</summary>
        public void RestoreWalkLocomotion()
        {
            ApplyLocomotion(WalkClipIndex, loop: true);
        }

        public string ClipName(int index)
        {
            if (_set?.Clips == null || index < 0 || index >= _set.Clips.Length)
                return "(none)";
            string name = _set.Clips[index].Name;
            return string.IsNullOrEmpty(name) ? $"Clip {index}" : name;
        }

        static int FindClipIndex(SpriteSheetProfile data, params string[] needles)
        {
            for (int n = 0; n < needles.Length; n++)
            {
                string needle = needles[n];
                for (int i = 0; i < data.Clips.Count; i++)
                {
                    string name = data.Clips[i]?.Name;
                    if (string.IsNullOrEmpty(name))
                        continue;
                    if (NameMatches(name, needle))
                        return i;
                }
            }
            return -1;
        }

        static bool NameMatches(string name, string needle)
        {
            if (string.IsNullOrEmpty(needle))
                return false;
            if (needle.StartsWith("row ", System.StringComparison.OrdinalIgnoreCase))
            {
                return name.EndsWith(needle, System.StringComparison.OrdinalIgnoreCase)
                    || name.IndexOf(" " + needle, System.StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("-" + needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return name.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void DestroySpriteStatsHud()
        {
            if (SpriteStatsHud.Instance != null)
            {
                Destroy(SpriteStatsHud.Instance.gameObject);
                SpriteStatsHud.Instance = null;
            }

            var named = GameObject.Find("SpriteStatsHud");
            if (named != null)
                Destroy(named);
        }
    }
}