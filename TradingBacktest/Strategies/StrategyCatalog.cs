using System;
using System.Collections.Generic;

namespace TradingBacktest.Strategies
{
    /// <summary>
    /// The buy/sell rules the app ships with. The list powers the strategy drop-down; a new
    /// rule is added by implementing <see cref="IStrategy"/> and putting an instance here.
    /// </summary>
    public static class StrategyCatalog
    {
        private static readonly List<IStrategy> Strategies = new List<IStrategy>
        {
            new CrossoverStrategy(0, 1),
            new DonchianBreakoutStrategy(20),
            new BuyAndHoldStrategy()
        };

        public static IList<IStrategy> All
        {
            get { return Strategies; }
        }

        /// <summary>Finds a rule by <see cref="IStrategy.Name"/>, case-insensitively.</summary>
        public static IStrategy Find(string name)
        {
            for (int i = 0; i < Strategies.Count; i++)
            {
                if (string.Equals(Strategies[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return Strategies[i];
                }
            }

            return null;
        }
    }
}
