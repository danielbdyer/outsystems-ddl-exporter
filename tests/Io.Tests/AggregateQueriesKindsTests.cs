using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DbChange.Kernel;
using DbChange.Kernel.Tests;
using DbChange.Tests;
using Microsoft.Data.SqlClient;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// io/AggregateQueries over the kinds only a plan's statements show (WP 2.1's second slice). KeyLength counts the rows whose key passes
/// the index's limit, 1,700 bytes or 900 for a clustered index, by the DATALENGTH of the key's one column of a varying width beside its
/// fixed columns' sizes, the value converted first where the change gives the column another type; SQL Server's CREATE INDEX fails with
/// Msg 1946 on exactly the rows it counts. A key of two columns of a varying width, and one whose fixed columns alone pass the limit,
/// read not predictable read-only. A Scan and a Rebuild count their table's rows and never a violating row. Computed and Authored are
/// declared by a person, which Coordinates.Of places for the person and no query counts, and Temporal and SeedDrift are counts no form of
/// the allowlist holds, so none of the four is counted.
/// </summary>
public sealed class AggregateQueriesKindsTests : IDisposable
{
    private readonly ScratchFolder folder = ScratchFolder.UnderRepository("aggregate-kinds-under-test");

    public void Dispose() => folder.Dispose();

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("N.Big nvarchar(1000) NULL", "N.Big nvarchar(1000) NULL", false, "SUM(CASE WHEN DATALENGTH([t].[Big]) > 1700 THEN 1 ELSE 0 END)")]
    [InlineData("N.Big varchar(1000) NOT NULL", "N.Big varchar(1000) NOT NULL", true, "SUM(CASE WHEN DATALENGTH([t].[Big]) > 896 THEN 1 ELSE 0 END)")]
    [InlineData("N.Big varchar(1000) NULL", "N.Big nvarchar(1000) NULL", false,
        "SUM(CASE WHEN DATALENGTH(TRY_CONVERT (NVARCHAR (1000), [t].[Big])) > 1700 THEN 1 ELSE 0 END)")]
    public void KeyLength_counts_the_rows_whose_one_column_of_a_varying_width_passes_what_the_fixed_columns_leave_of_the_limit(string before,
        string after, bool clustered, string item)
    {
        var columns = clustered ? new[] { "Id", "Big" } : ["Big"];
        var queries = Queries(Model.Of("N.Id int NOT NULL", before), Model.Of("N.Id int NOT NULL", after), Key(clustered, columns));

        Assert.Empty(queries.NotPredictable);
        Assert.Contains(item, Regex.Replace(Assert.Single(queries.Tables).Query.Statement, @"\s+", " "), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("N.A nvarchar(500) NULL", "N.B nvarchar(500) NULL", "its key holds 2 columns of a varying width")]
    [InlineData("N.A char(1000) NOT NULL", "N.B nchar(400) NULL", "its columns of a fixed size alone take 1800 bytes")]
    public void KeyLength_over_two_columns_of_a_varying_width_or_fixed_columns_past_the_limit_reads_not_predictable_read_only(string a,
        string b, string why)
    {
        var model = Model.Of("N.Id int NOT NULL", a, b);

        var queries = Queries(model, model, Key(false, "A", "B"));

        Assert.Contains(why, Assert.Single(queries.NotPredictable).Why, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_Scan_and_a_Rebuild_count_their_table_s_rows_and_the_declared_and_unheld_kinds_none()
    {
        var model = Model.Of("N.Id int NOT NULL", "N.Big nvarchar(1000) NULL", "N.Start datetime2(7) NOT NULL", "N.End datetime2(7) NOT NULL");
        Precondition On(PreconditionKind kind, params string[] columns) =>
            new(Model.TableName("N"), SortedArray.Of(columns.Select(c => Value(Name.Of(c)))), kind);
        var (scan, rebuild) = (On(new PreconditionKind.Scan()), On(new PreconditionKind.Rebuild(), "Id"));
        Precondition[] declared = [On(new PreconditionKind.Computed("(len([Big]))"), "Big"),
            On(new PreconditionKind.Authored(ScriptPart.PostDeploy, 3), "Big")];
        Precondition[] unheld = [On(new PreconditionKind.Temporal(Value(Name.Of("Start")), Value(Name.Of("End")), null), "Start", "End"),
            On(new PreconditionKind.SeedDrift([], ScriptPart.PostDeploy, 3), "Id")];
        var change = Value(Change.Between(model.Elements, model.Elements, []));

        var queries = Queries(model, model, [scan, rebuild, .. declared, .. unheld]);
        var table = Assert.Single(queries.Tables);
        var read = AggregateQueries.Read(table, AggregateQueriesTests.Answered(table, item => item.Precondition is null ? 7 : 0));

        Assert.All(declared, p => Assert.IsType<Coordinates.Declared>(Value(Coordinates.Of(p, change, model.Elements))));
        Assert.Equal(SortedArray.Of(unheld), SortedArray.Of(queries.NotPredictable.Select(u => u.Precondition)));
        Assert.Equal(SortedArray.Of(new PreconditionCount(scan, 7, 0), new PreconditionCount(rebuild, 7, 0)), read.Counted);
    }

    /// <summary>
    /// The count against rows planted on a copy, beside what SQL Server does with them: of 851 characters, past 1,700 bytes, one row of
    /// four, which CREATE NONCLUSTERED INDEX refuses with Msg 1946; deleted, the index builds over 850 characters, an empty string and
    /// NULL.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task KeyLength_counts_exactly_the_rows_on_which_CREATE_INDEX_fails_with_Msg_1946()
    {
        var model = Model.Of("N.Id int NOT NULL", "N.Big nvarchar(1000) NULL");
        var key = Key(false, "Big");
        var root = SqlServerFixture.RepositoryRoot(folder.Path);
        using var copy = DisposableCopy.Create(root, await SqlServerFixture.ServerAsync());
        await SqlServerFixture.ExecuteAsync(copy.Copy.Connection, "CREATE TABLE [dbo].[N] ([Id] int NOT NULL, [Big] nvarchar(1000) NULL);"
            + "INSERT dbo.N (Id, Big) VALUES (1, REPLICATE(N'a', 850)), (2, REPLICATE(N'b', 851)), (3, NULL), (4, N'');");

        var counts = Value(AggregateQueries.Count(copy.Copy, Assert.Single(Queries(model, model, key).Tables),
            SqlServer.QueryLog.Start(root)));
        var refused = await Assert.ThrowsAsync<SqlException>(() =>
            SqlServerFixture.ExecuteAsync(copy.Copy.Connection, "CREATE NONCLUSTERED INDEX [IX_N_Big] ON [dbo].[N] ([Big]);"));
        await SqlServerFixture.ExecuteAsync(copy.Copy.Connection,
            "DELETE dbo.N WHERE Id = 2; CREATE NONCLUSTERED INDEX [IX_N_Big] ON [dbo].[N] ([Big]);");

        Assert.Equal((new PreconditionCount(key, 4, 1), 1946), (Assert.Single(counts.Counted), refused.Number));
    }

    private static Precondition Key(bool clustered, params string[] columns) => new(Model.TableName("N"),
        SortedArray.Of(columns.Select(c => Value(Name.Of(c)))),
        new PreconditionKind.KeyLength(Value(Name.Of(clustered ? "CX_N" : "IX_N_Big")), clustered));

    /// <summary>The queries of preconditions the vocabulary raises, each placed in the environment as WP 2.5 places them.</summary>
    private static AggregateQueries.Queries Queries(Model before, Model after, params Precondition[] preconditions)
    {
        var change = Value(Change.Between(before.Elements, after.Elements, []));
        var counted = preconditions.Select(p => Value(Coordinates.Of(p, change, before.Elements, AggregateQueriesTests.Database)))
            .OfType<Coordinates.Counted>().ToList();
        return Value(AggregateQueries.Of(counted, after.Elements, before.Elements, AggregateQueriesTests.Database,
            SqlServer.Session.Assumed));
    }
}
