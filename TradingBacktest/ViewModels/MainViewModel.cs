using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using TradingBacktest.Abstractions;
using TradingBacktest.Models;
using TradingBacktest.Mvvm;
using TradingBacktest.Services;

namespace TradingBacktest.ViewModels
{
    /// <summary>
    /// Settings and the run loop.
    ///
    /// Parameters: ticker, data folder, period, cost of operation, the two indicator slots
    /// and the buy/sell rule. Pressing Start takes whatever the slots and the drop-down
    /// currently say, hands them to the engine and publishes the result for the chart.
    ///
    /// The indicators are not known here. They arrive as <see cref="IIndicatorFactory"/>
    /// instances loaded from Ind*.dll at startup, so nothing in this class names a concrete
    /// indicator.
    /// </summary>
    public sealed class MainViewModel : ObservableObject
    {
        private const string KeyDirectory = "DataDirectory";
        private const string KeyTicker = "Ticker";
        private const string KeyCost = "CostPercent";
        private const string KeyFrom = "From";
        private const string KeyTo = "To";
        private const string KeyIndicators = "Indicators";
        private const string KeyStrategy = "Strategy";
        private const string KeyAllowShort = "AllowShort";
        private const string KeyShortFee = "ShortAnnualFeePercent";
        private const string KeyTimeframe = "Timeframe";

        private readonly BacktestEngine _engine = new BacktestEngine();
        private readonly SettingsStore _settings = new SettingsStore();
        private readonly PluginLoader<IIndicatorFactory> _indicators =
            new PluginLoader<IIndicatorFactory>(PluginLoader<IIndicatorFactory>.IndicatorPattern, PluginLoader<IIndicatorFactory>.IndicatorFolder, "indicator");
        private readonly PluginLoader<IStrategyFactory> _rules =
            new PluginLoader<IStrategyFactory>(PluginLoader<IStrategyFactory>.StrategyPattern, PluginLoader<IStrategyFactory>.StrategyFolder, "rule");

        private string _ticker;
        private string _costText;
        private string _dataDirectory;
        private DateTime? _fromDate;
        private DateTime? _toDate;
        private bool _isBusy;
        private bool _allowShort;
        private string _shortFeeText;
        private string _statusText;
        private string _errorText;
        private string _strategyHint;
        private string _pluginHint;
        private BacktestResult _result;
        private StrategySlotViewModel _strategySlot;
        private IndicatorSlotViewModel _slot1;
        private IndicatorSlotViewModel _slot2;
        private ObservableCollection<StatItem> _stats = new ObservableCollection<StatItem>();
        private ObservableCollection<string> _availableTickers = new ObservableCollection<string>();
        private ObservableCollection<TimeframeOption> _availableTimeframes = new ObservableCollection<TimeframeOption>();
        private string _timeframe;

        // Bars for the timeframes an indicator is calculated on. Keyed by file, so
        // switching back and forth between two slots does not re-read anything.
        private readonly Dictionary<string, IList<Candle>> _timeframeCache =
            new Dictionary<string, IList<Candle>>(StringComparer.OrdinalIgnoreCase);

        // Candles are kept between runs so changing only the cost, the period or the rule
        // does not re-read the file from disk.
        private IList<Candle> _cachedCandles;
        private string _cachedKey = string.Empty;
        private string _cachedFile = string.Empty;
        private DateTime _dataFrom;
        private DateTime _dataTo;

        public MainViewModel()
        {
            _ticker = _settings.Get(KeyTicker) ?? "TQBR_CHMF";
            _costText = NumberParser.FormatPercent(_settings.GetDouble(KeyCost, 0.10));
            _allowShort = _settings.GetBool(KeyAllowShort, false);
            _shortFeeText = NumberParser.FormatPercent(_settings.GetDouble(KeyShortFee, 0.0));
            _dataDirectory = _settings.Get(KeyDirectory) ?? SuggestDirectory();
            _fromDate = _settings.GetDate(KeyFrom);
            _toDate = _settings.GetDate(KeyTo);
            _timeframe = _settings.Get(KeyTimeframe);

            // Start every Ind*.dll and Str*.dll we can find, before anything asks for one.
            _indicators.Load();
            _rules.Load();
            _pluginHint = _indicators.Summary() + "; " + _rules.Summary();

            _strategySlot = new StrategySlotViewModel();
            RestoreStrategy(_settings.Get(KeyStrategy));

            _slot1 = new IndicatorSlotViewModel();
            _slot2 = new IndicatorSlotViewModel();
            RestoreIndicators(_settings.Get(KeyIndicators));

            StartCommand = new RelayCommand(OnStart, () => !_isBusy);
            BrowseFolderCommand = new RelayCommand(OnBrowseFolder);
            RefreshTickersCommand = new RelayCommand(ReloadTickers);
            PeriodCommand = new RelayCommand(OnPeriodPreset);

            ReloadTickers();
            RefreshSlotTimeframes();

            // Show something on screen immediately rather than an empty frame.
            OnStart();
        }

