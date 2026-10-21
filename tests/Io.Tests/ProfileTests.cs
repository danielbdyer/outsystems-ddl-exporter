using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DbChange.Cli;
using DbChange.Kernel;
using DbChange.Tests;
using Microsoft.Data.SqlClient;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// dbchange profile against a named environment classified real (V3_MILESTONES.md WP 3.7; VALUES.md P1 and P2; law 12′; M3 exit 4): a
/// registered database stands in for env:uat, read as the read-only principal through a file: reference. Its dbo.Customer holds
/// PlantedValue's values, addresses, phone numbers and names unique enough to identify a row, values of punctuation alone, a name in
/// other scripts, NULLs, empty strings and spaces; dbo.Customer.Code is varchar under Cyrillic_General_CI_AS, and dbo.Customer.Note
/// varchar under Latin1_General_100_CI_AS_SC_UTF8. A column's profile returns
/// its signatures as the class map gives them, those seen fewer than five times or of punctuation alone folded, and the summary's
/// integers, and no output or file the run writes holds a value; --counts writes each table's row tier into dbchange/ledgers/row-tiers.md
/// with the environment and the date, and --commit pushes it on a branch of its own to a local bare origin.
/// </summary>
public sealed class ProfileTests(RealEnvironment uat) : IClassFixture<RealEnvironment>, IDisposable
{
    private readonly ScratchFolder root = ScratchFolder.UnderRepository("profile-under-test");

    public void Dispose() => root.Dispose();

    /// <summary>
    /// The privacy case (VALUES.md P2): no value of the column appears in what profile prints or in any file the run writes, the
    /// identifying addresses and phone numbers, PlantedValue's values and the name in other scripts included; every signature shown is
    /// seen five times or more and holds a letter, a digit or another character, so a value of punctuation alone, seen six times, is
    /// folded; no signature shown is a value the column holds; and the signatures shown and the fold are the class map's over the column's
    /// values, every row of the column accounted for.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "P2")]
    [Trait("Law", "12′ no value from an environment reaches a copy or a committed file")]
    public void No_value_is_reconstructable_from_a_column_s_shape()
    {
        var here = uat.Repository(root.Path);

        var (exit, answer, printed) = VerbAnswer.Printing(here, "profile", "--env", "uat", "dbo.Customer.Email");

        Assert.Equal((0, "done"), (exit, (string?)answer["outcome"]));
        var profile = answer["profile"]!;
        var shown = profile["signatures"]!.AsArray().Select(s => ((string)s!["signature"]!, (long)s["rows"]!)).ToList();
        var nonEmpty = RealEnvironment.Emails.Where(v => v is { Length: > 0 }).Select(v => v!).ToList();
        var expected = nonEmpty.GroupBy(SignatureFormTests.Expected)
            .Where(g => g.Count() >= SqlServer.Fold && SqlServer.IsSignature(g.Key))
            .Select(g => (g.Key, (long)g.Count())).OrderByDescending(s => s.Item2).ThenBy(s => s.Key, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, shown);
        Assert.Contains(("a.a@a.a", 6L), shown);
        Assert.Contains(("x", 6L), shown);
        Assert.DoesNotContain(shown, s => s.Item1 is "--" or "___" or "A/A");
        Assert.All(shown, s => Assert.True(s.Item2 >= SqlServer.Fold && SqlServer.IsSignature(s.Item1), s.Item1));
        Assert.Equal(nonEmpty.Count - shown.Sum(s => s.Item2), (long)profile["folded"]!["rows"]!);
        Assert.DoesNotContain(shown, s => RealEnvironment.Emails.Contains(s.Item1, StringComparer.Ordinal));
        foreach (var value in RealEnvironment.Identifying)
        {
            new PlantedValue(value).AbsentFrom(printed + Written(root.Path));
        }
    }

