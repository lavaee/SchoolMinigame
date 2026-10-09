using System;
using System.Collections.Generic;
using System.Text;

namespace SmallPot
{
    /// <summary>
    /// Player-visible text, resolved through the chain the brief describes.
    ///
    /// Lookup order for every key:
    ///   1. strings.json        - the override file; this is the one edited during the live demo
    ///   2. strings-base.json   - the complete default set supplied by the employer
    ///   3. strings-extra.json  - this project's own defaults for UI it adds (settings/debug chrome)
    ///   4. a visible "[key]" marker, recorded in <see cref="MissingKeys"/>
    ///
    /// A missing key never throws and never blanks the screen: the game keeps running with the marker
    /// so the omission is obvious in a debug build and harmless in the judged flow, which only uses
    /// keys from strings-base.json.
    /// </summary>
    public sealed class StringsStore
    {
        public const string OverrideFileName = "strings.json";
        public const string BaseFileName = "strings-base.json";
        public const string ExtraFileName = "strings-extra.json";

        private readonly Dictionary<string, string> _override = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _base = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _extra = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _missing = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _missingOrdered = new List<string>();
        private readonly List<string> _warnings = new List<string>();

        private readonly Dictionary<string, string> _displayCache = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Locale declared by the highest-priority strings file that sets one.</summary>
        public string Locale { get; private set; } = "fa";

        public bool PersianDigits => PersianText.IsRightToLeftLocale(Locale);

        public IReadOnlyList<string> MissingKeys => _missingOrdered;

        public IReadOnlyList<string> Warnings => _warnings;

        public int Count => _override.Count + _base.Count + _extra.Count;

        public static StringsStore CreateEmpty() => new StringsStore();

        /// <summary>
        /// Merges one parsed strings file into the requested layer. Later calls for the same layer
        /// overwrite earlier ones, which is what a reload should do.
        /// </summary>
        public void Merge(string fileName, JsonValue root)
        {
            Dictionary<string, string> layer = LayerFor(fileName);
            if (layer == null)
            {
                _warnings.Add($"Unknown strings file '{fileName}' was ignored.");
                return;
            }

            if (root == null || !root.IsObject)
            {
                _warnings.Add($"{fileName}: root is not an object.");
                return;
            }

            // Layers are merged lowest priority first (strings-extra, strings-base, strings.json), so
            // the last locale seen is automatically the highest-priority one.
            string locale = root["locale"].AsString();
            if (!string.IsNullOrEmpty(locale))
            {
                Locale = locale;
            }

            JsonValue strings = root["strings"];
            if (!strings.IsObject)
            {
                _warnings.Add($"{fileName}: no 'strings' object was found.");
                return;
            }

            _displayCache.Clear();
            foreach (string key in strings.Keys)
            {
                string value = strings[key].AsString();
                if (value == null)
                {
                    _warnings.Add($"{fileName}: key '{key}' is not a string; it was skipped.");
                    continue;
                }

                layer[key] = value;
                _missing.Remove(key);
            }
        }

        private Dictionary<string, string> LayerFor(string fileName)
        {
            if (string.Equals(fileName, OverrideFileName, StringComparison.OrdinalIgnoreCase))
            {
                return _override;
            }

            if (string.Equals(fileName, BaseFileName, StringComparison.OrdinalIgnoreCase))
            {
                return _base;
            }

            if (string.Equals(fileName, ExtraFileName, StringComparison.OrdinalIgnoreCase))
            {
                return _extra;
            }

            return null;
        }

        public bool Has(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            return _override.ContainsKey(key) || _base.ContainsKey(key) || _extra.ContainsKey(key);
        }

        /// <summary>Raw logical text for a key. Never returns null.</summary>
        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            if (_override.TryGetValue(key, out string value) ||
                _base.TryGetValue(key, out value) ||
                _extra.TryGetValue(key, out value))
            {
                return value;
            }

            if (_missing.Add(key))
            {
                _missingOrdered.Add(key);
            }

            return "[" + key + "]";
        }

        public string GetOrFallback(string key, string fallback) => Has(key) ? Get(key) : fallback;

        // ---- Formatting ------------------------------------------------------------------------
        // The supplied strings use named placeholders: {n}, {word}, {answer}, {state}.

        public string Format(string key, IReadOnlyDictionary<string, string> arguments)
        {
            string template = Get(key);
            if (arguments == null || arguments.Count == 0 || template.IndexOf('{') < 0)
            {
                return template;
            }

            var builder = new StringBuilder(template.Length + 16);
            for (int i = 0; i < template.Length; i++)
            {
                char c = template[i];
                if (c != '{')
                {
                    builder.Append(c);
                    continue;
                }

                int close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    builder.Append(c);
                    continue;
                }

                string name = template.Substring(i + 1, close - i - 1);
                if (arguments.TryGetValue(name, out string replacement))
                {
                    builder.Append(replacement);
                    i = close;
                }
                else
                {
                    // Unknown placeholder: keep it verbatim so the mistake is visible rather than silent.
                    builder.Append(template, i, close - i + 1);
                    i = close;
                }
            }

            return builder.ToString();
        }

        /// <summary>Formats "{n}" with a number, applying Persian digits when the locale asks for them.</summary>
        public string FormatNumber(string key, int value) =>
            Format(key, new Dictionary<string, string>(1, StringComparer.Ordinal)
            {
                { "n", PersianText.FormatNumber(value, PersianDigits) }
            });

        public string FormatWord(string key, string word) =>
            Format(key, new Dictionary<string, string>(1, StringComparer.Ordinal) { { "word", word } });

        public string FormatAnswer(string key, string answer) =>
            Format(key, new Dictionary<string, string>(1, StringComparer.Ordinal) { { "answer", answer } });

        /// <summary>Formats "{state}" with the label for another key, e.g. pot.state.sprout.</summary>
        public string FormatState(string key, string stateLabel) =>
            Format(key, new Dictionary<string, string>(1, StringComparer.Ordinal) { { "state", stateLabel } });

        /// <summary>Label for a pot state id, falling back to the id itself for an unknown stage.</summary>
        public string StateLabel(string stateId)
        {
            if (string.IsNullOrEmpty(stateId))
            {
                return string.Empty;
            }

            string key = "pot.state." + stateId;
            return Has(key) ? Get(key) : stateId;
        }

        /// <summary>
        /// Text ready to be assigned to an RTLTextMeshPro component: the house digit style applied,
        /// still logical (the component performs the shaping and the flow reversal).
        /// </summary>
        public string Display(string logicalText)
        {
            if (string.IsNullOrEmpty(logicalText) || !PersianDigits)
            {
                return logicalText;
            }

            if (_displayCache.TryGetValue(logicalText, out string cached))
            {
                return cached;
            }

            string converted = PersianText.ToDisplayText(logicalText, true);
            if (_displayCache.Count > 512)
            {
                _displayCache.Clear();
            }

            _displayCache[logicalText] = converted;
            return converted;
        }

        /// <summary>Formats a number and applies the digit style in one step, for HUD values.</summary>
        public string DisplayNumber(int value) =>
            PersianText.FormatNumber(value, PersianDigits);

        public void ClearWarnings() => _warnings.Clear();
    }
}
