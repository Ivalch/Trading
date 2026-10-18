using System.Collections.Generic;
using TradingBacktest.Abstractions;

namespace Str.BuyAndHold
{
    /// <summary>
    /// Bought and held for the whole period. Not a useful rule, but the reference every
    /// other one should be measured against - and the chart draws it as a benchmark anyway.
    ///
    /// It declares no parameters at all, which is the other half of the contract: a plug-in
    /// with nothing to tune simply returns an empty list and the host shows no editors.
    /// </summary>
    public sealed class BuyAndHoldStrategyFactory : IStrategyFactory
    {
        public string Id
        {
            get { return "BuyAndHold"; }
        }

        public string Name
        {
            get { return "Buy and hold"; }
        }

        public string Description
        {
            get { return "One entry on the first bar, never exits."; }
        }

        public IList<IndicatorParameterDefinition> Parameters
        {
            get { return new List<IndicatorParameterDefinition>(); }
        }

        public IStrategy Create(IndicatorParameters parameters)
        {
            return new BuyAndHoldStrategy();
        }
    }

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