using System;
using System.Collections.Generic;
using System.Windows.Media;
using TradingBacktest.Models;

namespace TradingBacktest.Indicators
{
    /// <summary>Simple moving average: the unweighted mean of the last N closes.</summary>
    public sealed class SmaIndicator : IIndicator
    {
        public static readonly Color DefaultColor = Color.FromRgb(0xEF, 0x6C, 0x00);

        private readonly int _period;

        public SmaIndicator()
            : this(20)
        {
        }

        public SmaIndicator(int period)
        {
            if (period < 2)
            {
                throw new ArgumentOutOfRangeException("period", "The window must be at least 2 bars.");
            }

            _period = period;
        }

        public string Name
        {
            get { return "SMA"; }
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
            return new SmaIndicator(period);
        }

        public IList<IndicatorLine> Calculate(IList<Candle> candles)
        {
            var values = new double[candles.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = double.NaN;
            }

            // Running sum: O(n) instead of O(n * period).
            double sum = 0;
            for (int i = 0; i < candles.Count; i++)
            {
                sum += candles[i].Close;
                if (i >= _period)
                {
                    sum -= candles[i - _period].Close;
                }

                if (i >= _period - 1)
                {
                    values[i] = sum / _period;
                }
            }

            var line = new IndicatorLine("SMA " + _period, values, DefaultColor, true);
            return new List<IndicatorLine> { line };
        }
    }
}
