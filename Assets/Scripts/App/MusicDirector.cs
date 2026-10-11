using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// The ambience: one bed at a time, crossfaded, looping forever, never stopped by a pause.
    ///
    /// There are two beds because the game has two moods. Outside the minigame the two general tracks take
    /// turns, so the room never sounds like one loop; from the first round of a set until the reward is
    /// dismissed the focus track holds, and then hands back to whichever general track is next.
    ///
    /// The bed also dips while something is on screen asking for an answer, so a pop-up arrives into quiet
    /// rather than over the top of the music, and comes back up when it is gone.
    ///
    /// Its own GameObject, its own two sources: nothing here shares a source with an effect, so an effect's
    /// pitch and this fade can never interfere.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MusicDirector : MonoBehaviour
    {
        public static MusicDirector Instance { get; private set; }

        [Tooltip("The two beds. Created on this object when left empty.")]
        public AudioSource First;
        public AudioSource Second;

        [Tooltip("Seconds one bed takes to hand over to the next.")]
        public float CrossfadeSeconds = 1.5f;

        [Tooltip("How far the bed dips while a pop-up is on screen.")]
        [Range(0f, 1f)]
        public float DuckLevel = 0.35f;

        [Tooltip("Seconds to dip, and to come back.")]
        public float DuckSeconds = 0.35f;

        [Tooltip("One line each time the bed changes, which is how it is checked on a device.")]
        public bool LogChanges = true;

        private AudioSource _playing;
        private AudioSource _waiting;
        private string _boardTrack = AudioLibrary.AmbienceFirst;
        private bool _focus;
        private bool _ducked;
        private float _level = 1f;
        private float _silence;   // counts a bed down while it is not wanted at all

        private void Awake()
        {
            Instance = this;

            if (First == null)
            {
                First = gameObject.AddComponent<AudioSource>();
            }

            if (Second == null)
            {
                Second = gameObject.AddComponent<AudioSource>();
            }

            Prepare(First);
            Prepare(Second);
            _playing = First;
            _waiting = Second;
            _level = 0f;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>The general bed: the two tracks taking turns, forever.</summary>
        public void PlayBoard()
        {
            if (_focus)
            {
                _focus = false;
                _boardTrack = AudioLibrary.NextBoardTrack(_boardTrack);
            }

            Want(_boardTrack);
        }

        /// <summary>The minigame's own bed, from the first round until the reward is dismissed.</summary>
        public void PlayFocus()
        {
            _focus = true;
            Want(AudioLibrary.AmbienceFocus);
        }

        /// <summary>Dips the bed while something is on screen, and lifts it again afterwards.</summary>
        public void Duck(bool on) => _ducked = on;

        private void Want(string track)
        {
            if (_playing.clip != null && _playing.clip.name == track)
            {
                return;
            }

            AudioClip clip = AudioLibrary.LoadMusic(track);
            if (clip == null)
            {
                // No such file is silence, exactly as a missing effect is.
                return;
            }

            _waiting.clip = clip;
            _waiting.loop = true;
            _waiting.volume = 0f;
            _waiting.Play();

            if (LogChanges)
            {
                Debug.Log($"[SmallPot] music {track}");
            }
        }

        private void Update()
        {
            float music = AudioDirector.Instance != null ? AudioDirector.Instance.MusicVolume : 0.6f;
            float wanted = _ducked ? DuckLevel : 1f;
            float step = DuckSeconds > 0f ? Time.unscaledDeltaTime / DuckSeconds : 1f;
            _level = Mathf.MoveTowards(_level, wanted, step);

            bool handingOver = _waiting.isPlaying && _waiting.clip != null && _waiting.volume < 1f;
            if (handingOver)
            {
                float fade = CrossfadeSeconds > 0f ? Time.unscaledDeltaTime / CrossfadeSeconds : 1f;
                _waiting.volume = Mathf.MoveTowards(_waiting.volume, 1f, fade);
                _playing.volume = Mathf.MoveTowards(_playing.volume, 0f, fade);

                if (_waiting.volume >= 1f)
                {
                    _playing.Stop();
                    AudioSource finished = _playing;
                    _playing = _waiting;
                    _waiting = finished;
                }

                return;
            }

            _playing.volume = Mathf.MoveTowards(_playing.volume, _level * music, step);
            _waiting.volume = Mathf.MoveTowards(_waiting.volume, 0f, step);

            // A general track that has reached its end hands over to the other one, which is what makes the
            // room sound like it keeps going rather than like one file on repeat.
            if (!_focus && _playing.clip != null && !_playing.isPlaying && _waiting.clip == null)
            {
                _boardTrack = AudioLibrary.NextBoardTrack(_boardTrack);
                Want(_boardTrack);
            }
        }

        private static void Prepare(AudioSource source)
        {
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.ignoreListenerPause = true;
        }
    }
}
