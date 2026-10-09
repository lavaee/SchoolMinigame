using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace SmallPot
{
    /// <summary>Lightweight logging with a single prefix, so console output is easy to filter.</summary>
    public static class Log
    {
        public const string Prefix = "[SmallPot] ";

        /// <summary>Set by the debug panel so a release build stays quiet.</summary>
        public static bool Verbose = true;

        public static void Info(string message)
        {
            if (Verbose)
            {
                Debug.Log(Prefix + message);
            }
        }

        public static void Warn(string message) => Debug.LogWarning(Prefix + message);

        public static void Error(string message) => Debug.LogError(Prefix + message);
    }

    /// <summary>Everything that survives closing the app.</summary>
    [Serializable]
    public sealed class SaveState
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public PlantState Plant = new PlantState();
        public bool ReduceMotion;
        public string ServerUrlOverride = string.Empty;
        public int MorningCount;

        public SaveState Clone() => new SaveState
        {
            Version = Version,
            Plant = Plant?.Clone() ?? new PlantState(),
            ReduceMotion = ReduceMotion,
            ServerUrlOverride = ServerUrlOverride,
            MorningCount = MorningCount
        };
    }

    /// <summary>
    /// Reads and writes save.json in Application.persistentDataPath.
    ///
    /// Written through a temporary file and then moved into place, so a process death mid-write cannot
    /// leave a truncated file behind. A file that fails to parse is reported and replaced by defaults
    /// rather than crashing the game.
    /// </summary>
    public static class SaveStore
    {
        public const string FileName = "save.json";

        public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

        public static bool TryLoad(out SaveState state, out string error)
        {
            error = null;
            state = new SaveState();

            try
            {
                string path = SavePath;
                if (!File.Exists(path))
                {
                    return false;
                }

                string text = File.ReadAllText(path, Encoding.UTF8);
                if (!JsonValue.TryParse(text, out JsonValue root, out string parseError))
                {
                    error = $"save.json could not be read ({parseError}); starting a new game.";
                    return false;
                }

                state = FromJson(root);
                return true;
            }
            catch (Exception exception)
            {
                error = $"save.json could not be read ({exception.Message}); starting a new game.";
                state = new SaveState();
                return false;
            }
        }

        public static bool Save(SaveState state, out string error)
        {
            error = null;
            if (state == null)
            {
                error = "Nothing to save.";
                return false;
            }

            try
            {
                string path = SavePath;
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string temporary = path + ".tmp";
                File.WriteAllText(temporary, ToJson(state), new UTF8Encoding(false));

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temporary, path);
                return true;
            }
            catch (Exception exception)
            {
                error = $"Could not write save.json: {exception.Message}";
                return false;
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    File.Delete(SavePath);
                }
            }
            catch (Exception exception)
            {
                Log.Warn($"Could not delete save.json: {exception.Message}");
            }
        }

        public static SaveState FromJson(JsonValue root)
        {
            var state = new SaveState();
            if (root == null || !root.IsObject)
            {
                return state;
            }

            state.Version = root["version"].AsInt(SaveState.CurrentVersion);
            state.ReduceMotion = root["reduceMotion"].AsBool(false);
            state.ServerUrlOverride = root["serverUrlOverride"].AsString(string.Empty) ?? string.Empty;
            state.MorningCount = Math.Max(0, root["morningCount"].AsInt(0));

            JsonValue plant = root["plant"];
            if (plant.IsObject)
            {
                state.Plant.Drops = Math.Max(0, plant["drops"].AsInt(0));
                state.Plant.GrowthIndex = Math.Max(0, plant["growthIndex"].AsInt(0));
                state.Plant.ProgressMornings = Math.Max(0, plant["progressMornings"].AsInt(0));
                state.Plant.DryMornings = Math.Max(0, plant["dryMornings"].AsInt(0));
                state.Plant.Thirsty = plant["thirsty"].AsBool(false);
                state.Plant.Watered = plant["watered"].AsBool(false);
            }

            return state;
        }

        public static string ToJson(SaveState state)
        {
            PlantState plant = state.Plant ?? new PlantState();
            var builder = new StringBuilder(256);
            builder.Append("{\n");
            builder.Append("  \"version\": ").Append(SaveState.CurrentVersion).Append(",\n");
            builder.Append("  \"reduceMotion\": ").Append(state.ReduceMotion ? "true" : "false").Append(",\n");
            builder.Append("  \"serverUrlOverride\": \"").Append(Escape(state.ServerUrlOverride)).Append("\",\n");
            builder.Append("  \"morningCount\": ").Append(state.MorningCount).Append(",\n");
            builder.Append("  \"plant\": {\n");
            builder.Append("    \"drops\": ").Append(plant.Drops).Append(",\n");
            builder.Append("    \"growthIndex\": ").Append(plant.GrowthIndex).Append(",\n");
            builder.Append("    \"progressMornings\": ").Append(plant.ProgressMornings).Append(",\n");
            builder.Append("    \"dryMornings\": ").Append(plant.DryMornings).Append(",\n");
            builder.Append("    \"thirsty\": ").Append(plant.Thirsty ? "true" : "false").Append(",\n");
            builder.Append("    \"watered\": ").Append(plant.Watered ? "true" : "false").Append("\n");
            builder.Append("  }\n");
            builder.Append("}\n");
            return builder.ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("X4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Clamps a loaded state against the active config, so a save written while a different
        /// config.json was installed can never index past the end of the stage list.
        /// </summary>
        public static void ClampToConfig(SaveState state, GameConfig config)
        {
            if (state?.Plant == null || config == null)
            {
                return;
            }

            state.Plant.GrowthIndex = Mathf.Clamp(state.Plant.GrowthIndex, 0, Mathf.Max(0, config.StageCount - 1));
            int required = state.Plant.RequiredMorningsForNext(config);
            state.Plant.ProgressMornings = required <= 0
                ? 0
                : Mathf.Clamp(state.Plant.ProgressMornings, 0, required);
            state.Plant.DryMornings = Mathf.Max(0, state.Plant.DryMornings);
            state.Plant.Drops = Mathf.Max(0, state.Plant.Drops);
        }
    }
}
