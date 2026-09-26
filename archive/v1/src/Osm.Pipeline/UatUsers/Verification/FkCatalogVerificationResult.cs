using System.Collections.Immutable;

namespace Osm.Pipeline.UatUsers.Verification;

/// <summary>
/// Result of FK catalog completeness verification.
/// Compares discovered FK columns against expected columns from model metadata.
/// </summary>
public sealed record FkCatalogVerificationResult(
    bool IsValid,
    int DiscoveredColumnCount,
    int ExpectedColumnCount,
    ImmutableArray<string> MissingColumns,
    ImmutableArray<string> UnexpectedColumns)
{
    /// <summary>
    /// Creates a successful verification result when all expected columns are discovered.
    /// </summary>
    public static FkCatalogVerificationResult Success(int discoveredColumnCount, int expectedColumnCount) =>
        new(
            IsValid: true,
            DiscoveredColumnCount: discoveredColumnCount,
            ExpectedColumnCount: expectedColumnCount,
            MissingColumns: ImmutableArray<string>.Empty,
            UnexpectedColumns: ImmutableArray<string>.Empty);

    /// <summary>
    /// Creates a failure result with identified missing or unexpected columns.
    /// </summary>
    public static FkCatalogVerificationResult Failure(
        int discoveredColumnCount,
        int expectedColumnCount,
        ImmutableArray<string> missingColumns,
        ImmutableArray<string> unexpectedColumns) =>
        new(
            IsValid: false,
            DiscoveredColumnCount: discoveredColumnCount,
            ExpectedColumnCount: expectedColumnCount,
            MissingColumns: missingColumns,
            UnexpectedColumns: unexpectedColumns);
}
