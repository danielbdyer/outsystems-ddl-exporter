using System.Collections.Immutable;

namespace Osm.Pipeline.UatUsers.Verification;

/// <summary>
/// Result of UAT inventory compliance verification.
/// Verifies that all target user IDs referenced in transformation map exist in UAT inventory.
/// </summary>
public sealed record UatInventoryComplianceResult(
    bool IsValid,
    int UatInventoryCount,
    int ReferencedTargetCount,
    ImmutableArray<UserIdentifier> MissingUatUsers)
{
    /// <summary>
    /// Creates a successful compliance result when all targets exist in UAT inventory.
    /// </summary>
    public static UatInventoryComplianceResult Success(int uatInventoryCount, int referencedTargetCount) =>
        new(
            IsValid: true,
            UatInventoryCount: uatInventoryCount,
            ReferencedTargetCount: referencedTargetCount,
            MissingUatUsers: ImmutableArray<UserIdentifier>.Empty);

    /// <summary>
    /// Creates a failure result with identified missing UAT users.
    /// </summary>
    public static UatInventoryComplianceResult Failure(
        int uatInventoryCount,
        int referencedTargetCount,
        ImmutableArray<UserIdentifier> missingUatUsers) =>
        new(
            IsValid: false,
            UatInventoryCount: uatInventoryCount,
            ReferencedTargetCount: referencedTargetCount,
            MissingUatUsers: missingUatUsers);
}
