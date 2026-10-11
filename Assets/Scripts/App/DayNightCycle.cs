using DG.Tweening;
using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// The sunrise that plays when the player goes to the next morning.
    ///
    /// The sun's intensity and its rotation are driven by two curves rather than by a fixed ease, because
    /// the shape of a sunrise is the one part of it worth tuning by eye: how long the light lingers low
    /// before it comes up is art direction, not a number. Both curves are normalised, evaluated from 0 to
    /// 1 across the transition, so a curve ending at 1 reaches the peak intensity and turns the whole
    /// number of degrees, while one ending lower deliberately stops short.
    ///
    /// It runs on unscaled time, because this game pauses by setting the time scale to zero.
    ///
    /// Nothing here is required in the scene. Call <see cref="Play"/> and it will find the sun and put
    /// itself on it; add the component to the Sun light by hand instead and your curves are the ones that
    /// play, which is the reason the curves exist.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DayNightCycle : MonoBehaviour
    {
        [Header("Shape")]
        [Tooltip("Intensity across the transition, evaluated from 0 to 1. The default eases in and out.")]
        public AnimationCurve Intensity = DefaultEase();

        [Tooltip("Rotation across the transition, evaluated from 0 to 1. The default eases in and out.")]
        public AnimationCurve Rotation = DefaultEase();

        [Header("Timing")]
        [Tooltip("How long the transition takes, in seconds.")]
        public float DurationSeconds = 3f;

        [Tooltip("How far the sun turns over the transition, in degrees. A full turn is one whole day.")]
        public float Degrees = 360f;

        [Header("Light")]
        [Tooltip("The intensity the sun starts from, before the intensity curve is applied.")]
        public float StartIntensity = 0.55f;

        [Tooltip("The intensity the sun reaches when the intensity curve ends at 1.")]
        public float PeakIntensity = 1.15f;

        private Light _sun;
        private Tweener _running;

        /// <summary>
        /// Plays the sunrise.
        ///
        /// Safe to call from anywhere at any time: an instance already in the scene is used - including one
        /// placed on the Sun by hand, so its tuned curves are the ones that play - and one is created on
        /// the sun when there is none, so a missing component is never a crash.
        /// </summary>
        public static void Play() => PlayTurn(1f);

        /// <summary>A half turn: dawn rather than a whole day, for a shorter beat.</summary>
        public static void PlayDawn() => PlayTurn(0.5f);

        /// <summary>
        /// Runs the transition. Also usable from a UnityEvent on a component placed by hand.
        ///
        /// Safe to call again mid-flight: the running transition is killed without completing, so the light
        /// stays exactly where it is rather than snapping back, and the new one carries on from there. When
        /// a transition finishes, the light is left at the intensity and angle it ended on.
        /// </summary>
        public void Begin(float degrees)
        {
            Light sun = _sun != null ? _sun : FindSun(out _);
            if (sun == null)
            {
                return;
            }

            _sun = sun;

            if (_running != null)
            {
                // Killed, not completed and not reverted: the light keeps whatever value it had reached.
                _running.Kill();
                _running = null;
            }

            MotionPrefs motion = Juice.Prefs;
            float duration = motion != null ? motion.Duration(DurationSeconds) : DurationSeconds;
            duration = Mathf.Max(0.01f, duration);

            Quaternion from = sun.transform.rotation;
            Quaternion to = from * Quaternion.Euler(0f, degrees, 0f);

            // One linear driver across 0 to 1, with both curves applied on top of it. The shape of the
            // transition lives entirely in the curves; the driver only says how far along it is. Unclamped
            // interpolation, so a curve that overshoots 1 overshoots the light as well.
            float progress = 0f;
            _running = DOTween.To(
                    () => progress,
                    value =>
                    {
                        progress = value;
                        sun.intensity = Mathf.LerpUnclamped(StartIntensity, PeakIntensity, Intensity.Evaluate(value));
                        sun.transform.rotation = Quaternion.SlerpUnclamped(from, to, Rotation.Evaluate(value));
                    },
                    1f,
                    duration)
                .SetEase(Ease.Linear)
                .SetUpdate(true)
                .SetId(this)
                .OnComplete(() => _running = null);
        }

        private void Awake()
        {
            if (_sun == null)
            {
                _sun = FindSun(out _);
            }
        }

        private static void PlayTurn(float fraction)
        {
            Light sun = FindSun(out string found);
            if (sun == null)
            {
                Debug.LogWarning("[SmallPot] no directional light to play a sunrise on.");
                return;
            }

            DayNightCycle cycle = ExistingFor(sun);
            if (cycle == null)
            {
                // Attached here rather than left to the caller, so a scene that has never heard of this
                // component still gets a sunrise. Add it to the Sun by hand instead to tune the curves.
                cycle = sun.gameObject.AddComponent<DayNightCycle>();
            }

            Debug.Log($"[SmallPot] sunrise on {found}, {cycle.DurationSeconds:0.0}s");
            cycle.Begin(cycle.Degrees * Mathf.Max(0f, fraction));
        }

        /// <summary>
        /// The instance to use: one on the sun if there is one, then any in the scene at all, so a
        /// component placed somewhere convenient still contributes its curves.
        /// </summary>
        private static DayNightCycle ExistingFor(Light sun)
        {
            DayNightCycle onSun = sun.GetComponent<DayNightCycle>();
            return onSun != null ? onSun : FindObjectOfType<DayNightCycle>();
        }

        /// <summary>
        /// The sun, in order of how plainly the project means it: the light the lighting settings name as
        /// the sun, then anything whose name says so, then any directional light at all. Names are matched
        /// case-insensitively because "Sun" and "sun" are both reasonable things to call one.
        /// </summary>
        private static Light FindSun(out string found)
        {
            Light named = RenderSettings.sun;
            if (named != null)
            {
                found = $"RenderSettings.sun ({named.gameObject.name})";
                return named;
            }

            Light[] lights = FindObjectsOfType<Light>();

            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional &&
                    lights[i].gameObject.name.IndexOf("sun", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = $"directional light named {lights[i].gameObject.name}";
                    return lights[i];
                }
            }

            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional)
                {
                    found = $"first directional light ({lights[i].gameObject.name})";
                    return lights[i];
                }
            }

            found = "nothing";
            return null;
        }

        /// <summary>
        /// Smooth in and out, so the morning settles rather than arriving at a constant rate.
        ///
        /// Built in code because the field initialiser is what a component created at runtime gets, and a
        /// straight line would make an un-tuned sunrise look mechanical. Tangents of zero at both ends and
        /// two in the middle give the S shape that reads as dawn.
        /// </summary>
        private static AnimationCurve DefaultEase()
        {
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f),
                new Keyframe(0.5f, 0.5f, 2f, 2f),
                new Keyframe(1f, 1f, 0f, 0f));

            curve.preWrapMode = WrapMode.Clamp;
            curve.postWrapMode = WrapMode.Clamp;
            return curve;
        }
    }
}
