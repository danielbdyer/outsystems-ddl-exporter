using FluentAssertions;
using Osm.Pipeline.RemapUsers;
using Xunit;

namespace Osm.Pipeline.Tests.RemapUsers;

public sealed class RemapUsersRunHasherTests
{
    [Fact]
    public void ComputeHash_IgnoresDryRunFlag()
    {
        var dryRunParameters = new RemapUsersRunParameters(
            "DEV",
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

        var commitParameters = dryRunParameters with { DryRun = false };

        var dryRunHash = RemapUsersRunHasher.ComputeHash(dryRunParameters);
        var commitHash = RemapUsersRunHasher.ComputeHash(commitParameters);

        dryRunHash.Should().Be(commitHash);
    }
}
