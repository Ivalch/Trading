using System.Collections.Generic;
using System.Windows.Media;
using TradingBacktest.Abstractions;

namespace Ind.Sma
{
    /// <summary>
    /// Simple moving average: the unweighted mean of the last N closes.
    ///
    /// The host constructs this factory at startup, once, purely to read Id/Name/Parameters.
    /// The indicator instance below is created per run.
    /// </summary>
    public sealed class SmaIndicatorFactory : IIndicatorFactory
    {
        public const string PeriodKey = "Period";

        public string Id
        {
            get { return "Sma"; }
        }

        public string Name
        {
            get { return "SMA"; }
        }

        public string Description
        {
            get { return "Simple moving average of the close."; }
        }

        public IList<IndicatorParameterDefinition> Parameters
        {
            get
            {
                return new List<IndicatorParameterDefinition>
                {
                    IndicatorParameterDefinition.Integer(PeriodKey, "bars", 20, 2, 2000, "How many bars the average covers.")
                };
            }
        }

        public IIndicator Create(IndicatorParameters parameters)
        {
            return new SmaIndicator(parameters.GetInt(PeriodKey));
        }
    }

    public sealed class SmaIndicator : IIndicator
    {
        public static readonly Color LineColor = Color.FromRgb(0xEF, 0x6C, 0x00);

        private readonly int _period;

        public SmaIndicator(int period)
        {
            if (period < 2)
            {
                throw new System.ArgumentOutOfRangeException("period");
            }

            _period = period;
        }

        public string Name
        {
            get { return "SMA " + _period.ToString(System.Globalization.CultureInfo.CurrentCulture); }
        }

        public string Description
        {
            get { return "Mean of the last " + _period + " closes."; }
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

            // Running sum: O(n) rather than O(n * period).
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

            return new IndicatorResult(new List<IndicatorLine>
            {
                new IndicatorLine(Name, values, LineColor, true)
            });
        }
    }
}