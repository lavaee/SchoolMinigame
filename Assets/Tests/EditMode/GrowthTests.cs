using System.Collections.Generic;
using NUnit.Framework;
using SmallPot;

namespace SmallPot.Tests
{
    /// <summary>
    /// Growth happens at the next morning, never instantly, and thirst never takes growth back. These
    /// tests walk the whole state machine with the supplied configuration.
    /// </summary>
    public sealed class GrowthTests
    {
        private GameConfig _config;

        [SetUp]
        public void SetUp() => _config = RepoData.Config();

        private PlantState NewGame() => PlantState.NewGame(_config);

        [Test]
        public void ANewGameStartsWithJustTheSeedAndTheStartingDrops()
        {
            PlantState state = NewGame();

            Assert.AreEqual(_config.StartDrops, state.Drops);
            Assert.AreEqual(0, state.GrowthIndex);
            Assert.AreEqual("seed", state.StageId(_config));
            Assert.IsFalse(state.Thirsty);
            Assert.IsFalse(state.Watered);
            Assert.IsFalse(state.AtFullBloom(_config));
        }

        [Test]
        public void WateringIsTheOnlyThingThatPreparesTheNextMorning()
        {
            PlantState state = NewGame();
            Assert.AreEqual(WaterBlock.NotEnoughDrops, state.CanWater(_config));
            Assert.IsFalse(state.TryWater(_config));
            Assert.AreEqual(_config.StartDrops, state.Drops, "A refused watering must not charge drops.");
            Assert.IsFalse(state.Watered);
        }

        [Test]
        public void AnUnwateredMorningMakesThePotThirstyAndKeepsItsGrowth()
        {
            PlantState state = NewGame();
            state.GrowthIndex = 1;
            state.ProgressMornings = 1;

            MorningOutcome outcome = state.ApplyMorning(_config);

            Assert.AreEqual(MorningOutcome.Thirsty, outcome);
            Assert.IsTrue(state.Thirsty);
            Assert.AreEqual(1, state.GrowthIndex, "Thirst must not take growth back.");
            Assert.AreEqual(1, state.ProgressMornings, "Thirst must not take progress back.");
        }

        [Test]
        public void WateringDoesNothingOnItsOwn()
        {
            PlantState state = NewGame();
            state.Drops = 10;
            Assert.AreEqual(WaterBlock.Allowed, state.CanWater(_config));
            Assert.IsTrue(state.TryWater(_config));

            Assert.AreEqual(10 - _config.Watering.CostDrops, state.Drops);
            Assert.IsTrue(state.Watered);
            Assert.AreEqual(0, state.GrowthIndex, "Watering prepares the morning; it does not grow the plant.");
        }

        [Test]
        public void WateringTwiceInOneDayIsRefused()
        {
            PlantState state = NewGame();
            state.Drops = 10;
            state.TryWater(_config);

            Assert.AreEqual(WaterBlock.AlreadyWatered, state.CanWater(_config));
            Assert.IsFalse(state.TryWater(_config));
            Assert.AreEqual(10 - _config.Watering.CostDrops, state.Drops);
        }

        [Test]
        public void AWateredMorningGrowsThePlantOnceTheRequiredMorningsPass()
        {
            PlantState state = NewGame();
            state.Drops = 100;

            Assert.IsTrue(state.TryWater(_config));
            Assert.AreEqual(MorningOutcome.Grew, state.ApplyMorning(_config));
            Assert.AreEqual(1, state.GrowthIndex);
            Assert.AreEqual("sprout", state.StageId(_config));
            Assert.IsFalse(state.Watered, "The watering is consumed by the morning.");
            Assert.IsFalse(state.Thirsty);
        }

