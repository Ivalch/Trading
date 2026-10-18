using System.Globalization;

namespace TradingBacktest.Models
{
    /// <summary>
    /// The costs and switches one run is made under. Grouped in an object because they keep
    /// growing, and a ten-argument call is easy to get wrong at the call site.
    /// </summary>
    public sealed class BacktestSettings
    {
        /// <summary>Percent charged on the traded value of every buy and every sell.</summary>
        public double CostPerOperationPercent { get; set; }

        /// <summary>
        /// Whether a rule is allowed to go short. With this off, a Short request is held as
        /// flat, so the same rule can be run both ways without editing it.
        /// </summary>
        public bool AllowShort { get; set; }

        /// <summary>
        /// Annual borrow fee on a short position, in percent. Charged when the short is
        /// covered, prorated over the time it was open - see the engine for the detail.
        /// </summary>
        public double ShortAnnualFeePercent { get; set; }

        public static BacktestSettings Default()
        {
            return new BacktestSettings
            {
                CostPerOperationPercent = 0.10,
                AllowShort = false,
                ShortAnnualFeePercent = 0.0
            };
        }

        /// <summary>One line for the stats strip.</summary>
        public string Describe()
        {
            return "cost " + Format(CostPerOperationPercent) + "% per operation"
                + (AllowShort ? ", short on, " + Format(ShortAnnualFeePercent) + "% a year to borrow" : ", no shorts");
        }

        private static string Format(double value)
        {
            return value.ToString("0.##", CultureInfo.CurrentCulture);
        }
    }
}