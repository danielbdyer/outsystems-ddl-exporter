using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DbChangeLite;
using Xunit;

namespace DbChangeLite.Tests;

public sealed class RulesTests : IDisposable
{
    private const string EnvironmentModel = "CREATE TABLE [dbo].[Customer] ([Id] INT NOT NULL IDENTITY PRIMARY KEY, [Email] NVARCHAR(256) NULL, [AccountId] INT NULL, [Region] NVARCHAR(40) NULL);"
        + " CREATE TABLE [dbo].[Account] ([Id] INT NOT NULL PRIMARY KEY); CREATE TABLE [dbo].[Status] ([Id] INT NOT NULL PRIMARY KEY, [Code] NVARCHAR(20) NOT NULL);";

    private const string Raise = "RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT";

    private readonly Scratch scratch = new();
    private readonly Model environment;

    public RulesTests() => environment = Model.Load(Dacpacs.Of(scratch.Under("environment.dacpac"), EnvironmentModel));

    [Fact]
    [Trait("Category", "fast")]
    public void Each_precondition_kind_is_read_from_its_statement_in_v3s_spelling_and_its_query_is_one_select()
    {
        var script = Scripts.Read(
            "ALTER TABLE [dbo].[Customer] ADD CONSTRAINT [FK_Customer_Account_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Account] ([Id]);\nGO\n"
            + "ALTER TABLE [dbo].[Customer] ADD CONSTRAINT [CK_Customer_Region] CHECK ([Region] IN (N'West', N'East'));\nGO\n"
            + "CREATE UNIQUE NONCLUSTERED INDEX [UX_Customer_Email] ON [dbo].[Customer]([Email] ASC) WHERE [Email] IS NOT NULL;\nGO\n"
            + "ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;\nGO\n"
            + "IF EXISTS (select top 1 1 from [dbo].[Customer])\n    " + Raise + "\nGO\n"
            + "CREATE TABLE [dbo].[Priority] ([Id] INT NOT NULL CONSTRAINT [PK_Priority] PRIMARY KEY, [Code] NVARCHAR(20) NOT NULL);\nGO\n");
        var found = Preconditions.Of(script, BlockOnPossibleDataLoss.Checks(script), environment);
        Assert.Equal(
            ["TableEmpty on [dbo].[Customer]", "ForeignKey on [dbo].[Customer] ([AccountId])", "Check on [dbo].[Customer] ([Region])", "Unique on [dbo].[Customer] ([Email])",
             "NotNull on [dbo].[Customer] ([Email])", "Unique on [dbo].[Priority] ([Id])"],
            found.Select(p => p.Spelled).ToArray());
        Assert.All(found, p => Session.OneSelect(p.Query));
        Assert.All(found, p => Session.OneSelect(p.CountQuery));
        Assert.Contains("NOT EXISTS (SELECT 1 FROM [dbo].[Account] AS parent WHERE parent.[Id] = child.[AccountId])", found[1].Query, StringComparison.Ordinal);
        Assert.Contains("WHERE NOT ([Region] IN (N'West', N'East'))", found[2].Query, StringComparison.Ordinal);
        Assert.Contains("WHERE [Email] IS NOT NULL GROUP BY [Email] HAVING COUNT(*) > 1", found[3].Query, StringComparison.Ordinal);
        Assert.True(found[5].OnNewObject);
        Assert.False(found[1].OnNewObject);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_column_already_not_null_in_the_environment_yields_no_NotNull_precondition()
    {
        var script = Scripts.Read("ALTER TABLE [dbo].[Status] ALTER COLUMN [Code] NVARCHAR (20) NOT NULL;\nGO\n");
        Assert.Empty(Preconditions.Of(script, [], environment));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_rebuild_is_read_from_its_copy_step_and_a_lost_identity_beside_it()
    {
        var script = Scripts.Read(
            "CREATE TABLE [dbo].[tmp_ms_xx_Customer] ([Id] INT NOT NULL, [Email] NVARCHAR(256) NULL);\nGO\n"
            + "IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) BEGIN INSERT INTO [dbo].[tmp_ms_xx_Customer] ([Id], [Email]) SELECT [Id], [Email] FROM [dbo].[Customer]; END\nGO\n"
            + "DROP TABLE [dbo].[Customer];\nGO\nEXECUTE sp_rename N'[dbo].[tmp_ms_xx_Customer]', N'Customer';\nGO\n");
        var shape = NotPredictable.Shape(script);
        Assert.Equal(["[dbo].[Customer]"], shape.Rebuilt.ToArray());
        Assert.Empty(shape.Dropped);
        var whys = NotPredictable.Of(script, shape, environment, []).Select(u => u.Why).ToArray();
        Assert.Equal(["rebuild", "identity"], whys);
        Assert.Empty(NotPredictable.Contract(script, shape, environment));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_constraint_revalidated_with_check_is_re_trust_unless_the_plan_adds_it()
    {
        var existing = Scripts.Read("ALTER TABLE [dbo].[Customer] WITH CHECK CHECK CONSTRAINT [FK_Existing];\nGO\n");
        Assert.Equal(["re-trust"], NotPredictable.Of(existing, NotPredictable.Shape(existing), environment, []).Select(u => u.Why).ToArray());
        var added = Scripts.Read("ALTER TABLE [dbo].[Customer] WITH NOCHECK ADD CONSTRAINT [FK_New] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Account] ([Id]);\nGO\n"
            + "ALTER TABLE [dbo].[Customer] WITH CHECK CHECK CONSTRAINT [FK_New];\nGO\n");
        Assert.Empty(NotPredictable.Of(added, NotPredictable.Shape(added), environment, []));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_seed_that_writes_a_table_the_plan_alters_is_authored_and_one_on_a_new_or_untouched_table_is_settled()
    {
        var seed = "MERGE [dbo].[Status] AS target USING (VALUES (1, N'Low')) AS source ([Id], [Code]) ON target.[Id] = source.[Id] WHEN NOT MATCHED BY TARGET THEN INSERT ([Id], [Code]) VALUES (source.[Id], source.[Code]);";
        var altered = Scripts.Read("ALTER TABLE [dbo].[Status] ADD [Label] NVARCHAR (40) NOT NULL;\nGO\n" + seed + "\nGO\n", post: seed);
        var unsettled = NotPredictable.Of(altered, NotPredictable.Shape(altered), environment, []);
        Assert.Equal([("authored", "[dbo].[Status]")], unsettled.Select(u => (u.Why, u.Table)).ToArray());
        var created = Scripts.Read("CREATE TABLE [dbo].[Status] ([Id] INT NOT NULL PRIMARY KEY, [Code] NVARCHAR(20) NOT NULL);\nGO\n" + seed + "\nGO\n", post: seed);
        Assert.Empty(NotPredictable.Of(created, NotPredictable.Shape(created), environment, []));
        var untouched = Scripts.Read("ALTER TABLE [dbo].[Customer] ADD [X] INT NULL;\nGO\n" + seed + "\nGO\n", post: seed);
        Assert.Empty(NotPredictable.Of(untouched, NotPredictable.Shape(untouched), environment, []));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_table_the_cdc_ledger_names_is_not_predictable_when_the_plan_touches_it()
    {
        var script = Scripts.Read("ALTER TABLE [dbo].[Customer] ADD [X] INT NULL;\nGO\n");
        var tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "[dbo].[Customer]" };
        Assert.Equal([("cdc", "[dbo].[Customer]")], NotPredictable.Of(script, NotPredictable.Shape(script), environment, tracked).Select(u => (u.Why, u.Table)).ToArray());
        Assert.Empty(NotPredictable.Of(script, NotPredictable.Shape(script), environment, new HashSet<string> { "[dbo].[Account]" }));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Statements_outside_the_closed_list_are_unrecognised_and_dacfx_own_forms_are_admitted()
    {
        var update = Scripts.Read("UPDATE [dbo].[Customer] SET [Email] = N'' WHERE [Email] IS NULL;\nGO\n");
        Assert.Equal(["unrecognised"], NotPredictable.Of(update, NotPredictable.Shape(update), environment, []).Select(u => u.Why).ToArray());
        var dynamic = Scripts.Read("EXECUTE sp_executesql N'SELECT 1';\nGO\n");
        Assert.Equal(["unrecognised"], NotPredictable.Of(dynamic, NotPredictable.Shape(dynamic), environment, []).Select(u => u.Why).ToArray());
        var admitted = Scripts.Read(
            "EXECUTE sp_rename N'[dbo].[Customer].[Region]', N'Area', N'COLUMN';\nGO\n"
            + "INSERT INTO [dbo].[__RefactorLog] (OperationKey) VALUES ('6d1c1b5e-3f0a-4c2e-9b7d-2a4f8e6c0d13');\nGO\n"
            + "CREATE TABLE [#__checkStatus] (id INT, schema_name NVARCHAR(128));\nINSERT INTO [#__checkStatus] SELECT 1, N'dbo';\nGO\n"
            + "DECLARE @schemaname NVARCHAR(256), @tablename NVARCHAR(256), @checkname NVARCHAR(256), @is_not_trusted INT, @statement NVARCHAR(1024);\n"
            + "SET @statement = N'ALTER TABLE [' + @schemaname + N'].[' + @tablename + N'] WITH ' + CASE @is_not_trusted WHEN 0 THEN N'CHECK' ELSE N'NOCHECK' END + N' CHECK CONSTRAINT [' + @checkname + N']';\n"
            + "EXECUTE [sp_executesql] @statement;\nGO\n"
            + "PRINT N'done';\nGO\nALTER DATABASE [$(DatabaseName)] SET QUERY_STORE = OFF;\nGO\n");
        Assert.Empty(NotPredictable.Of(admitted, NotPredictable.Shape(admitted), environment, []));
        Assert.Equal([("[dbo].[Customer]", "[dbo].[Customer].[Region] is renamed")], NotPredictable.Contract(admitted, NotPredictable.Shape(admitted), environment).Select(c => (c.Table, c.What)).ToArray());
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Contract_changes_name_the_drop_the_retype_the_tightening_and_the_loosening_against_the_environments_column()
    {
        var script = Scripts.Read("ALTER TABLE [dbo].[Customer] DROP COLUMN [Region];\nGO\nALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;\nGO\n"
            + "ALTER TABLE [dbo].[Account] ALTER COLUMN [Id] BIGINT NOT NULL;\nGO\nALTER TABLE [dbo].[Status] ALTER COLUMN [Code] NVARCHAR (20) NULL;\nGO\nDROP TABLE [dbo].[Status];\nGO\n");
        var changes = NotPredictable.Contract(script, NotPredictable.Shape(script), environment).Select(c => c.Table + ": " + c.What).ToArray();
        Assert.Equal(["[dbo].[Customer]: the column [Region] is dropped", "[dbo].[Customer]: the column [Email] becomes mandatory", "[dbo].[Account]: the column [Id] changes type",
            "[dbo].[Status]: the column [Code] becomes optional", "[dbo].[Status]: the table is dropped"], changes);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void An_update_or_delete_through_an_alias_in_a_deploy_script_writes_the_table_its_from_clause_names()
    {
        foreach (var statement in new[]
        {
            "UPDATE c SET c.[Email] = N'' FROM [dbo].[Customer] AS c WHERE c.[Email] IS NULL;",
            "DELETE c FROM [dbo].[Customer] AS c WHERE c.[Email] IS NULL;",
            "UPDATE c SET c.[Region] = a.[Id] FROM [dbo].[Account] AS a INNER JOIN [dbo].[Customer] AS c ON c.[AccountId] = a.[Id];",
        })
        {
            var script = Scripts.Read("ALTER TABLE [dbo].[Customer] ADD [X] INT NULL;\nGO\n" + statement + "\nGO\n", post: statement);
            Assert.Equal([("authored", "[dbo].[Customer]")], NotPredictable.Of(script, NotPredictable.Shape(script), environment, []).Select(u => (u.Why, u.Table)).ToArray());
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_constraint_on_an_existing_table_that_reads_a_column_the_plan_adds_is_left_to_prove_and_one_on_a_new_table_is_clean()
    {
        var script = Scripts.Read("ALTER TABLE [dbo].[Customer] ADD [Score] INT CONSTRAINT [DF_Customer_Score] DEFAULT (0) NOT NULL, CONSTRAINT [CK_Customer_Score] CHECK ([Score] > [Id]);\nGO\n"
            + "ALTER TABLE [dbo].[Customer] ADD [Tier] INT NULL CONSTRAINT [UQ_Customer_Tier] UNIQUE;\nGO\n"
            + "ALTER TABLE [dbo].[Customer] ADD [ParentId] INT NULL;\nGO\nALTER TABLE [dbo].[Customer] WITH NOCHECK ADD CONSTRAINT [FK_Customer_Parent] FOREIGN KEY ([ParentId]) REFERENCES [dbo].[Account] ([Id]);\nGO\n"
            + "CREATE TABLE [dbo].[Priority] ([Id] INT NOT NULL CONSTRAINT [PK_Priority] PRIMARY KEY, [Rank] INT NOT NULL CONSTRAINT [CK_Priority_Rank] CHECK ([Rank] > 0));\nGO\n");
        var found = Preconditions.Of(script, [], environment);
        Assert.Equal(["Check on [dbo].[Customer] ([Id], [Score])", "Unique on [dbo].[Customer] ([Tier])"], found.Where(p => p.ReadsNew).Select(p => p.Spelled).ToArray());
        Assert.All(found.Where(p => p.Table == "Priority"), p => Assert.True(p.OnNewObject && !p.ReadsNew));
        Assert.Contains(found, p => p.Spelled == "ForeignKey on [dbo].[Customer] ([ParentId])" && p.OnNewObject && !p.ReadsNew);
        var measured = Predict.Measure(null!, found, false);
        Assert.Equal((2, "unknown"), (measured.Level, measured.Outcome));
        Assert.Equal(2, measured.Unanswered.Count);
        Assert.All(measured.Preconditions.Where(p => p.Table == "[dbo].[Customer]" && p.Kind != "ForeignKey"), p => Assert.Null(p.Violated));
        Assert.Contains(measured.Preconditions, p => p.Kind == "ForeignKey" && p.Violated == false);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_row_whose_check_went_unanswered_is_unknown_at_level_2_and_an_answered_empty_table_applies_at_level_3()
    {
        var empty = Precondition.Of("TableEmpty", "dbo", "Customer", [], null, false, 1);
        var unanswered = new Measurement([new CheckAnswer("[dbo].[Customer]", null, "exists", empty.Query)], [], [(empty, new Measured.TimedOut(30))]);
        Assert.Equal((2, "unknown", (string?)null), (unanswered.Level, unanswered.Outcome, unanswered.BlockedBy));
        var answered = new Measurement([new CheckAnswer("[dbo].[Customer]", false, "exists", empty.Query)], [], []);
        Assert.Equal((3, "applies"), (answered.Level, answered.Outcome));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_proof_does_not_stand_on_an_item_the_environment_did_not_answer_and_skips_one_that_reads_a_new_column()
    {
        var empty = Precondition.Of("TableEmpty", "dbo", "Customer", [], null, false, 1);
        var reads = Precondition.Of("Check", "dbo", "Customer", ["Score"], "SELECT 1 AS present FROM [dbo].[Customer] WHERE NOT ([Score] > 0)", true, 2) with { ReadsNew = true };
        var environmentAnswer = new Measurement([new CheckAnswer("[dbo].[Customer]", null, "exists", empty.Query)], [new PreconditionAnswer("Check", "[dbo].[Customer]", ["Score"], null, null, reads.Query)], [(empty, new Measured.Refused(229))]);
        var copyAnswer = new Measurement([new CheckAnswer("[dbo].[Customer]", true, "exists", empty.Query)], [new PreconditionAnswer("Check", "[dbo].[Customer]", ["Score"], null, null, reads.Query)], []);
        Assert.Equal(["[dbo].[Customer] was not read on dev"], Prove.Differences(environmentAnswer, copyAnswer, [empty, reads], "dev"));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_check_constraints_expression_is_read_from_the_model_and_one_written_with_IN_is_its_known_cause()
    {
        var model = Model.Load(Dacpacs.Of(scratch.Under("checks.dacpac"), "CREATE TABLE [dbo].[Order] ([Id] INT NOT NULL PRIMARY KEY, [StatusText] NVARCHAR(20) NULL, [Total] INT NULL,"
            + " CONSTRAINT [CK_Order_StatusText] CHECK ([StatusText] IN (N'Pending', N'Paid')), CONSTRAINT [CK_Order_Total] CHECK ([Total] >= 0));"));
        Assert.Contains(model.Checks, c => c.Name == "[dbo].[CK_Order_StatusText]" && c.Expression.Contains("IN", StringComparison.Ordinal));
        var operations = new List<PlanOperation> { new("Create", "SqlCheckConstraint", "[dbo].[CK_Order_StatusText]"), new("Create", "SqlCheckConstraint", "[dbo].[CK_Order_Total]") };
        Assert.Equal("check-written-with-in", Causes.Of(operations[0], operations, model, ""));
        Assert.Null(Causes.Of(operations[1], operations, model, ""));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_smart_default_DacFx_writes_as_a_table_constraint_for_a_new_mandatory_column_is_named_for_the_lead()
    {
        var change = Model.Load(Dacpacs.Of(scratch.Under("change.dacpac"), EnvironmentModel.Replace("[Region] NVARCHAR(40) NULL);", "[Region] NVARCHAR(40) NULL, [MiddleName] NVARCHAR(100) NOT NULL);", StringComparison.Ordinal)));
        var script = Scripts.Read("ALTER TABLE [dbo].[Customer]\n    ADD [MiddleName] NVARCHAR (100) NOT NULL,\n        CONSTRAINT [SD_Customer_dabecc4e03f748ca961c200fbf04150b] DEFAULT N'' FOR [MiddleName];\nGO\n");
        Assert.Equal([("[dbo].[Customer]", "MiddleName")], NotPredictable.SmartDefaults(script, change).Select(d => (d.Table, d.Column)).ToArray());
        var declared = Model.Load(Dacpacs.Of(scratch.Under("declared.dacpac"), EnvironmentModel.Replace("[Region] NVARCHAR(40) NULL);", "[Region] NVARCHAR(40) NULL, [MiddleName] NVARCHAR(100) NOT NULL CONSTRAINT [DF_Customer_MiddleName] DEFAULT (N''));", StringComparison.Ordinal)));
        Assert.Empty(NotPredictable.SmartDefaults(Scripts.Read("ALTER TABLE [dbo].[Customer]\n    ADD [MiddleName] NVARCHAR (100) CONSTRAINT [DF_Customer_MiddleName] DEFAULT (N'') NOT NULL;\nGO\n"), declared));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void An_intent_explains_the_plan_operations_it_can_produce_and_no_other()
    {
        var plan = new List<PlanOperation> { new("Drop", "SqlSimpleColumn", "[dbo].[Customer].[ContactPhone]"), new("Create", "SqlSimpleColumn", "[dbo].[Customer].[MobileNumber]") };
        Assert.Equal(2, NotPredictable.Unexplained(plan, "rename-attribute", Repo.Src).Count);
        Assert.Empty(NotPredictable.Unexplained([new PlanOperation("Rename", "SqlSimpleColumn", "[dbo].[Customer].[ContactPhone]")], "rename-attribute", Repo.Src));
        var stop = Assert.Throws<Stop>(() => NotPredictable.Unexplained(plan, "rename-column", Repo.Src));
        Assert.Equal("intent.unknown", stop.Finding.Code);
        Assert.Equal(45, System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo.Src, "operations.json"))).RootElement.EnumerateObject().Count());
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_check_written_with_IN_and_a_case_only_rename_are_known_causes()
    {
        Assert.True(Causes.WrittenWithIn("([Region] IN (N'West', N'East'))"));
        Assert.False(Causes.WrittenWithIn("([Total] >= (0))"));
        var operations = new List<PlanOperation> { new("Drop", "SqlTable", "[dbo].[customer]"), new("Create", "SqlTable", "[dbo].[Customer]") };
        Assert.Equal("case-only-rename", Causes.Of(operations[0], operations, environment, ""));
        Assert.Null(Causes.Of(new PlanOperation("Create", "SqlIndex", "[dbo].[Customer].[IX]"), operations, environment, ""));
        Assert.Equal(["check-written-with-in", "clustered-key-rebuild", "query-store-option", "case-only-rename"], Causes.Known(Repo.Src).Select(c => c.Name).ToArray());
    }

    public void Dispose() => scratch.Dispose();
}
