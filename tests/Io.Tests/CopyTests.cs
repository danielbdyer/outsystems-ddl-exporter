using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Estate.Budgets.Tests;
using Estate.Cli;
using Estate.Kernel;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Contract = Estate.Cli.Contract;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// A disposable copy (V3_MILESTONES.md WP 1.4, §2.2's Substrate and SqlServer rows, law 3′): io/Substrate makes, registers and drops
/// it on the run's SQL Server; Publish writes to it; Model reads it back through LoadFromDatabase and the walk; and Plan of a package
/// against its own published copy is empty. Walk fingerprints are compared only between like sources: a package's keys with its
/// copy's, and one copy's fingerprint with another's.
/// </summary>
public sealed class CopyTests(ProvingGround ground) : IClassFixture<ProvingGround>, IDisposable
{
    private readonly string root = SqlServerFixture.EstateRoot(Path.Combine(Repository.Root, ".estate", "copies-under-test", Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]));

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Substrate_names_a_copy_for_its_host_and_process_registers_it_and_Drop_removes_the_database_and_its_row()
    {
        var server = await SqlServerFixture.ServerAsync();
        var copy = Made(Substrate.Create(root, server));
        try
        {
            Assert.Matches("^" + SqlServerFixture.DatabaseName(Environment.MachineName, Environment.ProcessId, "") + "[0-9a-f]{8}$", copy.Name);
            Assert.True(await SqlServerFixture.ExistsAsync(copy.Name), copy.Name + " was not created");
            var row = Assert.Single(Registry())!.AsObject();
            Assert.Equal(["created", "host", "name", "pid", "server"], row.Select(p => p.Key).Order(StringComparer.Ordinal));
            Assert.Equal((copy.Name, Environment.ProcessId, Made(Substrate.ServerName(server))), ((string)row["name"]!, (int)row["pid"]!, (string)row["server"]!));
            var registry = File.ReadAllText(Path.Combine(root, ".estate", "copies.json"));
            Assert.All(new[] { new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(server).Password }.Where(password => password.Length > 0), password => Assert.DoesNotContain(password, registry, StringComparison.Ordinal));
            Assert.Equal(TimeSpan.Zero, DateTimeOffset.Parse((string)row["created"]!, System.Globalization.CultureInfo.InvariantCulture).Offset);
            Assert.Equal(copy.Name, Assert.IsType<SqlServer.Copy>(Made(SqlServer.Resolve(Made(SqlServer.Target.Parse("copy:" + copy.Name)), root))).Name);
        }
        finally
        {
            Made(Substrate.Drop(copy));
        }

        Assert.False(await SqlServerFixture.ExistsAsync(copy.Name), copy.Name + " outlived Drop");
        Assert.Empty(Registry());
        var gone = Assert.IsType<Result<SqlServer.Database>.Refused>(SqlServer.Resolve(Made(SqlServer.Target.Parse("copy:" + copy.Name)), root)).Refusal;
        Assert.Equal(("copy.unregistered", 9), (gone.Code, Contract.Exit(gone)));
    }

    /// <summary>
    /// Law 3′ across sources: the golden project published to two copies models to the package's keys, both copies to one
    /// fingerprint, and the change from the package to its copy holds nothing of what make-mandatory changes, which the change from
    /// the package to the make-mandatory head does hold.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Law", "3′ the read is complete")]
    public async Task A_copy_published_from_a_package_models_to_the_package_s_keys_and_two_copies_of_it_to_one_fingerprint()
    {
        var strict = Made(Profiles.Load(ground.Profile));
        var (one, two) = (Made(Substrate.Create(root, await SqlServerFixture.ServerAsync())), Made(Substrate.Create(root, await SqlServerFixture.ServerAsync())));
        try
        {
            Made(one.Publish(ground.Base, strict));
            Made(two.Publish(ground.Base, strict));
            var (first, second) = (Made(SqlServer.Model(one)), Made(SqlServer.Model(two)));
            using var basePackage = Made(Ssdt.Load(ground.Base));
            using var headPackage = Made(Ssdt.Load(ground.Mandatory));
            var (packaged, head) = (Made(Ssdt.Walk(basePackage)), Made(Ssdt.Walk(headPackage)));
            var schema = Seq.Of(packaged.Elements.Where(e => e.Key.Type is not (Element.PreDeploymentScript or Element.PostDeploymentScript or Element.RefactorLogOperation)));

            Assert.Equal(schema.Select(e => e.Key), first.Select(e => e.Key));
            Assert.Equal(Fingerprint.Of(first), Fingerprint.Of(second));
            Assert.DoesNotContain(Made(Change.Between(schema, first, [])).Changed, MakesMandatory);
            Assert.Contains(Made(Change.Between(packaged.Elements, head.Elements, head.Renames)).Changed, MakesMandatory);
        }
        finally
        {
            Made(Substrate.Drop(one));
            Made(Substrate.Drop(two));
        }
    }

    /// <summary>§1 fact 4 through io: the convergence oracle is an empty plan under the pipeline's profile; the make-mandatory head's plan against the same copy is one Alter, its guard in the script.</summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Law", "3′ the read is complete")]
    public async Task The_plan_of_a_package_against_its_own_published_copy_is_empty_and_of_the_make_mandatory_head_is_not()
    {
        var strict = Made(Profiles.Load(ground.Profile));
        var copy = Made(Substrate.Create(root, await SqlServerFixture.ServerAsync()));
        try
        {
            Made(copy.Publish(ground.Base, strict));

            var own = Made(SqlServer.Plan(ground.Base, copy, strict));
            var head = Made(SqlServer.Plan(ground.Mandatory, copy, strict));

            Assert.True(own.IsEmpty, "the plan of the package against its own copy has operations:\n" + own.Report);
            Assert.Equal(1, head.Operations);
            Assert.Contains("ALTER COLUMN [Email] NVARCHAR (256) NOT NULL", head.Script, StringComparison.Ordinal);
            Assert.Contains("RAISERROR (N'Rows were detected", head.Script, StringComparison.Ordinal);
        }
        finally
        {
            Made(Substrate.Drop(copy));
        }
    }

    /// <summary>
    /// A failed DacServices.Publish through Copy.Publish and Database.Refused. The copy holds the seed's Customer rows, so the
    /// make-mandatory head's guard (BlockOnPossibleDataLoss True in the pipeline's profile) raises Msg 50000 and DacFx throws
    /// DacServicesException with no SqlException inside. Its Message holds DacFx's errors (SQL72014 quoting Msg 50000, SQL72045), and
    /// its Messages adds informational entries of number 0 that Message leaves out: the pre-deployment script's PRINT output and "An
    /// error occurred while the batch was being executed.". The refusal is routed by the number inside SQL72014, which Message alone
    /// carries, and quotes Message only.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_publish_the_guard_stops_is_refused_as_server_failed_by_Msg_50000_quoting_DacFx_s_errors_and_not_its_informational_messages()
    {
        var strict = Made(Profiles.Load(ground.Profile));
        var copy = Made(Substrate.Create(root, await SqlServerFixture.ServerAsync()));
        try
        {
            Made(copy.Publish(ground.Base, strict));

            var refused = Assert.IsType<Result<SqlServer.Copy>.Refused>(copy.Publish(ground.Mandatory, strict)).Refusal;

            Assert.Equal(("server.failed", 4), (refused.Code, Contract.Exit(refused)));
            Assert.StartsWith("copy:" + copy.Name + " failed the statement: Msg 50000: ", refused.Message, StringComparison.Ordinal);
            Assert.Contains("SQL72014", refused.Message, StringComparison.Ordinal);
            Assert.Contains("Rows were detected", refused.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("SQL0:", refused.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("An error occurred while the batch was being executed.", refused.Message, StringComparison.Ordinal);
        }
        finally
        {
            Made(Substrate.Drop(copy));
        }
    }

    /// <summary>
    /// DF-4: DacFx's own failure, with no SqlException inside. A package built for a newer platform than the 2022 substrate (Sql180),
    /// planned for a named environment on it under the pipeline's profile (AllowIncompatiblePlatform False), is refused as dacfx.failed
    /// at exit 6, and the refusal quotes DacFx's reason; SQL Server's messages alone are withheld for a named environment.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_package_for_a_newer_platform_planned_for_a_named_environment_is_refused_with_DacFx_s_reason()
    {
        await using var database = await SqlServerFixture.RegisterAsync();
        File.WriteAllText(Path.Combine(root, "dev.connection"), database.ConnectionString);
        File.WriteAllText(Path.Combine(root, "estate", "posture.json"),
            "{ \"environments\": { \"dev\": { \"connection\": \"file:dev.connection\", \"profile\": \"estate/profiles/pipeline.publish.xml\" } } }");
        var dacpac = Path.Combine(root, "vnext.dacpac");
        using (var model = new TSqlModel(SqlServerVersion.Sql180, new TSqlModelOptions()))
        {
            model.AddObjects("CREATE TABLE dbo.Customer (Id INT NOT NULL);");
            DacPackageExtensions.BuildPackage(dacpac, model, new PackageMetadata());
        }

        var dev = Assert.IsType<SqlServer.Named>(Made(SqlServer.Resolve(Made(SqlServer.Target.Parse("env:dev")), root)));
        var refused = Assert.IsType<Result<SqlServer.Deployment>.Refused>(SqlServer.Plan(dacpac, dev, Made(Profiles.Load(ground.Profile)))).Refusal;

        Assert.Equal(("dacfx.failed", 6), (refused.Code, Contract.Exit(refused)));
        Assert.Contains("cannot be published to SQL Server 2022", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("withheld", refused.Message, StringComparison.Ordinal);
    }

    private static bool MakesMandatory(Change.Altered altered) =>
        altered.Key.ToString() == "Column [dbo].[Customer].[Email]" && altered.Properties.Any(p => p.Name == "Nullable");

    private JsonArray Registry() => File.Exists(Path.Combine(root, ".estate", "copies.json"))
        ? JsonNode.Parse(File.ReadAllText(Path.Combine(root, ".estate", "copies.json")))!["copies"]!.AsArray()
        : [];

    private static T Made<T>(Result<T> result) => result.Match(value => value, refusal => throw new Xunit.Sdk.XunitException(refusal.Code + ": " + refusal.Message));
}
