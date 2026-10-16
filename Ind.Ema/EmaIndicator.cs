using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;
using TradingBacktest.Abstractions;

namespace Ind.Ema
{
    /// <summary>
    /// Exponential moving average. Seeded with the simple average of the first window, then
    /// smoothed with alpha = 2 / (period + 1).
    /// </summary>
    public sealed class EmaIndicatorFactory : IIndicatorFactory
    {
        public const string PeriodKey = "Period";
        public const string SourceKey = "Source";

        private static readonly List<string> Sources = new List<string> { "Close", "Open", "High", "Low" };

        public string Id
        {
            get { return "Ema"; }
        }

        public string Name
        {
            get { return "EMA"; }
        }

        public string Description
        {
            get { return "Exponential moving average, weighted towards recent bars."; }
        }

        public IList<IndicatorParameterDefinition> Parameters
        {
            get
            {
                return new List<IndicatorParameterDefinition>
                {
                    IndicatorParameterDefinition.Integer(PeriodKey, "bars", 20, 2, 2000, "How many bars the average covers."),
                    IndicatorParameterDefinition.Choice(SourceKey, "source", "Close", Sources, "Which price to average.")
                };
            }
        }

        public IIndicator Create(IndicatorParameters parameters)
        {
            return new EmaIndicator(parameters.GetInt(PeriodKey), parameters.GetString(SourceKey));
        }
    }

    public sealed class EmaIndicator : IIndicator
    {
        public static readonly Color LineColor = Color.FromRgb(0x8E, 0x24, 0xAA);

        private readonly int _period;
        private readonly Func<Candle, double> _pick;

        public EmaIndicator(int period, string source)
        {
            if (period < 2)
            {
                throw new ArgumentOutOfRangeException("period");
            }

            _period = period;
            _pick = SourcePicker.Pick(source);
        }

        public string Name
        {
            get { return "EMA " + _period.ToString(CultureInfo.CurrentCulture); }
        }

        public string Description
        {
            get { return "Exponential average over " + _period + " bars."; }
        }

        public int MinBars
        {
            get { return _period; }
        }

        public IndicatorResult Calculate(IList<Candle> candles)
        {
            if (candles == null || candles.Count == 0)
            {
                return IndicatorResult.Empty;
            }

            var values = new double[candles.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = double.NaN;
            }

            if (candles.Count < _period)
            {
                return new IndicatorResult(new List<IndicatorLine>
                {
                    new IndicatorLine(Name, values, LineColor, true)
                });
            }

            double alpha = 2.0 / (_period + 1.0);
            double sum = 0;
            for (int i = 0; i < _period; i++)
            {
                sum += _pick(candles[i]);
            }

            double ema = sum / _period;
            values[_period - 1] = ema;

            for (int i = _period; i < candles.Count; i++)
            {
                ema = (_pick(candles[i]) - ema) * alpha + ema;
                values[i] = ema;
            }

            return new IndicatorResult(new List<IndicatorLine>
            {
                new IndicatorLine(Name, values, LineColor, true)
            });
        }
    }

    /// <summary>Maps the source name onto a price accessor.</summary>
    internal static class SourcePicker
    {
        internal static Func<Candle, double> Pick(string source)
        {
            if (!string.IsNullOrEmpty(source))
            {
                if (source.Equals("Open", StringComparison.OrdinalIgnoreCase))
                {
                    return delegate (Candle c) { return c.Open; };
                }

                if (source.Equals("High", StringComparison.OrdinalIgnoreCase))
                {
                    return delegate (Candle c) { return c.High; };
                }

                if (source.Equals("Low", StringComparison.OrdinalIgnoreCase))
                {
                    return delegate (Candle c) { return c.Low; };
                }
            }

            return delegate (Candle c) { return c.Close; };
        }
    }
}