using System.Windows.Media;

namespace TradingBacktest.Abstractions
{
    /// <summary>
    /// One series an indicator produced. One value per bar, NaN while the window is still
    /// filling up.
    /// </summary>
    public sealed class IndicatorLine
    {
        public IndicatorLine(string label, double[] values, Color color, bool usesPriceScale)
        {
            if (values == null)
            {
                throw new System.ArgumentNullException("values");
            }

            Label = label;
            Values = values;
            Color = color;
            UsesPriceScale = usesPriceScale;
        }

        /// <summary>Legend text, e.g. "SMA 20".</summary>
        public string Label { get; private set; }

        /// <summary>One value per bar, NaN during the warm-up.</summary>
        public double[] Values { get; private set; }

        /// <summary>Line colour.</summary>
        public Color Color { get; private set; }

        /// <summary>
        /// True when the values share the price axis (moving averages, Bollinger bands).
        /// False puts the series in its own band under the price pane, so an oscillator
        /// between 0 and 100 is not squashed against prices in the thousands.
        /// </summary>
        public bool UsesPriceScale { get; private set; }

        public bool HasValueAt(int index)
        {
            return index >= 0 && index < Values.Length && !double.IsNaN(Values[index]);
        }
    }
}