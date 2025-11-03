using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;

namespace Osm.Pipeline.Profiling;

internal sealed class ProfilingPlanBuilder : IProfilingPlanBuilder
{
    private readonly OsmModel _model;
    private readonly EntityProfilingLookup _entityLookup;

    public ProfilingPlanBuilder(OsmModel model)
        : this(model, NamingOverrideOptions.Empty)
    {
    }

    public ProfilingPlanBuilder(OsmModel model, NamingOverrideOptions namingOverrides)
        : this(model, EntityProfilingLookup.Create(model, namingOverrides))
    {
    }

    internal ProfilingPlanBuilder(OsmModel model, EntityProfilingLookup entityLookup)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _entityLookup = entityLookup ?? throw new ArgumentNullException(nameof(entityLookup));
    }

    public Dictionary<(string Schema, string Table), TableProfilingPlan> BuildPlans(
        IReadOnlyDictionary<(string Schema, string Table, string Column), ColumnMetadata> metadata,
        IReadOnlyDictionary<(string Schema, string Table), long> rowCounts,
        bool allowMissingTables = false)
    {
        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (rowCounts is null)
        {
            throw new ArgumentNullException(nameof(rowCounts));
        }

        var builders = new Dictionary<(string Schema, string Table), TableProfilingPlanAccumulator>(TableKeyComparer.Instance);

        foreach (var entity in _model.Modules.SelectMany(static module => module.Entities))
        {
            var schema = entity.Schema.Value;
            var table = entity.PhysicalName.Value;
            var key = (schema, table);

            // In lenient mode, check if the table has any columns in metadata before processing
            if (allowMissingTables)
            {
                var hasAnyColumn = entity.Attributes.Any(attr =>
                    metadata.ContainsKey((schema, table, attr.ColumnName.Value)));

                if (!hasAnyColumn)
                {
                    // Skip this table entirely - it doesn't exist in this environment
                    continue;
                }
            }

            if (!builders.TryGetValue(key, out var accumulator))
            {
                accumulator = new TableProfilingPlanAccumulator(schema, table);
                builders[key] = accumulator;
            }

            foreach (var attribute in entity.Attributes)
            {
                var columnName = attribute.ColumnName.Value;
                var metadataKey = (schema, table, columnName);
                if (metadata.ContainsKey(metadataKey))
                {
                    accumulator.AddColumn(columnName);

                    // Track primary key columns from metadata
                    if (metadata.TryGetValue(metadataKey, out var columnMetadata) && columnMetadata.IsPrimaryKey)
                    {
                        accumulator.AddPrimaryKeyColumn(columnName);
                    }
                }

                if (attribute.Reference.IsReference && attribute.Reference.TargetEntity is not null)
                {
                    var targetName = attribute.Reference.TargetEntity.Value;
                    if (_entityLookup.TryGet(targetName, out var targetEntry) &&
                        targetEntry.PreferredIdentifier is { } targetIdentifier)
                    {
                        var targetEntity = targetEntry.Entity;

                        // In lenient mode, only add FK if target table exists in metadata
                        // In strict mode, always add FK (including to system tables like ossys_User)
                        var targetTableKey = (targetEntity.Schema.Value, targetEntity.PhysicalName.Value);
                        var targetHasColumns = !allowMissingTables ||
                            metadata.Keys.Any(k => k.Schema == targetTableKey.Schema && k.Table == targetTableKey.Table);

                        if (targetHasColumns)
                        {
                            accumulator.AddForeignKey(
                                columnName,
                                targetEntity.Schema.Value,
                                targetEntity.PhysicalName.Value,
                                targetIdentifier.ColumnName.Value);
                        }
                    }
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

                // In lenient mode, only add unique constraint if all columns exist in metadata
                if (allowMissingTables)
                {
                    var allColumnsExist = orderedColumns.All(col =>
                        metadata.ContainsKey((schema, table, col)));

                    if (!allColumnsExist)
                    {
                        continue;
                    }
                }

                accumulator.AddUniqueCandidate(orderedColumns);
            }
        }

        var plans = new Dictionary<(string Schema, string Table), TableProfilingPlan>(builders.Count, TableKeyComparer.Instance);
        foreach (var kvp in builders)
        {
            var key = kvp.Key;
            rowCounts.TryGetValue(key, out var rowCount);
            plans[key] = kvp.Value.Build(rowCount);
        }

        return plans;
    }

    internal static string BuildUniqueKey(IEnumerable<string> columns)
    {
        return string.Join("|", columns.Select(static column => column.ToLowerInvariant()));
    }

    internal static string BuildForeignKeyKey(string column, string targetSchema, string targetTable, string targetColumn)
    {
        return string.Join("|", new[] { column, targetSchema, targetTable, targetColumn }.Select(static value => value.ToLowerInvariant()));
    }

    private sealed class TableProfilingPlanAccumulator
    {
        private readonly HashSet<string> _columns = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _primaryKeyColumns = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _uniqueKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<UniqueCandidatePlan> _uniqueCandidates = new();
        private readonly HashSet<string> _foreignKeyKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ForeignKeyPlan> _foreignKeys = new();

        public TableProfilingPlanAccumulator(string schema, string table)
        {
            Schema = schema;
            Table = table;
        }

        public string Schema { get; }

        public string Table { get; }

        public void AddColumn(string column)
        {
            if (!string.IsNullOrWhiteSpace(column))
            {
                _columns.Add(column);
            }
        }

        public void AddPrimaryKeyColumn(string column)
        {
            if (!string.IsNullOrWhiteSpace(column))
            {
                _primaryKeyColumns.Add(column);
            }
        }

        public void AddUniqueCandidate(IReadOnlyList<string> columns)
        {
            if (columns is null || columns.Count == 0)
            {
                return;
            }

            var normalized = columns.Select(static c => c).ToImmutableArray();
            var key = BuildUniqueKey(normalized);
            if (_uniqueKeys.Add(key))
            {
                _uniqueCandidates.Add(new UniqueCandidatePlan(key, normalized));
            }
        }

        public void AddForeignKey(string column, string targetSchema, string targetTable, string targetColumn)
        {
            if (string.IsNullOrWhiteSpace(column) ||
                string.IsNullOrWhiteSpace(targetSchema) ||
                string.IsNullOrWhiteSpace(targetTable) ||
                string.IsNullOrWhiteSpace(targetColumn))
            {
                return;
            }

            var key = BuildForeignKeyKey(column, targetSchema, targetTable, targetColumn);
            if (_foreignKeyKeys.Add(key))
            {
                _foreignKeys.Add(new ForeignKeyPlan(key, column, targetSchema, targetTable, targetColumn));
            }
        }

        public TableProfilingPlan Build(long rowCount)
        {
            var columns = _columns
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
            var primaryKeyColumns = _primaryKeyColumns
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
            var uniqueCandidates = _uniqueCandidates.ToImmutableArray();
            var foreignKeys = _foreignKeys.ToImmutableArray();
            return new TableProfilingPlan(Schema, Table, rowCount, columns, uniqueCandidates, foreignKeys, primaryKeyColumns);
        }
    }
}
