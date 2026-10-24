using System;
using System.IO;
using Osm.Pipeline.UatUsers;
using Xunit;

namespace Osm.Pipeline.Tests.UatUsers;

public sealed class UserInventoryLoaderTests
{
    [Fact]
    public void Load_ReadsRows()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "qa_users.csv");
        File.WriteAllLines(path, new[]
        {
            "Id,Username,EMail,Name,External_Id,Is_Active,Creation_Date,Last_Login",
            "100,qa-user,qa@example.com,QA User,,1,2024-01-01T00:00:00Z,2024-02-01T00:00:00Z",
            "200,qa-ops,ops@example.com,QA Ops,,0,,"
        });

        var result = UserInventoryLoader.Load(path);

        Assert.Equal(2, result.Records.Count);
        Assert.True(result.Records.ContainsKey(UserIdentifier.FromString("100")));
        Assert.Equal("qa-user", result.Records[UserIdentifier.FromString("100")].Username);
        Assert.Equal("qa-ops", result.Records[UserIdentifier.FromString("200")].Username);
    }

    [Fact]
    public void Load_ThrowsWhenFileMissing()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => UserInventoryLoader.Load("missing.csv"));
        Assert.Contains("missing.csv", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_ThrowsOnDuplicateIds()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "qa_users.csv");
        File.WriteAllLines(path, new[]
        {
            "Id,Username",
            "100,first",
            "100,second"
        });

        var ex = Assert.Throws<InvalidDataException>(() => UserInventoryLoader.Load(path));
        Assert.Contains("Duplicate user identifier", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qa-user-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // ignore cleanup issues
            }
        }
    }
}
