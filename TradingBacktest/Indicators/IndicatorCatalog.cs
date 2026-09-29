using System;
using System.Collections.Generic;

namespace TradingBacktest.Indicators
{
    /// <summary>
    /// One entry of the indicator drop-down. A choice may stand for "no indicator at all",
    /// which is why the prototype is nullable: the two indicator slots are always there and
    /// the user fills in what each one should be.
    /// </summary>
    public sealed class IndicatorChoice
    {
        public static readonly string NoneName = "none";

        public IndicatorChoice(string name, IIndicator prototype)
        {
            Name = name;
            Prototype = prototype;
        }

        /// <summary>Text shown in the drop-down.</summary>
        public string Name { get; private set; }

        /// <summary>The calculation to run, or null for the "none" entry.</summary>
        public IIndicator Prototype { get; private set; }

        public bool IsEmpty
        {
            get { return Prototype == null; }
        }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// Prototypes of every indicator the app can draw. The list powers the two indicator
    /// drop-downs; each entry is a default configuration that gets cloned with
    /// <see cref="IIndicator.WithPeriod"/> once the user changes the window.
    /// </summary>
    public static class IndicatorCatalog
    {
        private static readonly List<IIndicator> Prototypes = new List<IIndicator>
        {
            new SmaIndicator(),
            new EmaIndicator(),
            new RsiIndicator(),
            new BollingerBandsIndicator()
        };

        private static readonly List<IndicatorChoice> AllChoices = BuildChoices();

        /// <summary>One entry per indicator type, without the empty one.</summary>
        public static IList<IIndicator> All
        {
            get { return Prototypes; }
        }

        /// <summary>The empty entry first, then one per indicator type.</summary>
        public static IList<IndicatorChoice> Choices
        {
            get { return AllChoices; }
        }

        /// <summary>The entry that leaves a slot empty.</summary>
        public static IndicatorChoice None
        {
            get { return AllChoices[0]; }
        }

        /// <summary>Finds a prototype by <see cref="IIndicator.Name"/>, case-insensitively.</summary>
        public static IIndicator Find(string name)
        {
            for (int i = 0; i < Prototypes.Count; i++)
            {
                if (string.Equals(Prototypes[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return Prototypes[i];
                }
            }

            return null;
        }

        /// <summary>Finds a drop-down entry by name, or the empty one when unknown.</summary>
        public static IndicatorChoice FindChoice(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < AllChoices.Count; i++)
                {
                    if (string.Equals(AllChoices[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return AllChoices[i];
                    }
                }
            }

            return None;
        }

        private static List<IndicatorChoice> BuildChoices()
        {
            var choices = new List<IndicatorChoice>();
            choices.Add(new IndicatorChoice(IndicatorChoice.NoneName, null));

            for (int i = 0; i < Prototypes.Count; i++)
            {
                choices.Add(new IndicatorChoice(Prototypes[i].Name, Prototypes[i]));
            }

            return choices;
        }
    }
}
