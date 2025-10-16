using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;
using Osm.Domain.Profiling;

namespace Osm.Validation.Tightening;

public sealed class UniqueIndexEvidenceAggregator
{
    private UniqueIndexEvidenceAggregator(
        ISet<ColumnCoordinate> singleColumnClean,
        ISet<ColumnCoordinate> singleColumnDuplicates,
        ISet<ColumnCoordinate> compositeClean,
        ISet<ColumnCoordinate> compositeDuplicates,
        IReadOnlyDictionary<string, CompositeUniqueCandidateProfile> compositeProfiles)
    {
        SingleColumnClean = singleColumnClean;
        SingleColumnDuplicates = singleColumnDuplicates;
        CompositeClean = compositeClean;
        CompositeDuplicates = compositeDuplicates;
        CompositeProfiles = compositeProfiles;
    }

    public ISet<ColumnCoordinate> SingleColumnClean { get; }

    public ISet<ColumnCoordinate> SingleColumnDuplicates { get; }

    public ISet<ColumnCoordinate> CompositeClean { get; }

    public ISet<ColumnCoordinate> CompositeDuplicates { get; }

    public IReadOnlyDictionary<string, CompositeUniqueCandidateProfile> CompositeProfiles { get; }

    public static UniqueIndexEvidenceAggregator Create(
        OsmModel model,
        IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> uniqueProfiles,
        ImmutableArray<CompositeUniqueCandidateProfile> compositeProfiles,
        bool enforceSingleColumnUnique,
        bool enforceCompositeUnique)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (uniqueProfiles is null)
        {
            throw new ArgumentNullException(nameof(uniqueProfiles));
        }

        var singleColumnClean = BuildSingleColumnClean(model, uniqueProfiles, enforceSingleColumnUnique);
        var singleColumnDuplicates = BuildSingleColumnDuplicates(model, uniqueProfiles);
        var compositeSignals = BuildCompositeSignals(model, compositeProfiles, enforceCompositeUnique);

        return new UniqueIndexEvidenceAggregator(
            singleColumnClean,
            singleColumnDuplicates,
            compositeSignals.Clean,
            compositeSignals.Duplicates,
            compositeSignals.Lookup);
    }

    private static ISet<ColumnCoordinate> BuildSingleColumnClean(
        OsmModel model,
        IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> uniqueProfiles,
        bool enforceSingleColumnUnique)
    {
        var result = new HashSet<ColumnCoordinate>();

        if (!enforceSingleColumnUnique)
        {
            return result;
        }

        foreach (var entity in model.Modules.SelectMany(static m => m.Entities))
        {
            foreach (var index in entity.Indexes.Where(static i => i.IsUnique))
            {
                var keyColumns = index.Columns
                    .Where(static c => !c.IsIncluded)
                    .ToArray();

                if (keyColumns.Length != 1)
                {
                    continue;
                }

                var keyColumn = keyColumns[0];
                var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, keyColumn.Column);
                if (uniqueProfiles.TryGetValue(coordinate, out var profile) && !profile.HasDuplicate)
                {
                    result.Add(coordinate);
                }
            }
        }

        return result;
    }

    private static ISet<ColumnCoordinate> BuildSingleColumnDuplicates(
        OsmModel model,
        IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> uniqueProfiles)
    {
        var result = new HashSet<ColumnCoordinate>();

        foreach (var entity in model.Modules.SelectMany(static m => m.Entities))
        {
            foreach (var index in entity.Indexes.Where(static i => i.IsUnique))
            {
                var keyColumns = index.Columns
                    .Where(static c => !c.IsIncluded)
                    .ToArray();

                if (keyColumns.Length != 1)
                {
                    continue;
                }

                var keyColumn = keyColumns[0];
                var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, keyColumn.Column);
                if (uniqueProfiles.TryGetValue(coordinate, out var profile) && profile.HasDuplicate)
                {
                    result.Add(coordinate);
                }
            }
        }

        return result;
    }

    private static CompositeSignalSet BuildCompositeSignals(
        OsmModel model,
        ImmutableArray<CompositeUniqueCandidateProfile> compositeProfiles,
        bool enforceCompositeUnique)
    {
        var clean = new HashSet<ColumnCoordinate>();
        var duplicates = new HashSet<ColumnCoordinate>();
        var lookup = new Dictionary<string, CompositeUniqueCandidateProfile>(StringComparer.OrdinalIgnoreCase);

        if (!compositeProfiles.IsDefaultOrEmpty)
        {
            foreach (var profile in compositeProfiles)
            {
                var key = UniqueIndexEvidenceKey.Create(profile.Schema.Value, profile.Table.Value, profile.Columns.Select(static c => c.Value));
                lookup[key] = profile;
            }
        }

        foreach (var entity in model.Modules.SelectMany(static m => m.Entities))
        {
            foreach (var index in entity.Indexes.Where(static i => i.IsUnique))
            {
                var keyColumns = index.Columns
                    .Where(static c => !c.IsIncluded)
                    .ToArray();

                if (keyColumns.Length <= 1)
                {
                    continue;
                }

                var key = UniqueIndexEvidenceKey.Create(
                    entity.Schema.Value,
                    entity.PhysicalName.Value,
                    keyColumns.Select(static c => c.Column.Value));

                if (!lookup.TryGetValue(key, out var profile))
                {
                    continue;
                }

                foreach (var column in keyColumns)
                {
                    var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, column.Column);
                    if (profile.HasDuplicate)
                    {
                        duplicates.Add(coordinate);
                    }
                    else if (enforceCompositeUnique)
                    {
                        clean.Add(coordinate);
                    }
                }
            }
        }

        return new CompositeSignalSet(clean, duplicates, lookup);
    }

    private sealed record CompositeSignalSet(
        ISet<ColumnCoordinate> Clean,
        ISet<ColumnCoordinate> Duplicates,
        IReadOnlyDictionary<string, CompositeUniqueCandidateProfile> Lookup);
}

internal static class UniqueIndexEvidenceKey
{
    public static string Create(string schema, string table, IEnumerable<string> columns)
    {
        if (schema is null)
        {
            throw new ArgumentNullException(nameof(schema));
        }

        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        if (columns is null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        var normalizedColumns = columns
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Select(static c => c.Trim().ToUpperInvariant())
            .OrderBy(static c => c, StringComparer.Ordinal);

        return $"{schema.ToUpperInvariant()}|{table.ToUpperInvariant()}|{string.Join(',', normalizedColumns)}";
    }
}
