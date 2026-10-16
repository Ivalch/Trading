using System.Collections.Generic;

namespace TradingBacktest.Abstractions
{
    /// <summary>
    /// A horizontal guide line an indicator wants drawn on its own band - RSI's 70 and 30,
    /// for instance. Declaring it here keeps the chart from having to recognise an indicator
    /// by name.
    /// </summary>
    public sealed class IndicatorLevel
    {
        public IndicatorLevel(double value, string label)
        {
            Value = value;
            Label = label ?? string.Empty;
        }

        public double Value { get; private set; }

        public string Label { get; private set; }
    }

    /// <summary>
    /// What an indicator produced. A wrapper rather than a bare list, so an indicator can
    /// grow extra output later - a signal band, a fill level - without breaking the host.
    /// </summary>
    public sealed class IndicatorResult
    {
        private readonly IList<IndicatorLine> _lines;
        private readonly IList<IndicatorLevel> _levels;

        public IndicatorResult(IList<IndicatorLine> lines)
            : this(lines, null)
        {
        }

        public IndicatorResult(IList<IndicatorLine> lines, IList<IndicatorLevel> levels)
        {
            _lines = lines ?? new List<IndicatorLine>();
            _levels = levels ?? new List<IndicatorLevel>();
        }

        public IList<IndicatorLine> Lines
        {
            get { return _lines; }
        }

        /// <summary>Guide lines to draw on the band this indicator was plotted in.</summary>
        public IList<IndicatorLevel> Levels
        {
            get { return _levels; }
        }

        /// <summary>An indicator with nothing to draw this time - too few bars to warm up.</summary>
        public static IndicatorResult Empty
        {
            get { return new IndicatorResult(new List<IndicatorLine>()); }
        }

        public bool IsEmpty
        {
            get { return _lines.Count == 0; }
        }
    }
}