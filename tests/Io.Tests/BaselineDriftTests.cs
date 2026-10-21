using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DbChange.Kernel;
using DbChange.Tests;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// Baseline drift and the coordinate rules on plans DacFx 170.5.96 makes package to package, which connect to nothing, under the
/// pipeline profile. A change of a column's type or nullability under an object that reads it plans DacFx's own Drop and Create of that
/// object around the ALTER COLUMN, and a change of a clustered key's type a TableRebuild with its own Drops and Creates; none of them is
/// drift, and an object the environment lacks is. An unnamed constraint's report item is keyed as io/Ssdt.ReadModel keys the
/// constraint. A column the change adds that allows NULL holds NULL in every existing row whatever its default.
/// </summary>
public sealed class BaselineDriftTests : IDisposable
{
    private const string TableT = "CREATE TABLE dbo.T (Id INT NOT NULL CONSTRAINT PK_T PRIMARY KEY, ";

    private static readonly string[] Parent =
    [
        "CREATE TABLE dbo.P (Code VARCHAR(20) NOT NULL CONSTRAINT PK_P PRIMARY KEY, Name NVARCHAR(50) NULL,"
            + " M INT NULL DEFAULT 1 CHECK (M > 0));",
        "CREATE INDEX IX_P_Name ON dbo.P (Name);",
        "CREATE TABLE dbo.C (Id INT NOT NULL CONSTRAINT PK_C PRIMARY KEY,"
            + " PCode VARCHAR(20) NULL CONSTRAINT FK_C_P REFERENCES dbo.P (Code));",
        "CREATE TABLE dbo.D (Id INT NOT NULL CONSTRAINT PK_D PRIMARY KEY, PCode VARCHAR(20) NULL REFERENCES dbo.P (Code));",
    ];

    /// <summary>Each case's model before the change and after it, as T-SQL.</summary>
    private static readonly Dictionary<string, (string[] Before, string[] After)> Cases = new(StringComparer.Ordinal)
    {
        ["a unique index and a statistic"] = (
            [TableT + "Code NVARCHAR(50) NOT NULL);", "CREATE UNIQUE INDEX UX_T_Code ON dbo.T (Code);",
                "CREATE STATISTICS ST_T_Code ON dbo.T (Code);"],
            [TableT + "Code NVARCHAR(40) NOT NULL);", "CREATE UNIQUE INDEX UX_T_Code ON dbo.T (Code);",
                "CREATE STATISTICS ST_T_Code ON dbo.T (Code);"]),
        ["a unique index over a column made NOT NULL"] = (
            [TableT + "Code NVARCHAR(50) NULL);", "CREATE UNIQUE INDEX UX_T_Code ON dbo.T (Code);"],
            [TableT + "Code NVARCHAR(50) NOT NULL);", "CREATE UNIQUE INDEX UX_T_Code ON dbo.T (Code);"]),
        ["a named and an unnamed default"] = (
            [TableT + "Note VARCHAR(20) NULL CONSTRAINT DF_T_Note DEFAULT ('x'), Memo VARCHAR(20) NULL DEFAULT ('y'));"],
            [TableT + "Note VARCHAR(10) NULL CONSTRAINT DF_T_Note DEFAULT ('x'), Memo VARCHAR(10) NULL DEFAULT ('y'));"]),
        ["a named and an unnamed foreign key, the parent rebuilt"] =
            (Parent, [.. Parent.Select(s => s.Replace("VARCHAR(20)", "VARCHAR(30)", StringComparison.Ordinal))]),
        ["a named check and two unnamed ones"] = (
            [TableT + "Amt INT NULL CONSTRAINT CK_T_Amt CHECK (Amt > 0) CHECK (Amt < 100) CHECK (Amt <> 7));"],
            [TableT + "Amt BIGINT NULL CONSTRAINT CK_T_Amt CHECK (Amt > 0) CHECK (Amt < 100) CHECK (Amt <> 7));"]),
        ["an index over a computed column"] = (
            [TableT + "A INT NULL, Twice AS (A * 2));", "CREATE INDEX IX_T_Twice ON dbo.T (Twice);"],
            [TableT + "A BIGINT NULL, Twice AS (A * 2));", "CREATE INDEX IX_T_Twice ON dbo.T (Twice);"]),
    };

