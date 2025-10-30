using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    private readonly EntityProfilingLookup _entityLookup;
    private readonly ITableMetadataLoader _metadataLoader;
    private readonly IProfilingPlanBuilder _planBuilder;
    private readonly IProfilingQueryExecutor _queryExecutor;
    private readonly SqlMetadataLog? _metadataLog;

    public SqlDataProfiler(
        IDbConnectionFactory connectionFactory,
        OsmModel model,
        SqlProfilerOptions? options = null,
        SqlMetadataLog? metadataLog = null)
        : this(
            connectionFactory,
            model,
            options ?? SqlProfilerOptions.Default,
            null,
            null,
            null,
            metadataLog)
    {
    }

    internal SqlDataProfiler(
        IDbConnectionFactory connectionFactory,
        OsmModel model,
        SqlProfilerOptions options,
        ITableMetadataLoader? metadataLoader,
        IProfilingPlanBuilder? planBuilder,
        IProfilingQueryExecutor? queryExecutor,
        SqlMetadataLog? metadataLog = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _metadataLog = metadataLog;
        _entityLookup = EntityProfilingLookup.Create(_model, _options.NamingOverrides);
        _metadataLoader = metadataLoader ?? new TableMetadataLoader(_options, _metadataLog);
        _planBuilder = planBuilder ?? new ProfilingPlanBuilder(_model, _entityLookup);
        _queryExecutor = queryExecutor ?? new ProfilingQueryExecutor(_connectionFactory, _options, _metadataLog);
    }

    public async Task<Result<ProfileSnapshot>> CaptureAsync(CancellationToken cancellationToken = default)
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

            var maxConcurrency = Math.Max(1, _options.MaxConcurrentTableProfiles);
            if (maxConcurrency == 1 || plans.Count <= 1)
            {
                foreach (var plan in plans.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var results = await _queryExecutor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);
                    resultsLookup[(plan.Schema, plan.Table)] = results;
                }
            }
            else
            {
                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = maxConcurrency,
                };

                await Parallel.ForEachAsync(
                        plans.Values,
                        parallelOptions,
                        async (plan, ct) =>
                        {
                            var results = await _queryExecutor.ExecuteAsync(plan, ct).ConfigureAwait(false);
                            resultsLookup[(plan.Schema, plan.Table)] = results;
                        })
                    .ConfigureAwait(false);
            }

            var columnProfiles = new List<ColumnProfile>();
            var uniqueProfiles = new List<UniqueCandidateProfile>();
            var compositeProfiles = new List<CompositeUniqueCandidateProfile>();
            var foreignKeys = new List<ForeignKeyReality>();
            var coverageAnomalies = new List<ProfilingCoverageAnomaly>();

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
                        RecordColumnMetadataAnomaly(coverageAnomalies, entity, attribute);
                        continue;
                    }

                    var nullCount = tableResults.NullCounts.TryGetValue(columnName, out var value) ? value : 0L;
                    var hasNullStatus = tableResults.NullCountStatuses.TryGetValue(columnName, out var status) && status is not null;
                    var nullStatus = hasNullStatus ? status! : ProfilingProbeStatus.Unknown;

                    RecordNullProbeAnomaly(coverageAnomalies, entity, attribute, nullStatus, hasNullStatus);

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
                        nullCount,
                        nullStatus);

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
                    var hasUniqueStatus = tableResults.UniqueDuplicateStatuses.TryGetValue(candidateKey, out var uniqueProbe) && uniqueProbe is not null;
                    var uniqueStatus = hasUniqueStatus ? uniqueProbe! : ProfilingProbeStatus.Unknown;

                    if (orderedColumns.Length == 1)
                    {
                        var profileResult = UniqueCandidateProfile.Create(
                            SchemaName.Create(schema).Value,
                            TableName.Create(table).Value,
                            ColumnName.Create(orderedColumns[0]).Value,
                            hasDuplicates,
                            uniqueStatus);

                        RecordUniqueProbeAnomaly(
                            coverageAnomalies,
                            entity,
                            orderedColumns,
                            uniqueStatus,
                            hasUniqueStatus,
                            isComposite: false);

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

                        RecordUniqueProbeAnomaly(
                            coverageAnomalies,
                            entity,
                            orderedColumns,
                            uniqueStatus,
                            hasUniqueStatus,
                            isComposite: true);

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
                    if (!_entityLookup.TryGet(targetName, out var targetEntry))
                    {
                        continue;
                    }

                    if (targetEntry.PreferredIdentifier is not { } targetIdentifier)
                    {
                        continue;
                    }

                    var targetEntity = targetEntry.Entity;

                    var foreignKeyKey = ProfilingPlanBuilder.BuildForeignKeyKey(
                        attribute.ColumnName.Value,
                        targetEntity.Schema.Value,
                        targetEntity.PhysicalName.Value,
                        targetIdentifier.ColumnName.Value);

                    var hasOrphans = tableResults.ForeignKeys.TryGetValue(foreignKeyKey, out var orphaned) && orphaned;
                    var isNoCheck = tableResults.ForeignKeyIsNoCheck.TryGetValue(foreignKeyKey, out var noCheck) && noCheck;
                    var hasForeignKeyStatus = tableResults.ForeignKeyStatuses.TryGetValue(foreignKeyKey, out var fkStatus) && fkStatus is not null;
                    var hasNoCheckStatus = tableResults.ForeignKeyNoCheckStatuses.TryGetValue(foreignKeyKey, out var noCheckStatus) && noCheckStatus is not null;
                    var foreignKeyStatus = hasForeignKeyStatus
                        ? fkStatus!
                        : hasNoCheckStatus
                            ? noCheckStatus!
                            : ProfilingProbeStatus.Unknown;

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

                    var realityResult = ForeignKeyReality.Create(referenceResult.Value, hasOrphans, isNoCheck, foreignKeyStatus);
                    if (realityResult.IsSuccess)
                    {
                        foreignKeys.Add(realityResult.Value);
                    }

                    RecordForeignKeyProbeAnomaly(
                        coverageAnomalies,
                        entity,
                        attribute,
                        targetEntity,
                        targetIdentifier,
                        foreignKeyStatus,
                        hasForeignKeyStatus || hasNoCheckStatus);
                }
            }

            return ProfileSnapshot.Create(columnProfiles, uniqueProfiles, compositeProfiles, foreignKeys, coverageAnomalies);
        }
        catch (DbException ex)
        {
            return Result<ProfileSnapshot>.Failure(ValidationError.Create(
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

    private static void RecordColumnMetadataAnomaly(
        List<ProfilingCoverageAnomaly> anomalies,
        EntityModel entity,
        AttributeModel attribute)
    {
        var coordinateResult = ProfilingInsightCoordinate.Create(entity.Schema, entity.PhysicalName, attribute.ColumnName);
        if (coordinateResult.IsFailure)
        {
            return;
        }

        var message = $"Column metadata was not returned for {entity.Schema.Value}.{entity.PhysicalName.Value}.{attribute.ColumnName.Value}; the column was skipped.";
        const string hint = "Grant the profiler principal access to sys.columns (or the equivalent metadata views) and confirm the column exists in the target database.";

        anomalies.Add(ProfilingCoverageAnomaly.Create(
            ProfilingCoverageAnomalyType.ColumnMetadataMissing,
            message,
            hint,
            coordinateResult.Value,
            new[] { attribute.ColumnName.Value },
            ProfilingProbeOutcome.Unknown));
    }

    private static void RecordNullProbeAnomaly(
        List<ProfilingCoverageAnomaly> anomalies,
        EntityModel entity,
        AttributeModel attribute,
        ProfilingProbeStatus status,
        bool statusFound)
    {
        if (statusFound && status.Outcome == ProfilingProbeOutcome.Succeeded)
        {
            return;
        }

        var coordinateResult = ProfilingInsightCoordinate.Create(entity.Schema, entity.PhysicalName, attribute.ColumnName);
        if (coordinateResult.IsFailure)
        {
            return;
        }

        var outcome = statusFound ? status.Outcome : ProfilingProbeOutcome.Unknown;
        var qualifiedName = $"{entity.Schema.Value}.{entity.PhysicalName.Value}.{attribute.ColumnName.Value}";
        var message = outcome switch
        {
            ProfilingProbeOutcome.FallbackTimeout => $"Null-count probe timed out for {qualifiedName}; nullability evidence is incomplete.",
            ProfilingProbeOutcome.Cancelled => $"Null-count probe was cancelled for {qualifiedName}; nullability evidence is incomplete.",
            _ => $"Null-count probe never completed for {qualifiedName}; nullability evidence is unavailable."
        };
        const string hint = "Increase the SQL profiler timeout or verify the profiler principal can SELECT from the table.";

        anomalies.Add(ProfilingCoverageAnomaly.Create(
            ProfilingCoverageAnomalyType.NullCountProbeMissing,
            message,
            hint,
            coordinateResult.Value,
            new[] { attribute.ColumnName.Value },
            outcome));
    }

    private static void RecordUniqueProbeAnomaly(
        List<ProfilingCoverageAnomaly> anomalies,
        EntityModel entity,
        IReadOnlyList<string> columns,
        ProfilingProbeStatus status,
        bool statusFound,
        bool isComposite)
    {
        if (statusFound && status.Outcome == ProfilingProbeOutcome.Succeeded)
        {
            return;
        }

        ColumnName? coordinateColumn = null;
        if (!isComposite)
        {
            var columnResult = ColumnName.Create(columns[0]);
            if (columnResult.IsFailure)
            {
                return;
            }

            coordinateColumn = columnResult.Value;
        }

        var coordinateResult = ProfilingInsightCoordinate.Create(entity.Schema, entity.PhysicalName, coordinateColumn);
        if (coordinateResult.IsFailure)
        {
            return;
        }

        var outcome = statusFound ? status.Outcome : ProfilingProbeOutcome.Unknown;
        var columnList = string.Join(", ", columns);
        var scope = isComposite ? $"composite unique probe ({columnList})" : $"unique probe ({columnList})";
        var message = outcome switch
        {
            ProfilingProbeOutcome.FallbackTimeout => $"{scope} timed out for {entity.Schema.Value}.{entity.PhysicalName.Value}; duplicate evidence is incomplete.",
            ProfilingProbeOutcome.Cancelled => $"{scope} was cancelled for {entity.Schema.Value}.{entity.PhysicalName.Value}; duplicate evidence is incomplete.",
            _ => $"{scope} never completed for {entity.Schema.Value}.{entity.PhysicalName.Value}; duplicate evidence is unavailable."
        };
        const string hint = "Increase the SQL profiler timeout or ensure the profiler principal can read the underlying tables.";

        anomalies.Add(ProfilingCoverageAnomaly.Create(
            isComposite ? ProfilingCoverageAnomalyType.CompositeUniqueProbeMissing : ProfilingCoverageAnomalyType.UniqueProbeMissing,
            message,
            hint,
            coordinateResult.Value,
            columns,
            outcome));
    }

    private static void RecordForeignKeyProbeAnomaly(
        List<ProfilingCoverageAnomaly> anomalies,
        EntityModel sourceEntity,
        AttributeModel attribute,
        EntityModel targetEntity,
        AttributeModel targetIdentifier,
        ProfilingProbeStatus status,
        bool statusFound)
    {
        if (statusFound && status.Outcome == ProfilingProbeOutcome.Succeeded)
        {
            return;
        }

        var coordinateResult = ProfilingInsightCoordinate.Create(
            sourceEntity.Schema,
            sourceEntity.PhysicalName,
            attribute.ColumnName,
            targetEntity.Schema,
            targetEntity.PhysicalName,
            targetIdentifier.ColumnName);

        if (coordinateResult.IsFailure)
        {
            return;
        }

        var outcome = statusFound ? status.Outcome : ProfilingProbeOutcome.Unknown;
        var message = outcome switch
        {
            ProfilingProbeOutcome.FallbackTimeout => $"Foreign-key probe timed out for {sourceEntity.Schema.Value}.{sourceEntity.PhysicalName.Value}.{attribute.ColumnName.Value} → {targetEntity.Schema.Value}.{targetEntity.PhysicalName.Value}.{targetIdentifier.ColumnName.Value}; orphan detection was skipped.",
            ProfilingProbeOutcome.Cancelled => $"Foreign-key probe was cancelled for {sourceEntity.Schema.Value}.{sourceEntity.PhysicalName.Value}.{attribute.ColumnName.Value} → {targetEntity.Schema.Value}.{targetEntity.PhysicalName.Value}.{targetIdentifier.ColumnName.Value}; orphan detection was skipped.",
            _ => $"Foreign-key probe never completed for {sourceEntity.Schema.Value}.{sourceEntity.PhysicalName.Value}.{attribute.ColumnName.Value} → {targetEntity.Schema.Value}.{targetEntity.PhysicalName.Value}.{targetIdentifier.ColumnName.Value}; orphan detection is unavailable."
        };
        const string hint = "Increase the SQL profiler timeout or verify the profiler principal can join both tables to evaluate referential integrity.";

        anomalies.Add(ProfilingCoverageAnomaly.Create(
            ProfilingCoverageAnomalyType.ForeignKeyProbeMissing,
            message,
            hint,
            coordinateResult.Value,
            new[] { attribute.ColumnName.Value },
            outcome));
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
}
