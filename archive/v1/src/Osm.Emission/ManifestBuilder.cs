using System;
using System.Collections.Generic;
using Osm.Domain.Model.Artifacts;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Emission;

public sealed class ManifestBuilder
{
    public SsdtManifest Build(
        IReadOnlyList<TableArtifactSnapshot> tables,
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
                decisionReport.TogglePrecedence);

        var manifestEntries = BuildManifestEntries(tables);
        var preRemediationEntries = preRemediation ?? Array.Empty<PreRemediationManifestEntry>();
        var coverageSummary = coverage ?? SsdtCoverageSummary.CreateComplete(tableCount, columnCount, constraintCount);
        var unsupportedEntries = unsupported ?? Array.Empty<string>();

        return new SsdtManifest(
            manifestEntries,
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

    private static IReadOnlyList<TableManifestEntry> BuildManifestEntries(IReadOnlyList<TableArtifactSnapshot> tables)
    {
        if (tables.Count == 0)
        {
            return Array.Empty<TableManifestEntry>();
        }

        var entries = new List<TableManifestEntry>(tables.Count);
        for (var i = 0; i < tables.Count; i++)
        {
            var snapshot = tables[i];
            if (snapshot is null)
            {
                continue;
            }

            var emission = snapshot.Emission ?? throw new InvalidOperationException(
                $"Emission metadata missing for table '{snapshot.Identity.Schema}.{snapshot.Identity.Name}'.");

            if (string.IsNullOrWhiteSpace(emission.ManifestPath))
            {
                throw new InvalidOperationException(
                    $"Manifest path missing for table '{snapshot.Identity.Schema}.{snapshot.Identity.Name}'.");
            }

            entries.Add(new TableManifestEntry(
                snapshot.Identity.Module,
                snapshot.Identity.Schema,
                emission.TableName,
                emission.ManifestPath!,
                emission.IndexNames,
                emission.ForeignKeyNames,
                emission.IncludesExtendedProperties));
        }

        return entries;
    }
}
