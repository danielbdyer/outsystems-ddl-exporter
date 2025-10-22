using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;

namespace Osm.Validation.Tightening;

public static class ModelPredicateEvaluator
{
    public static PredicateTelemetry Evaluate(OsmModel model, PolicyDecisionSet decisions)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (decisions is null)
        {
            throw new ArgumentNullException(nameof(decisions));
        }

        var tableBuilder = ImmutableArray.CreateBuilder<TablePredicateTelemetry>();
        var columnBuilder = ImmutableArray.CreateBuilder<ColumnPredicateTelemetry>();
        var indexBuilder = ImmutableArray.CreateBuilder<IndexPredicateTelemetry>();
        var sequenceBuilder = ImmutableArray.CreateBuilder<SequencePredicateTelemetry>();
        var extendedBuilder = ImmutableArray.CreateBuilder<ExtendedPropertyPredicateTelemetry>();

        if (!model.ExtendedProperties.IsDefaultOrEmpty && model.ExtendedProperties.Length > 0)
        {
            extendedBuilder.Add(new ExtendedPropertyPredicateTelemetry(
                Scope: "Model",
                Module: null,
                Schema: null,
                Table: null,
                Column: null,
                Predicates: ImmutableArray.Create("HasExtendedProperties")));
        }

        foreach (var module in model.Modules)
        {
            if (!module.ExtendedProperties.IsDefaultOrEmpty && module.ExtendedProperties.Length > 0)
            {
                extendedBuilder.Add(new ExtendedPropertyPredicateTelemetry(
                    Scope: "Module",
                    Module: module.Name.Value,
                    Schema: null,
                    Table: null,
                    Column: null,
                    Predicates: ImmutableArray.Create("HasExtendedProperties")));
            }

            foreach (var entity in module.Entities)
            {
                var tablePredicates = CollectTablePredicates(entity);
                if (tablePredicates.Count > 0)
                {
                    tablePredicates.Sort(StringComparer.Ordinal);
                    tableBuilder.Add(new TablePredicateTelemetry(
                        module.Name.Value,
                        entity.LogicalName.Value,
                        entity.Schema.Value,
                        entity.PhysicalName.Value,
                        tablePredicates.ToImmutableArray()));
                }

                if (!entity.Metadata.ExtendedProperties.IsDefaultOrEmpty && entity.Metadata.ExtendedProperties.Length > 0)
                {
                    extendedBuilder.Add(new ExtendedPropertyPredicateTelemetry(
                        Scope: "Entity",
                        Module: module.Name.Value,
                        Schema: entity.Schema.Value,
                        Table: entity.PhysicalName.Value,
                        Column: null,
                        Predicates: ImmutableArray.Create("HasExtendedProperties")));
                }

                if (!entity.Metadata.Temporal.ExtendedProperties.IsDefaultOrEmpty &&
                    entity.Metadata.Temporal.ExtendedProperties.Length > 0)
                {
                    extendedBuilder.Add(new ExtendedPropertyPredicateTelemetry(
                        Scope: "Temporal",
                        Module: module.Name.Value,
                        Schema: entity.Metadata.Temporal.HistorySchema?.Value ?? entity.Schema.Value,
                        Table: entity.Metadata.Temporal.HistoryTable?.Value ?? entity.PhysicalName.Value,
                        Column: null,
                        Predicates: ImmutableArray.Create("HasExtendedProperties")));
                }

                foreach (var attribute in entity.Attributes)
                {
                    var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);
                    var columnPredicates = CollectColumnPredicates(attribute, coordinate, decisions);
                    if (columnPredicates.Count > 0)
                    {
                        columnPredicates.Sort(StringComparer.Ordinal);
                        columnBuilder.Add(new ColumnPredicateTelemetry(
                            module.Name.Value,
                            entity.LogicalName.Value,
                            entity.Schema.Value,
                            entity.PhysicalName.Value,
                            attribute.ColumnName.Value,
                            columnPredicates.ToImmutableArray()));
                    }

                    if (!attribute.Metadata.ExtendedProperties.IsDefaultOrEmpty && attribute.Metadata.ExtendedProperties.Length > 0)
                    {
                        extendedBuilder.Add(new ExtendedPropertyPredicateTelemetry(
                            Scope: "Attribute",
                            Module: module.Name.Value,
                            Schema: entity.Schema.Value,
                            Table: entity.PhysicalName.Value,
                            Column: attribute.ColumnName.Value,
                            Predicates: ImmutableArray.Create("HasExtendedProperties")));
                    }
                }

                foreach (var index in entity.Indexes)
                {
                    var indexCoordinate = new IndexCoordinate(entity.Schema, entity.PhysicalName, index.Name);
                    var indexPredicates = CollectIndexPredicates(index, indexCoordinate, decisions);
                    if (indexPredicates.Count > 0)
                    {
                        indexPredicates.Sort(StringComparer.Ordinal);
                        indexBuilder.Add(new IndexPredicateTelemetry(
                            module.Name.Value,
                            entity.LogicalName.Value,
                            entity.Schema.Value,
                            entity.PhysicalName.Value,
                            index.Name.Value,
                            indexPredicates.ToImmutableArray()));
                    }

                    if (!index.ExtendedProperties.IsDefaultOrEmpty && index.ExtendedProperties.Length > 0)
                    {
                        extendedBuilder.Add(new ExtendedPropertyPredicateTelemetry(
                            Scope: "Index",
                            Module: module.Name.Value,
                            Schema: entity.Schema.Value,
                            Table: entity.PhysicalName.Value,
                            Column: index.Name.Value,
                            Predicates: ImmutableArray.Create("HasExtendedProperties")));
                    }
                }
            }
        }

        foreach (var sequence in model.Sequences)
        {
            var sequencePredicates = CollectSequencePredicates(sequence);
            if (sequencePredicates.Count > 0)
            {
                sequencePredicates.Sort(StringComparer.Ordinal);
                sequenceBuilder.Add(new SequencePredicateTelemetry(
                    sequence.Schema.Value,
                    sequence.Name.Value,
                    sequencePredicates.ToImmutableArray()));
            }

            if (!sequence.ExtendedProperties.IsDefaultOrEmpty && sequence.ExtendedProperties.Length > 0)
            {
                extendedBuilder.Add(new ExtendedPropertyPredicateTelemetry(
                    Scope: "Sequence",
                    Module: null,
                    Schema: sequence.Schema.Value,
                    Table: sequence.Name.Value,
                    Column: null,
                    Predicates: ImmutableArray.Create("HasExtendedProperties")));
            }
        }

        return new PredicateTelemetry(
            tableBuilder.ToImmutable(),
            columnBuilder.ToImmutable(),
            indexBuilder.ToImmutable(),
            sequenceBuilder.ToImmutable(),
            extendedBuilder.ToImmutable());
    }

    private static List<string> CollectTablePredicates(EntityModel entity)
    {
        var predicates = new List<string>();

        if (entity.IsStatic)
        {
            predicates.Add("IsStatic");
        }

        if (entity.IsExternal)
        {
            predicates.Add("IsExternal");
        }

        if (!entity.IsActive)
        {
            predicates.Add("IsInactive");
        }

        if (!entity.Triggers.IsDefaultOrEmpty && entity.Triggers.Length > 0)
        {
            predicates.Add("HasTriggers");
        }

        if (!entity.Relationships.IsDefaultOrEmpty && entity.Relationships.Length > 0)
        {
            predicates.Add("HasRelationships");
            if (entity.Relationships.Any(static relationship => relationship.HasDatabaseConstraint))
            {
                predicates.Add("HasDbBackedRelationships");
            }
        }

        if (entity.Attributes.Any(static attribute => attribute.Reference.IsReference))
        {
            predicates.Add("HasLogicalReferences");
        }

        if (entity.Attributes.Any(static attribute => attribute.Reality.IsPresentButInactive))
        {
            predicates.Add("HasInactivityDrift");
        }

        if (entity.Attributes.Any(static attribute => HasDefaultConstraint(attribute)))
        {
            predicates.Add("HasDefaultConstraints");
        }

        if (entity.Metadata.Temporal.Type == TemporalTableType.SystemVersioned)
        {
            predicates.Add("HasTemporalHistory");
            if (entity.Metadata.Temporal.RetentionPolicy.Kind != TemporalRetentionKind.None)
            {
                predicates.Add("HasTemporalRetention");
            }
        }

        return predicates;
    }

    private static List<string> CollectColumnPredicates(
        AttributeModel attribute,
        ColumnCoordinate coordinate,
        PolicyDecisionSet decisions)
    {
        var predicates = new List<string>();

        if (attribute.IsIdentifier)
        {
            predicates.Add("IsIdentifier");
        }

        if (attribute.IsMandatory)
        {
            predicates.Add("IsMandatory");
        }

        if (attribute.IsAutoNumber)
        {
            predicates.Add("IsAutoNumber");
        }

        if (attribute.Reference.IsReference)
        {
            predicates.Add("IsReference");
            if (attribute.Reference.HasDatabaseConstraint)
            {
                predicates.Add("ReferenceHasDbConstraint");
            }
            else
            {
                predicates.Add("ReferenceMissingDbConstraint");
            }
        }

        if (!string.IsNullOrWhiteSpace(attribute.DefaultValue) || HasDefaultConstraint(attribute))
        {
            predicates.Add("HasDefaultConstraint");
        }

        if (!string.IsNullOrWhiteSpace(attribute.OnDisk.SqlType))
        {
            predicates.Add("HasPhysicalType");
        }

        if (attribute.OnDisk.IsNullable is not null)
        {
            predicates.Add(attribute.OnDisk.IsNullable.Value ? "PhysicalNullable" : "PhysicalNotNull");
        }

        if (attribute.OnDisk.IsIdentity == true)
        {
            predicates.Add("HasIdentity");
        }

        if (attribute.OnDisk.IsComputed == true)
        {
            predicates.Add("IsComputed");
        }

        if (!string.IsNullOrWhiteSpace(attribute.OnDisk.ComputedDefinition))
        {
            predicates.Add("HasComputedDefinition");
        }

        if (!string.IsNullOrWhiteSpace(attribute.OnDisk.DefaultDefinition))
        {
            predicates.Add("HasOnDiskDefault");
        }

        if (attribute.Reality.IsNullableInDatabase is not null)
        {
            predicates.Add("HasRealityNullability");
        }

        if (attribute.Reality.HasNulls is not null)
        {
            predicates.Add("HasProfilerNulls");
        }

        if (attribute.Reality.HasDuplicates is not null)
        {
            predicates.Add("HasProfilerDuplicates");
        }

        if (attribute.Reality.HasOrphans is not null)
        {
            predicates.Add("HasProfilerOrphans");
        }

        if (attribute.Reality.IsPresentButInactive)
        {
            predicates.Add("PresentButInactive");
        }

        if (decisions.Nullability.TryGetValue(coordinate, out var nullability))
        {
            if (nullability.MakeNotNull)
            {
                predicates.Add("DecisionMakeNotNull");
            }

            if (nullability.RequiresRemediation)
            {
                predicates.Add("DecisionRequiresRemediation");
            }
        }

        if (decisions.ForeignKeys.TryGetValue(coordinate, out var foreignKey))
        {
            if (foreignKey.CreateConstraint)
            {
                predicates.Add("DecisionCreatesForeignKey");
            }
            else
            {
                predicates.Add("DecisionSkipsForeignKey");
            }
        }

        return predicates;
    }

    private static List<string> CollectIndexPredicates(
        IndexModel index,
        IndexCoordinate coordinate,
        PolicyDecisionSet decisions)
    {
        var predicates = new List<string>();

        if (index.IsPrimary)
        {
            predicates.Add("IsPrimary");
        }

        if (index.IsUnique)
        {
            predicates.Add("IsUnique");
        }

        if (index.Columns.Length > 1)
        {
            predicates.Add("IsComposite");
        }

        if (index.Columns.Any(static column => column.IsIncluded))
        {
            predicates.Add("HasIncludedColumns");
        }

        if (index.Columns.Any(static column => column.Direction == IndexColumnDirection.Descending))
        {
            predicates.Add("HasDescendingColumns");
        }

        if (!string.IsNullOrWhiteSpace(index.OnDisk.FilterDefinition))
        {
            predicates.Add("HasFilter");
        }

        if (index.OnDisk.DataSpace is not null)
        {
            predicates.Add("HasDataSpace");
        }

        if (!index.OnDisk.PartitionColumns.IsDefaultOrEmpty && index.OnDisk.PartitionColumns.Length > 0)
        {
            predicates.Add("HasPartitionScheme");
        }

        if (!index.OnDisk.DataCompression.IsDefaultOrEmpty && index.OnDisk.DataCompression.Length > 0)
        {
            predicates.Add("HasCompression");
        }

        if (decisions.UniqueIndexes.TryGetValue(coordinate, out var uniqueDecision))
        {
            if (uniqueDecision.EnforceUnique)
            {
                predicates.Add("DecisionEnforceUnique");
            }

            if (uniqueDecision.RequiresRemediation)
            {
                predicates.Add("DecisionRequiresRemediation");
            }
        }

        return predicates;
    }

    private static List<string> CollectSequencePredicates(SequenceModel sequence)
    {
        var predicates = new List<string>();

        if (!string.IsNullOrWhiteSpace(sequence.DataType))
        {
            predicates.Add("HasDataType");
        }

        if (sequence.StartValue is not null)
        {
            predicates.Add("HasStartValue");
        }

        if (sequence.Minimum is not null)
        {
            predicates.Add("HasMinimum");
        }

        if (sequence.Maximum is not null)
        {
            predicates.Add("HasMaximum");
        }

        if (sequence.IsCycleEnabled)
        {
            predicates.Add("IsCycleEnabled");
        }

        if (sequence.CacheMode == SequenceCacheMode.Cache)
        {
            predicates.Add("HasCache");
        }

        if (sequence.CacheMode == SequenceCacheMode.NoCache)
        {
            predicates.Add("NoCache");
        }

        if (sequence.CacheSize is not null)
        {
            predicates.Add("HasCacheSize");
        }

        return predicates;
    }

    private static bool HasDefaultConstraint(AttributeModel attribute)
        => attribute.OnDisk.DefaultConstraint is not null &&
           !string.IsNullOrWhiteSpace(attribute.OnDisk.DefaultConstraint.Definition);
}
