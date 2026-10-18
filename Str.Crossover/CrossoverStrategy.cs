using System;
using System.Collections.Generic;
using System.Globalization;
using TradingBacktest.Abstractions;

namespace Str.Crossover
{
    /// <summary>
    /// Classic moving-average crossover: long while the first indicator reads above the
    /// second, flat otherwise. Optionally waits for the cross to persist before entering,
    /// which is the cheapest way to cut out the whipsaws that plague crossovers.
    /// </summary>
    public sealed class CrossoverStrategyFactory : IStrategyFactory
    {
        public const string ConfirmKey = "Confirm";

        private readonly int _fastLine;
        private readonly int _slowLine;

        public CrossoverStrategyFactory()
            : this(0, 1)
        {
        }

        public CrossoverStrategyFactory(int fastLine, int slowLine)
        {
            if (fastLine < 0 || slowLine < 0)
            {
                throw new ArgumentOutOfRangeException("fastLine");
            }

            _fastLine = fastLine;
            _slowLine = slowLine;
        }

        public string Id
        {
            get { return "Crossover"; }
        }

        public string Name
        {
            get { return "Average crossover"; }
        }

        /// <summary>No parameter values here - this instance is built once, at startup.</summary>
        public string Description
        {
            get { return "Long while the first indicator reads above the second."; }
        }

        public IList<IndicatorParameterDefinition> Parameters
        {
            get
            {
                return new List<IndicatorParameterDefinition>
                {
                    IndicatorParameterDefinition.Integer(ConfirmKey, "confirm", 1, 1, 50, "Bars the cross must persist before entering.")
                };
            }
        }

        public IStrategy Create(IndicatorParameters parameters)
        {
            return new CrossoverStrategy(_fastLine, _slowLine, parameters.GetInt(ConfirmKey));
        }
    }

    public sealed class CrossoverStrategy : IStrategy
    {
        private readonly int _fastLine;
        private readonly int _slowLine;
        private readonly int _confirm;

        public CrossoverStrategy(int fastLine, int slowLine, int confirm)
        {
            _fastLine = fastLine;
            _slowLine = slowLine;
            _confirm = confirm < 1 ? 1 : confirm;
        }

        public string Name
        {
            get { return "Average crossover"; }
        }

        /// <summary>Built from the configured instance, so the number quoted is the one in use.</summary>
        public string Description
        {
            get
            {
                if (_confirm <= 1)
                {
                    return "Long while indicator " + (_fastLine + 1) + " reads above indicator " + (_slowLine + 1) + ".";
                }

                return "Long while indicator " + (_fastLine + 1) + " reads above indicator " + (_slowLine + 1)
                    + ", entering only after " + _confirm.ToString(CultureInfo.CurrentCulture) + " bars.";
            }
        }

        public int MinBars
        {
            get { return _confirm + 1; }
        }

        public IList<Signal> GenerateSignals(StrategyContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            int highest = Math.Max(_fastLine, _slowLine);
            if (context.Series.Count <= highest)
            {
                throw new InvalidOperationException(
                    "The crossover rule works on the first " + (highest + 1) + " indicator slots but " + context.Series.Count
                    + " are filled. Fill both slots.");
            }

            IndicatorLine fast = context.Series[_fastLine].Primary;
            IndicatorLine slow = context.Series[_slowLine].Primary;
            if (fast == null || slow == null || fast.Values.Length != slow.Values.Length)
            {
                throw new InvalidOperationException("The two indicators returned mismatched series.");
            }

            var signals = new List<Signal>();
            StrategyAction current = StrategyAction.Flat;
            int barsAbove = 0;

            for (int i = 0; i < context.Candles.Count; i++)
            {
                bool above = fast.HasValueAt(i) && slow.HasValueAt(i) && fast.Values[i] > slow.Values[i];

                barsAbove = above ? barsAbove + 1 : 0;
                StrategyAction wanted = above && barsAbove >= _confirm ? StrategyAction.Long : StrategyAction.Flat;

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