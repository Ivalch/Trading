using System;
using System.Collections.Generic;

namespace TradingBacktest.Abstractions
{
    /// <summary>
    /// What a strategy wants to hold from a given bar on. Long/flat only: the engine has
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
    /// Everything one indicator plug-in produced. A plug-in may return several series
    /// (Bollinger has three); the first one is the main line.
    /// </summary>
    public sealed class IndicatorSeries
    {
        public IndicatorSeries(IIndicator indicator, IndicatorResult result)
        {
            if (indicator == null)
            {
                throw new ArgumentNullException("indicator");
            }

            if (result == null)
            {
                throw new ArgumentNullException("result");
            }

            Indicator = indicator;
            Result = result;
        }

        public IIndicator Indicator { get; private set; }

        public IndicatorResult Result { get; private set; }

        public IList<IndicatorLine> Lines
        {
            get { return Result.Lines; }
        }

        /// <summary>The main series, e.g. the middle band of a Bollinger set.</summary>
        public IndicatorLine Primary
        {
            get { return Result.Lines.Count > 0 ? Result.Lines[0] : null; }
        }

        public string Label
        {
            get { return Indicator.Name; }
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
        /// One entry per filled indicator slot, in slot order. A rule picks what it needs by
        /// position, so filling the slots differently changes what it trades on - the slots
        /// are a parameter, not decoration.
        /// </summary>
        public IList<IndicatorSeries> Series { get; private set; }
    }

    /// <summary>
    /// The buy/sell rule. It receives bars plus indicator output and answers with a list of
    /// decisions, and knows nothing about the engine, the chart or the host - which is why
    /// it can live in its own DLL.
    ///
    /// <see cref="Description"/> is read from a *configured* instance, so any wording that
    /// quotes a parameter ("the previous 50 bars") is true for the value actually in use.
    /// The factory's own description is static and must not quote one.
    /// </summary>
    public interface IStrategy
    {
        /// <summary>Display name, e.g. "Donchian breakout 50".</summary>
        string Name { get; }

        /// <summary>One line describing what this configured rule will actually do.</summary>
        string Description { get; }

        /// <summary>Bars needed before the first decision can be made.</summary>
        int MinBars { get; }

        /// <summary>
        /// Produces the decisions. Returned signals are sparse - only the bars where the
        /// wanted position changes - and the engine holds the position in between.
        /// </summary>
        IList<Signal> GenerateSignals(StrategyContext context);
    }
}