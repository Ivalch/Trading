using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;
using TradingBacktest.Abstractions;

namespace Ind.Rsi
{
    /// <summary>
    /// Relative Strength Index (Wilder). An oscillator between 0 and 100, so it declares
    /// that it does not share the price axis and the host gives it its own band.
    /// </summary>
    public sealed class RsiIndicatorFactory : IIndicatorFactory
    {
        public const string PeriodKey = "Period";

        public string Id
        {
            get { return "Rsi"; }
        }

        public string Name
        {
            get { return "RSI"; }
        }

        public string Description
        {
            get { return "Relative Strength Index, Wilder smoothing, plotted on its own scale."; }
        }

        public IList<IndicatorParameterDefinition> Parameters
        {
            get
            {
                return new List<IndicatorParameterDefinition>
                {
                    IndicatorParameterDefinition.Integer(PeriodKey, "bars", 14, 2, 500, "Lookback of the average gains and losses.")
                };
            }
        }

        public IIndicator Create(IndicatorParameters parameters)
        {
            return new RsiIndicator(parameters.GetInt(PeriodKey));
        }
    }

    public sealed class RsiIndicator : IIndicator
    {
        public static readonly Color LineColor = Color.FromRgb(0x00, 0x8F, 0xFF);

        public const double Overbought = 70.0;
        public const double Oversold = 30.0;

        private readonly int _period;

        public RsiIndicator(int period)
        {
            if (period < 2)
            {
                throw new System.ArgumentOutOfRangeException("period");
            }

            _period = period;
        }

        public string Name
        {
            get { return "RSI " + _period.ToString(CultureInfo.CurrentCulture); }
        }

        public string Description
        {
            get { return "Relative Strength Index over " + _period + " bars, 0 to 100."; }
        }

        public int MinBars
        {
            get { return _period + 1; }
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

            if (candles.Count <= _period)
            {
                return new IndicatorResult(new List<IndicatorLine>
                {
                    new IndicatorLine(Name, values, LineColor, false)
                });
            }

            double gainSum = 0;
            double lossSum = 0;
            for (int i = 1; i <= _period; i++)
            {
                double change = candles[i].Close - candles[i - 1].Close;
                if (change >= 0)
                {
                    gainSum += change;
                }
                else
                {
                    lossSum -= change;
                }
            }

            double gain = gainSum / _period;
            double loss = lossSum / _period;
            values[_period] = Value(gain, loss);

            for (int i = _period + 1; i < candles.Count; i++)
            {
                double change = candles[i].Close - candles[i - 1].Close;
                double up = change > 0 ? change : 0;
                double down = change < 0 ? -change : 0;

                // Wilder smoothing.
                gain = (gain * (_period - 1) + up) / _period;
                loss = (loss * (_period - 1) + down) / _period;
                values[i] = Value(gain, loss);
            }

            return new IndicatorResult(
                new List<IndicatorLine> { new IndicatorLine(Name, values, LineColor, false) },
                new List<IndicatorLevel>
                {
                    new IndicatorLevel(Overbought, "overbought"),
                    new IndicatorLevel(Oversold, "oversold")
                });
        }

        private static double Value(double gain, double loss)
        {
            if (loss == 0)
            {
                return gain == 0 ? 50.0 : 100.0;
            }

            double rs = gain / loss;
            return 100.0 - (100.0 / (1.0 + rs));
        }
    }
}