using NetArchTest.Rules;

namespace LogiFlow.ArchitectureTests;

/// <summary>
/// Turns a NetArchTest result into an actionable failure message.
/// </summary>
/// <remarks>
/// The default assertion failure is just <c>Expected: True, Actual: False</c>, which tells you
/// a rule broke but not which type broke it. Naming the offenders turns a ten-minute
/// investigation into a five-second fix — and a test that is annoying to diagnose is a test
/// people eventually delete.
/// </remarks>
internal static class ArchTestMessages
{
    /// <summary>Builds a message naming every type that violated the rule.</summary>
    public static string FailureMessage(TestResult result, string rule)
    {
        if (result.IsSuccessful)
        {
            return rule;
        }

        IEnumerable<string> offenders = (result.FailingTypeNames ?? []).Take(20);

        return $"{rule}. Offending types: {string.Join(", ", offenders)}";
    }
}
