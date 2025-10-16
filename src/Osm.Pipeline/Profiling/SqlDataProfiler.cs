using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Profiling;

public sealed class SqlDataProfiler : IDataProfiler
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly OsmModel _model;
    private readonly SqlProfilerOptions _options;
    private readonly ITableMetadataLoader _metadataLoader;
    private readonly IProfilingPlanBuilder _planBuilder;
    private readonly IProfilingQueryExecutor _queryExecutor;

    public SqlDataProfiler(IDbConnectionFactory connectionFactory, OsmModel model, SqlProfilerOptions? options = null)
        : this(
            connectionFactory,
            model,
            options ?? SqlProfilerOptions.Default,
            null,
            null,
            null)
    {
    }

    internal SqlDataProfiler(
        IDbConnectionFactory connectionFactory,
        OsmModel model,
        SqlProfilerOptions options,
        ITableMetadataLoader? metadataLoader,
        IProfilingPlanBuilder? planBuilder,
        IProfilingQueryExecutor? queryExecutor)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _metadataLoader = metadataLoader ?? new TableMetadataLoader(_options);
        _planBuilder = planBuilder ?? new ProfilingPlanBuilder(_model);
        _queryExecutor = queryExecutor ?? new ProfilingQueryExecutor(_connectionFactory, _options);
    }

    public async Task<Result<ProfilingCaptureResult>> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var tables = CollectTables();
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            var metadata = await _metadataLoader.LoadColumnMetadataAsync(connection, tables, cancellationToken).ConfigureAwait(false);
            var rowCounts = await _metadataLoader.LoadRowCountsAsync(connection, tables, cancellationToken).ConfigureAwait(false);
            var plans = _planBuilder.BuildPlans(metadata, rowCounts);
            var resultsLookup = new ConcurrentDictionary<(string Schema, string Table), TableProfilingResults>(TableKeyComparer.Instance);

            using var gate = new SemaphoreSlim(_options.MaxConcurrentTableProfiles);
            var tasks = new List<Task>(plans.Count);
            foreach (var plan in plans.Values)
            {
                tasks.Add(ProfileTableWithGateAsync(plan, resultsLookup, gate, cancellationToken));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            var warningBuilder = ImmutableArray.CreateBuilder<string>();
            foreach (var plan in plans.Values
                .OrderBy(static p => p.Schema, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static p => p.Table, StringComparer.OrdinalIgnoreCase))
            {
                if (!resultsLookup.TryGetValue((plan.Schema, plan.Table), out var tableResults))
                {
                    continue;
                }

                foreach (var warning in BuildTimeoutWarnings(plan, tableResults))
                {
                    warningBuilder.Add(warning);
                }
            }

            var columnProfiles = new List<ColumnProfile>();
            var uniqueProfiles = new List<UniqueCandidateProfile>();
            var compositeProfiles = new List<CompositeUniqueCandidateProfile>();
            var foreignKeys = new List<ForeignKeyReality>();

            foreach (var entity in _model.Modules.SelectMany(static module => module.Entities))
            {
                var schema = entity.Schema.Value;
                var table = entity.PhysicalName.Value;
                var tableKey = (schema, table);
                rowCounts.TryGetValue(tableKey, out var tableRowCount);
                var tableResults = resultsLookup.TryGetValue(tableKey, out var computed)
                    ? computed
                    : TableProfilingResults.Empty;

                foreach (var attribute in entity.Attributes)
                {
                    var columnName = attribute.ColumnName.Value;
                    var columnKey = (schema, table, columnName);
                    if (!metadata.TryGetValue(columnKey, out var meta))
                    {
                        continue;
                    }

                    var nullCount = tableResults.NullCounts.TryGetValue(columnName, out var value) ? value : 0L;

                    var columnProfileResult = ColumnProfile.Create(
                        SchemaName.Create(schema).Value,
                        TableName.Create(table).Value,
                        ColumnName.Create(columnName).Value,
                        meta.IsNullable,
                        meta.IsComputed,
                        meta.IsPrimaryKey,
                        IsSingleColumnUnique(entity, columnName),
                        meta.DefaultDefinition,
                        tableRowCount,
                        nullCount);

                    if (columnProfileResult.IsSuccess)
                    {
                        columnProfiles.Add(columnProfileResult.Value);
                    }
                }

                foreach (var index in entity.Indexes.Where(static idx => idx.IsUnique))
                {
                    var orderedColumns = index.Columns
                        .OrderBy(static column => column.Ordinal)
                        .Select(static column => column.Column.Value)
                        .ToArray();

                    if (orderedColumns.Length == 0)
                    {
                        continue;
                    }

                    var candidateKey = ProfilingPlanBuilder.BuildUniqueKey(orderedColumns);
                    var hasDuplicates = tableResults.UniqueDuplicates.TryGetValue(candidateKey, out var duplicate) && duplicate;

                    if (orderedColumns.Length == 1)
                    {
                        var profileResult = UniqueCandidateProfile.Create(
                            SchemaName.Create(schema).Value,
                            TableName.Create(table).Value,
                            ColumnName.Create(orderedColumns[0]).Value,
                            hasDuplicates);

                        if (profileResult.IsSuccess)
                        {
                            uniqueProfiles.Add(profileResult.Value);
                        }
                    }
                    else
                    {
                        var columns = orderedColumns
                            .Select(name => ColumnName.Create(name).Value)
                            .ToImmutableArray();
                        var profileResult = CompositeUniqueCandidateProfile.Create(
                            SchemaName.Create(schema).Value,
                            TableName.Create(table).Value,
                            columns,
                            hasDuplicates);

                        if (profileResult.IsSuccess)
                        {
                            compositeProfiles.Add(profileResult.Value);
                        }
                    }
                }

                foreach (var attribute in entity.Attributes)
                {
                    if (!attribute.Reference.IsReference || attribute.Reference.TargetEntity is null)
                    {
                        continue;
                    }

                    var targetName = attribute.Reference.TargetEntity.Value;
                    if (!TryFindEntity(targetName, out var targetEntity))
                    {
                        continue;
                    }

                    var targetIdentifier = GetPreferredIdentifier(targetEntity);
                    if (targetIdentifier is null)
                    {
                        continue;
                    }

                    var foreignKeyKey = ProfilingPlanBuilder.BuildForeignKeyKey(
                        attribute.ColumnName.Value,
                        targetEntity.Schema.Value,
                        targetEntity.PhysicalName.Value,
                        targetIdentifier.ColumnName.Value);

                    var hasOrphans = tableResults.ForeignKeys.TryGetValue(foreignKeyKey, out var orphaned) && orphaned;

                    var referenceResult = ForeignKeyReference.Create(
                        SchemaName.Create(schema).Value,
                        TableName.Create(table).Value,
                        ColumnName.Create(attribute.ColumnName.Value).Value,
                        SchemaName.Create(targetEntity.Schema.Value).Value,
                        TableName.Create(targetEntity.PhysicalName.Value).Value,
                        ColumnName.Create(targetIdentifier.ColumnName.Value).Value,
                        attribute.Reference.HasDatabaseConstraint);

                    if (referenceResult.IsFailure)
                    {
                        continue;
                    }

                    var realityResult = ForeignKeyReality.Create(referenceResult.Value, hasOrphans, isNoCheck: false);
                    if (realityResult.IsSuccess)
                    {
                        foreignKeys.Add(realityResult.Value);
                    }
                }
            }

            var snapshotResult = ProfileSnapshot.Create(columnProfiles, uniqueProfiles, compositeProfiles, foreignKeys);
            if (snapshotResult.IsFailure)
            {
                return Result<ProfilingCaptureResult>.Failure(snapshotResult.Errors);
            }

            var capture = new ProfilingCaptureResult(snapshotResult.Value, warningBuilder.ToImmutable());
            return Result<ProfilingCaptureResult>.Success(capture);
        }
        catch (DbException ex)
        {
            return Result<ProfilingCaptureResult>.Failure(ValidationError.Create(
                "profile.sql.executionFailed",
                $"Failed to capture profiling snapshot: {ex.Message}"));
        }
    }

    private IReadOnlyCollection<(string Schema, string Table)> CollectTables()
    {
        var tables = new HashSet<(string Schema, string Table)>(TableKeyComparer.Instance);
        foreach (var entity in _model.Modules.SelectMany(static module => module.Entities))
        {
            tables.Add((entity.Schema.Value, entity.PhysicalName.Value));
        }

        return tables;
    }

    private async Task ProfileTableWithGateAsync(
        TableProfilingPlan plan,
        ConcurrentDictionary<(string Schema, string Table), TableProfilingResults> resultsLookup,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var results = await _queryExecutor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);
            resultsLookup[(plan.Schema, plan.Table)] = results;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryFindEntity(EntityName logicalName, out EntityModel entity)
    {
        foreach (var module in _model.Modules)
        {
            foreach (var candidate in module.Entities)
            {
                if (candidate.LogicalName.Equals(logicalName))
                {
                    entity = candidate;
                    return true;
                }
            }
        }

        entity = null!;
        return false;
    }

    private static AttributeModel? GetPreferredIdentifier(EntityModel entity)
    {
        foreach (var attribute in entity.Attributes)
        {
            if (attribute.IsIdentifier)
            {
                return attribute;
            }
        }

        return null;
    }

    private static bool IsSingleColumnUnique(EntityModel entity, string columnName)
    {
        foreach (var index in entity.Indexes)
        {
            if (!index.IsUnique)
            {
                continue;
            }

            if (index.Columns.Length == 1 && string.Equals(index.Columns[0].Column.Value, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static string BuildTableFilterClause(
        DbCommand command,
        IReadOnlyCollection<(string Schema, string Table)> tables,
        string schemaColumn,
        string tableColumn)
    {
        return TableMetadataLoader.BuildTableFilterClause(command, tables, schemaColumn, tableColumn);
    }

    private static IEnumerable<string> BuildTimeoutWarnings(TableProfilingPlan plan, TableProfilingResults results)
    {
        var identifier = string.Format(CultureInfo.InvariantCulture, "[{0}].[{1}]", plan.Schema, plan.Table);

        if (results.UsedNullCountFallback)
        {
            yield return string.Format(
                CultureInfo.InvariantCulture,
                "Null-count profiling timed out for table {0}; using conservative fallback values.",
                identifier);
        }

        if (results.UsedUniqueFallback)
        {
            yield return string.Format(
                CultureInfo.InvariantCulture,
                "Unique candidate profiling timed out for table {0}; duplicate detection results may be incomplete.",
                identifier);
        }

        if (results.UsedForeignKeyFallback)
        {
            yield return string.Format(
                CultureInfo.InvariantCulture,
                "Foreign key profiling timed out for table {0}; orphan detection results may be incomplete.",
                identifier);
        }
    }
}
