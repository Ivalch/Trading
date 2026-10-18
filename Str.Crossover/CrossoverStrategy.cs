using System;
using System.Collections.Generic;
using System.Globalization;
using TradingBacktest.Abstractions;

namespace Str.Crossover
{
    /// <summary>
    /// Classic moving-average crossover: long while the first indicator reads above the
    /// second. What it does the other way round - sit out, or go short - is a parameter, and
    /// a short is additionally gated by the host's Allow short switch.
    /// </summary>
    public sealed class CrossoverStrategyFactory : IStrategyFactory
    {
        public const string ConfirmKey = "Confirm";
        public const string BelowKey = "Below";

        public const string BelowFlat = "flat";
        public const string BelowShort = "short";

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
            get { return "Long on the first indicator crossing above the second."; }
        }

        public IList<IndicatorParameterDefinition> Parameters
        {
            get
            {
                var options = new List<string> { BelowFlat, BelowShort };
                return new List<IndicatorParameterDefinition>
                {
                    IndicatorParameterDefinition.Integer(ConfirmKey, "confirm", 1, 1, 50, "Bars the cross must persist before entering."),
                    IndicatorParameterDefinition.Choice(BelowKey, "below", BelowFlat, options,
                        "What to hold the other way round. Shorting also needs Allow short switched on.")
                };
            }
        }

        public IStrategy Create(IndicatorParameters parameters)
        {
            bool shortBelow = string.Equals(parameters.GetString(BelowKey), BelowShort, StringComparison.OrdinalIgnoreCase);
            return new CrossoverStrategy(_fastLine, _slowLine, parameters.GetInt(ConfirmKey), shortBelow);
        }
    }

    public sealed class CrossoverStrategy : IStrategy
    {
        private readonly int _fastLine;
        private readonly int _slowLine;
        private readonly int _confirm;
        private readonly bool _shortBelow;

        public CrossoverStrategy(int fastLine, int slowLine, int confirm, bool shortBelow)
        {
            _fastLine = fastLine;
            _slowLine = slowLine;
            _confirm = confirm < 1 ? 1 : confirm;
            _shortBelow = shortBelow;
        }

        public string Name
        {
            get { return "Average crossover"; }
        }

        /// <summary>
        /// Built from the configured instance, so every number quoted is the one in force.
        /// </summary>
        public string Description
        {
            get
            {
                string head = "Long while indicator " + (_fastLine + 1) + " reads above indicator " + (_slowLine + 1);

                if (_confirm > 1)
                {
                    head += ", entering after " + _confirm.ToString(CultureInfo.CurrentCulture) + " bars";
                }

                return head + (_shortBelow ? ", short the other way round." : ", flat the other way round.");
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
                    "The crossover rule works on the first " + (highest + 1) + " indicator slots but "
                    + context.Series.Count + (context.Series.Count == 1 ? " is" : " are") + " filled.");
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
                bool ready = fast.HasValueAt(i) && slow.HasValueAt(i);
                bool above = ready && fast.Values[i] > slow.Values[i];

                barsAbove = above ? barsAbove + 1 : 0;

                StrategyAction wanted;
                if (above && barsAbove >= _confirm)
                {
                    wanted = StrategyAction.Long;
                }
                else if (ready && !above && _shortBelow)
                {
                    wanted = StrategyAction.Short;
                }
                else
                {
                    wanted = StrategyAction.Flat;
                }

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