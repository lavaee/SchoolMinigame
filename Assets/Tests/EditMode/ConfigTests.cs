using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SmallPot;
using UnityEngine;

namespace SmallPot.Tests
{
    /// <summary>
    /// The numbers in config.json drive the whole game, and the code must keep working when that file is
    /// replaced, truncated or emptied without a rebuild.
    /// </summary>
    public sealed class ConfigTests
    {
        [Test]
        public void SuppliedConfigIsReadAsWritten()
        {
            GameConfig config = RepoData.Config();

            Assert.AreEqual(3, config.StartDrops);
            Assert.AreEqual(5, config.MiniGame.Rounds);
            Assert.AreEqual(3, config.MiniGame.ChoicesShown);
            Assert.AreEqual(60, config.MiniGame.TotalSeconds);
            Assert.AreEqual(3, config.MiniGame.MinCorrectForReward);
            Assert.AreEqual(1, config.MiniGame.DropsPerCorrect);
            Assert.AreEqual(2, config.MiniGame.BonusDropsIfAllCorrect);
            Assert.AreEqual(4, config.Watering.CostDrops);
            Assert.AreEqual(1, config.Thirsty.AfterMorningsWithoutWater);
            Assert.AreEqual(600, config.Ui.MaxAnimationMs);
            Assert.AreEqual(400, config.Ui.NumberCountUpMs);

            CollectionAssert.AreEqual(
                new[] { "seed", "sprout", "plant", "bloom" },
                config.Growth.Stages);

            Assert.AreEqual(1, config.Growth.MorningsFor("seed", "sprout"));
            Assert.AreEqual(2, config.Growth.MorningsFor("sprout", "plant"));
            Assert.AreEqual(2, config.Growth.MorningsFor("plant", "bloom"));
        }

        [Test]
        public void ThirstIsNotAGrowthStage()
        {
            // Thirst is an overlay on a stage, never a stage of its own: it must not take growth back.
            GameConfig config = RepoData.Config();
            CollectionAssert.DoesNotContain(config.Growth.Stages, PlantState.ThirstyStageId);
            Assert.AreEqual(4, config.StageCount);
        }

        [Test]
        public void DerivedTimingsComeFromTheConfiguredBudget()
        {
            GameConfig config = RepoData.Config();

            Assert.AreEqual(0.6f, config.MaxAnimationSeconds, 0.0001f);
            Assert.AreEqual(0.3f, config.CorrectFeedbackSeconds, 0.0001f);
            Assert.AreEqual(0.6f, config.TransitionSeconds, 0.0001f);
            Assert.AreEqual(0.4f, config.NumberCountUpSeconds, 0.0001f);
            Assert.GreaterOrEqual(config.WrongFeedbackSeconds, config.CorrectFeedbackSeconds);
        }

        [Test]
        public void RewardPayoutFollowsTheConfiguredNumbers()
        {
            GameConfig config = RepoData.Config();

            Assert.AreEqual(0, config.MiniGame.RewardFor(0));
            Assert.AreEqual(3, config.MiniGame.RewardFor(3));
            Assert.AreEqual(4, config.MiniGame.RewardFor(4));
            Assert.AreEqual(5 + 2, config.MiniGame.RewardFor(5));

            Assert.IsFalse(config.MiniGame.MeetsRewardThreshold(2));
            Assert.IsTrue(config.MiniGame.MeetsRewardThreshold(3));
        }

        [Test]
        public void MissingSectionsKeepTheirDefaults()
        {
            GameConfig config = GameConfig.FromJsonText("{}");

            Assert.AreEqual(3, config.StartDrops);
            Assert.AreEqual(5, config.MiniGame.Rounds);
            Assert.AreEqual(4, config.Watering.CostDrops);
            Assert.AreEqual(4, config.StageCount);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("{ \"startDrops\": }")]
        [TestCase("not json at all")]
        [TestCase("[1,2,3]")]
        public void UnusableInputFallsBackToDefaultsInsteadOfThrowing(string text)
        {
            GameConfig config = GameConfig.FromJsonText(text);

            Assert.AreEqual(GameConfig.Defaults().StartDrops, config.StartDrops);
            Assert.AreEqual(GameConfig.Defaults().MiniGame.Rounds, config.MiniGame.Rounds);
            Assert.IsTrue(config.Warnings.Count > 0, "A fallback should be reported as a warning.");
        }

