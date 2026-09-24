using System.Collections.Immutable;

namespace Osm.Pipeline.UatUsers.Verification;

/// <summary>
/// Result of transformation map validation.
/// Verifies map completeness, duplicate detection, and source/target consistency.
/// </summary>
public sealed record UserMapVerificationResult(
    bool IsValid,
    int OrphanCount,
    int MappedCount,
    int UnmappedCount,
    ImmutableArray<UserIdentifier> DuplicateSources,
    ImmutableArray<UserIdentifier> MissingSources,
    ImmutableArray<UserIdentifier> InvalidTargets)
{
    /// <summary>
    /// Creates a successful validation result with no discrepancies.
    /// </summary>
    public static UserMapVerificationResult Success(int orphanCount, int mappedCount) =>
        new(
            IsValid: true,
            OrphanCount: orphanCount,
            MappedCount: mappedCount,
            UnmappedCount: 0,
            DuplicateSources: ImmutableArray<UserIdentifier>.Empty,
            MissingSources: ImmutableArray<UserIdentifier>.Empty,
            InvalidTargets: ImmutableArray<UserIdentifier>.Empty);

    /// <summary>
    /// Creates a failure result with identified discrepancies.
    /// </summary>
    public static UserMapVerificationResult Failure(
        int orphanCount,
        int mappedCount,
        int unmappedCount,
        ImmutableArray<UserIdentifier> duplicateSources,
        ImmutableArray<UserIdentifier> missingSources,
        ImmutableArray<UserIdentifier> invalidTargets) =>
        new(
            IsValid: false,
            OrphanCount: orphanCount,
            MappedCount: mappedCount,
            UnmappedCount: unmappedCount,
            DuplicateSources: duplicateSources,
            MissingSources: missingSources,
            InvalidTargets: invalidTargets);
}
