using Osm.Domain.Profiling;
using Osm.Validation.Tightening;

namespace Osm.Validation.Tightening.Signals;

internal sealed record MandatorySignal()
    : NullabilitySignal("S5_LOGICAL_MANDATORY", "Logical attribute is mandatory")
{
    protected override SignalEvaluation EvaluateCore(in NullabilitySignalContext context)
    {
        if (!context.Attribute.IsMandatory)
        {
            return SignalEvaluation.Create(Code, Description, result: false);
        }

        if (context.ColumnProfile is not { } profile ||
            profile.NullCountStatus.Outcome is not ProfilingProbeOutcome.Succeeded and not ProfilingProbeOutcome.TrustedConstraint)
        {
            return SignalEvaluation.Create(
                Code,
                Description,
                result: true,
                rationales: new[] { TighteningRationales.Mandatory });
        }

        if (HasNullsBeyondBudget(profile, context.Options.Policy.NullBudget))
        {
            return SignalEvaluation.Create(
                Code,
                Description,
                result: false,
                rationales: new[]
                {
                    TighteningRationales.Mandatory,
                    TighteningRationales.DataHasNulls
                });
        }

        return SignalEvaluation.Create(
            Code,
            Description,
            result: true,
            rationales: new[] { TighteningRationales.Mandatory });
    }

    private static bool HasNullsBeyondBudget(ColumnProfile profile, double nullBudget)
    {
        if (profile.NullCount == 0 || profile.RowCount == 0)
        {
            return false;
        }

        if (nullBudget <= 0)
        {
            return true;
        }

        var allowedNulls = profile.RowCount * nullBudget;
        return profile.NullCount > allowedNulls;
    }
}
