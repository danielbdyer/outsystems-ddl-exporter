using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Validation.Tightening.Signals;
using Osm.Domain.ValueObjects;

namespace Osm.Validation.Tightening.Opportunities;

public interface ITighteningAnalyzer
{
    OpportunitiesReport Analyze(OsmModel model, ProfileSnapshot profile, PolicyDecisionSet decisions);
}

public sealed class TighteningOpportunitiesAnalyzer : ITighteningAnalyzer
{
    public OpportunitiesReport Analyze(OsmModel model, ProfileSnapshot profile, PolicyDecisionSet decisions)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (decisions is null)
        {
            throw new ArgumentNullException(nameof(decisions));
        }

        var attributeIndex = EntityAttributeIndex.Create(model);
        var attributeLookup = attributeIndex.Entries.ToDictionary(static entry => entry.Coordinate, static entry => entry);
        var entityLookup = BuildEntityLookup(model);
        var columnProfiles = new Dictionary<ColumnCoordinate, ColumnProfile>();
        foreach (var column in profile.Columns)
        {
            columnProfiles[ColumnCoordinate.From(column)] = column;
        }

        var uniqueProfiles = new Dictionary<ColumnCoordinate, UniqueCandidateProfile>();
        foreach (var unique in profile.UniqueCandidates)
        {
            uniqueProfiles[ColumnCoordinate.From(unique)] = unique;
        }
        var compositeProfiles = BuildCompositeProfileLookup(profile.CompositeUniqueCandidates);
        var foreignKeys = new Dictionary<ColumnCoordinate, ForeignKeyReality>();
        foreach (var foreignKey in profile.ForeignKeys)
        {
            foreignKeys[ColumnCoordinate.From(foreignKey.Reference)] = foreignKey;
        }
        var indexLookup = BuildIndexLookup(model);

        var opportunities = ImmutableArray.CreateBuilder<Opportunity>();
        var dispositionCounts = new Dictionary<OpportunityDisposition, int>();
        var typeCounts = new Dictionary<OpportunityType, int>();
        var riskCounts = new Dictionary<RiskLevel, int>();

        foreach (var decision in decisions.Nullability.Values)
        {
            if (!decision.MakeNotNull || !attributeLookup.TryGetValue(decision.Column, out var entry))
            {
                continue;
            }

            columnProfiles.TryGetValue(decision.Column, out var profileRecord);
            uniqueProfiles.TryGetValue(decision.Column, out var uniqueProfile);
            foreignKeys.TryGetValue(decision.Column, out var fkReality);

            var opportunity = CreateNotNullOpportunity(decision, entry, profileRecord, uniqueProfile, fkReality);
            RecordOpportunity(opportunity, opportunities, dispositionCounts, typeCounts, riskCounts);
        }

        foreach (var decision in decisions.UniqueIndexes.Values)
        {
            if (!decision.EnforceUnique || !indexLookup.TryGetValue(decision.Index, out var indexEntry))
            {
                continue;
            }

        }

        foreach (var decision in decisions.ForeignKeys.Values)
        {
            if (!decision.CreateConstraint || !attributeLookup.TryGetValue(decision.Column, out var entry))
            {
                continue;
            }

            var fkReality = foreignKeys.TryGetValue(decision.Column, out var fkProfile) ? fkProfile : null;
            var targetEntity = ResolveTargetEntity(entry, entityLookup);
            if (targetEntity is null)
            {
                continue;
            }

            var opportunity = CreateForeignKeyOpportunity(decision, entry, targetEntity, fkReality);
            RecordOpportunity(opportunity, opportunities, dispositionCounts, typeCounts, riskCounts);
        }

        var report = new OpportunitiesReport(
            opportunities.ToImmutable(),
            dispositionCounts.ToImmutableDictionary(),
            typeCounts.ToImmutableDictionary(),
            riskCounts.ToImmutableDictionary(),
            DateTimeOffset.UtcNow);

