using System;
using System.Collections;
using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// Single gate for every animation in the game.
    ///
    /// The "reduce motion" setting from the brief is honoured by returning a zero duration, so the
    /// caller's tween runs instantly and the final state is applied - the state change is never
    /// skipped, only its movement. Opacity fades keep a short duration because a fade conveys
    /// "something changed" without moving anything on screen.
    /// </summary>
    public sealed class MotionPrefs
    {
        public const float ReducedMotionFadeSeconds = 0.15f;

        /// <summary>Raised when the setting changes so views can re-render immediately.</summary>
        public event Action Changed;

        private bool _reduceMotion;

        public bool ReduceMotion
        {
            get => _reduceMotion;
            set
            {
                if (_reduceMotion == value)
                {
                    return;
                }

                _reduceMotion = value;
                Changed?.Invoke();
            }
        }

        /// <summary>Duration for a movement or scale animation; zero when motion is reduced.</summary>
        public float Duration(float seconds) => _reduceMotion ? 0f : Mathf.Max(0f, seconds);

        /// <summary>Duration for an opacity-only change; short but never zero.</summary>
        public float FadeDuration(float seconds) =>
            _reduceMotion ? ReducedMotionFadeSeconds : Mathf.Max(0f, seconds);

        public static IEnumerator Run(IEnumerator routine)
        {
            yield return routine;
        }
    }

    /// <summary>
    /// Tiny coroutine tween helpers.
    ///
    /// Written by hand rather than pulling in DOTween or a Timeline asset: the animations here are a
    /// count-up, a small pop, a grow and a fade, and every one of them has to be switchable off by a
    /// single flag, which is easier when the tweens are a dozen lines each.
    /// </summary>
    public static class Tween
    {
        public static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float inverse = 1f - t;
            return 1f - (inverse * inverse * inverse);
        }

        public static float EaseInOutQuad(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 2f * t * t : 1f - ((-2f * t + 2f) * (-2f * t + 2f) / 2f);
        }

        /// <summary>Overshoots slightly past 1 and settles back, for a small "pop" on success.</summary>
        public static float EaseOutBack(float t, float overshoot = 1.7f)
        {
            t = Mathf.Clamp01(t);
            float c3 = overshoot + 1f;
            float inverse = t - 1f;
            return 1f + (c3 * inverse * inverse * inverse) + (overshoot * inverse * inverse);
        }

        /// <summary>Drives a float from a to b. Always applies the final value, even at zero duration.</summary>
        public static IEnumerator Value(float from, float to, float duration, Action<float> apply)
        {
            if (apply == null)
            {
                yield break;
            }

            if (duration <= 0f)
            {
                apply(to);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                apply(Mathf.LerpUnclamped(from, to, EaseOutCubic(t)));
                yield return null;
            }

            apply(to);
        }

        public static IEnumerator Scale(Transform target, Vector3 from, Vector3 to, float duration)
        {
            if (target == null)
            {
                yield break;
            }

            yield return Value(0f, 1f, duration, t =>
            {
                if (target != null)
                {
                    target.localScale = Vector3.LerpUnclamped(from, to, t);
                }
            });
        }

        /// <summary>Scales from a squash to the original size with a soft overshoot.</summary>
        public static IEnumerator Pop(Transform target, float scaleDownTo, float duration)
        {
            if (target == null)
            {
                yield break;
            }

            Vector3 original = target.localScale;
            Vector3 small = original * scaleDownTo;

            float half = duration * 0.5f;
            if (half <= 0f)
            {
                target.localScale = original;
                yield break;
            }

            yield return Value(1f, 0f, half, t => target.localScale = Vector3.LerpUnclamped(small, original, t));
            yield return Value(0f, 1f, half, t =>
            {
                float eased = EaseOutBack(t);
                target.localScale = Vector3.LerpUnclamped(small, original, eased);
            });
            target.localScale = original;
        }

        public static IEnumerator LocalMove(Transform target, Vector3 from, Vector3 to, float duration)
        {
            if (target == null)
            {
                yield break;
            }

            yield return Value(0f, 1f, duration, t =>
            {
                if (target != null)
                {
                    target.localPosition = Vector3.LerpUnclamped(from, to, t);
                }
            });
        }

        public static IEnumerator ColorTo(GraphicLike graphic, Color from, Color to, float duration)
        {
            if (graphic == null)
            {
                yield break;
            }

            yield return Value(0f, 1f, duration, t => graphic.SetColor(Color.LerpUnclamped(from, to, t)));
        }

        public static IEnumerator FadeCanvasGroup(CanvasGroup group, float from, float to, float duration)
        {
            if (group == null)
            {
                yield break;
            }

            yield return Value(0f, 1f, duration, t => group.alpha = Mathf.LerpUnclamped(from, to, t));
            group.alpha = to;
        }

        /// <summary>Waits for a duration using unscaled time, so a paused timer cannot stall feedback.</summary>
        public static IEnumerator WaitUnscaled(float seconds)
        {
            if (seconds <= 0f)
            {
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }

    /// <summary>
    /// Lets the tween helpers work with anything that has a colour without depending on a concrete
    /// UI type, which keeps this file free of uGUI specifics.
    /// </summary>
    public abstract class GraphicLike : MonoBehaviour
    {
        public abstract void SetColor(Color color);
    }
}
