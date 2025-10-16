using System.Collections.Generic;
using System.Collections.Immutable;

namespace Osm.Validation.Tightening.Signals;

internal sealed record NullabilityPolicyDefinition(
    NullabilitySignal Root,
    NullabilitySignal Evidence,
    ImmutableHashSet<string> ConditionalSignalCodes,
    bool EvidenceEmbeddedInRoot,
    PrimaryKeySignal PrimaryKeySignal,
    PhysicalNotNullSignal PhysicalSignal,
    UniqueCleanSignal UniqueSignal,
    MandatorySignal MandatorySignal,
    DefaultSignal DefaultSignal,
    ForeignKeySupportSignal ForeignKeySignal)
{
    public bool RequiresRemediationEvaluation => !EvidenceEmbeddedInRoot;
}
