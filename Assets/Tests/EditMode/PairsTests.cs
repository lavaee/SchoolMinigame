using System;
using System.Collections.Generic;
using NUnit.Framework;
using SmallPot;

namespace SmallPot.Tests
{
    /// <summary>
    /// The minigame is built fresh every time from the pair list: five rounds, one word each, three
    /// options, and exactly one of them is the antonym.
    /// </summary>
    public sealed class PairsTests
    {
        private static PairsData Load()
        {
            PairsData data = PairsData.FromJsonText(RepoData.Text(PairsData.FileName), out string error);
            Assert.IsNull(error);
            return data;
        }

        [Test]
        public void TheSuppliedFileHoldsFifteenUsablePairs()
        {
            PairsData data = Load();

            Assert.AreEqual(15, data.Pairs.Count);
            Assert.IsTrue(data.HasUsablePairs);

            foreach (PairEntry entry in data.Pairs)
            {
                Assert.IsNotEmpty(entry.Word, "Every pair needs a word.");
                Assert.IsNotEmpty(entry.Opposite, "Every pair needs an antonym.");
                Assert.AreEqual(2, entry.Wrong.Count, $"'{entry.Word}' needs exactly two distractors.");
                CollectionAssert.DoesNotContain(entry.Wrong, entry.Opposite);
                CollectionAssert.DoesNotContain(entry.Wrong, entry.Word);
                Assert.IsFalse(entry.Wrong[0] == entry.Wrong[1], $"'{entry.Word}' repeats a distractor.");
            }
        }

        [Test]
        public void WordsAndAntonymsAreUnique()
        {
            PairsData data = Load();
            var words = new HashSet<string>();

            foreach (PairEntry entry in data.Pairs)
            {
                Assert.IsTrue(words.Add(entry.Word), $"'{entry.Word}' appears twice.");
            }
        }

        [Test]
        public void RoundsUseTheConfiguredCountAndChoices()
        {
            GameConfig config = RepoData.Config();
            PairsData data = Load();

            List<PairRound> rounds = data.BuildRounds(
                config.MiniGame.Rounds,
                config.MiniGame.ChoicesShown,
                new Random(20240513));

            Assert.AreEqual(config.MiniGame.Rounds, rounds.Count);

            foreach (PairRound round in rounds)
            {
                Assert.IsNotEmpty(round.Word);
                Assert.AreEqual(config.MiniGame.ChoicesShown, round.Choices.Count);
                Assert.IsTrue(round.AnswerIndex >= 0 && round.AnswerIndex < round.Choices.Count);

                int answers = 0;
                for (int i = 0; i < round.Choices.Count; i++)
                {
                    if (round.IsAnswer(i))
                    {
                        answers++;
                        Assert.AreEqual(round.Answer, round.Choices[i]);
                    }
                }

                Assert.AreEqual(1, answers, "A round has exactly one correct option.");

                var distinct = new HashSet<string>(round.Choices);
                Assert.AreEqual(round.Choices.Count, distinct.Count, "Options must not repeat.");
            }

            foreach (PairRound round in rounds)
            {
                Assert.IsNotEmpty(round.Answer);
            }
        }

        [Test]
        public void RoundsAreDrawnWithoutRepeatingAWord()
        {
            PairsData data = Load();
            List<PairRound> rounds = data.BuildRounds(5, 3, new Random(7));

            var seen = new HashSet<string>();
            foreach (PairRound round in rounds)
            {
                Assert.IsTrue(seen.Add(round.Word), $"'{round.Word}' was asked twice in one session.");
            }
        }

        [Test]
        public void AskingForMoreRoundsThanPairsStillTerminates()
        {
            PairsData data = Load();
            List<PairRound> rounds = data.BuildRounds(40, 3, new Random(3));

            Assert.IsTrue(rounds.Count > 0);
            Assert.IsTrue(rounds.Count <= 40);
        }

        [Test]
        public void TheSameSeedGivesTheSameSession()
        {
            PairsData data = Load();

            List<PairRound> first = data.BuildRounds(5, 3, new Random(99));
            List<PairRound> second = data.BuildRounds(5, 3, new Random(99));

            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].Word, second[i].Word);
                Assert.AreEqual(first[i].AnswerIndex, second[i].AnswerIndex);
                CollectionAssert.AreEqual(first[i].Choices, second[i].Choices);
            }
        }

        [Test]
        public void DifferentSeedsRearrangeTheSession()
        {
            PairsData data = Load();

            List<PairRound> a = data.BuildRounds(5, 3, new Random(1));
            List<PairRound> b = data.BuildRounds(5, 3, new Random(2));

            bool differs = false;
            for (int i = 0; i < Math.Min(a.Count, b.Count) && !differs; i++)
            {
                if (a[i].Word != b[i].Word || a[i].AnswerIndex != b[i].AnswerIndex)
                {
                    differs = true;
                }
            }

            Assert.IsTrue(differs, "Round order and option order must be shuffled each time.");
        }

        [Test]
        public void OptionOrderVariesWithinTheSameWord()
        {
            PairsData data = Load();
            var positions = new HashSet<int>();

            for (int seed = 0; seed < 40; seed++)
            {
                foreach (PairRound round in data.BuildRounds(5, 3, new Random(seed)))
                {
                    positions.Add(round.AnswerIndex);
                }
            }

            Assert.Greater(positions.Count, 1, "The correct answer must not always sit in the same place.");
        }

        [Test]
        public void FewerChoicesThanDistractorsIsStillAnswered()
        {
            PairsData data = Load();

            foreach (PairRound round in data.BuildRounds(5, 2, new Random(11)))
            {
                Assert.AreEqual(2, round.Choices.Count);
                Assert.AreEqual(round.Answer, round.Choices[round.AnswerIndex]);
            }
        }

        [Test]
        public void AnEmptyPairListIsReportedInsteadOfCrashing()
        {
            PairsData empty = PairsData.FromJsonText("[]", out string error);

            Assert.IsNull(error);
            Assert.IsFalse(empty.HasUsablePairs);

            List<PairRound> rounds = empty.BuildRounds(5, 3, new Random(1));
            Assert.IsNotNull(rounds);
            Assert.AreEqual(0, rounds.Count);
        }

        [Test]
        public void MalformedPairFilesReportAnError()
        {
            PairsData data = PairsData.FromJsonText("{ not json", out string error);

            Assert.IsNotNull(error);
            Assert.IsFalse(data.HasUsablePairs);
        }

        [Test]
        public void EntriesWithoutAnAntonymAreSkippedAndShortEntriesAreToppedUp()
        {
            PairsData data = PairsData.FromJsonText(@"[
                { ""word"": ""a"", ""opposite"": ""b"", ""wrong"": [""c"", ""d""] },
                { ""word"": ""broken"" },
                { ""word"": ""e"", ""opposite"": ""f"", ""wrong"": [""g""] }
            ]", out string error);

            Assert.IsNull(error);
            Assert.AreEqual(2, data.Pairs.Count, "Only the entry with no antonym should be dropped.");
            Assert.IsTrue(data.Warnings.Count >= 1, "Skipped entries should be explained.");

            // The entry with a single distractor still has to produce a full round: options are topped
            // up from the rest of the file rather than the round being dropped.
            foreach (PairRound round in data.BuildRounds(6, 3, new Random(5)))
            {
                Assert.AreEqual(3, round.Choices.Count, $"'{round.Word}' could not fill its options.");
                Assert.AreEqual(round.Answer, round.Choices[round.AnswerIndex]);
            }
        }
    }
}
