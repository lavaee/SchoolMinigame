using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SmallPot;
using UnityEngine;

namespace SmallPot.Tests
{
    /// <summary>
    /// The rules behind the sound library and the tuning knobs this project added to data/dev.json.
    ///
    /// These are the parts of the game that cannot be checked by looking at it: a sound that never plays is
    /// silent for the same reason a good one is quiet, and a clamped setting looks identical to an accepted
    /// one until someone types a silly number. So the assertions here are about decisions - that every
    /// moment has been mapped to a file or knowingly left random, that a take never repeats, and that a
    /// value out of range is pulled back rather than obeyed.
    /// </summary>
    public sealed class AudioAndSettingsTests
    {
        /// <summary>The one moment resolved at random rather than by file name; see AudioLibrary.Names.</summary>
        private const Sound Randomised = Sound.Select;

        [Test]
        public void EveryMomentsEitherNamesAFileOrIsDeliberatelyRandomised()
        {
            var names = new HashSet<string>(AudioLibrary.KnownFileNames());

            foreach (Sound sound in Enum.GetValues(typeof(Sound)))
            {
                string fileName = AudioLibrary.FileNameFor(sound);

                if (sound == Randomised)
                {
                    // It is resolved through the four "Select flowerpot" takes instead, so the table has
                    // nothing to say about it.
                    Assert.IsNull(fileName, "Select is expected to be resolved by variation, not by name.");
                    continue;
                }

                Assert.IsNotNull(fileName, $"'{sound}' has no file name; a new moment needs a decision.");
                Assert.IsTrue(names.Contains(fileName), $"'{fileName}' is not among the known names.");
            }
        }

        [Test]
        public void TheNewMomentsPointAtFilesThatAreActuallyThere()
        {
            Assert.AreEqual("Level Up", AudioLibrary.FileNameFor(Sound.LevelUp));
            Assert.AreEqual("No Water", AudioLibrary.FileNameFor(Sound.NoWater));

            // Named exactly as they sit on disk, capitals and spaces included. A silent typo here would
            // otherwise only ever be noticed by ear.
            foreach (Sound sound in new[] { Sound.LevelUp, Sound.NoWater })
            {
                string path = Path.Combine(
                    Application.dataPath, "Resources", AudioLibrary.EffectsFolder,
                    AudioLibrary.FileNameFor(sound) + ".mp3");

                Assert.IsTrue(File.Exists(path), $"Audio file not found: {path}");
            }
        }

        [Test]
        public void AnAnswerNeverReplaysTheTakeItJustUsed()
        {
            const int count = 4;

            for (int previous = 0; previous < count; previous++)
            {
                // Rolls beyond the range so the wrap-around and the negative case are both covered.
                for (int roll = -2 * count; roll <= 2 * count; roll++)
                {
                    int index = AudioLibrary.NextVariationIndex(previous, count, roll);

                    Assert.GreaterOrEqual(index, 0);
                    Assert.Less(index, count);
                    Assert.AreNotEqual(
                        previous, index,
                        $"roll {roll} repeated take {previous}; pressing an answer must not sound repeated.");
                }
            }
        }

        [Test]
        public void WithNoTakesThereIsNothingToPlay()
        {
            Assert.AreEqual(-1, AudioLibrary.NextVariationIndex(0, 0, 0));
            Assert.AreEqual(-1, AudioLibrary.NextVariationIndex(2, -1, 3));
        }

        [Test]
        public void TheTwoMusicBedsTakeTurns()
        {
            string first = AudioLibrary.AmbienceFirst;
            string second = AudioLibrary.AmbienceSecond;

            Assert.AreNotEqual(first, second, "Two beds that are the same file would not be an alternation.");
            Assert.AreEqual(second, AudioLibrary.NextBoardTrack(first));
            Assert.AreEqual(first, AudioLibrary.NextBoardTrack(second));
            Assert.AreNotEqual(first, AudioLibrary.NextBoardTrack(first));
            Assert.AreNotEqual(second, AudioLibrary.NextBoardTrack(second));
        }

