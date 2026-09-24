using System;
using System.IO;
using System.Linq;
using Estate.Cli;
using Estate.Kernel;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// io/SqlServer's targets and connections (V3_MILESTONES.md WP 1.4, M1 exits 5 and 7; VALUES.md X1, X2): the target grammar as a
/// closed type; env: resolved against estate/posture.json and copy: against .estate/copies.json alone; a connection reference resolved
/// to the caller's integrated identity unless it names another; and no refusal or printed value carrying what a reference resolves to.
/// </summary>
public sealed class TargetTests : IDisposable
{
    private const string Planted = "Pa55!planted#7f3a";

    private readonly string scratch = Directory.CreateTempSubdirectory("estate-targets-").FullName;

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("env:dev", "Env", "dev")]
    [InlineData("env:uat-2", "Env", "uat-2")]
    [InlineData("copy:estate_danny_pc_4242_0a1b2c3d", "Copy", "estate_danny_pc_4242_0a1b2c3d")]
    [InlineData("twin", "Twin", "")]
    [InlineData("ref:main", "Ref", "main")]
    [InlineData("ref:origin/release/2026.09", "Ref", "origin/release/2026.09")]
    [InlineData("dacpac:.estate/build/0a1b/SampleCatalog.dacpac", "Dacpac", ".estate/build/0a1b/SampleCatalog.dacpac")]
    public void The_target_grammar_reads_each_form_into_its_case_and_writes_it_back(string text, string form, string named)
    {
        var target = Made(SqlServer.Target.Parse(text));

        Assert.Equal(form, target.GetType().Name);
        Assert.Equal(named, target.Match(e => e.Name, c => c.Name, () => "", r => r.Name, d => d.Path));
        Assert.Equal(text, target.ToString());
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("sql:dev")]
    [InlineData("dev")]
    [InlineData("env:")]
    [InlineData("env:DEV")]
    [InlineData("env:ESTATE_DEV")]
    [InlineData("copy:")]
    [InlineData("copy:Estate-Copy")]
    [InlineData("twin:dev")]
    [InlineData("ref:")]
    [InlineData("ref:-n")]
    [InlineData("dacpac:")]
    [InlineData("")]
    public void An_unknown_target_form_is_refused_at_exit_1(string text)
    {
        var refusal = Refused(SqlServer.Target.Parse(text));

        Assert.Equal(("target.unknown", 1), (refusal.Code, Contract.Exit(refusal)));
    }

    /// <summary>VALUES.md X1, M1 exit 7: a literal connection string given where a target goes is exit 6, and nothing of it is quoted.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Server=db;User ID=estate;Password=" + Planted)]
    [InlineData("Data Source=db;Initial Catalog=Orders;Integrated Security=True;Application Name=" + Planted)]
    [InlineData("env:dev;Pwd=" + Planted)]
    public void A_literal_connection_string_as_a_target_is_exit_6_and_quoted_nowhere(string text)
    {
        var refusal = Refused(SqlServer.Target.Parse(text, "--target"));

        Assert.Equal(("connection.literal", 6), (refusal.Code, Contract.Exit(refusal)));
        Assert.Contains("--target", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, refusal.Message + refusal.Remedy, StringComparison.Ordinal);
    }

    /// <summary>M1 exit 5: copy: resolves against .estate/copies.json alone, and a name it does not hold is exit 9.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_copy_the_registry_does_not_hold_is_exit_9()
    {
        var refusal = Refused(SqlServer.Resolve(Made(SqlServer.Target.Parse("copy:estate_nowhere_1_00000000")), scratch));

        Assert.Equal(("copy.unregistered", 9), (refusal.Code, Contract.Exit(refusal)));
        Assert.Contains("copy:estate_nowhere_1_00000000", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>M1 exit 5, R15: a substrate on the host an environment's reference resolves to is exit 9, before anything connects; the refusal names the environment and quotes neither connection.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("tcp:127.0.0.1,1433", "localhost,11433")]
    [InlineData("localhost", "127.0.0.1,11433")]
    [InlineData("(local)\\SQLEXPRESS", ".")]
    [InlineData("dev-sql.corp.example,1433", "tcp:DEV-SQL.corp.example,11433")]
    public void A_substrate_on_a_host_an_environment_s_reference_names_is_exit_9(string environment, string substrate)
    {
        var root = Estate("\"dev\": { \"connection\": \"file:" + Written("dev.connection", "Server=" + environment + ";Initial Catalog=Dev;User ID=reader;Password=" + Planted) + "\", \"profile\": \"estate/profiles/pipeline.publish.xml\" }");

        var refusal = Refused(Substrate.Create(root, "Server=" + substrate + ";Initial Catalog=master;User ID=sa;Password=" + Planted + ";TrustServerCertificate=True"));

        Assert.Equal(("copy.named-host", 9), (refusal.Code, Contract.Exit(refusal)));
        Assert.Contains("env:dev", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, refusal.Message + refusal.Remedy, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, ".estate", "copies.json")), "a refused substrate registered a copy");
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_host_is_this_machine_however_the_connection_spells_it_and_otherwise_its_name_in_lower_case()
    {
        foreach (var local in (string[])["localhost", "127.0.0.1,11433", "tcp:127.0.0.1,1433", ".", "(local)", "[::1],1433", Environment.MachineName + "\\SQLEXPRESS", "tcp:" + Environment.MachineName.ToLowerInvariant()])
        {
            Assert.Equal("localhost", SqlServer.Host(local));
        }

        Assert.Equal("dev-sql.corp.example", SqlServer.Host("tcp:DEV-SQL.corp.example,1433"));
        Assert.Equal("dev-sql", SqlServer.Host("np:\\\\DEV-SQL\\pipe\\sql\\query"));
        Assert.Equal("(localdb)", SqlServer.Host("(localdb)\\MSSQLLocalDB"));
    }

    /// <summary>The caller's integrated identity by default; SQL authentication where the reference names it; and a Named prints as its environment alone.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Server=dev-sql;Initial Catalog=Dev", true)]
    [InlineData("Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + Planted, false)]
    public void A_reference_resolves_to_the_caller_s_integrated_identity_unless_it_names_another(string connection, bool integrated)
    {
        var variable = "ESTATE_TEST_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        Environment.SetEnvironmentVariable(variable, connection);
        try
        {
            var root = Estate("\"qa\": { \"connection\": \"env:" + variable + "\", \"profile\": \"estate/profiles/pipeline.publish.xml\" }");

            var named = Assert.IsType<SqlServer.Named>(Made(SqlServer.Resolve(Made(SqlServer.Target.Parse("env:qa")), root)));

            var resolved = new SqlConnectionStringBuilder(named.Connection);
            Assert.Equal((integrated, "Dev"), (resolved.IntegratedSecurity, resolved.InitialCatalog));
            Assert.Equal(integrated ? "" : "reader", resolved.UserID);
            Assert.Equal("env:qa", named.ToString());
            Assert.Equal("env:qa", named.Where);
            Assert.DoesNotContain(Planted, named.ToString() + named.Where + named.Environment, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    /// <summary>A reference that resolves to nothing, or to no connection string that names its database, is exit 6 by the reference, and quotes nothing it read.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("env", "connection.unresolved")]
    [InlineData("missing file", "connection.unresolved")]
    [InlineData("not a connection string", "connection.malformed")]
    [InlineData("no database", "connection.malformed")]
    public void A_reference_that_resolves_to_no_connection_is_exit_6_and_quotes_nothing_it_read(string how, string code)
    {
        var reference = how switch
        {
            "env" => "env:ESTATE_UNSET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            "missing file" => "file:" + Path.Combine(scratch, "absent.connection"),
            "not a connection string" => "file:" + Written("garbled.connection", "Nonsense " + Planted + " = 1"),
            _ => "file:" + Written("bare.connection", "Server=dev-sql;User ID=reader;Password=" + Planted),
        };
        var root = Estate("\"qa\": { \"connection\": \"" + reference.Replace("\\", "\\\\", StringComparison.Ordinal) + "\", \"profile\": \"estate/profiles/pipeline.publish.xml\" }");

        var refusal = Refused(SqlServer.Resolve(Made(SqlServer.Target.Parse("env:qa")), root));

        Assert.Equal((code, 6), (refusal.Code, Contract.Exit(refusal)));
        Assert.Contains("env:qa", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, refusal.Message + refusal.Remedy, StringComparison.Ordinal);
    }

    /// <summary>ref: and dacpac: name no database; the Twin arrives in M3.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("ref:main", "target.not-a-database", 1)]
    [InlineData("dacpac:build/x.dacpac", "target.not-a-database", 1)]
    [InlineData("twin", "twin.not-built", 6)]
    public void A_target_that_is_no_database_this_build_reads_is_refused_where_a_database_is_asked_for(string text, string code, int exit)
    {
        var refusal = Refused(SqlServer.Resolve(Made(SqlServer.Target.Parse(text)), scratch));

        Assert.Equal((code, exit), (refusal.Code, Contract.Exit(refusal)));
    }

    /// <summary>
    /// VALUES.md X2, M1 exit 7, §18: a named environment's SQL Server error is withheld whatever its number, and a denied login says a lead's
    /// prediction will appear on the pull request; a copy's rows are minted, so a copy's failure keeps the engine's message.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(18456, "server.denied")]
    [InlineData(4060, "server.denied")]
    [InlineData(229, "server.denied")]
    [InlineData(-2, "server.unreachable")]
    [InlineData(53, "server.unreachable")]
    [InlineData(258, "server.unreachable")]
    [InlineData(245, "server.failed")]
    [InlineData(2628, "server.failed")]
    public void A_named_environment_s_error_is_withheld_and_a_copy_s_is_kept(int number, string code)
    {
        var root = Estate("\"qa\": { \"connection\": \"file:" + Written("qa.connection", "Server=qa-sql;Initial Catalog=Qa") + "\", \"profile\": \"estate/profiles/pipeline.publish.xml\" }");
        var named = Made(SqlServer.Resolve(Made(SqlServer.Target.Parse("env:qa")), root));
        var copy = new SqlServer.Copy("estate_host_1_0a1b2c3d", "Server=localhost,11433;User ID=sa;Password=" + Planted, root);
        var message = "Conversion failed when converting the nvarchar value '" + Planted + "' to data type int.";

        var (fromNamed, fromCopy) = (named.Refused(number, message), copy.Refused(number, message));

        Assert.Equal((code, 4), (fromNamed.Code, Contract.Exit(fromNamed)));
        Assert.StartsWith("env:qa ", fromNamed.Message, StringComparison.Ordinal);
        Assert.Contains(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Msg {number}"), fromNamed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, fromNamed.Message + fromNamed.Remedy + fromCopy.Remedy, StringComparison.Ordinal);
        Assert.Equal(code == "server.denied", fromNamed.Message.Contains("a lead's prediction will appear on the pull request", StringComparison.Ordinal));
        Assert.Equal(code == "server.failed", fromCopy.Message.Contains(Planted, StringComparison.Ordinal));
    }

    private string Written(string file, string text)
    {
        File.WriteAllText(Path.Combine(scratch, file), text);
        return Path.Combine(scratch, file).Replace('\\', '/');
    }

    /// <summary>An estate's root under the scratch folder whose estate/posture.json names the environments given.</summary>
    private string Estate(string environments)
    {
        var root = Directory.CreateDirectory(Path.Combine(scratch, "estate-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        Directory.CreateDirectory(Path.Combine(root, "estate"));
        File.WriteAllText(Path.Combine(root, "estate", "posture.json"), "{ \"environments\": { " + environments + " } }");
        return root;
    }

    private static T Made<T>(Result<T> result) => result.Match(value => value, refusal => throw new Xunit.Sdk.XunitException(refusal.Code + ": " + refusal.Message));

    private static Refusal Refused<T>(Result<T> result) => Assert.IsType<Result<T>.Refused>(result).Refusal;
}
