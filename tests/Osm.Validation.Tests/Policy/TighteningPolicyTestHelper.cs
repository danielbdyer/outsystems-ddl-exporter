using Osm.Domain.Configuration;

namespace Osm.Validation.Tests.Policy;

internal static class TighteningPolicyTestHelper
{
    public static TighteningOptions CreateOptions(TighteningMode mode, double nullBudget = 0.0)
    {
        var defaults = TighteningOptions.Default;
        var policy = PolicyOptions.Create(mode, nullBudget).Value;
        return TighteningOptions.Create(
            policy,
            defaults.ForeignKeys,
            defaults.Uniqueness,
            defaults.Remediation,
            defaults.Emission,
            defaults.Mocking).Value;
    }
}
