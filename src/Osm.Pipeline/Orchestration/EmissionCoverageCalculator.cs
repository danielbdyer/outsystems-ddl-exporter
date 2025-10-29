using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;
using Osm.Emission;
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
        var predicateSnapshots = new List<PredicateSnapshot>(entityMap.Count);
        var predicateCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, snapshot) in entityMap)
        {
            var predicates = EvaluatePredicates(snapshot);
            predicateSnapshots.Add(new PredicateSnapshot(
                snapshot.ModuleName,
                snapshot.Entity.Schema.Value,
                snapshot.Entity.PhysicalName.Value,
                predicates));
            IncrementPredicateCounts(predicateCounts, predicates);

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

                var expectedName = IndexNameGenerator.Generate(
                    snapshot.Entity,
                    analysis.ReferencedAttributes,
                    index.IsUnique,
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
                    fk.Columns.Any(column =>
                        string.Equals(column, attribute.ColumnName.Value, StringComparison.OrdinalIgnoreCase)) &&
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

        var orderedSnapshots = predicateSnapshots
            .OrderBy(static snapshot => snapshot.Module, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static snapshot => snapshot.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static snapshot => snapshot.Table, StringComparer.OrdinalIgnoreCase)
            .Select(snapshot =>
            {
                var predicates = snapshot.Predicates;
                if (!predicates.IsDefaultOrEmpty)
                {
                    predicates = predicates.Sort(StringComparer.Ordinal);
                }

                return PredicateCoverageEntry.Create(snapshot.Module, snapshot.Schema, snapshot.Table, predicates);
            })
            .ToArray();

        var orderedCounts = predicateCounts.Count == 0
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : predicateCounts
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        var predicateCoverage = new SsdtPredicateCoverage(orderedSnapshots, orderedCounts);

        return new EmissionCoverageResult(coverage, unsupported.ToImmutableArray(), predicateCoverage);
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

    private static ImmutableArray<string> EvaluatePredicates(DomainEntitySnapshot snapshot)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var entity = snapshot.Entity;

        if (entity.Metadata.Temporal.Type is TemporalTableType.SystemVersioned or TemporalTableType.HistoryTable)
        {
            builder.Add(SsdtPredicateNames.HasTemporalHistory);
        }

        if (!entity.Triggers.IsDefaultOrEmpty)
        {
            builder.Add(SsdtPredicateNames.HasTrigger);
        }

        if (entity.IsStatic)
        {
            builder.Add(SsdtPredicateNames.IsStaticEntity);
        }

        if (entity.IsExternal)
        {
            builder.Add(SsdtPredicateNames.IsExternalEntity);
        }

        if (!entity.IsActive)
        {
            builder.Add(SsdtPredicateNames.IsInactiveEntity);
        }

        if (HasInactiveColumns(entity))
        {
            builder.Add(SsdtPredicateNames.HasInactiveColumns);
        }

        if (entity.Attributes.Any(HasDefaultConstraint))
        {
            builder.Add(SsdtPredicateNames.HasDefaultConstraint);
        }

        if (entity.Attributes.Any(attribute => !attribute.OnDisk.CheckConstraints.IsDefaultOrEmpty))
        {
            builder.Add(SsdtPredicateNames.HasCheckConstraint);
        }

        if (HasExtendedProperties(entity))
        {
            builder.Add(SsdtPredicateNames.HasExtendedProperties);
        }

        if (entity.Indexes.Any(index => index.IsUnique))
        {
            builder.Add(SsdtPredicateNames.HasUniqueIndex);
        }

        if (entity.Indexes.Any(index => index.IsUnique && index.Columns.Count(column => !column.IsIncluded) > 1))
        {
            builder.Add(SsdtPredicateNames.HasCompositeUniqueIndex);
        }

        if (entity.Indexes.Any(index => !string.IsNullOrWhiteSpace(index.OnDisk.FilterDefinition)))
        {
            builder.Add(SsdtPredicateNames.HasFilteredIndex);
        }

        if (entity.Indexes.Any(index => index.Columns.Any(column => column.IsIncluded)))
        {
            builder.Add(SsdtPredicateNames.HasIncludedIndexColumns);
        }

        if (entity.Attributes.Any(attribute => attribute.Reference.IsReference))
        {
            builder.Add(SsdtPredicateNames.HasLogicalForeignKey);
        }

        if (entity.Attributes.Any(attribute => attribute.Reference.IsReference && !attribute.Reference.HasDatabaseConstraint))
        {
            builder.Add(SsdtPredicateNames.HasLogicalForeignKeyWithoutDbConstraint);
        }

        if (entity.Attributes.Any(attribute => attribute.Reference.IsReference && attribute.Reference.HasDatabaseConstraint))
        {
            builder.Add(SsdtPredicateNames.HasLogicalForeignKeyWithDbConstraint);
        }

        return builder.ToImmutable();
    }

    private static bool HasInactiveColumns(EntityModel entity)
        => entity.Attributes.Any(attribute => !attribute.IsActive || attribute.Reality.IsPresentButInactive);

    private static bool HasExtendedProperties(EntityModel entity)
    {
        if (!entity.Metadata.ExtendedProperties.IsDefaultOrEmpty && entity.Metadata.ExtendedProperties.Length > 0)
        {
            return true;
        }

        if (!entity.Metadata.Temporal.ExtendedProperties.IsDefaultOrEmpty && entity.Metadata.Temporal.ExtendedProperties.Length > 0)
        {
            return true;
        }

        if (entity.Attributes.Any(attribute => !attribute.Metadata.ExtendedProperties.IsDefaultOrEmpty && attribute.Metadata.ExtendedProperties.Length > 0))
        {
            return true;
        }

        return false;
    }

    private static bool HasDefaultConstraint(AttributeModel attribute)
    {
        if (!string.IsNullOrEmpty(attribute.DefaultValue))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(attribute.OnDisk.DefaultDefinition))
        {
            return true;
        }

        if (attribute.OnDisk.DefaultConstraint is { Definition: { Length: > 0 } })
        {
            return true;
        }

        return false;
    }

    private static void IncrementPredicateCounts(Dictionary<string, int> counts, ImmutableArray<string> predicates)
    {
        foreach (var predicate in predicates)
        {
            if (counts.TryGetValue(predicate, out var current))
            {
                counts[predicate] = current + 1;
            }
            else
            {
                counts[predicate] = 1;
            }
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

    private sealed record PredicateSnapshot(string Module, string Schema, string Table, ImmutableArray<string> Predicates);
}

public sealed record EmissionCoverageResult(
    SsdtCoverageSummary Summary,
    ImmutableArray<string> Unsupported,
    SsdtPredicateCoverage PredicateCoverage);
