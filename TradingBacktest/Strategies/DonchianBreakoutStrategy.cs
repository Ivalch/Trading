using System;
using System.Collections.Generic;
using TradingBacktest.Abstractions;

namespace TradingBacktest.Strategies
{
    /// <summary>
    /// Donchian breakout: go long when the close pushes above the highest high of the
    /// previous N bars, stay out otherwise. Needs no indicators at all, which is a useful
    /// reminder that the rule and the indicators really are independent.
    /// </summary>
    public sealed class DonchianBreakoutStrategy : IStrategy
    {
        private readonly int _lookback;

        public DonchianBreakoutStrategy()
            : this(20)
        {
        }

        public DonchianBreakoutStrategy(int lookback)
        {
            if (lookback < 2)
            {
                throw new ArgumentOutOfRangeException("lookback");
            }

            _lookback = lookback;
        }

        public string Name
        {
            get { return "Donchian breakout"; }
        }

        public string Description
        {
            get { return "Long when the close exceeds the highest high of the previous " + _lookback + " bars."; }
        }

        public int MinBars
        {
            get { return _lookback + 1; }
        }

        public int Lookback
        {
            get { return _lookback; }
        }

        public IStrategy WithLookback(int lookback)
        {
            return new DonchianBreakoutStrategy(lookback);
        }

        public IList<Signal> GenerateSignals(StrategyContext context)
        {
            IList<Candle> candles = context.Candles;
            var signals = new List<Signal>();
            StrategyAction current = StrategyAction.Flat;

            // Start at the first bar that has a full window of previous highs behind it.
            for (int i = _lookback; i < candles.Count; i++)
            {
                double highest = candles[i - 1].High;
                for (int j = i - _lookback + 1; j < i; j++)
                {
                    if (candles[j].High > highest)
                    {
                        highest = candles[j].High;
                    }
                }

                StrategyAction wanted = candles[i].Close > highest ? StrategyAction.Long : StrategyAction.Flat;
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
