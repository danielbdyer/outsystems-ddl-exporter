using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Cli;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// dbchange classify and dbchange check cdc (WP 2.5) in a repository of their own: classify builds the project at a ref and the project as
/// the working tree holds it and classifies the change between them, the word provisional first; M2 exit 5, each rename made by typing
/// refused naming its remedy; the CDC ledger read; and the planted-value scan's first half through classify. check cdc reads
/// sys.tables.is_tracked_by_cdc as the read-only principal on SQL Server Express, which runs no Change Data Capture.
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class ClassifyVerbTests(ScratchRepository repository) : IClassFixture<ScratchRepository>
{
    private static readonly Name Customer = Expect.Value(Name.Of("dbo", "Customer"));

    public static TheoryData<string, string> TypedRenames => SampleClassificationTests.TypedRenames;

    /// <summary>
    /// M2 exit 5: classify from the golden project's commit to a working tree holding a column, a table or a schema move renamed by typing,
    /// with no refactorlog entry, refuses at exit 9; its message opens with the word provisional and ends with the refusal's code (the
    /// schema move also creates the schema [sales], which no operation names), and its one error names the remedy dbchange diff
    /// --refactorlog, in the JSON answer and in the Markdown one.
    /// </summary>
    [Theory]
    [Trait("Category", "build")]
    [Trait("Exit", "M2.5")]
    [Trait("Value", "S4")]
    [MemberData(nameof(TypedRenames))]
    public void Classify_on_each_rename_made_by_typing_refuses_at_exit_9_and_names_the_remedy_dbchange_diff_refactorlog(string name,
        string code)
    {
        repository.Applied(GoldenProject.Change(name));
        try
        {
            var (exit, answer) = repository.Answer("classify", "--at", repository.Base);
            var (printedExit, printed) = repository.Run("classify", "--at", "ref:" + repository.Base);

            Assert.True(exit == 9, answer.ToJsonString());
            Assert.Equal("refused", (string?)answer["outcome"]);
            Assert.Matches("^provisional: (unclassified change: prove it; )?refused: " + Regex.Escape(code) + "$", (string?)answer["message"]);
            var refusal = Assert.Single(answer["findings"]!.AsArray(), f => (string?)f!["severity"] == "error")!;
            Assert.Equal(code, (string?)refusal["code"]);
            Assert.StartsWith("Run dbchange diff --refactorlog to write the entry", (string?)refusal["remedy"], StringComparison.Ordinal);
            Assert.Equal(("ref:" + repository.Base, repository.Base),
                ((string?)answer["classify"]!["at"], (string?)answer["classify"]!["commit"]));
            Assert.Equal(9, printedExit);
            Assert.StartsWith((string?)answer["message"] + "\n", printed, StringComparison.Ordinal);
            Assert.Contains("- error `" + code + "` ", printed, StringComparison.Ordinal);
            Assert.Contains("Remedy: Run dbchange diff --refactorlog to write the entry", printed, StringComparison.Ordinal);
        }
        finally
        {
            repository.Restored();
        }
    }

    /// <summary>
    /// A change a recognize: block names, a nullable column added: exit 0, outcome classified, the message the provisional summary, the
    /// Markdown the summary and then the operation with its element, and the JSON answer valid against dbchange.classify/1 naming the
    /// operation, its element and how it ships.
    /// </summary>
    [Fact]
    [Trait("Category", "build")]
    [Trait("Value", "A1")]
    public void Classify_prints_the_word_provisional_first_then_each_operation_and_its_answer_validates_against_dbchange_classify_1()
    {
        repository.Applied(GoldenProject.Change("add-a-nullable-column"));
        try
        {
            var (exit, answer) = repository.Answer("classify", "--at", repository.Base);
            var (_, printed) = repository.Run("classify", "--at", repository.Base);

            Assert.True(exit == 0, answer.ToJsonString());
            Assert.Equal(("classified", "provisional: add-optional"), ((string?)answer["outcome"], (string?)answer["message"]));
            var operation = Assert.Single(answer["classify"]!["operations"]!.AsArray())!;
            Assert.Equal(("add-optional", "Column [dbo].[Customer].[Nickname]"),
                ((string?)operation["operation"], (string?)operation["subject"]));
            Assert.False(string.IsNullOrEmpty((string?)operation["ships"]));
            Assert.Null(answer["classify"]!["environment"]);
            Assert.Empty(answer["classify"]!["cdcTracked"]!.AsArray());
            Assert.Equal(["provisional: add-optional", "add-optional Column [dbo].[Customer].[Nickname]"], printed.Split('\n').Take(2));
        }
        finally
        {
            repository.Restored();
        }
    }

    /// <summary>
    /// A column added to a table dbchange/ledgers/cdc-tracked.md names: refused at exit 9 with cdc.column-list, the table named among the
    /// tracked; and, with no environment named, a warning that no deploy plan was read, so a rebuild of the table is not checked.
    /// </summary>
    [Fact]
    [Trait("Category", "build")]
    [Trait("Value", "S8")]
    public void A_column_added_to_a_table_the_cdc_ledger_names_is_refused_at_exit_9_naming_the_capture_instance_step()
    {
        var ledger = Path.Combine(repository.Root, CdcLedger.File);
        File.WriteAllText(ledger, CdcLedger.Written([], Expect.Value(EnvironmentName.Of("--env", "dev")), [Customer], "2026-10-21"));
        repository.Applied(GoldenProject.Change("add-a-nullable-column"));
        try
        {
            var (exit, answer) = repository.Answer("classify", "--at", repository.Base);

            Assert.True(exit == 9, answer.ToJsonString());
            Assert.Equal("provisional: add-optional; refused: cdc.column-list", (string?)answer["message"]);
            Assert.Equal(["[dbo].[Customer]"], answer["classify"]!["cdcTracked"]!.AsArray().Select(t => (string?)t));
            var codes = answer["findings"]!.AsArray().Select(f => ((string?)f!["severity"], (string?)f["code"])).ToList();
            Assert.Contains(("error", "cdc.column-list"), codes);
            Assert.Contains(("warning", "classify.no-plan"), codes);
        }
        finally
        {
            File.Delete(ledger);
            repository.Restored();
        }
    }

    /// <summary>
    /// M2 exit 4's first half through classify (R7, R16): a fixture environment classified real, read as the read-only principal, whose
    /// dbo.Product holds a planted value in Code, classified against a working tree that retypes Code from nvarchar to int, a conversion
    /// that fails on the value. classify plans the working tree's package against the environment and answers exit 0; the value appears in
    /// none of its JSON answer, its Markdown answer and the run's queries.log.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Exit", "M2.4")]
    [Trait("Value", "X2")]
    public async Task Classify_against_an_environment_classified_real_holding_a_planted_value_prints_answers_and_logs_no_value()
    {
        await using var database = await SqlServerFixture.RegisterAsync();
        GoldenProject.Publish(await GoldenProject.Built(), database, GoldenProject.Pipeline());
        var planted = PlantedValue.Unique();
        await SqlServerFixture.ExecuteAsync(database.ConnectionString,
            "INSERT INTO dbo.Product (Code, LegacyCode) VALUES (@value, N'LGC-1');", null, ("@value", planted.Text));
        var reader = await ReadOnlyPrincipal.CreateAsync(database);
        var root = repository.Named(("dev", Path.Combine(Repository.Root, reader.Reference["file:".Length..])));
        repository.Applied(GoldenProject.Change("retype-nvarchar-to-int"));
        try
        {
            var (exit, answer, printed) = VerbAnswer.Printing(new Checkout(root, root, repository.Tool.Folder, Contract.Version),
                "classify", "--at", repository.Base, "--env", "dev");
            var (markdownExit, markdown) = repository.RunAt(root, "classify", "--at", repository.Base, "--env", "dev");
            var log = File.ReadAllText(Path.Combine(root, (string)answer["log"]!));

            Assert.True(exit == 0, printed);
            Assert.True(markdownExit == 0, markdown);
            Assert.Equal(("provisional: retype-explicit", "env:dev"),
                ((string?)answer["message"], (string?)answer["classify"]!["environment"]));
            Assert.Contains("HAS_PERMS_BY_NAME", log, StringComparison.Ordinal);   // the run reached the environment
            planted.AbsentFrom(printed);
            planted.AbsentFrom(markdown);
            planted.AbsentFrom(log);
        }
        finally
        {
            repository.Restored();
        }
    }

    /// <summary>
    /// check cdc --env as the read-only principal, on SQL Server Express, which runs no Change Data Capture: exit 0, outcome listed, no
    /// table tracked of those the project declares at HEAD; every statement the run logs under check cdc is an aggregate query the allowlist admits,
    /// each answered with one row.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "S8")]
    [Trait("Value", "P2")]
    public async Task Check_cdc_as_the_read_only_principal_on_SQL_Server_Express_lists_no_tracked_table_and_logs_only_admitted_aggregate_queries()
    {
        await using var database = await SqlServerFixture.RegisterAsync();
        GoldenProject.Publish(await GoldenProject.Built(), database, GoldenProject.Pipeline());
        var reader = await ReadOnlyPrincipal.CreateAsync(database);
        var root = repository.Named(("dev", Path.Combine(Repository.Root, reader.Reference["file:".Length..])));

        var (exit, answer) = repository.AnswerAt(root, "check", "cdc", "--env", "dev");
        var log = File.ReadAllText(Path.Combine(root, (string)answer["log"]!));

        Assert.True(exit == 0, answer.ToJsonString());
        Assert.Equal("listed", (string?)answer["outcome"]);
        Assert.Empty(answer["cdc"]!["tracked"]!.AsArray());
        Assert.True((int)answer["cdc"]!["counts"]!["declared"]! > 0, answer.ToJsonString());
        Assert.Equal((repository.Head, "dev"), ((string?)answer["cdc"]!["commit"], (string?)answer["cdc"]!["environment"]));
        var queries = Regex.Matches(log, @"^-- \S+ \S+ (?<site>check cdc[^\n]*): (?<outcome>[^\n]+)\n(?<statement>.*?)\nGO$",
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.NotEmpty(queries);
        Assert.All(queries, p => Assert.Equal("1 row", p.Groups["outcome"].Value));
        Assert.All(queries, p => Expect.Value(SqlServer.AggregateQuery.Of(p.Groups["statement"].Value, "logged")));
    }
}
