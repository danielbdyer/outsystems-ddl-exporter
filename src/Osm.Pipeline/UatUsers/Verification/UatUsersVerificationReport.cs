using System;
using System.Collections.Immutable;

namespace Osm.Pipeline.UatUsers.Verification;

/// <summary>
/// Final verification report for UAT-users artifacts.
/// Provides machine-readable pass/fail status and detailed discrepancies.
/// </summary>
public sealed record UatUsersVerificationReport(
    string OverallStatus,
    DateTimeOffset Timestamp,
    UatUsersVerificationContext Context,
    ImmutableArray<string> Discrepancies,
    string ArtifactRoot)
{
    /// <summary>
    /// Creates a success report when all verifications pass.
    /// </summary>
    public static UatUsersVerificationReport Success(
        UatUsersVerificationContext context,
        string artifactRoot) =>
        new(
            OverallStatus: "PASS",
            Timestamp: DateTimeOffset.UtcNow,
            Context: context,
            Discrepancies: ImmutableArray<string>.Empty,
            ArtifactRoot: artifactRoot);

    /// <summary>
    /// Creates a failure report with detailed discrepancies.
    /// </summary>
    public static UatUsersVerificationReport Failure(
        UatUsersVerificationContext context,
        ImmutableArray<string> discrepancies,
        string artifactRoot) =>
        new(
            OverallStatus: "FAIL",
            Timestamp: DateTimeOffset.UtcNow,
            Context: context,
            Discrepancies: discrepancies,
            ArtifactRoot: artifactRoot);
}
