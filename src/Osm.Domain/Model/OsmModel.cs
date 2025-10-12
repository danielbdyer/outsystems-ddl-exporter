using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Osm.Domain.Abstractions;

namespace Osm.Domain.Model;

public sealed record OsmModel(
    DateTime ExportedAtUtc,
    ImmutableArray<ModuleModel> Modules,
    ImmutableArray<SequenceModel> Sequences,
    ImmutableArray<ExtendedProperty> ExtendedProperties)
{
    public static Result<OsmModel> Create(
        DateTime exportedAtUtc,
        IEnumerable<ModuleModel> modules,
        IEnumerable<SequenceModel>? sequences = null,
        IEnumerable<ExtendedProperty>? extendedProperties = null)
    {
        if (modules is null)
        {
            throw new ArgumentNullException(nameof(modules));
        }

        var materialized = modules.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            return Result<OsmModel>.Failure(ValidationError.Create("model.modules.empty", "Model must include at least one module."));
        }

        if (materialized.Select(m => m.Name.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() != materialized.Length)
        {
            return Result<OsmModel>.Failure(ValidationError.Create("model.modules.duplicate", "Duplicate module names detected."));
        }

        var sequenceArray = (sequences ?? Enumerable.Empty<SequenceModel>()).ToImmutableArray();
        if (sequenceArray.IsDefault)
        {
            sequenceArray = ImmutableArray<SequenceModel>.Empty;
        }

        if (!AreSequenceNamesUnique(sequenceArray))
        {
            return Result<OsmModel>.Failure(ValidationError.Create("model.sequences.duplicate", "Duplicate sequence names detected."));
        }

        var propertyArray = (extendedProperties ?? Enumerable.Empty<ExtendedProperty>()).ToImmutableArray();
        if (propertyArray.IsDefault)
        {
            propertyArray = ExtendedProperty.EmptyArray;
        }

        return Result<OsmModel>.Success(new OsmModel(exportedAtUtc, materialized, sequenceArray, propertyArray));
    }

    private static bool AreSequenceNamesUnique(ImmutableArray<SequenceModel> sequences)
    {
        if (sequences.IsDefaultOrEmpty)
        {
            return true;
        }

        var comparer = StringComparer.OrdinalIgnoreCase;
        var seen = new HashSet<string>(comparer);
        foreach (var sequence in sequences)
        {
            var key = $"{sequence.Schema.Value}.{sequence.Name.Value}";
            if (!seen.Add(key))
            {
                return false;
            }
        }

        return true;
    }
}
