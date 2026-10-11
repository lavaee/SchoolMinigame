using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SmallPot
{
    /// <summary>
    /// The juice: the small movements that make a tap feel like it landed.
    ///
    /// DOTween drives these rather than the hand-written tweens in Motion.cs, because a jelly is a
    /// sequence of three curves and a shake is a decaying oscillation - a line each with a tween library,
    /// a page each by hand. Motion.cs keeps the count-ups and fades it already does well.
    ///
    /// Two rules hold for everything in this file.
    ///
    /// It goes through MotionPrefs, so the reduce motion setting damps all of it, and a damped animation
    /// still applies its final state rather than being skipped.
    ///
    /// It runs on unscaled time, because this game pauses by setting the time scale to zero - the
    /// connection gate and the update card are pauses - and an entrance that freezes halfway is worse
    /// than no entrance at all.
    ///
    /// Nothing here changes state, and nothing here leaves a widget anywhere but where it found it: the
    /// interface in the scene is hand-authored, so every helper returns the size, position and opacity it
    /// was given. The components at the bottom capture that baseline themselves rather than assuming 1.
    /// </summary>
    public static class Juice
    {
        /// <summary>
        /// The gate every helper consults.
        ///
        /// Set once by the composition root: a component that animates a widget cannot reach the
        /// controller on its own, and searching for it on every tap would be a scene search per press.
        /// </summary>
        public static MotionPrefs Prefs;

        private const float FadeSeconds = 0.18f;

        /// <summary>Stops everything this helper has running on a target, before it is animated again.</summary>
        public static void Stop(Component target)
        {
            if (target != null)
            {
                DOTween.Kill(target);
            }
        }

        // ---- Entrances and exits ----------------------------------------------------------------

        /// <summary>
        /// A pop-up arriving: a fade with a slight overshoot in scale.
        ///
        /// The overshoot is what makes it read as arriving rather than appearing, and it is deliberately
        /// small - a panel that bounces is a panel that looks unsteady.
        /// </summary>
        public static void PopIn(RectTransform rect, CanvasGroup group, float seconds = 0.36f, float from = 0.88f,
            Vector3 home = default)
        {
            if (rect == null)
            {
                return;
            }

            // Zero is never a real authored scale, so it is the signal that nothing was passed in.
            if (home == Vector3.zero)
            {
                home = Vector3.one;
            }

            MotionPrefs motion = Prefs;
            if (motion != null && motion.ReduceMotion)
            {
                rect.localScale = home;
                if (group != null)
                {
                    group.alpha = 1f;
                }

                return;
            }

            Stop(rect);
            Stop(group);

            rect.localScale = home * from;
            if (group != null)
            {
                group.alpha = 0f;
            }

            rect.DOScale(home, motion != null ? motion.Duration(seconds) : seconds)
                .SetEase(Ease.OutBack)
                .SetUpdate(true)
                .SetId(rect);

            if (group != null)
            {
                group.DOFade(1f, motion != null ? motion.FadeDuration(FadeSeconds) : FadeSeconds)
                    .SetUpdate(true)
                    .SetId(group);
            }
        }

        /// <summary>
        /// A pop-up leaving. Calls back when it has gone, so the caller can deactivate it afterwards -
        /// a fade that is cut off by SetActive is not a fade.
        /// </summary>
        public static void PopOut(RectTransform rect, CanvasGroup group, Action done, float seconds = 0.16f,
            float to = 0.93f, bool scale = true, Vector3 home = default)
        {
            if (rect == null)
            {
                if (done != null)
                {
                    done();
                }

                return;
            }

            if (home == Vector3.zero)
            {
                home = Vector3.one;
            }

            MotionPrefs motion = Prefs;
            if (motion != null && motion.ReduceMotion)
            {
                rect.localScale = home;
                if (group != null)
                {
                    group.alpha = 1f;
                }

                if (done != null)
                {
                    done();
                }

                return;
            }

            Stop(rect);
            Stop(group);

            float duration = Mathf.Max(0.01f, motion != null ? motion.FadeDuration(seconds) : seconds);
            Sequence sequence = DOTween.Sequence().SetUpdate(true).SetId(rect);

            if (group != null)
            {
                sequence.Join(group.DOFade(0f, duration));
            }

            if (scale)
            {
                sequence.Join(rect.DOScale(home * to, duration).SetEase(Ease.InQuad));
            }

            sequence.OnComplete(() =>
            {
                // Left as found, so the next entrance starts from a clean state rather than from wherever
                // this one happened to finish.
                rect.localScale = home;
                if (group != null)
                {
                    group.alpha = 1f;
                }

                if (done != null)
                {
                    done();
                }
            });
        }

        // ---- Attention --------------------------------------------------------------------------

        /// <summary>
        /// A wobble that settles, for something that just changed.
        ///
        /// Relative, through DOPunchScale, rather than absolute scales of its own: a widget the scene
        /// authored at something other than 1 keeps its size, because a punch oscillates around whatever
        /// it finds and ends there. An absolute jelly would quietly flatten it to 1 on the first tap.
        /// </summary>
        public static void Jelly(RectTransform rect, float strength = 0.05f, float seconds = 0.6f)
        {
            if (rect == null)
            {
                return;
            }

            MotionPrefs motion = Prefs;
            float duration = motion != null ? motion.Duration(seconds) : seconds;
            if (duration <= 0f)
            {
                return;
            }

            Stop(rect);
            rect.DOPunchScale(Vector3.one * strength, duration, 2, 0.6f).SetUpdate(true).SetId(rect);
        }

        /// <summary>One clean overshoot, for a number that just went up.</summary>
        public static void Punch(Transform target, float strength = 0.18f, float seconds = 0.4f)
        {
            if (target == null)
            {
                return;
            }

            MotionPrefs motion = Prefs;
            float duration = motion != null ? motion.Duration(seconds) : seconds;
            if (duration <= 0f)
            {
                return;
            }

            target.DOPunchScale(Vector3.one * strength, duration, 8, 0.9f).SetUpdate(true);
        }

        /// <summary>A short shake, for a wrong answer that should be felt rather than read.</summary>
        public static void Shake(RectTransform rect, float strength = 12f, float seconds = 0.32f)
        {
            if (rect == null)
            {
                return;
            }

            MotionPrefs motion = Prefs;
            float duration = motion != null ? motion.Duration(seconds) : seconds;
            if (duration <= 0f)
            {
                return;
            }

            Stop(rect);
            rect.DOShakeAnchorPos(duration, strength, 12, 90f, false, true).SetUpdate(true).SetId(rect);
        }

        // ---- Buttons ----------------------------------------------------------------------------

        /// <summary>The squish while a button is held. Restores whatever scale it found.</summary>
        public static void Press(RectTransform rect, Vector3 home, float to = 0.95f)
        {
            if (rect == null)
            {
                return;
            }

            MotionPrefs motion = Prefs;
            float duration = motion != null ? motion.Duration(0.09f) : 0.09f;
            if (duration <= 0f)
            {
                return;
            }

            Stop(rect);
            rect.DOScale(home * to, duration).SetEase(Ease.OutQuad).SetUpdate(true).SetId(rect);
        }

        /// <summary>The release: back past its own size, then settled.</summary>
        public static void Release(RectTransform rect, Vector3 home, float overshoot = 0.035f)
        {
            if (rect == null)
            {
                return;
            }

            MotionPrefs motion = Prefs;
            float duration = motion != null ? motion.Duration(0.28f) : 0.28f;
            if (duration <= 0f)
            {
                rect.localScale = home;
                return;
            }

            Stop(rect);

            Sequence sequence = DOTween.Sequence().SetUpdate(true).SetId(rect);
            sequence.Append(rect.DOScale(home * (1f + overshoot), duration * 0.45f).SetEase(Ease.OutQuad));
            sequence.Append(rect.DOScale(home, duration * 0.55f).SetEase(Ease.OutBack));
        }

        // ---- Text -------------------------------------------------------------------------------

        /// <summary>
        /// Reveals a label character by character, from the side the language starts on.
        ///
        /// Only the visible range is animated, never the string: the label's own text is already shaped by
        /// RTLTMPro, and touching it would shape it a second time and produce glyphs that no longer read as
        /// words. The worst this can do is show too little of the finished text.
        ///
        /// The direction matters and is not cosmetic. maxVisibleCharacters counts in mesh order, and
        /// RTLTMPro lays Persian into the mesh right to left, so growing that count would spell a heading
        /// out backwards from its last word. Persian therefore animates firstVisibleCharacter down instead,
        /// which reveals the mesh from its far end - the side the sentence actually begins on.
        /// </summary>
        public static void RevealText(TMP_Text label, Action done = null, float seconds = 0.4f,
            bool rightToLeft = true)
        {
            if (label == null)
            {
                if (done != null)
                {
                    done();
                }

                return;
            }

            MotionPrefs motion = Prefs;
            float duration = motion != null ? motion.Duration(seconds) : seconds;

            label.ForceMeshUpdate();
            int total = label.textInfo != null ? label.textInfo.characterCount : 0;

            if (duration <= 0f || total <= 1)
            {
                label.maxVisibleCharacters = int.MaxValue;
                label.firstVisibleCharacter = 0;
                if (done != null)
                {
                    done();
                }

                return;
            }

            Stop(label);

            if (rightToLeft)
            {
                label.maxVisibleCharacters = int.MaxValue;
                label.firstVisibleCharacter = total;

                DOTween.To(() => label.firstVisibleCharacter, value => label.firstVisibleCharacter = value, 0, duration)
                    .SetEase(Ease.OutQuad)
                    .SetUpdate(true)
                    .SetId(label)
                    .OnComplete(() =>
                    {
                        label.firstVisibleCharacter = 0;
                        if (done != null)
                        {
                            done();
                        }
                    });

                return;
            }

            label.firstVisibleCharacter = 0;
            label.maxVisibleCharacters = 0;

            DOTween.To(() => label.maxVisibleCharacters, value => label.maxVisibleCharacters = value, total, duration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .SetId(label)
                .OnComplete(() =>
                {
                    label.maxVisibleCharacters = int.MaxValue;
                    if (done != null)
                    {
                        done();
                    }
                });
        }

        /// <summary>A slow drift for something at rest. Titles and the plant, never body copy.</summary>
        public static void Sway(RectTransform rect, float pixels = 2.5f, float seconds = 2.6f)
        {
            if (rect == null)
            {
                return;
            }

            MotionPrefs motion = Prefs;
            float duration = motion != null ? motion.Duration(seconds) : seconds;
            if (duration <= 0f)
            {
                return;
            }

            Vector2 home = rect.anchoredPosition;
            Stop(rect);

            rect.DOAnchorPos(home + new Vector2(0f, pixels), duration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true)
                .SetId(rect);
        }

        /// <summary>A slow breath, for the plant when it is thriving. Movement without attention.</summary>
        public static void Breathe(RectTransform rect, Vector3 home, float amount = 0.02f, float seconds = 2.2f)
        {
            if (rect == null)
            {
                return;
            }

            MotionPrefs motion = Prefs;
            float duration = motion != null ? motion.Duration(seconds) : seconds;
            if (duration <= 0f)
            {
                return;
            }

            Stop(rect);

            rect.DOScale(home * (1f + amount), duration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true)
                .SetId(rect);
        }

        /// <summary>Slides a row in from the side, for lists that arrive one line at a time.</summary>
        public static void SlideIn(RectTransform rect, Vector2 from, float seconds = 0.32f, float delay = 0f)
        {
            if (rect == null)
            {
                return;
            }

            MotionPrefs motion = Prefs;
            float duration = motion != null ? motion.Duration(seconds) : seconds;
            if (duration <= 0f)
            {
                return;
            }

            Vector2 home = rect.anchoredPosition;
            Stop(rect);
            rect.anchoredPosition = home + from;

            rect.DOAnchorPos(home, duration)
                .SetEase(Ease.OutCubic)
                .SetDelay(delay)
                .SetUpdate(true)
                .SetId(rect);
        }
    }

    /// <summary>
    /// Gives a panel its entrance, without every view having to remember to ask for one.
    ///
    /// This hangs off OnEnable, so a panel that is shown by SetActive - which is how all of them are shown
    /// - arrives with a pop the moment it appears. The baseline is captured rather than assumed, because
    /// the scene's panels are hand-authored and one of them may not be at scale 1.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PanelJuice : MonoBehaviour
    {
        [Tooltip("Off for a full-screen scrim: a scrim should fade, not pop.")]
        public bool Scale = true;

        [Tooltip("Seconds the entrance takes.")]
        public float Seconds = 0.36f;

        [Tooltip("Where the scale starts, as a fraction of the authored size.")]
        public float From = 0.88f;

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

    /// <summary>
    /// The squish under a press, for every button the kit builds.
    ///
    /// It listens for the pointer itself rather than relying on the Button's own transitions, because a
    /// press that is felt on the way down is a different thing from a colour change on the way up. The
    /// scale it returns to is the one the button was authored with, captured once.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ButtonJuice : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Range(0.8f, 1f)]
        public float PressedScale = 0.95f;

        private RectTransform _rect;
        private Vector3 _home;
        private bool _held;

        private void Awake()
        {
            _rect = transform as RectTransform;
            _home = _rect != null ? _rect.localScale : Vector3.one;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _held = true;
            Juice.Press(_rect, _home, PressedScale);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_held)
            {
                _held = false;
                Juice.Release(_rect, _home);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // Dragged off the button: let it back up rather than leaving it squashed.
            if (_held)
            {
                _held = false;
                Juice.Release(_rect, _home);
            }
        }

        private void OnDisable()
        {
            _held = false;
            if (_rect != null)
            {
                _rect.localScale = _home;
            }
        }
    }
}
