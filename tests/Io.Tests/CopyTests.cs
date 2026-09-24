using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Estate.Budgets.Tests;
using Estate.Cli;
using Estate.Kernel;
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
    private readonly string root = Directory.CreateDirectory(Path.Combine(Repository.Root, ".estate", "copies-under-test", Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8])).FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Substrate_names_a_copy_for_its_host_and_process_registers_it_and_Drop_removes_the_database_and_its_row()
    {
        var copy = Made(Substrate.Create(root, await SqlServerFixture.ServerAsync()));
        try
        {
            Assert.Matches("^" + SqlServerFixture.DatabaseName(Environment.MachineName, Environment.ProcessId, "") + "[0-9a-f]{8}$", copy.Name);
            Assert.True(await SqlServerFixture.ExistsAsync(copy.Name), copy.Name + " was not created");
            var row = Assert.Single(Registry())!.AsObject();
            Assert.Equal(["created", "host", "name", "pid"], row.Select(p => p.Key).Order(StringComparer.Ordinal));
            Assert.Equal((copy.Name, Environment.ProcessId), ((string)row["name"]!, (int)row["pid"]!));
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

    private static bool MakesMandatory(Change.Altered altered) =>
        altered.Key.ToString() == "Column [dbo].[Customer].[Email]" && altered.Properties.Any(p => p.Name == "Nullable");

    private JsonArray Registry() => File.Exists(Path.Combine(root, ".estate", "copies.json"))
        ? JsonNode.Parse(File.ReadAllText(Path.Combine(root, ".estate", "copies.json")))!["copies"]!.AsArray()
        : [];

    private static T Made<T>(Result<T> result) => result.Match(value => value, refusal => throw new Xunit.Sdk.XunitException(refusal.Code + ": " + refusal.Message));
}
