# Trading Backtest

A WPF desk toy for testing a trading rule on exported price history. Pick a ticker, pick
the folder the history files live in, pick the period and the cost of operation, press
**Start**, and read the result off the chart.

Built for **Visual Studio 2015 / C# 6 / .NET Framework 4.8**. No NuGet packages: the
chart is drawn by hand in `OnRender`, and the MVVM base classes are written out rather
than generated, so the solution opens and builds on the oldest supported toolchain.

## Building

Open `TradingBacktest.sln` in VS 2015 and build. `ToolsVersion` is 14.0 and the projects
are old-style (not SDK-style), which is what VS 2015 understands.

## Data files

The app reads the tab separated files produced by the Finam / Tinkoff "export history"
tool, one row per bar, no header:

```
date time<TAB>open<TAB>close<TAB>high<TAB>low<TAB>volume<TAB>extra
2007-3-23 0:0:0.0	348.0	349.96	351.14	343.1	37343.0	1
```

Two things are easy to get wrong, and the loader checks for both:

* the date and the time share **one** field, separated by a space - the first tab only
  appears after the time;
* the column order is open, **close**, high, low - not OHLLC.

A file name looks like `20211129_TQBR_CHMF_Day`, so the ticker is everything between the
leading date stamp and the trailing timeframe. When several files match, the one with the
longest history wins: `Day`, then `Week`, `Month`, `Hour`, `15M`, `5M`, `Min`. Rows whose
high is below the open or close are rejected rather than plotted wrong, and if that leaves
no bars the app says so instead of drawing an empty chart.

## What the app does

* **Buy/sell rule** - one of the strategies below.
* **Indicators** - two slots, no more. Each slot picks a type and a window. The rule reads
  them in order, so slot 1 is the fast one in a crossover. Anything on the price scale is
  drawn with the price; an oscillator (RSI) gets its own band underneath.
* **Period** - from/to pickers plus 1Y / 3Y / 5Y / All shortcuts, clamped to the data.
* **Cost of operation** - a percentage charged on **both** sides of every trade. Parsed
  with either `,` or `.` as the decimal separator, always shown with two decimals.
* **Chart** - price with the indicators and the buy/sell markers, the oscillator band, and
  the equity curve against buy & hold and the starting capital. Hover for a crosshair and
  a readout of every series at that bar.

The backtest is long/flat only, all-in sizing rounded down to whole lots, and a decision
made at the close of a bar is filled at the **open of the next** one - so the engine can
never trade on a price it could not have seen.

## Layout

```
Models/        Candle, Trade, StatItem, BacktestResult
Indicators/    IIndicator + one class per indicator
Strategies/    IStrategy + one class per buy/sell rule
Services/      BacktestEngine, FinamDataLoader, FolderDialog, NumberParser, SettingsStore
Controls/      BacktestChart - the whole chart, drawn in OnRender
ViewModels/    MainViewModel, IndicatorViewModel
```

The three parts worth keeping apart are the indicators, the rule, and the engine. Each is
an object, and each is replaced by handing a different instance to the same call:

```csharp
_engine.Run(ticker, source, candles, cost, from, to, indicators, strategy);
```

`BacktestEngine` knows how to charge for an operation and how to size a position. It has
no idea what an SMA is and does not know when to buy, so it never has to change.

### Adding an indicator

Implement `IIndicator` and add an instance to `IndicatorCatalog.All`:

```csharp
public sealed class RocIndicator : IIndicator
{
    public string Name { get { return "ROC"; } }
    public int Period { get; private set; }
    public int DefaultPeriod { get { return 12; } }
    public IIndicator WithPeriod(int period) { return new RocIndicator(period); }

    public IList<IndicatorLine> Calculate(IList<Candle> candles)
    {
        // One value per bar, NaN until the window is full. Return several lines if the
        // indicator has more than one series; the first one is the main one.
    }
}
```

Pass `usesPriceScale: false` for an oscillator so it gets its own band instead of being
squashed against prices in the thousands.

### Adding a buy/sell rule

Implement `IStrategy` and add an instance to `StrategyCatalog.All`:

```csharp
public IList<Signal> GenerateSignals(StrategyContext context)
{
    // context.Candles - the bars
    // context.Series  - one entry per indicator, in list order; [0].Primary is its main line
    //                  an RSI rule lives in Strategies/, not in the chart
    // Return only the bars where the wanted position changes.
}
```

Shipped rules: `CrossoverStrategy` (long while indicator 1 reads above indicator 2),
`DonchianBreakoutStrategy` (long on a new N bar high) and `BuyAndHoldStrategy`.

A rule refers to indicators **by slot**, so swapping the two slots flips a crossover. That
is deliberate: the slots are parameters, and the description line under the settings says
which indicator is which.

## Settings

The last folder, ticker, cost, period, rule and both indicator slots are remembered in
`%LOCALAPPDATA%\TradingBacktest\TradingBacktest.settings` (plain `key=value`).
