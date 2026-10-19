using System.Collections.Immutable;

namespace Osm.Pipeline.UatUsers.Verification;

/// <summary>
/// Result of SQL safety analysis for generated UPDATE scripts.
/// Verifies presence of NULL guards, idempotence patterns, and target sanity checks.
/// </summary>
public sealed record SqlSafetyVerificationResult(
    bool IsValid,
    bool HasNullGuards,
    bool HasTargetSanityCheck,
    bool HasIdempotenceGuard,
    ImmutableArray<string> MissingGuards,
    ImmutableArray<string> Warnings)
{
    /// <summary>
    /// Creates a successful verification result when all required guards are present.
    /// </summary>
    public static SqlSafetyVerificationResult Success() =>
        new(
            IsValid: true,
            HasNullGuards: true,
            HasTargetSanityCheck: true,
            HasIdempotenceGuard: true,
            MissingGuards: ImmutableArray<string>.Empty,
            Warnings: ImmutableArray<string>.Empty);

    /// <summary>
    /// Creates a failure result with identified missing guards.
    /// </summary>
    public static SqlSafetyVerificationResult Failure(
        bool hasNullGuards,
        bool hasTargetSanityCheck,
        bool hasIdempotenceGuard,
        ImmutableArray<string> missingGuards,
        ImmutableArray<string> warnings) =>
        new(
            IsValid: false,
            HasNullGuards: hasNullGuards,
            HasTargetSanityCheck: hasTargetSanityCheck,
            HasIdempotenceGuard: hasIdempotenceGuard,
            MissingGuards: missingGuards,
            Warnings: warnings);
}
