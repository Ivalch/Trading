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
* **Allow short** - whether a rule may go short, and **borrow, %** - the annual fee for
  doing so.
* **Chart** - price with the indicators and the buy/sell markers, the oscillator band, and
  the equity curve against buy & hold and the starting capital. Hover for a crosshair and
  a readout of every series at that bar.

Sizing is all-in, rounded down to whole lots, and a decision made at the close of a bar is
filled at the **open of the next** one, so the engine can never trade on a price it could not
have seen.

### Shorts

`Allow short` decides whether a rule's `Short` requests are taken. A rule always emits what
it wants; with the switch off the engine holds flat instead, so the same rule can be run both
ways without editing it. `Str.Crossover` exposes this as a `below` parameter (flat or short)
so you can watch the difference. Stats show how many shorts were entered and how much was paid
to borrow.

What the model does and does not do, so the numbers are read honestly:

* **no leverage, no margin call.** A short carries one unit of notional per unit of capital.
  A real short would have been forced flat on the way up, so short results here are optimistic.
* the borrow fee is charged **once, when the short is covered**, on the amount actually
  borrowed: `entry notional x annual rate x days / 365`. Charging on the entry notional
  rather than the cover notional keeps the fee independent of the exit price, so it is easy
  to predict and easy to check by hand.
* opening a short is a **sell** and covering it is a **buy**, which is why the chart's markers
  read the same way as for a long.

## Layout

```
TradingBacktest.Abstractions/   contracts only: Candle, IIndicator + IIndicatorFactory,
                                IStrategy + IStrategyFactory, IndicatorParameterDefinition
Ind.Sma/  Ind.Ema/  Ind.Rsi/    indicator plug-ins, one DLL each: Ind*.dll
Ind.BollingerBands/
Str.Crossover/                  rule plug-ins, one DLL each: Str*.dll
Str.DonchianBreakout/
Str.BuyAndHold/
TradingBacktest/                the WPF host
  Models/                       BacktestResult, StatItem, Trade
  Services/                     BacktestEngine, PluginLoader, FinamDataLoader,
                                FolderDialog, NumberParser, SettingsStore
  Controls/                     BacktestChart, drawn in OnRender; ParameterTemplateSelector
  ViewModels/                   MainViewModel, IndicatorSlotViewModel, StrategySlotViewModel
```

Indicators and rules are plug-ins, by the same contract and with the same loader. The
dependency points inwards, which is the whole point:

```
    host  --->  TradingBacktest.Abstractions  <---  Ind.Sma.dll, Ind.Ema.dll, Str.Crossover.dll, ...
```

The host references the contracts. Each plug-in references the contracts. Neither references
the other, so a plug-in cannot drag host types into itself, and the host is not recompiled
when an indicator changes. The engine never names an indicator:

```csharp
_engine.Run(ticker, source, candles, cost, from, to, indicators, strategy);
```

`BacktestEngine` knows how to charge for an operation and how to size a position. It has no
idea what an SMA is and does not know when to buy, so it never has to change.

## Indicators and rules are plug-ins

At startup `PluginLoader<T>` takes **every `Ind*.dll`** (and an `Indicators` sub-folder, if
there is one) for indicators, and **every `Str*.dll`** (and a `Strategies` sub-folder) for
rules. For each it calls `Assembly.LoadFrom` and constructs every public parameterless type
implementing the matching factory - `IIndicatorFactory` or `IStrategyFactory`. That
construction is the only moment the host touches a plug-in type. A DLL that will not load, or a
factory whose constructor throws, is reported and skipped: one broken plug-in never stops the
app starting. The status line reads
`4 indicators: Bollinger, EMA, RSI, SMA; 3 rules: Average crossover, Buy and hold, Donchian breakout`,
or names the file that failed.

Adding either kind means dropping a DLL in the folder: no project file, no registration, no
edit to the host.

