using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SmallPot
{
    /// <summary>
    /// Strongly typed view of data/config.json.
    ///
    /// Every field is optional: the file may be replaced by the employer (or edited live during the
    /// presentation), so a missing, misspelled or wrongly typed value must fall back to the default
    /// recorded here rather than throwing. Anything that was defaulted or ignored is reported through
    /// <see cref="Warnings"/> so the debug panel can show it instead of failing silently.
    /// </summary>
    [Serializable]
    public sealed class GameConfig
    {
        public const string ConfigFileName = "config.json";

        public int StartDrops = 3;

        public MiniGameConfig MiniGame = new MiniGameConfig();
        public WateringConfig Watering = new WateringConfig();
        public GrowthConfig Growth = new GrowthConfig();
        public ThirstyConfig Thirsty = new ThirstyConfig();
        public UiConfig Ui = new UiConfig();

        /// <summary>Human-readable notes about values that were missing or invalid.</summary>
        public readonly List<string> Warnings = new List<string>();

        public static GameConfig Defaults() => new GameConfig();

        public static GameConfig FromJson(JsonValue root)
        {
            var config = new GameConfig();
            if (root == null || !root.IsObject)
            {
                config.Warnings.Add($"{ConfigFileName}: root is not an object; using defaults.");
                return config;
            }

            config.StartDrops = ReadInt(root, "startDrops", config.StartDrops, config.Warnings);

            config.MiniGame = MiniGameConfig.FromJson(root["miniGame"], config.Warnings);
            config.Watering = WateringConfig.FromJson(root["watering"], config.Warnings);
            config.Growth = GrowthConfig.FromJson(root["growth"], config.Warnings);
            config.Thirsty = ThirstyConfig.FromJson(root["thirsty"], config.Warnings);
            config.Ui = UiConfig.FromJson(root["ui"], config.Warnings);

            return config;
        }

        public static GameConfig FromJsonText(string text)
        {
            if (!JsonValue.TryParse(text, out JsonValue root, out string error))
            {
                var failed = new GameConfig();
                failed.Warnings.Add($"{ConfigFileName}: {error} Using defaults.");
                return failed;
            }

            return FromJson(root);
        }

        // ---- Derived values -------------------------------------------------------------------
        // The supplied config.json defines two animation numbers. Every other duration the game needs
        // is derived from them, so no extra key has to exist for the game to behave correctly.

        /// <summary>Hard cap for any single animation, from ui.maxAnimationMs.</summary>
        public float MaxAnimationSeconds => Math.Max(0f, Ui.MaxAnimationMs / 1000f);

        /// <summary>
        /// How long a correct answer stays on screen. Half the animation budget: long enough to read,
        /// short enough that five rounds inside the shared 60 second timer still feel brisk.
        /// </summary>
        public float CorrectFeedbackSeconds => MaxAnimationSeconds * 0.5f;

        /// <summary>
        /// How long a wrong answer stays on screen. The full budget, because this screen has to be
        /// read: the kind message and the separately shown correct answer must both be legible.
        /// </summary>
        public float WrongFeedbackSeconds => Math.Max(MaxAnimationSeconds, CorrectFeedbackSeconds + 0.35f);

        /// <summary>Duration used for grow / water / stage swap transitions.</summary>
        public float TransitionSeconds => MaxAnimationSeconds;

        public float NumberCountUpSeconds => Math.Max(0f, Ui.NumberCountUpMs / 1000f);

        public int StageCount => Growth.Stages.Count;

        public string StageAt(int index)
        {
            if (Growth.Stages.Count == 0)
            {
                return "seed";
            }

            return Growth.Stages[Math.Max(0, Math.Min(Growth.Stages.Count - 1, index))];
        }

        public bool IsLastStage(int index) => index >= Growth.Stages.Count - 1;

        internal static int ReadInt(JsonValue parent, string key, int fallback, List<string> warnings)
        {
            JsonValue value = parent[key];
            if (value.IsNull)
            {
                return fallback;
            }

            if (value.IsNumber || (value.IsString && value.AsString() != null))
            {
                return value.AsInt(fallback);
            }

            warnings.Add($"{ConfigFileName}: '{key}' is not a number; using {fallback}.");
            return fallback;
        }

        internal static float ReadFloat(JsonValue parent, string key, float fallback, List<string> warnings)
        {
            JsonValue value = parent[key];
            if (value.IsNull)
            {
                return fallback;
            }

            if (value.IsNumber || (value.IsString && value.AsString() != null))
            {
                return value.AsFloat(fallback);
            }

            warnings.Add($"{ConfigFileName}: '{key}' is not a number; using {fallback}.");
            return fallback;
        }

        internal static bool ReadBool(JsonValue parent, string key, bool fallback, List<string> warnings)
        {
            JsonValue value = parent[key];
            if (value.IsNull)
            {
                return fallback;
            }

            if (value.IsBool)
            {
                return value.AsBool(fallback);
            }

            warnings.Add($"{ConfigFileName}: '{key}' is not a boolean; using {fallback}.");
            return fallback;
        }
    }

    [Serializable]
    public sealed class MiniGameConfig
    {
        public int Rounds = 5;
        public int ChoicesShown = 3;
        public int TotalSeconds = 60;
        public int MinCorrectForReward = 3;
        public int DropsPerCorrect = 1;
        public int BonusDropsIfAllCorrect = 2;

        public static MiniGameConfig FromJson(JsonValue node, List<string> warnings)
        {
            var config = new MiniGameConfig();
            if (!node.IsObject)
            {
                if (!node.IsNull)
                {
                    warnings.Add($"{GameConfig.ConfigFileName}: 'miniGame' is not an object; using defaults.");
                }

                return config;
            }

            config.Rounds = Math.Max(1, GameConfig.ReadInt(node, "rounds", config.Rounds, warnings));
            config.ChoicesShown = Math.Max(2, GameConfig.ReadInt(node, "choicesShown", config.ChoicesShown, warnings));
            config.TotalSeconds = Math.Max(5, GameConfig.ReadInt(node, "totalSeconds", config.TotalSeconds, warnings));
            config.MinCorrectForReward = GameConfig.ReadInt(node, "minCorrectForReward", config.MinCorrectForReward, warnings);
            config.DropsPerCorrect = Math.Max(0, GameConfig.ReadInt(node, "dropsPerCorrect", config.DropsPerCorrect, warnings));
            config.BonusDropsIfAllCorrect = Math.Max(0, GameConfig.ReadInt(node, "bonusDropsIfAllCorrect", config.BonusDropsIfAllCorrect, warnings));
            return config;
        }

        /// <summary>Drops earned for a finished round set. Never negative, never below zero drops.</summary>
        public int RewardFor(int correctAnswers)
        {
            int clamped = Math.Max(0, correctAnswers);
            int reward = clamped * DropsPerCorrect;
            if (clamped >= Rounds)
            {
                reward += BonusDropsIfAllCorrect;
            }

            return Math.Max(0, reward);
        }

        public bool MeetsRewardThreshold(int correctAnswers) => correctAnswers >= MinCorrectForReward;
    }

    [Serializable]
    public sealed class WateringConfig
    {
        public int CostDrops = 4;

        public static WateringConfig FromJson(JsonValue node, List<string> warnings)
        {
            var config = new WateringConfig();
            if (!node.IsObject)
            {
                if (!node.IsNull)
                {
                    warnings.Add($"{GameConfig.ConfigFileName}: 'watering' is not an object; using defaults.");
                }

                return config;
            }

            config.CostDrops = Math.Max(0, GameConfig.ReadInt(node, "costDrops", config.CostDrops, warnings));
            return config;
        }
    }

    [Serializable]
    public sealed class GrowthConfig
    {
        /// <summary>Ordered stage ids. "thirsty" belongs to no list: it is an overlay, not a stage.</summary>
        public List<string> Stages = new List<string> { "seed", "sprout", "plant", "bloom" };

        /// <summary>Transition name ("seedToSprout") to the number of watered mornings it needs.</summary>
        public readonly Dictionary<string, int> Mornings = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Optional generic fallback for transitions the file does not name.</summary>
        public int DefaultMorningsPerTransition = 1;

        public static GrowthConfig FromJson(JsonValue node, List<string> warnings)
        {
            var config = new GrowthConfig();
            if (!node.IsObject)
            {
                if (!node.IsNull)
                {
                    warnings.Add($"{GameConfig.ConfigFileName}: 'growth' is not an object; using defaults.");
                }

                return config;
            }

            JsonValue stages = node["stages"];
            if (stages.IsArray)
            {
                var parsed = new List<string>();
                foreach (JsonValue item in stages.Items)
                {
                    string id = item.AsString();
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        warnings.Add($"{GameConfig.ConfigFileName}: 'growth.stages' contains a non-string entry; it was skipped.");
                        continue;
                    }

                    id = id.Trim();
                    if (parsed.Contains(id))
                    {
                        warnings.Add($"{GameConfig.ConfigFileName}: 'growth.stages' repeats '{id}'; the repeat was skipped.");
                        continue;
                    }

                    parsed.Add(id);
                }

                if (parsed.Count >= 2)
                {
                    config.Stages = parsed;
                }
                else
                {
                    warnings.Add($"{GameConfig.ConfigFileName}: 'growth.stages' needs at least two stages; using defaults.");
                }
            }
            else if (!stages.IsNull)
            {
                warnings.Add($"{GameConfig.ConfigFileName}: 'growth.stages' is not an array; using defaults.");
            }

            JsonValue mornings = node["mornings"];
            if (mornings.IsObject)
            {
                foreach (string key in mornings.Keys)
                {
                    JsonValue value = mornings[key];
                    if (!value.IsNumber && !value.IsString)
                    {
                        warnings.Add($"{GameConfig.ConfigFileName}: 'growth.mornings.{key}' is not a number; it was skipped.");
                        continue;
                    }

                    config.Mornings[key] = Math.Max(1, value.AsInt(1));
                }
            }
            else if (!mornings.IsNull)
            {
                warnings.Add($"{GameConfig.ConfigFileName}: 'growth.mornings' is not an object; every transition needs 1 watered morning.");
            }

            config.DefaultMorningsPerTransition = Math.Max(
                1,
                GameConfig.ReadInt(node, "defaultMorningsPerTransition", config.DefaultMorningsPerTransition, warnings));

            return config;
        }

        /// <summary>
        /// Watered mornings required for the transition between two stage ids.
        ///
        /// The supplied file names its transitions "seedToSprout", "sproutToPlant", "plantToBloom".
        /// That naming cannot cover a stage added later, so the lookup is derived from the stage ids
        /// and falls back in this order: exact camel-case name, snake-case name, a generic default,
        /// then 1. Adding a sixth stage therefore needs no code change.
        /// </summary>
        public int MorningsFor(string fromStageId, string toStageId)
        {
            if (string.IsNullOrEmpty(fromStageId) || string.IsNullOrEmpty(toStageId))
            {
                return DefaultMorningsPerTransition;
            }

            if (Mornings.TryGetValue(TransitionKey(fromStageId, toStageId), out int camel) ||
                Mornings.TryGetValue($"{fromStageId}_to_{toStageId}", out camel) ||
                Mornings.TryGetValue($"{fromStageId}To{toStageId}", out camel))
            {
                return Math.Max(1, camel);
            }

            return Math.Max(1, DefaultMorningsPerTransition);
        }

        /// <summary>Transition key as spelled in the supplied file, e.g. "seed" + "To" + "Sprout".</summary>
        public static string TransitionKey(string fromStageId, string toStageId)
        {
            if (string.IsNullOrEmpty(toStageId))
            {
                return fromStageId;
            }

            string capitalized = char.ToUpperInvariant(toStageId[0]) + toStageId.Substring(1);
            return fromStageId + "To" + capitalized;
        }
    }

    [Serializable]
    public sealed class ThirstyConfig
    {
        public int AfterMorningsWithoutWater = 1;

        public static ThirstyConfig FromJson(JsonValue node, List<string> warnings)
        {
            var config = new ThirstyConfig();
            if (!node.IsObject)
            {
                if (!node.IsNull)
                {
                    warnings.Add($"{GameConfig.ConfigFileName}: 'thirsty' is not an object; using defaults.");
                }

                return config;
            }

            config.AfterMorningsWithoutWater = Math.Max(
                1,
                GameConfig.ReadInt(node, "afterMorningsWithoutWater", config.AfterMorningsWithoutWater, warnings));
            return config;
        }
    }

    [Serializable]
    public sealed class UiConfig
    {
        public int MaxAnimationMs = 600;
        public int NumberCountUpMs = 400;

        public static UiConfig FromJson(JsonValue node, List<string> warnings)
        {
            var config = new UiConfig();
            if (!node.IsObject)
            {
                if (!node.IsNull)
                {
                    warnings.Add($"{GameConfig.ConfigFileName}: 'ui' is not an object; using defaults.");
                }

                return config;
            }

            config.MaxAnimationMs = Math.Max(0, GameConfig.ReadInt(node, "maxAnimationMs", config.MaxAnimationMs, warnings));
            config.NumberCountUpMs = Math.Max(0, GameConfig.ReadInt(node, "numberCountUpMs", config.NumberCountUpMs, warnings));
            return config;
        }
    }

    /// <summary>Key/value bag for data/dev.json, the project's own (non-gameplay) knobs.</summary>
    [Serializable]
    public sealed class DevSettings
    {
        public const string FileName = "dev.json";

        /// <summary>Base URL of the data server. Empty means "bundled data only".</summary>
        public string DataServerUrl = string.Empty;

        /// <summary>
        /// Every address worth trying, in order. A phone reaches the same server at a different address
        /// depending on how it is connected - the machine's address on the network over Wi-Fi, and
        /// 127.0.0.1 when the port is forwarded over USB - so the build bakes both and the game keeps
        /// whichever one answers. Empty means <see cref="DataServerUrl"/> alone is used.
        /// </summary>
        public List<string> DataServerUrls = new List<string>();

        public bool ShowDataSourceInfo = true;
        public bool ShowDebugPanel = true;

        /// <summary>
        /// When true the game will not start until a data server answers, and shows a retry prompt instead.
        ///
        /// Off is the brief's posture: the bundled files are a floor, so a missing server is not an error.
        /// On is this project's posture for a demo that has to prove content really comes from the server -
        /// silence is the one thing a failed connection must never be.
        /// </summary>
        public bool RequireServer;

        /// <summary>
        /// How often a build that insists on a server checks that it is still there, in seconds.
        ///
        /// The check is one small request, and a connection that drops while the game is being played has
        /// to be noticed then rather than at the next launch, so it is deliberately short.
        /// </summary>
        public float ConnectionCheckSeconds = 3f;

        public static DevSettings FromJson(JsonValue root)
        {
            var settings = new DevSettings();
            if (root == null || !root.IsObject)
            {
                return settings;
            }

            settings.DataServerUrl = (root["dataServerUrl"].AsString(string.Empty) ?? string.Empty).Trim();

            JsonValue addresses = root["dataServerUrls"];
            if (addresses.IsArray)
            {
                for (int i = 0; i < addresses.Count; i++)
                {
                    string address = (addresses[i].AsString(string.Empty) ?? string.Empty).Trim();
                    if (address.Length > 0)
                    {
                        settings.DataServerUrls.Add(address);
                    }
                }
            }
            if (root.Has("showDataSourceInfo"))
            {
                settings.ShowDataSourceInfo = GameConfig.ReadBool(root, "showDataSourceInfo", settings.ShowDataSourceInfo, new List<string>());
            }

            if (root.Has("showDebugPanel"))
            {
                settings.ShowDebugPanel = GameConfig.ReadBool(root, "showDebugPanel", settings.ShowDebugPanel, new List<string>());
            }

            // Read on its own: a file that insists on a server and says nothing about the debug panel is a
            // perfectly reasonable file, and silently ignoring the flag would be the worst kind of bug.
            if (root.Has("requireServer"))
            {
                settings.RequireServer = GameConfig.ReadBool(root, "requireServer", settings.RequireServer, new List<string>());
            }

            if (root.Has("connectionCheckSeconds"))
            {
                float seconds = GameConfig.ReadFloat(
                    root, "connectionCheckSeconds", settings.ConnectionCheckSeconds, new List<string>());
                if (seconds >= 1f)
                {
                    settings.ConnectionCheckSeconds = seconds;
                }
            }

            return settings;
        }
    }
}
