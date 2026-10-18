using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using TradingBacktest.Abstractions;
using TradingBacktest.Mvvm;
using TradingBacktest.Services;

namespace TradingBacktest.ViewModels
{
    /// <summary>
    /// One editable parameter of one indicator slot. The editor is chosen from the
    /// plug-in's <see cref="IndicatorParameterType"/>, so the app never hard-codes what an
    /// indicator accepts.
    /// </summary>
    public sealed class IndicatorParameterViewModel : ObservableObject
    {
        private readonly IndicatorParameterDefinition _definition;
        private string _text;
        private string _choice;

        public IndicatorParameterViewModel(IndicatorParameterDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            _definition = definition;
            _text = definition.DefaultText;
            _choice = definition.Format(definition.DefaultValue);
        }

        public IndicatorParameterDefinition Definition
        {
            get { return _definition; }
        }

        public string Label
        {
            get { return _definition.Label; }
        }

        public string Description
        {
            get { return _definition.Description; }
        }

        public IndicatorParameterType Type
        {
            get { return _definition.Type; }
        }

        /// <summary>Key of the DataTemplate that fits this parameter's editor.</summary>
        public string TemplateKey
        {
            get { return "Parameter" + Type; }
        }

        /// <summary>Value for an integer, number or text editor.</summary>
        public string Text
        {
            get { return _text; }
            set
            {
                // Kept as typed: reformatting mid-edit would fight the caret. The definition
                // parses and clamps when the indicator is actually built.
                if (SetProperty(ref _text, value))
                {
                    OnPropertyChanged("Value");
                }
            }
        }

        /// <summary>Value for a choice editor.</summary>
        public string Choice
        {
            get { return _choice; }
            set
            {
                if (SetProperty(ref _choice, value))
                {
                    OnPropertyChanged("Value");
                }
            }
        }

        public IList<string> Options
        {
            get { return _definition.Options; }
        }

        /// <summary>What goes into the indicator: the raw text, or the selected option.</summary>
        public string Value
        {
            get { return _definition.Type == IndicatorParameterType.Choice ? _choice : _text; }
        }
    }

    /// <summary>
    /// One of the two indicator slots. It holds a factory that came from a plug-in DLL and
    /// the parameter values that factory asked for, and turns the two into an
    /// <see cref="IIndicator"/> at run time.
    ///
    /// The slot never names a concrete indicator - it only knows <see cref="IIndicatorFactory"/>,
    /// which is what lets any DLL drop in here without a change.
    /// </summary>
    public sealed class IndicatorSlotViewModel : ObservableObject
    {
        /// <summary>
        /// Settings key for the timeframe. Reserved: should a plug-in ever declare a
        /// parameter called "Tf", the plug-in wins and the slot falls back to the chart's
        /// own bars, rather than the two fighting over one key.
        /// </summary>
        public const string TimeframeKey = "Tf";

        private readonly ObservableCollection<IndicatorParameterViewModel> _parameters = new ObservableCollection<IndicatorParameterViewModel>();
        private readonly ObservableCollection<TimeframeOption> _timeframeOptions = new ObservableCollection<TimeframeOption>();
        private IIndicatorFactory _factory;
        private string _timeframe = string.Empty;

        public IndicatorSlotViewModel()
        {
        }

        public IndicatorSlotViewModel(IIndicatorFactory factory)
        {
            Factory = factory;
        }

        public IIndicatorFactory Factory
        {
            get { return _factory; }
            set
            {
                _parameters.Clear();

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

                OnPropertyChanged("HasIndicator");
                OnPropertyChanged("DisplayName");
            }
        }

        /// <summary>
        /// A parameter changed. The indicator slot has no description to keep honest, but the
        /// tooltip still has to show the values actually in force.
        /// </summary>
        private void OnParameterChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Value")
            {
                OnPropertyChanged("ParameterSummary");
            }
        }

        /// <summary>The editor values, shown as the slot's tooltip.</summary>
        public string ParameterSummary
        {
            get
            {
                IDictionary<string, string> values = CurrentValues();
                var parts = new List<string>(values.Count);
                foreach (KeyValuePair<string, string> pair in values)
                {
                    parts.Add(pair.Value);
                }

                return string.Join(", ", parts.ToArray());
            }
        }
        public bool HasIndicator
        {
            get { return _factory != null; }
        }

        public string DisplayName
        {
            get
            {
                if (_factory == null)
                {
                    return "none";
                }

                return _timeframe.Length == 0
                    ? _factory.Name
                    : _factory.Name + " (" + Timeframes.ShortLabel(_timeframe) + ")";
            }
        }

