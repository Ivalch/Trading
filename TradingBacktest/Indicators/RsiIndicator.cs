using System;
using System.Collections.Generic;
using System.Windows.Media;
using TradingBacktest.Models;

namespace TradingBacktest.Indicators
{
    /// <summary>
    /// Relative Strength Index (Wilder). Oscillator between 0 and 100, so it declares that
    /// it does not share the price axis and the chart gives it its own band.
    /// </summary>
    public sealed class RsiIndicator : IIndicator
    {
        public static readonly Color DefaultColor = Color.FromRgb(0x00, 0x8F, 0xFF);

        /// <summary>Bands drawn as a faint background behind the RSI line.</summary>
        public static readonly Color ZoneColor = Color.FromArgb(0x33, 0x9E, 0x9E, 0x9E);

        public const double Overbought = 70.0;
        public const double Oversold = 30.0;

        private readonly int _period;

        public RsiIndicator()
            : this(14)
        {
        }

        public RsiIndicator(int period)
        {
            if (period < 2)
            {
                throw new ArgumentOutOfRangeException("period", "The window must be at least 2 bars.");
            }

            _period = period;
        }

        public string Name
        {
            get { return "RSI"; }
        }

        public int Period
        {
            get { return _period; }
        }

        public int DefaultPeriod
        {
            get { return 14; }
        }

        public IIndicator WithPeriod(int period)
        {
            return new RsiIndicator(period);
        }

        public IList<IndicatorLine> Calculate(IList<Candle> candles)
        {
            var values = new double[candles.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = double.NaN;
            }

            if (candles.Count <= _period)
            {
                return new List<IndicatorLine> { new IndicatorLine("RSI " + _period, values, DefaultColor, false) };
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
            values[_period] = Rsi(gain, loss);

            for (int i = _period + 1; i < candles.Count; i++)
            {
                double change = candles[i].Close - candles[i - 1].Close;
                double up = change > 0 ? change : 0;
                double down = change < 0 ? -change : 0;

                // Wilder smoothing.
                gain = (gain * (_period - 1) + up) / _period;
                loss = (loss * (_period - 1) + down) / _period;
                values[i] = Rsi(gain, loss);
            }

            var line = new IndicatorLine("RSI " + _period, values, DefaultColor, false);
            return new List<IndicatorLine> { line };
        }

        private static double Rsi(double gain, double loss)
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
