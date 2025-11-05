using System.IO;
using Osm.Pipeline.UatUsers;
using Xunit;

namespace Osm.Pipeline.Tests.UatUsers;

public sealed class UserMapLoaderTests
{
    [Fact]
    public void ParsesCsvAndDeduplicates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "uat-users-tests", "maps");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "map.csv");
        File.WriteAllLines(path, new[]
        {
            "SourceUserId,TargetUserId,Rationale",
            "100,200,Primary",
            "100,300,Duplicate should be ignored",
            "200,400,",
            "400,,Pending",
            "",
            "# comment",
            "300,500"
        });

        var entries = UserMapLoader.Load(path);

        Assert.Collection(entries,
            entry =>
            {
                Assert.Equal(UserIdentifier.FromString("100"), entry.SourceUserId);
                Assert.True(entry.TargetUserId.HasValue);
                Assert.Equal(UserIdentifier.FromString("200"), entry.TargetUserId.Value);
                Assert.Equal("Primary", entry.Rationale);
            },
            entry =>
            {
                Assert.Equal(UserIdentifier.FromString("200"), entry.SourceUserId);
                Assert.True(entry.TargetUserId.HasValue);
                Assert.Equal(UserIdentifier.FromString("400"), entry.TargetUserId.Value);
                Assert.Null(entry.Rationale);
            },
            entry =>
            {
                Assert.Equal(UserIdentifier.FromString("300"), entry.SourceUserId);
                Assert.True(entry.TargetUserId.HasValue);
                Assert.Equal(UserIdentifier.FromString("500"), entry.TargetUserId.Value);
                Assert.Null(entry.Rationale);
            },
            entry =>
            {
                Assert.Equal(UserIdentifier.FromString("400"), entry.SourceUserId);
                Assert.Null(entry.TargetUserId);
                Assert.Equal("Pending", entry.Rationale);
            });
    }
}
