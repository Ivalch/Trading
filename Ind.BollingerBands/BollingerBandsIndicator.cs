using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;
using TradingBacktest.Abstractions;

namespace Ind.BollingerBands
{
    /// <summary>
    /// Bollinger bands: a moving average in the middle and two bands a number of standard
    /// deviations away. One plug-in, three series - which is why the contract returns a
    /// result object rather than a single array.
    /// </summary>
    public sealed class BollingerBandsIndicatorFactory : IIndicatorFactory
    {
        public const string PeriodKey = "Period";
        public const string DeviationsKey = "Deviations";

        public string Id
        {
            get { return "BollingerBands"; }
        }

        public string Name
        {
            get { return "Bollinger"; }
        }

        public string Description
        {
            get { return "Moving average with an envelope a number of standard deviations wide."; }
        }

        public IList<IndicatorParameterDefinition> Parameters
        {
            get
            {
                return new List<IndicatorParameterDefinition>
                {
                    IndicatorParameterDefinition.Integer(PeriodKey, "bars", 20, 2, 2000, "Window for the average and the deviation."),
                    IndicatorParameterDefinition.Number(DeviationsKey, "std dev", 2.0, 0.1, 10.0, "How far the bands sit from the average.")
                };
            }
        }

        public IIndicator Create(IndicatorParameters parameters)
        {
            return new BollingerBandsIndicator(parameters.GetInt(PeriodKey), parameters.GetDouble(DeviationsKey));
        }
    }

    public sealed class BollingerBandsIndicator : IIndicator
    {
        public static readonly Color MiddleColor = Color.FromRgb(0x00, 0x69, 0x5C);
        public static readonly Color UpperColor = Color.FromRgb(0x5C, 0x6B, 0x73);
        public static readonly Color LowerColor = Color.FromRgb(0x5C, 0x6B, 0x73);

        private readonly int _period;
        private readonly double _deviations;

        public BollingerBandsIndicator(int period, double deviations)
        {
            if (period < 2)
            {
                throw new ArgumentOutOfRangeException("period");
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
            get { return "Bollinger " + _period.ToString(CultureInfo.CurrentCulture); }
        }

        public string Description
        {
            get { return "Average over " + _period + " bars, bands at " + _deviations.ToString("0.##", CultureInfo.CurrentCulture) + " standard deviations."; }
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
            // this indicator, and the host treats the first line as the main one.
            return new IndicatorResult(new List<IndicatorLine>
            {
                new IndicatorLine("BB mid " + _period, middle, MiddleColor, true),
                new IndicatorLine("BB upper " + _period, upper, UpperColor, true),
                new IndicatorLine("BB lower " + _period, lower, LowerColor, true)
            });
        }
    }
}