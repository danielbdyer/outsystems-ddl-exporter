using System;

namespace Osm.Pipeline.Orchestration;

public sealed record DataIntegrityVerificationReport(
    string OverallStatus,
    DateTimeOffset VerificationTimestampUtc,
    BasicIntegrityCheckResult BaseVerification,
    object? UatUsersVerification = null);
