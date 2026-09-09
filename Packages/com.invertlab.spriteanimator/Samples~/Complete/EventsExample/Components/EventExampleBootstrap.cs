using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Wires the Events sample character to a profile that defines Footstep / Attack
    /// event types, and injects demo markers on Walk / Attack clips when missing.
    /// Mutates a runtime profile clone so Showcase assets stay untouched.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteAnimSetAuthoring))]
    [RequireComponent(typeof(SpriteAnimPlayerAuthoring))]
    public sealed class EventExampleBootstrap : MonoBehaviour
    {
        public const byte FootstepId = 1;
        public const byte AttackId = 2;

        [Tooltip("Preferred Showcase profile (Warrior). Builder assigns EventsExampleProfile when present.")]
        public ScriptableSpriteSheetProfile PreferredProfile;

        [Min(0)] public int IdleClipIndex = 0;
        [Min(0)] public int WalkClipIndex = 1;
        [Min(0)] public int AttackClipIndex = 13;

        [Tooltip("Play-order frames on the Walk clip that fire Footstep.")]
        public int[] WalkFootstepFrames = { 1, 4 };

        [Tooltip("Play-order frames on the Attack clip that fire Attack.")]
        public int[] AttackHitFrames = { 2 };

        ScriptableSpriteSheetProfile _runtimeProfile;
        SpriteAnimSetAuthoring _set;

        void Awake()
        {
            DestroySpriteStatsHud();
            _set = GetComponent<SpriteAnimSetAuthoring>();
            if (_set == null)
                return;

            EnsureProfile();
            EnsureEventTypes();
            EnsureDemoMarkers();
            ApplyAuthoringEventIds();
            ResolveClipIndices();
        }

        void Start() => DestroySpriteStatsHud();

        void EnsureProfile()
        {
            if (_set.Profile == null && PreferredProfile != null)
                _set.Profile = PreferredProfile;

            if (_set.Profile == null)
            {
                Debug.LogWarning(
                    "[EventsExample] No SpriteAnimSetAuthoring.Profile. Assign Warrior / EventsExampleProfile.");
                return;
            }

            // Clone so marker injection never dirties Showcase assets.
            _runtimeProfile = Instantiate(_set.Profile);
            _runtimeProfile.name = _set.Profile.name + " (Events Sample Runtime)";
            _set.Profile = _runtimeProfile;
            _set.ApplyFromProfile();
            _set.ShowSpriteInScene = true;
        }

        void EnsureEventTypes()
        {
            var data = _set.Profile?.Data;
            if (data == null)
                return;

            data.Events ??= new List<SpriteEventDef>();
            EnsureEventDef(data.Events, FootstepId, "Footstep", new Color(0.35f, 0.85f, 1f, 1f));
            EnsureEventDef(data.Events, AttackId, "Attack", new Color(1f, 0.35f, 0.3f, 1f));
        }

        void EnsureDemoMarkers()
        {
            var data = _set.Profile?.Data;
            if (data?.Clips == null || data.Clips.Count == 0)
                return;

            ResolveClipIndices();
            if (WalkClipIndex >= 0 && WalkClipIndex < data.Clips.Count)
                InjectMarkers(data.Clips[WalkClipIndex], FootstepId, WalkFootstepFrames);
            if (AttackClipIndex >= 0 && AttackClipIndex < data.Clips.Count)
                InjectMarkers(data.Clips[AttackClipIndex], AttackId, AttackHitFrames);
        }

        void ApplyAuthoringEventIds()
        {
            var data = _set.Profile?.Data;
            if (data?.Clips == null || _set.Clips == null)
                return;

            // Re-copy so ClipAuthoring.EventIds mirrors markers for GO frame listening.
            _set.ApplyFromProfile();
            for (int i = 0; i < _set.Clips.Length && i < data.Clips.Count; i++)
            {
                var profileClip = data.Clips[i];
                profileClip.EnsureEventMarkers();
                profileClip.SyncLegacyEventsFromMarkers();
                var author = _set.Clips[i];
                author.EventIds = CopyBytes(profileClip.EventIds);
                author.EventNormalizedTimes = CopyFloats(profileClip.EventNormalizedTimes);
                _set.Clips[i] = author;
            }
        }

        void ResolveClipIndices()
        {
            var data = _set.Profile?.Data;
            if (data?.Clips == null || data.Clips.Count == 0)
                return;

            int idle = FindClipIndex(data, "idle", "row 1");
            int walk = FindClipIndex(data, "walk", "run", "row 2");
            int attack = FindClipIndex(data, "attack", "row 14");

            if (idle >= 0) IdleClipIndex = idle;
            if (walk >= 0) WalkClipIndex = walk;
            if (attack >= 0) AttackClipIndex = attack;

            IdleClipIndex = Mathf.Clamp(IdleClipIndex, 0, data.Clips.Count - 1);
            WalkClipIndex = Mathf.Clamp(WalkClipIndex, 0, data.Clips.Count - 1);
            AttackClipIndex = Mathf.Clamp(AttackClipIndex, 0, data.Clips.Count - 1);
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
            // Exact row token: "row 2" must not match "row 20" / "row 12".
            if (needle.StartsWith("row ", System.StringComparison.OrdinalIgnoreCase))
            {
                return name.EndsWith(needle, System.StringComparison.OrdinalIgnoreCase)
                    || name.IndexOf(" " + needle, System.StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("-" + needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return name.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void InjectMarkers(SpriteClipDef clip, byte eventId, int[] frames)
        {
            if (clip == null || frames == null || frames.Length == 0)
                return;

            clip.EnsureFrameData();
            clip.EnsureEventMarkers();

            bool hasId = false;
            for (int i = 0; i < clip.EventMarkers.Count; i++)
            {
                if (clip.EventMarkers[i] != null && clip.EventMarkers[i].EventId == eventId)
                {
                    hasId = true;
                    break;
                }
            }
            if (hasId)
            {
                clip.SyncLegacyEventsFromMarkers();
                return;
            }

            int frameCount = clip.Frames != null ? clip.Frames.Length : 0;
            for (int i = 0; i < frames.Length; i++)
            {
                int frame = frames[i];
                if (frame < 0 || (frameCount > 0 && frame >= frameCount))
                    continue;
                clip.AddEventMarker(frame, eventId, 0f);
            }
            clip.SyncLegacyEventsFromMarkers();
        }

        static void EnsureEventDef(List<SpriteEventDef> events, byte id, string name, Color color)
        {
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] != null && events[i].Id == id)
                {
                    if (string.IsNullOrWhiteSpace(events[i].Name))
                        events[i].Name = name;
                    return;
                }
            }
            events.Add(new SpriteEventDef { Id = id, Name = name, Color = color });
        }

        static byte[] CopyBytes(byte[] source)
        {
            if (source == null || source.Length == 0)
                return source;
            var copy = new byte[source.Length];
            System.Array.Copy(source, copy, source.Length);
            return copy;
        }

        static float[] CopyFloats(float[] source)
        {
            if (source == null || source.Length == 0)
                return source;
            var copy = new float[source.Length];
            System.Array.Copy(source, copy, source.Length);
            return copy;
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

        public string ResolveEventName(byte id)
        {
            var events = _set?.Profile?.Data?.Events;
            if (events != null)
            {
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i] != null && events[i].Id == id && !string.IsNullOrEmpty(events[i].Name))
                        return events[i].Name;
                }
            }

            return id switch
            {
                FootstepId => "Footstep",
                AttackId => "Attack",
                SpriteAnimLifecycleId.Start => "ClipStarted",
                SpriteAnimLifecycleId.Complete => "ClipCompleted",
                _ => "Event " + id,
            };
        }
    }
}
