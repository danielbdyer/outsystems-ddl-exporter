using System;
using System.Collections.Immutable;
using Osm.Domain.Configuration;

namespace Osm.Validation.Tightening.Signals;

internal static class NullabilitySignalFactory
{
    public static NullabilityPolicyDefinition Create(TighteningMode mode)
    {
        var primaryKey = new PrimaryKeySignal();
        var physical = new PhysicalNotNullSignal();
        var foreignKey = new ForeignKeySupportSignal();
        var unique = new UniqueCleanSignal();
        var mandatory = new MandatorySignal();
        var defaultSignal = new DefaultSignal();
        var evidence = new NullEvidenceSignal();

        NullabilitySignal root = mode switch
        {
            TighteningMode.Cautious => new AnyOfSignal(
                "MODE_CAUTIOUS",
                "Cautious policy (S1 ∪ S2)",
                primaryKey,
                physical),
            TighteningMode.EvidenceGated => new AnyOfSignal(
                "MODE_EVIDENCE_GATED",
                "Evidence gated policy",
                primaryKey,
                physical,
                new RequiresEvidenceSignal(
                    new AnyOfSignal(
                        "EVIDENCE_STRONG_SIGNALS",
                        "Strong signals requiring evidence",
                        foreignKey,
                        unique,
                        mandatory),
                    evidence)),
            TighteningMode.Aggressive => new AnyOfSignal(
                "MODE_AGGRESSIVE",
                "Aggressive policy (S1 ∪ S2 ∪ S3 ∪ S4 ∪ S5)",
                primaryKey,
                physical,
                foreignKey,
                unique,
                mandatory),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported tightening mode."),
        };

        var conditionalSignals = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            foreignKey.Code,
            unique.Code,
            mandatory.Code);

        var evidenceEmbedded = mode == TighteningMode.EvidenceGated;

        return new NullabilityPolicyDefinition(
            root,
            evidence,
            conditionalSignals,
            evidenceEmbedded,
            primaryKey,
            physical,
            unique,
            mandatory,
            defaultSignal,
            foreignKey);
    }
}