        // ------------------------------------------------------------------ inputs

        /// <summary>
        /// Ticker name. Free text, but the combo box offers whatever the chosen folder
        /// actually contains, so a typo does not turn into a silent "no data" run.
        /// </summary>
        public string Ticker
        {
            get { return _ticker; }
            set
            {
                if (SetProperty(ref _ticker, value))
                {
                    OnPropertyChanged("TickerHint");
                }
            }
        }

        /// <summary>
        /// Cost of one buy or sell, in percent, shown with two decimals. Kept as text so a
        /// half-typed "0," does not get rewritten to 0.00 under the caret.
        /// </summary>
        public string CostText
        {
            get { return _costText; }
            set { SetProperty(ref _costText, value); }
        }

        /// <summary>
        /// Whether a rule may go short. Off by default: a short here carries no leverage and
        /// no margin call, so it flatters the result compared with a real one.
        /// </summary>
        public bool AllowShort
        {
            get { return _allowShort; }
            set
            {
                if (SetProperty(ref _allowShort, value))
                {
                    OnPropertyChanged("ShortFeeEnabled");
                }
            }
        }

        /// <summary>
        /// Annual borrow fee on a short, in percent. Greyed out while shorts are off, because
        /// it then has no effect at all.
        /// </summary>
        public string ShortAnnualFeeText
        {
            get { return _shortFeeText; }
            set { SetProperty(ref _shortFeeText, value); }
        }

        public bool ShortFeeEnabled
        {
            get { return _allowShort; }
        }

        /// <summary>
        /// The bar size the chart shows. Only timeframes the ticker actually has a file for
        /// are offered, so an intraday view cannot be asked for over daily data.
        /// </summary>
        public string Timeframe
        {
            get { return _timeframe; }
            set
            {
                string code = value == null ? string.Empty : value.Trim();
                if (!SetProperty(ref _timeframe, code))
                {
                    return;
                }

                RefreshSlotTimeframes();
                OnPropertyChanged("TimeframeLabel");
            }
        }

        /// <summary>Name of the chosen bar size, for the status line.</summary>
        public string TimeframeLabel
        {
            get { return Timeframes.Describe(_timeframe); }
        }

        /// <summary>The timeframes available for the current ticker, finest first.</summary>
        public ObservableCollection<TimeframeOption> AvailableTimeframes
        {
            get { return _availableTimeframes; }
        }

        /// <summary>
        /// Offers each indicator slot the coarser timeframes this ticker has a file for.
        /// Called whenever the chart's timeframe, ticker or folder changes, because all
        /// three decide what a slot is allowed to ask for.
        /// </summary>
        private void RefreshSlotTimeframes()
        {
            var options = new List<TimeframeOption>();
            options.Add(new TimeframeOption(Timeframes.Same, "same bars"));

            IList<string> coarser = Timeframes.CoarserThan(_timeframe);
            for (int i = 0; i < coarser.Count; i++)
            {
                if (HasTimeframeFile(coarser[i]))
                {
                    options.Add(new TimeframeOption(coarser[i], Timeframes.Display(coarser[i])));
                }
            }

            if (_slot1 != null)
            {
                _slot1.SetTimeframeOptions(options);
            }

            if (_slot2 != null)
            {
                _slot2.SetTimeframeOptions(options);
            }

            OnPropertyChanged("HasUpperTimeframes");
            OnPropertyChanged("UpperTimeframesHint");
        }

