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

internal sealed class UniqueIndexOpportunityBuilder
{
    private readonly OpportunityContext _context;

    public UniqueIndexOpportunityBuilder(OpportunityContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public IEnumerable<Opportunity> Build(IEnumerable<UniqueIndexDecision> decisions)
    {
        if (decisions is null)
        {
            yield break;
        }

        foreach (var decision in decisions)
        {
            if (decision is null || !decision.EnforceUnique)
            {
                continue;
            }

            if (!_context.UniqueIndexLookup.TryGetValue(decision.Index, out var entry))
            {
                continue;
            }

            yield return CreateOpportunity(decision, entry, _context.ColumnProfiles, _context.UniqueProfiles, _context.CompositeProfiles);
        }
    }

    private static Opportunity CreateOpportunity(
        UniqueIndexDecision decision,
        (EntityModel Entity, IndexModel Index) entry,
        IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> columnProfiles,
        IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> uniqueProfiles,
        IReadOnlyDictionary<string, CompositeUniqueCandidateProfile> compositeProfiles)
    {
        var statements = ImmutableArray.Create(BuildCreateUniqueStatement(entry.Entity, entry.Index));
        var rationales = decision.Rationales.IsDefault ? ImmutableArray<string>.Empty : decision.Rationales;
        var keyColumns = entry.Index.Columns
            .Where(static c => !c.IsIncluded)
            .OrderBy(static c => c.Ordinal)
            .Select(c => new ColumnCoordinate(entry.Entity.Schema, entry.Entity.PhysicalName, c.Column))
            .ToArray();

        var columnAnalyses = ImmutableArray.CreateBuilder<ColumnAnalysis>(keyColumns.Length);
        var evidence = ImmutableArray.CreateBuilder<string>();

        bool? dataClean = null;
        bool? hasDuplicates = null;

        foreach (var coordinate in keyColumns)
        {
            columnProfiles.TryGetValue(coordinate, out var profile);
            uniqueProfiles.TryGetValue(coordinate, out var uniqueProfile);
            var uniqueProbe = uniqueProfile?.ProbeStatus;

            var compositeKey = UniqueIndexEvidenceKey.Create(
                coordinate.Schema.Value,
                coordinate.Table.Value,
                keyColumns.Select(static c => c.Column.Value));

            compositeProfiles.TryGetValue(compositeKey, out var compositeProfile);

            if (compositeProfile is not null)
            {
                hasDuplicates ??= compositeProfile.HasDuplicate;
                dataClean ??= !compositeProfile.HasDuplicate;
                evidence.Add($"Composite duplicates={compositeProfile.HasDuplicate}");
            }
            else if (uniqueProfile is not null)
            {
                hasDuplicates ??= uniqueProfile.HasDuplicate;
                dataClean ??= !uniqueProfile.HasDuplicate;
                evidence.Add(
                    $"Unique duplicates={uniqueProfile.HasDuplicate} (Outcome={uniqueProfile.ProbeStatus.Outcome}, Sample={uniqueProfile.ProbeStatus.SampleSize.ToString(CultureInfo.InvariantCulture)}, Captured={uniqueProfile.ProbeStatus.CapturedAtUtc:O})");
            }

            columnAnalyses.Add(BuildColumnAnalysis(entry.Entity, coordinate, profile, uniqueProfile, uniqueProbe));
        }

        if (columnAnalyses.Count == 0)
        {
            evidence.Add("No key columns resolved for index.");
        }

        var metrics = new OpportunityMetrics(
            decision.RequiresRemediation,
            evidence.Count > 0,
            dataClean,
            hasDuplicates,
            null);

        var risk = decision.RequiresRemediation ? ChangeRisk.NeedsRemediation : ChangeRisk.SafeToApply;

        return new Opportunity(
            ConstraintType.Unique,
            risk,
            entry.Entity.Schema.Value,
            entry.Entity.PhysicalName.Value,
            entry.Index.Name.Value,
            statements,
            rationales,
            evidence.ToImmutable(),
            metrics,
            columnAnalyses.ToImmutable());
    }

    private static ColumnAnalysis BuildColumnAnalysis(
        EntityModel entity,
        ColumnCoordinate coordinate,
        ColumnProfile? profile,
        UniqueCandidateProfile? uniqueProfile,
        ProfilingProbeStatus? uniqueProbe)
    {
        var attribute = entity.Attributes.First(
            a => string.Equals(a.ColumnName.Value, coordinate.Column.Value, StringComparison.OrdinalIgnoreCase));

        return new ColumnAnalysis(
            coordinate,
            entity.Module.Value,
            entity.LogicalName.Value,
            attribute.LogicalName.Value,
            attribute.DataType,
            attribute.OnDisk.SqlType,
            attribute.OnDisk.IsNullable,
            attribute.OnDisk.IsIdentity,
            profile?.RowCount,
            profile?.NullCount,
            profile?.NullCountStatus,
            uniqueProfile?.HasDuplicate,
            uniqueProbe,
            null,
            attribute.Reference.HasDatabaseConstraint,
            attribute.Reference.DeleteRuleCode);
    }

    private static string BuildCreateUniqueStatement(EntityModel entity, IndexModel index)
    {
        var table = SqlIdentifierFormatter.Qualify(entity.Schema.Value, entity.PhysicalName.Value);
        var indexName = SqlIdentifierFormatter.Quote(index.Name.Value);
        var keyColumns = index.Columns
            .Where(static c => !c.IsIncluded)
            .OrderBy(static c => c.Ordinal)
            .Select(static c => SqlIdentifierFormatter.Quote(c.Column.Value) + (c.Direction == IndexColumnDirection.Descending ? " DESC" : " ASC"));

        return $"CREATE UNIQUE NONCLUSTERED INDEX {indexName} ON {table} ({string.Join(", ", keyColumns)});";
    }
}
