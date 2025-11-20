namespace Osm.Pipeline.UatUsers.Verification;

/// <summary>
/// Aggregates all UAT-users artifact verification results.
/// Provides overall validation status across map, catalog, SQL safety, and inventory compliance.
/// </summary>
public sealed record UatUsersVerificationContext(
    string ArtifactRoot,
    UserMapVerificationResult MapVerification,
    FkCatalogVerificationResult CatalogVerification,
    SqlSafetyVerificationResult SqlSafety,
    UatInventoryComplianceResult InventoryCompliance)
{
    /// <summary>
    /// Overall validation status.
    /// Returns true only if all individual verifications pass.
    /// </summary>
    public bool IsValid =>
        MapVerification.IsValid &&
        CatalogVerification.IsValid &&
        SqlSafety.IsValid &&
        InventoryCompliance.IsValid;

    /// <summary>
    /// Count of total discrepancies across all verifications.
    /// </summary>
    public int DiscrepancyCount =>
        MapVerification.DuplicateSources.Length +
        MapVerification.MissingSources.Length +
        MapVerification.InvalidTargets.Length +
        MapVerification.UnmappedCount +
        CatalogVerification.MissingColumns.Length +
        CatalogVerification.UnexpectedColumns.Length +
        SqlSafety.MissingGuards.Length +
        InventoryCompliance.MissingUatUsers.Length;
}
