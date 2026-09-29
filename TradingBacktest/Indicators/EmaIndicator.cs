using System;
using System.Collections.Generic;
using System.Windows.Media;
using TradingBacktest.Models;

namespace TradingBacktest.Indicators
{
    /// <summary>
    /// Exponential moving average. Seeded with the simple average of the first window and
    /// smoothed with alpha = 2 / (period + 1) afterwards.
    /// </summary>
    public sealed class EmaIndicator : IIndicator
    {
        public static readonly Color DefaultColor = Color.FromRgb(0x8E, 0x24, 0xAA);

        private readonly int _period;

        public EmaIndicator()
            : this(20)
        {
        }

        public EmaIndicator(int period)
        {
            if (period < 2)
            {
                throw new ArgumentOutOfRangeException("period", "The window must be at least 2 bars.");
            }

            _period = period;
        }

        public string Name
        {
            get { return "EMA"; }
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
            return new EmaIndicator(period);
        }

        public IList<IndicatorLine> Calculate(IList<Candle> candles)
        {
            var values = new double[candles.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = double.NaN;
            }

            if (candles.Count < _period)
            {
                return new List<IndicatorLine> { new IndicatorLine("EMA " + _period, values, DefaultColor, true) };
            }

            double alpha = 2.0 / (_period + 1.0);
            double sum = 0;
            for (int i = 0; i < _period; i++)
            {
                sum += candles[i].Close;
            }

            double ema = sum / _period;
            values[_period - 1] = ema;

            for (int i = _period; i < candles.Count; i++)
            {
                ema = (candles[i].Close - ema) * alpha + ema;
                values[i] = ema;
            }

            var line = new IndicatorLine("EMA " + _period, values, DefaultColor, true);
            return new List<IndicatorLine> { line };
        }
    }
}
