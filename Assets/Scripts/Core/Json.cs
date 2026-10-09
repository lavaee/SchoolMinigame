using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SmallPot
{
    /// <summary>Thrown when a data file cannot be parsed. Callers treat this as "this layer failed".</summary>
    public sealed class JsonParseException : Exception
    {
        public JsonParseException(string message) : base(message)
        {
        }
    }

    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object
    }

    /// <summary>
    /// A minimal, allocation-light JSON value.
    ///
    /// The project deliberately does not depend on Newtonsoft.Json or on JsonUtility: JsonUtility
    /// cannot represent the string dictionaries that strings.json needs, cannot parse the bare array
    /// that pairs.json contains, and reports malformed input by silently producing empty objects.
    /// The brief requires that a broken or partial data file degrades instead of breaking the game,
    /// so parsing errors have to be catchable and the accessors have to be total.
    /// </summary>
    public sealed class JsonValue
    {
        private static readonly JsonValue NullValue = new JsonValue(JsonKind.Null);

        private readonly JsonKind _kind;
        private readonly bool _bool;
        private readonly double _number;
        private readonly string _string;
        private readonly List<JsonValue> _array;
        private readonly Dictionary<string, JsonValue> _object;

        private JsonValue(JsonKind kind)
        {
            _kind = kind;
        }

        private JsonValue(bool value)
        {
            _kind = JsonKind.Bool;
            _bool = value;
        }

        private JsonValue(double value)
        {
            _kind = JsonKind.Number;
            _number = value;
        }

        private JsonValue(string value)
        {
            _kind = JsonKind.String;
            _string = value;
        }

        private JsonValue(List<JsonValue> value)
        {
            _kind = JsonKind.Array;
            _array = value;
        }

        private JsonValue(Dictionary<string, JsonValue> value)
        {
            _kind = JsonKind.Object;
            _object = value;
        }

        public JsonKind Kind => _kind;

        public bool IsNull => _kind == JsonKind.Null;

        public bool IsObject => _kind == JsonKind.Object;

        public bool IsArray => _kind == JsonKind.Array;

        public bool IsString => _kind == JsonKind.String;

        public bool IsNumber => _kind == JsonKind.Number;

        public bool IsBool => _kind == JsonKind.Bool;

        public int Count => _kind == JsonKind.Array ? _array.Count : _kind == JsonKind.Object ? _object.Count : 0;

        /// <summary>Indexer for arrays. Out-of-range returns JSON null rather than throwing.</summary>
        public JsonValue this[int index]
        {
            get
            {
                if (_kind != JsonKind.Array || index < 0 || index >= _array.Count)
                {
                    return NullValue;
                }

                return _array[index];
            }
        }

        /// <summary>Indexer for objects. A missing key returns JSON null rather than throwing.</summary>
        public JsonValue this[string key]
        {
            get
            {
                if (_kind != JsonKind.Object || key == null || !_object.TryGetValue(key, out JsonValue value))
                {
                    return NullValue;
                }

                return value;
            }
        }

        public bool Has(string key) => _kind == JsonKind.Object && key != null && _object.ContainsKey(key);

        public IEnumerable<string> Keys => _kind == JsonKind.Object ? (IEnumerable<string>)_object.Keys : Array.Empty<string>();

        public IEnumerable<JsonValue> Items => _kind == JsonKind.Array ? (IEnumerable<JsonValue>)_array : Array.Empty<JsonValue>();

        public string AsString(string fallback = null) => _kind == JsonKind.String ? _string : fallback;

        public double AsDouble(double fallback = 0d) => _kind == JsonKind.Number ? _number : fallback;

        public bool AsBool(bool fallback = false) => _kind == JsonKind.Bool ? _bool : fallback;

        /// <summary>
        /// Integer accessor that also accepts a numeric string, and rounds rather than truncating so
        /// that 5.999999 (a common JSON round-trip artefact) is read as 6.
        /// </summary>
        public int AsInt(int fallback)
        {
            if (_kind == JsonKind.Number)
            {
                return (int)Math.Round(_number, MidpointRounding.AwayFromZero);
            }

            if (_kind == JsonKind.String &&
                double.TryParse(_string, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                return (int)Math.Round(parsed, MidpointRounding.AwayFromZero);
            }

            return fallback;
        }

        public float AsFloat(float fallback)
        {
            if (_kind == JsonKind.Number)
            {
                return (float)_number;
            }

            if (_kind == JsonKind.String &&
                float.TryParse(_string, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                return parsed;
            }

            return fallback;
        }

        public static JsonValue Null() => NullValue;

        public static JsonValue FromString(string value) => value == null ? NullValue : new JsonValue(value);

        public static JsonValue FromArray(List<JsonValue> items) => new JsonValue(items ?? new List<JsonValue>());

        public static JsonValue FromObject(Dictionary<string, JsonValue> members) =>
            new JsonValue(members ?? new Dictionary<string, JsonValue>(StringComparer.Ordinal));

        /// <summary>Parses JSON text. Throws <see cref="JsonParseException"/> with a human-readable offset.</summary>
        public static JsonValue Parse(string text)
        {
            if (text == null)
            {
                throw new JsonParseException("Input was null.");
            }

            var reader = new Reader(text);
            reader.SkipWhitespace();
            JsonValue value = reader.ReadValue(0);
            reader.SkipWhitespace();
            if (!reader.AtEnd)
            {
                throw new JsonParseException($"Unexpected trailing content at offset {reader.Position}.");
            }

            return value;
        }

        public static bool TryParse(string text, out JsonValue value, out string error)
        {
            try
            {
                value = Parse(text);
                error = null;
                return true;
            }
            catch (JsonParseException exception)
            {
                value = NullValue;
                error = exception.Message;
                return false;
            }
        }

        public override string ToString() => _kind switch
        {
            JsonKind.Null => "null",
            JsonKind.Bool => _bool ? "true" : "false",
            JsonKind.Number => _number.ToString(CultureInfo.InvariantCulture),
            JsonKind.String => _string,
            JsonKind.Array => $"[{_array.Count} items]",
            _ => $"{{{_object.Count} members}}"
        };

        private struct Reader
        {
            private const int MaxDepth = 32;

            private readonly string _text;
            private int _index;

            public Reader(string text)
            {
                _text = text;
                _index = 0;
            }

            public int Position => _index;

            public bool AtEnd => _index >= _text.Length;

            public void SkipWhitespace()
            {
                while (_index < _text.Length)
                {
                    char c = _text[_index];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    {
                        _index++;
                        continue;
                    }

                    // Tolerate a UTF-8 byte order mark and stray non-breaking spaces from editors.
                    if (c == '\uFEFF' || c == '\u00A0')
                    {
                        _index++;
                        continue;
                    }

                    break;
                }
            }

            public JsonValue ReadValue(int depth)
            {
                if (depth > MaxDepth)
                {
                    throw new JsonParseException("Maximum nesting depth exceeded.");
                }

                SkipWhitespace();
                if (AtEnd)
                {
                    throw new JsonParseException("Unexpected end of input while reading a value.");
                }

                char c = _text[_index];
                switch (c)
                {
                    case '{':
                        return ReadObject(depth);
                    case '[':
                        return ReadArray(depth);
                    case '"':
                        return new JsonValue(ReadString());
                    case 't':
                        Expect("true");
                        return new JsonValue(true);
                    case 'f':
                        Expect("false");
                        return new JsonValue(false);
                    case 'n':
                        Expect("null");
                        return NullValue;
                    default:
                        return new JsonValue(ReadNumber());
                }
            }

            private JsonValue ReadObject(int depth)
            {
                _index++; // '{'
                var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                SkipWhitespace();
                if (!AtEnd && _text[_index] == '}')
                {
                    _index++;
                    return new JsonValue(members);
                }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_index] != '"')
                    {
                        throw new JsonParseException($"Expected an object key at offset {_index}.");
                    }

                    string key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || _text[_index] != ':')
                    {
                        throw new JsonParseException($"Expected ':' after key '{key}' at offset {_index}.");
                    }

                    _index++;
                    members[key] = ReadValue(depth + 1);
                    SkipWhitespace();

                    if (AtEnd)
                    {
                        throw new JsonParseException($"Unterminated object starting near key '{key}'.");
                    }

                    if (_text[_index] == ',')
                    {
                        _index++;
                        continue;
                    }

                    if (_text[_index] == '}')
                    {
                        _index++;
                        return new JsonValue(members);
                    }

                    throw new JsonParseException($"Expected ',' or '}}' at offset {_index}.");
                }
            }

            private JsonValue ReadArray(int depth)
            {
                _index++; // '['
                var items = new List<JsonValue>();
                SkipWhitespace();
                if (!AtEnd && _text[_index] == ']')
                {
                    _index++;
                    return new JsonValue(items);
                }

                while (true)
                {
                    items.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw new JsonParseException("Unterminated array.");
                    }

                    if (_text[_index] == ',')
                    {
                        _index++;
                        continue;
                    }

                    if (_text[_index] == ']')
                    {
                        _index++;
                        return new JsonValue(items);
                    }

                    throw new JsonParseException($"Expected ',' or ']' at offset {_index}.");
                }
            }

            private string ReadString()
            {
                _index++; // opening quote
                var builder = new StringBuilder();

                while (true)
                {
                    if (AtEnd)
                    {
                        throw new JsonParseException("Unterminated string.");
                    }

                    char c = _text[_index++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (AtEnd)
                    {
                        throw new JsonParseException("Unterminated escape sequence.");
                    }

                    char escape = _text[_index++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _text.Length)
                            {
                                throw new JsonParseException("Truncated \\u escape.");
                            }

                            string hex = _text.Substring(_index, 4);
                            if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
                            {
                                throw new JsonParseException($"Invalid \\u escape '{hex}'.");
                            }

                            builder.Append((char)code);
                            _index += 4;
                            break;
                        default:
                            throw new JsonParseException($"Unknown escape '\\{escape}' at offset {_index}.");
                    }
                }
            }

            private double ReadNumber()
            {
                int start = _index;
                while (_index < _text.Length)
                {
                    char c = _text[_index];
                    if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                    {
                        _index++;
                        continue;
                    }

                    break;
                }

                string token = _text.Substring(start, _index - start);
                if (token.Length == 0 ||
                    !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    throw new JsonParseException($"Invalid number '{token}' at offset {start}.");
                }

                return value;
            }

            private void Expect(string literal)
            {
                if (_index + literal.Length > _text.Length ||
                    string.CompareOrdinal(_text, _index, literal, 0, literal.Length) != 0)
                {
                    throw new JsonParseException($"Expected '{literal}' at offset {_index}.");
                }

                _index += literal.Length;
            }
        }
    }
}
