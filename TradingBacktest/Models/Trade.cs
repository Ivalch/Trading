namespace TradingBacktest.Models
{
    public enum TradeSide
    {
        Buy,
        Sell
    }

    /// <summary>
    /// A single order that the engine executed. <see cref="Profit"/> is only meaningful on
    /// a <see cref="TradeSide.Sell"/> row, where it holds the net result of the round trip
    /// (both sides of the operation cost already deducted). It is NaN on buy rows.
    /// </summary>
    public sealed class Trade
    {
        public Trade(int barIndex, System.DateTime date, TradeSide side, double price, double quantity, double cost, double equityAfter)
        {
            BarIndex = barIndex;
            Date = date;
            Side = side;
            Price = price;
            Quantity = quantity;
            Cost = cost;
            EquityAfter = equityAfter;
            Profit = double.NaN;
        }

        /// <summary>Index of the bar the order was filled on.</summary>
        public int BarIndex { get; private set; }

        public System.DateTime Date { get; private set; }

        public TradeSide Side { get; private set; }

        public double Price { get; private set; }

        public double Quantity { get; private set; }

        /// <summary>Money paid for this operation, derived from the cost-per-operation setting.</summary>
        public double Cost { get; private set; }

        /// <summary>Portfolio value (cash + open position marked to market) right after the fill.</summary>
        public double EquityAfter { get; private set; }

        /// <summary>Net profit of the round trip; set on sell rows only, NaN otherwise.</summary>
        public double Profit { get; set; }
    }
}
