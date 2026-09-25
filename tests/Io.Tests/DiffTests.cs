using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Estate.Cli;
using Estate.Kernel;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// estate read and estate diff (WP 1.7, V3_ARCHITECTURE.md §8.1 and §8.5) against a git repository holding the golden project at Base and
/// the make-mandatory sample change at Head: M1 exit 2, and each verb's JSON against its schema under cli/schemas/.
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class DiffTests(ScratchEstate estate) : IClassFixture<ScratchEstate>
{
    /// <summary>M1 exit 2: the diff of the make-mandatory sample change prints Customer.Email's Nullable true → false, and nothing else.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Diff_from_the_base_ref_to_the_make_mandatory_head_prints_Customer_Email_s_Nullable_true_to_false_and_nothing_else()
    {
        var (exit, output) = estate.Estate("diff", "--from", "ref:" + estate.Base, "--to", "ref:" + estate.Head);

        Assert.Equal(0, exit);
        Assert.Equal("Column [dbo].[Customer].[Email]: Nullable true → false\n", output);
    }

    /// <summary>
    /// M1 exit 2 on the executable, not in this test host: dist/estate/estate.dll as its own process, under its own runtime settings, prints
    /// the one line, and reads the package its build wrote with an answer valid against estate.read/1.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_published_estate_as_its_own_process_prints_the_make_mandatory_diff_and_reads_the_package()
    {
        var (exit, output) = estate.Tool.RunAt(estate.Root, "diff", "--from", "ref:" + estate.Base, "--to", "ref:" + estate.Head);

        Assert.True(exit == 0, output);
        Assert.Equal("Column [dbo].[Customer].[Email]: Nullable true → false\n", output);

        var dacpac = Built(estate.Head);
        var (readExit, read) = estate.Tool.RunAt(estate.Root, "read", "--from", "dacpac:" + dacpac, "--json");

        Assert.True(readExit == 0, read);
        var answer = JsonNode.Parse(read)!;
        ScratchEstate.Valid("estate.read.1.schema.json", answer);
        Assert.False((bool)Elements(answer).Single(e => (string?)e!["key"] == "Column [dbo].[Customer].[Email]")!["properties"]!["Nullable"]!);
    }

    /// <summary>
    /// The elements a read's answer holds: the golden project holds more than Render.Shown, so the answer is cut and names the run's
    /// answer.json, which holds them all; a smaller read holds them in the answer itself.
    /// </summary>
    private JsonArray Elements(JsonNode answer) =>
        ((string?)answer["full"] is { } full ? JsonNode.Parse(File.ReadAllText(Path.Combine(estate.Root, full)))! : answer)["read"]!["elements"]!.AsArray();

    /// <summary>The package a ref's build wrote: under .estate/build/, the commit's folder, then the folder the tool folder's build files name.</summary>
    private string Built(string commit) => Path.Combine(estate.Root, ".estate", "build", commit,
        GitTests.Ok(Ssdt.BuildTargets.Of(estate.Tool.Folder)).Fingerprint.ToString()[..16], "SampleCatalog.dacpac");

    /// <summary>
    /// VALUES.md X2 for a database read, the other half of PublishProfilesTests.No_output_contains_Password's search of every error: a
    /// registered database holding a SQL login and a user for it, read through estate read --from env:uat --json as the fixture's
    /// admin identity, who sees the login. What the test asserts is that the answer names the login and that no property in it is
    /// named after a member of <see cref="Ssdt.Secrets"/>: DacFx makes up a new Login.Password on each read, since SQL Server keeps
    /// only a hash of the one the login was made with, and Ssdt.Elements leaves that property out. The answer is also searched for a
    /// password setting (<see cref="PublishProfilesTests.PasswordSetting"/>): on the container the fixture's identity signs in as sa with a
    /// password, the env:uat connection file holds that connection string with its Password=, and the search fails if estate read
    /// printed it.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task Estate_read_of_a_database_holding_a_SQL_login_prints_no_password()
    {
        await using var database = await SqlServerFixture.RegisterAsync();
        var login = database.Name + ReadOnlyPrincipal.Suffix;   // the fixture drops the login of this name with the database
        await SqlServerFixture.ExecuteAsync(database.ConnectionString, "DECLARE @sql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@name) + N' WITH PASSWORD = N''"
            + "Pa55!planted#7f3a''; CREATE USER ' + QUOTENAME(@name) + N' FOR LOGIN ' + QUOTENAME(@name) + N';'; EXEC (@sql);", login);
        var connection = Path.Combine(Path.GetDirectoryName(estate.Root)!, database.Name + ".connection");
        File.WriteAllText(connection, database.ConnectionString);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(connection, UnixFileMode.UserRead | UnixFileMode.UserWrite);   // io/SqlServer refuses a connection file others can read
        }

        try
        {
            var named = estate.Named(("uat", connection));
            var (exit, output) = estate.EstateAt(named, "read", "--from", "env:uat", "--json");

            Assert.True(exit == 0, output);
            var answer = JsonNode.Parse(output)!;
            var whole = (string?)answer["full"] is { } full ? File.ReadAllText(Path.Combine(named, full)) : output;
            var elements = JsonNode.Parse(whole)!["read"]!["elements"]!.AsArray();
            Assert.Contains(elements, e => (string?)e!["key"] == "Login [" + login + "]");
            var secrets = Ssdt.Secrets.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
            Assert.DoesNotContain(elements.SelectMany(e => e!["properties"]!.AsObject().Select(p => (string?)e["key"] + " " + p.Key)), p => secrets.Contains(p.Split('.', ' ')[^1]));
            Assert.DoesNotMatch(PublishProfilesTests.PasswordSetting, output);
            Assert.DoesNotMatch(PublishProfilesTests.PasswordSetting, whole);
        }
        finally
        {
            File.Delete(connection);
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Diff_json_validates_against_estate_diff_1_and_carries_the_one_property_both_fingerprints_and_the_DacFx_release_and_claims_nothing()
    {
        var (exit, output) = estate.Estate("diff", "--from", "ref:" + estate.Base, "--to", "ref:" + estate.Head, "--json");

        var answer = JsonNode.Parse(output)!;
        ScratchEstate.Valid("estate.diff.1.schema.json", answer);
        Assert.Equal(0, exit);
        var altered = Assert.Single(answer["diff"]!["change"]!["altered"]!.AsArray())!;
        Assert.Equal("Column [dbo].[Customer].[Email]", (string?)altered["key"]);
        var property = Assert.Single(altered["properties"]!.AsArray())!;
        Assert.Equal(("Nullable", true, false), ((string?)property["name"], (bool)property["before"]!, (bool)property["after"]!));
        Assert.NotEqual((string?)answer["diff"]!["from"]!["fingerprint"], (string?)answer["diff"]!["to"]!["fingerprint"]);
        Assert.Equal(DacFx.Version.Match(v => v.ToString(), e => e.Message), (string?)answer["dacfx"]);
        Assert.Null(answer["provenance"]);
    }

    /// <summary>--fail-on-change makes a change exit 5 (the drift check in CI); an unchanged pair still exits 0 and says so.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Diff_with_fail_on_change_exits_5_on_a_change_and_0_on_none()
    {
        Assert.Equal(5, estate.Estate("diff", "--from", "ref:" + estate.Base, "--to", "ref:" + estate.Head, "--fail-on-change").Exit);

        var (exit, output) = estate.Estate("diff", "--from", "ref:" + estate.Base, "--to", "ref:" + estate.Base, "--fail-on-change");

        Assert.Equal(0, exit);
        Assert.Equal("No change from ref:" + estate.Base + " to ref:" + estate.Base + ".\n", output);
    }

    /// <summary>A ref and the package its build wrote read into one model and one fingerprint; read's JSON validates against estate.read/1.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Read_of_a_ref_and_of_the_package_its_build_wrote_fingerprint_alike_and_validate_against_estate_read_1()
    {
        var (exit, output) = estate.Estate("read", "--from", "ref:" + estate.Base, "--json");
        var dacpac = Built(estate.Base);
        var (packageExit, package) = estate.Estate("read", "--from", "dacpac:" + dacpac, "--json");

        var (fromRef, fromPackage) = (JsonNode.Parse(output)!, JsonNode.Parse(package)!);
        ScratchEstate.Valid("estate.read.1.schema.json", fromRef);
        ScratchEstate.Valid("estate.read.1.schema.json", fromPackage);
        Assert.Equal((0, 0), (exit, packageExit));
        using var loaded = GitTests.Ok(Ssdt.Open(dacpac));
        var elements = GitTests.Ok(Ssdt.Elements(loaded)).Elements;
        Assert.Equal("sha256:" + Fingerprint.Of(elements), (string?)fromRef["read"]!["fingerprint"]);
        Assert.Equal((string?)fromRef["read"]!["fingerprint"], (string?)fromPackage["read"]!["fingerprint"]);
        Assert.Equal((elements.Count, Render.Shown, true), ((int)fromRef["read"]!["count"]!, fromRef["read"]!["elements"]!.AsArray().Count, (bool)fromRef["truncated"]!));
        Assert.Equal(elements.Count, Elements(fromRef).Count);
        var email = Elements(fromRef).Single(e => (string?)e!["key"] == "Column [dbo].[Customer].[Email]")!;
        Assert.True((bool)email["properties"]!["Nullable"]!);
        Assert.StartsWith("ref:" + estate.Base + ": " + elements.Count + " elements, fingerprint sha256:", estate.Estate("read", "--from", "ref:" + estate.Base).Output, StringComparison.Ordinal);
    }

    /// <summary>Bad arguments are exit 1 and still validate against the verb's schema, with what it adds null.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("read", "estate.read.1.schema.json", new[] { "--from" })]
    [InlineData("read", "estate.read.1.schema.json", new[] { "--from", "sql:dev" })]
    [InlineData("diff", "estate.diff.1.schema.json", new[] { "--from", "ref:main" })]
    [InlineData("diff", "estate.diff.1.schema.json", new[] { "--from", "ref:main", "--to", "ref:main", "--from", "ref:main" })]
    public void A_verb_given_bad_arguments_exits_1_and_its_answer_validates_against_its_schema(string verb, string schema, string[] arguments)
    {
        var (exit, output) = estate.Estate([verb, .. arguments, "--json"]);

        var answer = JsonNode.Parse(output)!;
        ScratchEstate.Valid(schema, answer);
        Assert.Equal(1, exit);
        Assert.Null(answer[verb]);
    }
}
