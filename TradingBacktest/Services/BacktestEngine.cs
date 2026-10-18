using System;
using System.Collections.Generic;
using TradingBacktest.Abstractions;
using TradingBacktest.Models;

namespace TradingBacktest.Services
{
    /// <summary>
    /// Turns bars plus a set of indicators plus a rule into an equity curve.
    ///
    /// The engine knows how to charge for an operation, how to size a position and how to
    /// carry a short; it has no idea what an SMA is and does not know when to buy. It
    /// therefore never has to change when either side does.
    ///
    /// Mechanics:
    ///  * a decision made at the close of a bar is filled at the open of the next one, so the
    ///    engine can never trade on a price it could not have seen;
    ///  * entries use all available cash, rounded down to whole lots;
    ///  * the operation cost is charged on both sides of every trade;
    ///  * a short carries one unit of notional per unit of capital - no leverage and no
    ///    margin call is modelled, so a short looks kinder than a real one would;
    ///  * the borrow fee on a short is charged once, when it is covered, on the amount
    ///    actually borrowed: entry notional x annual rate x days / 365. Charging on the
    ///    entry notional rather than the cover notional keeps the fee independent of the
    ///    exit price, so it is easy to predict and easy to check.
    /// </summary>
    public sealed class BacktestEngine
    {
        public const double DefaultInitialCapital = 100000.0;
        public const int LotSize = 1;
        public const double DaysPerYear = 365.0;

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
            BacktestSettings settings,
            DateTime from,
            DateTime to,
            IList<IndicatorRequest> requests,
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

            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            if (requests == null)
            {
                throw new ArgumentNullException("requests");
            }

            List<Candle> range = Slice(allCandles, from, to);
            if (range.Count == 0)
            {
                throw new InvalidOperationException("No bars in the selected period.");
            }

            IList<IndicatorSeries> series = Calculate(requests, range);
            IList<Signal> signals = strategy.GenerateSignals(new StrategyContext(range, series));

            int bars = range.Count;
            StrategyAction[] wanted = Spread(signals, bars, settings.AllowShort);

            var equity = new List<double>(bars);
            var buyAndHold = new List<double>(bars);
            var trades = new List<Trade>();

            var book = new Book(_initialCapital, settings);
            double costRate = settings.CostPerOperationPercent / 100.0;

            for (int i = 0; i < bars; i++)
            {
                Candle candle = range[i];

                // Fill the decision made at yesterday's close, at today's open.
                if (i > 0)
                {
                    StrategyAction want = wanted[i - 1];

                    // Close first when the rule turns round, so a flip costs two fills.
                    if (book.Position != 0 && book.Action != want)
                    {
                        book.Close(i, candle, costRate, trades);
                    }

                    if (want != StrategyAction.Flat && book.Position == 0)
                    {
                        book.Open(i, candle, want, costRate, trades);
                    }
                }

                // Mark to market at the close.
                equity.Add(book.ValueAt(candle.Close));
            }

            // Benchmark: everything in the ticker from the first bar of the range.
            double firstClose = range[0].Close;
            for (int i = 0; i < bars; i++)
            {
                buyAndHold.Add(_initialCapital * range[i].Close / firstClose);
            }

            return new BacktestResult(
                ticker,
                source,
                strategy.Name,
                settings,
                _initialCapital,
                range[0].Date,
                range[bars - 1].Date,
                range,
                series,
                equity,
                buyAndHold,
                trades);
        }

