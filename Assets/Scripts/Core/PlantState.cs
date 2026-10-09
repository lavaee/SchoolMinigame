using System;

namespace SmallPot
{
    /// <summary>What a morning did, which decides the short report card.</summary>
    public enum MorningOutcome
    {
        /// <summary>The plant moved on to the next stage.</summary>
        Grew,

        /// <summary>The plant was thirsty and has recovered, but has not grown yet.</summary>
        Recovered,

        /// <summary>The night passed without water, so the pot is thirsty. Growth is untouched.</summary>
        Thirsty,

        /// <summary>Watered, but this transition needs more mornings, or the plant is already in bloom.</summary>
        Nothing
    }

    /// <summary>Why the water button can or cannot be used.</summary>
    public enum WaterBlock
    {
        Allowed,
        AlreadyWatered,
        NotEnoughDrops,
        AtFullBloom
    }

    /// <summary>
    /// The whole game state, and the only place where time advances.
    ///
    /// Growth is stored as an index into config.growth.stages plus a count of watered mornings, and
    /// thirst is a separate overlay flag. That structure is what makes the rule in the brief
    /// ("thirst takes no growth back, it only blocks the next growth") impossible to violate: nothing
    /// about becoming thirsty can touch GrowthIndex.
    /// </summary>
    [Serializable]
    public sealed class PlantState
    {
        /// <summary>
        /// Display-only state id for thirst. It is deliberately not part of config.growth.stages,
        /// because thirst is a condition the plant is in, not a point in its life cycle.
        /// </summary>
        public const string ThirstyStageId = "thirsty";

        public int Drops;
        public int GrowthIndex;
        public int ProgressMornings;
        public int DryMornings;
        public bool Thirsty;
        public bool Watered;

        public static PlantState NewGame(GameConfig config)
        {
            var state = new PlantState();
            state.ResetTo(config);
            return state;
        }

        public void ResetTo(GameConfig config)
        {
            Drops = config?.StartDrops ?? 3;
            GrowthIndex = 0;
            ProgressMornings = 0;
            DryMornings = 0;
            Thirsty = false;
            Watered = false;
        }

        public string StageId(GameConfig config) => config.StageAt(GrowthIndex);

        /// <summary>The state shown to the player: thirst masks the growth stage without replacing it.</summary>
        public string DisplayStateId(GameConfig config) => Thirsty ? ThirstyStageId : StageId(config);

        public bool AtFullBloom(GameConfig config) => config.IsLastStage(GrowthIndex);

        /// <summary>Watered mornings still needed for the next stage; 0 once fully bloomed.</summary>
        public int RequiredMorningsForNext(GameConfig config)
        {
            if (AtFullBloom(config) || config.StageCount < 2)
            {
                return 0;
            }

            return config.Growth.MorningsFor(StageId(config), config.StageAt(GrowthIndex + 1));
        }

        /// <summary>0..1 progress toward the next stage, used to make each watered morning visible.</summary>
        public float ProgressTowardNext(GameConfig config)
        {
            int required = RequiredMorningsForNext(config);
            if (required <= 0)
            {
                return 1f;
            }

            float value = ProgressMornings / (float)required;
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }

        /// <param name="allowWateringAtFullBloom">
        /// When false, watering is blocked once the plant is fully bloomed so the player is never
        /// charged for a morning that cannot change anything. The caller sets this from whether the
        /// optional hint string exists, so the rule stays data-driven.
        /// </param>
        public WaterBlock CanWater(GameConfig config, bool allowWateringAtFullBloom = false)
        {
            if (Watered)
            {
                return WaterBlock.AlreadyWatered;
            }

            if (AtFullBloom(config) && !allowWateringAtFullBloom)
            {
                return WaterBlock.AtFullBloom;
            }

            if (Drops < config.Watering.CostDrops)
            {
                return WaterBlock.NotEnoughDrops;
            }

            return WaterBlock.Allowed;
        }

        public bool TryWater(GameConfig config, bool allowWateringAtFullBloom = false)
        {
            if (CanWater(config, allowWateringAtFullBloom) != WaterBlock.Allowed)
            {
                return false;
            }

            Drops -= config.Watering.CostDrops;
            Watered = true;
            return true;
        }

        /// <summary>
        /// Applies one "next morning". This is the only method that moves the plant forward.
        /// </summary>
        public MorningOutcome ApplyMorning(GameConfig config)
        {
            if (!Watered)
            {
                DryMornings++;
                if (DryMornings >= config.Thirsty.AfterMorningsWithoutWater)
                {
                    Thirsty = true;
                }

                // Growth index and progress are untouched: thirst never takes growth back.
                return Thirsty ? MorningOutcome.Thirsty : MorningOutcome.Nothing;
            }

            bool wasThirsty = Thirsty;
            Watered = false;
            DryMornings = 0;
            Thirsty = false;

            if (AtFullBloom(config))
            {
                return MorningOutcome.Nothing;
            }

            ProgressMornings++;
            int required = RequiredMorningsForNext(config);
            if (ProgressMornings >= required)
            {
                ProgressMornings = 0;
                GrowthIndex = Math.Min(GrowthIndex + 1, config.StageCount - 1);
                return MorningOutcome.Grew;
            }

            return wasThirsty ? MorningOutcome.Recovered : MorningOutcome.Nothing;
        }

        public PlantState Clone() => new PlantState
        {
            Drops = Drops,
            GrowthIndex = GrowthIndex,
            ProgressMornings = ProgressMornings,
            DryMornings = DryMornings,
            Thirsty = Thirsty,
            Watered = Watered
        };
    }
}
