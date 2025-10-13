using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.UatUsers.Steps;

public sealed class AnalyzeForeignKeyValuesStep : IPipelineStep<UatUsersContext>
{
    private readonly IUserForeignKeyValueProvider _valueProvider;
    private readonly IUserForeignKeySnapshotStore _snapshotStore;

    public AnalyzeForeignKeyValuesStep()
        : this(new SqlUserForeignKeyValueProvider(), new FileUserForeignKeySnapshotStore())
    {
    }

    public AnalyzeForeignKeyValuesStep(IUserForeignKeyValueProvider valueProvider, IUserForeignKeySnapshotStore snapshotStore)
    {
        _valueProvider = valueProvider ?? throw new ArgumentNullException(nameof(valueProvider));
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
    }

    public string Name => "analyze-foreign-key-values";

    public async Task ExecuteAsync(UatUsersContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.UserFkCatalog.Count == 0)
        {
            context.SetForeignKeyValueCounts(ImmutableDictionary<UserFkColumn, IReadOnlyDictionary<long, long>>.Empty);
            context.SetOrphanUserIds(Array.Empty<long>());
            return;
        }

        IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<long, long>> counts;

        UserForeignKeySnapshot? snapshot = null;
        if (!string.IsNullOrWhiteSpace(context.SnapshotPath))
        {
            snapshot = await _snapshotStore.LoadAsync(context.SnapshotPath!, cancellationToken).ConfigureAwait(false);
            if (snapshot is not null && !string.Equals(snapshot.SourceFingerprint, context.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                snapshot = null;
            }
        }

        if (snapshot is not null)
        {
            counts = BuildCountsFromSnapshot(snapshot, context.UserFkCatalog);
        }
        else
        {
            counts = await _valueProvider.CollectAsync(context.UserFkCatalog, context.ConnectionFactory, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(context.SnapshotPath))
            {
                var snapshotToPersist = BuildSnapshot(context, counts);
                await _snapshotStore.SaveAsync(context.SnapshotPath!, snapshotToPersist, cancellationToken).ConfigureAwait(false);
            }
        }

        context.SetForeignKeyValueCounts(counts);
        var orphans = ComputeOrphans(context, counts);
        context.SetOrphanUserIds(orphans);
    }

    private static IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<long, long>> BuildCountsFromSnapshot(
        UserForeignKeySnapshot snapshot,
        IReadOnlyList<UserFkColumn> catalog)
    {
        static string BuildKey(string schema, string table, string column) => string.Create(
            schema.Length + table.Length + column.Length + 2,
            (schema, table, column),
            static (span, value) =>
            {
                var index = 0;
                value.schema.AsSpan().CopyTo(span);
                index += value.schema.Length;
                span[index++] = '.';
                value.table.AsSpan().CopyTo(span[index..]);
                index += value.table.Length;
                span[index++] = '.';
                value.column.AsSpan().CopyTo(span[index..]);
            });

        var lookup = catalog.ToDictionary(
            column => BuildKey(column.SchemaName, column.TableName, column.ColumnName),
            column => column,
            StringComparer.OrdinalIgnoreCase);

        var builder = ImmutableDictionary.CreateBuilder<UserFkColumn, IReadOnlyDictionary<long, long>>();
        foreach (var column in catalog)
        {
            builder[column] = ImmutableDictionary<long, long>.Empty;
        }

        foreach (var columnSnapshot in snapshot.Columns)
        {
            var key = BuildKey(columnSnapshot.Schema, columnSnapshot.Table, columnSnapshot.Column);
            if (!lookup.TryGetValue(key, out var column))
            {
                continue;
            }

            var values = new SortedDictionary<long, long>();
            foreach (var value in columnSnapshot.Values)
            {
                values[value.UserId] = value.RowCount;
            }

            builder[column] = values.Count == 0
                ? ImmutableDictionary<long, long>.Empty
                : ImmutableSortedDictionary.CreateRange(values);
        }

        return builder.ToImmutable();
    }

    private static IReadOnlyCollection<long> ComputeOrphans(
        UatUsersContext context,
        IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<long, long>> counts)
    {
        var orphanSet = new SortedSet<long>();
        foreach (var column in counts.Values)
        {
            foreach (var pair in column)
            {
                if (!context.IsAllowedUser(pair.Key))
                {
                    orphanSet.Add(pair.Key);
                }
            }
        }

        return orphanSet;
    }

    private static UserForeignKeySnapshot BuildSnapshot(
        UatUsersContext context,
        IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<long, long>> counts)
    {
        var columns = new List<UserForeignKeySnapshotColumn>(context.UserFkCatalog.Count);
        foreach (var column in context.UserFkCatalog)
        {
            counts.TryGetValue(column, out var values);
            values ??= ImmutableDictionary<long, long>.Empty;

            var snapshotValues = values
                .OrderBy(static pair => pair.Key)
                .Select(pair => new UserForeignKeySnapshotValue
                {
                    UserId = pair.Key,
                    RowCount = pair.Value
                })
                .ToArray();

            columns.Add(new UserForeignKeySnapshotColumn
            {
                Schema = column.SchemaName,
                Table = column.TableName,
                Column = column.ColumnName,
                Values = snapshotValues
            });
        }

        var allowed = context.AllowedUserIds.OrderBy(id => id).ToArray();
        var orphans = ComputeOrphans(context, counts).OrderBy(id => id).ToArray();

        return new UserForeignKeySnapshot
        {
            CapturedAt = DateTimeOffset.UtcNow,
            SourceFingerprint = context.SourceFingerprint,
            AllowedUserIds = allowed,
            OrphanUserIds = orphans,
            Columns = columns
        };
    }
}
