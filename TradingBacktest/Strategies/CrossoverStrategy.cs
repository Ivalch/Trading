using System;
using System.Collections.Generic;
using TradingBacktest.Abstractions;

namespace TradingBacktest.Strategies
{
    /// <summary>
    /// Classic moving-average crossover: long while the first indicator reads above the
    /// second, flat otherwise.
    ///
    /// The rule refers to indicators by their position in the list, so it works with any
    /// pair - SMA over EMA, Bollinger middle over SMA, and so on. Two indicators that never
    /// cross simply produce no trades, which is a valid answer too.
    /// </summary>
    public sealed class CrossoverStrategy : IStrategy
    {
        private readonly int _fast;
        private readonly int _slow;

        public CrossoverStrategy()
            : this(0, 1)
        {
        }

        public CrossoverStrategy(int fast, int slow)
        {
            if (fast < 0 || slow < 0)
            {
                throw new ArgumentOutOfRangeException("fast");
            }

            _fast = fast;
            _slow = slow;
        }

        public string Name
        {
            get { return "Average crossover"; }
        }

        public string Description
        {
            get { return "Long while indicator " + (_fast + 1) + " reads above indicator " + (_slow + 1) + " of the list."; }
        }

        public int MinBars
        {
            get { return 2; }
        }

        /// <summary>The windows come from the indicators, so there is nothing to tune here.</summary>
        public int Lookback
        {
            get { return 0; }
        }

        public IStrategy WithLookback(int lookback)
        {
            return this;
        }

        public IList<Signal> GenerateSignals(StrategyContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            int highest = Math.Max(_fast, _slow);
            if (context.Series.Count <= highest)
            {
                throw new InvalidOperationException(
                    "The crossover rule needs " + (highest + 1) + " indicators but the list has " + context.Series.Count + ". Fill both indicator slots.");
            }

            IndicatorLine fast = context.Series[_fast].Primary;
            IndicatorLine slow = context.Series[_slow].Primary;
            if (fast == null || slow == null || fast.Values.Length != slow.Values.Length)
            {
                throw new InvalidOperationException("The two indicators returned mismatched series.");
            }

            var signals = new List<Signal>();
            StrategyAction current = StrategyAction.Flat;

            for (int i = 0; i < context.Candles.Count; i++)
            {
                bool above = fast.HasValueAt(i) && slow.HasValueAt(i) && fast.Values[i] > slow.Values[i];
                StrategyAction wanted = above ? StrategyAction.Long : StrategyAction.Flat;

                if (wanted != current)
                {
                    signals.Add(new Signal(i, wanted));
                    current = wanted;
                }
            }

            return signals;
        }
    }
}
