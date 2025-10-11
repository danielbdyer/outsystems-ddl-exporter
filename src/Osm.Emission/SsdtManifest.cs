using System.Collections.Generic;
namespace Osm.Emission;

public sealed record SsdtManifest(
    IReadOnlyList<TableManifestEntry> Tables,
    SsdtManifestOptions Options,
    SsdtPolicySummary? PolicySummary,
    SsdtEmissionMetadata Emission,
    IReadOnlyList<PreRemediationManifestEntry> PreRemediation);

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
    IReadOnlyDictionary<string, int> ForeignKeyRationales);
