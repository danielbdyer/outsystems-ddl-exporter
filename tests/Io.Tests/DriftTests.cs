using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// dbchange check drift (WP 1.7, V3_ARCHITECTURE.md §8.8, contract C7): the ref built, the target extracted once, the ref's package planned
/// against it package to package under the pipeline's profile; exit 0 when the deploy plan is empty and 5 naming each object when it is
/// not; law 2′ (M1 exit 3), R13's stamp (exit 6), R16's denied login (exit 7), and R14's watch on the read-only principal (exit 8).
/// VerbExitTests holds the refusals check drift makes before it connects.
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class DriftTests(ScratchRepository repository) : IClassFixture<ScratchRepository>
{
    /// <summary>
    /// Law 2′ (M1 exit 3): the golden project published to a fresh copy is in sync with it, check drift exit 0 and the deploy plan empty; one column
    /// altered on the copy is exit 5 naming its table and that column, the column's Length from the target to the repository.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Law", "2′ a published copy is in sync with its package")]
    [Trait("Exit", "M1.3")]
    public async Task A_published_copy_is_in_sync_with_its_package_and_one_column_altered_on_it_is_exit_5_naming_it()
    {
        var copy = await Published();
        using var disposable = DisposableCopy.Of(copy);
        var (exit, output) = Drift("copy:" + copy.Name);
        Assert.True(exit == 0, output);
        Assert.StartsWith("copy:" + copy.Name + " is in sync with ref:" + repository.Base + " (commit " + repository.Base[..8] + ").\n", output, StringComparison.Ordinal);

        await SqlServerFixture.ExecuteAsync(copy.Connection, "ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(300) NULL;");
        (exit, output) = Drift("copy:" + copy.Name);

        Assert.True(exit == 5, output);
        Assert.StartsWith("copy:" + copy.Name + " differs from ref:" + repository.Base + " (commit " + repository.Base[..8] + "): the deploy plan holds 1 operation.", output, StringComparison.Ordinal);
        Assert.Contains("- warning `drift.alter` Table [dbo].[Customer]: The deploy plan against copy:" + copy.Name + " would alter Table [dbo].[Customer].", output, StringComparison.Ordinal);
        Assert.Equal(["- warning `drift.column` Column [dbo].[Customer].[Email]: Length 300 → 256, from the target to the repository."],
            output.Split('\n').Where(l => l.Contains("`drift.column`", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The golden project published to a copy, then a column the project lacks added to dbo.Product with a named default, and a view the
    /// project lacks over it: the plan of the project against the copy drops the column. check drift names the table's alteration and the
    /// default's drop as warnings, quotes DacFx's DataIssue alert, lists any operation DacFx adds for a dependent in one note, and writes no
    /// warning for a refresh.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_column_the_package_drops_is_named_with_DacFx_s_data_issue_and_a_dependent_s_refresh_is_a_note()
    {
        var copy = await Published();
        using var disposable = DisposableCopy.Of(copy);
        await SqlServerFixture.ExecuteAsync(copy.Connection, "ALTER TABLE dbo.Product ADD LegacyNote NVARCHAR(40) NOT NULL CONSTRAINT DF_Product_LegacyNote DEFAULT (N'x');");
        await SqlServerFixture.ExecuteAsync(copy.Connection, "CREATE VIEW dbo.ProductNotes AS SELECT Id, LegacyNote FROM dbo.Product;");

        var (exit, output) = Drift("copy:" + copy.Name, "--json");

        var answer = JsonNode.Parse(output)!;
        VerbAnswer.Valid("dbchange.check.1.schema.json", answer);
        var findings = answer["findings"]!.AsArray().Select(f => (Code: (string)f!["code"]!, Severity: (string)f["severity"]!, Subject: (string)f["subject"]!, Message: (string)f["message"]!)).ToList();
        Console.WriteLine(string.Join('\n', findings));
        Assert.True(exit == 5, output);
        Assert.Contains(("drift.alter", "warning", "Table [dbo].[Product]"), findings.Select(f => (f.Code, f.Severity, f.Subject)));
        Assert.Contains(("drift.drop", "warning", "DefaultConstraint [dbo].[DF_Product_LegacyNote]"), findings.Select(f => (f.Code, f.Severity, f.Subject)));
        Assert.Contains(findings, f => f is { Code: "drift.data-issue", Severity: "warning", Subject: "Table [dbo].[Product]" }
            && f.Message == "The column [dbo].[Product].[LegacyNote] is being dropped, data loss could occur.");
        Assert.DoesNotContain(findings, f => f.Code == "drift.refresh");
        Assert.All(findings.Where(f => f.Code == "drift.consequence"), f => Assert.Equal("note", f.Severity));
        Assert.Contains("`drift.column` dropped Column [dbo].[Product].[LegacyNote]", Drift("copy:" + copy.Name).Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Decision 2.26's measurement (O2): the golden project built under a case-insensitive and under a case-sensitive collation, each
    /// published to a copy created under that collation, then dbo.Customer renamed CUSTOMER on each with sp_rename, which sys.tables
    /// confirms in a binary comparison. The plan of the package against its copy plans nothing under either collation: DacFx matches object
    /// names ignoring case even where the database reads [dbo].[Customer] and [dbo].[CUSTOMER] as two names, so a case-only rename on a
    /// case-sensitive database is a difference no deploy plan will reconcile. dbchange diff from the copy to the package reads names under the
    /// copy's collation: one case-only pair with its note under the case-insensitive collation, and the table dropped and created under the
    /// case-sensitive one. check drift builds the golden ref, a case-insensitive package: against the case-insensitive copy it exits 0, and
    /// against the case-sensitive copy it is refused as DacFx refuses a live plan of a case-insensitive model against a case-sensitive
    /// database (SQL72030), which the plan package to package does not check: plan.collation at exit 6.
    /// </summary>
    [Theory]
    [Trait("Category", "fixture")]
    [InlineData("SQL_Latin1_General_CP1_CI_AS", true, 0)]
    [InlineData("Latin1_General_CS_AS", false, 6)]
    [Trait("Value", "O2")]
    public async Task A_case_only_rename_of_a_table_on_a_copy_plans_nothing_under_either_collation_and_diff_reads_it_under_the_copy_s_collation(string collation, bool caseInsensitive, int driftExit)
    {
        var (copy, dacpac, profile) = await Published(collation);
        using var disposable = DisposableCopy.Of(copy);
        await SqlServerFixture.ExecuteAsync(copy.Connection, "EXEC sp_rename 'dbo.Customer', 'CUSTOMER';");
        Assert.Equal(1, await SqlServerFixture.ScalarAsync(copy.Connection, "SELECT COUNT(*) FROM sys.databases WHERE name = @name AND collation_name = N'" + collation + "';", copy.Name.ToString()));
        Assert.Equal((1, 0), (
            await SqlServerFixture.ScalarAsync(copy.Connection, "SELECT COUNT(*) FROM sys.tables WHERE name COLLATE Latin1_General_BIN2 = N'CUSTOMER';"),
            await SqlServerFixture.ScalarAsync(copy.Connection, "SELECT COUNT(*) FROM sys.tables WHERE name COLLATE Latin1_General_BIN2 = N'Customer';")));

        var plan = Planned(dacpac, copy, profile);
        var (exit, output) = Drift("copy:" + copy.Name);
        var (diffExit, diffOutput) = repository.Run("diff", "--from", "copy:" + copy.Name, "--to", "dacpac:" + dacpac, "--json");

        Assert.True(plan.Report.IsEmpty, collation + " planned:\n" + string.Join('\n', plan.Report.Operations));
        Assert.True(exit == driftExit, output);
        if (driftExit == 6)
        {
            Assert.Contains("`plan.collation`", output, StringComparison.Ordinal);
            Assert.Contains("SQL72030", output, StringComparison.Ordinal);
        }

        Assert.True(diffExit == 0, diffOutput);
        var answer = JsonNode.Parse(diffOutput)!;
        VerbAnswer.Valid("dbchange.diff.1.schema.json", answer);
        var change = answer["diff"]!["change"]!;
        var (dropped, created) = (change["dropped"]!.AsArray().Select(k => (string?)k).ToList(), change["created"]!.AsArray().Select(k => (string?)k).ToList());
        if (caseInsensitive)
        {
            Assert.Equal(["Table [dbo].[CUSTOMER] to Table [dbo].[Customer]"], change["caseOnlyRenamed"]!.AsArray().Select(r => r!["before"] + " to " + r["after"]));
            Assert.Contains(answer["findings"]!.AsArray(), f => (string?)f!["code"] == "diff.case-only-rename" && (string?)f["severity"] == "note"
                && ((string?)f["message"])!.Contains(collation + " reads as one name; DacFx plans nothing for it", StringComparison.Ordinal));
            Assert.DoesNotContain(dropped.Concat(created), key => key!.StartsWith("Table ", StringComparison.Ordinal));
        }
        else
        {
            Assert.Contains("Table [dbo].[CUSTOMER]", dropped);
            Assert.Contains("Table [dbo].[Customer]", created);
            Assert.Empty(change["caseOnlyRenamed"]!.AsArray());
        }
    }

    /// <summary>
    /// Decision 2.25 on a database: a copy of the golden project given a column named by one space and one whose name holds a tab, which
    /// SQL Server admits inside brackets, reads whole at exit 0 naming both; diff from the copy to the package, which lacks them, exits 5
    /// with --fail-on-change and prints each as dropped, the tab escaped as \u0009; and check drift against the golden ref exits 5 naming
    /// both columns under the altered table, escaped the same way. No raw tab reaches either Markdown output.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_copy_holding_a_column_named_by_a_space_and_one_holding_a_tab_is_read_whole_and_each_output_escapes_the_tab()
    {
        var (copy, dacpac, _) = await Published(null);
        using var disposable = DisposableCopy.Of(copy);
        await SqlServerFixture.ExecuteAsync(copy.Connection, "ALTER TABLE dbo.Customer ADD [ ] INT NULL, [a\tb] INT NULL;");

        var (readExit, read) = repository.Run("read", "--from", "copy:" + copy.Name, "--json");
        var (diffExit, diff) = repository.Run("diff", "--from", "copy:" + copy.Name, "--to", "dacpac:" + dacpac, "--fail-on-change");
        var (driftExit, drift) = Drift("copy:" + copy.Name);

        Assert.True(readExit == 0, read);
        var answer = JsonNode.Parse(read)!;
        var whole = (string?)answer["full"] is { } full ? JsonNode.Parse(File.ReadAllText(Path.Combine(repository.Root, full)))! : answer;
        var keys = whole["read"]!["elements"]!.AsArray().Select(e => (string?)e!["key"]).ToList();
        Assert.Contains("Column [dbo].[Customer].[ ]", keys);
        Assert.Contains("Column [dbo].[Customer].[a\tb]", keys);
        Assert.True(diffExit == 5, diff);
        Assert.Contains("dropped Column [dbo].[Customer].[ ]", diff.Split('\n'));
        Assert.Contains("dropped Column [dbo].[Customer].[a\\u0009b]", diff.Split('\n'));
        Assert.True(driftExit == 5, drift);
        Assert.Contains("- warning `drift.alter` Table [dbo].[Customer]: ", drift, StringComparison.Ordinal);
        Assert.Contains("`drift.column` dropped Column [dbo].[Customer].[ ]", drift, StringComparison.Ordinal);
        Assert.Contains("`drift.column` dropped Column [dbo].[Customer].[a\\u0009b]", drift, StringComparison.Ordinal);
        Assert.DoesNotContain('\t', diff + drift);
    }

    /// <summary>
    /// R1 and DECISIONS.md, 2026-09-25: a drift's provenance holds the fingerprint of the target's schema, its elements as the copy extracts,
    /// and of the deploy report, the change the claim is about; neither is the package's. It names the committed DacFx and the copy's server,
    /// with the image's digest where the copy ran in the container, and lacks only the existing data; the stamp says UNPINNED while the
    /// ledger's row does, and until S7 lands the Octopus step's profile, a note says the profile is not verified against it (§17 item 15).
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "R1")]
    [Trait("Exit", "M1.6")]
    public async Task A_drift_s_provenance_fingerprints_the_target_s_schema_and_the_deploy_report()
    {
        var (copy, dacpac, profile) = await Published(null);
        using var disposable = DisposableCopy.Of(copy);
        await SqlServerFixture.ExecuteAsync(copy.Connection, "ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(300) NULL;");
        var (exit, output) = Drift("copy:" + copy.Name, "--json");
        var plan = Planned(dacpac, copy, profile);
        using var extracted = GitTests.Ok(DacFx.Extract(copy));
        using var package = GitTests.Ok(Ssdt.Open(dacpac));
        var (schema, packaged) = (Fingerprint.Of(GitTests.Ok(extracted.Elements).Elements), Fingerprint.Of(GitTests.Ok(package.Elements).Elements));

        var answer = JsonNode.Parse(output)!;
        VerbAnswer.Valid("dbchange.check.1.schema.json", answer);
        Assert.Equal((5, "differs"), (exit, (string?)answer["outcome"]));
        var provenance = answer["provenance"]!;
        Assert.Equal(("sha256:" + schema, "sha256:" + Fingerprint.Of(plan.Report)), ((string?)provenance["schema"], (string?)provenance["change"]));
        Assert.DoesNotContain("sha256:" + packaged, new[] { (string?)provenance["schema"], (string?)provenance["change"] });
        // The copy ran in the dbchange-sql container when its server is the one ~/.dbchange/sql.env names, whether DBCHANGE_SQL also names it or
        // not; its image is then one of the names Docker gives the image that container runs (ImageTests holds which), and otherwise none.
        var container = File.Exists(LocalServer.SqlEnv) && LocalServer.ServerName(null, LocalServer.SqlEnv, localDb: false) is Result<ServerName>.Ok(var inContainer)
            && LocalServer.ServerName(copy.Connection) is Result<ServerName>.Ok(var made) && made == inContainer;
        var (version, level) = await Reported(copy.Connection);
        Assert.Equal((DoctorTests.PinnedDacFx, "UNPINNED"), ((string?)provenance["dacfx"], (string?)answer["pin"]));
        Assert.Equal((version, level), ((string?)provenance["server"]!["version"], (int?)provenance["server"]!["compatibilityLevel"]));
        Assert.Contains((string?)provenance["server"]!["image"], container ? RunningImage() : [null]);
        Assert.Equal(("copy:" + copy.Name, "[\"existingData\"]", repository.Base), ((string?)provenance["target"], provenance["lacking"]!.ToJsonString(), (string?)answer["check"]!["commit"]));
        Assert.Equal(answer["server"]!.ToJsonString(), provenance["server"]!.ToJsonString());
        Assert.Contains(answer["findings"]!.AsArray(), f => (string?)f!["code"] == "toolchain.unpinned" && ((string?)f["message"])!.Contains("UNPINNED", StringComparison.Ordinal));
        Assert.Contains(answer["findings"]!.AsArray(), f => (string?)f!["code"] == "profile.unverified" && (string?)f["severity"] == "note"
            && (string?)f["message"] == "The SSDT repository commits no copy of the publish profile the Octopus step applies, so " + ScratchRepository.Profile + " is not verified against it.");
    }

    /// <summary>
    /// VALUES.md S2, finding NFR-10: a check whose read of the database fails answers that failure, its stamp as far as the work got, and
    /// neither a column line nor matches; the cli once turned the same failure into an empty list of columns.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "S2")]
    public async Task A_check_drift_whose_extract_is_refused_answers_the_refusal_and_no_column_lines()
    {
        var copy = await Published();
        using var disposable = DisposableCopy.Of(copy);
        var refusal = new Error("server.failed", "copy:" + copy.Name + " failed the statement: Msg 245.", "Look the number up in SQL Server's error list.");
        var request = new DriftCheck.Request(new Target.RegisteredCopy(copy.Name), GitTests.Ok(GitRef.Of("--at", repository.Base)), ScratchRepository.Profile, null);

        var drift = DriftCheck.Run(new Checkout(repository.Root, repository.Root, repository.Tool.Folder, Cli.Contract.Version), request, SqlServer.QueryLog.Start(repository.Root), _ => refusal);

        Assert.Equal(refusal, Assert.IsType<Result<DriftCheck.Answer>.Failed>(drift.Result).Error);
        Assert.Equal("UNPINNED", drift.Stamp?.Pin?.ToString());
        Assert.NotNull(drift.Stamp?.Server);
    }

    /// <summary>M1 exit 7's second half (R16): a denied login prints one sentence naming the environment and saying a lead's prediction will appear on the pull request, before anything builds.</summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "X2")]
    [Trait("Exit", "M1.7")]
    public async Task A_denied_login_prints_one_sentence_naming_the_environment_and_a_lead_s_prediction()
    {
        var server = new SqlConnectionStringBuilder(await SqlServerFixture.ServerAsync());
        var connection = Path.Combine(Path.GetDirectoryName(repository.Root)!, "denied.connection");
        File.WriteAllText(connection, new SqlConnectionStringBuilder
        {
            DataSource = server.DataSource, InitialCatalog = "master", UserID = "dbchange_nobody_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)), Password = "Denied1!planted",
            TrustServerCertificate = true, Pooling = false,
        }.ConnectionString);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(connection, UnixFileMode.UserRead | UnixFileMode.UserWrite);   // io/SqlServer refuses a connection file others can read
        }

        var (exit, output) = repository.RunAt(repository.Named(("uat", connection)), "check", "drift", "--target", "env:uat", "--at", repository.Base);

        Assert.Equal(4, exit);
        var sentence = output.Split('\n')[0];
        Assert.Equal("env:uat refused this identity (Msg 18456, SQL Server's message withheld); a lead's prediction will appear on the pull request.", sentence);
        Assert.DoesNotContain("planted", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// M1 exit 8 (R14) and the ruling of 2026-09-25: an Extended Events session on the read-only principal's login, made by the fixture's
    /// admin identity, records every batch and call that login sends through a check drift of a named environment that matches, one that
    /// finds drift, and a read --from env:dev. None is DML, DDL or an EXEC: the plan runs package to package, so DacFx no longer reads the
    /// instance's default file paths (xp_instance_regread), and the test admits no EXEC at all. SQL Server masks the text of one call per read
    /// of the database, which the test cannot read and admits by its form: DacFx sends its catalog queries as one sp_executesql call, and SQL
    /// Server replaces that call's text with *encrypt and dashes because it names the key and credential catalog views (measured through
    /// SqlClient's trace on DacFx 170.5.96: the call holds SELECT statements only).
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "S7")]
    [Trait("Exit", "M1.8")]
    public async Task The_read_only_principal_sends_no_DML_no_DDL_and_no_EXEC_through_check_drift_and_read()
    {
        await using var database = await SqlServerFixture.RegisterAsync();
        GoldenProject.Publish(await GoldenProject.Built(), database, GoldenProject.Pipeline());
        var reader = await ReadOnlyPrincipal.CreateAsync(database);
        await using var trace = await ReadOnlyLoginTrace.StartAsync(reader.Login);
        var root = repository.Named(("dev", Path.Combine(Repository.Root, reader.Reference["file:".Length..])));

        var (matchExit, matching) = repository.RunAt(root, "check", "drift", "--target", "env:dev", "--at", repository.Base);
        await SqlServerFixture.ExecuteAsync(database.ConnectionString, "ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(300) NULL;");
        var (driftExit, drifted) = repository.RunAt(root, "check", "drift", "--target", "env:dev", "--at", repository.Base);
        var (readExit, read) = repository.RunAt(root, "read", "--from", "env:dev");
        var sent = await trace.StopAsync();

        Assert.True(matchExit == 0, matching);
        Assert.StartsWith("env:dev is in sync with ref:" + repository.Base, matching, StringComparison.Ordinal);
        Assert.True(driftExit == 5, drifted);
        Assert.Contains("`drift.column` Column [dbo].[Customer].[Email]: Length 300 → 256", drifted, StringComparison.Ordinal);
        Assert.True(readExit == 0, read);
        Assert.Contains(sent, s => s.Text.Contains("HAS_PERMS_BY_NAME", StringComparison.Ordinal));   // the session saw the run: dbchange's own first statement
        // The container's SQL Server shows DacFx's catalog batch masked (*encrypt---); the Windows runner's LocalDB shows it as written
        // but cut off, and Writes reads it token by token. A masked text is admitted only as DacFx's sp_executesql, one per read at most:
        // the two checks and the read.
        var masked = sent.Where(s => ReadOnlyLoginTrace.Masked(s.Text)).ToList();
        Assert.All(masked, s => Assert.Equal(("rpc_completed", "sp_executesql"), (s.Event, s.Object)));
        Assert.True(masked.Count <= 3, masked.Count + " masked calls for three reads");
        var writes = sent.Where(s => !ReadOnlyLoginTrace.Masked(s.Text)).SelectMany(s => ReadOnlyLoginTrace.Writes(s.Text)).ToList();
        Assert.True(writes.Count == 0, string.Join("\n----\n", writes));
    }

    /// <summary>The plan of the package at <paramref name="dacpac"/> against the copy, extracted, package to package, as check drift plans.</summary>
    private static Plan Planned(string dacpac, SqlServer.Copy copy, PublishProfile.Strict profile)
    {
        using var extracted = GitTests.Ok(DacFx.Extract(copy));
        using var package = GitTests.Ok(Ssdt.Open(dacpac));
        return GitTests.Ok(DacFx.Plan(package, extracted, copy.Catalog, profile, []));
    }

    /// <summary>A fresh copy on the run's local server, registered under the repository root, with the golden project published to it under the pipeline's profile.</summary>
    private async Task<SqlServer.Copy> Published() => (await Published(null)).Copy;

    /// <summary>
    /// A fresh copy, its collation set as given while it is still empty (the server's default when null), with the golden project built
    /// under that collation (its DefaultCollation, the model's; the project as committed when null) published to it under the pipeline's
    /// profile; the package and the profile too, for a plan against it. DacFx refuses a case-insensitive model deployed to a
    /// case-sensitive database (SQL72030), so the package's collation follows the copy's.
    /// </summary>
    private async Task<(SqlServer.Copy Copy, string Dacpac, PublishProfile.Strict Profile)> Published(string? collation)
    {
        var server = await SqlServerFixture.ServerAsync();
        var copy = GitTests.Ok(LocalServer.Create(repository.Root, server));
        string dacpac;
        if (collation is null)
        {
            dacpac = GitTests.Ok(Ssdt.Build(GitTests.Ok(Git.At(repository.Root, repository.Base)), "project/SampleCatalog.sqlproj", repository.Tool.Folder, Path.Combine(repository.Root, ".dbchange", "build"))).Path;
        }
        else
        {
            await SqlServerFixture.ExecuteAsync(server, "DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@name) + N' COLLATE " + collation + ";'; EXEC (@sql);", copy.Name.ToString());
            var project = Path.Combine(repository.Root, ".dbchange", "collation", collation);
            if (!Directory.Exists(project))
            {
                ToolFolderTests.Copy(Path.Combine(GitTests.Ok(Git.At(repository.Root, repository.Base)).Path, "project"), project);   // the golden project as committed, not the head's edit
                var file = Path.Combine(project, "SampleCatalog.sqlproj");
                var text = File.ReadAllText(file);
                File.WriteAllText(file, text.Contains("<DefaultCollation>", StringComparison.Ordinal)
                    ? System.Text.RegularExpressions.Regex.Replace(text, "<DefaultCollation>[^<]*</DefaultCollation>", "<DefaultCollation>" + collation + "</DefaultCollation>")
                    : text.Replace("<PropertyGroup>", "<PropertyGroup>\n    <DefaultCollation>" + collation + "</DefaultCollation>", StringComparison.Ordinal));
            }

            dacpac = GitTests.Ok(Ssdt.Build(Path.Combine(project, "SampleCatalog.sqlproj"), repository.Tool.Folder, Path.Combine(repository.Root, ".dbchange", "build"))).Path;
        }

        var profile = GitTests.Ok(PublishProfiles.Load(Path.Combine(repository.Root, ScratchRepository.Profile)));
        GitTests.Ok(copy.Publish(dacpac, profile));
        return (copy, dacpac, profile);
    }

    private (int Exit, string Output) Drift(string target, params string[] more) =>
        repository.Run(["check", "drift", "--target", target, "--at", repository.Base, "--profile", ScratchRepository.Profile, .. more]);

    /// <summary>What SQL Server reports of itself on a connection: SERVERPROPERTY('ProductVersion') and the database's compatibility level.</summary>
    private static async Task<(string Version, int Level)> Reported(string connection)
    {
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync();
        await using var command = new SqlCommand("SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)), compatibility_level FROM sys.databases WHERE database_id = DB_ID();", sql);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetByte(1));
    }

    /// <summary>The names Docker gives the image the dbchange-sql container runs: its image id, and each registry digest it was pulled by.</summary>
    private static IReadOnlyList<string?> RunningImage()
    {
        var id = Docker("container", "inspect", "--format", "{{.Image}}", "dbchange-sql");
        return [id, .. JsonNode.Parse(Docker("image", "inspect", "--format", "{{json .RepoDigests}}", id))!.AsArray().Select(d => ((string)d!).Split('@')[1])];
    }

    private static string Docker(params string[] arguments) =>
        new Command("docker", arguments, TimeSpan.FromSeconds(30)).Run() is Ran.Exited { Code: 0 } ran ? ran.Output.Trim()
            : throw new Xunit.Sdk.XunitException("docker " + string.Join(' ', arguments) + " failed");
}
