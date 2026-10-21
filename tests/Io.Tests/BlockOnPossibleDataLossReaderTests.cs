using System;
using System.IO;
using System.Linq;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// io/BlockOnPossibleDataLoss, the reader of WP 2.2 (V3_MILESTONES.md §8), over scripts written in DacFx's own form: SQLCMD directive lines
/// dropped, each $(var) substituted in memory, the rest parsed by TSql160Parser, and each IF EXISTS (…) RAISERROR at state 127 (§1 fact 3)
/// read with its table, the sites its block comment names and its predicate as an aggregate query the allowlist admits. The script below
/// holds the two lookalikes spike S2 measured, the copy step of a table rebuild and the verification of a constraint added WITH NOCHECK,
/// and quotes a check in the pre-deploy script's block comment, as every golden plan does. The fixture lane holds the reader to DacFx's
/// own plans on both releases (BlockOnPossibleDataLossCheckTests).
/// </summary>
public sealed class BlockOnPossibleDataLossReaderTests
{
    private const string Raise =
        "    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT\r\n";

    private const string ContactPhone = "The type for column ContactPhone in table [dbo].[Customer] is currently  NVARCHAR (40) NULL but is "
        + "being changed to  NVARCHAR (20) NULL. Data loss could occur and deployment may fail if the column contains data that is "
        + "incompatible with type  NVARCHAR (20) NULL.";

    private const string Email = "The column Email on table [dbo].[Customer] must be changed from NULL to NOT NULL. If the table contains "
        + "data, the ALTER script may not work.";

    private const string LegacyCode = "The column [dbo].[Product].[LegacyCode] is being dropped, data loss could occur.";

    private static readonly PlantedValue Token = PlantedValue.Password;

    /// <summary>A plan's script as DacFx writes one, CRLF line ends included, around the checks <paramref name="checks"/> holds.</summary>
    private static string Script(string checks) => string.Join("\r\n",
        "/*", "Deployment script for Estate", "*/", "", "GO", "SET ANSI_NULLS, QUOTED_IDENTIFIER ON;", "", "GO",
        ":setvar DatabaseName \"Estate\"", ":setvar DefaultFilePrefix \"Estate\"", "", "GO", ":on error exit", "GO",
        ":setvar __IsSqlCmdEnabled \"True\"", "GO", "IF N'$(__IsSqlCmdEnabled)' NOT LIKE N'True'", "    BEGIN", "        SET NOEXEC ON;",
        "    END", "", "", "GO", "USE [$(DatabaseName)];", "", "", "GO", "/*", "  The pre-deploy script quotes the check DacFx writes:",
        "      IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer])", "          RAISERROR (N'Rows were detected.', 16, 127)", "*/",
        "PRINT N'Pre-deploy for $(EnvironmentTag) as $(ServiceToken).';", "GO", "") + checks + string.Join("\r\n",
        "IF EXISTS (SELECT TOP 1 1 ", "           FROM   [dbo].[Category])", "    BEGIN",
        "        INSERT INTO [dbo].[tmp_ms_xx_Category] ([Id], [Code])", "        SELECT   [Id], [Code]", "        FROM     [dbo].[Category];",
        "    END", "", "GO", "ALTER TABLE [dbo].[Order] WITH NOCHECK ADD CHECK ([Total] >= 0);", "", "GO",
        "SELECT N'Constraint verification failed:' + [Schema] + N'.' + [Table] + N',' + [Constraint]", "FROM   [#__checkStatus];", "",
        "IF @@ROWCOUNT > 0", "    BEGIN", "        DROP TABLE [#__checkStatus];",
        "        RAISERROR (N'An error occurred while verifying constraints', 16, 127);", "    END", "", "GO",
        "ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;", "", "GO", "");

    /// <summary>The two checks of a plan that narrows and makes mandatory two columns of Customer and drops one of Product.</summary>
    private static readonly string Checks = "/*\r\n" + ContactPhone + "\r\n\r\n" + Email + "\r\n*/\r\n\r\n"
        + "IF EXISTS (select top 1 1 from [dbo].[Customer])\r\n" + Raise + "\r\nGO\r\n"
        + "/*\r\n" + LegacyCode + "\r\n*/\r\n\r\n" + "IF EXISTS (select top 1 1 from [$(ProductSchema)].[Product])\r\n" + Raise + "\r\nGO\r\n";