        /// <summary>
        /// False when the chart is already on the coarsest bar size, so a slot has nothing to
        /// be calculated on above it.
        /// </summary>
        public bool HasUpperTimeframes
        {
            get { return _slot1 == null || _slot1.TimeframeOptions.Count > 1; }
        }

        /// <summary>Says why the switch is disabled, instead of leaving it looking broken.</summary>
        public string UpperTimeframesHint
        {
            get
            {
                return HasUpperTimeframes
                    ? "Calculate this indicator on a coarser timeframe and step the result down onto these bars."
                    : "Nothing is coarser than the chart's bar size, so both indicators use the chart's own bars.";
            }
        }

        /// <summary>
        /// The bar size to open on when nothing usable is stored: daily when the ticker has
        /// it, otherwise the coarsest history on offer.
        ///
        /// Daily because that is this app's established preference - the file loader already
        /// documents that a ticker with both daily and intraday exports is tested on the
        /// daily one - and because it is the most history at a size with detail worth
        /// reading. The coarsest file is a poor default: this ticker's monthly history is
        /// 198 bars, of which the 1Y preset would show twelve.
        /// </summary>
        private string DefaultTimeframe()
        {
            for (int i = 0; i < _availableTimeframes.Count; i++)
            {
                if (string.Equals(_availableTimeframes[i].Code, "Day", StringComparison.OrdinalIgnoreCase))
                {
                    return _availableTimeframes[i].Code;
                }
            }

            string coarsest = string.Empty;
            for (int i = 0; i < _availableTimeframes.Count; i++)
            {
                if (coarsest.Length == 0 || Timeframes.Index(_availableTimeframes[i].Code) > Timeframes.Index(coarsest))
                {
                    coarsest = _availableTimeframes[i].Code;
                }
            }

            return coarsest;
        }

