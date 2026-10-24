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
        Assert.Empty(NotPredictable.Contract(script, shape));
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
            + "DECLARE @cmd NVARCHAR(MAX);\nSET @cmd = N'ALTER TABLE ' + N'[dbo].[Customer]' + N' WITH CHECK CHECK CONSTRAINT [CK_x]';\nEXECUTE sp_executesql @cmd;\nGO\n"
            + "PRINT N'done';\nGO\nALTER DATABASE [$(DatabaseName)] SET QUERY_STORE = OFF;\nGO\n");
        Assert.Empty(NotPredictable.Of(admitted, NotPredictable.Shape(admitted), environment, []));
        Assert.Equal([("[dbo].[Customer]", "[dbo].[Customer].[Region] is renamed")], NotPredictable.Contract(admitted, NotPredictable.Shape(admitted)).Select(c => (c.Table, c.What)).ToArray());
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Contract_changes_name_the_drop_the_type_change_and_the_tightening_a_consumer_sees()
    {
        var script = Scripts.Read("ALTER TABLE [dbo].[Customer] DROP COLUMN [Region];\nGO\nALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;\nGO\n"
            + "ALTER TABLE [dbo].[Account] ALTER COLUMN [Id] BIGINT NOT NULL;\nGO\nDROP TABLE [dbo].[Status];\nGO\n");
        var changes = NotPredictable.Contract(script, NotPredictable.Shape(script)).Select(c => c.Table + ": " + c.What).ToArray();
        Assert.Equal(["[dbo].[Customer]: the column [Region] is dropped", "[dbo].[Customer]: the column [Email] becomes mandatory", "[dbo].[Account]: the column [Id] becomes mandatory", "[dbo].[Status]: the table is dropped"], changes);
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
