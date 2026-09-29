using System;
using System.Collections.Generic;
using System.Windows.Media;
using TradingBacktest.Models;

namespace TradingBacktest.Indicators
{
    /// <summary>
    /// Bollinger bands: a moving average in the middle and two bands a number of standard
    /// deviations away. One indicator, three series - which is why the interface returns a
    /// list instead of a single array.
    /// </summary>
    public sealed class BollingerBandsIndicator : IIndicator
    {
        public static readonly Color MiddleColor = Color.FromRgb(0x00, 0x69, 0x5C);
        public static readonly Color UpperColor = Color.FromRgb(0x5C, 0x6B, 0x73);
        public static readonly Color LowerColor = Color.FromRgb(0x5C, 0x6B, 0x73);

        public const double DefaultDeviations = 2.0;

        private readonly int _period;
        private readonly double _deviations;

        public BollingerBandsIndicator()
            : this(20, DefaultDeviations)
        {
        }

        public BollingerBandsIndicator(int period)
            : this(period, DefaultDeviations)
        {
        }

        public BollingerBandsIndicator(int period, double deviations)
        {
            if (period < 2)
            {
                throw new ArgumentOutOfRangeException("period", "The window must be at least 2 bars.");
            }

            if (deviations <= 0)
            {
                throw new ArgumentOutOfRangeException("deviations");
            }

            _period = period;
            _deviations = deviations;
        }

        public string Name
        {
            get { return "Bollinger"; }
        }

        public int Period
        {
            get { return _period; }
        }

        public int DefaultPeriod
        {
            get { return 20; }
        }

        public IIndicator WithPeriod(int period)
        {
            return new BollingerBandsIndicator(period, _deviations);
        }

        public IList<IndicatorLine> Calculate(IList<Candle> candles)
        {
            int count = candles.Count;
            var middle = new double[count];
            var upper = new double[count];
            var lower = new double[count];
            for (int i = 0; i < count; i++)
            {
                middle[i] = double.NaN;
                upper[i] = double.NaN;
                lower[i] = double.NaN;
            }

            for (int i = _period - 1; i < count; i++)
            {
                double sum = 0;
                for (int j = i - _period + 1; j <= i; j++)
                {
                    sum += candles[j].Close;
                }

                double mean = sum / _period;

                double variance = 0;
                for (int j = i - _period + 1; j <= i; j++)
                {
                    double diff = candles[j].Close - mean;
                    variance += diff * diff;
                }

                double deviation = Math.Sqrt(variance / _period);
                middle[i] = mean;
                upper[i] = mean + _deviations * deviation;
                lower[i] = mean - _deviations * deviation;
            }

            // The middle band comes first: it is the series a rule means when it asks for
            // this indicator, and the chart uses the first line as the main one.
            return new List<IndicatorLine>
            {
                new IndicatorLine("BB mid " + _period, middle, MiddleColor, true),
                new IndicatorLine("BB upper " + _period, upper, UpperColor, true),
                new IndicatorLine("BB lower " + _period, lower, LowerColor, true)
            };
        }
    }
}
