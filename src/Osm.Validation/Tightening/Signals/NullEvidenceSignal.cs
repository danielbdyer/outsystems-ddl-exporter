using System;
using Osm.Domain.Profiling;
using Osm.Validation.Tightening;

namespace Osm.Validation.Tightening.Signals;

internal sealed record NullEvidenceSignal()
    : NullabilitySignal("D1_DATA_NO_NULLS", "Profiling evidence shows no NULL values within budget")
{
    protected override SignalEvaluation EvaluateCore(in NullabilitySignalContext context)
    {
        if (context.ColumnProfile is not { } profile)
        {
            return SignalEvaluation.Create(Code, Description, result: false);
        }

        if (profile.NullCountStatus.Outcome is not ProfilingProbeOutcome.Succeeded and not ProfilingProbeOutcome.TrustedConstraint)
        {
            return SignalEvaluation.Create(
                Code,
                Description,
                result: false,
                rationales: new[] { TighteningRationales.ProfileMissing });
        }

        var withinBudget = IsWithinNullBudget(profile, context.Options.Policy.NullBudget, out var usedBudget);
        if (!withinBudget)
        {
            return SignalEvaluation.Create(Code, Description, result: false);
        }

        var rationales = usedBudget
            ? new[] { TighteningRationales.DataNoNulls, TighteningRationales.NullBudgetEpsilon }
            : new[] { TighteningRationales.DataNoNulls };

        return SignalEvaluation.Create(Code, Description, result: true, rationales);
    }

    private static bool IsWithinNullBudget(ColumnProfile profile, double nullBudget, out bool usedBudget)
    {
        usedBudget = false;

        if (profile.NullCount == 0)
        {
            return true;
        }

        if (profile.RowCount == 0)
        {
            return true;
        }

        if (nullBudget <= 0)
        {
            return false;
        }

        var allowed = profile.RowCount * nullBudget;
        if (profile.NullCount <= allowed)
        {
            usedBudget = true;
            return true;
        }

        return false;
    }
}