The project entries exist only so the build produces the shipped plug-ins and copies them next
to the executable, and that copy is done by the `CopyIndicatorPlugins` target rather than by
MSBuild. The reason is a subtlety worth knowing: the project references use
`ReferenceOutputAssembly="false"`, which is what keeps the inversion honest - the exe must not
hold a compile-time or run-time reference to any plug-in, or deleting one would break it - but
that same flag is also what stops MSBuild copying the DLL into the output. Drop the flag and you
get the opposite mistake: the host hard-references every plug-in and stops being pluggable. If
the plug-ins are missing you will see `No indicators loaded...` or `No buy/sell rules loaded...`.
Remove those entries and copy the DLLs by hand instead and it behaves identically.

### Writing one

Two types: a factory, which the host reads at startup, and the indicator, built per run.

```csharp
public sealed class RocIndicatorFactory : IIndicatorFactory
{
    public string Id { get { return "Roc"; } }           // stable, used in the settings file
    public string Name { get { return "ROC"; } }         // shown in the drop-down
    public string Description { get { return "Rate of change."; } }

    public IList<IndicatorParameterDefinition> Parameters { get { return new List<...>
    {
        IndicatorParameterDefinition.Integer("Period", "bars", 12, 2, 2000, "Lookback."),
    } } }

    public IIndicator Create(IndicatorParameters parameters)
    {
        return new RocIndicator(parameters.GetInt("Period"));
    }
}
```

The parameter *definitions* drive the UI, so a plug-in declares its own settings and the app
needs no new code. `ParameterTemplateSelector` maps each declared `IndicatorParameterType`
to an editor: integer and number become a box, text becomes a box, choice becomes a
drop-down. Picking EMA therefore shows a *bars* box **and** a *source* drop-down, without the
host knowing what EMA is.

`Calculate` returns one value per bar - `NaN` until the window fills up - and one or more
`IndicatorLine`s, the first being the main one a rule means when it asks for this indicator.
Pass `usesPriceScale: false` for an oscillator so it gets its own band instead of being
squashed against prices in the thousands. Attach `IndicatorLevel`s (RSI's 30 and 70) to have
the chart draw guide lines without it recognising the indicator by name.


### Writing a rule

Same two types, same parameter machinery:

```csharp
public sealed class DonchianBreakoutStrategyFactory : IStrategyFactory
{
    public string Id { get { return "DonchianBreakout"; } }
    public string Name { get { return "Donchian breakout"; } }

    // Static: this instance is built once at startup and cannot know the user's value.
    public string Description { get { return "Long on a new high for the lookback period."; } }

    public IList<IndicatorParameterDefinition> Parameters { get { return new List<...>
    {
        IndicatorParameterDefinition.Integer("Lookback", "bars", 20, 2, 2000, "Previous bars."),
    } } }

    public IStrategy Create(IndicatorParameters parameters)
    {
        return new DonchianBreakoutStrategy(parameters.GetInt("Lookback"));
    }
}
```

`GenerateSignals(StrategyContext)` gets the bars and the indicator output:

```csharp
// context.Candles - the bars
// context.Series  - one entry per filled indicator slot, in slot order; [0].Primary is the main line
```

Emit `Long`, `Short` or `Flat` **only where the position should change**. The engine holds
whatever you last asked for until you ask again - a rule that emits nothing on a bar means
"keep the position", not "go flat".

Two rules about the wording, because the difference is a bug I already made once:

* the **factory's** description is built once at startup, so it must not quote a parameter
  value;
* the **configured rule's** description must, and is read from the instance the plug-in just
  built. The host shows that one, and rebuilds it whenever a parameter changes, so "the previous
  50 bars" is always the number actually in force.

Shipped rules: `Str.Crossover` (long while indicator 1 reads above indicator 2, optionally
waiting `confirm` bars), `Str.DonchianBreakout` (long on a new N bar high) and
`Str.BuyAndHold` (one entry, no parameters at all).

A rule refers to indicators **by slot**, so swapping the two slots flips a crossover. That is
deliberate: the slots are parameters, and the description line under the settings says which
indicator is which.

Settings serialise as `Sma(Period=20)|Ema(Period=20,Source=Close)` for the slots and
`DonchianBreakout(Lookback=50)` for the rule, so a run comes back exactly as it was left.
## Settings

The last folder, ticker, cost, period, rule and both indicator slots (with every parameter)
are remembered in `%LOCALAPPDATA%\TradingBacktest\TradingBacktest.settings` (plain
`key=value`).
