using System;
using System.Collections.Generic;
using TradingBacktest.Indicators;
using TradingBacktest.Models;

namespace TradingBacktest.Strategies
{
    /// <summary>
    /// What the strategy wants to hold from a given bar on. Long/flat only: the engine has
    /// no margin model, so a strategy cannot ask to go short.
    /// </summary>
    public enum StrategyAction
    {
        Flat,
        Long
    }

    /// <summary>
    /// A decision made at the close of <see cref="BarIndex"/>. The engine fills it at the
    /// open of the next bar, so a strategy can never trade on a price it could not have seen.
    /// </summary>
    public struct Signal
    {
        public Signal(int barIndex, StrategyAction action)
        {
            BarIndex = barIndex;
            Action = action;
        }

        public int BarIndex { get; private set; }

        public StrategyAction Action { get; private set; }
    }

    /// <summary>
    /// Everything one indicator produced. An indicator may return several series (Bollinger
    /// has three); the first one is the main line, the rest are companions.
    /// </summary>
    public sealed class IndicatorSeries
    {
        public IndicatorSeries(IIndicator indicator, IList<IndicatorLine> lines)
        {
            if (indicator == null)
            {
                throw new ArgumentNullException("indicator");
            }

            if (lines == null)
            {
                throw new ArgumentNullException("lines");
            }

            Indicator = indicator;
            Lines = lines;
        }

        public IIndicator Indicator { get; private set; }

        public IList<IndicatorLine> Lines { get; private set; }

        /// <summary>The main series, e.g. the middle band of a Bollinger set.</summary>
        public IndicatorLine Primary
        {
            get { return Lines.Count > 0 ? Lines[0] : null; }
        }

        public string Label
        {
            get { return Indicator.Name + " " + Indicator.Period.ToString(System.Globalization.CultureInfo.CurrentCulture); }
        }
    }

    /// <summary>What a rule is handed: the bars plus the indicator output.</summary>
    public sealed class StrategyContext
    {
        public StrategyContext(IList<Candle> candles, IList<IndicatorSeries> series)
        {
            Candles = candles;
            Series = series;
        }

        public IList<Candle> Candles { get; private set; }

        /// <summary>
        /// One entry per configured indicator, in the order the UI lists them. A rule picks
        /// what it needs by position, so moving an indicator in the list changes what the
        /// rule trades on - the list is a parameter, not decoration.
        /// </summary>
        public IList<IndicatorSeries> Series { get; private set; }
    }

    /// <summary>
    /// The buy/sell rule, kept apart from both the indicators and the backtest engine: it
    /// receives the bars plus whatever the indicators produced and answers with a list of
    /// decisions. Swapping in a different rule needs no change anywhere else.
    /// </summary>
    public interface IStrategy
    {
        string Name { get; }

        /// <summary>One line describing the rule, shown under the parameters.</summary>
        string Description { get; }

        /// <summary>Bars needed before the first decision can be made.</summary>
        int MinBars { get; }

        /// <summary>
        /// Lookback the rule exposes for tuning, or 0 for rules that have none (a crossover
        /// takes its windows from the indicators, for instance).
        /// </summary>
        int Lookback { get; }

        /// <summary>Returns the same rule with a different lookback. Ignored when there is none.</summary>
        IStrategy WithLookback(int lookback);

        /// <summary>
        /// Produces the decisions. Returned signals are sparse - only the bars where the
        /// wanted position changes - and the engine holds the position in between.
        /// </summary>
        IList<Signal> GenerateSignals(StrategyContext context);
    }
}
