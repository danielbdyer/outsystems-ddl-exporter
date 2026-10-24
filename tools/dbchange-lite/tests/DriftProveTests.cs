using System;
using System.IO;
using System.Linq;
using DbChangeLite;
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
        }
        finally
        {
            copy.Run("ALTER TABLE [dbo].[Customer] ALTER COLUMN [Region] NVARCHAR (50) NULL");
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
    public void Prove_applies_on_a_populated_copy_for_add_a_nullable_column_and_refuses_rows_that_answer_otherwise_than_the_environment()
    {
        var project = GoldenProject.Copy(copy.Scratch, "prove-nullable").Edit("add-a-nullable-column");
        var rows = Path.Combine(Repo.Golden, "copies", "populated.sql");
        copy.Emptied();
        copy.Run(File.ReadAllText(rows));
        var (exit, answer) = Cli.Run(copy.Root, "prove", "--env", "corpus", "--to", "project:" + project.Folder, "--onto", "none", "--rows", rows);
        Assert.True(exit == 0, answer.GetProperty("message").GetString());
        var mandatory = GoldenProject.Copy(copy.Scratch, "prove-mismatch").Edit("make-mandatory");
        var none = copy.Scratch.Write("rows-none.sql", "PRINT N'no rows';\n");
        var (refused, mismatch) = Cli.Run(copy.Root, "prove", "--env", "corpus", "--to", "project:" + mandatory.Folder, "--onto", "none", "--rows", none);
        Assert.Equal(8, refused);
        Assert.Equal("copy.rows-differ", mismatch.GetProperty("findings")[0].GetProperty("code").GetString());
    }
}
