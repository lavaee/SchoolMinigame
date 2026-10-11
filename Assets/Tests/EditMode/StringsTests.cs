using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SmallPot;
using UnityEngine;

namespace SmallPot.Tests
{
    /// <summary>
    /// Text is data. Nothing the player can read lives in code, every lookup survives a missing file,
    /// and the fallback chain runs extra -> base -> override so the supplied file always wins.
    /// </summary>
    public sealed class StringsTests
    {
        /// <summary>Every key the game can ask for. If this list grows, the base file has to grow too.</summary>
        private static readonly string[] RequiredKeys =
        {
            "hud.drops",
            "pot.state.seed",
            "pot.state.sprout",
            "pot.state.plant",
            "pot.state.bloom",
            "pot.state.thirsty",
            "pot.watered",
            "action.water",
            "action.play",
            "action.next_morning",
            "water.cost",
            "water.not_enough",
            "game.title",
            "game.prompt",
            "game.time_left",
            "game.correct",
            "game.wrong",
            "game.reward",
            "game.no_reward",
            "report.title",
            "report.grew",
            "report.thirsty",
            "report.nothing",
            "settings.title",
            "settings.reduce_motion",
            "common.continue",
            "common.close"
        };

        /// <summary>
        /// The keys this project added, which the supplied files know nothing about.
        ///
        /// They live in strings-extra.json. The gate is the screen that has to work when nothing else does,
        /// so a missing one shows the player a key name instead of a sentence - which is exactly what
        /// happened once, when a server was still serving an older copy of this file to a phone. Pinned
        /// here so the file cannot lose them quietly again.
        /// </summary>
        private static readonly string[] ProjectKeys =
        {
            "gate.title",
            "gate.body",
            "gate.retry",
            "gate.checking",
            "gate.failed",
            "gate.connected",
            "gate.hint",
            "gate.lost_title",
            "gate.lost_body",
            "gate.lost_status",
            "settings.server_url_hint",
            "settings.data_source_count",
            "content.pending",
            "update.title",
            "update.body",
            "update.detail",
            "update.apply",
            "water.no_drops",
            "water.no_drops_hint"
        };

        [Test]
        public void TheSuppliedBaseFileCoversEveryKeyTheGameNeeds()
        {
            StringsStore strings = RepoData.Strings();

            foreach (string key in RequiredKeys)
            {
                Assert.IsTrue(strings.Has(key), $"'{key}' is missing from the supplied string files.");
                Assert.IsNotEmpty(strings.Get(key));
            }

            Assert.IsEmpty(strings.MissingKeys, "Nothing the game asks for should be missing.");
        }

        [Test]
        public void EveryKeyIsPresentInTheBaseFileAlone()
        {
            // Replacing strings.json with an empty file must not break the game.
            var only = StringsStore.CreateEmpty();
            only.Merge(StringsStore.BaseFileName, JsonValue.Parse(RepoData.Text(StringsStore.BaseFileName)));

            foreach (string key in RequiredKeys)
            {
                Assert.IsTrue(only.Has(key), $"'{key}' has to be playable from strings-base.json alone.");
            }
        }

        [Test]
        public void TheFilesThisProjectAddedCoverTheirOwnKeys()
        {
            JsonValue strings = JsonValue.Parse(RepoData.Text(StringsStore.ExtraFileName))["strings"];

            foreach (string key in ProjectKeys)
            {
                Assert.IsTrue(strings.Has(key), $"'{key}' is missing from strings-extra.json.");
                Assert.IsNotEmpty(strings[key].AsString(string.Empty), $"'{key}' is empty in strings-extra.json.");
            }
        }

        [Test]
        public void TheCountOnTheStatusChipExplainsItselfInPersianDigits()
        {
            StringsStore strings = RepoData.Strings();
            Assert.IsTrue(strings.Has("settings.data_source_count"), "the chip's count line is missing.");

            string line = strings.Format(
                "settings.data_source_count",
                new Dictionary<string, string>(2, System.StringComparer.Ordinal)
                {
                    { "source", strings.Get("settings.data_source.remote") },
                    { "n", strings.DisplayNumber(6) }
                });

            // Both halves matter: the words say what the number is counting, and the number is written in the
            // digits the rest of the game uses. A bare latin "6" in the corner is what the chip used to show.
            StringAssert.Contains(strings.Get("settings.data_source.remote"), line);
            StringAssert.Contains("۶", line);
            StringAssert.DoesNotContain("6", line);
        }

