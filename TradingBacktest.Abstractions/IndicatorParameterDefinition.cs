using System;
using System.Collections.Generic;
using System.Globalization;

namespace TradingBacktest.Abstractions
{
    /// <summary>How the host should let the user edit a parameter.</summary>
    public enum IndicatorParameterType
    {
        /// <summary>Whole number in a box.</summary>
        Integer,

        /// <summary>Decimal number in a box.</summary>
        Number,

        /// <summary>Free text.</summary>
        Text,

        /// <summary>Pick one of a fixed list.</summary>
        Choice
    }

    /// <summary>
    /// Declares one editable parameter of an indicator. The host reads this and builds the
    /// editor, so a plug-in never brings its own UI and the app never hard-codes a list of
    /// indicator settings.
    /// </summary>
    public sealed class IndicatorParameterDefinition
    {
        private readonly IList<string> _options;

        private IndicatorParameterDefinition(
            string key,
            string label,
            IndicatorParameterType type,
            object defaultValue,
            string description,
            double minimum,
            double maximum,
            IList<string> options)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("A parameter needs a key.", "key");
            }

            Key = key;
            Label = label;
            Type = type;
            DefaultValue = defaultValue;
            Description = description ?? string.Empty;
            Minimum = minimum;
            Maximum = maximum;
            _options = options ?? new List<string>();
        }

        /// <summary>Stable name used in the settings file, e.g. "Period".</summary>
        public string Key { get; private set; }

        /// <summary>Text shown next to the editor.</summary>
        public string Label { get; private set; }

        public IndicatorParameterType Type { get; private set; }

        public object DefaultValue { get; private set; }

        public string Description { get; private set; }

        /// <summary>Lower bound for numbers.</summary>
        public double Minimum { get; private set; }

        /// <summary>Upper bound for numbers.</summary>
        public double Maximum { get; private set; }

        /// <summary>The permitted values, for <see cref="IndicatorParameterType.Choice"/>.</summary>
        public IList<string> Options
        {
            get { return _options; }
        }

        /// <summary>Default rendered the way the host shows it, using the current culture.</summary>
        public string DefaultText
        {
            get { return Format(DefaultValue); }
        }

        public static IndicatorParameterDefinition Integer(string key, string label, int defaultValue, int minimum, int maximum, string description = null)
        {
            return new IndicatorParameterDefinition(key, label, IndicatorParameterType.Integer, defaultValue, description, minimum, maximum, null);
        }

        public static IndicatorParameterDefinition Number(string key, string label, double defaultValue, double minimum, double maximum, string description = null)
        {
            return new IndicatorParameterDefinition(key, label, IndicatorParameterType.Number, defaultValue, description, minimum, maximum, null);
        }

        public static IndicatorParameterDefinition Text(string key, string label, string defaultValue, string description = null)
        {
            return new IndicatorParameterDefinition(key, label, IndicatorParameterType.Text, defaultValue ?? string.Empty, description, 0, 0, null);
        }

        public static IndicatorParameterDefinition Choice(string key, string label, string defaultValue, IList<string> options, string description = null)
        {
            if (options == null || options.Count == 0)
            {
                throw new ArgumentException("A choice parameter needs options.", "options");
            }

            return new IndicatorParameterDefinition(key, label, IndicatorParameterType.Choice, defaultValue, description, 0, 0, options);
        }

        /// <summary>
        /// Turns a stored string into a value of this parameter's type, clamped to its range
        /// and falling back to the default when it cannot be read. A plug-in therefore never
        /// has to defend itself against a hand edited settings file.
        /// </summary>
        public object Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return DefaultValue;
            }

            switch (Type)
            {
                case IndicatorParameterType.Integer:
                    return ParseInt(text, Minimum, Maximum);

                case IndicatorParameterType.Number:
                    return ParseDouble(text, Minimum, Maximum);

                case IndicatorParameterType.Text:
                    return text;

                case IndicatorParameterType.Choice:
                    for (int i = 0; i < _options.Count; i++)
                    {
                        if (string.Equals(_options[i], text, StringComparison.OrdinalIgnoreCase))
                        {
                            return _options[i];
                        }
                    }

                    return DefaultValue;

                default:
                    return DefaultValue;
            }
        }

        /// <summary>Renders a value the way the editor should display it.</summary>
        public string Format(object value)
        {
            if (value == null)
            {
                return DefaultText;
            }

            if (Type == IndicatorParameterType.Number)
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("0.####", CultureInfo.CurrentCulture);
            }

            if (Type == IndicatorParameterType.Integer)
            {
                return Convert.ToInt32(value, CultureInfo.CurrentCulture).ToString(CultureInfo.CurrentCulture);
            }

            return Convert.ToString(value, CultureInfo.CurrentCulture);
        }

        private int ParseInt(string text, double minimum, double maximum)
        {
            int value;
            if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
                || int.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return Clamp(value, minimum, maximum);
            }

            return Convert.ToInt32(DefaultValue, CultureInfo.CurrentCulture);
        }

        private double ParseDouble(string text, double minimum, double maximum)
        {
            string trimmed = text.Trim().Replace(',', '.');
            double value;
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return Clamp(value, minimum, maximum);
            }

            return Convert.ToDouble(DefaultValue, CultureInfo.InvariantCulture);
        }

        private static int Clamp(int value, double minimum, double maximum)
        {
            if (value < minimum)
            {
                value = (int)Math.Ceiling(minimum);
            }

            if (value > maximum)
            {
                value = (int)Math.Floor(maximum);
            }

            return value;
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }

            return value > maximum ? maximum : value;
        }
    }
}