using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using TradingBacktest.Abstractions;
using TradingBacktest.Mvvm;

namespace TradingBacktest.ViewModels
{
    /// <summary>
    /// The buy/sell rule in force, plus the parameters its plug-in declared.
    ///
    /// Same shape as <see cref="IndicatorSlotViewModel"/>, and for the same reason: the slot
    /// only knows <see cref="IStrategyFactory"/>, so any Str*.dll can be dropped in without
    /// a change here.
    /// </summary>
    public sealed class StrategySlotViewModel : ObservableObject
    {
        private readonly ObservableCollection<IndicatorParameterViewModel> _parameters = new ObservableCollection<IndicatorParameterViewModel>();
        private IStrategyFactory _factory;
        private IStrategy _configured;

        public StrategySlotViewModel()
        {
        }

        public StrategySlotViewModel(IStrategyFactory factory)
        {
            Factory = factory;
        }

        public IStrategyFactory Factory
        {
            get { return _factory; }
            set
            {
                _parameters.Clear();
                _configured = null;

                if (SetProperty(ref _factory, value) && value != null)
                {
                    IList<IndicatorParameterDefinition> definitions = value.Parameters;
                    if (definitions != null)
                    {
                        for (int i = 0; i < definitions.Count; i++)
                        {
                            var parameter = new IndicatorParameterViewModel(definitions[i]);
                            parameter.PropertyChanged += OnParameterChanged;
                            _parameters.Add(parameter);
                        }
                    }
                }

                OnPropertyChanged("HasRule");
                OnPropertyChanged("DisplayName");
                Refresh();
            }
        }

        public bool HasRule
        {
            get { return _factory != null; }
        }

        public string DisplayName
        {
            get { return _factory == null ? "none" : _factory.Name; }
        }

        /// <summary>The editors the UI shows, in the plug-in's order.</summary>
        public ObservableCollection<IndicatorParameterViewModel> Parameters
        {
            get { return _parameters; }
        }

        /// <summary>
        /// What the rule will actually do, in words. Taken from a configured instance so a
        /// phrase like "the previous 50 bars" is true for the value currently set rather than
        /// for whatever the plug-in's defaults were.
        /// </summary>
        public string Description
        {
            get { return _configured == null ? string.Empty : _configured.Description; }
        }

        /// <summary>Builds the configured rule, or null when no plug-in is chosen.</summary>
        public IStrategy Build()
        {
            return _factory == null ? null : _factory.Create(ToParameters());
        }

        public IndicatorParameters ToParameters()
        {
            return new IndicatorParameters(_factory == null ? null : _factory.Parameters, CurrentValues());
        }

        /// <summary>Loads values saved earlier, by parameter key.</summary>
        public void LoadValues(IDictionary<string, string> stored)
        {
            for (int i = 0; i < _parameters.Count; i++)
            {
                IndicatorParameterViewModel parameter = _parameters[i];

                string text = null;
                if (stored != null)
                {
                    stored.TryGetValue(parameter.Definition.Key, out text);
                }

                string value = parameter.Definition.Format(parameter.Definition.Parse(text));
                if (parameter.Definition.Type == IndicatorParameterType.Choice)
                {
                    parameter.Choice = value;
                }
                else
                {
                    parameter.Text = value;
                }
            }

            Refresh();
        }

        /// <summary>Values as the settings file keeps them.</summary>
        public IDictionary<string, string> CurrentValues()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _parameters.Count; i++)
            {
                values[_parameters[i].Definition.Key] = _parameters[i].Value;
            }

            return values;
        }

        /// <summary>Settings form: "DonchianBreakout(Lookback=50)".</summary>
        public string Serialize()
        {
            if (_factory == null)
            {
                return "none";
            }

            IDictionary<string, string> values = CurrentValues();

            var keys = new List<string>();
            foreach (KeyValuePair<string, string> pair in values)
            {
                keys.Add(pair.Key);
            }

            keys.Sort(StringComparer.Ordinal);

            var parts = new List<string>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                parts.Add(keys[i] + "=" + values[keys[i]]);
            }

            return _factory.Id + "(" + string.Join(",", parts.ToArray()) + ")";
        }

        /// <summary>Rebuilds a slot from the settings form. Empty when the plug-in is gone.</summary>
        public static StrategySlotViewModel Deserialize(string text, IList<IStrategyFactory> factories)
        {
            var slot = new StrategySlotViewModel();

            if (string.IsNullOrEmpty(text) || factories == null)
            {
                return slot;
            }

            string body = text.Trim();
            int open = body.IndexOf('(');
            string id = open > 0 ? body.Substring(0, open) : body;
            string inner = open > 0 && body.EndsWith(")", StringComparison.Ordinal)
                ? body.Substring(open + 1, body.Length - open - 2)
                : string.Empty;

            for (int i = 0; i < factories.Count; i++)
            {
                if (!string.Equals(factories[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                slot.Factory = factories[i];

                var stored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (inner.Length > 0)
                {
                    string[] pairs = inner.Split(',');
                    for (int j = 0; j < pairs.Length; j++)
                    {
                        int equals = pairs[j].IndexOf('=');
                        if (equals > 0)
                        {
                            stored[pairs[j].Substring(0, equals).Trim()] = pairs[j].Substring(equals + 1).Trim();
                        }
                    }
                }

                slot.LoadValues(stored);
                return slot;
            }

            return slot;
        }

        /// <summary>
        /// A parameter changed, so the configured instance - and anything it says about
        /// itself - has to be rebuilt.
        /// </summary>
        private void OnParameterChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Value")
            {
                Refresh();
            }
        }
        /// <summary>
        /// Rebuilds the configured rule so its description reflects the current parameters.
        /// Called whenever a parameter editor changes.
        /// </summary>
        public void Refresh()
        {
            _configured = Build();
            OnPropertyChanged("Description");
            OnPropertyChanged("DisplayName");
        }
    }
}