        /// <summary>
        /// The coarser timeframes this slot may be calculated on, offered as "same" plus one
        /// entry per timeframe the ticker actually has a file for. Set by the host, because
        /// only the host knows which files exist.
        /// </summary>
        public ObservableCollection<TimeframeOption> TimeframeOptions
        {
            get { return _timeframeOptions; }
        }

        /// <summary>
        /// Timeframe code this indicator is calculated on; empty means the chart's own bars.
        /// A rule reading this slot sees one value per chart bar either way.
        /// </summary>
        public string Timeframe
        {
            get { return _timeframe; }
            set
            {
                string code = value ?? string.Empty;

                // A slot can only be calculated on something coarser than the chart. Holding
                // that here rather than only while the options are refreshed keeps a hand
                // edited settings file or a stale value from slipping a finer one through.
                if (code.Length > 0 && !IsAllowed(code))
                {
                    code = string.Empty;
                }

                if (SetProperty(ref _timeframe, code))
                {
                    OnPropertyChanged("TimeframeSummary");
                    OnPropertyChanged("DisplayName");
                }
            }
        }

        /// <summary>
        /// Whether the host has offered this code. Before the first refresh there are no
        /// options to check against, so anything is taken and re-checked afterwards.
        /// </summary>
        private bool IsAllowed(string code)
        {
            if (_timeframeOptions.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < _timeframeOptions.Count; i++)
            {
                if (string.Equals(_timeframeOptions[i].Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Short description for the status line and tooltips.</summary>
        public string TimeframeSummary
        {
            get { return _timeframe.Length == 0 ? "same bars" : "on " + Timeframes.Display(_timeframe); }
        }

        /// <summary>
        /// Replaces the offered timeframes, keeping the current choice when it is still
        /// allowed and falling back to "same" when it is not - otherwise switching the chart
        /// from minutes to days would leave a slot claiming to run on an hour.
        /// </summary>
        public void SetTimeframeOptions(IList<TimeframeOption> options)
        {
            string wanted = _timeframe ?? string.Empty;

            _timeframeOptions.Clear();
            if (options != null)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    _timeframeOptions.Add(options[i]);
                }
            }

            bool allowed = wanted.Length == 0;
            for (int i = 0; !allowed && i < _timeframeOptions.Count; i++)
            {
                allowed = string.Equals(_timeframeOptions[i].Code, wanted, StringComparison.OrdinalIgnoreCase);
            }

            // Set through the field rather than the property. The combo is two-way bound to
            // Timeframe, so emptying the list mid-swap makes it write null back here; putting
            // the value back afterwards and announcing it is what keeps the user's choice.
            _timeframe = allowed ? wanted : string.Empty;
            OnPropertyChanged("Timeframe");
            OnPropertyChanged("TimeframeSummary");
            OnPropertyChanged("DisplayName");
        }

        /// <summary>The editors the UI shows, in the plug-in's order.</summary>
        public ObservableCollection<IndicatorParameterViewModel> Parameters
        {
            get { return _parameters; }
        }

        /// <summary>Builds the configured indicator, or null when the slot is empty.</summary>
        public IIndicator Build()
        {
            if (_factory == null)
            {
                return null;
            }

            return _factory.Create(ToParameters());
        }

        /// <summary>Current values as the contract's read-only parameter bag.</summary>
        public IndicatorParameters ToParameters()
        {
            return new IndicatorParameters(_factory == null ? null : _factory.Parameters, CurrentValues());
        }

        /// <summary>Loads values saved earlier, by key.</summary>
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

        /// <summary>Settings form: "Sma(Period=20)".</summary>
        public string Serialize()
        {
            if (_factory == null)
            {
                return "none";
            }

            IDictionary<string, string> values = CurrentValues();

            // Only written when it is not the default, so a settings file saved before
            // timeframes existed still reads back as "same bars".
            if (_timeframe.Length > 0)
            {
                values[TimeframeKey] = _timeframe;
            }

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

        /// <summary>Rebuilds a slot from the settings form. Returns an empty slot on a miss.</summary>
        public static IndicatorSlotViewModel Deserialize(string text, IList<IIndicatorFactory> factories)
        {
            var slot = new IndicatorSlotViewModel();

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

                // Only take Tf when the plug-in did not ask for a parameter by that name.
                bool reserved = true;
                for (int j = 0; j < slot._parameters.Count && reserved; j++)
                {
                    reserved = !string.Equals(slot._parameters[j].Definition.Key, TimeframeKey, StringComparison.OrdinalIgnoreCase);
                }

                if (reserved)
                {
                    string timeframe = null;
                    if (stored.TryGetValue(TimeframeKey, out timeframe) && Timeframes.IsKnown(timeframe))
                    {
                        slot.Timeframe = timeframe;
                    }
                }

                return slot;
            }

            return slot;
        }
    }
}