        [Test]
        public void GrowthStagesNeedTheirConfiguredNumberOfWateredMornings()
        {
            PlantState state = NewGame();
            state.Drops = 100;

            // seed -> sprout needs 1, sprout -> plant 2, plant -> bloom 2.
            Assert.AreEqual(1, state.RequiredMorningsForNext(_config));
            state.TryWater(_config);
            state.ApplyMorning(_config);
            Assert.AreEqual(1, state.GrowthIndex);

            Assert.AreEqual(2, state.RequiredMorningsForNext(_config));
            state.TryWater(_config);
            Assert.AreEqual(MorningOutcome.Nothing, state.ApplyMorning(_config));
            Assert.AreEqual(1, state.GrowthIndex, "One of two mornings is not enough to advance.");
            Assert.AreEqual(1, state.ProgressMornings);

            state.TryWater(_config);
            Assert.AreEqual(MorningOutcome.Grew, state.ApplyMorning(_config));
            Assert.AreEqual(2, state.GrowthIndex);
            Assert.AreEqual(0, state.ProgressMornings, "Progress resets when a stage is reached.");
        }

        [Test]
        public void FiveWateredMorningsReachFullBloom()
        {
            PlantState state = NewGame();
            state.Drops = 100;

            for (int i = 0; i < 5; i++)
            {
                Assert.IsTrue(state.TryWater(_config), $"Watering was refused on morning {i + 1}.");
                state.ApplyMorning(_config);
            }

            Assert.AreEqual(3, state.GrowthIndex);
            Assert.AreEqual("bloom", state.StageId(_config));
            Assert.IsTrue(state.AtFullBloom(_config));
            Assert.AreEqual(1f, state.ProgressTowardNext(_config), 0.0001f);
        }

        [Test]
        public void AThirstyPotRecoversWhenItIsWateredAgain()
        {
            PlantState state = NewGame();
            state.Drops = 100;

            state.TryWater(_config);
            state.ApplyMorning(_config);
            Assert.AreEqual(1, state.GrowthIndex);

            // Skip a night: the pot gets thirsty but keeps its stage and progress.
            state.ApplyMorning(_config);
            Assert.IsTrue(state.Thirsty);
            Assert.AreEqual(1, state.GrowthIndex);

            state.TryWater(_config);
            Assert.AreEqual(MorningOutcome.Recovered, state.ApplyMorning(_config));
            Assert.IsFalse(state.Thirsty);
            Assert.AreEqual(1, state.GrowthIndex, "Recovering is not the same as growing.");
            Assert.AreEqual(1, state.ProgressMornings);
        }

        [Test]
        public void AfterRecoveringTheNextWateredMorningGrowsAgain()
        {
            PlantState state = NewGame();
            state.Drops = 100;

            state.TryWater(_config);
            state.ApplyMorning(_config);        // seed -> sprout
            state.ApplyMorning(_config);        // thirsty
            state.TryWater(_config);
            state.ApplyMorning(_config);        // recovered, progress 1 of 2
            state.TryWater(_config);

            Assert.AreEqual(MorningOutcome.Grew, state.ApplyMorning(_config));
            Assert.AreEqual(2, state.GrowthIndex);
        }

        [Test]
        public void AtFullBloomThereIsNothingLeftToDo()
        {
            PlantState state = NewGame();
            state.Drops = 100;
            state.GrowthIndex = 3;
            state.Watered = true;

            Assert.AreEqual(MorningOutcome.Nothing, state.ApplyMorning(_config));
            Assert.AreEqual(3, state.GrowthIndex);
            Assert.AreEqual(0, state.RequiredMorningsForNext(_config));
        }

        [Test]
        public void WateringAtFullBloomIsBlockedByDefault()
        {
            PlantState state = NewGame();
            state.Drops = 100;
            state.GrowthIndex = 3;

            Assert.AreEqual(WaterBlock.AtFullBloom, state.CanWater(_config));
            Assert.AreEqual(WaterBlock.Allowed, state.CanWater(_config, true));
            Assert.IsFalse(state.TryWater(_config));
            Assert.AreEqual(100, state.Drops);
        }

