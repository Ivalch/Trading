using System.Collections.Generic;
using TradingBacktest.Abstractions;

namespace TradingBacktest.Strategies
{
    /// <summary>
    /// Bought and held for the whole period. Not a useful strategy, but the reference every
    /// other rule should be measured against - and the chart draws it as a benchmark anyway.
    /// </summary>
    public sealed class BuyAndHoldStrategy : IStrategy
    {
        public string Name
        {
            get { return "Buy and hold"; }
        }

        public string Description
        {
            get { return "One entry on the first bar, never exits."; }
        }

        public int MinBars
        {
            get { return 1; }
        }

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
            if (context.Candles.Count == 0)
            {
                return new List<Signal>();
            }

            return new List<Signal> { new Signal(0, StrategyAction.Long) };
        }
    }
}