        /// <summary>True when the drop-down already lists exactly these, so leave it alone.</summary>
        private bool SameTimeframes(IList<string> available)
        {
            if (available.Count != _availableTimeframes.Count)
            {
                return false;
            }

            for (int i = 0; i < available.Count; i++)
            {
                if (!string.Equals(_availableTimeframes[i].Code, available[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasTimeframeFile(string timeframe)
        {
            return FinamDataLoader.FindFileForTimeframe(_dataDirectory, CurrentTicker(), timeframe) != null;
        }

        private string CurrentTicker()
        {
            return _ticker == null ? string.Empty : _ticker.Trim();
        }

        /// <summary>Folder holding the exported history files (*.dat).</summary>
        public string DataDirectory
        {
            get { return _dataDirectory; }
            set
            {
                if (SetProperty(ref _dataDirectory, value))
                {
                    OnPropertyChanged("DirectoryExists");
                    OnPropertyChanged("DirectoryHint");
                }
            }
        }

        public bool DirectoryExists
        {
            get { return NumberParser.IsUsableDirectory(_dataDirectory); }
        }

        public string DirectoryHint
        {
            get { return DirectoryExists ? "Folder found." : "Folder not found - pick one with Browse."; }
        }

        public string TickerHint
        {
            get
            {
                string file = CurrentFile();
                return file == null ? "No file for this ticker in the folder." : Path.GetFileName(file);
            }
        }

        /// <summary>First day of the tested period.</summary>
        public DateTime? FromDate
        {
            get { return _fromDate; }
            set
            {
                if (SetProperty(ref _fromDate, value) && value.HasValue && _toDate.HasValue && value.Value > _toDate.Value)
                {
                    ToDate = value;
                }
            }
        }

        /// <summary>Last day of the tested period.</summary>
        public DateTime? ToDate
        {
            get { return _toDate; }
            set
            {
                if (SetProperty(ref _toDate, value) && value.HasValue && _fromDate.HasValue && value.Value < _fromDate.Value)
                {
                    FromDate = value;
                }
            }
        }

        // ------------------------------------------------- indicators and strategy

        /// <summary>
        /// Indicator types found in the Ind*.dll files at startup. Drop a DLL in the folder
        /// and it shows up here; nothing else has to change.
        /// </summary>
        public IList<IIndicatorFactory> AvailableIndicators
        {
            get { return _indicators.Plugins; }
        }

        /// <summary>
        /// Rules found in the Str*.dll files at startup. Same arrangement as the indicators:
        /// drop a DLL in and it shows up here.
        /// </summary>
        public IList<IStrategyFactory> AvailableStrategies
        {
            get { return _rules.Plugins; }
        }

        /// <summary>
        /// The two indicator slots, in slot order. A rule refers to indicators by position,
        /// so filling slot 2 instead of slot 1 changes what it trades on.
        /// </summary>
        public IndicatorSlotViewModel Indicator1
        {
            get { return _slot1; }
        }

        public IndicatorSlotViewModel Indicator2
        {
            get { return _slot2; }
        }

        /// <summary>
        /// The rule in force and its parameters. Exposed as a slot, exactly like the
        /// indicator slots, so the UI treats both the same way.
        /// </summary>
        public StrategySlotViewModel SelectedStrategy
        {
            get { return _strategySlot; }
            set
            {
                if (value == null || ReferenceEquals(value, _strategySlot))
                {
                    return;
                }

                if (_strategySlot != null)
                {
                    _strategySlot.PropertyChanged -= OnStrategyChanged;
                }

                if (SetProperty(ref _strategySlot, value))
                {
                    value.PropertyChanged += OnStrategyChanged;
                    _strategyHint = value.Description;
                    OnPropertyChanged("StrategyHint");
                }
            }
        }

        /// <summary>
        /// Keeps the hint truthful when a rule's own parameter changes. A rule that says
        /// "the previous 20 bars" has to say 50 once the lookback is 50, and the rule only
        /// rewrites its text when it is rebuilt - so rebuild it here.
        /// </summary>
        private void OnStrategyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "Description")
            {
                return;
            }

            var slot = sender as StrategySlotViewModel;
            if (slot == null)
            {
                return;
            }

            _strategyHint = slot.Description;
            OnPropertyChanged("StrategyHint");
        }

        public string StrategyHint
        {
            get { return _strategyHint; }
        }

        /// <summary>"4 indicators: Bollinger, EMA, RSI, SMA", or the load failures.</summary>
        public string PluginHint
        {
            get { return _pluginHint; }
        }

        // ------------------------------------------------------------------ outputs

        public ICommand StartCommand { get; private set; }

        public ICommand BrowseFolderCommand { get; private set; }

        public ICommand RefreshTickersCommand { get; private set; }

        /// <summary>Takes a parameter of "1Y", "3Y", "5Y" or "All".</summary>
        public ICommand PeriodCommand { get; private set; }

        public BacktestResult Result
        {
            get { return _result; }
            private set { SetProperty(ref _result, value); }
        }

        public ObservableCollection<StatItem> Stats
        {
            get { return _stats; }
        }

        public ObservableCollection<string> AvailableTickers
        {
            get { return _availableTickers; }
        }

        public string StatusText
        {
            get { return _statusText; }
            private set { SetProperty(ref _statusText, value); }
        }

        public string ErrorText
        {
            get { return _errorText; }
            set { SetProperty(ref _errorText, value); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set { SetProperty(ref _isBusy, value); }
        }

        public bool CanBrowse
        {
            get { return !_isBusy; }
        }

        // ------------------------------------------------------------------ commands

        private void OnBrowseFolder()
        {
            string chosen = FolderDialog.Browse(_dataDirectory);
            if (string.IsNullOrEmpty(chosen))
            {
                return;
            }

            DataDirectory = chosen;
            ReloadTickers();
        }

        private void OnPeriodPreset(object parameter)
        {
            if (string.IsNullOrEmpty(_dataDirectory) || !EnsureDataLoaded())
            {
                return;
            }

            string preset = parameter == null ? "All" : parameter.ToString();
            if (preset == "All")
            {
                FromDate = _dataFrom;
                ToDate = _dataTo;
            }
            else
            {
                int years;
                if (int.TryParse(preset.TrimEnd('Y', 'y'), NumberStyles.Integer, CultureInfo.InvariantCulture, out years))
                {
                    DateTime to = _dataTo;
                    DateTime from = to.AddYears(-years);
                    FromDate = from < _dataFrom ? _dataFrom : from;
                    ToDate = to;
                }
            }

            OnStart();
        }

        private async void OnStart()
        {
            if (_isBusy)
            {
                return;
            }

            ErrorText = null;
            _isBusy = true;
            OnPropertyChanged("CanBrowse");

            try
            {
                double cost;
                if (!NumberParser.TryParsePercent(_costText, out cost))
                {
                    ErrorText = "Cost of operation must be a number between 0 and 100, for example 0,10.";
                    return;
                }

                double shortFee = 0;
                if (_allowShort && !NumberParser.TryParsePercent(_shortFeeText, out shortFee))
                {
                    ErrorText = "The annual borrow fee must be a number between 0 and 100, for example 12,00.";
                    return;
                }

                if (string.IsNullOrEmpty(_ticker) || _ticker.Trim().Length == 0)
                {
                    ErrorText = "Enter a ticker name.";
                    return;
                }

                if (!NumberParser.IsUsableDirectory(_dataDirectory))
                {
                    ErrorText = "Data folder not found: " + NumberParser.Ellipsize(_dataDirectory, 60);
                    return;
                }

                if (_fromDate.HasValue && _toDate.HasValue && _fromDate.Value > _toDate.Value)
                {
                    ErrorText = "The start of the period is after its end.";
                    return;
                }

                // With no plug-in there is nothing to plot and nothing for a rule to read,
                // so say so plainly instead of letting the rule fail on an empty list.
                if (_indicators.Plugins.Count == 0)
                {
                    ErrorText = "No indicators loaded. Every Ind*.dll next to the executable is started at "
                        + "startup - build the Ind.* projects, or copy their DLLs into "
                        + NumberParser.Ellipsize(AppDomain.CurrentDomain.BaseDirectory, 70) + ".";
                    return;
                }

                if (_rules.Plugins.Count == 0)
                {
                    ErrorText = "No buy/sell rules loaded. Every Str*.dll next to the executable is started "
                        + "at startup - build the Str.* projects, or copy their DLLs into "
                        + NumberParser.Ellipsize(AppDomain.CurrentDomain.BaseDirectory, 70) + ".";
                    return;
                }

                // The ticker may have been typed since the lists were last read, so they are
                // rechecked here rather than on every keystroke - the same reason the ticker
                // drop-down is not rebuilt while the name is being entered.
                ReloadTimeframes();

                if (!EnsureDataLoaded())
                {
                    return;
                }

                DateTime from = _fromDate ?? _dataFrom;
                DateTime to = _toDate ?? _dataTo;
                string ticker = _ticker.Trim().ToUpperInvariant();
                string source = Path.GetFileName(_cachedFile);

                IList<IndicatorRequest> requests = BuildRequests();
                IStrategy strategy = _strategySlot == null ? null : _strategySlot.Build();

                var runSettings = new BacktestSettings
                {
                    CostPerOperationPercent = cost,
                    AllowShort = _allowShort,
                    ShortAnnualFeePercent = shortFee
                };

                BacktestResult result = await Task.Run(delegate
                {
                    return _engine.Run(ticker, source, _cachedCandles, runSettings, from, to, requests, strategy);
                });

                Result = result;
                RebuildStats(result);
                StatusText = string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} {1} - {2} bars, {3} - {4}, {5} trades, {6}",
                    ticker,
                    Timeframes.Display(_timeframe).ToLower(CultureInfo.CurrentCulture),
                    result.Candles.Count,
                    result.From.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture),
                    result.To.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture),
                    result.ClosedTradeCount,
                    runSettings.Describe());

                SaveSettings(runSettings, from, to);
            }
            catch (Exception ex)
            {
                ErrorText = ex.Message;
            }
            finally
            {
                _isBusy = false;
                OnPropertyChanged("CanBrowse");
            }
        }