    /// <summary>The environment's values: a literal, and the token a reference resolved to, which is marked sensitive.</summary>
    private static readonly SqlCmdValue[] Values =
    [
        new(Value(SqlCmdName.Of("test", "EnvironmentTag")), "uat", false),
        new(Value(SqlCmdName.Of("test", "ProductSchema")), "dbo", false),
        new(Value(SqlCmdName.Of("test", "ServiceToken")), Token.Text, true),
    ];

    [Fact]
    [Trait("Category", "fast")]
    public void The_reader_finds_each_IF_EXISTS_RAISERROR_at_state_127_with_its_table_and_the_sites_its_block_comment_names()
    {
        var checks = Value(BlockOnPossibleDataLoss.Read(Script(Checks), Values));

        Assert.Equal(
            [("[dbo].[Customer]", 41, "IF EXISTS (select top 1 1 from [dbo].[Customer])\n" + Raise.TrimEnd('\r', '\n')),
                ("[dbo].[Product]", 49, "IF EXISTS (select top 1 1 from [dbo].[Product])\n" + Raise.TrimEnd('\r', '\n'))],
            checks.Select(c => (c.Table.ToString(), c.Line, c.Text)));
        Assert.Equal([[Email, ContactPhone], [LegacyCode]], checks.Select(c => c.Sites.Select(s => s.Statement).ToArray()));
        Assert.All(checks, c => Assert.All(c.Sites, s => Assert.Equal(c.Table, s.Table)));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Neither_the_rebuild_s_copy_step_nor_the_constraint_verification_nor_a_check_quoted_in_a_comment_is_read_as_a_check() =>
        Assert.Empty(Value(BlockOnPossibleDataLoss.Read(Script(""), Values)));

    /// <summary>
    /// Each predicate runs as SELECT CASE WHEN EXISTS (&lt;predicate&gt;) THEN 1 ELSE 0 END, the predicate written back by io's
    /// Sql160ScriptGenerator, which is one of the allowlist's forms: the query reads again as itself, and its site names the table.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Each_check_s_predicate_runs_as_SELECT_CASE_WHEN_EXISTS_THEN_1_ELSE_0_END_and_passes_the_allowlist()
    {
        var checks = Value(BlockOnPossibleDataLoss.Read(Script(Checks), Values));

        Assert.All(checks, c =>
        {
            Assert.Equal("BlockOnPossibleDataLoss on " + c.Table, c.Query.Site);
            Assert.Equal("SELECT CASE WHEN EXISTS (SELECT TOP 1 1 FROM " + c.Table + ") THEN 1 ELSE 0 END",
                System.Text.RegularExpressions.Regex.Replace(c.Query.Statement, @"\s+", " "));
            Assert.Equal(c.Query.Statement, Value(SqlServer.AggregateQuery.Of(c.Query.Statement, c.Query.Site)).Statement);
        });
    }

    /// <summary>
    /// The predicate runs whole: Sql160ScriptGenerator writes a QuerySpecification on its own without its WHERE clause (measured on
    /// ScriptDom 180.102.0, where EXISTS (SELECT TOP 1 1 FROM [T] WHERE [Value] &gt; 0) came back as SELECT TOP 1 1 FROM [T]), so the query is
    /// written from the whole EXISTS, and a check whose predicate filters its rows runs with its filter.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_check_s_predicate_runs_with_every_clause_DacFx_wrote_its_WHERE_included()
    {
        var check = Assert.Single(Value(BlockOnPossibleDataLoss.Read(
            Script("IF EXISTS (select top 1 1 from [dbo].[Customer] where [Email] IS NULL)\r\n" + Raise + "GO\r\n"), Values)));

        Assert.Equal("SELECT CASE WHEN EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer] WHERE [Email] IS NULL) THEN 1 ELSE 0 END",
            System.Text.RegularExpressions.Regex.Replace(check.Query.Statement, @"\s+", " "));
    }

    /// <summary>A predicate outside the allowlist, a table of another database or a rowset read from outside, refuses the site, by its
    /// line in the plan's script.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("select top 1 1 from [Other].[dbo].[Customer]")]
    [InlineData("select top 1 1 from OPENROWSET(BULK N'/tmp/x', SINGLE_CLOB) AS x")]
    [InlineData("select top 1 1 from [dbo].[Customer] WITH (NOLOCK)")]
    public void A_check_whose_predicate_the_allowlist_refuses_is_refused_naming_its_line(string predicate)
    {
        var error = Failed(BlockOnPossibleDataLoss.Read(Script("IF EXISTS (" + predicate + ")\r\n" + Raise + "GO\r\n"), Values),
            "aggregate-query.refused");

        Assert.StartsWith("The BlockOnPossibleDataLoss check at line 35 of the plan's script ", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// WP 1.5's rule on the reader: the substituted text lives in memory alone, so the token a reference gave, used by the pre-deploy
    /// script, reaches no check, site, query or text the reader returns; and a check that reads a sensitive value is refused, naming the
    /// variable and never its value, since its query would write the value to the run's queries.log.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "X2")]
    public void A_SQLCMD_value_marked_sensitive_reaches_no_check_and_a_check_that_reads_one_is_refused_naming_only_its_variable()
    {
        var checks = Value(BlockOnPossibleDataLoss.Read(Script(Checks), Values));
        var refused = Failed(BlockOnPossibleDataLoss.Read(
            Script("IF EXISTS (select top 1 1 from [dbo].[Customer] where [Token] = N'$(ServiceToken)')\r\n" + Raise + "GO\r\n"), Values),
            "sqlcmd.sensitive");

        Token.AbsentFrom(string.Join('\n', checks.Select(c => c + "\n" + c.Text + "\n" + c.Query + "\n" + string.Join('\n', c.Sites))));
        Assert.Contains("$(ServiceToken)", refused.Message, StringComparison.Ordinal);
        Token.AbsentFrom(refused);
    }

    /// <summary>A script that lacks a variable's value and also holds a statement TSql160Parser does not read is sqlcmd.undefined, not
    /// plan.unparsed: the values are substituted before the script is parsed.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_variable_the_script_uses_with_no_value_is_sqlcmd_undefined_before_anything_is_parsed() =>
        Assert.Contains("$(ServiceToken)", Failed(BlockOnPossibleDataLoss.Read(
            Script(Checks + "INSERT INTO [dbo].[Customer] VALUES (,,);\r\nGO\r\n"), Values[..2]), "sqlcmd.undefined").Message,
            StringComparison.Ordinal);

    /// <summary>A script TSql160Parser does not read is refused by the line and column the parser names, quoting none of it, since a
    /// deployment script carries the project's own seed values.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_script_TSql160Parser_does_not_read_is_plan_unparsed_naming_its_line_and_quoting_nothing()
    {
        var error = Failed(BlockOnPossibleDataLoss.Read(Script("INSERT INTO [dbo].[Customer] VALUES (N'" + Token.Text + "' ,,);\r\nGO\r\n"),
            Values), "plan.unparsed");

        Assert.Contains("line 35", error.Message, StringComparison.Ordinal);
        Token.AbsentFrom(error);
    }

    /// <summary>WP 2.1a's site record, filled: Preconditions.Of reads a TableEmpty on each table a check reads, however many sites the
    /// check's comment names.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_sites_the_reader_fills_give_Preconditions_Of_one_TableEmpty_on_each_table_a_check_reads()
    {
        var sites = BlockOnPossibleDataLoss.Sites(Value(BlockOnPossibleDataLoss.Read(Script(Checks), Values)));

        Assert.Equal(3, sites.Count);
        Assert.Equal(["TableEmpty on [dbo].[Customer]", "TableEmpty on [dbo].[Product]"],
            Value(Preconditions.Of([], [], Value(Change.Between([], [], [])), sites)).Select(p => p.ToString()));
    }

    /// <summary>The committed checks spike S2 extracted read as themselves: the reader finds in each file what S2's extraction
    /// finds.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_reader_finds_in_each_committed_file_the_checks_spike_S2_extracted()
    {
        var files = Directory.GetFiles(Path.Combine(Repository.Root, "tests", "Golden", "data-loss-checks"));

        Assert.NotEmpty(files);
        Assert.All(files, file => Assert.Equal(BlockOnPossibleDataLossChecks.In(File.ReadAllText(file)),
            Value(BlockOnPossibleDataLoss.Read(File.ReadAllText(file), [])).Select(c => c.Text)));
    }
}
