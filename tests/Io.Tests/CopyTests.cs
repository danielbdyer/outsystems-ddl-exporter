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
/// A disposable copy (V3_MILESTONES.md WP 1.4, §2.2's rows for io/ScratchServer.cs and io/SqlServer.cs, law 3′): io/ScratchServer makes, registers and drops
/// it on the run's SQL Server; Publish writes to it and returns what DacFx deployed; DacFx.Extract reads it back into a package that
/// Ssdt.Elements reads; and the plan of a package against its own published copy, package to package, is empty. Model fingerprints are
/// compared only between like sources: a package's keys with its copy's, and one copy's fingerprint with another's.
/// </summary>
public sealed class CopyTests(GoldenProject project) : IClassFixture<GoldenProject>, IDisposable
{
    private readonly string root = SqlServerFixture.EstateRoot(Path.Combine(Repository.Root, ".estate", "copies-under-test", Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]));

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "O5")]
    [Trait("Value", "O12")]
    public async Task ScratchServer_names_a_copy_for_its_host_and_process_registers_it_and_Drop_removes_the_database_and_its_row()
    {
        var server = await SqlServerFixture.ServerAsync();
        var copy = Made(ScratchServer.Create(root, server));
        try
        {
            Assert.Equal((CopyName.Make(Environment.MachineName, 0, 0).Machine, Environment.ProcessId), (copy.Name.Machine, copy.Name.Pid));
            Assert.True(await SqlServerFixture.ExistsAsync(copy.Name.ToString()), copy.Name + " was not created");
            var row = Assert.Single(Registry())!.AsObject();
            Assert.Equal(["created", "host", "name", "pid", "server"], row.Select(p => p.Key).Order(StringComparer.Ordinal));
            Assert.Equal((copy.Name.ToString(), Environment.ProcessId, Made(ScratchServer.ServerName(server)).ToString()), ((string)row["name"]!, (int)row["pid"]!, (string)row["server"]!));
            var registry = File.ReadAllText(Path.Combine(root, ".estate", "copies.json"));
            Assert.All(new[] { new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(server).Password }.Where(password => password.Length > 0), password => Assert.DoesNotContain(password, registry, StringComparison.Ordinal));
            Assert.Equal(TimeSpan.Zero, DateTimeOffset.Parse((string)row["created"]!, System.Globalization.CultureInfo.InvariantCulture).Offset);
            Assert.Equal(copy.Name, Assert.IsType<SqlServer.Copy>(Made(SqlServer.Resolve(Made(SqlServer.Target("copy:" + copy.Name, "--target")), root))).Name);
        }
        finally
        {
            Made(ScratchServer.Drop(copy));
        }

        Assert.False(await SqlServerFixture.ExistsAsync(copy.Name.ToString()), copy.Name + " outlived Drop");
        Assert.Empty(Registry());
        var gone = Assert.IsType<Result<SqlServer.Database>.Failed>(SqlServer.Resolve(Made(SqlServer.Target("copy:" + copy.Name, "--target")), root)).Error;
        Assert.Equal(("copy.unregistered", 9), (gone.Code, Contract.Exit(gone)));
    }

    /// <summary>
    /// Law 3′ across sources: the golden project published to two copies models to the package's keys, both copies to one
    /// fingerprint, and the change from the package to its copy holds nothing of what make-mandatory changes, which the change from
    /// the package to the make-mandatory head does hold.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Law", "3′ the model is complete")]
    [Trait("Exit", "M1.4")]
    public async Task A_copy_published_from_a_package_models_to_the_package_s_keys_and_two_copies_of_it_to_one_fingerprint()
    {
        var strict = Made(PublishProfiles.Load(project.Profile));
        var (one, two) = (Made(ScratchServer.Create(root, await SqlServerFixture.ServerAsync())), Made(ScratchServer.Create(root, await SqlServerFixture.ServerAsync())));
        try
        {
            Made(one.Publish(project.Base, strict));
            Made(two.Publish(project.Base, strict));
            var (first, second) = (Extracted(one), Extracted(two));
            using var basePackage = Made(Ssdt.Open(project.Base));
            using var headPackage = Made(Ssdt.Open(project.Mandatory));
            var (packaged, head) = (Made(Ssdt.Elements(basePackage)), Made(Ssdt.Elements(headPackage)));
            var schema = SortedArray.Of(packaged.Elements.Where(e => e.Key.Type is not (Element.PreDeploymentScript or Element.PostDeploymentScript or Element.RefactorLogOperation)));

            Assert.Equal(schema.Select(e => e.Key), first.Select(e => e.Key));
            Assert.Equal(Fingerprint.Of(first), Fingerprint.Of(second));
            Assert.DoesNotContain(Made(Change.Between(schema, first, [])).Altered, MakesMandatory);
            Assert.Contains(Made(Change.Between(packaged.Elements, head.Elements, head.Renames)).Altered, MakesMandatory);
        }
        finally
        {
            Made(ScratchServer.Drop(one));
            Made(ScratchServer.Drop(two));
        }
    }

    /// <summary>
    /// §1 fact 4 through io: a copy matches its package when the deploy plan under the pipeline's profile, of the package against the copy
    /// extracted, is empty; the make-mandatory head's plan against the same copy alters one table, its data-loss check in the script.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Law", "3′ the model is complete")]
    [Trait("Exit", "M1.4")]
    public async Task The_plan_of_a_package_against_its_own_published_copy_is_empty_and_of_the_make_mandatory_head_is_not()
    {
        var strict = Made(PublishProfiles.Load(project.Profile));
        var copy = Made(ScratchServer.Create(root, await SqlServerFixture.ServerAsync()));
        try
        {
            Made(copy.Publish(project.Base, strict));
            using var extracted = Made(DacFx.Extract(copy));
            using var basePackage = Made(Ssdt.Open(project.Base));
            using var headPackage = Made(Ssdt.Open(project.Mandatory));

            var own = Made(DacFx.Plan(basePackage, extracted, copy.Catalog, strict, []));
            var head = Made(DacFx.Plan(headPackage, extracted, copy.Catalog, strict, []));

            Assert.True(own.Report.IsEmpty, "the plan of the package against its own copy has operations:\n" + string.Join('\n', own.Report.Operations));
            Assert.Equal(["Alter Table [dbo].[Customer]"], head.Report.Operations.Where(o => !o.Kind.IsConsequence).Select(o => o.Kind + " " + o.Key));
            Assert.Contains("ALTER COLUMN [Email] NVARCHAR (256) NOT NULL", head.Script, StringComparison.Ordinal);
            Assert.Contains("RAISERROR (N'Rows were detected", head.Script, StringComparison.Ordinal);
        }
        finally
        {
            Made(ScratchServer.Drop(copy));
        }
    }

    /// <summary>
    /// A publish returns what DacFx deployed, its report and its script: the foreign-key head over the base on a copy, under the copy's
    /// Permissive profile, creates one foreign key, which the seed's rows satisfy. The make-mandatory head cannot be the example: the seed's
    /// post-deployment MERGE sets Customer.Email to NULL on every publish, so its publish fails with Msg 515 under either profile.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_publish_returns_the_report_and_script_DacFx_deployed()
    {
        var strict = Made(PublishProfiles.Load(project.Profile));
        var copy = Made(ScratchServer.Create(root, await SqlServerFixture.ServerAsync()));
        try
        {
            Made(copy.Publish(project.Base, strict));
            using var head = Made(Ssdt.Open(project.ForeignKey));

            var published = Made(DacFx.Publish(copy, head, copy.Permissive(strict)));

            Assert.Equal(["Create ForeignKeyConstraint [dbo].[FK_Customer_Account_AccountId]"], published.Report.Operations.Where(o => !o.Kind.IsConsequence).Select(o => o.Kind + " " + o.Key));
            Assert.Contains("ADD CONSTRAINT [FK_Customer_Account_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Account] ([Id])", published.Script, StringComparison.Ordinal);
        }
        finally
        {
            Made(ScratchServer.Drop(copy));
        }
    }

    /// <summary>
    /// A failed DacServices.Publish through Copy.Publish and io/DacFx.Failed. The copy holds the seed's Customer rows, so the
    /// make-mandatory head's data-loss check (BlockOnPossibleDataLoss True in the pipeline's profile) raises Msg 50000 and DacFx throws
    /// DacServicesException with no SqlException inside. Its Message holds DacFx's errors (SQL72014 quoting Msg 50000, SQL72045), and
    /// its Messages adds informational entries of number 0 that Message leaves out: the pre-deployment script's PRINT output and "An
    /// error occurred while the batch was being executed.". The error is routed by the number inside SQL72014, which Message alone
    /// carries, and quotes Message only.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_publish_the_data_loss_check_stops_is_server_failed_by_Msg_50000_quoting_DacFx_s_errors_and_not_its_informational_messages()
    {
        var strict = Made(PublishProfiles.Load(project.Profile));
        var copy = Made(ScratchServer.Create(root, await SqlServerFixture.ServerAsync()));
        try
        {
            Made(copy.Publish(project.Base, strict));

            var error = Assert.IsType<Result<SqlServer.Copy>.Failed>(copy.Publish(project.Mandatory, strict)).Error;

            Assert.Equal(("server.failed", 4), (error.Code, Contract.Exit(error)));
            Assert.StartsWith("copy:" + copy.Name + " failed the statement: Msg 50000: ", error.Message, StringComparison.Ordinal);
            Assert.Contains("SQL72014", error.Message, StringComparison.Ordinal);
            Assert.Contains("Rows were detected", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("SQL0:", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("An error occurred while the batch was being executed.", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Made(ScratchServer.Drop(copy));
        }
    }

    /// <summary>
    /// DF-4: DacFx's own failure, with no SqlException inside. A package built for a newer platform (Sql180) than the SQL Server it is
    /// planned against (SQL Server 2022 in the container, an older release in the Windows runner's LocalDB), planned against a named
    /// environment's extracted package under the pipeline's profile (AllowIncompatiblePlatform False), is plan.platform at exit 6, naming
    /// both platforms; nothing SQL Server says is in it.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_package_for_a_newer_platform_planned_for_a_named_environment_fails_naming_both_platforms()
    {
        await using var database = await SqlServerFixture.RegisterAsync();
        File.WriteAllText(Path.Combine(root, "dev.connection"), database.ConnectionString);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path.Combine(root, "dev.connection"), UnixFileMode.UserRead | UnixFileMode.UserWrite);   // io/SqlServer refuses a connection file others can read
        }

        File.WriteAllText(Path.Combine(root, "estate", "posture.json"),
            "{ \"environments\": { \"dev\": { \"host\": \"localhost\", \"connection\": \"file:dev.connection\", \"profile\": \"estate/profiles/pipeline.publish.xml\" } } }");
        var dacpac = Path.Combine(root, "vnext.dacpac");
        using (var model = new TSqlModel(SqlServerVersion.Sql180, new TSqlModelOptions()))
        {
            model.AddObjects("CREATE TABLE dbo.Customer (Id INT NOT NULL);");
            DacPackageExtensions.BuildPackage(dacpac, model, new PackageMetadata());
        }

        var dev = Assert.IsType<SqlServer.EnvironmentDatabase>(Made(SqlServer.Resolve(Made(SqlServer.Target("env:dev", "--target")), root)));
        using var extracted = Made(DacFx.Extract(dev));
        using var vnext = Made(Ssdt.Open(dacpac));
        var error = Assert.IsType<Result<Plan>.Failed>(DacFx.Plan(vnext, extracted, dev.Catalog, Made(PublishProfiles.Load(project.Profile)), [])).Error;

        Assert.Equal(("plan.platform", 6), (error.Code, Contract.Exit(error)));
        // The platform of the server differs between the container and the Windows runner's LocalDB; a failure prints the whole message.
        Assert.True(error.Message.StartsWith("The package targets Sql180 and env:dev is Sql", StringComparison.Ordinal), error.Message);
        Assert.DoesNotContain("withheld", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A copy's elements, as io reads a database: extracted once.</summary>
    private static SortedArray<Element> Extracted(SqlServer.Copy copy)
    {
        using var package = Made(DacFx.Extract(copy));
        return Made(package.Elements).Elements;
    }

    private static bool MakesMandatory(Change.Alteration altered) =>
        altered.Key.ToString() == "Column [dbo].[Customer].[Email]" && altered.Properties.Any(p => p.Name == "Nullable");

    private JsonArray Registry() => File.Exists(Path.Combine(root, ".estate", "copies.json"))
        ? JsonNode.Parse(File.ReadAllText(Path.Combine(root, ".estate", "copies.json")))!["copies"]!.AsArray()
        : [];

    private static T Made<T>(Result<T> result) => result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));
}