        // ------------------------------------------------------------------ data

        private void ReloadTickers()
        {
            IList<string> tickers = FinamDataLoader.ListTickers(_dataDirectory);
            ReloadTimeframes();

            bool keepCurrent = false;
            for (int i = 0; i < tickers.Count; i++)
            {
                if (string.Equals(tickers[i], _ticker, StringComparison.OrdinalIgnoreCase))
                {
                    keepCurrent = true;
                    break;
                }
            }

            _availableTickers.Clear();
            for (int i = 0; i < tickers.Count; i++)
            {
                _availableTickers.Add(tickers[i]);
            }

            if (_availableTickers.Count > 0 && !keepCurrent)
            {
                Ticker = _availableTickers[0];
            }

            OnPropertyChanged("DirectoryExists");
            OnPropertyChanged("DirectoryHint");
            OnPropertyChanged("TickerHint");
        }

        /// <summary>
        /// Rebuilds the list of bar sizes the current ticker has files for, and picks a
        /// default when the stored one is no longer on offer - a ticker exported only
        /// daily cannot be asked for minutes.
        /// </summary>
        private void ReloadTimeframes()
        {
            IList<string> available = FinamDataLoader.ListTimeframes(_dataDirectory, CurrentTicker());

            // Read the selection before the list is touched. The drop-down is two-way bound,
            // so emptying it can write null back into Timeframe - and reading it afterwards
            // would see that null and quietly replace the reader's choice with the default.
            string wanted = _timeframe ?? string.Empty;

            if (!SameTimeframes(available))
            {
                _availableTimeframes.Clear();
                for (int i = 0; i < available.Count; i++)
                {
                    _availableTimeframes.Add(new TimeframeOption(available[i], Timeframes.Display(available[i])));
                }
            }
            bool keep = wanted != null && wanted.Length > 0;
            for (int i = 0; !keep && i < _availableTimeframes.Count; i++)
            {
                keep = string.Equals(_availableTimeframes[i].Code, wanted, StringComparison.OrdinalIgnoreCase);
            }

            if (!keep)
            {
                _timeframe = DefaultTimeframe();
                OnPropertyChanged("Timeframe");
                OnPropertyChanged("TimeframeLabel");
            }

            RefreshSlotTimeframes();
        }

