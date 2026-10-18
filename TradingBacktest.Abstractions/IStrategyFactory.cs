using System.Collections.Generic;

namespace TradingBacktest.Abstractions
{
    /// <summary>
    /// What a strategy plug-in DLL offers: one factory per rule it contains.
    ///
    /// Same shape as <see cref="IIndicatorFactory"/>, and for the same reason: the host
    /// constructs this type at startup and never sees the rule itself. Parameters are
    /// declared here and edited through the same machinery as an indicator's, so a rule can
    /// be as configurable as an indicator.
    ///
    /// One rule about the wording: anything that quotes a parameter value belongs in the
    /// configured <see cref="IStrategy.Description"/>, not here, because this instance is
    /// created once at startup and cannot know what the user will type.
    /// </summary>
    public interface IStrategyFactory
    {
        /// <summary>
        /// Stable identifier stored in the settings file, e.g. "DonchianBreakout".
        /// Changing it orphans the saved parameters for that rule.
        /// </summary>
        string Id { get; }

        /// <summary>Name shown in the drop-down, e.g. "Donchian breakout".</summary>
        string Name { get; }

        /// <summary>What the rule does, in general terms. No parameter values.</summary>
        string Description { get; }

        /// <summary>Parameters the host should let the user edit, in display order.</summary>
        IList<IndicatorParameterDefinition> Parameters { get; }

        /// <summary>Builds a configured rule. Called afresh for every run.</summary>
        IStrategy Create(IndicatorParameters parameters);
    }
}