    /// <summary>
    /// Law 12′ in its planted form and M3 exit 4, the planted-value scan's second half (VALUES.md P1): profile of a column and --counts
    /// against the environment classified real store no value, in the answer, the run's queries.log and answer.json, the ledger or any
    /// other file under the repository; and a declared witness carrying one of the environment's values, a literal neither synthetic nor
    /// defined by the repository, is refused by its line and quoted nowhere.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Law", "12′ no value from an environment reaches a copy or a committed file")]
    [Trait("Value", "P1")]
    [Trait("Exit", "M3.4")]
    public void Profile_of_an_environment_classified_real_stores_no_value_and_a_declared_witness_carrying_one_of_its_values_is_refused()
    {
        var here = uat.Repository(root.Path);

        var outputs = new[] { "dbo.Customer.Email", "dbo.Customer.Name", "dbo.Customer.Code" }
            .Select(column => VerbAnswer.Printing(here, "profile", "--env", "uat", column))
            .Append(VerbAnswer.Printing(here, "profile", "--env", "uat", "--counts")).ToList();

        Assert.All(outputs, o => Assert.Equal(0, o.Exit));
        Assert.True(File.Exists(Path.Combine(root.Path, "dbchange", "ledgers", "row-tiers.md")), "--counts wrote no ledger");
        var stored = string.Concat(outputs.Select(o => o.Printed)) + Written(root.Path);
        foreach (var value in RealEnvironment.Identifying.Concat(RealEnvironment.Names))
        {
            new PlantedValue(value).AbsentFrom(stored);
        }

        var planted = RealEnvironment.Planted[0];
        var refused = Failed(Witnesses.Parse("-- plants: [dbo].[Customer]\nINSERT INTO dbo.Customer (Email) VALUES (N'" + planted + "');",
            RealEnvironment.Model), "witness.not-synthetic");
        Assert.StartsWith("dbchange/witnesses.sql line 2 ", refused.Message, StringComparison.Ordinal);
        new PlantedValue(planted).AbsentFrom(refused);
    }

    /// <summary>
    /// The server computes the class map's signatures: the formats of PRV-33, an accented Latin name as Aax (PRV-34), a name in another
    /// script as x, a varchar column of a Cyrillic code page read as its Unicode characters, and one under a UTF-8 collation holding
    /// characters outside the Basic Multilingual Plane and U+0000, each x; and the summary counts what no signature shows, the NULLs,
    /// the empty strings and the values of spaces alone, with distinct as collated beside distinct as bytes, two values that differ in
    /// case alone telling them apart.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "P2")]
    public void The_server_s_signatures_are_the_class_map_s_and_the_summary_counts_what_no_signature_shows()
    {
        var here = uat.Repository(root.Path);

        var email = VerbAnswer.Of(here, "profile", "--env", "uat", "dbo.Customer.Email").Answer["profile"]!;
        var code = VerbAnswer.Of(here, "profile", "--env", "uat", "dbo.Customer.Code").Answer["profile"]!;
        var note = VerbAnswer.Of(here, "profile", "--env", "uat", "dbo.Customer.Note").Answer["profile"]!;

        Assert.Equal(RealEnvironment.Emails.Select((_, i) => RealEnvironment.Notes[i % RealEnvironment.Notes.Count])
                .GroupBy(SignatureFormTests.Expected).Select(g => (g.Key, (long)g.Count()))
                .OrderByDescending(s => s.Item2).ThenBy(s => s.Key, StringComparer.Ordinal),
            note["signatures"]!.AsArray().Select(s => ((string)s!["signature"]!, (long)s["rows"]!)));
        Assert.Equal(["Aax_x_x", "axa", "Aa-99"], RealEnvironment.Notes.Select(SignatureFormTests.Expected));
        var values = RealEnvironment.Emails;
        Assert.Equal(("[dbo].[Customer]", "Email", "nvarchar"), ((string?)email["table"], (string?)email["column"], (string?)email["type"]));
        Assert.Equal((long)values.Count, (long)email["rows"]!);
        Assert.Equal(((long)values.Count(v => v is null), (long)values.Count(v => v == ""), 5L, 5L),
            ((long)email["nulls"]!, (long)email["empty"]!, (long)email["spacesOnly"]!, (long)email["edgeSpaces"]!));
        var present = values.Where(v => v is not null).Select(v => v!.TrimEnd(' ')).ToList();
        Assert.Equal(((long)present.Distinct(StringComparer.OrdinalIgnoreCase).Count(), (long)present.Distinct(StringComparer.Ordinal).Count()),
            ((long)email["distinctAsCollated"]!, (long)email["distinctBinary"]!));
        Assert.Equal((long)values.Max(v => v?.TrimEnd(' ').Length ?? 0), (long)email["maxLength"]!);
        Assert.Equal([("x", (long)values.Count)], code["signatures"]!.AsArray().Select(s => ((string)s!["signature"]!, (long)s["rows"]!)));
        Assert.Equal((2L, 3L), ((long)code["distinctAsCollated"]!, (long)code["distinctBinary"]!));
    }

