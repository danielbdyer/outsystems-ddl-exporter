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
            "SourceUserId,TargetUserId,Note",
            "100,200,Primary",
            "100,300,Duplicate should be ignored",
            "200,400,",
            "",
            "# comment",
            "300,500"
        });

        var entries = UserMapLoader.Load(path);

        Assert.Collection(entries,
            entry =>
            {
                Assert.Equal(100, entry.SourceUserId);
                Assert.Equal(200, entry.TargetUserId);
                Assert.Equal("Primary", entry.Note);
            },
            entry =>
            {
                Assert.Equal(200, entry.SourceUserId);
                Assert.Equal(400, entry.TargetUserId);
                Assert.Null(entry.Note);
            },
            entry =>
            {
                Assert.Equal(300, entry.SourceUserId);
                Assert.Equal(500, entry.TargetUserId);
                Assert.Null(entry.Note);
            });
    }
}
