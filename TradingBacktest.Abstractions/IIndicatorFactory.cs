using System.Collections.Generic;

namespace TradingBacktest.Abstractions
{
    /// <summary>
    /// What a plug-in DLL offers: one factory per indicator it contains.
    ///
    /// The host constructs exactly this type at startup, once per DLL, and never sees the
    /// indicator implementation itself - that is the whole point. The plug-in depends on
    /// the contracts; the host depends on the contracts; neither depends on the other.
    /// </summary>
    public interface IIndicatorFactory
    {
        /// <summary>
        /// Stable identifier stored in the settings file, e.g. "Sma". Changing it orphans
        /// saved parameters for that indicator, so keep it stable across versions.
        /// </summary>
        string Id { get; }

        /// <summary>Name shown in the drop-down, e.g. "SMA".</summary>
        string Name { get; }

        /// <summary>One line describing the calculation.</summary>
        string Description { get; }

        /// <summary>Parameters the host should let the user edit, in display order.</summary>
        IList<IndicatorParameterDefinition> Parameters { get; }

        /// <summary>Builds a configured indicator. Called afresh for every run.</summary>
        IIndicator Create(IndicatorParameters parameters);
    }
}