        /// <summary>
        /// Loads the price file for the current folder, ticker and timeframe, reusing the
        /// previous load when none of them changed. Returns false and reports the reason on
        /// failure.
        /// </summary>
        private bool EnsureDataLoaded()
        {
            string ticker = _ticker == null ? string.Empty : _ticker.Trim();
            if (ticker.Length == 0)
            {
                ErrorText = "Enter a ticker name.";
                return false;
            }

            string key = NumberParser.NormalizePath(_dataDirectory) + "|" + ticker.ToUpperInvariant() + "|" + (_timeframe ?? string.Empty);
            if (string.Equals(key, _cachedKey, StringComparison.Ordinal))
            {
                return true;
            }

            string file = string.IsNullOrEmpty(_timeframe)
                ? FinamDataLoader.FindFile(_dataDirectory, ticker, null)
                : FinamDataLoader.FindFileForTimeframe(_dataDirectory, ticker, _timeframe);

            if (file == null)
            {
                _cachedKey = string.Empty;
                _cachedCandles = null;
                ErrorText = string.IsNullOrEmpty(_timeframe)
                    ? "No file for ticker " + ticker + " in the chosen folder."
                    : "No " + Timeframes.Display(_timeframe) + " file for " + ticker + " in the chosen folder.";
                return false;
            }

            IList<Candle> candles = FinamDataLoader.ReadFile(file);
            if (candles.Count == 0)
            {
                _cachedKey = string.Empty;
                _cachedCandles = null;
                ErrorText = "File " + Path.GetFileName(file) + " contains no readable bars.";
                return false;
            }

            _cachedCandles = candles;
            _cachedFile = file;
            _cachedKey = key;
            BacktestEngine.GetDataSpan(candles, out _dataFrom, out _dataTo);

            // The pickers can only cover what the file holds.
            if (_fromDate.HasValue && _fromDate.Value < _dataFrom)
            {
                FromDate = _dataFrom;
            }

            if (_toDate.HasValue && _toDate.Value > _dataTo)
            {
                ToDate = _dataTo;
            }

            if (!_fromDate.HasValue)
            {
                _fromDate = _dataFrom;
                OnPropertyChanged("FromDate");
            }

            if (!_toDate.HasValue)
            {
                _toDate = _dataTo;
                OnPropertyChanged("ToDate");
            }

            OnPropertyChanged("TickerHint");
            return true;
        }

        private string CurrentFile()
        {
            string ticker = CurrentTicker();
            if (string.IsNullOrEmpty(_timeframe))
            {
                return FinamDataLoader.FindFile(_dataDirectory, ticker, null);
            }

            return FinamDataLoader.FindFileForTimeframe(_dataDirectory, ticker, _timeframe);
        }