        [Test]
        public void TheUpdateCardNamesItsRevisionInPersianDigits()
        {
            StringsStore strings = RepoData.Strings();
            Assert.IsTrue(strings.Has("update.detail"), "the update card's revision line is missing.");

            string line = strings.FormatNumber("update.detail", 43);
            StringAssert.Contains("۴۳", line);
            StringAssert.DoesNotContain("43", line);
        }

        [Test]
        public void TheOverrideWinsOverTheBaseFile()
        {
            var store = StringsStore.CreateEmpty();
            store.Merge(StringsStore.BaseFileName, JsonValue.Parse(RepoData.Text(StringsStore.BaseFileName)));
            store.Merge(StringsStore.OverrideFileName, JsonValue.Parse(RepoData.Text(StringsStore.OverrideFileName)));

            JsonValue supplied = JsonValue.Parse(RepoData.Text(StringsStore.OverrideFileName))["strings"];
            foreach (string key in supplied.Keys)
            {
                Assert.AreEqual(supplied[key].AsString(), store.Get(key), $"'{key}' should come from strings.json.");
            }
        }

        [Test]
        public void ExtraKeysSitBelowTheSuppliedFiles()
        {
            var store = StringsStore.CreateEmpty();
            const string extra = @"{ ""strings"": { ""game.title"": ""extra wins"", ""only.in.extra"": ""fallback text"" } }";

            store.Merge(StringsStore.ExtraFileName, JsonValue.Parse(extra));
            Assert.AreEqual("extra wins", store.Get("game.title"));

            store.Merge(StringsStore.BaseFileName, JsonValue.Parse(RepoData.Text(StringsStore.BaseFileName)));
            Assert.AreNotEqual("extra wins", store.Get("game.title"), "The supplied file has to win over extras.");
            Assert.AreEqual("fallback text", store.Get("only.in.extra"), "Extra keys still resolve when nothing overrides them.");
        }

        [Test]
        public void UnknownKeysReturnAMarkerRatherThanNull()
        {
            StringsStore store = RepoData.Strings();

            Assert.AreEqual("[nope.missing]", store.Get("nope.missing"));
            Assert.IsNotNull(store.Get(null));
            Assert.AreEqual(string.Empty, store.Get(string.Empty));
            Assert.AreEqual("fallback", store.GetOrFallback("nope.missing", "fallback"));
            CollectionAssert.Contains(store.MissingKeys, "nope.missing");
        }

        [Test]
        public void UnusableFilesAreReportedAndIgnored()
        {
            var store = StringsStore.CreateEmpty();
            store.Merge("mystery.json", JsonValue.Parse("{}"));
            store.Merge(StringsStore.BaseFileName, JsonValue.Parse("[]"));
            store.Merge(StringsStore.BaseFileName, JsonValue.Parse(@"{ ""locale"": ""fa"" }"));

            Assert.AreEqual(3, store.Warnings.Count);
            Assert.AreEqual(0, store.Count);
        }

        [Test]
        public void PlaceholdersAreReplacedWithPersianDigits()
        {
            StringsStore store = RepoData.Strings();

            string cost = store.FormatNumber("water.cost", 4);
            Assert.IsFalse(cost.Contains("{n}"), "The placeholder should have been replaced.");
            StringAssert.Contains("\u06F4", cost); // Persian four

            string reward = store.FormatNumber("game.reward", 7);
            StringAssert.Contains("\u06F7", reward);

            string prompt = store.FormatWord("game.prompt", store.Get("pot.state.seed"));
            Assert.IsFalse(prompt.Contains("{word}"));

            string wrong = store.FormatAnswer("game.wrong", "x");
            Assert.IsFalse(wrong.Contains("{answer}"));

            string grew = store.FormatState("report.grew", store.StateLabel("sprout"));
            Assert.IsFalse(grew.Contains("{state}"));
        }