        return report;
    }

    private static void RecordOpportunity(
        Opportunity opportunity,
        ImmutableArray<Opportunity>.Builder accumulator,
        IDictionary<OpportunityDisposition, int> dispositionCounts,
        IDictionary<OpportunityType, int> typeCounts,
        IDictionary<RiskLevel, int> riskCounts)
    {
        accumulator.Add(opportunity);
        Increment(dispositionCounts, opportunity.Disposition);
        Increment(typeCounts, opportunity.Type);
        Increment(riskCounts, opportunity.Risk.Level);
    }

    private static void Increment<TKey>(IDictionary<TKey, int> map, TKey key)
        where TKey : notnull
    {
        if (map.TryGetValue(key, out var count))
        {
            map[key] = count + 1;
        }
        else
        {
            map[key] = 1;
        }
    }

    private static Dictionary<IndexCoordinate, (EntityModel Entity, IndexModel Index)> BuildIndexLookup(OsmModel model)
    {
        var result = new Dictionary<IndexCoordinate, (EntityModel, IndexModel)>();
        foreach (var entity in model.Modules.SelectMany(static module => module.Entities))
        {
            foreach (var index in entity.Indexes.Where(static i => i.IsUnique))
            {
                var coordinate = new IndexCoordinate(entity.Schema, entity.PhysicalName, index.Name);
                result[coordinate] = (entity, index);
            }
        }

        return result;
    }

    private static Dictionary<string, CompositeUniqueCandidateProfile> BuildCompositeProfileLookup(
        ImmutableArray<CompositeUniqueCandidateProfile> profiles)
    {
        var result = new Dictionary<string, CompositeUniqueCandidateProfile>(StringComparer.OrdinalIgnoreCase);
        if (profiles.IsDefaultOrEmpty)
        {
            return result;
        }

        foreach (var profile in profiles)
        {
            var key = global::Osm.Validation.Tightening.UniqueIndexEvidenceKey.Create(
                profile.Schema.Value,
                profile.Table.Value,
                profile.Columns.Select(static c => c.Value));
            result[key] = profile;
        }

        return result;
    }

    private static Opportunity CreateNotNullOpportunity(
        NullabilityDecision decision,
        EntityAttributeIndex.EntityAttributeIndexEntry entry,
        ColumnProfile? columnProfile,
        UniqueCandidateProfile? uniqueProfile,
        ForeignKeyReality? fkReality)
    {
        var disposition = decision.RequiresRemediation
            ? OpportunityDisposition.NeedsRemediation
            : OpportunityDisposition.ReadyToApply;
        var risk = ChangeRiskClassifier.ForNotNull(decision);
        var statements = ImmutableArray.Create(BuildAlterColumnStatement(entry.Entity, entry.Attribute));
        var evidence = BuildNotNullEvidence(columnProfile, decision.Trace);
        var evidenceSummary = new OpportunityEvidenceSummary(
            decision.RequiresRemediation,
            columnProfile is not null,
            columnProfile is null ? null : columnProfile.NullCount == 0,
            uniqueProfile?.HasDuplicate,
            fkReality?.HasOrphan);
        var columns = ImmutableArray.Create(BuildColumnInsight(entry, columnProfile, uniqueProfile, fkReality));
        var rationales = decision.Rationales.IsDefault ? ImmutableArray<string>.Empty : decision.Rationales;
        var summary = disposition == OpportunityDisposition.NeedsRemediation
            ? "Remediate data before enforcing NOT NULL."
            : "Enforce NOT NULL constraint.";

        return Opportunity.Create(
            OpportunityType.Nullability,
            "NOT NULL",
            summary,
            risk,
            evidence,
            column: entry.Coordinate,
            disposition: disposition,
            statements: statements,
            rationales: rationales,
            evidenceSummary: evidenceSummary,
            columns: columns,
            schema: entry.Entity.Schema.Value,
            table: entry.Entity.PhysicalName.Value,
            constraintName: entry.Attribute.ColumnName.Value);
    }

    private static ImmutableArray<string> BuildNotNullEvidence(ColumnProfile? profile, SignalEvaluation? trace)
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
        var qualifiedTable = Qualify(entity.Schema.Value, entity.PhysicalName.Value);
        var column = Quote(attribute.ColumnName.Value);
        var dataType = ResolveColumnDataType(attribute);
        var builder = new StringBuilder();
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

    private static Opportunity CreateUniqueOpportunity(
        UniqueIndexDecision decision,
        (EntityModel Entity, IndexModel Index) entry,
        IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> columnProfiles,
        IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> uniqueProfiles,
        IReadOnlyDictionary<string, CompositeUniqueCandidateProfile> compositeProfiles)
    {
        var disposition = decision.RequiresRemediation
            ? OpportunityDisposition.NeedsRemediation
            : OpportunityDisposition.ReadyToApply;
        var risk = disposition == OpportunityDisposition.NeedsRemediation
            ? ChangeRisk.Moderate("Remediate data before enforcing the unique index.")
            : ChangeRisk.Low("Unique index can be enforced automatically.");
        var statements = ImmutableArray.Create(BuildCreateUniqueStatement(entry.Entity, entry.Index));
        var rationales = decision.Rationales.IsDefault ? ImmutableArray<string>.Empty : decision.Rationales;

        var keyColumns = entry.Index.Columns
            .Where(static c => !c.IsIncluded)
            .OrderBy(static c => c.Ordinal)
            .Select(c => new ColumnCoordinate(entry.Entity.Schema, entry.Entity.PhysicalName, c.Column))
            .ToArray();

        var columnAnalyses = ImmutableArray.CreateBuilder<OpportunityColumn>(keyColumns.Length);
        var evidence = ImmutableArray.CreateBuilder<string>();

        bool? dataClean = null;
        bool? hasDuplicates = null;
        foreach (var coordinate in keyColumns)
        {
            columnProfiles.TryGetValue(coordinate, out var profile);
            uniqueProfiles.TryGetValue(coordinate, out var uniqueProfile);
            var uniqueProbe = uniqueProfile?.ProbeStatus;

            var compositeKey = global::Osm.Validation.Tightening.UniqueIndexEvidenceKey.Create(
                coordinate.Schema.Value,
                coordinate.Table.Value,
                keyColumns.Select(static c => c.Column.Value));
            compositeProfiles.TryGetValue(compositeKey, out var compositeProfile);

            if (compositeProfile is not null)
            {
                hasDuplicates ??= compositeProfile.HasDuplicate;
                dataClean ??= !compositeProfile.HasDuplicate;
                evidence.Add(
                    $"Composite duplicates={compositeProfile.HasDuplicate}");
            }
            else if (uniqueProfile is not null)
            {
                hasDuplicates ??= uniqueProfile.HasDuplicate;
                dataClean ??= !uniqueProfile.HasDuplicate;
                evidence.Add(
                    $"Unique duplicates={uniqueProfile.HasDuplicate} (Outcome={uniqueProfile.ProbeStatus.Outcome}, Sample={uniqueProfile.ProbeStatus.SampleSize.ToString(CultureInfo.InvariantCulture)}, Captured={uniqueProfile.ProbeStatus.CapturedAtUtc:O})");
            }

            columnAnalyses.Add(BuildColumnInsight(entry.Entity, coordinate, profile, uniqueProfile, uniqueProbe));
        }

        if (columnAnalyses.Count == 0)
        {
            evidence.Add("No key columns resolved for index.");
        }

        var evidenceSummary = new OpportunityEvidenceSummary(
            decision.RequiresRemediation,
            evidence.Count > 0,
            dataClean,
            hasDuplicates,
            null);

        var summary = disposition == OpportunityDisposition.NeedsRemediation
            ? "Remediate data before enforcing the unique index."
            : "Enforce the unique index automatically.";

        return Opportunity.Create(
            OpportunityType.UniqueIndex,
            "UNIQUE",
            summary,
            risk,
            evidence,
            index: new IndexCoordinate(entry.Entity.Schema, entry.Entity.PhysicalName, entry.Index.Name),
            disposition: disposition,
            statements: statements,
            rationales: rationales,
            evidenceSummary: evidenceSummary,
            columns: columnAnalyses.ToImmutable(),
            schema: entry.Entity.Schema.Value,
            table: entry.Entity.PhysicalName.Value,
            constraintName: entry.Index.Name.Value);
    }

    private static OpportunityColumn BuildColumnInsight(
        EntityAttributeIndex.EntityAttributeIndexEntry entry,
        ColumnProfile? columnProfile,
        UniqueCandidateProfile? uniqueProfile,
        ForeignKeyReality? fkReality)
    {
        return new OpportunityColumn(
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

    private static OpportunityColumn BuildColumnInsight(
        EntityModel entity,
        ColumnCoordinate coordinate,
        ColumnProfile? profile,
        UniqueCandidateProfile? uniqueProfile,
        ProfilingProbeStatus? uniqueProbe)
    {
        var attribute = entity.Attributes.First(a => string.Equals(a.ColumnName.Value, coordinate.Column.Value, StringComparison.OrdinalIgnoreCase));
        return new OpportunityColumn(
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

    private static Opportunity CreateForeignKeyOpportunity(
        ForeignKeyDecision decision,
        EntityAttributeIndex.EntityAttributeIndexEntry entry,
        EntityModel targetEntity,
        ForeignKeyReality? fkReality)
    {
        var statements = BuildForeignKeyStatements(entry.Entity, entry.Attribute, targetEntity);
        var evidence = BuildForeignKeyEvidence(fkReality);
        var evidenceSummary = new OpportunityEvidenceSummary(
            RequiresRemediation: false,
            EvidenceAvailable: fkReality is not null,
            DataClean: fkReality is null ? null : !fkReality.HasOrphan,
            HasDuplicates: null,
            HasOrphans: fkReality?.HasOrphan);
        var columns = ImmutableArray.Create(BuildColumnInsight(entry, null, null, fkReality));
        var rationales = decision.Rationales.IsDefault ? ImmutableArray<string>.Empty : decision.Rationales;

        return Opportunity.Create(
            OpportunityType.ForeignKey,
            "FOREIGN KEY",
            "Create foreign key constraint.",
            ChangeRisk.Low("Foreign key creation is safe to apply."),
            evidence,
            column: entry.Coordinate,
            disposition: OpportunityDisposition.ReadyToApply,
            statements: statements,
            rationales: rationales,
            evidenceSummary: evidenceSummary,
            columns: columns,
            schema: entry.Entity.Schema.Value,
            table: entry.Entity.PhysicalName.Value,
            constraintName: BuildForeignKeyName(entry.Entity, entry.Attribute, targetEntity));
    }

    private static ImmutableArray<string> BuildForeignKeyStatements(EntityModel sourceEntity, AttributeModel attribute, EntityModel targetEntity)
    {
        var fkName = BuildForeignKeyName(sourceEntity, attribute, targetEntity);
        var sourceTable = Qualify(sourceEntity.Schema.Value, sourceEntity.PhysicalName.Value);
        var targetTable = Qualify(targetEntity.Schema.Value, targetEntity.PhysicalName.Value);
        var sourceColumn = Quote(attribute.ColumnName.Value);
        var targetColumns = ResolveTargetColumns(targetEntity);

        var builder = ImmutableArray.CreateBuilder<string>(2);
        builder.Add($"ALTER TABLE {sourceTable} WITH CHECK ADD CONSTRAINT {Quote(fkName)} FOREIGN KEY ({sourceColumn}) REFERENCES {targetTable} ({targetColumns});");
        builder.Add($"ALTER TABLE {sourceTable} CHECK CONSTRAINT {Quote(fkName)};");
        return builder.ToImmutable();
    }

    private static string ResolveTargetColumns(EntityModel entity)
    {
        var identifiers = entity.Attributes.Where(static a => a.IsIdentifier).ToArray();
        if (identifiers.Length == 0)
        {
            identifiers = entity.Attributes.Take(1).ToArray();
        }

        return string.Join(", ", identifiers.Select(static a => Quote(a.ColumnName.Value)));
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

        builder.Add(
            $"HasConstraint={fkReality.Reference.HasDatabaseConstraint}");
        builder.Add(
            $"HasOrphans={fkReality.HasOrphan} (Outcome={fkReality.ProbeStatus.Outcome}, Sample={fkReality.ProbeStatus.SampleSize.ToString(CultureInfo.InvariantCulture)}, Captured={fkReality.ProbeStatus.CapturedAtUtc:O})");

        return builder.ToImmutable();
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

    private static IReadOnlyDictionary<string, EntityModel> BuildEntityLookup(OsmModel model)
    {
        var lookup = new Dictionary<string, EntityModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in model.Modules.SelectMany(static module => module.Entities))
        {
            lookup.TryAdd(entity.LogicalName.Value, entity);
        }

        return lookup;
    }

    private static string BuildCreateUniqueStatement(EntityModel entity, IndexModel index)
    {
        var table = Qualify(entity.Schema.Value, entity.PhysicalName.Value);
        var indexName = Quote(index.Name.Value);
        var keyColumns = index.Columns
            .Where(static c => !c.IsIncluded)
            .OrderBy(static c => c.Ordinal)
            .Select(static c => Quote(c.Column.Value) + (c.Direction == IndexColumnDirection.Descending ? " DESC" : " ASC"));

        return $"CREATE UNIQUE NONCLUSTERED INDEX {indexName} ON {table} ({string.Join(", ", keyColumns)});";
    }

    private static string Qualify(string schema, string name)
        => $"{Quote(schema)}.{Quote(name)}";

    private static string Quote(string identifier)
        => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}