        /// <summary>
        /// Asks the two slots for a configured indicator each, skipping an empty slot. The
        /// plug-ins have already been validated by the loader; anything they throw here is
        /// caught by the run and shown as an error.
        /// </summary>
        private IList<IndicatorRequest> BuildRequests()
        {
            var requests = new List<IndicatorRequest>(2);
            AddIfFilled(requests, _slot1);
            AddIfFilled(requests, _slot2);
            return requests;
        }

        private void AddIfFilled(List<IndicatorRequest> requests, IndicatorSlotViewModel slot)
        {
            if (slot == null || !slot.HasIndicator)
            {
                return;
            }

            IIndicator built = slot.Build();
            if (built == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(slot.Timeframe))
            {
                requests.Add(IndicatorRequest.On(built));
                return;
            }

            requests.Add(IndicatorRequest.OnTimeframe(built, LoadTimeframe(slot.Timeframe), slot.Timeframe));
        }

        /// <summary>
        /// The bars of another timeframe, read once and kept. A missing file is an error
        /// rather than a silent fallback: an indicator quietly calculated on the wrong bars
        /// is the one failure here that would not look like a failure.
        /// </summary>
        private IList<Candle> LoadTimeframe(string timeframe)
        {
            string ticker = CurrentTicker();
            string file = FinamDataLoader.FindFileForTimeframe(_dataDirectory, ticker, timeframe);
            if (file == null)
            {
                throw new InvalidOperationException(
                    "There is no " + Timeframes.Display(timeframe) + " file for " + ticker
                    + " in the chosen folder, so an indicator cannot be calculated on it.");
            }

            IList<Candle> cached;
            if (_timeframeCache.TryGetValue(file, out cached))
            {
                return cached;
            }

            IList<Candle> candles = FinamDataLoader.ReadFile(file);
            if (candles.Count == 0)
            {
                throw new InvalidOperationException(
                    "The " + Timeframes.Display(timeframe) + " file " + Path.GetFileName(file) + " contains no readable bars.");
            }

            _timeframeCache[file] = candles;
            return candles;
        }

        /// <summary>
        /// Rebuilds the two slots from the settings file. Anything missing or unrecognised
        /// falls back to a fast and a slow simple average, which is what the crossover rule
        /// expects in the first two slots.
        /// </summary>
        private void RestoreIndicators(string serialized)
        {
            IList<IIndicatorFactory> factories = _indicators.Plugins;

            if (!string.IsNullOrEmpty(serialized))
            {
                string[] parts = serialized.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length && i < 2; i++)
                {
                    IndicatorSlotViewModel restored = IndicatorSlotViewModel.Deserialize(parts[i].Trim(), factories);
                    if (i == 0)
                    {
                        _slot1 = restored;
                    }
                    else
                    {
                        _slot2 = restored;
                    }
                }
            }

            // A crossover needs two averages; anything less would open on a dead error.
            if (!_slot1.HasIndicator)
            {
                _slot1 = DefaultSlot("Sma", 20);
            }

            if (!_slot2.HasIndicator)
            {
                _slot2 = DefaultSlot("Sma", 60);
            }
        }

