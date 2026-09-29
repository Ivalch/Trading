using System;
using System.Globalization;
using TradingBacktest.Indicators;
using TradingBacktest.Mvvm;
using TradingBacktest.Strategies;

namespace TradingBacktest.ViewModels
{
    /// <summary>
    /// One row of the indicator list: the type, its window, and whether it takes part in
    /// the run. The row is only a shell - the calculation itself is done by the
    /// <see cref="IIndicator"/> it builds on demand, so changing the list needs no change
    /// in the engine.
    /// </summary>
    public sealed class IndicatorViewModel : ObservableObject
    {
        private readonly IIndicator _prototype;
        private int _period;

        public IndicatorViewModel(IIndicator prototype)
        {
            if (prototype == null)
            {
                throw new ArgumentNullException("prototype");
            }

            _prototype = prototype;

            // Use the window the prototype was built with, not its default, so a restored
            // or explicitly configured instance keeps its own number.
            _period = prototype.Period;
        }

        /// <summary>The configured instance, as shown in the drop-down ("SMA", "RSI", ...).</summary>
        public IIndicator Prototype
        {
            get { return _prototype; }
        }

        public string TypeName
        {
            get { return _prototype.Name; }
        }

        public int Period
        {
            get { return _period; }
            set
            {
                int clamped = value < 2 ? 2 : (value > 1000 ? 1000 : value);
                if (SetProperty(ref _period, clamped))
                {
                    OnPropertyChanged("PeriodText");
                }
            }
        }

        /// <summary>
        /// Text box backing for the window, so a half typed "2" is not rewritten to 20
        /// under the caret while the user is still typing.
        /// </summary>
        public string PeriodText
        {
            get { return _period.ToString(CultureInfo.CurrentCulture); }
            set
            {
                int parsed;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
                {
                    Period = parsed;
                }
            }
        }
        /// <summary>Text for the list row, e.g. "SMA 20".</summary>
        public string Label
        {
            get { return _prototype.Name + " " + _period.ToString(CultureInfo.CurrentCulture); }
        }

        /// <summary>The indicator to hand to the engine, built from the current settings.</summary>
        public IIndicator Build()
        {
            return _prototype.WithPeriod(_period);
        }

        /// <summary>Encodes the row for the settings file, e.g. "SMA:20".</summary>
        public string Serialize()
        {
            return _prototype.Name + ":" + _period.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Rebuilds a row from the settings file. Returns null for an unknown entry.</summary>
        public static IndicatorViewModel Deserialize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            int separator = text.LastIndexOf(':');
            string name = separator > 0 ? text.Substring(0, separator) : text;

            IIndicator prototype = IndicatorCatalog.Find(name);
            if (prototype == null)
            {
                return null;
            }

            var row = new IndicatorViewModel(prototype);
            if (separator > 0)
            {
                int period;
                if (int.TryParse(text.Substring(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out period))
                {
                    row.Period = period;
                }
            }

            return row;
        }
    }

    /// <summary>One strategy row: the rule plus, where the rule has one, its lookback.</summary>
    public sealed class StrategyViewModel : ObservableObject
    {
        private readonly IStrategy _prototype;
        private int _lookback;

        public StrategyViewModel(IStrategy prototype)
        {
            if (prototype == null)
            {
                throw new ArgumentNullException("prototype");
            }

            _prototype = prototype;
            _lookback = prototype.Lookback > 0 ? prototype.Lookback : 20;
        }

        public IStrategy Prototype
        {
            get { return _prototype; }
        }

        public string Name
        {
            get { return _prototype.Name; }
        }

        public string Description
        {
            get { return _prototype.Description; }
        }

        /// <summary>Lookback for rules that expose one; ignored by the others.</summary>
        public int Lookback
        {
            get { return _lookback; }
            set
            {
                int clamped = value < 2 ? 2 : (value > 1000 ? 1000 : value);
                if (SetProperty(ref _lookback, clamped))
                {
                    OnPropertyChanged("LookbackText");
                }
            }
        }

        public string LookbackText
        {
            get { return _lookback.ToString(CultureInfo.CurrentCulture); }
            set
            {
                int parsed;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
                {
                    Lookback = parsed;
                }
            }
        }

        /// <summary>True when the rule exposes a lookback worth showing.</summary>
        public bool HasLookback
        {
            get { return _prototype.Lookback > 0; }
        }

        /// <summary>The configured rule to hand to the engine.</summary>
        public IStrategy Build()
        {
            return _prototype.WithLookback(_lookback);
        }

        public string Serialize()
        {
            return _prototype.Name;
        }
    }
}
