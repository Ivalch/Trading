namespace TradingBacktest.Models
{
    public enum TradeSide
    {
        /// <summary>Buying to open a long, or buying to close a short.</summary>
        Buy,

        /// <summary>Selling to close a long, or selling to open a short.</summary>
        Sell
    }

    /// <summary>
    /// A single order that the engine executed. <see cref="Profit"/> is only meaningful on
    /// the row that closes a position, where it holds the net result of the round trip -
    /// both sides of the operation cost and any borrow fee already deducted.
    /// It is NaN on the row that opens one.
    ///
    /// <see cref="Side"/> is the side of the order, not of the position: opening a short is
    /// a sell, covering it is a buy.
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

        /// <summary>Operation cost charged on this fill, from the cost-per-operation setting.</summary>
        public double Cost { get; private set; }

        /// <summary>
        /// Borrow fee for closing a short: the annual rate prorated over the days the
        /// position was open. Zero on every other row.
        /// </summary>
        public double BorrowFee { get; set; }

        /// <summary>Portfolio value (cash plus the open position marked to market) after the fill.</summary>
        public double EquityAfter { get; private set; }

        /// <summary>Net profit of the round trip; set on the closing row only, NaN otherwise.</summary>
        public double Profit { get; set; }

        /// <summary>Everything the engine paid on this fill, for the fee summary.</summary>
        public double TotalFees
        {
            get { return Cost + BorrowFee; }
        }
    }
}