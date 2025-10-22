using System;
using System.Collections.Generic;
using Osm.Validation.Tightening;
namespace Osm.Emission;

public sealed record SsdtManifest(
    IReadOnlyList<TableManifestEntry> Tables,
    SsdtManifestOptions Options,
    SsdtPolicySummary? PolicySummary,
    SsdtEmissionMetadata Emission,
    IReadOnlyList<PreRemediationManifestEntry> PreRemediation,
    SsdtCoverageSummary Coverage,
    SsdtPredicateCoverage PredicateCoverage,
    IReadOnlyList<string> Unsupported);

public sealed record TableManifestEntry(
    string Module,
    string Schema,
    string Table,
    string TableFile,
    IReadOnlyList<string> Indexes,
    IReadOnlyList<string> ForeignKeys,
    bool IncludesExtendedProperties);

public sealed record SsdtManifestOptions(
    bool IncludePlatformAutoIndexes,
    bool EmitBareTableOnly,
    bool SanitizeModuleNames,
    int ModuleParallelism);

public sealed record SsdtEmissionMetadata(string Algorithm, string Hash);

public sealed record PreRemediationManifestEntry(
    string Module,
    string Table,
    string TableFile,
    string Hash);

public sealed record SsdtPolicySummary(
    int ColumnCount,
    int TightenedColumnCount,
    int RemediationColumnCount,
    int UniqueIndexCount,
    int UniqueIndexesEnforcedCount,
    int UniqueIndexesRequireRemediationCount,
    int ForeignKeyCount,
    int ForeignKeysCreatedCount,
    IReadOnlyDictionary<string, int> ColumnRationales,
    IReadOnlyDictionary<string, int> UniqueIndexRationales,
    IReadOnlyDictionary<string, int> ForeignKeyRationales,
    IReadOnlyDictionary<string, ModuleDecisionRollup> ModuleRollups,
    IReadOnlyDictionary<string, ToggleExportValue> TogglePrecedence);

public sealed record SsdtCoverageSummary(
    CoverageBreakdown Tables,
    CoverageBreakdown Columns,
    CoverageBreakdown Constraints)
{
    public static SsdtCoverageSummary CreateComplete(int tables, int columns, int constraints)
    {
        return new SsdtCoverageSummary(
            CoverageBreakdown.Create(tables, tables),
            CoverageBreakdown.Create(columns, columns),
            CoverageBreakdown.Create(constraints, constraints));
    }
}

public sealed record CoverageBreakdown(int Emitted, int Total, decimal Percentage)
{
    public static CoverageBreakdown Create(int emitted, int total)
    {
        var percentage = ComputePercentage(emitted, total);
        return new CoverageBreakdown(emitted, total, percentage);
    }

    private static decimal ComputePercentage(int emitted, int total)
    {
        if (total <= 0)
        {
            return 100m;
        }

        if (emitted <= 0)
        {
            return 0m;
        }

        var value = (decimal)emitted / total * 100m;
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
