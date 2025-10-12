using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public static class EmissionCoverageCalculator
{
    public static EmissionCoverageResult Compute(
        OsmModel model,
        ImmutableArray<EntityModel> supplementalEntities,
        PolicyDecisionSet decisions,
        SmoModel smoModel,
        SmoBuildOptions options)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (decisions is null)
        {
            throw new ArgumentNullException(nameof(decisions));
        }

        if (smoModel is null)
        {
            throw new ArgumentNullException(nameof(smoModel));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var entityMap = BuildEntityMap(model, supplementalEntities);
        var totalTables = entityMap.Count;
        var totalColumns = entityMap.Values.Sum(snapshot => snapshot.EmittableAttributes.Length);

        var emittedTables = smoModel.Tables.Length;
        var emittedColumns = smoModel.Tables.Sum(table => table.Columns.Length);
        var emittedPrimaryKeys = smoModel.Tables.Count(table => table.Indexes.Any(index => index.IsPrimaryKey));
        var emittedNonPrimaryIndexes = smoModel.Tables.Sum(table => table.Indexes.Count(index => !index.IsPrimaryKey));
        var emittedForeignKeys = smoModel.Tables.Sum(table => table.ForeignKeys.Length);
        var emittedConstraints = emittedPrimaryKeys + emittedNonPrimaryIndexes + emittedForeignKeys;

        var tableLookup = smoModel.Tables.ToDictionary(
            table => SchemaTableKey(table.Schema, table.Name),
            table => table,
            StringComparer.OrdinalIgnoreCase);

        var expectedPrimaryKeys = 0;
        var expectedNonPrimaryIndexes = 0;
        var expectedForeignKeys = 0;

        var unsupported = new List<string>();
        var unsupportedSeen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, snapshot) in entityMap)
        {
            expectedPrimaryKeys++;

            if (!snapshot.EmittableAttributes.Any(attribute => attribute.IsIdentifier))
            {
                AddUnsupported(unsupported, unsupportedSeen, $"Primary key skipped for {key}: no emittable identifier columns.");
            }

            if (!tableLookup.TryGetValue(key, out var smoTable))
            {
                AddUnsupported(unsupported, unsupportedSeen, $"Table {key} missing from emission output.");
                continue;
            }

            if (!smoTable.Indexes.Any(index => index.IsPrimaryKey))
            {
                AddUnsupported(unsupported, unsupportedSeen, $"Primary key missing for table {key}.");
            }

            var expectedColumnCount = snapshot.EmittableAttributes.Length;
            if (smoTable.Columns.Length < expectedColumnCount)
            {
                AddUnsupported(
                    unsupported,
                    unsupportedSeen,
                    $"Table {key} emitted {smoTable.Columns.Length} of {expectedColumnCount} expected columns.");
            }

            var emittedIndexNames = new HashSet<string>(
                smoTable.Indexes.Where(index => !index.IsPrimaryKey).Select(index => index.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var index in snapshot.Entity.Indexes)
            {
                if (index.IsPrimary)
                {
                    continue;
                }

                if (!options.IncludePlatformAutoIndexes && index.IsPlatformAuto && !index.IsUnique)
                {
                    continue;
                }

                expectedNonPrimaryIndexes++;

                var analysis = AnalyzeIndex(index, snapshot);
                if (!analysis.Succeeded)
                {
                    AddUnsupported(
                        unsupported,
                        unsupportedSeen,
                        $"Index {index.Name.Value} on {key} skipped: {analysis.Reason}.");
                    continue;
                }

                var expectedName = ConstraintNameNormalizer.Normalize(
                    index.Name.Value,
                    snapshot.Entity,
                    analysis.ReferencedAttributes,
                    index.IsUnique ? ConstraintNameKind.UniqueIndex : ConstraintNameKind.NonUniqueIndex,
                    options.Format);

                if (!emittedIndexNames.Contains(expectedName))
                {
                    AddUnsupported(
                        unsupported,
                        unsupportedSeen,
                        $"Index {expectedName} on {key} missing from emission output.");
                }
            }

            foreach (var attribute in snapshot.EmittableAttributes)
            {
                if (!attribute.Reference.IsReference)
                {
                    continue;
                }

                var coordinate = new ColumnCoordinate(snapshot.Entity.Schema, snapshot.Entity.PhysicalName, attribute.ColumnName);
                if (!decisions.ForeignKeys.TryGetValue(coordinate, out var decision) || !decision.CreateConstraint)
                {
                    continue;
                }

                expectedForeignKeys++;

                var hasForeignKey = smoTable.ForeignKeys.Any(fk =>
                    string.Equals(fk.Column, attribute.LogicalName.Value, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        fk.ReferencedTable,
                        attribute.Reference.TargetPhysicalName?.Value ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase));

                if (!hasForeignKey)
                {
                    AddUnsupported(
                        unsupported,
                        unsupportedSeen,
                        $"Foreign key on {key}.{attribute.LogicalName.Value} missing from emission output.");
                }
            }
        }

        var totalConstraints = expectedPrimaryKeys + expectedNonPrimaryIndexes + expectedForeignKeys;
        var coverage = new SsdtCoverageSummary(
            CoverageBreakdown.Create(emittedTables, totalTables),
            CoverageBreakdown.Create(emittedColumns, totalColumns),
            CoverageBreakdown.Create(emittedConstraints, totalConstraints));

        return new EmissionCoverageResult(coverage, unsupported.ToImmutableArray());
    }

    private static Dictionary<string, DomainEntitySnapshot> BuildEntityMap(
        OsmModel model,
        ImmutableArray<EntityModel> supplementalEntities)
    {
        var map = new Dictionary<string, DomainEntitySnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in model.Modules)
        {
            foreach (var entity in module.Entities)
            {
                var key = SchemaTableKey(entity.Schema.Value, entity.PhysicalName.Value);
                map[key] = DomainEntitySnapshot.Create(module.Name.Value, entity);
            }
        }

        if (!supplementalEntities.IsDefaultOrEmpty)
        {
            var supplementalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in supplementalEntities)
            {
                if (entity is null)
                {
                    continue;
                }

                var key = SchemaTableKey(entity.Schema.Value, entity.PhysicalName.Value);
                if (map.ContainsKey(key) || !supplementalKeys.Add(key))
                {
                    continue;
                }

                map[key] = DomainEntitySnapshot.Create(entity.Module.Value, entity);
            }
        }

        return map;
    }

    private static IndexAnalysisResult AnalyzeIndex(IndexModel index, DomainEntitySnapshot snapshot)
    {
        if (index.Columns.IsDefaultOrEmpty)
        {
            return IndexAnalysisResult.Failure("index does not define any columns");
        }

        var referencedBuilder = ImmutableArray.CreateBuilder<AttributeModel>(index.Columns.Length);
        var hasKeyColumn = false;

        var orderedColumns = index.Columns
            .OrderBy(column => column.Ordinal)
            .ToArray();

        foreach (var column in orderedColumns)
        {
            if (!snapshot.AttributeLookup.TryGetValue(column.Column.Value, out var attribute))
            {
                return IndexAnalysisResult.Failure($"referenced column '{column.Column.Value}' is not emittable");
            }

            if (!column.IsIncluded)
            {
                hasKeyColumn = true;
                referencedBuilder.Add(attribute);
            }
        }

        if (!hasKeyColumn)
        {
            return IndexAnalysisResult.Failure("index only defines included columns");
        }

        return IndexAnalysisResult.Success(referencedBuilder.ToImmutable());
    }

    private static string SchemaTableKey(string schema, string table)
        => $"{schema}.{table}";

    private static void AddUnsupported(List<string> messages, HashSet<string> seen, string message)
    {
        if (seen.Add(message))
        {
            messages.Add(message);
        }
    }

    private sealed record DomainEntitySnapshot(
        string ModuleName,
        EntityModel Entity,
        ImmutableArray<AttributeModel> EmittableAttributes,
        IReadOnlyDictionary<string, AttributeModel> AttributeLookup)
    {
        public static DomainEntitySnapshot Create(string moduleName, EntityModel entity)
        {
            var emittable = entity.Attributes
                .Where(static attribute => attribute is not null && attribute.IsActive && !attribute.Reality.IsPresentButInactive)
                .ToImmutableArray();

            var lookup = emittable.ToDictionary(
                attribute => attribute.ColumnName.Value,
                attribute => attribute,
                StringComparer.OrdinalIgnoreCase);

            return new DomainEntitySnapshot(moduleName, entity, emittable, lookup);
        }
    }

    private sealed record IndexAnalysisResult(bool Succeeded, ImmutableArray<AttributeModel> ReferencedAttributes, string Reason)
    {
        public static IndexAnalysisResult Success(ImmutableArray<AttributeModel> referencedAttributes)
            => new(true, referencedAttributes, string.Empty);

        public static IndexAnalysisResult Failure(string reason)
            => new(false, ImmutableArray<AttributeModel>.Empty, reason);
    }
}

public sealed record EmissionCoverageResult(SsdtCoverageSummary Summary, ImmutableArray<string> Unsupported);
