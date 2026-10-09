using System.Text;
using RTLTMPro;

namespace SmallPot
{
    /// <summary>
    /// Persian text helpers.
    ///
    /// Shaping is *not* done here: the UI uses the RTLTextMeshPro component from RTLTMPro, which
    /// converts logical Persian into Arabic presentation forms and reverses the flow when the text is
    /// assigned. This class owns the one thing that is easier to control centrally, which is digits,
    /// because "all numbers are rendered as Persian digits" is a rule the brief asks for and a rule
    /// the data files cannot express on their own.
    /// </summary>
    public static class PersianText
    {
        public const char ZeroWidthNoJoiner = '\u200C';

        public const char PersianZero = '\u06F0';
        public const char PersianNine = '\u06F9';
        public const char ArabicZero = '\u0660';
        public const char ArabicNine = '\u0669';

        /// <summary>True for locales that are written right to left.</summary>
        public static bool IsRightToLeftLocale(string locale)
        {
            if (string.IsNullOrEmpty(locale))
            {
                return false;
            }

            string normalized = locale.Trim().ToLowerInvariant();
            int separator = normalized.IndexOfAny(new[] { '-', '_', '.' });
            string language = separator > 0 ? normalized.Substring(0, separator) : normalized;

            switch (language)
            {
                case "fa":
                case "ar":
                case "he":
                case "ur":
                case "ps":
                case "sd":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Replaces ASCII digits with Persian digits (U+06F0-U+06F9). Everything else is untouched.</summary>
        public static string ToPersianDigits(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var builder = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                builder.Append(c >= '0' && c <= '9' ? (char)(PersianZero + (c - '0')) : c);
            }

            return builder.ToString();
        }

        /// <summary>Replaces Persian and Arabic-Indic digits with ASCII digits.</summary>
        public static string ToLatinDigits(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var builder = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c >= PersianZero && c <= PersianNine)
                {
                    builder.Append((char)('0' + (c - PersianZero)));
                }
                else if (c >= ArabicZero && c <= ArabicNine)
                {
                    builder.Append((char)('0' + (c - ArabicZero)));
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        public static string FormatNumber(int value, bool persianDigits)
        {
            string text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return persianDigits ? ToPersianDigits(text) : text;
        }

        /// <summary>
        /// Applies the house style to a logical string: digits become Persian when the locale asks for
        /// it. The result is still logical text and is safe to assign to an RTLTextMeshPro component.
        /// </summary>
        public static string ToDisplayText(string logicalText, bool persianDigits) =>
            persianDigits ? ToPersianDigits(logicalText) : logicalText;

        /// <summary>Number of Arabic-script letters that are still in a base (unshaped) block.</summary>
        public static int CountUnshapedArabicLetters(string shapedText)
        {
            if (string.IsNullOrEmpty(shapedText))
            {
                return 0;
            }

            int count = 0;
            foreach (char c in shapedText)
            {
                if (c < 0x0600 || c > 0x06FF)
                {
                    continue;
                }

                bool allowed =
                    (c >= PersianZero && c <= PersianNine) ||
                    (c >= ArabicZero && c <= ArabicNine) ||
                    c == '\u060C' || // Arabic comma
                    c == '\u061B' || // Arabic semicolon
                    c == '\u061F' || // Arabic question mark
                    c == '\u0640';   // tatweel

                // Arabic diacritics (tashkeel) legitimately stay in the base block.
                bool tashkeel = c >= 0x064B && c <= 0x065F;

                if (!allowed && !tashkeel)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Shapes logical text into presentation forms, the way the RTLTextMeshPro component does.
        /// Exposed for tests; the running UI relies on the component itself.
        /// </summary>
        public static string Shape(string logicalText, bool farsi = true, bool fixTags = true, bool preserveNumbers = true)
        {
            if (string.IsNullOrEmpty(logicalText))
            {
                return logicalText;
            }

            var builder = new FastStringBuilder(logicalText.Length + 16);
            RTLSupport.FixRTL(logicalText, builder, farsi, fixTags, preserveNumbers);
            builder.Reverse();
            return builder.ToString();
        }
    }
}