        [Test]
        public void CloningDoesNotShareState()
        {
            PlantState state = NewGame();
            PlantState copy = state.Clone();
            copy.GrowthIndex = 2;
            copy.Drops = 99;

            Assert.AreEqual(0, state.GrowthIndex);
            Assert.AreEqual(_config.StartDrops, state.Drops);
        }

        [Test]
        public void TheFiveDisplayedStatesAreDistinguishableWithoutReadingText()
        {
            // Seed, sprout, plant, bloom and thirsty. Colour is never the only signifier, so this checks
            // the silhouette: height, leaf pairs, flower, soil wetness, posture and badges.
            PlantState seed = NewGame();

            PlantState sprout = NewGame();
            sprout.GrowthIndex = 1;

            PlantState plant = NewGame();
            plant.GrowthIndex = 2;

            PlantState bloom = NewGame();
            bloom.GrowthIndex = 3;

            PlantState thirsty = NewGame();
            thirsty.GrowthIndex = 1;
            thirsty.Thirsty = true;

            var signatures = new Dictionary<string, string>();
            foreach (KeyValuePair<string, PlantState> pair in new[]
            {
                new KeyValuePair<string, PlantState>("seed", seed),
                new KeyValuePair<string, PlantState>("sprout", sprout),
                new KeyValuePair<string, PlantState>("plant", plant),
                new KeyValuePair<string, PlantState>("bloom", bloom),
                new KeyValuePair<string, PlantState>("thirsty", thirsty)
            })
            {
                PotVisual visual = PotVisual.Resolve(_config, pair.Value);
                Assert.AreEqual(pair.Key, pair.Value.DisplayStateId(_config));

                string signature = string.Join("|", new[]
                {
                    visual.Height.ToString("0.000"),
                    visual.LeafPairs.ToString(),
                    visual.LeafTiltDegrees.ToString("0.0"),
                    visual.Flower ? "flower" : "-",
                    visual.SoilDryness.ToString("0.00"),
                    visual.Droop ? "droop" : "-",
                    visual.ThirstyBadge ? "thirsty" : "-"
                });

                Assert.IsFalse(signatures.ContainsKey(signature),
                    $"'{pair.Key}' looks identical to '{signatures.GetValueOrDefault(signature, "?")}'.");
                signatures[signature] = pair.Key;
            }

            Assert.AreEqual(5, signatures.Count);
        }

        [Test]
        public void ThePlantGetsTallerWithEveryStage()
        {
            float previous = -1f;
            for (int index = 0; index < _config.StageCount; index++)
            {
                var state = new PlantState { GrowthIndex = index };
                float height = PotVisual.Resolve(_config, state).Height;
                Assert.Greater(height, previous, $"Stage {index} is not taller than the one before it.");
                previous = height;
            }
        }

        [Test]
        public void WateringShowsAWateredState()
        {
            PlantState state = NewGame();
            state.Drops = 10;
            state.TryWater(_config);

            PotVisual visual = PotVisual.Resolve(_config, state);
            Assert.IsTrue(visual.WateredBadge, "A watered pot has to be visibly watered before the morning.");
            Assert.AreEqual(0.05f, visual.SoilDryness, 0.0001f);
        }

        [Test]
        public void ThirstDriesTheSoilAndDroopsTheLeaves()
        {
            var wet = new PlantState { GrowthIndex = 1 };
            var dry = new PlantState { GrowthIndex = 1, Thirsty = true };

            PotVisual wetVisual = PotVisual.Resolve(_config, wet);
            PotVisual dryVisual = PotVisual.Resolve(_config, dry);

            Assert.IsTrue(dryVisual.Droop);
            Assert.IsFalse(wetVisual.Droop);
            Assert.Greater(dryVisual.SoilDryness, wetVisual.SoilDryness);
            Assert.Greater(dryVisual.LeafTiltDegrees, wetVisual.LeafTiltDegrees);
        }
    }
}