        /// <summary>
        /// Turns the sparse decision list into one wanted position per bar. A rule only emits
        /// a signal where the position it wants changes, so the decision has to be carried
        /// forward: defaulting every bar to flat would close a position the bar after it was
        /// opened.
        ///
        /// A Short asked for while shorts are switched off becomes Flat, so the same rule can
        /// be run both ways without editing it.
        /// </summary>
        private static StrategyAction[] Spread(IList<Signal> signals, int bars, bool allowShort)
        {
            var decisions = new StrategyAction[bars];
            var decided = new bool[bars];

            for (int i = 0; i < signals.Count; i++)
            {
                Signal signal = signals[i];
                if (signal.BarIndex < 0 || signal.BarIndex >= bars)
                {
                    continue;
                }

                decisions[signal.BarIndex] = signal.Action;
                decided[signal.BarIndex] = true;
            }

            // Carry the last decision forward: a bar with no signal means "keep what you
            // have", not "go flat".
            var wanted = new StrategyAction[bars];
            StrategyAction held = StrategyAction.Flat;
            for (int i = 0; i < bars; i++)
            {
                if (decided[i])
                {
                    held = decisions[i];
                }

                wanted[i] = held == StrategyAction.Short && !allowShort ? StrategyAction.Flat : held;
            }

            return wanted;
        }

        /// <summary>
        /// Runs every indicator over the bars it was asked for, keeping the declared order so
        /// a rule can refer to them by position.
        ///
        /// An indicator on a coarser timeframe is calculated over that timeframe's own bars
        /// and then spread back across the chart's bars, so from here on everything is one
        /// value per chart bar and nothing downstream has to know a second resolution exists.
        /// </summary>
        private static IList<IndicatorSeries> Calculate(IList<IndicatorRequest> requests, IList<Candle> candles)
        {
            var series = new List<IndicatorSeries>();
            if (requests == null)
            {
                return series;
            }

            for (int i = 0; i < requests.Count; i++)
            {
                IndicatorRequest request = requests[i];
                if (request == null || request.Indicator == null)
                {
                    continue;
                }

                IList<Candle> source = request.UsesHigherTimeframe ? request.HigherTimeframeCandles : candles;

                IndicatorResult result = request.Indicator.Calculate(source);
                if (result == null || result.IsEmpty)
                {
                    continue;
                }

                if (request.UsesHigherTimeframe)
                {
                    result = SpreadDown(result, source, candles, request.Timeframe);
                }

                series.Add(new IndicatorSeries(request.Indicator, result));
            }

            return series;
        }

