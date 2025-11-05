using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Osm.Pipeline.UatUsers.Steps;

public sealed class AnalyzeForeignKeyValuesStep : IPipelineStep<UatUsersContext>
{
    private readonly IUserForeignKeyValueProvider _valueProvider;
    private readonly IUserForeignKeySnapshotStore _snapshotStore;
    private readonly ILogger<AnalyzeForeignKeyValuesStep> _logger;

    public AnalyzeForeignKeyValuesStep(ILogger<AnalyzeForeignKeyValuesStep>? logger = null)
        : this(
            new SqlUserForeignKeyValueProvider(),
            new FileUserForeignKeySnapshotStore(),
            logger)
    {
    }

    public AnalyzeForeignKeyValuesStep(
        IUserForeignKeyValueProvider valueProvider,
        IUserForeignKeySnapshotStore snapshotStore,
        ILogger<AnalyzeForeignKeyValuesStep>? logger = null)
    {
        _valueProvider = valueProvider ?? throw new ArgumentNullException(nameof(valueProvider));
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        _logger = logger ?? NullLogger<AnalyzeForeignKeyValuesStep>.Instance;
    }

    public string Name => "analyze-foreign-key-values";

    public async Task ExecuteAsync(UatUsersContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        _logger.LogInformation(
            "Analyzing {ColumnCount} foreign key columns for user references.",
            context.UserFkCatalog.Count);

        if (context.UserFkCatalog.Count == 0)
        {
            _logger.LogInformation("No foreign key columns discovered; skipping analysis.");
            context.SetForeignKeyValueCounts(ImmutableDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>.Empty);
            context.SetOrphanUserIds(Array.Empty<UserIdentifier>());
            return;
        }

        IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>> counts;

        UserForeignKeySnapshot? snapshot = null;
        if (!string.IsNullOrWhiteSpace(context.SnapshotPath))
        {
            _logger.LogInformation("Attempting to load snapshot from {SnapshotPath}.", context.SnapshotPath);
            snapshot = await _snapshotStore.LoadAsync(context.SnapshotPath!, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                _logger.LogInformation("Snapshot not found or could not be deserialized.");
            }
            else if (!string.Equals(snapshot.SourceFingerprint, context.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Snapshot fingerprint {SnapshotFingerprint} does not match source {SourceFingerprint}; ignoring snapshot.",
                    snapshot.SourceFingerprint,
                    context.SourceFingerprint);
                snapshot = null;
            }
            else
            {
                _logger.LogInformation(
                    "Snapshot captured at {CapturedAt:u} is compatible and will be reused.",
                    snapshot.CapturedAt);
            }
        }

        if (snapshot is not null)
        {
            counts = BuildCountsFromSnapshot(snapshot, context.UserFkCatalog);
            _logger.LogInformation(
                "Loaded foreign key value counts from snapshot for {ColumnCount} columns.",
                counts.Count);
        }
        else
        {
            _logger.LogInformation("Collecting foreign key value counts from the source database.");
            counts = await _valueProvider
                .CollectAsync(context.UserFkCatalog, context.ConnectionFactory, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(context.SnapshotPath))
            {
                var snapshotToPersist = BuildSnapshot(context, counts);
                await _snapshotStore.SaveAsync(context.SnapshotPath!, snapshotToPersist, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "Persisted snapshot with {ColumnCount} columns and {AllowedCount} allowed IDs to {SnapshotPath}.",
                    snapshotToPersist.Columns.Count,
                    snapshotToPersist.AllowedUserIds.Count,
                    context.SnapshotPath);
            }
        }

        context.SetForeignKeyValueCounts(counts);
        var orphans = ComputeOrphans(context, counts);
        context.SetOrphanUserIds(orphans);

        var totalDistinctValues = counts.Sum(pair => pair.Value.Count);
        var totalRowCount = counts.Sum(pair => pair.Value.Sum(value => value.Value));

        _logger.LogInformation(
            "Aggregated {DistinctValueCount} distinct user IDs across {RowCount} referencing rows; identified {OrphanCount} orphan IDs.",
            totalDistinctValues,
            totalRowCount,
            orphans.Count);
    }

    private static IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>> BuildCountsFromSnapshot(
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

        var builder = ImmutableDictionary.CreateBuilder<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>();
        foreach (var column in catalog)
        {
            builder[column] = ImmutableDictionary<UserIdentifier, long>.Empty;
        }

        foreach (var columnSnapshot in snapshot.Columns)
        {
            var key = BuildKey(columnSnapshot.Schema, columnSnapshot.Table, columnSnapshot.Column);
            if (!lookup.TryGetValue(key, out var column))
            {
                continue;
            }

            var values = new SortedDictionary<UserIdentifier, long>();
            foreach (var value in columnSnapshot.Values)
            {
                values[value.UserId] = value.RowCount;
            }

            builder[column] = values.Count == 0
                ? ImmutableDictionary<UserIdentifier, long>.Empty
                : ImmutableSortedDictionary.CreateRange(values);
        }

        return builder.ToImmutable();
    }

    private static IReadOnlyCollection<UserIdentifier> ComputeOrphans(
        UatUsersContext context,
        IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>> counts)
    {
        var orphanSet = new SortedSet<UserIdentifier>();
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
        IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>> counts)
    {
        var columns = new List<UserForeignKeySnapshotColumn>(context.UserFkCatalog.Count);
        foreach (var column in context.UserFkCatalog)
        {
            counts.TryGetValue(column, out var values);
            values ??= ImmutableDictionary<UserIdentifier, long>.Empty;

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
