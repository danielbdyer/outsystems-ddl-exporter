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

        var context = OpportunityAnalysisContext.Create(model, profile);
        var accumulator = new OpportunityAccumulator();

        foreach (var opportunity in AnalyzeNullability(decisions.Nullability.Values, context))
        {
            accumulator.Record(opportunity);
        }

        foreach (var opportunity in AnalyzeUniqueIndexes(decisions.UniqueIndexes.Values, context))
        {
            accumulator.Record(opportunity);
        }

        foreach (var opportunity in AnalyzeForeignKeys(decisions.ForeignKeys.Values, context))
        {
            accumulator.Record(opportunity);
        }

        return accumulator.BuildReport();
    }

    private static IEnumerable<Opportunity> AnalyzeNullability(
        IEnumerable<NullabilityDecision> decisions,
        OpportunityAnalysisContext context)
    {
        foreach (var decision in decisions)
        {
            if (!decision.MakeNotNull)
            {
                continue;
            }

            if (!context.Attributes.TryGet(decision.Column, out var entry))
            {
                continue;
            }

            var opportunity = CreateNotNullOpportunity(
                decision,
                entry,
                context.ColumnProfiles.Find(decision.Column),
                context.UniqueProfiles.Find(decision.Column),
                context.ForeignKeys.Find(decision.Column));

            yield return opportunity;
        }
    }

    private static IEnumerable<Opportunity> AnalyzeUniqueIndexes(
        IEnumerable<UniqueIndexDecision> decisions,
        OpportunityAnalysisContext context)
    {
        foreach (var decision in decisions)
        {
            if (!decision.EnforceUnique)
            {
                continue;
            }

            if (!context.UniqueIndexes.TryGet(decision.Index, out var indexEntry))
            {
                continue;
            }

            yield return CreateUniqueOpportunity(
                decision,
                indexEntry,
                context.ColumnProfiles,
                context.UniqueProfiles,
                context.CompositeUniqueProfiles);
        }
    }

    private static IEnumerable<Opportunity> AnalyzeForeignKeys(
        IEnumerable<ForeignKeyDecision> decisions,
        OpportunityAnalysisContext context)
    {
        foreach (var decision in decisions)
        {
            if (!decision.CreateConstraint)
            {
                continue;
            }

            if (!context.Attributes.TryGet(decision.Column, out var entry))
            {
                continue;
            }

            var fkReality = context.ForeignKeys.Find(decision.Column);
            var targetEntity = ResolveTargetEntity(entry, context.Entities);
            if (targetEntity is null)
            {
                continue;
            }

            yield return CreateForeignKeyOpportunity(decision, entry, targetEntity, fkReality);
        }
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

    private static OpportunityCategory ClassifyNullabilityOpportunity(
        NullabilityDecision decision,
        ImmutableArray<string> rationales,
        bool isPhysicallyNotNull)
    {
        // Contradiction: Data violates model expectations
        if (decision.RequiresRemediation &&
            ContainsAny(rationales, TighteningRationales.DataHasNulls, TighteningRationales.DataHasOrphans))
        {
            return OpportunityCategory.Contradiction;
        }

        // Validation: Already physically enforced and profiling confirms it's clean
        if (isPhysicallyNotNull && decision.MakeNotNull)
        {
            return OpportunityCategory.Validation;
        }

        // Recommendation: New constraint we could safely apply
        if (decision.MakeNotNull && !decision.RequiresRemediation)
        {
            return OpportunityCategory.Recommendation;
        }

        return OpportunityCategory.Unknown;
    }

    private static OpportunityCategory ClassifyUniqueOpportunity(
        UniqueIndexDecision decision,
        ImmutableArray<string> rationales,
        bool hasPhysicalUniqueConstraint)
    {
        // Contradiction: Duplicates found in unique candidate
        if (decision.RequiresRemediation &&
            ContainsAny(rationales,
                TighteningRationales.UniqueDuplicatesPresent,
                TighteningRationales.CompositeUniqueDuplicatesPresent))
        {
            return OpportunityCategory.Contradiction;
        }

        // Validation: Already has unique constraint that profiling confirms
        if (hasPhysicalUniqueConstraint && decision.EnforceUnique)
        {
            return OpportunityCategory.Validation;
        }

        // Recommendation: New unique index we could safely apply
        if (decision.EnforceUnique && !decision.RequiresRemediation)
        {
            return OpportunityCategory.Recommendation;
        }

        return OpportunityCategory.Unknown;
    }

    private static OpportunityCategory ClassifyForeignKeyOpportunity(
        ForeignKeyDecision decision,
        ImmutableArray<string> rationales,
        bool hasPhysicalConstraint)
    {
        // Contradiction: Orphaned rows detected
        if (ContainsAny(rationales, TighteningRationales.DataHasOrphans))
        {
            return OpportunityCategory.Contradiction;
        }

        // Validation: Already has FK constraint that profiling confirms
        if (hasPhysicalConstraint && decision.CreateConstraint)
        {
            return OpportunityCategory.Validation;
        }

        // Recommendation: New FK constraint we could safely apply
        if (decision.CreateConstraint)
        {
            return OpportunityCategory.Recommendation;
        }

        return OpportunityCategory.Unknown;
    }

    private static bool ContainsAny(ImmutableArray<string> rationales, params string[] targets)
    {
        if (rationales.IsDefault || rationales.Length == 0)
        {
            return false;
        }

        foreach (var target in targets)
        {
            if (rationales.Contains(target, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
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
        var category = ClassifyNullabilityOpportunity(
            decision,
            rationales,
            entry.Attribute.OnDisk.IsNullable == false);

        var summary = category switch
        {
            OpportunityCategory.Contradiction =>
                "DATA CONTRADICTION: Profiling found NULL values that violate the model's mandatory constraint. Manual remediation required.",
            OpportunityCategory.Validation =>
                "Validated: Column is already NOT NULL and profiling confirms data integrity.",
            OpportunityCategory.Recommendation when disposition == OpportunityDisposition.ReadyToApply =>
                "Recommendation: Column qualifies for NOT NULL enforcement based on profiling evidence.",
            _ => disposition == OpportunityDisposition.NeedsRemediation
                ? "Remediate data before enforcing NOT NULL."
                : "Enforce NOT NULL constraint."
        };

        return Opportunity.Create(
            OpportunityType.Nullability,
            "NOT NULL",
            summary,
            risk,
            evidence,
            column: entry.Coordinate,
            disposition: disposition,
            category: category,
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
        ColumnProfileLookup columnProfiles,
        UniqueCandidateLookup uniqueProfiles,
        CompositeUniqueProfileLookup compositeProfiles)
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
        compositeProfiles.TryGet(entry.Entity, keyColumns, out var compositeProfile);

        foreach (var coordinate in keyColumns)
        {
            var profile = columnProfiles.Find(coordinate);
            var uniqueProfile = uniqueProfiles.Find(coordinate);
            var uniqueProbe = uniqueProfile?.ProbeStatus;

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

        var category = ClassifyUniqueOpportunity(
            decision,
            rationales,
            entry.Index.IsUnique);

        var summary = category switch
        {
            OpportunityCategory.Contradiction =>
                "DATA CONTRADICTION: Profiling found duplicate values in a unique index. Manual remediation required.",
            OpportunityCategory.Validation =>
                "Validated: Index is already UNIQUE and profiling confirms data integrity.",
            OpportunityCategory.Recommendation when disposition == OpportunityDisposition.ReadyToApply =>
                "Recommendation: Index qualifies for UNIQUE enforcement based on profiling evidence.",
            _ => disposition == OpportunityDisposition.NeedsRemediation
                ? "Remediate data before enforcing the unique index."
                : "Enforce the unique index automatically."
        };

        return Opportunity.Create(
            OpportunityType.UniqueIndex,
            "UNIQUE",
            summary,
            risk,
            evidence,
            index: new IndexCoordinate(entry.Entity.Schema, entry.Entity.PhysicalName, entry.Index.Name),
            disposition: disposition,
            category: category,
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
        var identity = ColumnIdentity.From(entry);
        return new OpportunityColumn(
            identity,
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
        var identity = ColumnIdentity.From(entity, attribute);
        return new OpportunityColumn(
            identity,
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
        var hasPhysicalConstraint = fkReality?.Reference.HasDatabaseConstraint ?? entry.Attribute.Reference.HasDatabaseConstraint;
        var hasOrphans = fkReality?.HasOrphan ?? false;

        var disposition = hasOrphans
            ? OpportunityDisposition.NeedsRemediation
            : OpportunityDisposition.ReadyToApply;

        var evidenceSummary = new OpportunityEvidenceSummary(
            RequiresRemediation: hasOrphans,
            EvidenceAvailable: fkReality is not null,
            DataClean: fkReality is null ? null : !fkReality.HasOrphan,
            HasDuplicates: null,
            HasOrphans: fkReality?.HasOrphan);
        var columns = ImmutableArray.Create(BuildColumnInsight(entry, null, null, fkReality));
        var rationales = decision.Rationales.IsDefault ? ImmutableArray<string>.Empty : decision.Rationales;

        var category = ClassifyForeignKeyOpportunity(decision, rationales, hasPhysicalConstraint);

        var summary = category switch
        {
            OpportunityCategory.Contradiction =>
                "DATA CONTRADICTION: Profiling found orphaned rows that violate referential integrity. Manual remediation required.",
            OpportunityCategory.Validation =>
                "Validated: Foreign key constraint already exists and profiling confirms referential integrity.",
            OpportunityCategory.Recommendation when disposition == OpportunityDisposition.ReadyToApply =>
                "Recommendation: Foreign key constraint can be safely created based on profiling evidence.",
            _ => "Create foreign key constraint."
        };

        var risk = category == OpportunityCategory.Contradiction
            ? ChangeRisk.High("Orphaned rows detected - remediation required before constraint creation.")
            : ChangeRisk.Low("Foreign key creation is safe to apply.");

        return Opportunity.Create(
            OpportunityType.ForeignKey,
            "FOREIGN KEY",
            summary,
            risk,
            evidence,
            column: entry.Coordinate,
            disposition: disposition,
            category: category,
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
        EntityLookup entityLookup)
    {
        if (!entry.Attribute.Reference.IsReference || entry.Attribute.Reference.TargetEntity is null)
        {
            return null;
        }

        return entityLookup.Find(entry.Attribute.Reference.TargetEntity.Value.Value);
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

    private sealed class OpportunityAccumulator
    {
        private readonly ImmutableArray<Opportunity>.Builder _opportunities = ImmutableArray.CreateBuilder<Opportunity>();
        private readonly Dictionary<OpportunityDisposition, int> _dispositionCounts = new();
        private readonly Dictionary<OpportunityCategory, int> _categoryCounts = new();
        private readonly Dictionary<OpportunityType, int> _typeCounts = new();
        private readonly Dictionary<RiskLevel, int> _riskCounts = new();

        public void Record(Opportunity opportunity)
        {
            _opportunities.Add(opportunity);
            Increment(_dispositionCounts, opportunity.Disposition);
            Increment(_categoryCounts, opportunity.Category);
            Increment(_typeCounts, opportunity.Type);
            Increment(_riskCounts, opportunity.Risk.Level);
        }

        public OpportunitiesReport BuildReport()
        {
            return new OpportunitiesReport(
                _opportunities.ToImmutable(),
                _dispositionCounts.ToImmutableDictionary(),
                _categoryCounts.ToImmutableDictionary(),
                _typeCounts.ToImmutableDictionary(),
                _riskCounts.ToImmutableDictionary(),
                DateTimeOffset.UtcNow);
        }
    }

    private sealed class OpportunityAnalysisContext
    {
        private OpportunityAnalysisContext(
            AttributeLookup attributes,
            EntityLookup entities,
            ColumnProfileLookup columnProfiles,
            UniqueCandidateLookup uniqueProfiles,
            CompositeUniqueProfileLookup compositeUniqueProfiles,
            ForeignKeyRealityLookup foreignKeys,
            UniqueIndexLookup uniqueIndexes)
        {
            Attributes = attributes;
            Entities = entities;
            ColumnProfiles = columnProfiles;
            UniqueProfiles = uniqueProfiles;
            CompositeUniqueProfiles = compositeUniqueProfiles;
            ForeignKeys = foreignKeys;
            UniqueIndexes = uniqueIndexes;
        }

        public AttributeLookup Attributes { get; }

        public EntityLookup Entities { get; }

        public ColumnProfileLookup ColumnProfiles { get; }

        public UniqueCandidateLookup UniqueProfiles { get; }

        public CompositeUniqueProfileLookup CompositeUniqueProfiles { get; }

        public ForeignKeyRealityLookup ForeignKeys { get; }

        public UniqueIndexLookup UniqueIndexes { get; }

        public static OpportunityAnalysisContext Create(OsmModel model, ProfileSnapshot profile)
        {
            return new OpportunityAnalysisContext(
                AttributeLookup.Create(model),
                EntityLookup.Create(model),
                ColumnProfileLookup.Create(profile),
                UniqueCandidateLookup.Create(profile),
                CompositeUniqueProfileLookup.Create(profile),
                ForeignKeyRealityLookup.Create(profile),
                UniqueIndexLookup.Create(model));
        }
    }

    private readonly struct AttributeLookup
    {
        private readonly IReadOnlyDictionary<ColumnCoordinate, EntityAttributeIndex.EntityAttributeIndexEntry> _entries;

        private AttributeLookup(IReadOnlyDictionary<ColumnCoordinate, EntityAttributeIndex.EntityAttributeIndexEntry> entries)
        {
            _entries = entries;
        }

        public static AttributeLookup Create(OsmModel model)
        {
            var attributeIndex = EntityAttributeIndex.Create(model);
            var map = attributeIndex.Entries
                .ToDictionary(static entry => entry.Coordinate, static entry => entry);
            return new AttributeLookup(map);
        }

        public bool TryGet(ColumnCoordinate coordinate, out EntityAttributeIndex.EntityAttributeIndexEntry entry)
            => _entries.TryGetValue(coordinate, out entry);
    }

    private readonly struct EntityLookup
    {
        private readonly IReadOnlyDictionary<string, EntityModel> _entities;

        private EntityLookup(IReadOnlyDictionary<string, EntityModel> entities)
        {
            _entities = entities;
        }

        public static EntityLookup Create(OsmModel model)
        {
            var map = new Dictionary<string, EntityModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in model.Modules.SelectMany(static module => module.Entities))
            {
                map.TryAdd(entity.LogicalName.Value, entity);
            }

            return new EntityLookup(map);
        }

        public EntityModel? Find(string logicalName)
            => _entities.TryGetValue(logicalName, out var entity) ? entity : null;
    }

    private readonly struct ColumnProfileLookup
    {
        private readonly IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> _profiles;

        private ColumnProfileLookup(IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> profiles)
        {
            _profiles = profiles;
        }

        public static ColumnProfileLookup Create(ProfileSnapshot profile)
        {
            var map = profile.Columns
                .ToDictionary(static column => ColumnCoordinate.From(column), static column => column);
            return new ColumnProfileLookup(map);
        }

        public ColumnProfile? Find(ColumnCoordinate coordinate)
            => _profiles.TryGetValue(coordinate, out var profile) ? profile : null;
    }

    private readonly struct UniqueCandidateLookup
    {
        private readonly IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> _profiles;

        private UniqueCandidateLookup(IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> profiles)
        {
            _profiles = profiles;
        }

        public static UniqueCandidateLookup Create(ProfileSnapshot profile)
        {
            var map = profile.UniqueCandidates
                .ToDictionary(static candidate => ColumnCoordinate.From(candidate), static candidate => candidate);
            return new UniqueCandidateLookup(map);
        }

        public UniqueCandidateProfile? Find(ColumnCoordinate coordinate)
            => _profiles.TryGetValue(coordinate, out var profile) ? profile : null;
    }

    private readonly struct CompositeUniqueProfileLookup
    {
        private readonly IReadOnlyDictionary<string, CompositeUniqueCandidateProfile> _profiles;

        private CompositeUniqueProfileLookup(IReadOnlyDictionary<string, CompositeUniqueCandidateProfile> profiles)
        {
            _profiles = profiles;
        }

        public static CompositeUniqueProfileLookup Create(ProfileSnapshot profile)
        {
            if (profile.CompositeUniqueCandidates.IsDefaultOrEmpty)
            {
                return new CompositeUniqueProfileLookup(new Dictionary<string, CompositeUniqueCandidateProfile>(StringComparer.OrdinalIgnoreCase));
            }

            var map = new Dictionary<string, CompositeUniqueCandidateProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in profile.CompositeUniqueCandidates)
            {
                var key = global::Osm.Validation.Tightening.UniqueIndexEvidenceKey.Create(
                    candidate.Schema.Value,
                    candidate.Table.Value,
                    candidate.Columns.Select(static c => c.Value));
                map[key] = candidate;
            }

            return new CompositeUniqueProfileLookup(map);
        }

        public bool TryGet(EntityModel entity, IReadOnlyList<ColumnCoordinate> columns, out CompositeUniqueCandidateProfile? profile)
        {
            var key = global::Osm.Validation.Tightening.UniqueIndexEvidenceKey.Create(
                entity.Schema.Value,
                entity.PhysicalName.Value,
                columns.Select(static c => c.Column.Value));

            if (_profiles.TryGetValue(key, out var resolved))
            {
                profile = resolved;
                return true;
            }

            profile = null;
            return false;
        }
    }

    private readonly struct ForeignKeyRealityLookup
    {
        private readonly IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> _realities;

        private ForeignKeyRealityLookup(IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> realities)
        {
            _realities = realities;
        }

        public static ForeignKeyRealityLookup Create(ProfileSnapshot profile)
        {
            var map = profile.ForeignKeys
                .ToDictionary(static fk => ColumnCoordinate.From(fk.Reference), static fk => fk);
            return new ForeignKeyRealityLookup(map);
        }

        public ForeignKeyReality? Find(ColumnCoordinate coordinate)
            => _realities.TryGetValue(coordinate, out var reality) ? reality : null;
    }

    private readonly struct UniqueIndexLookup
    {
        private readonly IReadOnlyDictionary<IndexCoordinate, (EntityModel Entity, IndexModel Index)> _indexes;

        private UniqueIndexLookup(IReadOnlyDictionary<IndexCoordinate, (EntityModel Entity, IndexModel Index)> indexes)
        {
            _indexes = indexes;
        }

        public static UniqueIndexLookup Create(OsmModel model)
        {
            var map = new Dictionary<IndexCoordinate, (EntityModel, IndexModel)>();
            foreach (var entity in model.Modules.SelectMany(static module => module.Entities))
            {
                foreach (var index in entity.Indexes.Where(static i => i.IsUnique))
                {
                    var coordinate = new IndexCoordinate(entity.Schema, entity.PhysicalName, index.Name);
                    map[coordinate] = (entity, index);
                }
            }

            return new UniqueIndexLookup(map);
        }

        public bool TryGet(IndexCoordinate coordinate, out (EntityModel Entity, IndexModel Index) entry)
            => _indexes.TryGetValue(coordinate, out entry);
    }
}