    /// <summary>
    /// A column the environment does not hold, or holds as no character type, is refused before any query reads a row: target.no-column
    /// and target.column-type, exit 1, each naming the column; and a column named in other than three parts is refused before anything
    /// connects.
    /// </summary>
    [Theory]
    [Trait("Category", "fixture")]
    [InlineData("dbo.Customer.Missing", "target.no-column")]
    [InlineData("dbo.Nowhere.Email", "target.no-column")]
    [InlineData("dbo.Customer.Age", "target.column-type")]
    public void A_column_the_environment_does_not_hold_or_holds_as_no_character_type_is_exit_1_naming_it(string column, string code)
    {
        var here = uat.Repository(root.Path);

        var (exit, answer) = VerbAnswer.Of(here, "profile", "--env", "uat", column);

        Assert.Equal((1, code), (exit, (string?)answer["findings"]![0]!["code"]));
        Assert.Contains("env:uat", (string)answer["message"]!, StringComparison.Ordinal);
    }

    /// <summary>
    /// --counts measures each table's rows from sys.partitions and appends a row for each table whose tier differs from its environment's
    /// last row, or that has none, every row carrying the environment and the UTC date of the clock given; a second measurement after
    /// one table grows appends that table's row alone, and no row is edited.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "O12")]
    public async Task Counts_appends_a_row_for_each_table_whose_tier_changed_with_the_environment_and_the_UTC_date_on_every_row()
    {
        var here = uat.Repository(root.Path);
        var ledger = Path.Combine(root.Path, "dbchange", "ledgers", "row-tiers.md");

        var first = Value(Profile.Counts(here, "uat", commit: false, SqlServer.QueryLog.Start(root.Path),
            new DateTimeOffset(2026, 10, 20, 23, 30, 0, TimeSpan.FromHours(-5))));
        var written = File.ReadAllText(ledger);
        await SqlServerFixture.ExecuteAsync(uat.Database.ConnectionString, "INSERT INTO dbo.Growing (Id) VALUES (1);");
        var second = Value(Profile.Counts(here, "uat", commit: false, SqlServer.QueryLog.Start(root.Path),
            new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero)));

