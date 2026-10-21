using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// synthetic-copy up where a planted row links to a lookup: a key whose leg is unique, so each child row takes a parent row of its own,
/// and a lookup the declared witnesses plant whole, so the planter plants none of its rows and links the child to the declared row.
/// </summary>
public sealed partial class SyntheticCopyTests
{
    /// <summary>
    /// WP 3.5's done-when, its last clause (V3_MILESTONES.md §9): two child rows whose leg is a unique key link to two distinct rows of a
    /// lookup seeded with one, that row and one planted past it, so neither INSERT fails on the key (Msg 2601). At a ref adding dbo.Kind,
    /// which the post-deploy script seeds with the one row Id 1, and dbo.Holder, whose NOT NULL KindId references Kind under the unique
    /// index UIX_Holder_KindId, an environment holds a Holder row with a Label over three characters and one with a NULL Label, each
    /// with a Kind of its own; the change makes Label nvarchar(3) NOT NULL. The copy holds two Holder rows with two KindIds, the seeded
    /// row of Kind and one planted, and answers every precondition as the environment does.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task Two_child_rows_whose_leg_is_a_unique_key_link_to_the_lookup_s_seeded_row_and_one_planted_past_it_with_no_Msg_2601()
    {
        var at = repository.Committed(new SampleChange("a lookup seeded with one row and a table whose unique key references it",
            Lookup(unique: true, seeded: true), []), null);
        var head = repository.Committed(new SampleChange("Holder.Label made nvarchar(3) NOT NULL",
            [.. Lookup(unique: true, seeded: true), Narrowed(mandatory: true)], []), null);
        var profile = Profile("a unique key to a seeded lookup");
        try
        {
            var existing = await Environment(at, "INSERT INTO dbo.Kind (Id) VALUES (2); "
                + "INSERT INTO dbo.Holder (KindId, Label) VALUES (1, N'toolongvalue'), (2, NULL);", head, profile);

            var (exit, answer) = Up("--at", at, "--existing-data", Written(existing), "--profile", profile);

            Assert.True(exit == 0, answer.ToJsonString());
            Assert.Equal(JsonNode.Parse(ExistingDataFile.Text(existing))!["states"]!.ToJsonString(),
                answer["syntheticCopy"]!["answers"]!.ToJsonString());
            var copy = await Connection((string)answer["syntheticCopy"]!["copy"]!);
            Assert.Equal((2, 2), (await SqlServerFixture.ScalarAsync(copy, "SELECT COUNT(*) FROM dbo.Holder;"),
                await SqlServerFixture.ScalarAsync(copy, "SELECT COUNT(DISTINCT KindId) FROM dbo.Holder;")));
            Assert.Equal((1, 2), (await SqlServerFixture.ScalarAsync(copy, "SELECT COUNT(*) FROM dbo.Kind WHERE Id = 1;"),
                await SqlServerFixture.ScalarAsync(copy, "SELECT COUNT(*) FROM dbo.Kind;")));
            Assert.Equal(1, Planted(answer, "[dbo].[Kind]"));
            Assert.Equal(0, await SqlServerFixture.ScalarAsync(copy,
                "SELECT COUNT(*) FROM dbo.Holder AS h WHERE NOT EXISTS (SELECT 1 FROM dbo.Kind AS k WHERE k.Id = h.KindId);"));
        }
        finally
        {
            await Dropped();
        }
    }

