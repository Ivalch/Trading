namespace TradingBacktest.Models
{
    /// <summary>
    /// One daily price bar. Only the fields the backtester needs are kept - no volume,
    /// splits or dividends, because the demo strategy trades on closes alone.
    /// </summary>
    public sealed class Candle
    {
        public Candle(System.DateTime date, double open, double high, double low, double close)
        {
            Date = date;
            Open = open;
            High = high;
            Low = low;
            Close = close;
        }

        public System.DateTime Date { get; private set; }

        public double Open { get; private set; }

        public double High { get; private set; }

        public double Low { get; private set; }

        public double Close { get; private set; }
    }
}
