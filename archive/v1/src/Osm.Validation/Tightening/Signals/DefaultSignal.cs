using System;
using Osm.Validation.Tightening;

namespace Osm.Validation.Tightening.Signals;

internal sealed record DefaultSignal()
    : NullabilitySignal("S7_DEFAULT_PRESENT", "Column has default value")
{
    protected override SignalEvaluation EvaluateCore(in NullabilitySignalContext context)
    {
        var hasDefault = !string.IsNullOrWhiteSpace(context.Attribute.DefaultValue);
        var rationales = hasDefault && context.Attribute.IsMandatory
            ? new[] { TighteningRationales.DefaultPresent }
            : Array.Empty<string>();

        return SignalEvaluation.Create(Code, Description, hasDefault, rationales);
    }
}