        Assert.Equal(new DateOnly(2026, 10, 21), first.Date);
        Assert.Equal([("[dbo].[Customer]", "1–1k"), ("[dbo].[Empty]", "0"), ("[dbo].[Growing]", "0"), ("[dbo].[Thousands]", "1k–100k")],
            first.Tables.Select(t => (t.Table.ToString(), t.Tier!)));
        Assert.All(first.Tables, t => Assert.True(t.Appended));
        Assert.Equal(["[dbo].[Growing]"], second.Tables.Where(t => t.Appended).Select(t => t.Table.ToString()));
        Assert.StartsWith(written, File.ReadAllText(ledger), StringComparison.Ordinal);
        Assert.Equal(
        [
            "| 2026-10-21 | uat | [dbo].[Customer] | 1–1k |", "| 2026-10-21 | uat | [dbo].[Empty] | 0 |",
            "| 2026-10-21 | uat | [dbo].[Growing] | 0 |", "| 2026-10-21 | uat | [dbo].[Thousands] | 1k–100k |",
            "| 2026-10-22 | uat | [dbo].[Growing] | 1–1k |",
        ], File.ReadAllLines(ledger).Where(l => l.StartsWith("| 2026-", StringComparison.Ordinal)));
        Assert.Equal((null, null), (first.Branch, first.Commit));
    }

    /// <summary>
    /// --commit, against a local bare origin only: the ledger is committed on HEAD's tree on a branch of its own, named for the
    /// environment and the time, and pushed; the caller's branch, index and working tree stay as they were, the ledger beside them. A
    /// second measurement that appends no row pushes nothing: the verb answers measured, with no branch, and a note that says so.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public void Counts_with_commit_pushes_the_ledger_on_a_branch_of_its_own_to_a_local_bare_origin()
    {
        using var scratch = new Scratch();
        var origin = scratch.Origin();
        using var reference = ScratchFolder.Temporary("profile-reference");
        var head = scratch.Commit("the environments file",
            ("dbchange/environments.json", uat.EnvironmentsJson(reference.Path)), ("README.md", "x\n"));
        scratch.Git("push", "-q", "origin", "main");
        var here = new Checkout(scratch.Root, scratch.Root, null, Contract.Version);

        var counted = Value(Profile.Counts(here, "uat", commit: true, SqlServer.QueryLog.Start(scratch.Root),
            new DateTimeOffset(2026, 10, 21, 9, 15, 0, TimeSpan.Zero)));
        var (exit, again) = VerbAnswer.Of(here, "profile", "--env", "uat", "--counts", "--commit");

        var commit = counted.Commit ?? throw new InvalidOperationException("--commit pushed nothing");
        Assert.Equal("dbchange/row-tiers-uat-20261021T091500Z", counted.Branch);
        Assert.Equal(["dbchange/ledgers/row-tiers.md"],
            scratch.GitAt(origin, "diff-tree", "-r", "--name-only", "--no-commit-id", head, commit).Split('\n'));
        Assert.Equal(("refs/heads/main", head), (scratch.Git("symbolic-ref", "HEAD"), scratch.Git("rev-parse", "HEAD")));
        Assert.Contains("| 2026-10-21 | uat | [dbo].[Customer] | 1–1k |",
            scratch.GitAt(origin, "show", commit + ":dbchange/ledgers/row-tiers.md"), StringComparison.Ordinal);
        Assert.Equal((0, "measured", null), (exit, (string?)again["outcome"], (string?)again["rowTiers"]!["branch"]));
        Assert.Equal(["profile.unpushed"], again["findings"]!.AsArray().Select(f => (string)f!["code"]!));
        Assert.Equal([commit], scratch.GitAt(origin, "for-each-ref", "--format=%(objectname)", "refs/heads/dbchange/").Split('\n'));
    }

    /// <summary>Every file under the folder but the two the test wrote, the environments file and the connection's, their text joined,
    /// for a search of what the run wrote anywhere.</summary>
    private static string Written(string folder) => string.Join('\n',
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".connection", StringComparison.Ordinal) && Path.GetFileName(f) != "environments.json")
            .Select(File.ReadAllText));
}

/// <summary>
/// The environment env:uat, classified real: a registered database holding dbo.Customer, dbo.Empty, dbo.Growing and dbo.Thousands, read
/// as its read-only principal, whose connection a file outside git holds; and the repository model a declared witness is held to.
/// </summary>
public sealed class RealEnvironment : IAsyncLifetime
{
    /// <summary>PlantedValue's values, one per row: each unique to its row.</summary>
    public static readonly IReadOnlyList<string> Planted = [.. Enumerable.Range(0, 6).Select(_ => PlantedValue.Unique().Text)];

    /// <summary>Addresses and phone numbers unique enough to identify a row, each held once.</summary>
    public static readonly IReadOnlyList<string> Identifying =
    [
        .. Planted, "amara.okafor@corp.example", "liu.wen@corp.example", "noor.haddad@corp.example", "ines.duarte@corp.example",
        "tomas.berg@corp.example", "keoni.kahale@corp.example", "+1 (206) 555-0142", "+1 (425) 555-0177", "+1 (360) 555-0108",
        "+1 (509) 555-0163", "+1 (253) 555-0191", "31/12/1990", "07/03/1984", "22/11/2001", "15/06/1975", "JOSÉ",
    ];

    /// <summary>Names in other scripts and accented Latin names, each held more than once.</summary>
    public static readonly IReadOnlyList<string> Names = ["王小明", "José", "Zoë", "Иван", "иван", "Пётр"];

    /// <summary>dbo.Customer.Email's values, NULL included: the identifying ones once, and the shapes held more than once.</summary>
    public static readonly IReadOnlyList<string?> Emails =
    [
        .. Identifying, .. Enumerable.Repeat("--", 6), .. Enumerable.Repeat("王小明", 6), .. Enumerable.Repeat("José", 3),
        .. Enumerable.Repeat("Zoë", 2), .. Enumerable.Repeat("   ", 5), "N/A", "", "", null, null, null,
    ];

