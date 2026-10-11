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

        /// <summary>
        /// How long the server is asked to hold a content watch open, in seconds.
        ///
        /// The app no longer asks "has anything changed?" on a timer. It asks once, and the server answers
        /// when a push happens - or when this many seconds have passed, whichever comes first. So this is
        /// the ceiling on how stale an idle app's knowledge can be; the floor is one round trip, which is
        /// why a push now feels immediate.
        /// </summary>
        public float UpdateWatchHoldSeconds = 20f;

        /// <summary>
        /// The feel of the sound, from data/dev.json rather than from the scene.
        ///
        /// These live here for the same reason the connection timings do: they are the numbers worth
        /// adjusting while listening, and this file can be pushed from the admin page into a running game.
        /// Nothing about the sound needs a rebuild to retune.
        /// </summary>
        public AudioSettings Audio = new AudioSettings();

        /// <summary>
        /// The interface's colours and icons, also from data/dev.json.
        ///
        /// They live here rather than in the supplied config.json, which a test pins byte for byte because
        /// it is the employer's file. Every value is a string and an absent one means "keep the colour the
        /// game already uses", so adding a key is the only way to change anything - and removing it again
        /// puts things back.
        /// </summary>
        public UiSettings Ui = new UiSettings();

        /// <summary>
        /// The shapes of the animations, from data/dev.json.
        ///
        /// Same reasoning as the audio: these are the numbers worth adjusting while watching the game run,
        /// and this file can be pushed into a live build, so the feel can be retuned without a rebuild.
        /// The AnimationCurve fields on the day/night component are deliberately not here - a curve belongs
        /// in the Inspector, where it can be shaped by hand.
        /// </summary>
        public JuiceSettings Juice = new JuiceSettings();

        /// <summary>How the morning transition moves the sun. Durations and angles only; curves stay in code.</summary>
        public DayNightSettings DayNight = new DayNightSettings();

        /// <summary>
        /// How long the result of an answer stays on screen before the next question, with the shared clock
        /// stopped. This belongs here rather than in the supplied config.json, which is hash-pinned and
        /// cannot be touched; the supplied feedback holds still apply on top of it.
        /// </summary>
        public float FeedbackPauseSeconds = 1f;

        [Serializable]
        public sealed class JuiceSettings
        {
            public float PanelSeconds = 0.36f;
            public float PanelFrom = 0.88f;
            public float ButtonPressedScale = 0.95f;
            public float ButtonReleaseOvershoot = 0.035f;
            public float RevealSeconds = 0.4f;
            public float RevealDelaySeconds = 0.25f;
            public float PunchStrength = 0.18f;
            public float JellyStrength = 0.05f;

            public static JuiceSettings FromJson(JsonValue node)
            {
                var juice = new JuiceSettings();
                if (node == null || !node.IsObject)
                {
                    return juice;
                }

                var ignored = new List<string>();
                juice.PanelSeconds = Range(GameConfig.ReadFloat(node, "panelSeconds", juice.PanelSeconds, ignored), 0f, 3f);
                juice.PanelFrom = Range(GameConfig.ReadFloat(node, "panelFrom", juice.PanelFrom, ignored), 0.1f, 1.5f);
                juice.ButtonPressedScale = Range(GameConfig.ReadFloat(node, "buttonPressedScale", juice.ButtonPressedScale, ignored), 0.5f, 1f);
                juice.ButtonReleaseOvershoot = Range(GameConfig.ReadFloat(node, "buttonReleaseOvershoot", juice.ButtonReleaseOvershoot, ignored), 0f, 0.5f);
                juice.RevealSeconds = Range(GameConfig.ReadFloat(node, "revealSeconds", juice.RevealSeconds, ignored), 0f, 5f);
                juice.RevealDelaySeconds = Range(GameConfig.ReadFloat(node, "revealDelaySeconds", juice.RevealDelaySeconds, ignored), 0f, 5f);
                juice.PunchStrength = Range(GameConfig.ReadFloat(node, "punchStrength", juice.PunchStrength, ignored), 0f, 1f);
                juice.JellyStrength = Range(GameConfig.ReadFloat(node, "jellyStrength", juice.JellyStrength, ignored), 0f, 0.5f);
                return juice;
            }

            private static float Range(float value, float low, float high) =>
                value < low ? low : (value > high ? high : value);
        }

        [Serializable]
        public sealed class DayNightSettings
        {
            public float DurationSeconds = 3f;
            public float Degrees = 360f;
            public float PeakIntensity = 1.15f;
            public float StartIntensity = 0.55f;

            public static DayNightSettings FromJson(JsonValue node)
            {
                var dayNight = new DayNightSettings();
                if (node == null || !node.IsObject)
                {
                    return dayNight;
                }

                var ignored = new List<string>();
                dayNight.DurationSeconds = Range(GameConfig.ReadFloat(node, "durationSeconds", dayNight.DurationSeconds, ignored), 0.2f, 30f);
                dayNight.Degrees = Range(GameConfig.ReadFloat(node, "degrees", dayNight.Degrees, ignored), 0f, 720f);
                dayNight.PeakIntensity = Range(GameConfig.ReadFloat(node, "peakIntensity", dayNight.PeakIntensity, ignored), 0f, 8f);
                dayNight.StartIntensity = Range(GameConfig.ReadFloat(node, "startIntensity", dayNight.StartIntensity, ignored), 0f, 8f);
                return dayNight;
            }

            private static float Range(float value, float low, float high) =>
                value < low ? low : (value > high ? high : value);
        }

        [Serializable]
        public sealed class UiSettings
        {
            /// <summary>Used when nothing has been read yet, so a view never has to check for null.</summary>
            public static readonly UiSettings Default = new UiSettings();

            public string AnswerCorrectBackground = string.Empty;
            public string AnswerCorrectLabel = string.Empty;
            public string AnswerWrongBackground = string.Empty;
            public string AnswerWrongLabel = string.Empty;
            public string AnswerRevealBackground = string.Empty;
            public string AnswerRevealLabel = string.Empty;

            public string CorrectIcon = "check-128";
            public string CorrectTint = string.Empty;
            public string WrongIcon = "cross-128";
            public string WrongTint = string.Empty;
            public string RewardMetIcon = "droplet-128";
            public string RewardMetTint = string.Empty;
            public string RewardMissedIcon = "leaf-128";
            public string RewardMissedTint = string.Empty;

            /// <summary>
            /// TMP character spacing for the two cards whose labels are generated rather than authored, so
            /// there is nothing in the scene to set it on. Negative tightens the Persian joins, which is what
            /// a hand-tuned heading wants. The range is wide enough to be useful and narrow enough that a
            /// mistyped value cannot scatter the letters off the card.
            /// </summary>
            public float CharacterSpacing = -5f;

            public static UiSettings FromJson(JsonValue node)
            {
                var ui = new UiSettings();
                if (node == null || !node.IsObject)
                {
                    return ui;
                }

                ui.AnswerCorrectBackground = Text(node, "answerCorrectBackground", ui.AnswerCorrectBackground);
                ui.AnswerCorrectLabel = Text(node, "answerCorrectLabel", ui.AnswerCorrectLabel);
                ui.AnswerWrongBackground = Text(node, "answerWrongBackground", ui.AnswerWrongBackground);
                ui.AnswerWrongLabel = Text(node, "answerWrongLabel", ui.AnswerWrongLabel);
                ui.AnswerRevealBackground = Text(node, "answerRevealBackground", ui.AnswerRevealBackground);
                ui.AnswerRevealLabel = Text(node, "answerRevealLabel", ui.AnswerRevealLabel);
                ui.CorrectIcon = Text(node, "correctIcon", ui.CorrectIcon);
                ui.CorrectTint = Text(node, "correctTint", ui.CorrectTint);
                ui.WrongIcon = Text(node, "wrongIcon", ui.WrongIcon);
                ui.WrongTint = Text(node, "wrongTint", ui.WrongTint);
                ui.RewardMetIcon = Text(node, "rewardMetIcon", ui.RewardMetIcon);
                ui.RewardMetTint = Text(node, "rewardMetTint", ui.RewardMetTint);
                ui.RewardMissedIcon = Text(node, "rewardMissedIcon", ui.RewardMissedIcon);
                ui.RewardMissedTint = Text(node, "rewardMissedTint", ui.RewardMissedTint);

                // The only numeric knob in this block, and the only route to it: neither card's labels exist
                // in the scene until they have been authored, so a value typed into the Inspector would be
                // overwritten on the next build. Warnings are dropped the same way this block's neighbours
                // drop them - it describes appearance, and a malformed value falls back rather than stopping.
                var ignored = new List<string>();
                ui.CharacterSpacing = Range(
                    GameConfig.ReadFloat(node, "characterSpacing", ui.CharacterSpacing, ignored), -20f, 20f);

                return ui;
            }

            private static float Range(float value, float low, float high) =>
                value < low ? low : (value > high ? high : value);

            private static string Text(JsonValue node, string key, string fallback)
            {
                if (!node.Has(key))
                {
                    return fallback;
                }

                string value = node[key].AsString(fallback);
                return value == null ? fallback : value.Trim();
            }
        }

        /// <summary>Volumes, variation and the ambience crossfade. Every value is clamped on the way in.</summary>
        [Serializable]
        public sealed class AudioSettings
        {
            public float SfxVolume = 0.8f;
            public float MusicVolume = 0.6f;
            public float PitchVariation = 0.03f;
            public float RepeatWindowSeconds = 0.04f;
            public float CrossfadeSeconds = 1.5f;
            public float DuckLevel = 0.35f;
            public float DuckSeconds = 0.35f;
            public bool LogPlays = true;

            public static AudioSettings FromJson(JsonValue node)
            {
                var audio = new AudioSettings();
                if (node == null || !node.IsObject)
                {
                    return audio;
                }

                var ignored = new List<string>();
                audio.SfxVolume = Unit(GameConfig.ReadFloat(node, "sfxVolume", audio.SfxVolume, ignored));
                audio.MusicVolume = Unit(GameConfig.ReadFloat(node, "musicVolume", audio.MusicVolume, ignored));
                audio.DuckLevel = Unit(GameConfig.ReadFloat(node, "duckLevel", audio.DuckLevel, ignored));

                audio.PitchVariation = Clamp(
                    GameConfig.ReadFloat(node, "pitchVariation", audio.PitchVariation, ignored), 0f, 0.2f);
                audio.RepeatWindowSeconds = Clamp(
                    GameConfig.ReadFloat(node, "repeatWindowSeconds", audio.RepeatWindowSeconds, ignored), 0f, 0.5f);
                audio.CrossfadeSeconds = Clamp(
                    GameConfig.ReadFloat(node, "crossfadeSeconds", audio.CrossfadeSeconds, ignored), 0f, 10f);
                audio.DuckSeconds = Clamp(
                    GameConfig.ReadFloat(node, "duckSeconds", audio.DuckSeconds, ignored), 0f, 5f);

                if (node.Has("logPlays"))
                {
                    audio.LogPlays = GameConfig.ReadBool(node, "logPlays", audio.LogPlays, ignored);
                }

                return audio;
            }

            private static float Unit(float value) => Clamp(value, 0f, 1f);

            private static float Clamp(float value, float low, float high) =>
                value < low ? low : (value > high ? high : value);
        }

        /// <summary>
        /// The settings last read from data/dev.json.
        ///
        /// Static because the interface needs them long after the loader has finished with them: a view is
        /// handed a configuration built from the supplied config.json, and these live in our own file.
        /// </summary>
        public static DevSettings Current { get; private set; }

        public static DevSettings FromJson(JsonValue root)
        {
            var settings = new DevSettings();
            Current = settings;
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

            if (root.Has("updateWatchHoldSeconds"))
            {
                float seconds = GameConfig.ReadFloat(
                    root, "updateWatchHoldSeconds", settings.UpdateWatchHoldSeconds, new List<string>());
                if (seconds >= 2f)
                {
                    settings.UpdateWatchHoldSeconds = seconds;
                }
            }

            settings.Audio = AudioSettings.FromJson(root["audio"]);
            settings.Ui = UiSettings.FromJson(root["ui"]);
            settings.Juice = JuiceSettings.FromJson(root["juice"]);
            settings.DayNight = DayNightSettings.FromJson(root["dayNight"]);

            float pause = GameConfig.ReadFloat(root, "feedbackPauseSeconds", settings.FeedbackPauseSeconds, new List<string>());
            settings.FeedbackPauseSeconds = pause < 0f ? 0f : (pause > 10f ? 10f : pause);

            // Published straight to the animation helpers, so a pushed change to the feel of the interface
            // takes effect on the next tap rather than at the next launch.
            // Fully qualified on purpose: the Juice field on this class shadows the Juice type, so a bare
            // Juice.Apply here binds to the field and the compiler rightly refuses it.
            SmallPot.Juice.Apply(settings.Juice);

            return settings;
        }
    }
}
