using System.Collections.Generic;
using System.Windows.Media;
using TradingBacktest.Models;

namespace TradingBacktest.Indicators
{
    /// <summary>
    /// An indicator is a pure calculation: bars in, one or more plotted series out.
    /// It knows nothing about trading, about the chart and about the other indicators, so
    /// new ones can be dropped in and swapped without touching the engine or the UI.
    /// </summary>
    public interface IIndicator
    {
        /// <summary>Short type name, e.g. "SMA". Used for labels and for restoring settings.</summary>
        string Name { get; }

        /// <summary>Window this instance was built with.</summary>
        int Period { get; }

        /// <summary>Window a freshly created prototype of this indicator uses.</summary>
        int DefaultPeriod { get; }

        /// <summary>Returns the same indicator with a different window. Never mutates <c>this</c>.</summary>
        IIndicator WithPeriod(int period);

        /// <summary>
        /// Computes the series over <paramref name="candles"/>. Values are aligned with the
        /// input one to one and are NaN while the window is still filling up, so the chart
        /// simply skips them.
        /// </summary>
        IList<IndicatorLine> Calculate(IList<Candle> candles);
    }

    /// <summary>One plotted series produced by an indicator.</summary>
    public sealed class IndicatorLine
    {
        public IndicatorLine(string label, double[] values, Color color, bool usesPriceScale)
        {
            Label = label;
            Values = values;
            Color = color;
            UsesPriceScale = usesPriceScale;
        }

        /// <summary>Legend text, e.g. "SMA 20".</summary>
        public string Label { get; private set; }

        /// <summary>One value per bar, NaN during the warm-up.</summary>
        public double[] Values { get; private set; }

        /// <summary>Line colour. Indicators live inside a WPF app, so pulling in a Color is fine.</summary>
        public Color Color { get; private set; }

        /// <summary>
        /// True when the values share the price axis (moving averages, Bollinger bands).
        /// False puts the series in its own band under the price pane (RSI and friends),
        /// otherwise an oscillator between 0 and 100 would flatten out against prices
        /// in the thousands.
        /// </summary>
        public bool UsesPriceScale { get; private set; }


        public bool HasValueAt(int index)
        {
            return index >= 0 && index < Values.Length && !double.IsNaN(Values[index]);
        }
    }
}