    /// <summary>dbo.Customer.Note's values, a varchar column under a UTF-8 collation, in turn: characters outside the Basic Multilingual
    /// Plane, which UTF-16 writes as two code units, and a U+0000 among letters.</summary>
    public static readonly IReadOnlyList<string> Notes = ["José 😀😀 東京", "a\0b", "Ab-12"];

    /// <summary>The repository's model of dbo.Customer, which a declared witness is held to.</summary>
    internal static Witnesses.Repository Model { get; } = Modelled();

    private RegisteredDatabase? database;
    private ReadOnlyPrincipal? reader;

    public RegisteredDatabase Database => database!;

    public async Task InitializeAsync()
    {
        database = await SqlServerFixture.RegisterAsync();
        await SqlServerFixture.ExecuteAsync(database.ConnectionString,
            "CREATE TABLE dbo.Customer (Id INT IDENTITY PRIMARY KEY, Email NVARCHAR(200) COLLATE SQL_Latin1_General_CP1_CI_AS NULL, "
            + "Name NVARCHAR(100) NULL, Code VARCHAR(40) COLLATE Cyrillic_General_CI_AS NULL, Age INT NULL, "
            + "Note VARCHAR(100) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL); "
            + "CREATE TABLE dbo.Empty (Id INT NOT NULL PRIMARY KEY); CREATE TABLE dbo.Growing (Id INT NOT NULL PRIMARY KEY); "
            + "CREATE TABLE dbo.Thousands (Id INT NOT NULL PRIMARY KEY); "
            + "INSERT INTO dbo.Thousands (Id) SELECT TOP (1001) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_columns AS a "
            + "CROSS JOIN sys.all_columns AS b;");
        foreach (var (email, i) in Emails.Select((e, i) => (e, i)))
        {
            await SqlServerFixture.ExecuteAsync(database.ConnectionString,
                "INSERT INTO dbo.Customer (Email, Name, Code, Age, Note) VALUES (" + (email is null ? "NULL" : "@name")
                    + ", @n, @code, @age, @note);",
                email,
                ("@n", Names[i % 2]), ("@code", ((string[])["Иван", "Иван", "Иван", "Иван", "Иван", "иван", "Пётр"])[i % 7]),
                ("@age", (20 + i).ToString(CultureInfo.InvariantCulture)), ("@note", Notes[i % Notes.Count]));
        }

        reader = await ReadOnlyPrincipal.CreateAsync(database);
    }

    public async Task DisposeAsync()
    {
        if (database is not null)
        {
            await database.DisposeAsync();
        }
    }

    /// <summary>A checkout at <paramref name="root"/> whose environments file names env:uat, classified real, its connection in a
    /// file there that git ignores.</summary>
    public Checkout Repository(string root)
    {
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(root, "dbchange")).FullName, "environments.json"),
            EnvironmentsJson(root));
        return new Checkout(root, root, null, Contract.Version);
    }

    /// <summary>The environments file naming env:uat, classified real, its connection written to a file under
    /// <paramref name="folder"/>, which its owner alone reads.</summary>
    public string EnvironmentsJson(string folder)
    {
        var file = Path.Combine(folder, "uat.connection");
        File.WriteAllText(file, reader!.ConnectionString);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return DbChange.Tests.EnvironmentsJson.Of("uat",
            new("file:" + file.Replace('\\', '/'), "uat-sql.corp.example", Classification: "real")).Json();
    }

    private static Witnesses.Repository Modelled()
    {
        using var model = new Microsoft.SqlServer.Dac.Model.TSqlModel(Microsoft.SqlServer.Dac.Model.SqlServerVersion.Sql160,
            new Microsoft.SqlServer.Dac.Model.TSqlModelOptions());
        model.AddObjects("CREATE TABLE dbo.Customer (Id INT IDENTITY PRIMARY KEY, Email NVARCHAR(200) NULL, Name NVARCHAR(100) NULL, "
            + "Code VARCHAR(40) NULL, Age INT NULL);");
        var elements = Value(Ssdt.ReadModel(model));
        return Witnesses.Repository.Of(elements, Value(Schema.Of(elements)));
    }
}
