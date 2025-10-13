using System;
using System.Collections.Generic;
using System.Linq;

namespace Osm.Pipeline.RemapUsers;

public sealed record RemapUsersRunParameters(
    string SourceEnvironment,
    string SnapshotPath,
    IReadOnlyList<string> MatchingRules,
    RemapUsersPolicy Policy,
    bool IncludePii,
    bool RebuildMap,
    bool DryRun,
    string UserTable,
    int BatchSize,
    int CommandTimeoutSeconds,
    int Parallelism,
    long? FallbackUserId)
{
    public RemapUsersRunParameters Normalize()
    {
        return this with
        {
            SourceEnvironment = SourceEnvironment.Trim(),
            SnapshotPath = SnapshotPath.Trim(),
            UserTable = UserTable.Trim(),
            MatchingRules = MatchingRules.Select(rule => rule.Trim()).ToArray()
        };
    }
}

public sealed record RemapUsersRunManifest(
    RemapUsersRunParameters Parameters,
    DateTimeOffset ExecutedAtUtc)
{
    public bool IsDryRun => Parameters.DryRun;

    public bool MatchesForCommit(RemapUsersRunParameters commitParameters, DateTimeOffset asOfUtc, TimeSpan maxAge)
    {
        if (!Parameters.DryRun || commitParameters.DryRun)
        {
            return false;
        }

        if (asOfUtc - ExecutedAtUtc > maxAge)
        {
            return false;
        }

        return string.Equals(Parameters.SourceEnvironment, commitParameters.SourceEnvironment, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Parameters.SnapshotPath, commitParameters.SnapshotPath, StringComparison.Ordinal)
            && string.Equals(Parameters.UserTable, commitParameters.UserTable, StringComparison.OrdinalIgnoreCase)
            && SequenceEquals(Parameters.MatchingRules, commitParameters.MatchingRules)
            && Parameters.Policy == commitParameters.Policy
            && Parameters.IncludePii == commitParameters.IncludePii
            && Parameters.RebuildMap == commitParameters.RebuildMap
            && Parameters.BatchSize == commitParameters.BatchSize
            && Parameters.CommandTimeoutSeconds == commitParameters.CommandTimeoutSeconds
            && Parameters.Parallelism == commitParameters.Parallelism
            && Parameters.FallbackUserId == commitParameters.FallbackUserId;
    }

    private static bool SequenceEquals(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
