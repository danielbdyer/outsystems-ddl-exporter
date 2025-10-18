using System;
using Osm.Domain.Configuration;

namespace Osm.Validation.Tightening;

internal sealed class LegacyPolicyAdapter : ILegacyPolicyAdapter
{
    public TighteningOptions Adapt(TighteningMode mode)
    {
        var defaults = TighteningOptions.Default;
        var policyResult = PolicyOptions.Create(mode, defaults.Policy.NullBudget);

        if (!policyResult.IsSuccess)
        {
            throw new InvalidOperationException($"Unable to adapt legacy tightening mode '{mode}'.");
        }

        var policy = policyResult.Value;
        var optionsResult = TighteningOptions.Create(
            policy,
            defaults.ForeignKeys,
            defaults.Uniqueness,
            defaults.Remediation,
            defaults.Emission,
            defaults.Mocking);

        if (!optionsResult.IsSuccess)
        {
            throw new InvalidOperationException($"Unable to construct tightening options for legacy mode '{mode}'.");
        }

        return optionsResult.Value;
    }
}
