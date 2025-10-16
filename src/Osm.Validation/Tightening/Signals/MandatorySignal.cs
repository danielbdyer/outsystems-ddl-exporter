using System;
using Osm.Validation.Tightening;

namespace Osm.Validation.Tightening.Signals;

internal sealed record MandatorySignal()
    : NullabilitySignal("S5_LOGICAL_MANDATORY", "Logical attribute is mandatory")
{
    protected override SignalEvaluation EvaluateCore(in NullabilitySignalContext context)
    {
        var result = context.Attribute.IsMandatory;
        var rationales = result ? new[] { TighteningRationales.Mandatory } : Array.Empty<string>();

        return SignalEvaluation.Create(Code, Description, result, rationales);
    }
}
