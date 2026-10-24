using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DbChangeLite;
using Xunit;

namespace DbChangeLite.Tests;

public sealed class ScriptTests
{
    private const string Raise = "    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT";

    private const string Lookalikes = ":setvar DatabaseName \"Estate\"\n:setvar ProductSchema \"dbo\"\nGO\n:on error exit\nGO\nUSE [$(DatabaseName)];\nGO\n"
        + "/*\n  The pre-deploy script quotes the check DacFx writes:\n  IF EXISTS (select top 1 1 from [dbo].[Quoted])\n" + Raise + "\n*/\nGO\n"
        + "IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Category])\n    BEGIN\n        INSERT INTO [dbo].[tmp_ms_xx_Category] ([Id], [Code])\n        SELECT   [Id], [Code]\n        FROM     [dbo].[Category];\n    END\nGO\n"
        + "ALTER TABLE [dbo].[Order] WITH NOCHECK ADD CHECK ([Total] >= 0);\nGO\n"
        + "IF @@ROWCOUNT > 0\n    BEGIN\n        DROP TABLE [#__checkStatus];\n        RAISERROR (N'An error occurred while verifying constraints', 16, 127) WITH NOWAIT;\n    END\nGO\n"
        + "IF EXISTS (select top 1 1 from [$(ProductSchema)].[Product])\n" + Raise + "\nGO\n"
        + "IF EXISTS (select top 1 1 from [dbo].[Customer])\n" + Raise + "\nGO\n";

    [Fact]
    [Trait("Category", "fast")]
    public void Every_check_of_the_golden_data_loss_scripts_is_found_with_its_table()
    {
        var folder = Path.Combine(Repo.Golden, "data-loss-checks");
        Assert.True(Directory.Exists(folder), folder);
        foreach (var file in Directory.EnumerateFiles(folder, "*.sql").Order(StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file);
            var expected = Regex.Matches(text, @"IF EXISTS \(select top 1 1 from (\[[^\]]+\]\.\[[^\]]+\])\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
            var found = BlockOnPossibleDataLoss.Checks(Scripts.Read(text)).Select(c => c.Table).Order(StringComparer.Ordinal).ToList();
            Assert.True(expected.SequenceEqual(found, StringComparer.OrdinalIgnoreCase), Path.GetFileName(file) + ": expected " + string.Join(", ", expected) + " found " + string.Join(", ", found));
            Assert.NotEmpty(found);
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_three_lookalikes_of_spike_S2_are_skipped_and_the_substituted_check_is_found()
    {
        var checks = BlockOnPossibleDataLoss.Checks(Scripts.Read(Lookalikes));
        Assert.Equal(["[dbo].[Product]", "[dbo].[Customer]"], checks.Select(c => c.Table).ToArray());
        Assert.Equal("SELECT CASE WHEN EXISTS (SELECT TOP 1 1 FROM [dbo].[Product]) THEN 1 ELSE 0 END", Precondition.Of("TableEmpty", checks[0].Schema, checks[0].Name, [], null, false, checks[0].Line).Query);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Statements_carry_their_part_line_and_kind_and_the_sqlcmd_lines_leave_the_lines_in_place()
    {
        var post = "MERGE dbo.Priority AS target USING (VALUES (1, N'Low')) AS source (Id, Code) ON target.Id = source.Id WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Code) VALUES (source.Id, source.Code);";
        var script = Scripts.Read(":setvar DatabaseName \"Estate\"\nGO\nPRINT N'Creating Table [dbo].[Priority]...';\nGO\nCREATE TABLE [dbo].[Priority] ([Id] INT NOT NULL, [Code] NVARCHAR(20) NOT NULL);\nGO\n" + post + "\nGO\n", post: post);
        Assert.Equal(["PrintStatement", "CreateTableStatement", "MergeStatement"], script.Statements.Select(s => s.Kind).ToArray());
        Assert.Equal(["own", "own", "post"], script.Statements.Select(s => s.Part).ToArray());
        Assert.Equal([3, 5, 7], script.Statements.Select(s => s.Line).ToArray());
        Assert.Empty(script.Unresolved);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_variable_no_setvar_profile_or_environment_values_is_reported_once()
    {
        var script = Scripts.Read("PRINT N'$(Unknown) $(Unknown)';\nGO\n");
        Assert.Equal(["Unknown"], script.Unresolved.ToArray());
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Nested_statements_are_read_inside_if_begin_end_try_and_while()
    {
        var script = Scripts.Read("IF 1 = 1 BEGIN BEGIN TRY BEGIN UPDATE dbo.T SET a = 1; END END TRY BEGIN CATCH PRINT 'x'; END CATCH END\nGO\nWHILE 1 = 0 DELETE dbo.T;\nGO\n");
        Assert.Equal(["IfStatement", "BeginEndBlockStatement", "TryCatchStatement", "BeginEndBlockStatement", "UpdateStatement", "PrintStatement", "WhileStatement", "DeleteStatement"],
            script.Statements.Select(s => s.Kind).ToArray());
    }
}
