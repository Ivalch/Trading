using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using TradingBacktest.Indicators;
using TradingBacktest.Models;
using TradingBacktest.Mvvm;
using TradingBacktest.Services;
using TradingBacktest.Strategies;

namespace TradingBacktest.ViewModels
{
    /// <summary>
    /// Settings and the run loop.
    ///
    /// Parameters: ticker, data folder, period, cost of operation, the list of indicators
    /// and the buy/sell rule. Pressing Start takes whatever the list and the drop-down
    /// currently say, hands them to the engine and publishes the result for the chart.
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

        private readonly BacktestEngine _engine = new BacktestEngine();
        private readonly SettingsStore _settings = new SettingsStore();

        private string _ticker;
        private string _costText;
        private string _dataDirectory;
        private DateTime? _fromDate;
        private DateTime? _toDate;
        private bool _isBusy;
        private string _statusText;
        private string _errorText;
        private string _strategyHint;
        private BacktestResult _result;
        private IIndicator _indicatorToAdd;
        private StrategyViewModel _selectedStrategy;
        private ObservableCollection<StatItem> _stats = new ObservableCollection<StatItem>();
        private ObservableCollection<string> _availableTickers = new ObservableCollection<string>();

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
            _dataDirectory = _settings.Get(KeyDirectory) ?? SuggestDirectory();
            _fromDate = _settings.GetDate(KeyFrom);
            _toDate = _settings.GetDate(KeyTo);

            Strategies = new ObservableCollection<StrategyViewModel>();
            for (int i = 0; i < StrategyCatalog.All.Count; i++)
            {
                Strategies.Add(new StrategyViewModel(StrategyCatalog.All[i]));
            }

            _selectedStrategy = FindStrategy(_settings.Get(KeyStrategy)) ?? Strategies[0];
            _strategyHint = _selectedStrategy.Description;

            AvailableIndicators = IndicatorCatalog.All;
            _indicatorToAdd = AvailableIndicators[0];

            RestoreIndicators(_settings.Get(KeyIndicators));

            StartCommand = new RelayCommand(OnStart, () => !_isBusy);
            BrowseFolderCommand = new RelayCommand(OnBrowseFolder);
            RefreshTickersCommand = new RelayCommand(ReloadTickers);
            PeriodCommand = new RelayCommand(OnPeriodPreset);
            AddIndicatorCommand = new RelayCommand(OnAddIndicator);
            RemoveIndicatorCommand = new RelayCommand(OnRemoveIndicator);

            ReloadTickers();

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

        /// <summary>Indicator types that can be added, one row per type.</summary>
        public IList<IIndicator> AvailableIndicators { get; private set; }

        /// <summary>The indicator the "Add" button will put on the list.</summary>
        public IIndicator IndicatorToAdd
        {
            get { return _indicatorToAdd; }
            set { SetProperty(ref _indicatorToAdd, value); }
        }

        /// <summary>
        /// The indicator list, in the order the engine will hand it to the rule. A rule
        /// refers to its series by position here, so reordering changes the meaning of a
        /// crossover - the list is a parameter, not a cosmetic detail.
        /// </summary>
        public ObservableCollection<IndicatorViewModel> Indicators { get; private set; }

        public ObservableCollection<StrategyViewModel> Strategies { get; private set; }

        public StrategyViewModel SelectedStrategy
        {
            get { return _selectedStrategy; }
            set
            {
                if (SetProperty(ref _selectedStrategy, value) && value != null)
                {
                    _strategyHint = value.Description;
                    OnPropertyChanged("StrategyHint");
                }
            }
        }

        public string StrategyHint
        {
            get { return _strategyHint; }
        }

        // ------------------------------------------------------------------ outputs

        public ICommand StartCommand { get; private set; }

        public ICommand BrowseFolderCommand { get; private set; }

        public ICommand RefreshTickersCommand { get; private set; }

        /// <summary>Takes a parameter of "1Y", "3Y", "5Y" or "All".</summary>
        public ICommand PeriodCommand { get; private set; }

        public ICommand AddIndicatorCommand { get; private set; }

        /// <summary>Takes the row to remove as parameter.</summary>
        public ICommand RemoveIndicatorCommand { get; private set; }

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

        private void OnAddIndicator()
        {
            if (_indicatorToAdd == null)
            {
                return;
            }

            Indicators.Add(new IndicatorViewModel(_indicatorToAdd));
            OnPropertyChanged("IndicatorListHint");
        }

