using System;
using System.IO;
using System.Linq;
using DbChangeLite;
using Microsoft.Data.SqlClient;
using Xunit;

namespace DbChangeLite.Tests;

public sealed class DriftProveTests(CorpusCopy copy) : IClassFixture<CorpusCopy>
{
    [Fact]
    [Trait("Category", "fixture")]
    public void Drift_answers_in_sync_against_the_copy_the_base_was_published_to_and_notes_a_table_added_by_hand()
    {
        var (exit, answer) = Cli.Run(copy.Root, "drift", "--env", "corpus", "--at", "project:" + copy.Base.Folder);
        Assert.True(exit == 0, answer.GetProperty("message").GetString());
        Assert.Equal("in-sync", answer.GetProperty("outcome").GetString());
        Assert.Equal(1, answer.GetProperty("environments")[0].GetProperty("level").GetInt32());
        copy.Run("CREATE TABLE [dbo].[ByHand] ([Id] INT NOT NULL CONSTRAINT [PK_ByHand] PRIMARY KEY)");
        try
        {
            var (again, noted) = Cli.Run(copy.Root, "drift", "--env", "corpus", "--at", "project:" + copy.Base.Folder);
            Assert.Equal(0, again);
            Assert.Contains(noted.GetProperty("findings").EnumerateArray(), f => f.GetProperty("code").GetString() == "drift.unmanaged" && f.GetProperty("subject").GetString() == "[dbo].[ByHand]");
        }
        finally
        {
            copy.Run("DROP TABLE [dbo].[ByHand]");
        }
    }

    [Fact]
    [Trait("Category", "fixture")]
    public void Drift_answers_differs_with_the_object_named_when_a_column_was_widened_by_hand()
    {
        copy.Run("ALTER TABLE [dbo].[Customer] ALTER COLUMN [Region] NVARCHAR (100) NULL");
        try
        {
            var (exit, answer) = Cli.Run(copy.Root, "drift", "--env", "corpus", "--at", "project:" + copy.Base.Folder);
            Assert.Equal(5, exit);
            Assert.Equal("differs", answer.GetProperty("outcome").GetString());
            Assert.Contains(answer.GetProperty("findings").EnumerateArray(), f => f.GetProperty("code").GetString() == "drift.differs" && f.GetProperty("subject").GetString() == "[dbo].[Customer]");
            Assert.Equal(0, answer.GetProperty("environments")[0].GetProperty("level").GetInt32());
        }
        finally
        {
            copy.Run("ALTER TABLE [dbo].[Customer] ALTER COLUMN [Region] NVARCHAR (50) NULL");
        }
    }

    [Fact]
    [Trait("Category", "fixture")]
    public void Drift_names_an_index_made_by_hand_as_the_difference_the_plan_drops_and_not_as_unmanaged()
    {
        copy.Run("CREATE INDEX [IX_ByHand] ON [dbo].[Customer] ([Region])");
        try
        {
            var (exit, answer) = Cli.Run(copy.Root, "drift", "--env", "corpus", "--at", "project:" + copy.Base.Folder);
            Assert.True(exit == 5, answer.GetProperty("message").GetString());
            var findings = answer.GetProperty("findings").EnumerateArray().Where(f => f.GetProperty("subject").GetString() == "[dbo].[Customer].[IX_ByHand]").Select(f => f.GetProperty("code").GetString()!).ToArray();
            Assert.Equal(["drift.differs"], findings);
        }
        finally
        {
            copy.Run("DROP INDEX [IX_ByHand] ON [dbo].[Customer]");
        }
    }

