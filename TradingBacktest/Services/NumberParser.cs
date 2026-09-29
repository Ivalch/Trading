using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TradingBacktest.Services
{
    /// <summary>
    /// Parses and formats the cost-per-operation field.
    ///
    /// The setting is a percentage with two decimals ("0,10"). Parsing deliberately accepts
    /// both "," and "." as the decimal separator so the box works on machines whose culture
    /// differs from the one the value was typed in; formatting follows the current culture.
    /// </summary>
    public static class NumberParser
    {
        /// <summary>Two decimals, using the separator of the current culture ("," in ru-RU).</summary>
        public static string FormatPercent(double value)
        {
            return value.ToString("0.00", CultureInfo.CurrentCulture);
        }

        /// <summary>Money with two decimals, thousands separated by the current culture.</summary>
        public static string FormatMoney(double value)
        {
            return value.ToString("#,##0.00", CultureInfo.CurrentCulture);
        }

        public static string FormatSignedPercent(double value)
        {
            return (value >= 0 ? "+" : string.Empty) + value.ToString("0.00", CultureInfo.CurrentCulture) + " %";
        }

        /// <summary>
        /// Tries to read a non-negative percentage. Accepts "0.10", "0,10", " 0.1 % " and
        /// ".1". Rejects anything else so the view model can show a message instead of
        /// silently running a backtest on a nonsense cost.
        /// </summary>
        public static bool TryParsePercent(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string cleaned = text.Trim().Replace("%", string.Empty).Replace(" ", string.Empty);
            if (cleaned.Length == 0)
            {
                return false;
            }

            // Both separators map onto the invariant "." so the same string works in any culture.
            cleaned = cleaned.Replace(',', '.');

            double parsed;
            if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return false;
            }

            if (double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < 0 || parsed > 100)
            {
                return false;
            }

            value = parsed;
            return true;
        }

        /// <summary>Normalises a folder path for comparison (trailing separators removed).</summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            return path.Trim().TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }

        /// <summary>True when the folder exists and can be listed.</summary>
        public static bool IsUsableDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                return System.IO.Directory.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Shortens a long path so it fits into the text box without breaking layout.</summary>
        public static string Ellipsize(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text;
            }

            int keep = Math.Max(4, (maxLength - 3) / 2);
            return text.Substring(0, keep) + "..." + text.Substring(text.Length - keep);
        }

        private static readonly Regex InvalidFileNameChars = new Regex(@"[\\/:*?""<>|]");

        /// <summary>
        /// Strips characters Windows does not allow in a file name, so a ticker like
        /// "BRK/B" still produces a usable file name when the app looks for its data.
        /// </summary>
        public static string ToFileNameSafe(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return InvalidFileNameChars.Replace(text.Trim(), "_");
        }
    }
}