        /// <summary>
        /// Stretches a coarser timeframe's result across the chart's bars.
        ///
        /// The export stamps every bar with the moment it OPENED, not the moment it closed -
        /// the last bar of a full session is stamped 23:49 because the evening session ends
        /// at 23:50. So a coarser bar stamped S is only finished at the stamp of the next
        /// one, and a chart bar may be given nothing but the coarser bars already finished.
        ///
        /// Concretely, a chart bar at T takes the value of the coarser bar immediately
        /// BEFORE the one running at T. A minute bar at 11:30 still shows the hour that
        /// opened at 10:00, never the one opened at 11:00 that is still running. Taking
        /// the running bar instead would look perfectly smooth on the chart and be a whole
        /// coarse bar of lookahead.
        ///
        /// Consequences that follow from that rule rather than from special cases: the last
        /// coarser bar is never shown, because nothing marks where it would have closed;
        /// and a coarser history that starts after the chart leaves NaN rather than
        /// borrowing a value from ahead.
        /// </summary>
        private static IndicatorResult SpreadDown(IndicatorResult result, IList<Candle> coarse, IList<Candle> chart, string timeframe)
        {
            string suffix = " (" + Timeframes.ShortLabel(timeframe) + ")";
            var lines = new List<IndicatorLine>(result.Lines.Count);

            for (int line = 0; line < result.Lines.Count; line++)
            {
                IndicatorLine source = result.Lines[line];
                var values = new double[chart.Count];

                int pointer = 0;
                for (int i = 0; i < chart.Count; i++)
                {
                    DateTime at = chart[i].Date;

                    // The bar running at or before this moment; whatever it is, it is still
                    // open, so what is shown is the one before it.
                    while (pointer + 1 < coarse.Count && coarse[pointer + 1].Date <= at)
                    {
                        pointer++;
                    }

                    int finished = pointer - 1;
                    values[i] = finished >= 0 && finished < source.Values.Length
                        ? source.Values[finished]
                        : double.NaN;
                }

                lines.Add(new IndicatorLine(source.Label + suffix, values, source.Color, source.UsesPriceScale));
            }

            return new IndicatorResult(lines, result.Levels);
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

        /// <summary>
        /// The running position. Kept apart from the engine so the fill logic reads plainly,
        /// and so a position can never be half long and half short.
        /// </summary>
        private sealed class Book
        {
            private readonly BacktestSettings _settings;
            private double _cash;
            private int _shares;
            private double _entryPrice;
            private double _entryCost;
            private DateTime _entryDate;

            public Book(double capital, BacktestSettings settings)
            {
                _cash = capital;
                _settings = settings;
            }

            /// <summary>1 long, -1 short, 0 flat.</summary>
            public int Position { get; private set; }

            public StrategyAction Action
            {
                get
                {
                    return Position > 0 ? StrategyAction.Long
                        : Position < 0 ? StrategyAction.Short
                        : StrategyAction.Flat;
                }
            }

            /// <summary>Cash plus the open position marked to market.</summary>
            public double ValueAt(double price)
            {
                return _cash + Position * _shares * price;
            }

            public void Open(int bar, Candle candle, StrategyAction action, double costRate, List<Trade> trades)
            {
                int shares = (int)Math.Floor(_cash / candle.Open / LotSize) * LotSize;
                if (shares <= 0)
                {
                    return;
                }

                double value = shares * candle.Open;
                double cost = value * costRate;

                if (action == StrategyAction.Long)
                {
                    _cash -= value + cost;
                }
                else
                {
                    // The sale proceeds sit in cash; the obligation is the position itself.
                    _cash += value - cost;
                }

                _shares = shares;
                Position = action == StrategyAction.Long ? 1 : -1;
                _entryPrice = candle.Open;
                _entryCost = cost;
                _entryDate = candle.Date;

                // Opening a long is a purchase; opening a short is a sale.
                trades.Add(new Trade(
                    bar,
                    candle.Date,
                    action == StrategyAction.Long ? TradeSide.Buy : TradeSide.Sell,
                    candle.Open,
                    shares,
                    cost,
                    ValueAt(candle.Close)));
            }

            public void Close(int bar, Candle candle, double costRate, List<Trade> trades)
            {
                double value = _shares * candle.Open;
                double cost = value * costRate;
                double borrowFee = 0;

                if (Position > 0)
                {
                    _cash += value - cost;
                }
                else
                {
                    // Covering: hand the shares back, plus whatever the borrow has cost.
                    borrowFee = BorrowFee(candle.Date);
                    _cash -= value + cost + borrowFee;
                }

                double opened = _entryPrice * _shares - _entryCost;
                double closed = value + cost + borrowFee;
                double profit = Position > 0 ? closed - opened : opened - closed;

                var trade = new Trade(
                    bar,
                    candle.Date,
                    Position > 0 ? TradeSide.Sell : TradeSide.Buy,
                    candle.Open,
                    _shares,
                    cost,
                    ValueAt(candle.Close))
                {
                    BorrowFee = borrowFee,
                    Profit = profit
                };
                trades.Add(trade);

                _shares = 0;
                Position = 0;
                _entryPrice = 0;
                _entryCost = 0;
                _entryDate = default(DateTime);
            }

            /// <summary>
            /// What was borrowed x the annual rate x the fraction of a year it was borrowed
            /// for. Charged once, on the cover, so the equity curve only ever sees it at a
            /// decision point.
            /// </summary>
            private double BorrowFee(DateTime on)
            {
                if (_settings.ShortAnnualFeePercent <= 0)
                {
                    return 0;
                }

                double days = (on - _entryDate).TotalDays;
                if (days <= 0)
                {
                    return 0;
                }

                double borrowed = _entryPrice * _shares;
                return borrowed * (_settings.ShortAnnualFeePercent / 100.0) * (days / DaysPerYear);
            }
        }
    }
}