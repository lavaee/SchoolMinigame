using UnityEngine;

namespace SmallPot
{
    /// <summary>The moments that are worth a sound. Deliberately a small, closed set.</summary>
    public enum Sound
    {
        Click,
        Correct,
        Wrong,
        Reward,
        Water,
        Morning,
        Growth,
        Thirst
    }

    /// <summary>
    /// The one place sound is played from.
    ///
    /// Every clip is optional and empty by default: a missing clip is silence, not an error, so the game
    /// is playable before any audio exists and stays playable if one is removed. Drop a clip into the
    /// matching slot and it plays - no code change. Sounds are one-shots on a single source, which is
    /// enough for interface feedback and keeps the routing obvious.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioDirector : MonoBehaviour
    {
        public static AudioDirector Instance { get; private set; }

        [Header("Output")]
        [Tooltip("Created automatically on this object when left empty.")]
        public AudioSource Source;

        [Range(0f, 1f)]
        public float Volume = 0.8f;

        [Tooltip("Silences everything without discarding the clips.")]
        public bool Muted;

        [Header("Clips (all optional)")]
        public AudioClip Click;
        public AudioClip Correct;
        public AudioClip Wrong;
        public AudioClip Reward;
        public AudioClip Water;
        public AudioClip Morning;
        public AudioClip Growth;
        public AudioClip Thirst;

        private void Awake()
        {
            Instance = this;

            if (Source == null)
            {
                Source = GetComponent<AudioSource>();
            }

            if (Source == null)
            {
                Source = gameObject.AddComponent<AudioSource>();
            }

            Source.playOnAwake = false;
            Source.spatialBlend = 0f;
            Source.volume = Volume;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Plays a moment. Does nothing when there is no director, no clip, or sound is muted.</summary>
        public static void Play(Sound sound)
        {
            if (Instance != null)
            {
                Instance.PlayClip(sound);
            }
        }

        public void PlayClip(Sound sound)
        {
            AudioClip clip = ClipFor(sound);
            if (clip == null || Muted || Source == null)
            {
                return;
            }

            Source.volume = Volume;
            Source.PlayOneShot(clip);
        }

        public AudioClip ClipFor(Sound sound)
        {
            switch (sound)
            {
                case Sound.Click: return Click;
                case Sound.Correct: return Correct;
                case Sound.Wrong: return Wrong;
                case Sound.Reward: return Reward;
                case Sound.Water: return Water;
                case Sound.Morning: return Morning;
                case Sound.Growth: return Growth;
                case Sound.Thirst: return Thirst;
                default: return null;
            }
        }

        /// <summary>True when at least one clip is assigned; used to hide the sound switch until it matters.</summary>
        public bool HasAnyClip =>
            Click != null || Correct != null || Wrong != null || Reward != null
            || Water != null || Morning != null || Growth != null || Thirst != null;
    }
}