    [Fact]
    [Trait("Category", "fixture")]
    public void Predict_answers_the_environment_as_not_read_while_a_schema_change_holds_a_lock_on_its_catalog()
    {
        var project = GoldenProject.Copy(copy.Scratch, "predict-locked").Edit("add-a-nullable-column");
        using var holder = new SqlConnection(copy.Connection);
        holder.Open();
        using var transaction = holder.BeginTransaction();
        try
        {
            using (var alter = new SqlCommand("ALTER TABLE [dbo].[Customer] ADD [HeldByTest] INT NULL", holder, transaction))
            {
                alter.ExecuteNonQuery();
            }

            var (exit, answer) = Cli.Run(copy.Root, "predict", "--env", "corpus", "--to", "project:" + project.Folder, "--onto", "none");
            Assert.True(exit == 8, answer.GetProperty("message").GetString());
            Assert.Equal("corpus not read.", answer.GetProperty("confidence").GetString());
            Assert.DoesNotContain(answer.GetProperty("findings").EnumerateArray(), f => f.GetProperty("code").GetString() == "internal.unexpected");
            Assert.StartsWith("dbchange-lite predict --env corpus --to project:", answer.GetProperty("open")[0].GetProperty("next").GetProperty("command").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            transaction.Rollback();
        }
    }

    [Fact]
    [Trait("Category", "fixture")]
    public void Prove_is_blocked_by_BlockOnPossibleDataLoss_on_a_populated_copy_for_make_mandatory()
    {
        var project = GoldenProject.Copy(copy.Scratch, "prove-make-mandatory").Edit("make-mandatory");
        var rows = Path.Combine(Repo.Golden, "copies", "populated.sql");
        copy.Emptied();
        copy.Run(File.ReadAllText(rows));
        var (exit, answer) = Cli.Run(copy.Root, "prove", "--env", "corpus", "--to", "project:" + project.Folder, "--onto", "none", "--rows", rows);
        Assert.True(exit == 3, answer.GetProperty("message").GetString());
        Assert.Equal("block-on-possible-data-loss", answer.GetProperty("blockedBy").GetString());
        Assert.Equal(4, answer.GetProperty("environments")[0].GetProperty("level").GetInt32());
        Assert.StartsWith("Proven on corpus", answer.GetProperty("confidence").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fixture")]
    public void Prove_is_blocked_by_a_constraint_violation_on_a_violating_copy_for_add_a_check_constraint_when_the_seed_cannot_correct_the_row()
    {
        var project = GoldenProject.Copy(copy.Scratch, "prove-add-check").Edit("add-a-check-constraint");
        var rows = copy.Scratch.Write("rows-violating.sql", File.ReadAllText(Path.Combine(Repo.Golden, "copies", "populated.sql"))
            + "\nGO\nSET IDENTITY_INSERT dbo.Product ON;\nINSERT INTO dbo.Product (Id, Code, LegacyCode) VALUES (999, N'', N'LGC-EMPTY');\nSET IDENTITY_INSERT dbo.Product OFF;\n");
        copy.Emptied();
        copy.Run(File.ReadAllText(rows));
        var (exit, answer) = Cli.Run(copy.Root, "prove", "--env", "corpus", "--to", "project:" + project.Folder, "--onto", "none", "--rows", rows);
        Assert.True(exit == 3, answer.GetProperty("message").GetString());
        Assert.Equal("constraint-violation", answer.GetProperty("blockedBy").GetString());
        var corrected = copy.Scratch.Write("rows-corrected.sql", File.ReadAllText(Path.Combine(Repo.Golden, "copies", "populated.sql")) + "\nGO\n" + File.ReadAllText(GoldenProject.Case("add-a-check-constraint", "violating.sql")) + "\n");
        copy.Emptied();
        copy.Run(File.ReadAllText(corrected));
        var (applied, warned) = Cli.Run(copy.Root, "prove", "--env", "corpus", "--to", "project:" + project.Folder, "--onto", "none", "--rows", corrected);
        Assert.True(applied == 0, warned.GetProperty("message").GetString());
        Assert.Contains(warned.GetProperty("findings").EnumerateArray(), f => f.GetProperty("code").GetString() == "prove.rows-changed" && f.GetProperty("subject").GetString() == "[dbo].[Product]");
    }

    [Fact]
    [Trait("Category", "fixture")]
    public void Prove_is_blocked_by_a_constraint_violation_when_a_unique_constraint_meets_duplicate_rows_at_Msg_1505()
    {
        var project = GoldenProject.Copy(copy.Scratch, "prove-add-unique").Edit("add-unique-constraint");
        var rows = copy.Scratch.Write("rows-duplicates.sql", File.ReadAllText(Path.Combine(Repo.Golden, "copies", "populated.sql")) + "\nGO\n" + File.ReadAllText(GoldenProject.Case("add-unique-constraint", "violating.sql")) + "\n");
        copy.Emptied();
        copy.Run(File.ReadAllText(rows));
        var (exit, answer) = Cli.Run(copy.Root, "prove", "--env", "corpus", "--to", "project:" + project.Folder, "--onto", "none", "--rows", rows);
        Assert.True(exit == 3, answer.GetProperty("message").GetString());
        Assert.Equal("constraint-violation", answer.GetProperty("blockedBy").GetString());
        Assert.Contains(answer.GetProperty("findings").EnumerateArray(), f => f.GetProperty("code").GetString() == "prove.dacfx" && f.GetProperty("message").GetString()!.Contains("1505", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public void Prove_on_a_never_read_environment_answers_a_rows_batch_the_releases_seed_already_wrote_as_needs_input()
    {
        var project = GoldenProject.Copy(copy.Scratch, "prove-never-read").Edit("add-a-nullable-column");
        File.WriteAllText(Path.Combine(copy.Root, "dbchange", "environments.json"), "{ \"environments\": { \"prod\": { \"host\": \"prod.invalid\", \"connection\": \"env:DBCHANGE_LITE_NONE\", \"profile\": \"profiles/pipeline.publish.xml\", \"neverRead\": true, \"deployOrder\": 4 } } }\n");
        try
        {
            var (exit, answer) = Cli.Run(copy.Root, "prove", "--env", "prod", "--to", "project:" + project.Folder, "--onto", "none", "--at", "project:" + copy.Base.Folder, "--rows", Path.Combine(Repo.Golden, "copies", "populated.sql"));
            Assert.True(exit == 8, answer.GetProperty("message").GetString());
            Assert.Equal("rows.failed", answer.GetProperty("findings")[0].GetProperty("code").GetString());
            var none = copy.Scratch.Write("rows-release-only.sql", "PRINT N'the release seed alone';\n");
            var (applied, proof) = Cli.Run(copy.Root, "prove", "--env", "prod", "--to", "project:" + project.Folder, "--onto", "none", "--at", "project:" + copy.Base.Folder, "--rows", none);
            Assert.True(applied == 0, proof.GetProperty("message").GetString());
            Assert.Equal("worst", proof.GetProperty("environments")[0].GetProperty("assumed").GetString());
            Assert.False(proof.GetProperty("environments")[0].TryGetProperty("level", out _));
            Assert.StartsWith("prod assumed; its worst case deployed, on dbchange_lite_", proof.GetProperty("confidence").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            copy.Environments("profiles/pipeline.publish.xml");
        }
    }

    [Fact]
    [Trait("Category", "fixture")]
    public void Prove_applies_on_a_populated_copy_for_add_a_nullable_column_and_refuses_rows_that_answer_otherwise_than_the_environment()
    {
        var project = GoldenProject.Copy(copy.Scratch, "prove-nullable").Edit("add-a-nullable-column");
        var rows = Path.Combine(Repo.Golden, "copies", "populated.sql");
        copy.Emptied();
        copy.Run(File.ReadAllText(rows));
        var ledger = Path.Combine(copy.Root, "dbchange", "ledgers", "toolchain.md");
        Directory.CreateDirectory(Path.GetDirectoryName(ledger)!);
        File.WriteAllText(ledger, "| Date | Profile | SHA-256 | Confirmed by |\n|---|---|---|---|\n| 2026-10-24 | profiles/pipeline.publish.xml | " + PublishProfiles.Load(copy.Root, "profiles/pipeline.publish.xml").Hash + " | the deployment step's owner |\n");
        var (exit, answer) = Cli.Run(copy.Root, "prove", "--env", "corpus", "--to", "project:" + project.Folder, "--onto", "none", "--rows", rows);
        File.Delete(ledger);
        Assert.True(exit == 0, answer.GetProperty("message").GetString());
        Assert.True(answer.GetProperty("provenance").GetProperty("profileVerified").GetBoolean());
        var mandatory = GoldenProject.Copy(copy.Scratch, "prove-mismatch").Edit("make-mandatory");
        var none = copy.Scratch.Write("rows-none.sql", "PRINT N'no rows';\n");
        var (refused, mismatch) = Cli.Run(copy.Root, "prove", "--env", "corpus", "--to", "project:" + mandatory.Folder, "--onto", "none", "--rows", none);
        Assert.Equal(8, refused);
        Assert.Equal("copy.rows-differ", mismatch.GetProperty("findings")[0].GetProperty("code").GetString());
    }
}
