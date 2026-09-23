using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>The audio clips a character's events play (index = <see cref="SpritePartsEventBlob.AudioIndex"/>). Baked.</summary>
    public class SpritePartsAudioBank : IComponentData
    {
        public AudioClip[] Clips;
    }

    /// <summary>
    /// Plays Parts event audio (Spine's audio events): 2D, with the event's volume and balance (stereo pan).
    /// Set <see cref="Handler"/> to route it into your own audio system instead.
    /// </summary>
    public static class SpritePartsAudio
    {
        /// <summary>Called instead of the built-in player: (character, clip, volume, balance).</summary>
        public static System.Action<Entity, AudioClip, float, float> Handler;

        /// <summary>False mutes the built-in player (the handler still runs).</summary>
        public static bool Enabled = true;

        const int PoolSize = 8;
        static GameObject s_host;
        static readonly List<AudioSource> s_sources = new List<AudioSource>();
        static int s_next;

        public static void Play(Entity character, AudioClip clip, float volume, float balance)
        {
            if (clip == null)
                return;
            if (Handler != null)
            {
                Handler(character, clip, volume, balance);
                return;
            }
            if (!Enabled || !Application.isPlaying)
                return;
            var source = NextSource();
            source.panStereo = Mathf.Clamp(balance, -1f, 1f);
            source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        static AudioSource NextSource()
        {
            if (s_host == null)
            {
                s_host = new GameObject("Sprite Parts Audio") { hideFlags = HideFlags.HideAndDontSave };
                Object.DontDestroyOnLoad(s_host);
                s_sources.Clear();
                for (int i = 0; i < PoolSize; i++)
                {
                    var s = s_host.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    s.spatialBlend = 0f;
                    s_sources.Add(s);
                }
            }
            var source = s_sources[s_next];
            s_next = (s_next + 1) % s_sources.Count;
            return source;
        }
    }
}
