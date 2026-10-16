using System;
using System.Collections.Generic;
using TradingBacktest.Abstractions;
using TradingBacktest.Models;
using TradingBacktest.Strategies;

namespace TradingBacktest.Services
{
    /// <summary>
    /// Turns bars plus a set of indicators plus a rule into an equity curve.
    ///
    /// The engine knows how to charge for an operation and how to size a position; it has
    /// no idea what an SMA is or when to buy. It therefore never needs changing when either
    /// side does.
    ///
    /// Mechanics:
    ///  * a decision made at the close of bar N is filled at the open of bar N + 1;
    ///  * entries use all available cash, rounded down to whole lots;
    ///  * the cost of operation is charged on both sides of every trade.
    /// </summary>
    public sealed class BacktestEngine
    {
        public const double DefaultInitialCapital = 100000.0;
        public const int LotSize = 1;

        private readonly double _initialCapital;

        public BacktestEngine()
            : this(DefaultInitialCapital)
        {
        }

        public BacktestEngine(double initialCapital)
        {
            _initialCapital = initialCapital <= 0 ? DefaultInitialCapital : initialCapital;
        }

        public double InitialCapital
        {
            get { return _initialCapital; }
        }

        /// <summary>
        /// Bars needed before the rule and the indicators can produce a usable decision.
        /// </summary>
        public static int RequiredBars(IList<IIndicator> indicators, IStrategy strategy)
        {
            int required = strategy == null ? 1 : Math.Max(1, strategy.MinBars);

            if (indicators != null)
            {
                for (int i = 0; i < indicators.Count; i++)
                {
                    if (indicators[i] != null)
                    {
                        required = Math.Max(required, indicators[i].MinBars);
                    }
                }
            }

            return required + 1;
        }

        /// <summary>
        /// Runs the backtest over the bars that fall inside [<paramref name="from"/>,
        /// <paramref name="to"/>].
        /// </summary>
        public BacktestResult Run(
            string ticker,
            string source,
            IList<Candle> allCandles,
            double costPerOperationPercent,
            DateTime from,
            DateTime to,
            IList<IIndicator> indicators,
            IStrategy strategy)
        {
            if (allCandles == null)
            {
                throw new ArgumentNullException("allCandles");
            }

            if (strategy == null)
            {
                throw new ArgumentNullException("strategy");
            }

            List<Candle> range = Slice(allCandles, from, to);
            if (range.Count == 0)
            {
                throw new InvalidOperationException("No bars in the selected period.");
            }

            IList<IndicatorSeries> series = Calculate(indicators, range);
            IList<Signal> signals = strategy.GenerateSignals(new StrategyContext(range, series));

            int count = range.Count;
            StrategyAction[] wanted = Spread(signals, count);

            var equity = new List<double>(count);
            var buyAndHold = new List<double>(count);
            var trades = new List<Trade>();

            double cash = _initialCapital;
            int shares = 0;
            double buyPrice = 0;
            double buyCost = 0;
            bool inPosition = false;
            double costRate = costPerOperationPercent / 100.0;

            for (int i = 0; i < count; i++)
            {
                Candle candle = range[i];

                // Fill the decision that was made at yesterday's close, at today's open.
                if (i > 0)
                {
                    if (wanted[i - 1] == StrategyAction.Long && !inPosition)
                    {
                        shares = (int)Math.Floor(cash / candle.Open / LotSize) * LotSize;
                        if (shares > 0)
                        {
                            double value = shares * candle.Open;
                            buyCost = value * costRate;
                            cash -= value + buyCost;
                            inPosition = true;
                            buyPrice = candle.Open;
                            trades.Add(new Trade(i, candle.Date, TradeSide.Buy, candle.Open, shares, buyCost, cash + shares * candle.Close));
                        }
                    }
                    else if (wanted[i - 1] == StrategyAction.Flat && inPosition)
                    {
                        double value = shares * candle.Open;
                        double cost = value * costRate;
                        double proceeds = value - cost;
                        cash += proceeds;

                        var sell = new Trade(i, candle.Date, TradeSide.Sell, candle.Open, shares, cost, cash);
                        sell.Profit = proceeds - (buyPrice * shares + buyCost);
                        trades.Add(sell);

                        shares = 0;
                        inPosition = false;
                    }
                }

                // Mark to market at the close.
                equity.Add(cash + shares * candle.Close);
            }

            // Benchmark: everything in the ticker from the first bar of the range.
            double firstClose = range[0].Close;
            for (int i = 0; i < count; i++)
            {
                buyAndHold.Add(_initialCapital * range[i].Close / firstClose);
            }

            return new BacktestResult(
                ticker,
                source,
                strategy.Name,
                costPerOperationPercent,
                _initialCapital,
                range[0].Date,
                range[count - 1].Date,
                range,
                series,
                equity,
                buyAndHold,
                trades);
        }

        /// <summary>
        /// Turns the sparse decision list into one wanted position per bar, so the fill
        /// loop stays a simple "what did I want to hold at the previous close".
        /// </summary>
        private static StrategyAction[] Spread(IList<Signal> signals, int count)
        {
            var wanted = new StrategyAction[count];
            for (int i = 0; i < count; i++)
            {
                wanted[i] = StrategyAction.Flat;
            }

            for (int i = 0; i < signals.Count; i++)
            {
                Signal signal = signals[i];
                if (signal.BarIndex >= 0 && signal.BarIndex < count)
                {
                    wanted[signal.BarIndex] = signal.Action;
                }
            }

            return wanted;
        }

        /// <summary>
        /// Runs every indicator over the bars, keeping the declared order so a rule can
        /// refer to them by position.
        /// </summary>
        private static IList<IndicatorSeries> Calculate(IList<IIndicator> indicators, IList<Candle> candles)
        {
            var series = new List<IndicatorSeries>();
            if (indicators == null)
            {
                return series;
            }

            for (int i = 0; i < indicators.Count; i++)
            {
                IIndicator indicator = indicators[i];
                if (indicator == null)
                {
                    continue;
                }

                IndicatorResult result = indicator.Calculate(candles);
                if (result != null && !result.IsEmpty)
                {
                    series.Add(new IndicatorSeries(indicator, result));
                }
            }

            return series;
        }

        /// <summary>Keeps the bars inside the inclusive date range, preserving order.</summary>
        public static List<Candle> Slice(IList<Candle> candles, DateTime from, DateTime to)
        {
            DateTime start = from.Date;
            DateTime end = to.Date;

            var result = new List<Candle>();
            for (int i = 0; i < candles.Count; i++)
            {
                Candle candle = candles[i];
                if (candle.Date.Date >= start && candle.Date.Date <= end)
                {
                    result.Add(candle);
                }
            }

            return result;
        }

        /// <summary>First and last bar dates of the whole file, used to seed the date pickers.</summary>
        public static void GetDataSpan(IList<Candle> candles, out DateTime from, out DateTime to)
        {
            if (candles == null || candles.Count == 0)
            {
                from = DateTime.Today.AddYears(-1);
                to = DateTime.Today;
                return;
            }

            from = candles[0].Date.Date;
            to = candles[candles.Count - 1].Date.Date;
        }
    }
}