        [Test]
        public void WrongTypesFallBackPerField()
        {
            GameConfig config = GameConfig.FromJsonText(@"{
                ""startDrops"": ""lots"",
                ""miniGame"": { ""rounds"": ""five"", ""choicesShown"": 4 },
                ""growth"": { ""stages"": ""seed"" }
            }");

            Assert.AreEqual(3, config.StartDrops);
            Assert.AreEqual(5, config.MiniGame.Rounds);
            Assert.AreEqual(4, config.MiniGame.ChoicesShown);
            Assert.AreEqual(4, config.StageCount);
            Assert.IsTrue(config.Warnings.Count > 0);
        }

        [Test]
        public void ASixthStageNeedsNoCodeChange()
        {
            // The live demo adds a stage to config.json only; the lookup must still answer.
            GameConfig config = GameConfig.FromJsonText(@"{
                ""growth"": {
                    ""stages"": [""seed"", ""sprout"", ""plant"", ""bloom"", ""fruit""],
                    ""mornings"": { ""seedToSprout"": 1, ""sproutToPlant"": 1, ""plantToBloom"": 1, ""bloomToFruit"": 3 }
                }
            }");

            Assert.AreEqual(5, config.StageCount);
            Assert.AreEqual("fruit", config.StageAt(4));
            Assert.IsTrue(config.IsLastStage(4));
            Assert.IsFalse(config.IsLastStage(3));
            Assert.AreEqual(3, config.Growth.MorningsFor("bloom", "fruit"));
            Assert.AreEqual(1, config.Growth.DefaultMorningsPerTransition);
        }

        [Test]
        public void UnknownTransitionsUseTheDefault()
        {
            var growth = new GrowthConfig();
            Assert.AreEqual(1, growth.MorningsFor("bloom", "fruit"));
            Assert.AreEqual(1, growth.MorningsFor(null, "fruit"));
            Assert.AreEqual(1, growth.MorningsFor("bloom", null));
        }

        [Test]
        public void MorningsForAcceptsEitherSpelling()
        {
            GameConfig camel = GameConfig.FromJsonText(
                @"{ ""growth"": { ""stages"": [""a"", ""b""], ""mornings"": { ""aToB"": 4 } } }");
            GameConfig snake = GameConfig.FromJsonText(
                @"{ ""growth"": { ""stages"": [""a"", ""b""], ""mornings"": { ""a_to_b"": 4 } } }");

            Assert.AreEqual(4, camel.Growth.MorningsFor("a", "b"));
            Assert.AreEqual(4, snake.Growth.MorningsFor("a", "b"));
        }

        [Test]
        public void ZeroOrNegativeMorningCountsAreClampedToOne()
        {
            GameConfig config = GameConfig.FromJsonText(
                @"{ ""growth"": { ""stages"": [""a"", ""b""], ""mornings"": { ""aToB"": 0 } } }");

            Assert.AreEqual(1, config.Growth.MorningsFor("a", "b"));
        }

        [Test]
        public void DevSettingsDefaultsToNoServer()
        {
            var settings = DevSettings.FromJson(JsonValue.Parse("{}"));
            Assert.AreEqual(string.Empty, settings.DataServerUrl);
            Assert.IsFalse(string.IsNullOrEmpty(DevSettings.FileName));
        }

