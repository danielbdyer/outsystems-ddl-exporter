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

    public Dictionary<TableCoordinate, TableProfilingPlan> BuildPlans(
        IReadOnlyDictionary<(string Schema, string Table, string Column), ColumnMetadata> metadata,
        IReadOnlyDictionary<TableCoordinate, long> rowCounts)
    {
        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (rowCounts is null)
        {
            throw new ArgumentNullException(nameof(rowCounts));
        }

        var builders = new Dictionary<TableCoordinate, TableProfilingPlanAccumulator>(TableCoordinate.OrdinalIgnoreCaseComparer);

        foreach (var entity in _model.Modules.SelectMany(static module => module.Entities))
        {
            var schema = entity.Schema.Value;
            var table = entity.PhysicalName.Value;
            var coordinate = TableCoordinate.From(entity);

            if (!builders.TryGetValue(coordinate, out var accumulator))
            {
                accumulator = new TableProfilingPlanAccumulator(coordinate);
                builders[coordinate] = accumulator;
            }

            foreach (var attribute in entity.Attributes)
            {
                var columnName = attribute.ColumnName.Value;
                if (metadata.ContainsKey((schema, table, columnName)))
                {
                    accumulator.AddColumn(columnName);
                }

                if (attribute.Reference.IsReference && attribute.Reference.TargetEntity is not null)
                {
                    var targetName = attribute.Reference.TargetEntity.Value;
                    if (_entityLookup.TryGet(targetName, out var targetEntry) &&
                        targetEntry.PreferredIdentifier is { } targetIdentifier)
                    {
                        var targetEntity = targetEntry.Entity;
                        accumulator.AddForeignKey(
                            columnName,
                            targetEntity.Schema.Value,
                            targetEntity.PhysicalName.Value,
                            targetIdentifier.ColumnName.Value);
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

                accumulator.AddUniqueCandidate(orderedColumns);
            }
        }

        var plans = new Dictionary<TableCoordinate, TableProfilingPlan>(builders.Count, TableCoordinate.OrdinalIgnoreCaseComparer);
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
        private readonly HashSet<string> _uniqueKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<UniqueCandidatePlan> _uniqueCandidates = new();
        private readonly HashSet<string> _foreignKeyKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ForeignKeyPlan> _foreignKeys = new();

        public TableProfilingPlanAccumulator(TableCoordinate coordinate)
        {
            Coordinate = coordinate;
            Schema = coordinate.Schema.Value;
            Table = coordinate.Table.Value;
        }

        public TableCoordinate Coordinate { get; }

        public string Schema { get; }

        public string Table { get; }

        public void AddColumn(string column)
        {
            if (!string.IsNullOrWhiteSpace(column))
            {
                _columns.Add(column);
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
            var uniqueCandidates = _uniqueCandidates.ToImmutableArray();
            var foreignKeys = _foreignKeys.ToImmutableArray();
            return new TableProfilingPlan(Coordinate, rowCount, columns, uniqueCandidates, foreignKeys);
        }
    }
}
