using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.Profiling.Insights;

namespace Osm.Pipeline.Profiling;

public interface IProfileInsightAnalyzer
{
    ProfileInsightReport Analyze(OsmModel model, ProfileSnapshot profile);
}

public sealed class ProfileInsightAnalyzer : IProfileInsightAnalyzer
{
    private const double HighNullRatioThreshold = 0.5;
    private const double LowNullRatioThreshold = 0.02;
    private const long MinimumRowCountForRatio = 20;

    public ProfileInsightReport Analyze(OsmModel model, ProfileSnapshot profile)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var insights = ImmutableArray.CreateBuilder<ProfileInsight>();
        var attributes = BuildAttributeLookup(model);

        AnalyzeNullPatterns(profile, attributes, insights);
        AnalyzeUniqueCandidates(profile, insights);
        AnalyzeForeignKeys(profile, insights);

        var reportResult = ProfileInsightReport.Create(insights.ToImmutable());
        if (reportResult.IsFailure)
        {
            throw new InvalidOperationException("Failed to construct profiling insight report.");
        }

        return reportResult.Value;
    }

    private static void AnalyzeNullPatterns(
        ProfileSnapshot profile,
        IReadOnlyDictionary<(string Schema, string Table, string Column), AttributeModel> attributes,
        ImmutableArray<ProfileInsight>.Builder insights)
    {
        foreach (var column in profile.Columns)
        {
            if (!attributes.TryGetValue((column.Schema.Value, column.Table.Value, column.Column.Value), out var attribute))
            {
                continue;
            }

            if (column.RowCount == 0)
            {
                continue;
            }

            var ratio = column.NullCount == 0
                ? 0d
                : (double)column.NullCount / column.RowCount;

            var anchor = ProfileInsightAnchor.Create(column.Schema, column.Table, column.Column);
            var metadata = new Dictionary<string, string?>
            {
                ["rowCount"] = column.RowCount.ToString(CultureInfo.InvariantCulture),
                ["nullCount"] = column.NullCount.ToString(CultureInfo.InvariantCulture),
                ["nullRatio"] = ratio.ToString("P2", CultureInfo.InvariantCulture),
                ["attributeIsMandatory"] = attribute.IsMandatory ? "true" : "false"
            };

            if (attribute.IsMandatory && column.NullCount > 0)
            {
                insights.Add(CreateInsight(
                    "profiling.nulls.mandatoryMismatch",
                    $"Mandatory attribute '{attribute.LogicalName.Value}' contains null values.",
                    "A logically mandatory attribute contains nulls in the profile snapshot, indicating upstream data quality drift.",
                    ProfileInsightSeverity.Risk,
                    anchor,
                    metadata));
                continue;
            }

            if (!attribute.IsMandatory && column.NullCount > 0 && column.RowCount >= MinimumRowCountForRatio)
            {
                if (ratio >= HighNullRatioThreshold)
                {
                    metadata["threshold"] = HighNullRatioThreshold.ToString("P0", CultureInfo.InvariantCulture);
                    insights.Add(CreateInsight(
                        "profiling.nulls.optional.high",
                        $"Optional attribute '{attribute.LogicalName.Value}' has frequent nulls.",
                        "A high proportion of rows are null for this optional attribute, suggesting the application seldom captures a value.",
                        ProfileInsightSeverity.Warning,
                        anchor,
                        metadata));
                }
                else if (ratio <= LowNullRatioThreshold)
                {
                    metadata["threshold"] = LowNullRatioThreshold.ToString("P2", CultureInfo.InvariantCulture);
                    insights.Add(CreateInsight(
                        "profiling.nulls.optional.tighten",
                        $"Optional attribute '{attribute.LogicalName.Value}' rarely has nulls.",
                        "Consider tightening this attribute to NOT NULL after addressing the remaining null rows.",
                        ProfileInsightSeverity.Recommendation,
                        anchor,
                        metadata));
                }
            }
        }
    }

    private static void AnalyzeUniqueCandidates(ProfileSnapshot profile, ImmutableArray<ProfileInsight>.Builder insights)
    {
        foreach (var candidate in profile.UniqueCandidates)
        {
            if (!candidate.HasDuplicate)
            {
                continue;
            }

            var anchor = ProfileInsightAnchor.Create(candidate.Schema, candidate.Table, candidate.Column);
            var metadata = new Dictionary<string, string?>
            {
                ["candidateType"] = "SingleColumn",
                ["column"] = candidate.Column.Value
            };

            insights.Add(CreateInsight(
                "profiling.unique.duplicate.single",
                $"Unique candidate '{candidate.Column.Value}' contains duplicates.",
                "Profiling detected duplicate values in a unique candidate column; tighten unique constraints only after remediation.",
                ProfileInsightSeverity.Warning,
                anchor,
                metadata));
        }

        foreach (var candidate in profile.CompositeUniqueCandidates)
        {
            if (!candidate.HasDuplicate)
            {
                continue;
            }

            var anchor = ProfileInsightAnchor.Create(candidate.Schema, candidate.Table, null, string.Join(",", candidate.Columns.Select(c => c.Value)));
            var metadata = new Dictionary<string, string?>
            {
                ["candidateType"] = "Composite",
                ["columns"] = string.Join(",", candidate.Columns.Select(c => c.Value))
            };

            insights.Add(CreateInsight(
                "profiling.unique.duplicate.composite",
                "Composite unique candidate contains duplicates.",
                "Profiling detected duplicate combinations within a composite unique candidate; remediate before enforcing uniqueness.",
                ProfileInsightSeverity.Warning,
                anchor,
                metadata));
        }
    }

    private static void AnalyzeForeignKeys(ProfileSnapshot profile, ImmutableArray<ProfileInsight>.Builder insights)
    {
        foreach (var foreignKey in profile.ForeignKeys)
        {
            var anchor = ProfileInsightAnchor.Create(
                foreignKey.Reference.FromSchema,
                foreignKey.Reference.FromTable,
                foreignKey.Reference.FromColumn,
                $"{foreignKey.Reference.FromTable.Value}->{foreignKey.Reference.ToTable.Value}");

            if (!foreignKey.Reference.HasDatabaseConstraint)
            {
                insights.Add(CreateInsight(
                    "profiling.foreignKey.missingConstraint",
                    "Foreign key is not enforced in the database.",
                    "The source model expects a relationship that is not backed by a database constraint.",
                    ProfileInsightSeverity.Warning,
                    anchor,
                    new Dictionary<string, string?>
                    {
                        ["targetSchema"] = foreignKey.Reference.ToSchema.Value,
                        ["targetTable"] = foreignKey.Reference.ToTable.Value,
                        ["targetColumn"] = foreignKey.Reference.ToColumn.Value
                    }));
            }

            if (foreignKey.IsNoCheck)
            {
                insights.Add(CreateInsight(
                    "profiling.foreignKey.noCheck",
                    "Foreign key is marked NOT TRUSTED.",
                    "The database reports this foreign key as NOT TRUSTED (WITH NOCHECK); re-validate and trust before tightening.",
                    ProfileInsightSeverity.Risk,
                    anchor,
                    new Dictionary<string, string?>
                    {
                        ["targetSchema"] = foreignKey.Reference.ToSchema.Value,
                        ["targetTable"] = foreignKey.Reference.ToTable.Value,
                        ["targetColumn"] = foreignKey.Reference.ToColumn.Value
                    }));
            }

            if (foreignKey.HasOrphan)
            {
                insights.Add(CreateInsight(
                    "profiling.foreignKey.orphans",
                    "Foreign key has orphan rows.",
                    "Profiling detected orphaned rows violating the relationship; clean the data before enabling WITH CHECK.",
                    ProfileInsightSeverity.Risk,
                    anchor,
                    new Dictionary<string, string?>
                    {
                        ["targetSchema"] = foreignKey.Reference.ToSchema.Value,
                        ["targetTable"] = foreignKey.Reference.ToTable.Value,
                        ["targetColumn"] = foreignKey.Reference.ToColumn.Value
                    }));
            }
        }
    }

    private static Dictionary<(string Schema, string Table, string Column), AttributeModel> BuildAttributeLookup(OsmModel model)
    {
        var dictionary = new Dictionary<(string Schema, string Table, string Column), AttributeModel>();

        foreach (var module in model.Modules)
        {
            foreach (var entity in module.Entities)
            {
                foreach (var attribute in entity.Attributes)
                {
                    var key = (entity.Schema.Value, entity.PhysicalName.Value, attribute.ColumnName.Value);
                    dictionary[key] = attribute;
                }
            }
        }

        return dictionary;
    }

    private static ProfileInsight CreateInsight(
        string code,
        string title,
        string detail,
        ProfileInsightSeverity severity,
        ProfileInsightAnchor anchor,
        IEnumerable<KeyValuePair<string, string?>> metadata)
    {
        var result = ProfileInsight.Create(code, title, detail, severity, anchor, metadata);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to build insight '{code}': {string.Join(",", result.Errors.Select(error => error.Code))}");
        }

        return result.Value;
    }
}