    private readonly string scratch = Directory.CreateTempSubdirectory("dbchange-drift-").FullName;

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    /// <summary>
    /// Each plan holds a Create, after a Drop or after a TableRebuild, of an object the change leaves as it was, which DacFx drops and
    /// creates again around the ALTER COLUMN or the rebuild; and the plan's baseline drift is empty.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("a unique index and a statistic")]
    [InlineData("a unique index over a column made NOT NULL")]
    [InlineData("a named and an unnamed default")]
    [InlineData("a named and an unnamed foreign key, the parent rebuilt")]
    [InlineData("a named check and two unnamed ones")]
    [InlineData("an index over a computed column")]
    public void A_change_under_an_object_that_reads_its_column_plans_DacFx_s_drop_and_create_of_it_and_no_baseline_drift(string under)
    {
        var (before, after) = Cases[under];

        var planned = Planned(before, after, before);

        Assert.Contains(planned.Plan.Report.Operations, o => o.Kind is PlanOperationKind.Create
            && planned.After.Any(e => e.Key == o.Key) && planned.Change.Created.All(c => c.Key != o.Key));
        Assert.Empty(Preconditions.BaselineDrift(planned.Plan.Report, planned.After, planned.Change, planned.Collation));
    }

    /// <summary>
    /// A clustered primary key redefined over other columns, as the sample change redefine-the-primary-key does: DacFx rebuilds the
    /// table, and drops and creates its default around the rebuild. The rebuild is the change's, though the change alters no element of
    /// the table but its keys, and the plan holds no baseline drift.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_primary_key_redefined_over_other_columns_rebuilds_its_table_as_the_change_s_own_and_no_baseline_drift()
    {
        const string Line = "CREATE TABLE dbo.L (Id INT NOT NULL, OrderId INT NOT NULL, LineNumber INT NOT NULL,"
            + " Amount DECIMAL(18, 2) NOT NULL CONSTRAINT DF_L_Amount DEFAULT (0), ";

        var planned = Planned([Line + "CONSTRAINT PK_L_Id PRIMARY KEY CLUSTERED (Id));"],
            [Line + "CONSTRAINT PK_L PRIMARY KEY CLUSTERED (OrderId, LineNumber));"]);

        Assert.Contains(planned.Plan.Report.Operations, o => o.Kind is PlanOperationKind.TableRebuild && o.Key.ToString() == "Table [dbo].[L]");
        Assert.Empty(Preconditions.BaselineDrift(planned.Plan.Report, planned.After, planned.Change, planned.Collation));
    }

    /// <summary>
    /// The same change of type planned against an environment that lacks the unique index over the column and an index on a table the
    /// change leaves alone: DacFx creates each and drops neither, and each Create is drift by its object.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_object_the_environment_lacks_is_drift_whether_or_not_it_reads_a_column_the_change_alters()
    {
        string[] other = ["CREATE TABLE dbo.O (Id INT NOT NULL CONSTRAINT PK_O PRIMARY KEY, A INT NULL);",
            "CREATE INDEX IX_O_A ON dbo.O (A);"];
        var (before, after) = Cases["a unique index and a statistic"];

        var planned = Planned([.. before, .. other], [.. after, .. other], [before[0], before[2], other[0]]);

        Assert.Equal(["Create Index [dbo].[O].[IX_O_A]", "Create Index [dbo].[T].[UX_T_Code]"],
            Preconditions.BaselineDrift(planned.Plan.Report, planned.After, planned.Change, planned.Collation)
                .Select(o => o.Kind + " " + o.Key));
    }

