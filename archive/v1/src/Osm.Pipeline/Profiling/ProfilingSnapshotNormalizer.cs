using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Profiling;

internal static class ProfilingSnapshotNormalizer
{
    public static ImmutableArray<ProfilingEnvironmentSnapshot> Normalize(
        IEnumerable<ProfilingEnvironmentSnapshot> snapshots)
    {
        if (snapshots is null)
        {
            throw new ArgumentNullException(nameof(snapshots));
        }

        var builder = ImmutableArray.CreateBuilder<ProfilingEnvironmentSnapshot>();

        foreach (var snapshot in snapshots)
        {
            if (snapshot is null)
            {
                continue;
            }

            if (snapshot.TableNameMappings.IsDefaultOrEmpty)
            {
                builder.Add(snapshot);
                continue;
            }

            var lookup = BuildCanonicalLookup(snapshot.TableNameMappings);
            if (lookup.Count == 0)
            {
                builder.Add(snapshot);
                continue;
            }

            var normalized = NormalizeSnapshot(snapshot.Snapshot, lookup);
            builder.Add(snapshot with { Snapshot = normalized });
        }

        return builder.ToImmutable();
    }

    private static Dictionary<(string Schema, string Table), (string Schema, string Table)> BuildCanonicalLookup(
        ImmutableArray<TableNameMapping> mappings)
    {
        var lookup = new Dictionary<(string Schema, string Table), (string Schema, string Table)>(TableKeyComparer.Instance);

        foreach (var mapping in mappings)
        {
            var canonical = (Schema: mapping.SourceSchema, Table: mapping.SourceTable);

            lookup[canonical] = canonical;

            var alias = (Schema: mapping.TargetSchema, Table: mapping.TargetTable);
            if (!lookup.TryGetValue(alias, out var existing) ||
                !string.Equals(existing.Schema, canonical.Schema, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(existing.Table, canonical.Table, StringComparison.OrdinalIgnoreCase))
            {
                lookup[alias] = canonical;
            }
        }

        return lookup;
    }

    private static ProfileSnapshot NormalizeSnapshot(
        ProfileSnapshot snapshot,
        IReadOnlyDictionary<(string Schema, string Table), (string Schema, string Table)> lookup)
    {
        var normalizedColumns = snapshot.Columns
            .Select(column => NormalizeColumn(column, lookup))
            .ToImmutableArray();

        var normalizedUniqueCandidates = snapshot.UniqueCandidates
            .Select(candidate => NormalizeUniqueCandidate(candidate, lookup))
            .ToImmutableArray();

        var normalizedCompositeCandidates = snapshot.CompositeUniqueCandidates
            .Select(candidate => NormalizeCompositeCandidate(candidate, lookup))
            .ToImmutableArray();

        var normalizedForeignKeys = snapshot.ForeignKeys
            .Select(foreignKey => NormalizeForeignKey(foreignKey, lookup))
            .ToImmutableArray();

        return snapshot with
        {
            Columns = normalizedColumns,
            UniqueCandidates = normalizedUniqueCandidates,
            CompositeUniqueCandidates = normalizedCompositeCandidates,
            ForeignKeys = normalizedForeignKeys,
        };
    }

    private static ColumnProfile NormalizeColumn(
        ColumnProfile column,
        IReadOnlyDictionary<(string Schema, string Table), (string Schema, string Table)> lookup)
    {
        var canonical = GetCanonical(column.Schema.Value, column.Table.Value, lookup);
        if (canonical.Schema is null)
        {
            return column;
        }

        return column with
        {
            Schema = SchemaName.Create(canonical.Schema).Value,
            Table = TableName.Create(canonical.Table).Value,
        };
    }

    private static UniqueCandidateProfile NormalizeUniqueCandidate(
        UniqueCandidateProfile candidate,
        IReadOnlyDictionary<(string Schema, string Table), (string Schema, string Table)> lookup)
    {
        var canonical = GetCanonical(candidate.Schema.Value, candidate.Table.Value, lookup);
        if (canonical.Schema is null)
        {
            return candidate;
        }

        return candidate with
        {
            Schema = SchemaName.Create(canonical.Schema).Value,
            Table = TableName.Create(canonical.Table).Value,
        };
    }

    private static CompositeUniqueCandidateProfile NormalizeCompositeCandidate(
        CompositeUniqueCandidateProfile candidate,
        IReadOnlyDictionary<(string Schema, string Table), (string Schema, string Table)> lookup)
    {
        var canonical = GetCanonical(candidate.Schema.Value, candidate.Table.Value, lookup);
        if (canonical.Schema is null)
        {
            return candidate;
        }

        return candidate with
        {
            Schema = SchemaName.Create(canonical.Schema).Value,
            Table = TableName.Create(canonical.Table).Value,
        };
    }

    private static ForeignKeyReality NormalizeForeignKey(
        ForeignKeyReality foreignKey,
        IReadOnlyDictionary<(string Schema, string Table), (string Schema, string Table)> lookup)
    {
        var reference = foreignKey.Reference;

        var fromCanonical = GetCanonical(reference.FromSchema.Value, reference.FromTable.Value, lookup);
        var toCanonical = GetCanonical(reference.ToSchema.Value, reference.ToTable.Value, lookup);

        if (fromCanonical.Schema is null && toCanonical.Schema is null)
        {
            return foreignKey;
        }

        var updatedReference = reference with
        {
            FromSchema = fromCanonical.Schema is null
                ? reference.FromSchema
                : SchemaName.Create(fromCanonical.Schema).Value,
            FromTable = fromCanonical.Schema is null
                ? reference.FromTable
                : TableName.Create(fromCanonical.Table).Value,
            ToSchema = toCanonical.Schema is null
                ? reference.ToSchema
                : SchemaName.Create(toCanonical.Schema).Value,
            ToTable = toCanonical.Schema is null
                ? reference.ToTable
                : TableName.Create(toCanonical.Table).Value,
        };

        return foreignKey with { Reference = updatedReference };
    }

    private static (string? Schema, string? Table) GetCanonical(
        string schema,
        string table,
        IReadOnlyDictionary<(string Schema, string Table), (string Schema, string Table)> lookup)
    {
        if (lookup.TryGetValue((schema, table), out var canonical) &&
            (!string.Equals(schema, canonical.Schema, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(table, canonical.Table, StringComparison.OrdinalIgnoreCase)))
        {
            return canonical;
        }

        return (null, null);
    }
}
