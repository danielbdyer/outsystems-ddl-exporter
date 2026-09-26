using Osm.Validation.Tightening;

namespace Osm.Validation.Tightening.Signals;

internal sealed record PrimaryKeySignal()
    : NullabilitySignal("S1_PK", "Column is OutSystems Identifier (PK)")
{
    protected override SignalEvaluation EvaluateCore(in NullabilitySignalContext context)
    {
        var result = context.Attribute.IsIdentifier;
        var rationales = result ? new[] { TighteningRationales.PrimaryKey } : Array.Empty<string>();

        return SignalEvaluation.Create(Code, Description, result, rationales);
    }
}
