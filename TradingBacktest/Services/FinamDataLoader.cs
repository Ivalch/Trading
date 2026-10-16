using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TradingBacktest.Abstractions;
using TradingBacktest.Models;

namespace TradingBacktest.Services
{
    /// <summary>
    /// Reads the tab separated history files produced by the Finam / Tinkoff "export
    /// history" tool. The layout is one row per bar with no header:
    ///
    ///     date time&lt;TAB&gt;open&lt;TAB&gt;close&lt;TAB&gt;high&lt;TAB&gt;low&lt;TAB&gt;volume&lt;TAB&gt;extra
    ///
    /// Two details are easy to get wrong:
    ///  * the date and the time share one field, separated by a space - the first tab
    ///    only appears after the time;
    ///  * the column order is open, CLOSE, high, low - not OHLLC.
    ///
    /// <see cref="TryParseLine"/> validates every row and rejects it when the "high"
    /// column sits below the open or close, which is how a mis-parse shows up instead of
    /// producing a plausible but wrong chart.
    /// </summary>
    public static class FinamDataLoader
    {
        /// <summary>
        /// Preference order when several files match: the longest history first, so a
        /// ticker that has both daily and intraday exports is tested on the daily one.
        /// </summary>
        private static readonly string[] TimeframesByPreference =
        {
            "Day", "Week", "Month", "Hour", "15M", "5M", "Min"
        };

        /// <summary>
        /// Lists the tickers available in <paramref name="directory"/>, one entry per file.
        /// Used to fill the ticker's drop-down so a typo does not silently produce a
        /// "no data" run.
        /// </summary>
        public static IList<string> ListTickers(string directory)
        {
            var tickers = new List<string>();
            if (!NumberParser.IsUsableDirectory(directory))
            {
                return tickers;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*.dat");
            }
            catch (Exception)
            {
                return tickers;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string ticker = ExtractTicker(Path.GetFileName(files[i]));
                if (ticker.Length > 0 && seen.Add(ticker))
                {
                    tickers.Add(ticker);
                }
            }

            tickers.Sort(StringComparer.OrdinalIgnoreCase);
            return tickers;
        }

        /// <summary>
        /// Picks the file for <paramref name="ticker"/>. When a timeframe is given it wins,
        /// otherwise the one with the longest history is used.
        /// </summary>
        public static string FindFile(string directory, string ticker, string timeframe)
        {
            if (!NumberParser.IsUsableDirectory(directory) || string.IsNullOrEmpty(ticker))
            {
                return null;
            }

            string safe = NumberParser.ToFileNameSafe(ticker);

            try
            {
                string[] files = Directory.GetFiles(directory, "*.dat");
                string best = null;
                int bestRank = int.MaxValue;

                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileNameWithoutExtension(files[i]);
                    if (name.IndexOf(safe, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    int rank = TimeframeRank(name, timeframe);
                    if (rank < bestRank)
                    {
                        bestRank = rank;
                        best = files[i];
                    }
                }

                return best;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Reads every bar of a file, oldest first.</summary>
        public static IList<Candle> ReadFile(string path)
        {
            var candles = new List<Candle>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return candles;
            }

            using (var reader = new StreamReader(path, Encoding.Default))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    Candle candle;
                    if (TryParseLine(line, out candle))
                    {
                        candles.Add(candle);
                    }
                }
            }

            // Exports are normally chronological, but sorting keeps the chart correct even
            // if a hand edited file has been shuffled.
            candles.Sort(delegate (Candle a, Candle b) { return a.Date.CompareTo(b.Date); });
            return candles;
        }

        private static bool TryParseLine(string line, out Candle candle)
        {
            candle = null;
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }

            string[] parts = line.Split('\t');

            // A few exporters put the date and the time in separate fields. Detect that by
            // looking for a time in the first field: if there is none and there are enough
            // columns, the time sits in the second one.
            bool splitDateAndTime = parts[0].IndexOf(':') < 0 && parts.Length >= 7;
            int first = splitDateAndTime ? 2 : 1;

            if (parts.Length < first + 4)
            {
                return false;
            }

            DateTime date;
            if (!TryParseDate(parts[0], splitDateAndTime ? parts[1] : null, out date))
            {
                return false;
            }

            double open, close, high, low;
            if (!TryParseNumber(parts[first], out open)
                || !TryParseNumber(parts[first + 1], out close)
                || !TryParseNumber(parts[first + 2], out high)
                || !TryParseNumber(parts[first + 3], out low))
            {
                return false;
            }

            // Sanity check that also documents the column order: a "high" below the open or
            // close means the file is not in the expected layout.
            if (high < open || high < close || low > open || low > close || low <= 0)
            {
                return false;
            }

            candle = new Candle(date, open, high, low, close);
            return true;
        }

        private static bool TryParseDate(string datePart, string timePart, out DateTime date)
        {
            date = default(DateTime);

            string text = (datePart + " " + (timePart ?? string.Empty)).Trim();
            if (text.Length == 0)
            {
                return false;
            }

            // The export writes "2007-3-23 0:0:0.0" - unpadded numbers and a trailing
            // tenth of a second, which the stock DateTime.ParseExact formats reject.
            string[] formats =
            {
                "yyyy-M-d H:m:s.f", "yyyy-M-d H:m:s", "yyyy-M-d H:m", "yyyy-M-d", "yyyyMMdd"
            };

            return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private static bool TryParseNumber(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Pulls the ticker out of a file name such as "20211129_TQBR_CHMF_Day".
        /// Returns an empty string when the name does not follow that pattern.
        /// </summary>
        private static string ExtractTicker(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return string.Empty;
            }

            string name = Path.GetFileNameWithoutExtension(fileName);
            string[] parts = name.Split('_');
            if (parts.Length < 2)
            {
                return string.Empty;
            }

            // Drop the leading date stamp and the trailing timeframe, keep the middle.
            var middle = new List<string>();
            for (int i = 1; i < parts.Length; i++)
            {
                if (IsTimeframe(parts[i]))
                {
                    break;
                }

                middle.Add(parts[i]);
            }

            return string.Join("_", middle.ToArray());
        }

        private static bool IsTimeframe(string token)
        {
            for (int i = 0; i < TimeframesByPreference.Length; i++)
            {
                if (string.Equals(token, TimeframesByPreference[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static int TimeframeRank(string fileNameWithoutExtension, string preferred)
        {
            for (int i = 0; i < TimeframesByPreference.Length; i++)
            {
                if (fileNameWithoutExtension.EndsWith("_" + TimeframesByPreference[i], StringComparison.OrdinalIgnoreCase))
                {
                    // The explicitly requested timeframe always wins.
                    if (!string.IsNullOrEmpty(preferred)
                        && string.Equals(TimeframesByPreference[i], preferred, StringComparison.OrdinalIgnoreCase))
                    {
                        return -1;
                    }

                    return i;
                }
            }

            return TimeframesByPreference.Length;
        }
    }
}
