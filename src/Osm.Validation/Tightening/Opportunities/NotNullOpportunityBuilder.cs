using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.Sql;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Signals;

namespace Osm.Validation.Tightening.Opportunities;

internal sealed class NotNullOpportunityBuilder
{
    private readonly OpportunityContext _context;

    public NotNullOpportunityBuilder(OpportunityContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public IEnumerable<Opportunity> Build(IEnumerable<NullabilityDecision> decisions)
    {
        if (decisions is null)
        {
            yield break;
        }

        foreach (var decision in decisions)
        {
            if (decision is null || !decision.MakeNotNull)
            {
                continue;
            }

            if (!_context.AttributeLookup.TryGetValue(decision.Column, out var entry))
            {
                continue;
            }

            _context.ColumnProfiles.TryGetValue(decision.Column, out var profile);
            _context.UniqueProfiles.TryGetValue(decision.Column, out var uniqueProfile);
            _context.ForeignKeys.TryGetValue(decision.Column, out var fkReality);

            yield return CreateOpportunity(decision, entry, profile, uniqueProfile, fkReality);
        }
    }

    private static Opportunity CreateOpportunity(
        NullabilityDecision decision,
        EntityAttributeIndex.EntityAttributeIndexEntry entry,
        ColumnProfile? columnProfile,
        UniqueCandidateProfile? uniqueProfile,
        ForeignKeyReality? fkReality)
    {
        var statements = ImmutableArray.Create(BuildAlterColumnStatement(entry.Entity, entry.Attribute));
        var evidence = BuildEvidence(columnProfile, decision.Trace);
        var metrics = new OpportunityMetrics(
            decision.RequiresRemediation,
            columnProfile is not null,
            columnProfile is null ? null : columnProfile.NullCount == 0,
            uniqueProfile?.HasDuplicate,
            fkReality?.HasOrphan);
        var columns = ImmutableArray.Create(BuildColumnAnalysis(entry, columnProfile, uniqueProfile, fkReality));
        var rationales = decision.Rationales.IsDefault ? ImmutableArray<string>.Empty : decision.Rationales;
        var risk = decision.RequiresRemediation ? ChangeRisk.NeedsRemediation : ChangeRisk.SafeToApply;

        return new Opportunity(
            ConstraintType.NotNull,
            risk,
            entry.Entity.Schema.Value,
            entry.Entity.PhysicalName.Value,
            entry.Attribute.ColumnName.Value,
            statements,
            rationales,
            evidence,
            metrics,
            columns);
    }

    private static ImmutableArray<string> BuildEvidence(ColumnProfile? profile, SignalEvaluation? trace)
    {
        var builder = ImmutableArray.CreateBuilder<string>();

        if (profile is null)
        {
            builder.Add("Null profile unavailable.");
        }
        else
        {
            builder.Add($"Rows={profile.RowCount.ToString(CultureInfo.InvariantCulture)}");
            builder.Add(
                $"Nulls={profile.NullCount.ToString(CultureInfo.InvariantCulture)} (Outcome={profile.NullCountStatus.Outcome}, Sample={profile.NullCountStatus.SampleSize.ToString(CultureInfo.InvariantCulture)}, Captured={profile.NullCountStatus.CapturedAtUtc:O})");
        }

        if (trace is not null)
        {
            builder.AddRange(trace.CollectRationales().Distinct(StringComparer.Ordinal));
        }

        return builder.ToImmutable();
    }

    private static string BuildAlterColumnStatement(EntityModel entity, AttributeModel attribute)
    {
        var qualifiedTable = SqlIdentifierFormatter.Qualify(entity.Schema.Value, entity.PhysicalName.Value);
        var column = SqlIdentifierFormatter.Quote(attribute.ColumnName.Value);
        var dataType = ResolveColumnDataType(attribute);
        var builder = new System.Text.StringBuilder();
        builder.Append("ALTER TABLE ");
        builder.Append(qualifiedTable);
        builder.AppendLine();
        builder.Append("    ALTER COLUMN ");
        builder.Append(column);
        builder.Append(' ');
        builder.Append(dataType);
        builder.Append(" NOT NULL;");
        return builder.ToString();
    }

    private static string ResolveColumnDataType(AttributeModel attribute)
    {
        if (!string.IsNullOrWhiteSpace(attribute.OnDisk.SqlType))
        {
            return attribute.OnDisk.SqlType!;
        }

        if (!string.IsNullOrWhiteSpace(attribute.ExternalDatabaseType))
        {
            return attribute.ExternalDatabaseType!;
        }

        return attribute.DataType;
    }

    private static ColumnAnalysis BuildColumnAnalysis(
        EntityAttributeIndex.EntityAttributeIndexEntry entry,
        ColumnProfile? columnProfile,
        UniqueCandidateProfile? uniqueProfile,
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
            columnProfile?.RowCount,
            columnProfile?.NullCount,
            columnProfile?.NullCountStatus,
            uniqueProfile?.HasDuplicate,
            uniqueProfile?.ProbeStatus,
            fkReality?.HasOrphan,
            fkReality?.Reference.HasDatabaseConstraint ?? entry.Attribute.Reference.HasDatabaseConstraint,
            entry.Attribute.Reference.DeleteRuleCode);
    }
}
