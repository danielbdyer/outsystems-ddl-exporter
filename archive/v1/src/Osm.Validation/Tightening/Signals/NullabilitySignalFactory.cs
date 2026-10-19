using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;

namespace Osm.Validation.Tightening.Signals;

internal static class NullabilitySignalFactory
{
    public static NullabilityPolicyDefinition Create(TighteningMode mode)
    {
        var definition = TighteningPolicyMatrix.Nullability.GetMode(mode);

        var primaryKey = new PrimaryKeySignal();
        var physical = new PhysicalNotNullSignal();
        var foreignKey = new ForeignKeySupportSignal();
        var unique = new UniqueCleanSignal();
        var mandatory = new MandatorySignal();
        var defaultSignal = new DefaultSignal();
        var evidence = new NullEvidenceSignal();

        var registry = new Dictionary<TighteningPolicyMatrix.NullabilitySignalKey, NullabilitySignal>
        {
            [TighteningPolicyMatrix.NullabilitySignalKey.PrimaryKey] = primaryKey,
            [TighteningPolicyMatrix.NullabilitySignalKey.Physical] = physical,
            [TighteningPolicyMatrix.NullabilitySignalKey.ForeignKey] = foreignKey,
            [TighteningPolicyMatrix.NullabilitySignalKey.Unique] = unique,
            [TighteningPolicyMatrix.NullabilitySignalKey.Mandatory] = mandatory
        };

        var rootChildren = new List<NullabilitySignal>();
        foreach (var signalDefinition in definition.SignalDefinitions)
        {
            if (signalDefinition.Participation != TighteningPolicyMatrix.NullabilitySignalParticipation.Tighten)
            {
                continue;
            }

            var child = registry[signalDefinition.Signal];
            if (signalDefinition.RequiresEvidence)
            {
                child = new RequiresEvidenceSignal(child, evidence);
            }

            rootChildren.Add(child);
        }

        var root = new AnyOfSignal(definition.Code, definition.Description, rootChildren);

        var conditionalCodes = definition.ConditionalSignals
            .Select(signal => registry[signal].Code)
            .ToImmutableHashSet(StringComparer.Ordinal);

        return new NullabilityPolicyDefinition(
            root,
            evidence,
            conditionalCodes,
            definition.EvidenceEmbeddedInRoot,
            primaryKey,
            physical,
            unique,
            mandatory,
            defaultSignal,
            foreignKey);
    }
}
