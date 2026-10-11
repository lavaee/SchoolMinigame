using System.Collections.Generic;
using UnityEngine;

namespace SmallPot
{
    /// <summary>The moments that are worth a sound. Deliberately a small, closed set.</summary>
    public enum Sound
    {
        Click,
        Select,
        Correct,
        Wrong,
        Reward,
        Water,
        Growth,
        LevelUp,
        NoWater,
        Encourage,
        Morning,
        Thirst,
        ConnectionLost,
        ConnectionUp
    }

    /// <summary>
    /// The one place sound is played from.
    ///
    /// Every clip is optional: a moment with no file is silence, not an error, so the game is playable with
    /// no audio in the project and stays playable if a file is removed. Clips are found by name from
    /// Assets/Resources/Audio (see AudioLibrary), which means dropping a file in is the whole installation;
    /// a clip assigned to a slot here still wins, so the scene keeps the last word.
    ///
    /// Effects play from a small pool of sources rather than one. PlayOneShot reads the source's pitch at
    /// the moment it is called, so a single source would retune whatever is still ringing every time a new
    /// sound arrives - which is audible the instant two buttons are pressed close together.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioDirector : MonoBehaviour
    {
        public static AudioDirector Instance { get; private set; }

        private const int VoiceCount = 5;

        private static readonly Sound[] AllSounds =
        {
            Sound.Click, Sound.Select, Sound.Correct, Sound.Wrong, Sound.Reward, Sound.Water,
            Sound.Growth, Sound.LevelUp, Sound.NoWater, Sound.Encourage, Sound.Morning, Sound.Thirst,
            Sound.ConnectionLost, Sound.ConnectionUp
        };

        [Header("Output")]
        [Tooltip("The first voice. The others are added to this object automatically.")]
        public AudioSource Source;

        [Range(0f, 1f)]
        public float Volume = 0.8f;

        [Range(0f, 1f)]
        public float MusicVolume = 0.6f;

        [Tooltip("Silences everything without discarding the clips.")]
        public bool Muted;

        [Header("Clips (optional - files in Resources/Audio are found by name)")]
        public AudioClip Click;
        public AudioClip Correct;
        public AudioClip Wrong;
        public AudioClip Reward;
        public AudioClip Water;
        public AudioClip Morning;
        public AudioClip Growth;
        public AudioClip Thirst;

        [Header("Feel")]
        [Tooltip("Random detune on every effect, so a repeated press never sounds like a copy of itself.")]
        [Range(0f, 0.2f)]
        public float PitchVariation = 0.03f;

        [Tooltip("A repeat of the same sound inside this window is dropped rather than stacked.")]
        [Range(0f, 0.5f)]
        public float RepeatWindowSeconds = 0.04f;

        [Tooltip("One line per sound played, which is how the mapping is checked on a device.")]
        public bool LogPlays = true;

        private readonly List<AudioSource> _voices = new List<AudioSource>();
        private readonly Dictionary<Sound, float> _lastPlayed = new Dictionary<Sound, float>();
        private readonly Dictionary<Sound, AudioClip> _resolved = new Dictionary<Sound, AudioClip>();
        private int _nextVoice;
        private int _lastVariation = -1;
        private DevSettings _appliedSettings;
        private bool _loggedSettings;

        private void Awake()
        {
            Instance = this;
            BuildVoices();
        }

        private void Start()
        {
            ApplyAudioSettings();
        }

        private void Update()
        {
            // Our own values can be replaced while the game is running, because a new revision can be pulled
            // from the server at any moment. A reference comparison is the whole test for that: it allocates
            // nothing and looks for nothing in the scene, so checking it every frame costs almost nothing.
            if (!ReferenceEquals(_appliedSettings, DevSettings.Current))
            {
                ApplyAudioSettings();
            }
        }

        /// <summary>
        /// Pushes the volumes and the feel from our own data onto this component.
        ///
        /// Called once at startup and again whenever the settings object is replaced. Until the data has been
        /// read there is nothing to apply, and the values serialised in the scene stand - which is also what
        /// happens if this component is dropped into a scene with no data loader at all.
        /// </summary>
        public void ApplyAudioSettings()
        {
            DevSettings settings = DevSettings.Current;
            _appliedSettings = settings;

            if (settings == null || settings.Audio == null)
            {
                return;
            }

            var audio = settings.Audio;
            Volume = audio.SfxVolume;
            MusicVolume = audio.MusicVolume;
            PitchVariation = audio.PitchVariation;
            RepeatWindowSeconds = audio.RepeatWindowSeconds;
            LogPlays = audio.LogPlays;

            // Once, not on every reload: this is a note about what was applied, not a running commentary,
            // and a line per revision would bury the per-sound lines that are the point of LogPlays.
            if (audio.LogPlays && !_loggedSettings)
            {
                _loggedSettings = true;
                Debug.Log($"[SmallPot] audio sfx {Volume:0.00} music {MusicVolume:0.00} " +
                          $"pitch +/-{PitchVariation:0.00} repeat {RepeatWindowSeconds:0.00}s");
            }
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
            if (Muted)
            {
                return;
            }

            AudioClip clip = ClipFor(sound);
            if (clip == null || IsTooSoon(sound))
            {
                return;
            }

            float pitch;
            float level;
            AudioLibrary.Voicing(sound, out pitch, out level);

            AudioSource voice = NextVoice();
            voice.pitch = pitch * Random.Range(1f - PitchVariation, 1f + PitchVariation);
            voice.PlayOneShot(clip, Mathf.Clamp01(level * Volume));

            if (LogPlays)
            {
                Debug.Log($"[SmallPot] sound {sound} ({clip.name}) at {voice.pitch:0.00}x");
            }
        }

        /// <summary>The clip for a moment: the assigned slot first, then the file named after it.</summary>
        public AudioClip ClipFor(Sound sound)
        {
            AudioClip assigned = Assigned(sound);
            if (assigned != null)
            {
                return assigned;
            }

            // The four "Select flowerpot" takes are chosen at random rather than by name, so this one moment
            // is resolved here instead of through the table. Pressing an answer should not sound like a
            // machine repeating itself, and a fixed file name could not express that.
            if (sound == Sound.Select)
            {
                return NextVariation();
            }

            AudioClip cached;
            if (_resolved.TryGetValue(sound, out cached))
            {
                return cached;
            }

            AudioClip loaded = AudioLibrary.LoadEffects(AudioLibrary.FileNameFor(sound));
            _resolved[sound] = loaded;
            return loaded;
        }

        /// <summary>True when anything at all can be played; used to hide the sound switch until it matters.</summary>
        public bool HasAnyClip
        {
            get
            {
                for (int index = 0; index < AllSounds.Length; index++)
                {
                    if (ClipFor(AllSounds[index]) != null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private AudioClip Assigned(Sound sound)
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

        /// <summary>One of the four takes, never the same one twice in a row.</summary>
        private AudioClip NextVariation()
        {
            string[] names = AudioLibrary.SelectVariations;
            _lastVariation = AudioLibrary.NextVariationIndex(_lastVariation, names.Length, Random.Range(0, names.Length));
            return AudioLibrary.LoadEffects(names[_lastVariation]);
        }

        private AudioSource NextVoice()
        {
            if (_voices.Count == 0)
            {
                BuildVoices();
            }

            AudioSource voice = _voices[_nextVoice % _voices.Count];
            _nextVoice = (_nextVoice + 1) % _voices.Count;
            return voice;
        }

        private bool IsTooSoon(Sound sound)
        {
            if (RepeatWindowSeconds <= 0f)
            {
                return false;
            }

            float now = Time.unscaledTime;
            float last;
            if (_lastPlayed.TryGetValue(sound, out last) && now - last < RepeatWindowSeconds)
            {
                return true;
            }

            _lastPlayed[sound] = now;
            return false;
        }

        private void BuildVoices()
        {
            _voices.Clear();

            if (Source == null)
            {
                Source = GetComponent<AudioSource>();
            }

            if (Source == null)
            {
                Source = gameObject.AddComponent<AudioSource>();
            }

            _voices.Add(Source);

            while (_voices.Count < VoiceCount)
            {
                _voices.Add(gameObject.AddComponent<AudioSource>());
            }

            for (int index = 0; index < _voices.Count; index++)
            {
                AudioSource voice = _voices[index];
                voice.playOnAwake = false;
                voice.spatialBlend = 0f;
                voice.loop = false;

                // Pausing this game means a timeScale of zero, but the listener could be paused too, and
                // interface feedback falling silent while the game waits for the player is the wrong answer.
                voice.ignoreListenerPause = true;
            }
        }
    }
}
