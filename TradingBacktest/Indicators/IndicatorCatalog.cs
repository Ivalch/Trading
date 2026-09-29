using System.Collections.Generic;
using TradingBacktest.Models;

namespace TradingBacktest.Indicators
{
    /// <summary>
    /// Prototypes of every indicator the app can draw. The list powers the "add indicator"
    /// drop-down; each entry is a default configuration that gets cloned with
    /// <see cref="IIndicator.WithPeriod"/> once the user changes the window.
    /// </summary>
    public static class IndicatorCatalog
    {
        private static readonly List<IIndicator> Prototypes = new List<IIndicator>
        {
            new SmaIndicator(),
            new EmaIndicator(),
            new RsiIndicator(),
            new BollingerBandsIndicator()
        };

        /// <summary>The same instances every time, so the drop-down shows one entry per type.</summary>
        public static IList<IIndicator> All
        {
            get { return Prototypes; }
        }

        /// <summary>Finds a prototype by its <see cref="IIndicator.Name"/>, case-insensitively.</summary>
        public static IIndicator Find(string name)
        {
            for (int i = 0; i < Prototypes.Count; i++)
            {
                if (string.Equals(Prototypes[i].Name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return Prototypes[i];
                }
            }

            return null;
        }
    }
}
