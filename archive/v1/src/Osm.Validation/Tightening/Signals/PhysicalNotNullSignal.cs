using System;
using Osm.Validation.Tightening;

namespace Osm.Validation.Tightening.Signals;

internal sealed record PhysicalNotNullSignal()
    : NullabilitySignal("S2_DB_NOT_NULL", "Physical column is marked NOT NULL")
{
    protected override SignalEvaluation EvaluateCore(in NullabilitySignalContext context)
    {
        var result = context.ColumnProfile is { IsNullablePhysical: false };
        var rationales = result ? new[] { TighteningRationales.PhysicalNotNull } : Array.Empty<string>();

        return SignalEvaluation.Create(Code, Description, result, rationales);
    }
}