        [Test]
        public void AKeyWithNoPlaceholderIsReturnedUnchanged()
        {
            StringsStore store = RepoData.Strings();
            Assert.AreEqual(store.Get("action.water"), store.FormatNumber("action.water", 3));
        }

        [Test]
        public void DigitsAreConvertedForDisplay()
        {
            StringsStore store = RepoData.Strings();

            Assert.AreEqual("\u06F0", store.DisplayNumber(0));
            Assert.AreEqual("\u06F3", store.DisplayNumber(3));
            Assert.AreEqual("\u06F1\u06F2", store.DisplayNumber(12));
            Assert.AreEqual("12", PersianText.ToLatinDigits(store.DisplayNumber(12)));
            Assert.AreEqual("3", PersianText.ToLatinDigits("\u06F3"));
        }

        [Test]
        public void ShapingTurnsLettersIntoJoinedForms()
        {
            StringsStore store = RepoData.Strings();
            string logical = store.Get("action.water");

            string shaped = PersianText.Shape(logical);
            Assert.AreNotEqual(logical, shaped, "Shaping has to change the text for it to be connected.");
            Assert.Less(PersianText.CountUnshapedArabicLetters(shaped), PersianText.CountUnshapedArabicLetters(logical) + 1);

            bool hasPresentationForm = false;
            foreach (char c in shaped)
            {
                if (c >= '\uFB50' && c <= '\uFEFF')
                {
                    hasPresentationForm = true;
                    break;
                }
            }

            Assert.IsTrue(hasPresentationForm, "Shaped Persian should use the presentation forms block.");
        }

        [Test]
        public void DisplayedTextIsShapedWithPersianDigitsAndIsIdempotent()
        {
            StringsStore store = RepoData.Strings();

            string once = store.Display(store.FormatNumber("water.cost", 4));
            string twice = store.Display(once);

            Assert.AreEqual(once, twice, "Displaying already displayed text must not corrupt it.");
        }

        [Test]
        public void EverySuppliedStringCanBeDisplayed()
        {
            StringsStore store = RepoData.Strings();

            foreach (string key in RequiredKeys)
            {
                string displayed = store.Display(store.Get(key));
                Assert.IsNotEmpty(displayed, $"'{key}' displayed as empty text.");
                Assert.IsFalse(displayed.Contains("["), $"'{key}' fell back to a missing-key marker.");
            }
        }

        [Test]
        public void StateLabelsExistForEveryConfiguredStage()
        {
            StringsStore store = RepoData.Strings();
            GameConfig config = RepoData.Config();

            for (int index = 0; index < config.StageCount; index++)
            {
                string stageId = config.StageAt(index);
                string label = store.StateLabel(stageId);
                Assert.AreNotEqual($"[pot.state.{stageId}]", label, $"Stage '{stageId}' needs a label.");
            }

            Assert.AreNotEqual($"[pot.state.{PlantState.ThirstyStageId}]", store.StateLabel(PlantState.ThirstyStageId));
        }

        [Test]
        public void NoPersianOrArabicTextIsHardCodedInTheGameCode()
        {
            // The brief requires every player-visible string to come from data. Scanning the sources is
            // the only way to keep that true over time.
            var roots = new[]
            {
                Path.Combine(Application.dataPath, "Scripts"),
                Path.Combine(Application.dataPath, "Editor")
            };

            var offenders = new List<string>();
            foreach (string root in roots)
            {
                foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    string text = File.ReadAllText(file);
                    for (int i = 0; i < text.Length; i++)
                    {
                        char c = text[i];
                        bool arabic = (c >= '\u0600' && c <= '\u06FF') || (c >= '\uFB50' && c <= '\uFEFF');
                        if (arabic)
                        {
                            offenders.Add($"{file}: U+{(int)c:X4}");
                            break;
                        }
                    }
                }
            }

            CollectionAssert.IsEmpty(offenders, "Player-visible text belongs in the data files, not in code.");
        }
    }
}
