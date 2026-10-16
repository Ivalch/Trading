namespace TradingBacktest.Abstractions
{
    /// <summary>
    /// One price bar, the input every indicator is calculated from.
    ///
    /// It lives in the contracts assembly rather than in the host so a plug-in never has to
    /// reference the application.
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