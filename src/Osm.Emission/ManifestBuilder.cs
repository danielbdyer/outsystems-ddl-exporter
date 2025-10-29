using System;
using System.Collections.Generic;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Emission;

public sealed class ManifestBuilder
{
    public SsdtManifest Build(
        IReadOnlyList<TableManifestEntry> tables,
        SmoBuildOptions options,
        SsdtEmissionMetadata emission,
        PolicyDecisionReport? decisionReport,
        IReadOnlyList<PreRemediationManifestEntry>? preRemediation,
        SsdtCoverageSummary? coverage,
        SsdtPredicateCoverage? predicateCoverage,
        IReadOnlyList<string>? unsupported,
        int tableCount,
        int columnCount,
        int constraintCount)
    {
        if (tables is null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (emission is null)
        {
            throw new ArgumentNullException(nameof(emission));
        }

        var summary = decisionReport is null
            ? null
            : new SsdtPolicySummary(
                decisionReport.ColumnCount,
                decisionReport.TightenedColumnCount,
                decisionReport.RemediationColumnCount,
                decisionReport.UniqueIndexCount,
                decisionReport.UniqueIndexesEnforcedCount,
                decisionReport.UniqueIndexesRequireRemediationCount,
                decisionReport.ForeignKeyCount,
                decisionReport.ForeignKeysCreatedCount,
                decisionReport.ColumnRationaleCounts,
                decisionReport.UniqueIndexRationaleCounts,
                decisionReport.ForeignKeyRationaleCounts,
                decisionReport.ModuleRollups,
                decisionReport.Toggles.ToExportDictionary());

        var preRemediationEntries = preRemediation ?? Array.Empty<PreRemediationManifestEntry>();
        var coverageSummary = coverage ?? SsdtCoverageSummary.CreateComplete(tableCount, columnCount, constraintCount);
        var unsupportedEntries = unsupported ?? Array.Empty<string>();

        return new SsdtManifest(
            tables,
            new SsdtManifestOptions(
                options.IncludePlatformAutoIndexes,
                options.EmitBareTableOnly,
                options.SanitizeModuleNames,
                options.ModuleParallelism),
            summary,
            emission,
            preRemediationEntries,
            coverageSummary,
            predicateCoverage ?? SsdtPredicateCoverage.Empty,
            unsupportedEntries);
    }
}