    /// <summary>
    /// PAR-033, PLT-83 and PLT-84: a table the declared witnesses plant whole under -- plants: takes no row from the planter, its block
    /// runs before the first table that references it, and a child the planter links finds the declared row. At a ref adding dbo.Kind,
    /// which no seed fills, and dbo.Holder, whose NOT NULL KindId references Kind, an environment holds two of each, one Holder.Label
    /// over the three characters the change narrows it to; dbchange/witnesses.sql plants Kind as the one row Id 7. The planter plants
    /// Holder's rows and none of Kind's; each Holder row's KindId is 7; Kind holds the declared row alone; and the copy answers every
    /// precondition as the environment does.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_table_the_declared_witnesses_plant_whole_takes_no_planted_row_and_its_child_links_to_the_declared_row()
    {
        var at = repository.Committed(new SampleChange("a lookup no seed fills and a table that references it",
            Lookup(unique: false, seeded: false), []), null);
        var head = repository.Committed(new SampleChange("Holder.Label narrowed to nvarchar(3)",
            [.. Lookup(unique: false, seeded: false), Narrowed(mandatory: false)], []),
            "-- plants: [dbo].[Kind]\nINSERT INTO dbo.Kind (Id) VALUES (7);\n");
        var profile = Profile("a lookup the declared witnesses plant");
        try
        {
            var existing = await Environment(at, "INSERT INTO dbo.Kind (Id) VALUES (1), (2); "
                + "INSERT INTO dbo.Holder (KindId, Label) VALUES (1, N'toolongvalue'), (2, N'ok');", head, profile);

            var (exit, answer) = Up("--at", at, "--existing-data", Written(existing), "--profile", profile);

            Assert.True(exit == 0, answer.ToJsonString());
            Assert.Equal(JsonNode.Parse(ExistingDataFile.Text(existing))!["states"]!.ToJsonString(),
                answer["syntheticCopy"]!["answers"]!.ToJsonString());
            Assert.Equal(0, Planted(answer, "[dbo].[Kind]"));
            Assert.True(Planted(answer, "[dbo].[Holder]") > 0, answer.ToJsonString());
            var copy = await Connection((string)answer["syntheticCopy"]!["copy"]!);
            Assert.Equal((1, 1), (await SqlServerFixture.ScalarAsync(copy, "SELECT COUNT(*) FROM dbo.Kind;"),
                await SqlServerFixture.ScalarAsync(copy, "SELECT COUNT(*) FROM dbo.Kind WHERE Id = 7;")));
            Assert.Equal(0, await SqlServerFixture.ScalarAsync(copy, "SELECT COUNT(*) FROM dbo.Holder WHERE KindId <> 7;"));
        }
        finally
        {
            await Dropped();
        }
    }

    /// <summary>
    /// A lookup and a table that references it, written into Modules/OrderStatusText.sql, which otherwise holds no schema object, one
    /// statement a batch, as SSDT builds a file (SQL71006): dbo.Kind, its key Id alone, and dbo.Holder, whose NOT NULL KindId references
    /// Kind and whose Label is nvarchar(20) NULL. Where <paramref name="unique"/>, KindId is also a key of Holder, UIX_Holder_KindId;
    /// where <paramref name="seeded"/>, the post-deploy script seeds Kind with the one row Id 1.
    /// </summary>
    private static GoldenEdit[] Lookup(bool unique, bool seeded)
    {
        GoldenEdit tables = new("Modules/OrderStatusText.sql", "-- Intentionally no schema object. The column lives in Modules/Order.sql.",
            "CREATE TABLE dbo.Kind (Id INT NOT NULL CONSTRAINT PK_Kind_Id PRIMARY KEY CLUSTERED);\nGO\n"
            + "CREATE TABLE dbo.Holder\n(\n    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Holder_Id PRIMARY KEY CLUSTERED,\n"
            + "    KindId INT NOT NULL CONSTRAINT FK_Holder_Kind_KindId REFERENCES dbo.Kind (Id),\n    Label NVARCHAR(20) NULL\n);\n"
            + (unique ? "GO\nCREATE UNIQUE INDEX UIX_Holder_KindId ON dbo.Holder (KindId);\n" : ""));
        GoldenEdit seed = new("Script.PostDeployment.sql", "PRINT 'Post-deploy complete: seed MERGEs applied (idempotent).';",
            "IF NOT EXISTS (SELECT 1 FROM dbo.Kind WHERE Id = 1) INSERT INTO dbo.Kind (Id) VALUES (1);\nGO\n\n"
            + "PRINT 'Post-deploy complete: seed MERGEs applied (idempotent).';");
        return seeded ? [tables, seed] : [tables];
    }

    /// <summary>Holder.Label narrowed from nvarchar(20) to nvarchar(3), and made NOT NULL where <paramref name="mandatory"/>.</summary>
    private static GoldenEdit Narrowed(bool mandatory) =>
        new("Modules/OrderStatusText.sql", "    Label NVARCHAR(20) NULL", "    Label NVARCHAR(3) " + (mandatory ? "NOT NULL" : "NULL"));
}
