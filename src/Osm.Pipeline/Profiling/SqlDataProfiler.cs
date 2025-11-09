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

internal interface ITableNameMappingProvider
{
    ImmutableArray<TableNameMapping> TableNameMappings { get; }
}

public sealed class SqlDataProfiler : IDataProfiler, ITableNameMappingProvider
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
            var plans = _planBuilder.BuildPlans(
                metadata,
                rowCounts,
                _options.AllowMissingTables,
                _options.TableNameMappings.IsDefaultOrEmpty ? null : _options.TableNameMappings.ToArray());

            // Log table name mappings when used
            if (_options.AllowMissingTables && !_options.TableNameMappings.IsDefaultOrEmpty && _metadataLog is not null)
            {
                foreach (var mapping in _options.TableNameMappings)
                {
                    _metadataLog.RecordRequest(
                        "profiling.table.mapped",
                        new
                        {
                            SourceSchema = mapping.SourceSchema,
                            SourceTable = mapping.SourceTable,
                            TargetSchema = mapping.TargetSchema,
                            TargetTable = mapping.TargetTable
                        });
                }
            }

            // Log skipped tables when in lenient mode
            if (_options.AllowMissingTables && _metadataLog is not null)
            {
                var modelTables = tables.ToHashSet(TableKeyComparer.Instance);
                var profiledTables = plans.Keys.ToHashSet(TableKeyComparer.Instance);
                var skippedTables = modelTables.Except(profiledTables).ToList();

                if (skippedTables.Count > 0)
                {
                    foreach (var (schema, table) in skippedTables.OrderBy(t => t.Schema).ThenBy(t => t.Table))
                    {
                        _metadataLog.RecordRequest(
                            "profiling.table.skipped",
                            new { Schema = schema, Table = table, Reason = "Table does not exist in target environment" });
                    }
                }
            }

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
                    var nullStatus = tableResults.NullCountStatuses.TryGetValue(columnName, out var status)
                        ? status
                        : ProfilingProbeStatus.Unknown;
                    var nullRowSample = tableResults.NullRowSamples.TryGetValue(columnName, out var sample)
                        ? sample
                        : null;

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
                        nullStatus,
                        nullRowSample);

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
                    var uniqueStatus = tableResults.UniqueDuplicateStatuses.TryGetValue(candidateKey, out var uniqueProbe)
                        ? uniqueProbe
                        : ProfilingProbeStatus.Unknown;

                    if (orderedColumns.Length == 1)
                    {
                        var profileResult = UniqueCandidateProfile.Create(
                            SchemaName.Create(schema).Value,
                            TableName.Create(table).Value,
                            ColumnName.Create(orderedColumns[0]).Value,
                            hasDuplicates,
                            uniqueStatus);

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

                    var orphanCount = tableResults.ForeignKeyOrphanCounts.TryGetValue(foreignKeyKey, out var count)
                        ? count
                        : 0L;
                    var hasOrphans = orphanCount > 0;
                    var isNoCheck = tableResults.ForeignKeyIsNoCheck.TryGetValue(foreignKeyKey, out var noCheck) && noCheck;
                    var foreignKeyStatus = tableResults.ForeignKeyStatuses.TryGetValue(foreignKeyKey, out var fkStatus)
                        ? fkStatus
                        : tableResults.ForeignKeyNoCheckStatuses.TryGetValue(foreignKeyKey, out var noCheckStatus)
                            ? noCheckStatus
                            : ProfilingProbeStatus.Unknown;
                    var orphanSample = tableResults.ForeignKeyOrphanSamples.TryGetValue(foreignKeyKey, out var sample)
                        ? sample
                        : null;

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

                    var realityResult = ForeignKeyReality.Create(
                        referenceResult.Value,
                        hasOrphans,
                        orphanCount,
                        isNoCheck,
                        foreignKeyStatus,
                        orphanSample);
                    if (realityResult.IsSuccess)
                    {
                        foreignKeys.Add(realityResult.Value);
                    }
                }
            }

            return ProfileSnapshot.Create(columnProfiles, uniqueProfiles, compositeProfiles, foreignKeys);
        }
        catch (DbException ex)
        {
            return Result<ProfileSnapshot>.Failure(ValidationError.Create(
                "profile.sql.executionFailed",
                $"Failed to capture profiling snapshot: {ex.Message}"));
        }
    }

    ImmutableArray<TableNameMapping> ITableNameMappingProvider.TableNameMappings => _options.TableNameMappings;

    private IReadOnlyCollection<(string Schema, string Table)> CollectTables()
    {
        var tables = new HashSet<(string Schema, string Table)>(TableKeyComparer.Instance);
        foreach (var entity in _model.Modules.SelectMany(static module => module.Entities))
        {
            tables.Add((entity.Schema.Value, entity.PhysicalName.Value));
        }

        if (_options.AllowMissingTables && !_options.TableNameMappings.IsDefaultOrEmpty)
        {
            foreach (var mapping in _options.TableNameMappings)
            {
                tables.Add((mapping.TargetSchema, mapping.TargetTable));
            }
        }

        return tables;
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
