using System.Collections.Generic;

namespace TradingBacktest.Abstractions
{
    /// <summary>
    /// A pure calculation: bars in, plotted series out. It knows nothing about trading, the
    /// chart, or the host - which is why it can live in its own DLL and be swapped at
    /// startup.
    /// </summary>
    public interface IIndicator
    {
        /// <summary>Display name, e.g. "SMA".</summary>
        string Name { get; }

        /// <summary>One line saying what it does, shown under the settings.</summary>
        string Description { get; }

        /// <summary>Warm-up in bars: the engine uses it to report an honest minimum period.</summary>
        int MinBars { get; }

        /// <summary>
        /// Computes the series over <paramref name="candles"/>, one value per bar and NaN
        /// while the window fills up. Return <see cref="IndicatorResult.Empty"/> when there
        /// are not enough bars.
        /// </summary>
        IndicatorResult Calculate(IList<Candle> candles);
    }
}