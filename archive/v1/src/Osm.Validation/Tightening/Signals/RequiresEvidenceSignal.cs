using System.Collections.Immutable;

namespace Osm.Validation.Tightening.Signals;

internal sealed record RequiresEvidenceSignal(NullabilitySignal Inner, NullabilitySignal Evidence)
    : NullabilitySignal(
        $"{Inner.Code}_REQUIRES_{Evidence.Code}",
        $"{Inner.Description} (requires {Evidence.Description})")
{
    protected override SignalEvaluation EvaluateCore(in NullabilitySignalContext context)
    {
        var innerEvaluation = Inner.Evaluate(context);
        if (!innerEvaluation.Result)
        {
            return SignalEvaluation.Create(Code, Description, result: false, children: ImmutableArray.Create(innerEvaluation));
        }

        var evidenceEvaluation = Evidence.Evaluate(context);
        var result = evidenceEvaluation.Result;

        return SignalEvaluation.Create(
            Code,
            Description,
            result,
            children: ImmutableArray.Create(innerEvaluation, evidenceEvaluation));
    }
}