        [Test]
        public void DevSettingsReadsEveryBakedAddress()
        {
            // A build bakes the machine's address on the network and 127.0.0.1 for a USB forward, because
            // which one works depends on how the phone is connected. Blank entries are not addresses.
            var settings = DevSettings.FromJson(JsonValue.Parse(
                @"{ ""dataServerUrl"": ""http://192.168.1.20:8787""," +
                @" ""dataServerUrls"": [""http://192.168.1.20:8787"", """", "" http://127.0.0.1:8787 ""] }"));

            Assert.AreEqual("http://192.168.1.20:8787", settings.DataServerUrl);
            Assert.AreEqual(2, settings.DataServerUrls.Count);
            Assert.AreEqual("http://192.168.1.20:8787", settings.DataServerUrls[0]);
            Assert.AreEqual("http://127.0.0.1:8787", settings.DataServerUrls[1]);
        }

        [Test]
        public void CandidateUrlsPutsTheOverrideFirstAndDropsDuplicates()
        {
            var settings = new DevSettings
            {
                DataServerUrl = "http://192.168.1.20:8787",
                DataServerUrls = new List<string> { "http://192.168.1.20:8787/", "http://127.0.0.1:8787" }
            };

            List<string> urls = DataLoader.CandidateUrls("http://10.0.0.9:8787", settings);

            // The override from the settings panel wins, then the baked list in order, and the single
            // dataServerUrl adds nothing because it is already there.
            Assert.AreEqual(3, urls.Count);
            Assert.AreEqual("http://10.0.0.9:8787", urls[0]);
            Assert.AreEqual("http://192.168.1.20:8787", urls[1]);
            Assert.AreEqual("http://127.0.0.1:8787", urls[2]);
        }

        [Test]
        public void DevSettingsRequiresAServerOnlyWhenTheFileSaysSo()
        {
            // Insisting on a server is a build's decision, not a hard-wired one: with the flag off, a
            // missing server is the shrug the brief asks for; with it on, the game refuses to start rather
            // than quietly pretending everything is current.
            Assert.IsFalse(DevSettings.FromJson(JsonValue.Parse("{}")).RequireServer);
            Assert.IsFalse(DevSettings.FromJson(
                JsonValue.Parse("{\"dataServerUrl\":\"http://192.168.1.20:8787\"}")).RequireServer);
            Assert.IsTrue(DevSettings.FromJson(JsonValue.Parse("{\"requireServer\":true}")).RequireServer);
        }

        [Test]
        public void CandidateUrlsIsEmptyWhenNoServerIsConfigured()
        {
            // No address means no network attempts at all, which is what keeps a bundled-only build
            // from spending seconds on requests that can never succeed.
            Assert.AreEqual(0, DataLoader.CandidateUrls(string.Empty, new DevSettings()).Count);
            Assert.AreEqual(0, DataLoader.CandidateUrls(string.Empty, null).Count);
        }

        [Test]
        public void DataFilesAreByteIdenticalToTheOnesSupplied()
        {
            // The brief's files are the contract. If one is ever edited by accident, this fails loudly
            // rather than the game quietly changing behaviour.
            var expected = new Dictionary<string, string>
            {
                { "config.json", "dfcd784e007706d0903ee9231329295ae562e0f6d6e87df1097bf9e3259e7cf6" },
                { "pairs.json", "3d05c36f54e8677b41f32181d997298dfe91011eecd5fa308168cfdeb96e19a9" },
                { "strings-base.json", "693a2f631ad7d4ef8df9f16d1b2d356b99ecd856251373fcd049cb9a2f5b96d6" },
                { "strings.json", "03a608de6293e2b78409bd2b301fb89801823af9da6e55f423f0511ecf1010e5" }
            };

            foreach (KeyValuePair<string, string> pair in expected)
            {
                string hash = Sha256(File.ReadAllBytes(RepoData.PathOf(pair.Key)));
                Assert.AreEqual(pair.Value, hash, $"{pair.Key} differs from the supplied file.");
            }
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                var builder = new System.Text.StringBuilder(digest.Length * 2);
                foreach (byte b in digest)
                {
                    builder.Append(b.ToString("x2"));
                }

                return builder.ToString();
            }
        }
    }
}
