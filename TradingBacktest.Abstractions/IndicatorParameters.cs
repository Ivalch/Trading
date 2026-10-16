using System;
using System.Collections.Generic;
using System.Globalization;

namespace TradingBacktest.Abstractions
{
    /// <summary>
    /// The values chosen for one indicator, keyed by the plug-in's own parameter keys.
    ///
    /// Read-only on purpose: an indicator is handed a snapshot, so it cannot reach back into
    /// the UI while the host is mid-run.
    /// </summary>
    public sealed class IndicatorParameters
    {
        private readonly Dictionary<string, object> _values;
        private readonly IList<IndicatorParameterDefinition> _definitions;

        /// <summary>Every value at its default.</summary>
        public IndicatorParameters(IList<IndicatorParameterDefinition> definitions)
            : this(definitions, null)
        {
        }

        /// <summary>
        /// Values read from <paramref name="stored"/>, a key=value dictionary as kept in the
        /// settings file. Anything missing or unreadable falls back to the default.
        /// </summary>
        public IndicatorParameters(IList<IndicatorParameterDefinition> definitions, IDictionary<string, string> stored)
        {
            _definitions = definitions ?? new List<IndicatorParameterDefinition>();
            _values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _definitions.Count; i++)
            {
                IndicatorParameterDefinition definition = _definitions[i];

                string text = null;
                if (stored != null)
                {
                    stored.TryGetValue(definition.Key, out text);
                }

                _values[definition.Key] = definition.Parse(text);
            }
        }

        /// <summary>The plug-in's declared parameters, in display order.</summary>
        public IList<IndicatorParameterDefinition> Definitions
        {
            get { return _definitions; }
        }

        public bool Has(string key)
        {
            return _values.ContainsKey(key);
        }

        public string GetString(string key)
        {
            object value;
            if (!_values.TryGetValue(key, out value) || value == null)
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public int GetInt(string key)
        {
            object value;
            if (_values.TryGetValue(key, out value) && value != null)
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }

            return 0;
        }

        public double GetDouble(string key)
        {
            object value;
            if (_values.TryGetValue(key, out value) && value != null)
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }

            return 0;
        }

        public bool GetBool(string key)
        {
            string text = GetString(key);
            return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1";
        }

        /// <summary>Formatted for the settings file: key=value pairs, culture independent.</summary>
        public IDictionary<string, string> ToStored()
        {
            var stored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _definitions.Count; i++)
            {
                IndicatorParameterDefinition definition = _definitions[i];
                stored[definition.Key] = definition.Format(_values[definition.Key]);
            }

            return stored;
        }
    }
}