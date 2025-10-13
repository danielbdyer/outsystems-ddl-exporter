using System;
using FluentAssertions;
using Osm.Pipeline.RemapUsers;
using Xunit;

namespace Osm.Pipeline.Tests.RemapUsers;

public sealed class RemapUsersManifestTests
{
    [Fact]
    public void MatchesForCommit_ReturnsTrue_WhenParametersMatchWithinWindow()
    {
        var dryRunParameters = new RemapUsersRunParameters(
            "DEV",
            RemapUsersContext.ComputeConnectionFingerprint("Server=.;Database=DEV;Trusted_Connection=True;"),
            "/snapshots/dev",
            new[] { "email", "normalize-email" },
            RemapUsersPolicy.Reassign,
            IncludePii: false,
            RebuildMap: false,
            DryRun: true,
            UserTable: "dbo.ossys_User",
            BatchSize: 1000,
            CommandTimeoutSeconds: 600,
            Parallelism: 4,
            FallbackUserId: 42L).Normalize();

        var manifest = new RemapUsersRunManifest(dryRunParameters, DateTimeOffset.UtcNow.AddHours(-1));

        var commitParameters = dryRunParameters with { DryRun = false };

        var matches = manifest.MatchesForCommit(commitParameters, DateTimeOffset.UtcNow, TimeSpan.FromHours(24));

        matches.Should().BeTrue();
    }

    [Fact]
    public void MatchesForCommit_ReturnsFalse_WhenRulesDiffer()
    {
        var dryRunParameters = new RemapUsersRunParameters(
            "DEV",
            RemapUsersContext.ComputeConnectionFingerprint("Server=.;Database=DEV;Trusted_Connection=True;"),
            "/snapshots/dev",
            new[] { "email" },
            RemapUsersPolicy.Reassign,
            IncludePii: false,
            RebuildMap: false,
            DryRun: true,
            UserTable: "dbo.ossys_User",
            BatchSize: 1000,
            CommandTimeoutSeconds: 600,
            Parallelism: 4,
            FallbackUserId: null).Normalize();

        var manifest = new RemapUsersRunManifest(dryRunParameters, DateTimeOffset.UtcNow);

        var commitParameters = dryRunParameters with { MatchingRules = new[] { "email", "username" }, DryRun = false };

        var matches = manifest.MatchesForCommit(commitParameters, DateTimeOffset.UtcNow, TimeSpan.FromHours(24));

        matches.Should().BeFalse();
    }
}
