namespace LogiFlow.Web.Components.Shared;

/// <summary>
/// Whether the plain-language explanations are switched on.
/// </summary>
/// <remarks>
/// <para>
/// Cascaded from <c>MainLayout</c> rather than injected as a scoped service, and that is a
/// deliberate choice. A cascading value re-renders <b>only the components that declared a
/// <see cref="Microsoft.AspNetCore.Components.CascadingParameterAttribute"/> for it</b> - which
/// here is <c>Explain</c> and nothing else. A service with an event would work, but every
/// subscriber must then remember to unsubscribe in <c>Dispose</c>, and a page that forgets leaks
/// for the lifetime of the circuit.
/// </para>
/// <para>
/// It matters that this is a <b>type</b>, not a named <c>bool</c>. Named cascading values
/// (<c>[CascadingParameter(Name = "ExplainMode")]</c>) match on a magic string, so a typo compiles
/// happily and silently never updates. A type cannot be misspelled.
/// </para>
/// <para>
/// Deliberately <b>not</b> persisted to browser storage: that needs JS interop, which cannot run
/// during prerendering, and the resulting "flash of the wrong state" costs more than it buys. The
/// setting lives as long as the circuit does, which is as long as the tab is open.
/// </para>
/// </remarks>
/// <param name="Enabled">True when the explanations should render.</param>
public sealed record ExplainMode(bool Enabled)
{
    /// <summary>Explanations hidden - the default, and what a colleague sees.</summary>
    public static ExplainMode Off { get; } = new(false);

    /// <summary>Explanations shown.</summary>
    public static ExplainMode On { get; } = new(true);

    /// <summary>Returns the opposite state.</summary>
    /// <returns><see cref="On"/> when currently off, otherwise <see cref="Off"/>.</returns>
    public ExplainMode Toggled() => Enabled ? Off : On;
}
