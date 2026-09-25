using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Xml.Linq;
using Estate.Budgets.Tests;
using Estate.Kernel;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// WP 1.5's Done-when on SQL Server, through WP 1.4's Copy.Publish: the pipeline's profile, given a TargetConnectionString that names
/// sentinel.invalid (a name no resolver answers, RFC 6761, so a publish that looked it up would fail) and a TargetDatabaseName of
/// another database, publishes the classic-minimal package to a copy io/Substrate made, Strict and then Permissive, which only the
/// copy makes; the package reaches the copy and nothing else.
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class SentinelTests(PublishedTool tool) : IDisposable
{
    private readonly string scratch = Path.Combine(Repository.Root, ".estate", "sentinel", Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_Permissive_publish_under_a_profile_naming_a_sentinel_server_reaches_the_copy_and_never_the_sentinel()
    {
        Telemetry.OptOut();   // before DacFx loads, as estate's Main does
        var elsewhere = "estate_sentinel_" + Guid.NewGuid().ToString("N")[..8];
        var strict = Made(Profiles.Load(Sentinel(elsewhere)));
        var dacpac = Made(Ssdt.Build(ClassicMinimal(), tool.Folder, Path.Combine(scratch, "build"))).Path;
        Assert.ThrowsAny<SocketException>(() => Dns.GetHostEntry("sentinel.invalid"));

        var copy = Made(Substrate.Create(SqlServerFixture.EstateRoot(scratch), await SqlServerFixture.ServerAsync()));
        try
        {
            var permissive = copy.Permissive(strict);
            foreach (var profile in (PublishProfile[])[strict, permissive])
            {
                Made(copy.Publish(dacpac, profile));
            }

            Assert.False(permissive.Options().BlockOnPossibleDataLoss);
            Assert.Equal(1, await SqlServerFixture.ScalarAsync(copy.Connection, "SELECT COUNT(*) FROM sys.tables WHERE SCHEMA_NAME(schema_id) = N'dbo' AND name = N'Customer';"));
            Assert.False(await SqlServerFixture.ExistsAsync(elsewhere), "the profile's TargetDatabaseName, " + elsewhere + ", was created");
        }
        finally
        {
            Made(Substrate.Drop(copy));
        }
    }

    /// <summary>The committed pipeline profile, given a target: a sentinel server and another database's name.</summary>
    private string Sentinel(string elsewhere)
    {
        var profile = XDocument.Load(Path.Combine(Repository.Root, "tests", "Golden", "proving-ground", "profiles", "pipeline.publish.xml"));
        var properties = profile.Root!.Elements().First(e => e.Name.LocalName == "PropertyGroup");
        properties.Add(
            new XElement(properties.Name.Namespace + "TargetConnectionString", "Data Source=sentinel.invalid;Initial Catalog=" + elsewhere + ";Integrated Security=True"),
            new XElement(properties.Name.Namespace + "TargetDatabaseName", elsewhere));
        Directory.CreateDirectory(scratch);
        var file = Path.Combine(scratch, "sentinel.publish.xml");
        profile.Save(file);
        return file;
    }

    /// <summary>A copy of the classic-minimal project with the corpus's stop files, so the engine's build settings stay out.</summary>
    private string ClassicMinimal()
    {
        var golden = Path.Combine(Repository.Root, "tests", "Golden");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(golden, "classic-minimal"), "*", SearchOption.AllDirectories)
            .Concat([Path.Combine(golden, "Directory.Build.props"), Path.Combine(golden, "Directory.Packages.props")])
            .Where(f => !Path.GetRelativePath(golden, f).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
        {
            var to = Path.Combine(scratch, "golden", Path.GetRelativePath(golden, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }

        return Path.Combine(scratch, "golden", "classic-minimal", "ClassicMinimal.sqlproj");
    }

    private static T Made<T>(Result<T> result) => result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));
}
