using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Estate.Kernel;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// estate read and estate diff (WP 1.7, V3_ARCHITECTURE.md §8.1 and §8.5) against a git repository holding the golden project at Base and
/// the make-mandatory archetype at Head: M1 exit 2, and each verb's JSON against its schema under cli/schemas/.
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class DiffTests(ScratchEstate estate) : IClassFixture<ScratchEstate>
{
    /// <summary>M1 exit 2: the diff of the make-mandatory archetype prints Customer.Email's Nullable true → false, and nothing else.</summary>
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

        var dacpac = Path.Combine(estate.Root, ".estate", "build", estate.Head, "SampleCatalog.dacpac");
        var (readExit, read) = estate.Tool.RunAt(estate.Root, "read", "--from", "dacpac:" + dacpac, "--json");

        Assert.True(readExit == 0, read);
        var answer = JsonNode.Parse(read)!;
        ScratchEstate.Valid("estate.read.1.schema.json", answer);
        Assert.False((bool)answer["read"]!["elements"]!.AsArray().Single(e => (string?)e!["key"] == "Column [dbo].[Customer].[Email]")!["properties"]!["Nullable"]!);
    }

    /// <summary>
    /// VALUES.md X2 for a database read, beside Io.Tests' search of every refusal for a planted password: a registered database
    /// holding a SQL login made with a planted password and a user for it, read through estate read --from env:uat --json as the
    /// fixture's admin identity, who sees the login. The answer names the login and carries neither the planted password, nor a
    /// password setting, nor a property DacFx keeps a password or a secret in (<see cref="Ssdt.Secrets"/>), whose value DacFx makes up.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task Estate_read_of_a_database_holding_a_SQL_login_prints_no_password()
    {
        const string Planted = "Pa55!planted#7f3a";
        await using var database = await SqlServerFixture.RegisterAsync();
        var login = database.Name + ReadOnlyPrincipal.Suffix;   // the fixture drops the login of this name with the database
        await SqlServerFixture.ExecuteAsync(database.ConnectionString, "DECLARE @sql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@name) + N' WITH PASSWORD = N''"
            + Planted + "''; CREATE USER ' + QUOTENAME(@name) + N' FOR LOGIN ' + QUOTENAME(@name) + N';'; EXEC (@sql);", login);
        var connection = Path.Combine(Path.GetDirectoryName(estate.Root)!, database.Name + ".connection");
        File.WriteAllText(connection, database.ConnectionString);
        try
        {
            var (exit, output) = estate.EstateAt(estate.Named(("uat", connection)), "read", "--from", "env:uat", "--json");

            Assert.True(exit == 0, output);
            var elements = JsonNode.Parse(output)!["read"]!["elements"]!.AsArray();
            Assert.Contains(elements, e => (string?)e!["key"] == "Login [" + login + "]");
            Assert.DoesNotContain(Planted, output, StringComparison.Ordinal);
            Assert.DoesNotMatch(new Regex(@"(?:password|pwd)\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), output);
            var secrets = Ssdt.Secrets.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
            Assert.DoesNotContain(elements.SelectMany(e => e!["properties"]!.AsObject().Select(p => (string?)e["key"] + " " + p.Key)), p => secrets.Contains(p.Split('.', ' ')[^1]));
        }
        finally
        {
            File.Delete(connection);
        }
    }

    /// <summary>
    /// VALUES.md X2 for a module's text: a registered database holding a symmetric key and a procedure that opens it by a planted
    /// password, read through estate read --from env:uat --json. SQL Server returns the procedure's text as it was written; the
    /// answer's Definition holds '&lt;left out&gt;' in the password's place and the planted value nowhere.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task Estate_read_of_a_database_whose_procedure_opens_a_key_by_password_prints_no_password()
    {
        const string Planted = "Pa55!module#7f3a";
        await using var database = await SqlServerFixture.RegisterAsync();
        await SqlServerFixture.ExecuteAsync(database.ConnectionString,
            "CREATE SYMMETRIC KEY K WITH ALGORITHM = AES_256 ENCRYPTION BY PASSWORD = N'" + Planted + "';");
        await SqlServerFixture.ExecuteAsync(database.ConnectionString,
            "CREATE PROCEDURE dbo.OpenKey AS OPEN SYMMETRIC KEY K DECRYPTION BY PASSWORD = N'" + Planted + "'; CLOSE SYMMETRIC KEY K;");
        var connection = Path.Combine(Path.GetDirectoryName(estate.Root)!, database.Name + ".connection");
        File.WriteAllText(connection, database.ConnectionString);
        try
        {
            var (exit, output) = estate.EstateAt(estate.Named(("uat", connection)), "read", "--from", "env:uat", "--json");

            Assert.True(exit == 0, output);
            var procedure = JsonNode.Parse(output)!["read"]!["elements"]!.AsArray().Single(e => (string?)e!["key"] == "Procedure [dbo].[OpenKey]")!;
            Assert.Contains("DECRYPTION BY PASSWORD = N'<left out>'", (string?)procedure["properties"]!["Definition"], StringComparison.Ordinal);
            Assert.DoesNotContain(Planted, output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(connection);
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Diff_json_validates_against_estate_diff_1_and_carries_the_one_property_both_fingerprints_and_the_engine()
    {
        var (exit, output) = estate.Estate("diff", "--from", "ref:" + estate.Base, "--to", "ref:" + estate.Head, "--json");

        var answer = JsonNode.Parse(output)!;
        ScratchEstate.Valid("estate.diff.1.schema.json", answer);
        Assert.Equal(0, exit);
        var changed = Assert.Single(answer["diff"]!["change"]!["changed"]!.AsArray())!;
        Assert.Equal("Column [dbo].[Customer].[Email]", (string?)changed["key"]);
        var property = Assert.Single(changed["properties"]!.AsArray())!;
        Assert.Equal(("Nullable", true, false), ((string?)property["name"], (bool)property["before"]!, (bool)property["after"]!));
        Assert.NotEqual((string?)answer["diff"]!["from"]!["fingerprint"], (string?)answer["diff"]!["to"]!["fingerprint"]);
        Assert.Equal(Doctor.DacFx, (string?)answer["engine"]!["dacfx"]);
        Assert.Null(answer["receipt"]);
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

    /// <summary>A ref and the package its build wrote read to one fingerprint, the walk's; read's JSON validates against estate.read/1.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Read_of_a_ref_and_of_the_package_its_build_wrote_fingerprint_alike_and_validate_against_estate_read_1()
    {
        var (exit, output) = estate.Estate("read", "--from", "ref:" + estate.Base, "--json");
        var dacpac = Path.Combine(estate.Root, ".estate", "build", estate.Base, "SampleCatalog.dacpac");
        var (packageExit, package) = estate.Estate("read", "--from", "dacpac:" + dacpac, "--json");

        var (fromRef, fromPackage) = (JsonNode.Parse(output)!, JsonNode.Parse(package)!);
        ScratchEstate.Valid("estate.read.1.schema.json", fromRef);
        ScratchEstate.Valid("estate.read.1.schema.json", fromPackage);
        Assert.Equal((0, 0), (exit, packageExit));
        using var loaded = GitTests.Ok(Ssdt.Load(dacpac));
        var walked = GitTests.Ok(Ssdt.Walk(loaded)).Elements;
        Assert.Equal("sha256:" + Fingerprint.Of(walked), (string?)fromRef["read"]!["fingerprint"]);
        Assert.Equal((string?)fromRef["read"]!["fingerprint"], (string?)fromPackage["read"]!["fingerprint"]);
        Assert.Equal(walked.Count, fromRef["read"]!["elements"]!.AsArray().Count);
        var email = fromRef["read"]!["elements"]!.AsArray().Single(e => (string?)e!["key"] == "Column [dbo].[Customer].[Email]")!;
        Assert.True((bool)email["properties"]!["Nullable"]!);
        Assert.StartsWith("ref:" + estate.Base + ": " + walked.Count + " elements, fingerprint sha256:", estate.Estate("read", "--from", "ref:" + estate.Base).Output, StringComparison.Ordinal);
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
