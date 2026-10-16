using System;
using System.Collections.Generic;
using TradingBacktest.Abstractions;
using TradingBacktest.Strategies;

namespace TradingBacktest.Models
{
    /// <summary>
    /// Everything the chart and the summary strip need. The engine returns one of these
    /// per run; the view model simply hands the latest one to the chart.
    /// </summary>
    public sealed class BacktestResult
    {
        public BacktestResult(
            string ticker,
            string source,
            string strategy,
            double costPerOperationPercent,
            double initialCapital,
            DateTime from,
            DateTime to,
            IList<Candle> candles,
            IList<IndicatorSeries> series,
            IList<double> equity,
            IList<double> buyAndHoldEquity,
            IList<Trade> trades)
        {
            Ticker = ticker;
            Source = source;
            Strategy = strategy;
            CostPerOperationPercent = costPerOperationPercent;
            InitialCapital = initialCapital;
            From = from;
            To = to;
            Candles = candles;
            Series = series;
            Equity = equity;
            BuyAndHoldEquity = buyAndHoldEquity;
            Trades = trades;

            var flat = new List<IndicatorLine>();
            for (int i = 0; i < series.Count; i++)
            {
                IList<IndicatorLine> produced = series[i].Lines;
                for (int j = 0; j < produced.Count; j++)
                {
                    flat.Add(produced[j]);
                }
            }

            Lines = flat;
        }

        public string Ticker { get; private set; }

        /// <summary>Human readable note about where the prices came from (file name or "generated").</summary>
        public string Source { get; private set; }

        /// <summary>Name of the rule that produced the trades.</summary>
        public string Strategy { get; private set; }

        public double CostPerOperationPercent { get; private set; }

        public double InitialCapital { get; private set; }

        public DateTime From { get; private set; }

        public DateTime To { get; private set; }

        /// <summary>Bars inside the requested range, oldest first.</summary>
        public IList<Candle> Candles { get; private set; }

        /// <summary>One entry per configured indicator, in list order.</summary>
        public IList<IndicatorSeries> Series { get; private set; }

        /// <summary>Every plotted line, flattened. This is what the chart draws.</summary>
        public IList<IndicatorLine> Lines { get; private set; }

        /// <summary>Portfolio value per bar, including the cash earned by closed trades.</summary>
        public IList<double> Equity { get; private set; }

        /// <summary>Benchmark: all money in the ticker from the first bar of the range.</summary>
        public IList<double> BuyAndHoldEquity { get; private set; }

        public IList<Trade> Trades { get; private set; }

        public double FinalEquity
        {
            get { return Equity.Count == 0 ? InitialCapital : Equity[Equity.Count - 1]; }
        }

        public double NetProfit
        {
            get { return FinalEquity - InitialCapital; }
        }

        public double ReturnPercent
        {
            get { return InitialCapital == 0 ? 0 : NetProfit / InitialCapital * 100.0; }
        }

        public double BuyAndHoldReturnPercent
        {
            get
            {
                if (BuyAndHoldEquity.Count == 0 || InitialCapital == 0)
                {
                    return 0;
                }

                return (BuyAndHoldEquity[BuyAndHoldEquity.Count - 1] - InitialCapital) / InitialCapital * 100.0;
            }
        }

        /// <summary>Number of round trips that were closed inside the range.</summary>
        public int ClosedTradeCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Trades.Count; i++)
                {
                    if (Trades[i].Side == TradeSide.Sell)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public double WinRatePercent
        {
            get
            {
                int wins = 0;
                int closed = 0;
                for (int i = 0; i < Trades.Count; i++)
                {
                    Trade trade = Trades[i];
                    if (trade.Side != TradeSide.Sell)
                    {
                        continue;
                    }

                    closed++;
                    if (trade.Profit > 0)
                    {
                        wins++;
                    }
                }

                return closed == 0 ? 0 : (double)wins / closed * 100.0;
            }
        }

        /// <summary>Largest peak-to-trough drop of the equity curve, as a positive percentage.</summary>
        public double MaxDrawdownPercent
        {
            get
            {
                double peak = double.MinValue;
                double worst = 0;
                for (int i = 0; i < Equity.Count; i++)
                {
                    double value = Equity[i];
                    if (value > peak)
                    {
                        peak = value;
                    }

                    if (peak > 0)
                    {
                        double drop = (peak - value) / peak * 100.0;
                        if (drop > worst)
                        {
                            worst = drop;
                        }
                    }
                }

                return worst;
            }
        }
    }
}
