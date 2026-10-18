using System;
using System.Collections.Generic;
using System.Globalization;
using TradingBacktest.Abstractions;

namespace Str.DonchianBreakout
{
    /// <summary>
    /// Donchian breakout: long when the close pushes above the highest high of the previous
    /// N bars. Needs no indicators at all, which is a useful reminder that the rule and the
    /// indicators really are independent.
    /// </summary>
    public sealed class DonchianBreakoutStrategyFactory : IStrategyFactory
    {
        public const string LookbackKey = "Lookback";

        public string Id
        {
            get { return "DonchianBreakout"; }
        }

        public string Name
        {
            get { return "Donchian breakout"; }
        }

        /// <summary>No parameter values here - this instance is built once, at startup.</summary>
        public string Description
        {
            get { return "Long on a new high for the lookback period."; }
        }

        public IList<IndicatorParameterDefinition> Parameters
        {
            get
            {
                return new List<IndicatorParameterDefinition>
                {
                    IndicatorParameterDefinition.Integer(LookbackKey, "bars", 20, 2, 2000, "How many previous bars set the high to break.")
                };
            }
        }

        public IStrategy Create(IndicatorParameters parameters)
        {
            return new DonchianBreakoutStrategy(parameters.GetInt(LookbackKey));
        }
    }

    public sealed class DonchianBreakoutStrategy : IStrategy
    {
        private readonly int _lookback;

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

        /// <summary>Built from the configured instance, so the number quoted is the one in use.</summary>
        public string Description
        {
            get
            {
                return "Long when the close exceeds the highest high of the previous "
                    + _lookback.ToString(CultureInfo.CurrentCulture) + " bars.";
            }
        }

        public int MinBars
        {
            get { return _lookback + 1; }
        }

        public IList<Signal> GenerateSignals(StrategyContext context)
        {
            IList<Candle> candles = context.Candles;
            var signals = new List<Signal>();
            StrategyAction current = StrategyAction.Flat;

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