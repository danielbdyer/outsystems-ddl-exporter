using System.Linq;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// io/PlanStatements, the listing kernel/Vocabulary reads (WP 2.1's second slice): a deploy script's statements found in DacFx's script by
/// the script's text, each at the script's own line, and DacFx's own at the plan's; the statements an IF, a BEGIN … END and a TRY … CATCH
/// hold listed one deeper, and a WHILE's body not; an ALTER TABLE of several elements one statement for each, and a CREATE TABLE one for
/// the table and one for each key constraint it declares; an index's INCLUDE columns apart from its key; and a deploy script the plan does
/// not hold verbatim refused.
/// </summary>
public sealed class PlanStatementsTests
{
    private const string Pre = "\n\n-- the backfill\nUPDATE [dbo].[Customer] SET [Email] = N'' WHERE [Email] IS NULL;\n";

    private const string Post = "PRINT N'Seeded.';";

    [Fact]
    [Trait("Category", "fast")]
    public void A_deploy_script_s_statements_stand_at_the_script_s_own_lines_and_DacFx_s_at_the_plan_s()
    {
        var plan = "PRINT N'header';\nGO\n" + Pre.Trim() + "\nGO\nALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;\nGO\n"
            + Post + "\nGO\n";

        var listing = Expect.Value(PlanStatements.Read(plan, [], new(Pre, Post), new(null, null)));

        Assert.Equal(["Plan 1 PrintStatement", "PreDeploy 4 UpdateStatement", "Plan 6 AlterTableAlterColumnStatement",
            "PostDeploy 1 PrintStatement"], listing.Plan.Select(s => s.Part + " " + s.Line + " " + s.Type));
        Assert.Empty(listing.ScriptsBefore);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void An_IF_a_BEGIN_END_and_a_TRY_CATCH_list_the_statements_they_hold_one_deeper_and_a_WHILE_none()
    {
        const string plan = "IF @@TRANCOUNT = 0 BEGIN PRINT N'a'; END\nGO\nBEGIN TRY PRINT N'b'; END TRY BEGIN CATCH PRINT N'c'; END CATCH\n"
            + "GO\nWHILE @@FETCH_STATUS = 0 BEGIN PRINT N'd'; END\nGO\n";

        var listing = Expect.Value(PlanStatements.Read(plan, [], new(null, null), new(null, null)));

        Assert.Equal(["0 IfStatement @@TRANCOUNT", "1 BeginEndBlockStatement", "2 PrintStatement", "0 TryCatchStatement", "1 PrintStatement",
            "1 PrintStatement", "0 WhileStatement @@FETCH_STATUS"], listing.Plan.Select(s => s.Depth + " " + s.Type + Words(s)));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void An_ALTER_TABLE_of_several_elements_is_one_statement_for_each_with_its_own_words_and_columns()
    {
        const string plan = "ALTER TABLE [dbo].[T] WITH NOCHECK ADD [A] INT NULL, [B] AS ([A] * 2) PERSISTED,\n"
            + "    CONSTRAINT [UQ_T_A] UNIQUE CLUSTERED ([A]), FOREIGN KEY ([A]) REFERENCES [dbo].[P] ([Id]);\nGO\n";

        var listing = Expect.Value(PlanStatements.Read(plan, [], new(null, null), new(null, null)));

        Assert.Equal(["1 [A] COLUMN|NULL|WITH NOCHECK", "1 [B] AS|COLUMN|PERSISTED|WITH NOCHECK",
            "1 [A] CLUSTERED|UNIQUE|WITH NOCHECK Element:[UQ_T_A]", "1 [A] FOREIGN KEY|WITH NOCHECK Referenced:[dbo].[P]"],
            listing.Plan.Select(s => s.Line + " " + string.Join(",", s.Columns) + " " + string.Join("|", s.Words)
                + string.Concat(s.Objects.Where(o => o.Role != PlanRole.Target).Select(o => " " + o.Role + ":" + o.Name))));
        Assert.All(listing.Plan, s => Assert.Equal(("AlterTableAddTableElementStatement", "[dbo].[T]"), (s.Type, s.Target!.Name.ToString())));
    }

    /// <summary>
    /// A rebuild's CREATE TABLE declares its keys inline, a table's and a column's, where an ALTER TABLE would add them: each is a statement
    /// of its own over its key's columns, with its words, so the vocabulary reads a key over its limit there; and a CREATE INDEX's INCLUDE
    /// columns stand apart from its key, which they never widen.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_CREATE_TABLE_is_one_statement_for_the_table_and_one_for_each_key_it_declares_and_an_index_s_INCLUDE_stands_apart_from_its_key()
    {
        const string plan = "CREATE TABLE [dbo].[tmp_ms_xx_T] ([Id] INT NOT NULL PRIMARY KEY NONCLUSTERED, [Code] NVARCHAR (50) NOT NULL,\n"
            + "    [N] AS ([Id] * 2) PERSISTED, CONSTRAINT [tmp_ms_xx_constraint_UQ_T1] UNIQUE CLUSTERED ([Code] ASC, [Id] ASC));\nGO\n"
            + "CREATE NONCLUSTERED INDEX [IX_T_Code] ON [dbo].[T]([Code] ASC) INCLUDE([N], [Id]);\nGO\n";

        var listing = Expect.Value(PlanStatements.Read(plan, [], new(null, null), new(null, null)));

        Assert.Equal(["CreateTableStatement [Code],[Id],[N] PERSISTED",
            "CreateTableStatement [Code],[Id] CLUSTERED|UNIQUE Element:[tmp_ms_xx_constraint_UQ_T1]", "CreateTableStatement [Id] PRIMARY KEY",
            "CreateIndexStatement [Code] Element:[IX_T_Code] INCLUDE [Id],[N]"],
            listing.Plan.Select(s => s.Type + " " + string.Join(",", s.Columns) + Words(s)
                + string.Concat(s.Objects.Where(o => o.Role != PlanRole.Target).Select(o => " " + o.Role + ":" + o.Name))
                + (s.Included.Count == 0 ? "" : " INCLUDE " + string.Join(",", s.Included))));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_deploy_script_with_a_statement_the_plan_does_not_hold_verbatim_is_refused()
    {
        var error = Expect.Failed(PlanStatements.Read("PRINT N'Update complete.';\nGO\n", [], new("PRINT N'Other';", null), new(null, null)),
            "plan.deploy-script");

        Assert.Contains("pre-deploy script", error.Message, System.StringComparison.Ordinal);
        Assert.Equal(0, Expect.Value(PlanStatements.Read("PRINT N'a';\n", [], new("-- a comment alone\n", null), new(null, null)))
            .Plan.Count(s => s.Part != ScriptPart.Plan));
    }

    /// <summary>
    /// What classify reads of a pre-deploy script: each INSERT, UPDATE, DELETE, MERGE and TRUNCATE TABLE by its target and verb, one inside
    /// an IF included, a temporary table's and DacFx's refactorlog table's left out, a table named through a SQLCMD variable read under
    /// the value the script's :setvar gives it, or under the variable's name where none does; a script with none, or no script, modifies
    /// no row.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_rows_a_pre_deploy_script_modifies_are_each_statement_s_target_and_verb_temporary_tables_left_out()
    {
        const string script = ":setvar Schema dbo\nIF EXISTS (SELECT 1 FROM dbo.Customer WHERE Email IS NULL)\n"
            + "    UPDATE dbo.Customer SET Email = N'x' WHERE Email IS NULL;\nGO\nTRUNCATE TABLE [$(Schema)].[Staging];\n"
            + "INSERT INTO #seen (Id) SELECT Id FROM dbo.Customer;\nDELETE FROM dbo.[__RefactorLog] WHERE 1 = 0;\n"
            + "MERGE dbo.Region AS r USING (VALUES (1)) AS v(Id) ON r.Id = v.Id WHEN NOT MATCHED THEN INSERT (Id) VALUES (v.Id);\n"
            + "DELETE FROM [$(Other)].[Archive] WHERE 1 = 0;\nPRINT N'done';\n";

        var rows = Expect.Value(PlanStatements.RowsModified(script));

        Assert.Equal(["[Other].[Archive] DELETE", "[dbo].[Customer] UPDATE", "[dbo].[Region] MERGE", "[dbo].[Staging] TRUNCATE TABLE"],
            rows.OfType<ScriptStatement.ModifiesRows>().Select(m => m.Table + " " + m.Verb).Order(System.StringComparer.Ordinal));
        Assert.Empty(Expect.Value(PlanStatements.RowsModified("PRINT N'Pre-deploy: no backfill active.';\nGO\n")));
        Assert.Empty(Expect.Value(PlanStatements.RowsModified(null)));
    }

    private static string Words(PlanStatement s) => s.Words.Count == 0 ? "" : " " + string.Join("|", s.Words);
}
