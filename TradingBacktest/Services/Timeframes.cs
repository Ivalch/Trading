using System;
using System.Collections.Generic;
using System.Globalization;

namespace TradingBacktest.Services
{
    /// <summary>
    /// The bar sizes a history export can come in, finest first.
    ///
    /// The codes are the words that end a Finam file name - "20211129_TQBR_CHMF_15M.dat" -
    /// so a timeframe is also how a file is found, and there is no mapping to keep in step.
    ///
    /// Nothing here knows what a bar costs or how long it lasts in minutes. Finam stamps
    /// every bar with the moment it CLOSED, and that is what the chart and the resampler
    /// rely on; the session gaps mean a "day" is not always 24 bars of an hour apart.
    /// </summary>
    public static class Timeframes
    {
        /// <summary>The code for "no other timeframe", i.e. the chart's own bars.</summary>
        public const string Same = "";

        private static readonly string[] Codes = { "Min", "5M", "15M", "Hour", "Day", "Week", "Month" };
        private static readonly string[] Labels = { "1 minute", "5 minutes", "15 minutes", "1 hour", "1 day", "1 week", "1 month" };

        // Short enough to sit in a legend next to "SMA 20" without shouting.
        private static readonly string[] ShortLabels = { "1m", "5m", "15m", "1h", "1d", "1w", "1mo" };

        /// <summary>Every known timeframe, finest first.</summary>
        public static IList<string> All
        {
            get { return new List<string>(Codes); }
        }

        /// <summary>Position in the finest-first order, or -1 when the code is unknown.</summary>
        public static int Index(string code)
        {
            for (int i = 0; i < Codes.Length; i++)
            {
                if (string.Equals(Codes[i], code, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        public static bool IsKnown(string code)
        {
            return Index(code) >= 0;
        }

        /// <summary>Name for a drop-down: "1 hour".</summary>
        public static string Display(string code)
        {
            int index = Index(code);
            return index < 0 ? "same" : Labels[index];
        }

        /// <summary>Name for a chart legend or a status line: "1h".</summary>
        public static string ShortLabel(string code)
        {
            int index = Index(code);
            return index < 0 ? string.Empty : ShortLabels[index];
        }

        /// <summary>
        /// True when <paramref name="candidate"/> is strictly coarser than the chart's
        /// timeframe - the only direction that makes sense for "calculated on the upper
        /// timeframe". An equal or finer code is refused, so a slot can never end up
        /// slower than the bars it is drawn on.
        /// </summary>
        public static bool IsCoarserThan(string candidate, string chart)
        {
            int candidateIndex = Index(candidate);
            if (candidateIndex < 0)
            {
                return false;
            }

            // Nothing can be shown to be coarser than a timeframe that is not known, so
            // nothing is offered. Guessing here is how a slot ends up finer than the chart,
            // and Index returns -1 for an unknown code, which would compare as "coarser".
            int chartIndex = Index(chart);
            return chartIndex >= 0 && candidateIndex > chartIndex;
        }

        /// <summary>The codes coarser than <paramref name="chart"/>, finest of them first.</summary>
        public static IList<string> CoarserThan(string chart)
        {
            var result = new List<string>();

            int start = Index(chart);
            if (start < 0)
            {
                return result;  // nothing is provably coarser than an unknown timeframe
            }

            for (int i = start + 1; i < Codes.Length; i++)
            {
                result.Add(Codes[i]);
            }

            return result;
        }

        /// <summary>A label safe to show when the code is unknown or empty.</summary>
        public static string Describe(string code)
        {
            return string.IsNullOrEmpty(code) ? "the chart's own bars" : Display(code);
        }

        /// <summary>Sorts a list of codes finest first.</summary>
        public static void SortFinestFirst(List<string> codes)
        {
            if (codes == null)
            {
                return;
            }

            codes.Sort(delegate (string a, string b)
            {
                int left = Index(a);
                int right = Index(b);

                // Unknown codes sink to the bottom rather than throwing.
                if (left < 0)
                {
                    left = Codes.Length;
                }

                if (right < 0)
                {
                    right = Codes.Length;
                }

                return left.CompareTo(right);
            });
        }
    }

    /// <summary>
    /// One entry of a timeframe drop-down. The code identifies the file to open; the
    /// display text is what the reader sees.
    /// </summary>
    public sealed class TimeframeOption
    {
        public TimeframeOption(string code, string display)
        {
            Code = code ?? Timeframes.Same;
            Display = display ?? string.Empty;
        }

        /// <summary>The file's timeframe code, or empty for the chart's own bars.</summary>
        public string Code { get; private set; }

        public string Display { get; private set; }

        /// <summary>True for the "same as the chart" entry an indicator slot starts on.</summary>
        public bool IsSame
        {
            get { return Code.Length == 0; }
        }

        public override string ToString()
        {
            return Display;
        }
    }
}