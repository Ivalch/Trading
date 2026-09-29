using System;
using System.Globalization;
using TradingBacktest.Indicators;
using TradingBacktest.Mvvm;
using TradingBacktest.Strategies;

namespace TradingBacktest.ViewModels
{
    /// <summary>
    /// One of the two indicator slots.
    ///
    /// The slot is only a shell - the calculation itself is done by the
    /// <see cref="IIndicator"/> it builds on demand, so changing a slot needs no change
    /// in the engine. There are exactly two of them: the crossover rule trades on one
    /// indicator against the other, and a second pair would only be confusing.
    /// </summary>
    public sealed class IndicatorViewModel : ObservableObject
    {
        private IndicatorChoice _choice;
        private int _period;

        public IndicatorViewModel(IndicatorChoice choice)
        {
            _choice = choice ?? IndicatorCatalog.None;
            _period = _choice.IsEmpty ? 20 : _choice.Prototype.DefaultPeriod;
        }

        /// <summary>What the slot currently holds; setting it resets the window to the default.</summary>
        public IndicatorChoice Choice
        {
            get { return _choice; }
            set
            {
                IndicatorChoice incoming = value ?? IndicatorCatalog.None;
                if (!ReferenceEquals(incoming, _choice) && SetProperty(ref _choice, incoming))
                {
                    Period = incoming.IsEmpty ? _period : incoming.Prototype.DefaultPeriod;
                }
            }
        }

        public string ChoiceName
        {
            get { return _choice.Name; }
        }

        public bool HasIndicator
        {
            get { return !_choice.IsEmpty; }
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

        /// <summary>The indicator to hand to the engine, or null when the slot is empty.</summary>
        public IIndicator Build()
        {
            return _choice.IsEmpty ? null : _choice.Prototype.WithPeriod(_period);
        }

        /// <summary>Encodes the slot for the settings file, e.g. "SMA:20" or "none".</summary>
        public string Serialize()
        {
            if (_choice.IsEmpty)
            {
                return IndicatorChoice.NoneName;
            }

            return _choice.Prototype.Name + ":" + _period.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Rebuilds a slot from the settings file. Unknown entries come back empty.</summary>
        public static IndicatorViewModel Deserialize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new IndicatorViewModel(IndicatorCatalog.None);
            }

            int separator = text.LastIndexOf(':');
            string name = separator > 0 ? text.Substring(0, separator) : text;

            IndicatorChoice choice = IndicatorCatalog.FindChoice(name);
            var row = new IndicatorViewModel(choice);

            if (separator > 0 && !choice.IsEmpty)
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
