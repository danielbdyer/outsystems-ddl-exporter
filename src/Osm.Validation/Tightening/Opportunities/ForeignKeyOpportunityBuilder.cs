using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.Sql;
using Osm.Validation.Tightening;

namespace Osm.Validation.Tightening.Opportunities;

internal sealed class ForeignKeyOpportunityBuilder
{
    private readonly OpportunityContext _context;

    public ForeignKeyOpportunityBuilder(OpportunityContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public IEnumerable<Opportunity> Build(IEnumerable<ForeignKeyDecision> decisions)
    {
        if (decisions is null)
        {
            yield break;
        }

        foreach (var decision in decisions)
        {
            if (decision is null || !decision.CreateConstraint)
            {
                continue;
            }

            if (!_context.AttributeLookup.TryGetValue(decision.Column, out var entry))
            {
                continue;
            }

            var targetEntity = ResolveTargetEntity(entry, _context.EntityLookup);
            if (targetEntity is null)
            {
                continue;
            }

            _context.ForeignKeys.TryGetValue(decision.Column, out var fkReality);

            yield return CreateOpportunity(decision, entry, targetEntity, fkReality);
        }
    }

    private static Opportunity CreateOpportunity(
        ForeignKeyDecision decision,
        EntityAttributeIndex.EntityAttributeIndexEntry entry,
        EntityModel targetEntity,
        ForeignKeyReality? fkReality)
    {
        var statements = BuildForeignKeyStatements(entry.Entity, entry.Attribute, targetEntity);
        var evidence = BuildForeignKeyEvidence(fkReality);
        var metrics = new OpportunityMetrics(
            RequiresRemediation: false,
            EvidenceAvailable: fkReality is not null,
            DataClean: fkReality is null ? null : !fkReality.HasOrphan,
            HasDuplicates: null,
            HasOrphans: fkReality?.HasOrphan);
        var columns = ImmutableArray.Create(BuildColumnAnalysis(entry, fkReality));
        var rationales = decision.Rationales.IsDefault ? ImmutableArray<string>.Empty : decision.Rationales;

        return new Opportunity(
            ConstraintType.ForeignKey,
            ChangeRisk.SafeToApply,
            entry.Entity.Schema.Value,
            entry.Entity.PhysicalName.Value,
            entry.Attribute.ColumnName.Value,
            statements,
            rationales,
            evidence,
            metrics,
            columns);
    }

    private static ImmutableArray<string> BuildForeignKeyStatements(EntityModel sourceEntity, AttributeModel attribute, EntityModel targetEntity)
    {
        var fkName = BuildForeignKeyName(sourceEntity, attribute, targetEntity);
        var sourceTable = SqlIdentifierFormatter.Qualify(sourceEntity.Schema.Value, sourceEntity.PhysicalName.Value);
        var targetTable = SqlIdentifierFormatter.Qualify(targetEntity.Schema.Value, targetEntity.PhysicalName.Value);
        var sourceColumn = SqlIdentifierFormatter.Quote(attribute.ColumnName.Value);
        var targetColumns = ResolveTargetColumns(targetEntity);

        var builder = ImmutableArray.CreateBuilder<string>(2);
        builder.Add($"ALTER TABLE {sourceTable} WITH CHECK ADD CONSTRAINT {SqlIdentifierFormatter.Quote(fkName)} FOREIGN KEY ({sourceColumn}) REFERENCES {targetTable} ({targetColumns});");
        builder.Add($"ALTER TABLE {sourceTable} CHECK CONSTRAINT {SqlIdentifierFormatter.Quote(fkName)};");
        return builder.ToImmutable();
    }

    private static string ResolveTargetColumns(EntityModel entity)
    {
        var identifiers = entity.Attributes.Where(static a => a.IsIdentifier).ToArray();
        if (identifiers.Length == 0)
        {
            identifiers = entity.Attributes.Take(1).ToArray();
        }

        return string.Join(", ", identifiers.Select(static a => SqlIdentifierFormatter.Quote(a.ColumnName.Value)));
    }

    private static string BuildForeignKeyName(EntityModel sourceEntity, AttributeModel attribute, EntityModel targetEntity)
    {
        var baseName = $"FK_{sourceEntity.PhysicalName.Value}_{attribute.ColumnName.Value}";
        if (!string.IsNullOrWhiteSpace(targetEntity.PhysicalName.Value))
        {
            baseName += $"_{targetEntity.PhysicalName.Value}";
        }

        return baseName.Length <= 128 ? baseName : baseName[..128];
    }

    private static ImmutableArray<string> BuildForeignKeyEvidence(ForeignKeyReality? fkReality)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        if (fkReality is null)
        {
            builder.Add("Foreign key profiling unavailable.");
            return builder.ToImmutable();
        }

        builder.Add($"HasConstraint={fkReality.Reference.HasDatabaseConstraint}");
        builder.Add(
            $"HasOrphans={fkReality.HasOrphan} (Outcome={fkReality.ProbeStatus.Outcome}, Sample={fkReality.ProbeStatus.SampleSize.ToString(CultureInfo.InvariantCulture)}, Captured={fkReality.ProbeStatus.CapturedAtUtc:O})");

        return builder.ToImmutable();
    }

    private static ColumnAnalysis BuildColumnAnalysis(
        EntityAttributeIndex.EntityAttributeIndexEntry entry,
        ForeignKeyReality? fkReality)
    {
        return new ColumnAnalysis(
            entry.Coordinate,
            entry.Entity.Module.Value,
            entry.Entity.LogicalName.Value,
            entry.Attribute.LogicalName.Value,
            entry.Attribute.DataType,
            entry.Attribute.OnDisk.SqlType,
            entry.Attribute.OnDisk.IsNullable,
            entry.Attribute.OnDisk.IsIdentity,
            null,
            null,
            null,
            null,
            null,
            fkReality?.HasOrphan,
            fkReality?.Reference.HasDatabaseConstraint ?? entry.Attribute.Reference.HasDatabaseConstraint,
            entry.Attribute.Reference.DeleteRuleCode);
    }

    private static EntityModel? ResolveTargetEntity(
        EntityAttributeIndex.EntityAttributeIndexEntry entry,
        IReadOnlyDictionary<string, EntityModel> entityLookup)
    {
        if (!entry.Attribute.Reference.IsReference || entry.Attribute.Reference.TargetEntity is null)
        {
            return null;
        }

        if (entityLookup.TryGetValue(entry.Attribute.Reference.TargetEntity.Value.Value, out var entity))
        {
            return entity;
        }

        return null;
    }
}