        private IndicatorSlotViewModel DefaultSlot(string id, int period)
        {
            IList<IIndicatorFactory> factories = _indicators.Plugins;
            for (int i = 0; i < factories.Count; i++)
            {
                if (!string.Equals(factories[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var slot = new IndicatorSlotViewModel(factories[i]);
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                IList<IndicatorParameterDefinition> definitions = factories[i].Parameters;
                if (definitions != null)
                {
                    for (int j = 0; j < definitions.Count; j++)
                    {
                        if (definitions[j].Type == IndicatorParameterType.Integer)
                        {
                            values[definitions[j].Key] = period.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                }

                slot.LoadValues(values);
                return slot;
            }

            return new IndicatorSlotViewModel();
        }

        /// <summary>
        /// Restores the rule from the settings file. The name and every parameter ride along
        /// in one token - "DonchianBreakout(Lookback=50)" - so a rule comes back exactly as
        /// it was left. An unknown or missing plug-in falls back to the first one found.
        /// </summary>
        private void RestoreStrategy(string stored)
        {
            StrategySlotViewModel restored = StrategySlotViewModel.Deserialize(stored, _rules.Plugins);

            if (!restored.HasRule && _rules.Plugins.Count > 0)
            {
                restored = new StrategySlotViewModel(_rules.Plugins[0]);
            }

            _strategySlot = restored;
            _strategySlot.PropertyChanged += OnStrategyChanged;
            _strategyHint = _strategySlot.Description;
        }

        /// <summary>
        /// First guess for the data folder: a "History" folder next to the executable,
        /// then the same folder a couple of levels up, which is where a freshly cloned copy
        /// of this project usually keeps its exports.
        /// </summary>
        private static string SuggestDirectory()
        {
            string start = AppDomain.CurrentDomain.BaseDirectory;
            var directory = new DirectoryInfo(start);

            for (int i = 0; i < 4 && directory != null; i++)
            {
                string candidate = Path.Combine(directory.FullName, "History");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return start;
        }

        private void SaveSettings(BacktestSettings settings, DateTime from, DateTime to)
        {
            _settings.Set(KeyDirectory, _dataDirectory);
            _settings.Set(KeyTicker, _ticker == null ? null : _ticker.Trim());
            _settings.Set(KeyCost, settings.CostPerOperationPercent);
            _settings.Set(KeyTimeframe, _timeframe ?? string.Empty);
            _settings.Set(KeyAllowShort, settings.AllowShort);
            _settings.Set(KeyShortFee, settings.ShortAnnualFeePercent);
            _settings.Set(KeyFrom, from);
            _settings.Set(KeyTo, to);
            _settings.Set(KeyIndicators, _slot1.Serialize() + "|" + _slot2.Serialize());
            _settings.Set(KeyStrategy, _strategySlot == null ? null : _strategySlot.Serialize());
            _settings.Save();
        }

        // ------------------------------------------------------------------ stats

        private void RebuildStats(BacktestResult result)
        {
            double totalCost = 0;
            for (int i = 0; i < result.Trades.Count; i++)
            {
                totalCost += result.Trades[i].TotalFees;
            }

            double excess = result.ReturnPercent - result.BuyAndHoldReturnPercent;

            _stats.Clear();
            Add("Ticker", result.Ticker, false);
            Add("Data file", result.Source, false);
            Add("Rule", _strategySlot == null ? string.Empty : _strategySlot.Serialize(), false);
            Add("Indicators", DescribeSlots(), false);
            Add("Bars", result.Candles.Count.ToString("N0", CultureInfo.CurrentCulture), false);
            Add("Period", result.From.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture) + " - " + result.To.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture), false);
            Add("Capital", NumberParser.FormatMoney(result.InitialCapital), false);
            Add("Final equity", NumberParser.FormatMoney(result.FinalEquity), true);
            Add("Net profit", NumberParser.FormatMoney(result.NetProfit), true);
            Add("Return", NumberParser.FormatSignedPercent(result.ReturnPercent), true);
            Add("Buy & hold", NumberParser.FormatSignedPercent(result.BuyAndHoldReturnPercent), false);
            Add("Excess", NumberParser.FormatSignedPercent(excess), false);
            Add("Round trips", result.ClosedTradeCount.ToString(CultureInfo.CurrentCulture), false);
            Add("Win rate", NumberParser.FormatPercent(result.WinRatePercent) + " %", false);
            Add("Max drawdown", NumberParser.FormatPercent(result.MaxDrawdownPercent) + " %", false);
            Add("Cost per operation", NumberParser.FormatPercent(result.Settings.CostPerOperationPercent) + " %", false);
            Add("Shorts", result.Settings.AllowShort
                ? result.ShortEntries.ToString(CultureInfo.CurrentCulture) + " entries, borrow "
                    + NumberParser.FormatPercent(result.Settings.ShortAnnualFeePercent) + " % a year"
                : "off", false);
            Add("Paid in fees", NumberParser.FormatMoney(totalCost), false);
            if (result.Settings.AllowShort && result.BorrowFeesPaid > 0)
            {
                Add("of which borrow", NumberParser.FormatMoney(result.BorrowFeesPaid), false);
            }
        }

        private string DescribeSlots()
        {
            string first = _slot1.Serialize();
            string second = _slot2.Serialize();
            return first == "none" ? second : (second == "none" ? first : first + ", " + second);
        }

        private void Add(string label, string value, bool highlighted)
        {
            _stats.Add(new StatItem(label, value, highlighted));
        }
    }
}