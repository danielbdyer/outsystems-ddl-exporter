using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Osm.Domain.Profiling;

namespace Osm.Cli;

internal static class ProfileSnapshotDebugFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string ToJson(ProfileSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var document = new ProfileSnapshotDebugDocument(
            snapshot.Columns.Select(column => new ProfileColumnDebug(
                column.Schema.Value,
                column.Table.Value,
                column.Column.Value,
                column.IsNullablePhysical,
                column.IsComputed,
                column.IsPrimaryKey,
                column.IsUniqueKey,
                column.DefaultDefinition,
                column.RowCount,
                column.NullCount)).ToArray(),
            snapshot.UniqueCandidates.Select(unique => new ProfileUniqueCandidateDebug(
                unique.Schema.Value,
                unique.Table.Value,
                unique.Column.Value,
                unique.HasDuplicate)).ToArray(),
            snapshot.CompositeUniqueCandidates.Select(composite => new ProfileCompositeUniqueCandidateDebug(
                composite.Schema.Value,
                composite.Table.Value,
                composite.Columns.Select(column => column.Value).ToArray(),
                composite.HasDuplicate)).ToArray(),
            snapshot.ForeignKeys.Select(foreignKey => new ProfileForeignKeyDebug(
                foreignKey.Reference.FromSchema.Value,
                foreignKey.Reference.FromTable.Value,
                foreignKey.Reference.FromColumn.Value,
                foreignKey.Reference.ToSchema.Value,
                foreignKey.Reference.ToTable.Value,
                foreignKey.Reference.ToColumn.Value,
                foreignKey.Reference.HasDatabaseConstraint,
                foreignKey.HasOrphan)).ToArray());

        return JsonSerializer.Serialize(document, JsonOptions);
    }

    private sealed record ProfileSnapshotDebugDocument(
        IReadOnlyList<ProfileColumnDebug> Columns,
        IReadOnlyList<ProfileUniqueCandidateDebug> UniqueCandidates,
        IReadOnlyList<ProfileCompositeUniqueCandidateDebug> CompositeUniqueCandidates,
        IReadOnlyList<ProfileForeignKeyDebug> ForeignKeys);

    private sealed record ProfileColumnDebug(
        string Schema,
        string Table,
        string Column,
        bool IsNullablePhysical,
        bool IsComputed,
        bool IsPrimaryKey,
        bool IsUniqueKey,
        string? DefaultDefinition,
        long RowCount,
        long NullCount);

    private sealed record ProfileUniqueCandidateDebug(
        string Schema,
        string Table,
        string Column,
        bool HasDuplicate);

    private sealed record ProfileCompositeUniqueCandidateDebug(
        string Schema,
        string Table,
        IReadOnlyList<string> Columns,
        bool HasDuplicate);

    private sealed record ProfileForeignKeyDebug(
        string FromSchema,
        string FromTable,
        string FromColumn,
        string ToSchema,
        string ToTable,
        string ToColumn,
        bool HasDatabaseConstraint,
        bool HasOrphan);
}
