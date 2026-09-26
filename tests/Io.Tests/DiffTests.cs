using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Cli;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// dbchange read and dbchange diff (WP 1.7, V3_ARCHITECTURE.md §8.1 and §8.5) against a git repository holding the golden project at Base and
/// the make-mandatory sample change at Head: M1 exit 2, and each verb's JSON against its schema under cli/schemas/.
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class DiffTests(ScratchRepository repository) : IClassFixture<ScratchRepository>
{
    /// <summary>M1 exit 2: the diff of the make-mandatory sample change prints Customer.Email's Nullable true → false, and nothing else.</summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Exit", "M1.2")]
    public void Diff_from_the_base_ref_to_the_make_mandatory_head_prints_Customer_Email_s_Nullable_true_to_false_and_nothing_else()
    {
        var (exit, output) = repository.Run("diff", "--from", "ref:" + repository.Base, "--to", "ref:" + repository.Head);

        Assert.Equal(0, exit);
        Assert.Equal("Column [dbo].[Customer].[Email]: Nullable true → false\n", output);
    }

    /// <summary>
    /// M1 exit 2 on the executable, not in this test host: dist/dbchange/dbchange.dll as its own process, under its own runtime settings, prints
    /// the one line, and reads the package its build wrote with an answer valid against dbchange.read/1.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Exit", "M1.2")]
    public void The_published_dbchange_as_its_own_process_prints_the_make_mandatory_diff_and_reads_the_package()
    {
        var (exit, output) = repository.Tool.RunAt(repository.Root, "diff", "--from", "ref:" + repository.Base, "--to", "ref:" + repository.Head);

        Assert.True(exit == 0, output);
        Assert.Equal("Column [dbo].[Customer].[Email]: Nullable true → false\n", output);

        var dacpac = Built(repository.Head);
        var (readExit, read) = repository.Tool.RunAt(repository.Root, "read", "--from", "dacpac:" + dacpac, "--json");

        Assert.True(readExit == 0, read);
        var answer = JsonNode.Parse(read)!;
        VerbAnswer.Valid("dbchange.read.1.schema.json", answer);
        Assert.False((bool)Elements(answer).Single(e => (string?)e!["key"] == "Column [dbo].[Customer].[Email]")!["properties"]!["Nullable"]!);
    }

    /// <summary>
    /// The elements a read's answer holds: the golden project holds more than Render.Shown, so the answer is cut and names the run's
    /// answer.json, which holds them all; a smaller read holds them in the answer itself.
    /// </summary>
    private JsonArray Elements(JsonNode answer) =>
        ((string?)answer["full"] is { } full ? JsonNode.Parse(File.ReadAllText(Path.Combine(repository.Root, full)))! : answer)["read"]!["elements"]!.AsArray();

    /// <summary>The package a ref's build wrote: under .dbchange/build/, the commit's folder, then the folder the tool folder's build files name.</summary>
    private string Built(string commit) => Path.Combine(repository.Root, ".dbchange", "build", commit,
        GitTests.Ok(Ssdt.BuildTargets.Of(repository.Tool.Folder)).Fingerprint.ToString()[..16], "SampleCatalog.dacpac");

    /// <summary>
    /// VALUES.md X2 for a database read, the other half of PublishProfilesTests.No_output_contains_Password's search of every error: a
    /// registered database holding a SQL login and a user for it, read through dbchange read --from env:uat --json as the fixture's
    /// admin identity, who sees the login. What the test asserts is that the answer names the login and that no property in it is
    /// named after a member of <see cref="Ssdt.Secrets"/>: DacFx makes up a new Login.Password on each read, since SQL Server keeps
    /// only a hash of the one the login was made with, and Ssdt.ReadModel leaves that property out. The answer is also searched for a
    /// password setting (<see cref="PublishProfilesTests.PasswordSetting"/>): on the container the fixture's identity signs in as sa with a
    /// password, the env:uat connection file holds that connection string with its Password=, and the search fails if dbchange read
    /// printed it.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "X2")]
    public async Task DbChange_read_of_a_database_holding_a_SQL_login_prints_no_password()
    {
        await using var database = await SqlServerFixture.RegisterAsync();
        var login = database.Name + ReadOnlyPrincipal.Suffix;   // the fixture drops the login of this name with the database
        await SqlServerFixture.ExecuteAsync(database.ConnectionString, "DECLARE @sql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@name) + N' WITH PASSWORD = N''"
            + "Pa55!planted#7f3a''; CREATE USER ' + QUOTENAME(@name) + N' FOR LOGIN ' + QUOTENAME(@name) + N';'; EXEC (@sql);", login);
        var connection = Path.Combine(Path.GetDirectoryName(repository.Root)!, database.Name + ".connection");
        File.WriteAllText(connection, database.ConnectionString);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(connection, UnixFileMode.UserRead | UnixFileMode.UserWrite);   // io/SqlServer refuses a connection file others can read
        }

        try
        {
            var named = repository.Named(("uat", connection));
            var (exit, output) = repository.RunAt(named, "read", "--from", "env:uat", "--json");

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

    /// <summary>
    /// The identity that reads a database sets what its model holds: SQL Server hides the logins users map to from an identity without
    /// VIEW ANY DEFINITION on the server, all but its own (measured). The read-only principal, which holds VIEW DEFINITION on its database alone, reads env:uat with the
    /// note read.database-scope and without the login another user of the database maps to; the fixture's admin identity reads the same
    /// database with no such note, and names that login.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_read_of_a_database_scoped_identity_carries_the_scope_note_and_an_admin_read_does_not()
    {
        await using var database = await SqlServerFixture.RegisterAsync();
        var reader = await ReadOnlyPrincipal.CreateAsync(database);
        var other = database.Name + "_other";
        await SqlServerFixture.ExecuteAsync(database.ConnectionString, "DECLARE @sql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@name) + N' WITH PASSWORD = N''Other!"
            + Guid.NewGuid().ToString("N")[..12] + "''; CREATE USER ' + QUOTENAME(@name) + N' FOR LOGIN ' + QUOTENAME(@name) + N';'; EXEC (@sql);", other);
        var connection = Path.Combine(Path.GetDirectoryName(repository.Root)!, database.Name + ".connection");
        File.WriteAllText(connection, database.ConnectionString);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(connection, UnixFileMode.UserRead | UnixFileMode.UserWrite);   // io/SqlServer refuses a connection file others can read
        }

        try
        {
            var (asReader, asAdmin) = (Read(repository.Named(("uat", Path.Combine(Repository.Root, reader.Reference["file:".Length..])))), Read(repository.Named(("uat", connection))));

            Assert.Contains(asReader.Findings, f => f == ("read.database-scope", "note"));
            Assert.DoesNotContain("Login [" + other + "]", asReader.Keys);
            Assert.DoesNotContain(asAdmin.Findings, f => f.Code == "read.database-scope");
            Assert.Contains("Login [" + other + "]", asAdmin.Keys);
        }
        finally
        {
            File.Delete(connection);
            await SqlServerFixture.ExecuteAsync(await SqlServerFixture.ServerAsync(), "DECLARE @sql nvarchar(max) = N'DROP LOGIN ' + QUOTENAME(@name) + N';'; EXEC (@sql);", other);
        }

        (IReadOnlyList<(string Code, string? Severity)> Findings, IReadOnlyList<string> Keys) Read(string root)
        {
            var (exit, output) = repository.RunAt(root, "read", "--from", "env:uat", "--json");
            Assert.True(exit == 0, output);
            var answer = JsonNode.Parse(output)!;
            var whole = (string?)answer["full"] is { } full ? JsonNode.Parse(File.ReadAllText(Path.Combine(root, full)))! : answer;
            return ([.. answer["findings"]!.AsArray().Select(f => ((string)f!["code"]!, (string?)f["severity"]))],
                [.. whole["read"]!["elements"]!.AsArray().Select(e => (string)e!["key"]!)]);
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Diff_json_validates_against_dbchange_diff_1_and_carries_the_one_property_both_fingerprints_and_the_DacFx_release_and_claims_nothing()
    {
        var (exit, answer) = repository.Answer("diff", "--from", "ref:" + repository.Base, "--to", "ref:" + repository.Head);

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
        Assert.Equal(5, repository.Run("diff", "--from", "ref:" + repository.Base, "--to", "ref:" + repository.Head, "--fail-on-change").Exit);

        var (exit, output) = repository.Run("diff", "--from", "ref:" + repository.Base, "--to", "ref:" + repository.Base, "--fail-on-change");

        Assert.Equal(0, exit);
        Assert.Equal("No change from ref:" + repository.Base + " to ref:" + repository.Base + ".\n", output);
    }

    /// <summary>A ref and the package its build wrote read into one model and one fingerprint; read's JSON validates against dbchange.read/1.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Read_of_a_ref_and_of_the_package_its_build_wrote_fingerprint_alike_and_validate_against_dbchange_read_1()
    {
        var (exit, fromRef) = repository.Answer("read", "--from", "ref:" + repository.Base);
        var dacpac = Built(repository.Base);
        var (packageExit, fromPackage) = repository.Answer("read", "--from", "dacpac:" + dacpac);

        Assert.Equal((0, 0), (exit, packageExit));
        using var loaded = GitTests.Ok(Ssdt.Open(dacpac));
        var elements = GitTests.Ok(Ssdt.ReadModel(loaded)).Elements;
        Assert.Equal("sha256:" + Fingerprint.Of(elements), (string?)fromRef["read"]!["fingerprint"]);
        Assert.Equal((string?)fromRef["read"]!["fingerprint"], (string?)fromPackage["read"]!["fingerprint"]);
        Assert.Equal((elements.Count, Render.Shown, true), ((int)fromRef["read"]!["count"]!, fromRef["read"]!["elements"]!.AsArray().Count, (bool)fromRef["truncated"]!));
        Assert.Equal(elements.Count, Elements(fromRef).Count);
        var email = Elements(fromRef).Single(e => (string?)e!["key"] == "Column [dbo].[Customer].[Email]")!;
        Assert.True((bool)email["properties"]!["Nullable"]!);
        Assert.StartsWith("ref:" + repository.Base + ": " + elements.Count + " elements, fingerprint sha256:", repository.Run("read", "--from", "ref:" + repository.Base).Output, StringComparison.Ordinal);
    }

    /// <summary>Bad arguments are exit 1 and still validate against the verb's schema, with what it adds null.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("read", "dbchange.read.1.schema.json", new[] { "--from" })]
    [InlineData("read", "dbchange.read.1.schema.json", new[] { "--from", "sql:dev" })]
    [InlineData("diff", "dbchange.diff.1.schema.json", new[] { "--from", "ref:main" })]
    [InlineData("diff", "dbchange.diff.1.schema.json", new[] { "--from", "ref:main", "--to", "ref:main", "--from", "ref:main" })]
    public void A_verb_given_bad_arguments_exits_1_and_its_answer_validates_against_its_schema(string verb, string schema, string[] arguments)
    {
        var (exit, output) = repository.Run([verb, .. arguments, "--json"]);

        var answer = JsonNode.Parse(output)!;
        VerbAnswer.Valid(schema, answer);
        Assert.Equal(1, exit);
        Assert.Null(answer[verb]);
    }
}
