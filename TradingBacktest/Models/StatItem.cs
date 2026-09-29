namespace TradingBacktest.Models
{
    /// <summary>One label/value pair for the summary strip under the chart.</summary>
    public sealed class StatItem
    {
        public StatItem(string label, string value, bool isHighlighted)
        {
            Label = label;
            Value = value;
            IsHighlighted = isHighlighted;
        }

        public string Label { get; private set; }

        public string Value { get; private set; }

        /// <summary>Highlights the headline numbers (return, profit) against the rest.</summary>
        public bool IsHighlighted { get; private set; }
    }
}