    /// <summary>
    /// DacFx names an unnamed constraint by its type and table alone, as "Check Constraint: unnamed constraint on [dbo].[T]": a column
    /// added with an inline unnamed default and check plans a Create of the check keyed under its column, as io/Ssdt.ReadModel keys it,
    /// and no drift. Two unnamed checks on one table, one created again and one new, beside one left as it was, leave DacFx's name on
    /// each item, with the note plan.unnamed-unmatched for the Creates and for the Drop, since the models do not say which is which.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_unnamed_constraint_s_report_item_is_keyed_as_io_keys_the_constraint_or_keeps_DacFx_s_name_with_a_note()
    {
        const string DacFxName = "CheckConstraint [Check Constraint: unnamed constraint on [dbo]].[T]]]";
        var added = Planned([TableT + "Note INT NULL);"], [TableT + "Note INT NULL, Rank INT NOT NULL DEFAULT (0) CHECK ([Rank] > 0));"]);
        var unmatched = Planned([TableT + "A INT NULL CHECK (A > 0), B INT NULL CHECK (B > 0));"],
            [TableT + "A INT NULL CHECK (A > 0), B BIGINT NULL CHECK (B > 0), C INT NULL CHECK (C > 0));"]);

        Assert.Contains(added.Plan.Report.Operations, o => o.Kind is PlanOperationKind.Create
            && o.Key.ToString() == "CheckConstraint [dbo].[T].[Rank].[ExpressionDependencies]");
        Assert.Empty(Preconditions.BaselineDrift(added.Plan.Report, added.After, added.Change, added.Collation));
        Assert.Empty(added.Plan.Notes);
        Assert.Equal(["Create", "Create", "Drop"],
            unmatched.Plan.Report.Operations.Where(o => o.Key.ToString() == DacFxName).Select(o => o.Kind.Name));
        Assert.Equal(3, unmatched.Plan.Report.Operations.Count(o => o.Key.Type == "CheckConstraint"));
        Assert.Equal([("plan.unnamed-unmatched", "[dbo].[T]"), ("plan.unnamed-unmatched", "[dbo].[T]")],
            unmatched.Plan.Notes.Select(n => (n.Code, n.Subject)));
    }

    /// <summary>
    /// The coordinate rule for a column the change adds, against DacFx's script: it adds a column that allows NULL with no WITH VALUES,
    /// so each existing row holds NULL whatever the column's default, and a NOT NULL column with its default; a unique index over each
    /// is counted with that value in place of the column.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_column_added_that_allows_NULL_holds_NULL_in_every_existing_row_whatever_its_default()
    {
        var planned = Planned([TableT[..^2] + ");"],
            [TableT + "Note INT NULL CONSTRAINT DF_T_Note DEFAULT (7), Rank INT NOT NULL CONSTRAINT DF_T_Rank DEFAULT (0));",
                "CREATE UNIQUE INDEX UX_T_Note ON dbo.T (Note);", "CREATE UNIQUE INDEX UX_T_Rank ON dbo.T (Rank);"]);
        var preconditions = Ok(Preconditions.Of(planned.Before, planned.After, planned.Change, []));

        Assert.Matches(@"ADD \[Note\] INT\s+CONSTRAINT \[DF_T_Note\] DEFAULT \(7\) NULL,", planned.Plan.Script);
        Assert.DoesNotContain("WITH VALUES", planned.Plan.Script, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([("[Note]", null), ("[Rank]", "(0)")], preconditions.Select(p => Ok(Coordinates.Of(p, planned.Change, planned.Before)))
            .Select(c => Assert.IsType<Coordinates.Counted>(c).Places.Single(place => place.After.Type == "Column"))
            .Select(place => (place.After.Name.ToString(), place.Default)));
    }

    /// <summary>The change from <paramref name="before"/> to <paramref name="after"/>, and the plan of the package after it against
    /// <paramref name="environment"/>, the package before it when none is given.</summary>
    private PlannedChange Planned(string[] before, string[] after, string[]? environment = null)
    {
        using var was = Ok(Ssdt.Open(Packaged("before", before)));
        using var now = Ok(Ssdt.Open(Packaged("after", after)));
        using var target = Ok(Ssdt.Open(Packaged("environment", environment ?? before)));
        var plan = Ok(DacFx.Plan(now, target, "Target", Ok(PublishProfiles.Load(GoldenProject.Profile)), []));
        var (from, to) = (Ok(was.Elements).Elements, Ok(now.Elements).Elements);
        var collation = Ok(Ssdt.CollationOf(to));
        return new(from, to, Ok(Change.Between(from, to, [], collation)), plan, collation);
    }

    private string Packaged(string name, string[] scripts)
    {
        var dacpac = Path.Combine(scratch, name + ".dacpac");
        using var model = new TSqlModel(SqlServerVersion.Sql160, new TSqlModelOptions());
        foreach (var script in scripts)
        {
            model.AddObjects(script);
        }

        DacPackageExtensions.BuildPackage(dacpac, model, new PackageMetadata());
        return dacpac;
    }

    private static T Ok<T>(Result<T> result) =>
        result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));
}

/// <summary>Two models, the change between them, the plan of the package after it against an environment, and the after model's
/// collation.</summary>
internal sealed record PlannedChange(SortedArray<Element> Before, SortedArray<Element> After, Change Change, Plan Plan,
    Collation Collation);
