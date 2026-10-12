using DG.Tweening;
using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// Gives a panel its entrance, without every view having to remember to ask for one.
    ///
    /// This hangs off OnEnable, so a panel that is shown by SetActive - which is how all of them are shown
    /// - arrives with a pop the moment it appears. The baseline is captured rather than assumed, because
    /// the scene's panels are hand-authored and one of them may not be at scale 1.
    ///
    /// It sits in its own file because Unity keeps one script asset per file, named after the file. A
    /// second component class inside another file has no script asset of its own, so adding it in the
    /// editor makes Unity embed a script object directly into the scene - and a player build cannot read
    /// a scene that contains one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PanelJuice : MonoBehaviour
    {
        [Tooltip("Off for a full-screen scrim: a scrim should fade, not pop.")]
        public bool Scale = true;

        [Tooltip("Seconds the entrance takes. Zero uses the value from data/dev.json.")]
        public float Seconds;

        [Tooltip("Where the scale starts, as a fraction of the authored size. Zero uses data/dev.json.")]
        public float From;

        private RectTransform _rect;
        private CanvasGroup _group;
        private Vector3 _home;

        private void Awake()
        {
            _rect = transform as RectTransform;
            _group = GetComponent<CanvasGroup>();
            if (_group == null)
            {
                _group = gameObject.AddComponent<CanvasGroup>();
            }

            _home = _rect != null ? _rect.localScale : Vector3.one;
        }

        private void OnEnable()
        {
            if (_rect == null)
            {
                _rect = transform as RectTransform;
            }

            if (_rect == null)
            {
                return;
            }

            if (!Scale)
            {
                // A scrim only fades, and it must not be resized: it covers the screen.
                _rect.localScale = _home;
                MotionPrefs motion = Juice.Prefs;
                if (motion != null && motion.ReduceMotion)
                {
                    _group.alpha = 1f;
                    return;
                }

                Juice.Stop(_group);
                _group.alpha = 0f;
                _group.DOFade(1f, motion != null ? motion.FadeDuration(0.22f) : 0.22f)
                    .SetUpdate(true)
                    .SetId(_group);
                return;
            }

            _rect.localScale = _home;
            Juice.PopIn(_rect, _group, Seconds, From, _home);
        }

        /// <summary>Fades out and then deactivates. For a panel the player dismissed.</summary>
        public void Leave()
        {
            if (_rect == null)
            {
                gameObject.SetActive(false);
                return;
            }

            // Scale is passed through: a fade-only panel must not shrink on the way out either.
            Juice.PopOut(_rect, _group, () => gameObject.SetActive(false), 0.16f, 0.93f, Scale, _home);
        }
    }
}