        [Test]
        public void ThePauseAfterAnAnswerDefaultsToOneSecondAndIsClamped()
        {
            Assert.AreEqual(1f, DevSettings.FromJson(JsonValue.Parse("{}")).FeedbackPauseSeconds, 0.001f);

            Assert.AreEqual(
                2.5f,
                DevSettings.FromJson(JsonValue.Parse("{\"feedbackPauseSeconds\":2.5}")).FeedbackPauseSeconds,
                0.001f);

            Assert.AreEqual(
                0f,
                DevSettings.FromJson(JsonValue.Parse("{\"feedbackPauseSeconds\":-4}")).FeedbackPauseSeconds,
                0.001f);

            Assert.AreEqual(
                10f,
                DevSettings.FromJson(JsonValue.Parse("{\"feedbackPauseSeconds\":99}")).FeedbackPauseSeconds,
                0.001f);
        }

        [Test]
        public void TheJuiceShapesAreReadFromDataAndClamped()
        {
            var defaults = DevSettings.FromJson(JsonValue.Parse("{}")).Juice;

            Assert.AreEqual(0.36f, defaults.PanelSeconds, 0.001f);
            Assert.AreEqual(0.88f, defaults.PanelFrom, 0.001f);
            Assert.AreEqual(0.95f, defaults.ButtonPressedScale, 0.001f);
            Assert.AreEqual(0.035f, defaults.ButtonReleaseOvershoot, 0.001f);
            Assert.AreEqual(0.4f, defaults.RevealSeconds, 0.001f);
            Assert.AreEqual(0.25f, defaults.RevealDelaySeconds, 0.001f);
            Assert.AreEqual(0.18f, defaults.PunchStrength, 0.001f);
            Assert.AreEqual(0.05f, defaults.JellyStrength, 0.001f);

            var read = DevSettings.FromJson(JsonValue.Parse(@"{
                ""juice"": {
                    ""panelSeconds"": 0.9,
                    ""panelFrom"": 0.5,
                    ""buttonPressedScale"": 0.8,
                    ""buttonReleaseOvershoot"": 0.2,
                    ""revealSeconds"": 1.5,
                    ""revealDelaySeconds"": 0.7,
                    ""punchStrength"": 0.4,
                    ""jellyStrength"": 0.2
                }
            }")).Juice;

            Assert.AreEqual(0.9f, read.PanelSeconds, 0.001f);
            Assert.AreEqual(0.5f, read.PanelFrom, 0.001f);
            Assert.AreEqual(0.8f, read.ButtonPressedScale, 0.001f);
            Assert.AreEqual(0.2f, read.ButtonReleaseOvershoot, 0.001f);
            Assert.AreEqual(1.5f, read.RevealSeconds, 0.001f);
            Assert.AreEqual(0.7f, read.RevealDelaySeconds, 0.001f);
            Assert.AreEqual(0.4f, read.PunchStrength, 0.001f);
            Assert.AreEqual(0.2f, read.JellyStrength, 0.001f);

            // Nonsense is pulled back to the nearest end rather than obeyed: a scale of five would throw a
            // panel off screen, and a negative strength would invert the animation.
            var clamped = DevSettings.FromJson(JsonValue.Parse(@"{
                ""juice"": {
                    ""panelSeconds"": 99,
                    ""panelFrom"": 5,
                    ""buttonPressedScale"": 0.1,
                    ""buttonReleaseOvershoot"": -1,
                    ""revealSeconds"": 60,
                    ""revealDelaySeconds"": -3,
                    ""punchStrength"": 8,
                    ""jellyStrength"": -1
                }
            }")).Juice;

            Assert.AreEqual(3f, clamped.PanelSeconds, 0.001f);
            Assert.AreEqual(1.5f, clamped.PanelFrom, 0.001f);
            Assert.AreEqual(0.5f, clamped.ButtonPressedScale, 0.001f);
            Assert.AreEqual(0f, clamped.ButtonReleaseOvershoot, 0.001f);
            Assert.AreEqual(5f, clamped.RevealSeconds, 0.001f);
            Assert.AreEqual(0f, clamped.RevealDelaySeconds, 0.001f);
            Assert.AreEqual(1f, clamped.PunchStrength, 0.001f);
            Assert.AreEqual(0f, clamped.JellyStrength, 0.001f);
        }

        [Test]
        public void TheSunriseIsReadFromDataAndClamped()
        {
            var defaults = DevSettings.FromJson(JsonValue.Parse("{}")).DayNight;

            Assert.AreEqual(3f, defaults.DurationSeconds, 0.001f);
            Assert.AreEqual(360f, defaults.Degrees, 0.001f);
            Assert.AreEqual(1.15f, defaults.PeakIntensity, 0.001f);
            Assert.AreEqual(0.55f, defaults.StartIntensity, 0.001f);

            var read = DevSettings.FromJson(JsonValue.Parse(@"{
                ""dayNight"": {
                    ""durationSeconds"": 6,
                    ""degrees"": 180,
                    ""peakIntensity"": 2,
                    ""startIntensity"": 0.2
                }
            }")).DayNight;

            Assert.AreEqual(6f, read.DurationSeconds, 0.001f);
            Assert.AreEqual(180f, read.Degrees, 0.001f);
            Assert.AreEqual(2f, read.PeakIntensity, 0.001f);
            Assert.AreEqual(0.2f, read.StartIntensity, 0.001f);

            // A duration of zero would divide into the transition, so it is floored rather than allowed.
            var clamped = DevSettings.FromJson(JsonValue.Parse(@"{
                ""dayNight"": {
                    ""durationSeconds"": 0,
                    ""degrees"": 5000,
                    ""peakIntensity"": -2,
                    ""startIntensity"": 40
                }
            }")).DayNight;

            Assert.AreEqual(0.2f, clamped.DurationSeconds, 0.001f);
            Assert.AreEqual(720f, clamped.Degrees, 0.001f);
            Assert.AreEqual(0f, clamped.PeakIntensity, 0.001f);
            Assert.AreEqual(8f, clamped.StartIntensity, 0.001f);
        }

        [Test]
        public void ApplyingTheJuiceShapesReplacesWhatTheHelpersWillUse()
        {
            // Read back and restored, because these are statics: a test that left them altered would change
            // the feel of every test that ran after it.
            float panel = Juice.PanelSeconds;
            float jelly = Juice.JellyStrength;

            try
            {
                Juice.Apply(new DevSettings.JuiceSettings { PanelSeconds = 1.25f, JellyStrength = 0.12f });

                Assert.AreEqual(1.25f, Juice.PanelSeconds, 0.0001f);
                Assert.AreEqual(0.12f, Juice.JellyStrength, 0.0001f);
            }
            finally
            {
                Juice.PanelSeconds = panel;
                Juice.JellyStrength = jelly;
            }
        }

        [Test]
        public void TheKnobsAreOnlyReadFromTheFilesThisProjectOwns()
        {
            // The four supplied data files are hash-pinned by ConfigTests, so anything tunable has to live
            // in dev.json. If this file ever stops being the source, the game would be relying on a file
            // that cannot be edited.
            Assert.IsTrue(File.Exists(RepoData.PathOf(DevSettings.FileName)), "data/dev.json is missing.");

            JsonValue root = JsonValue.Parse(RepoData.Text(DevSettings.FileName));

            foreach (string key in new[]
            {
                "feedbackPauseSeconds", "juice", "dayNight", "audio", "ui"
            })
            {
                Assert.IsTrue(root.Has(key), $"'{key}' is missing from data/dev.json.");
            }

            var juice = DevSettings.FromJson(root).Juice;
            Assert.Greater(juice.PanelSeconds, 0f, "the shipped panel entrance should take some time.");
        }
    }
}
