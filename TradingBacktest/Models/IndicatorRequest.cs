using System.Collections.Generic;
using TradingBacktest.Abstractions;

namespace TradingBacktest.Models
{
    /// <summary>
    /// One indicator slot's contribution to a run: the configured indicator, plus the bars
    /// it should be calculated on.
    ///
    /// The plug-in contract is untouched. An indicator is still handed a list of candles and
    /// returns a result; the host alone decides which candles those are, so "calculate this
    /// one on the daily file and spread it back over the minute bars" needs nothing from
    /// the plug-in and cannot be forgotten by a plug-in.
    /// </summary>
    public sealed class IndicatorRequest
    {
        public IIndicator Indicator { get; set; }

        /// <summary>
        /// The bars to calculate on when the indicator lives on a coarser timeframe, or null
        /// to use the bars the chart is showing.
        /// </summary>
        public IList<Candle> HigherTimeframeCandles { get; set; }

        /// <summary>Timeframe code of <see cref="HigherTimeframeCandles"/>, empty for the chart's own.</summary>
        public string Timeframe { get; set; }

        public bool UsesHigherTimeframe
        {
            get { return HigherTimeframeCandles != null && HigherTimeframeCandles.Count > 0 && !string.IsNullOrEmpty(Timeframe); }
        }

        public static IndicatorRequest On(IIndicator indicator)
        {
            return new IndicatorRequest { Indicator = indicator };
        }

        public static IndicatorRequest OnTimeframe(IIndicator indicator, IList<Candle> candles, string timeframe)
        {
            return new IndicatorRequest
            {
                Indicator = indicator,
                HigherTimeframeCandles = candles,
                Timeframe = timeframe
            };
        }
    }
}