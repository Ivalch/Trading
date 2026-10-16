using System;
using System.Globalization;
using TradingBacktest.Mvvm;
using TradingBacktest.Strategies;

namespace TradingBacktest.ViewModels
{
    /// <summary>
    /// One strategy row: the rule plus, where the rule has one, its lookback.
    ///
    /// Unlike the indicators, the rules are compiled in. They are the trading logic rather
    /// than the maths, so they change far more often than the plug-in set - and a strategy
    /// may want to reference the engine's own types. Adding one is implementing
    /// <see cref="IStrategy"/> and putting an instance in <see cref="StrategyCatalog"/>.
    /// </summary>
    public sealed class StrategyViewModel : ObservableObject
    {
        private readonly IStrategy _prototype;
        private int _lookback;
        private string _description;

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

        /// <summary>
        /// The rule's own description, taken from a configured instance so that any wording
        /// which mentions a parameter - "the previous 20 bars" - is true for the value
        /// currently set. Cached, and dropped again whenever the lookback changes.
        /// </summary>
        public string Description
        {
            get
            {
                if (_description == null)
                {
                    _description = Build().Description;
                }

                return _description;
            }
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
                    _description = null;
                    OnPropertyChanged("LookbackText");
                    OnPropertyChanged("Description");
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

        /// <summary>
        /// Settings form. The lookback rides along with the name so it survives a restart -
        /// a rule whose text says "the previous 50 bars" should still say 50 tomorrow.
        /// </summary>
        public string Serialize()
        {
            if (!HasLookback)
            {
                return _prototype.Name;
            }

            return _prototype.Name + "@" + _lookback.ToString(CultureInfo.InvariantCulture);
        }
    }
}