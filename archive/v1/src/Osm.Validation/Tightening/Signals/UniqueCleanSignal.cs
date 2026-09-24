using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Validation.Tightening;

namespace Osm.Validation.Tightening.Signals;

internal sealed record UniqueCleanSignal()
    : NullabilitySignal("S4_UNIQUE_CLEAN", "Unique index (single or composite) has no nulls or duplicates")
{
    protected override SignalEvaluation EvaluateCore(in NullabilitySignalContext context)
    {
        var rationales = new List<string>();
        var satisfied = false;

        if (context.IsSingleUniqueClean)
        {
            satisfied = true;
            rationales.Add(TighteningRationales.UniqueNoNulls);
        }
        else if (context.HasSingleUniqueDuplicates || context.UniqueProfile?.HasDuplicate == true)
        {
            rationales.Add(TighteningRationales.UniqueDuplicatesPresent);
        }

        if (context.IsCompositeUniqueClean)
        {
            satisfied = true;
            rationales.Add(TighteningRationales.CompositeUniqueNoNulls);
        }
        else if (context.HasCompositeUniqueDuplicates)
        {
            rationales.Add(TighteningRationales.CompositeUniqueDuplicatesPresent);
        }

        return SignalEvaluation.Create(Code, Description, satisfied, rationales);
    }
}