        private void OnRemoveIndicator(object parameter)
        {
            var row = parameter as IndicatorViewModel;
            if (row != null)
            {
                Indicators.Remove(row);
                OnPropertyChanged("IndicatorListHint");
            }
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

                if (!EnsureDataLoaded())
                {
                    return;
                }

                DateTime from = _fromDate ?? _dataFrom;
                DateTime to = _toDate ?? _dataTo;
                string ticker = _ticker.Trim().ToUpperInvariant();
                string source = Path.GetFileName(_cachedFile);

                IList<IIndicator> indicators = BuildIndicators();
                IStrategy strategy = _selectedStrategy == null ? null : _selectedStrategy.Build();

                BacktestResult result = await Task.Run(delegate
                {
                    return _engine.Run(ticker, source, _cachedCandles, cost, from, to, indicators, strategy);
                });

                Result = result;
                RebuildStats(result);
                StatusText = string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} - {1} bars, {2} - {3}, {4} trades, cost {5}% per operation",
                    ticker,
                    result.Candles.Count,
                    result.From.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture),
                    result.To.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture),
                    result.ClosedTradeCount,
                    NumberParser.FormatPercent(cost));

                SaveSettings(cost, from, to);
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
        /// Loads the price file for the current folder and ticker, reusing the previous
        /// load when neither changed. Returns false and reports the reason on failure.
        /// </summary>
        private bool EnsureDataLoaded()
        {
            string ticker = _ticker == null ? string.Empty : _ticker.Trim();
            if (ticker.Length == 0)
            {
                ErrorText = "Enter a ticker name.";
                return false;
            }

            string key = NumberParser.NormalizePath(_dataDirectory) + "|" + ticker.ToUpperInvariant();
            if (string.Equals(key, _cachedKey, StringComparison.Ordinal))
            {
                return true;
            }

            string file = FinamDataLoader.FindFile(_dataDirectory, ticker, null);
            if (file == null)
            {
                _cachedKey = string.Empty;
                _cachedCandles = null;
                ErrorText = "No file for ticker " + ticker + " in the chosen folder.";
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
            return FinamDataLoader.FindFile(_dataDirectory, _ticker == null ? string.Empty : _ticker.Trim(), null);
        }


        private IList<IIndicator> BuildIndicators()
        {
            var indicators = new List<IIndicator>();
            for (int i = 0; i < Indicators.Count; i++)
            {
                indicators.Add(Indicators[i].Build());
            }

            return indicators;
        }

        private void RestoreIndicators(string serialized)
        {
            Indicators = new ObservableCollection<IndicatorViewModel>();

            if (!string.IsNullOrEmpty(serialized))
            {
                string[] parts = serialized.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    IndicatorViewModel row = IndicatorViewModel.Deserialize(parts[i].Trim());
                    if (row != null)
                    {
                        Indicators.Add(row);
                    }
                }
            }

            if (Indicators.Count == 0)
            {
                // Default setup: a fast and a slow average, which is what the crossover
                // rule expects in the first two positions.
                Indicators.Add(new IndicatorViewModel(new SmaIndicator(20)));
                Indicators.Add(new IndicatorViewModel(new SmaIndicator(60)));
            }
        }

        private StrategyViewModel FindStrategy(string name)
        {
            for (int i = 0; i < Strategies.Count; i++)
            {
                if (string.Equals(Strategies[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return Strategies[i];
                }
            }

            return null;
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

        private void SaveSettings(double cost, DateTime from, DateTime to)
        {
            var text = new StringBuilder();
            for (int i = 0; i < Indicators.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(';');
                }

                text.Append(Indicators[i].Serialize());
            }

            _settings.Set(KeyDirectory, _dataDirectory);
            _settings.Set(KeyTicker, _ticker == null ? null : _ticker.Trim());
            _settings.Set(KeyCost, cost);
            _settings.Set(KeyFrom, from);
            _settings.Set(KeyTo, to);
            _settings.Set(KeyIndicators, text.ToString());
            _settings.Set(KeyStrategy, _selectedStrategy == null ? null : _selectedStrategy.Serialize());
            _settings.Save();
        }

        // ------------------------------------------------------------------ stats

        private void RebuildStats(BacktestResult result)
        {
            double totalCost = 0;
            for (int i = 0; i < result.Trades.Count; i++)
            {
                totalCost += result.Trades[i].Cost;
            }

            double excess = result.ReturnPercent - result.BuyAndHoldReturnPercent;

            _stats.Clear();
            Add("Ticker", result.Ticker, false);
            Add("Data file", result.Source, false);
            Add("Rule", result.Strategy, false);
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
            Add("Cost per operation", NumberParser.FormatPercent(result.CostPerOperationPercent) + " %", false);
            Add("Paid in fees", NumberParser.FormatMoney(totalCost), false);
        }

        private void Add(string label, string value, bool highlighted)
        {
            _stats.Add(new StatItem(label, value, highlighted));
        }
    }
}
