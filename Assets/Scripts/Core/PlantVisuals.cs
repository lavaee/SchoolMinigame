using System;

namespace SmallPot
{
    /// <summary>
    /// The shape of one growth stage, expressed as a few numbers the pot rig turns into geometry.
    ///
    /// Stages are keyed by id, but an unknown id is not an error: it is resolved parametrically from
    /// its position in config.growth.stages. That is what allows a sixth stage to be added live
    /// during the presentation by editing only config.json and the string files.
    /// </summary>
    public struct StageVisualProfile
    {
        public string StageId;
        public float Height;
        public int LeafPairs;
        public float LeafTiltDegrees;
        public bool Flower;
        public float SoilDryness;

        public static StageVisualProfile For(string stageId, int index, int stageCount)
        {
            switch (stageId)
            {
                case "seed":
                    return Make(stageId, 0.10f, 0, 0f, false, 0.10f);
                case "sprout":
                    return Make(stageId, 0.35f, 1, 6f, false, 0.12f);
                case "plant":
                    return Make(stageId, 0.70f, 2, 0f, false, 0.14f);
                case "bloom":
                    return Make(stageId, 1.00f, 2, -4f, true, 0.14f);
                default:
                    return Parametric(stageId, index, stageCount);
            }
        }

        /// <summary>
        /// Fallback for a stage the code has never seen: taller and leafier the later it sits in the
        /// growth order, with a flower only at the very end. Deterministic, so it is testable.
        /// </summary>
        public static StageVisualProfile Parametric(string stageId, int index, int stageCount)
        {
            if (stageCount <= 1)
            {
                return Make(stageId, 1f, 2, 0f, true, 0.14f);
            }

            float t = Clamp01(index / (float)(stageCount - 1));
            float height = Lerp(0.16f, 1.05f, t);
            int leafPairs = (int)Math.Round(Lerp(0f, 3f, t), MidpointRounding.AwayFromZero);
            bool flower = index >= stageCount - 1;
            return Make(stageId, height, leafPairs, 0f, flower, 0.14f);
        }

        private static StageVisualProfile Make(
            string stageId,
            float height,
            int leafPairs,
            float tilt,
            bool flower,
            float soilDryness) => new StageVisualProfile
        {
            StageId = stageId,
            Height = height,
            LeafPairs = leafPairs,
            LeafTiltDegrees = tilt,
            Flower = flower,
            SoilDryness = soilDryness
        };

        private static float Lerp(float a, float b, float t) => a + ((b - a) * Clamp01(t));

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }

    /// <summary>
    /// Everything the pot has to show, resolved once per state change so the view stays a renderer.
    ///
    /// Two independent visual channels carry the state, and neither of them is colour:
    /// posture (drooping leaves when thirsty) and badges (a droplet when watered). Colour is only
    /// ever a reinforcement of those signals.
    /// </summary>
    public struct PotVisual
    {
        public string StageId;
        public float Height;
        public int LeafPairs;
        public float LeafTiltDegrees;
        public bool Flower;
        public float SoilDryness;
        public bool Droop;
        public bool WateredBadge;
        public bool ThirstyBadge;

        public static PotVisual Resolve(GameConfig config, PlantState state)
        {
            string stageId = state.StageId(config);
            int index = state.GrowthIndex;
            int count = config.StageCount;

            StageVisualProfile profile = StageVisualProfile.For(stageId, index, count);

            // Each watered morning nudges the plant toward the next stage, so a morning that does not
            // change the state label still visibly does something.
            float progress = state.ProgressTowardNext(config);
            float height = profile.Height;
            if (!state.AtFullBloom(config))
            {
                int nextIndex = Math.Min(index + 1, count - 1);
                StageVisualProfile next = StageVisualProfile.For(config.StageAt(nextIndex), nextIndex, count);
                height = profile.Height + ((next.Height - profile.Height) * progress);
            }

            float soilDryness = state.Watered
                ? 0.05f
                : state.Thirsty ? 0.90f : profile.SoilDryness;

            // Thirst has to be visible without relying on colour, so it also changes the silhouette:
            // the leaves hang down and the whole plant sits a little lower.
            float tilt = profile.LeafTiltDegrees + (state.Thirsty ? 26f : 0f);
            if (state.Thirsty)
            {
                height *= 0.92f;
            }

            return new PotVisual
            {
                StageId = stageId,
                Height = height,
                LeafPairs = profile.LeafPairs,
                LeafTiltDegrees = tilt,
                Flower = profile.Flower && progress > 0f,
                SoilDryness = soilDryness,
                Droop = state.Thirsty,
                WateredBadge = state.Watered,
                ThirstyBadge = state.Thirsty
            };
        }
    }
}
