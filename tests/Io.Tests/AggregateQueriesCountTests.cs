using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DbChange.Kernel;
using DbChange.Kernel.Tests;
using DbChange.Tests;
using Microsoft.Data.SqlClient;
using Xunit;
using static DbChange.Tests.Expect;
using Case = DbChange.Io.Tests.AggregateQueriesTests.Case;

namespace DbChange.Io.Tests;

/// <summary>
/// io/AggregateQueries against rows planted by plain INSERTs on a copy (V3_MILESTONES.md WP 2.3): each builder's query, run through
/// SqlServer.Measure, counts exactly the rows planted to break its precondition, value class by value class and collation class by
/// collation class, and every table is read by one statement; the session's DATEFORMAT decides how a date written as a string converts;
/// TableEmpty reads the same rows from sys.partitions, as a login holding VIEW DEFINITION alone can; and a query that fails against an
/// environment classified real reports its number and its precondition, with no value.
/// </summary>
public sealed class AggregateQueriesCountTests : IDisposable
{
    private readonly ScratchFolder folder;

    /// <summary>The test's repository root, its environments file naming no environment until a test names one.</summary>
    private readonly string root;

    public AggregateQueriesCountTests()
    {
        folder = ScratchFolder.UnderRepository("aggregate-counts-under-test");
        root = SqlServerFixture.RepositoryRoot(folder.Path);
    }

    public void Dispose() => folder.Dispose();

    /// <summary>NotNull: the rows whose column holds NULL; an empty string is a value.</summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task NotNull_counts_the_rows_planted_with_NULL()
    {
        var change = new Case(Model.Of("Customer.Id int NOT NULL", "Customer.Email nvarchar(100) NULL"),
            Model.Of("Customer.Id int NOT NULL", "Customer.Email nvarchar(100) NOT NULL"));

        var (queries, counts, _) = await Counted(change,
            "INSERT dbo.Customer (Id, Email) VALUES (1, N'a@example.invalid'), (2, NULL), (3, NULL), (4, N'');");

        Assert.Equal("rows 4, violating 2", Line(counts, Kind(queries, "NotNull", "Email")));
    }

    /// <summary>
    /// Fits counts each value class as an item of its own, one row planted for each: over length and over bytes (trailing spaces alone,
    /// which ALTER COLUMN trims without an error) into a shorter string; not convertible, an empty string and overflow into a number; a
    /// lossy round trip into bit and into date; and a day-first date, which mdy reads as not convertible. The violating rows are those
    /// ALTER COLUMN fails on: over length, overflow, a value that does not convert, and the empty string decimal refuses, where int reads
    /// it as 0; over bytes and a lossy round trip convert without an error. Seven retypes on one table are items of one SELECT, so the
    /// run's log holds one aggregate query for the table.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task Fits_counts_the_rows_planted_in_each_value_class_and_reads_the_table_once()
    {
        var (queries, counts, log) = await Counted(Retypes, Misfits);

        Assert.Equal("rows 20, violating 1, over-length 1, over-bytes 1", Line(counts, Kind(queries, "Fits", "Code")));
        Assert.Equal("rows 20, violating 1, not-convertible 1, empty-string 2", Line(counts, Kind(queries, "Fits", "Qty")));
        Assert.Equal("rows 20, violating 3, not-convertible 1, empty-string 1, overflow 1", Line(counts, Kind(queries, "Fits", "Price")));
        Assert.Equal("rows 20, violating 1, overflow 1", Line(counts, Kind(queries, "Fits", "Total")));
        Assert.Equal("rows 20, violating 0, lossy-round-trip 1", Line(counts, Kind(queries, "Fits", "Flag")));
        Assert.Equal("rows 20, violating 0, overflow 0, lossy-round-trip 1", Line(counts, Kind(queries, "Fits", "PlacedOn")));
        Assert.Equal("rows 20, violating 1, not-convertible 1, empty-string 0", Line(counts, Kind(queries, "Fits", "Born")));
        Assert.Equal(["[dbo].[R]"], Entries(log).Where(e => !e.Site.StartsWith("Synonyms: ", StringComparison.Ordinal))
            .Select(e => e.Site.Split(':')[0]));
    }

    /// <summary>
    /// The session sets the language and DATEFORMAT before the query, as parameters: under us_english and mdy, the assumed defaults,
    /// 13/04/2001 is not a date; under dmy it is 13 April 2001, so the same rows count one value fewer as not convertible.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task The_session_s_DATEFORMAT_decides_how_a_date_written_as_a_string_converts()
    {
        var (queries, counts, log) = await Counted(Retypes, Misfits, new SqlServer.Session("us_english", SqlServer.DateFormat.Dmy));

        Assert.Equal("rows 20, violating 0, not-convertible 0, empty-string 0", Line(counts, Kind(queries, "Fits", "Born")));
        Assert.Contains("DECLARE @dateformat nvarchar(4000) = N'dmy';", log, StringComparison.Ordinal);
        Assert.Contains("SET DATEFORMAT @dateformat;", log, StringComparison.Ordinal);
    }

    /// <summary>
    /// ForeignKey: the child rows whose key columns are all non-null and match no parent row, a NULL in any column passing; a parent that
    /// holds a key twice still counts each child once; and a parent table the change creates holds no row, so every child row whose key
    /// is non-null is an orphan (QST-101).
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task ForeignKey_counts_the_planted_orphans_once_each_and_every_non_null_key_to_a_parent_the_change_creates()
    {
        var environment = Model.Of("Parent.Id int NOT NULL", "Pair.A int NOT NULL", "Pair.B int NOT NULL", "Child.Id int NOT NULL",
                "Child.PId int NULL", "Child.A int NULL", "Child.B int NULL", "Child.NId int NULL")
            .PrimaryKey("PK_Parent", "Parent", "Id").PrimaryKey("PK_Pair", "Pair", "A", "B").PrimaryKey("PK_Child", "Child", "Id");
        var head = environment.With("NewParent.Id int NOT NULL").PrimaryKey("PK_NewParent", "NewParent", "Id")
            .ForeignKey("FK_Child_Parent", "Child", ["PId"], "Parent", ["Id"]).ForeignKey("FK_Child_Pair", "Child", ["A", "B"], "Pair", ["A", "B"])
            .ForeignKey("FK_Child_NewParent", "Child", ["NId"], "NewParent", ["Id"]);

        var (queries, counts, _) = await Counted(new Case(environment, head),
            "INSERT dbo.Parent (Id) VALUES (1), (1), (2); INSERT dbo.Pair (A, B) VALUES (1, 1), (1, 2);"
            + "INSERT dbo.Child (Id, PId, A, B, NId) VALUES (1, 1, 1, 1, NULL), (2, 3, 1, 2, 5), (3, NULL, 2, 1, NULL), (4, 3, NULL, 9, 7),"
            + " (5, 2, 1, NULL, NULL);");

        Assert.Equal("rows 5, violating 2", Line(counts, Kind(queries, "ForeignKey", "PId")));
        Assert.Equal("rows 5, violating 1", Line(counts, Kind(queries, "ForeignKey", "A")));
        Assert.Equal("rows 5, violating 2", Line(counts, Kind(queries, "ForeignKey", "NId")));
    }

    /// <summary>
    /// Unique counts, under the collation the index is built with (Latin1_General_CI_AI, where the environment holds Latin1_General_CS_AS),
    /// the rows of the duplicate groups of each collation class, each planted: exact, case, accent, trailing space and two NULLs, and a
    /// pair that differs in case and a trailing space, which is the case class.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task Unique_counts_the_rows_planted_in_each_collation_class_under_the_collation_the_index_is_built_with()
    {
        var change = new Case(Model.Of("U.Id int NOT NULL", "U.Code nvarchar(20) NULL Latin1_General_CS_AS"),
            Model.Of("U.Id int NOT NULL", "U.Code nvarchar(20) NULL Latin1_General_CI_AI").UniqueIndex("UX_U_Code", "U", "Code"));

        var (queries, counts, _) = await Counted(change,
            "INSERT dbo.U (Id, Code) VALUES (1, N'abc'), (2, N'abc'), (3, N'ABC'), (4, N'x'), (5, N'x '), (6, N'é'), (7, N'e'), (8, NULL),"
            + " (9, NULL), (10, N'q'), (11, N'Q '), (12, N'zz');");

        Assert.Equal("rows 12, violating 11, exact 3, case 5, accent 2, trailing-space 2, nulls 2", Line(counts, Kind(queries, "Unique", "Code")));
        Assert.Equal("rows 12, violating 0, over-length 0, over-bytes 0", Line(counts, Kind(queries, "Fits", "Code")));
    }

    /// <summary>
    /// A filtered index counts the rows its filter holds alone, so two NULLs it leaves out are no duplicate and it counts no NULL class
    /// (QST-37); a key of two integers counts exact duplicates (QST-40); and a key over a column the change creates reads the value every
    /// existing row holds, its default or NULL, so the table's rows are one duplicate group (QST-100, QST-46).
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task Unique_counts_within_a_filter_over_two_integers_and_over_a_column_the_change_creates()
    {
        var environment = Model.Of("V.Id int NOT NULL", "V.Email nvarchar(50) NULL Latin1_General_CI_AS", "V.OrderId int NOT NULL",
            "V.LineNumber int NOT NULL");
        var head = environment.With("V.Sku nvarchar(20) NOT NULL", "V.ExternalRef nvarchar(40) NULL")
            .Default("DF_V_Sku", "V", "Sku", "(N'NONE')")
            .FilteredIndex("UX_V_Email", "V", ["Email"], "[Email] IS NOT NULL", "Email")
            .UniqueIndex("UX_V_Line", "V", "OrderId", "LineNumber").UniqueIndex("UX_V_Sku", "V", "Sku")
            .UniqueIndex("UX_V_ExternalRef", "V", "ExternalRef");

        var (queries, counts, _) = await Counted(new Case(environment, head),
            "INSERT dbo.V (Id, Email, OrderId, LineNumber) VALUES (1, N'a', 1, 1), (2, N'A', 1, 1), (3, NULL, 1, 2), (4, NULL, 2, 1),"
            + " (5, N'b', 3, 3);");

        Assert.Equal("rows 5, violating 2, exact 0, case 2, trailing-space 0", Line(counts, Kind(queries, "Unique", "Email")));
        Assert.Equal("rows 5, violating 2, exact 2", Line(counts, Kind(queries, "Unique", "LineNumber")));
        Assert.Equal("rows 5, violating 5, exact 5", Line(counts, Kind(queries, "Unique", "Sku")));
        Assert.Equal("rows 5, violating 5, nulls 5", Line(counts, Kind(queries, "Unique", "ExternalRef")));
    }

    /// <summary>
    /// Check counts the rows that make the check false, an unknown passing: a NULL compared is no violation; a CONVERT is read through its
    /// TRY_ form, a value it fails on counted, since the check would fail on it; and a column the change creates reads, where its name
    /// would stand, NULL, which no row breaks, or its default, which every row breaks here (QST-62).
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task Check_counts_the_rows_that_make_it_false_or_fail_its_conversion_and_reads_a_created_column_as_its_default()
    {
        var environment = Model.Of("K.Id int NOT NULL", "K.Total int NULL", "K.Ref varchar(20) NULL Latin1_General_CI_AS");
        var head = environment.With("K.Qty int NULL", "K.Rank int NOT NULL").Default("DF_K_Rank", "K", "Rank", "((0))")
            .Check("CK_K_Total", "K", "[Total]>(0)", ["Total"]).Check("CK_K_Ref", "K", "CONVERT([int],[Ref])>(0)", ["Ref"])
            .Check("CK_K_Qty", "K", "[Qty]>(0)", ["Qty"]).Check("CK_K_Rank", "K", "[Rank]>(0)", ["Rank"]);

        var (queries, counts, _) = await Counted(new Case(environment, head),
            "INSERT dbo.K (Id, Total, Ref) VALUES (1, 5, '7'), (2, 0, '0'), (3, NULL, 'abc'), (4, -1, NULL);");

        Assert.Equal("rows 4, violating 2", Line(counts, Kind(queries, "Check", "Total")));
        Assert.Equal("rows 4, violating 2", Line(counts, Kind(queries, "Check", "Ref")));
        Assert.Equal("rows 4, violating 0", Line(counts, Kind(queries, "Check", "Qty")));
        Assert.Equal("rows 4, violating 4", Line(counts, Kind(queries, "Check", "Rank")));
    }

    /// <summary>
    /// TableEmpty: the table's rows, read by the table's query, and the same rows read from sys.partitions. A login holding VIEW DEFINITION
    /// alone (§17 item 21) is refused the table's rows (Msg 229) and reads sys.partitions; a table sys.partitions does not show is never
    /// read as empty.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "P2")]
    public async Task TableEmpty_reads_the_table_s_rows_and_the_same_rows_from_sys_partitions_as_a_VIEW_DEFINITION_only_login()
    {
        var environment = Model.Of("Customer.Id int NOT NULL", "Customer.Email nvarchar(100) NULL", "Hidden.Id int NOT NULL");
        var change = new Case(environment, environment, ["Customer", "Hidden"]);
        var server = await SqlServerFixture.ServerAsync();
        using var copy = DisposableCopy.Create(root, server);
        var login = copy.Copy.Name + "_viewdefinition";
        try
        {
            await SqlServerFixture.ExecuteAsync(copy.Copy.Connection, "CREATE TABLE dbo.Customer (Id int NOT NULL, Email nvarchar(100) NULL);"
                + " INSERT dbo.Customer (Id, Email) VALUES (1, NULL), (2, N'b'), (3, N'c');");
            var password = await ViewDefinitionOnly(copy.Copy.Connection, login);
            var reader = Resolved("prod", new SqlConnectionStringBuilder(copy.Copy.Connection) { UserID = login, Password = password,
                IntegratedSecurity = false }.ConnectionString);
            var (customer, hidden) = (change.Counted.Single(c => c.Precondition.Table == Model.TableName("Customer")),
                change.Counted.Single(c => c.Precondition.Table == Model.TableName("Hidden")));
            var log = SqlServer.QueryLog.Start(root);
            var table = Value(AggregateQueries.Of([customer], environment.Elements, environment.Elements, AggregateQueriesTests.Database,
                SqlServer.Session.Assumed)).Tables.Single();

            var counts = Value(AggregateQueries.Count(copy.Copy, table, log));
            var partitions = Value(AggregateQueries.Count(copy.Copy, Value(AggregateQueries.Partitions(customer, SqlServer.Session.Assumed)),
                log));
            var refused = Value(AggregateQueries.Count(reader, table, log));
            var read = Value(AggregateQueries.Count(reader, Value(AggregateQueries.Partitions(customer, SqlServer.Session.Assumed)), log));
            var missing = Value(AggregateQueries.Count(reader, Value(AggregateQueries.Partitions(hidden, SqlServer.Session.Assumed)), log));

            Assert.Equal("rows 3, violating 3", Line(counts, customer.Precondition));
            Assert.Equal("rows 3, violating 3", Line(partitions, customer.Precondition));
            Assert.Equal("SQL Server raised Msg 229; message withheld", Assert.Single(refused.Unanswered).Why);
            Assert.Equal("rows 3, violating 3", Line(read, customer.Precondition));
            Assert.Contains("never read as empty", Assert.Single(missing.Unanswered).Why, StringComparison.Ordinal);
        }
        finally
        {
            await SqlServerFixture.DropAsync(server, copy.Copy.Name.ToString());
        }
    }

    /// <summary>
    /// §18, a value escaping in an error: a check that compares a varchar column with a number makes SQL Server convert it, and Msg 245
    /// quotes the planted value. Against an environment classified real the table's query fails, each precondition is counted again by a
    /// query of its own, and the check is reported by its number alone; the retype beside it, read through TRY_CONVERT, counts the same
    /// value as not convertible and raises nothing. The value appears in no count, reason or line of the run's log.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "X2")]
    public async Task A_query_that_fails_against_a_real_environment_reports_its_number_and_precondition_and_no_value()
    {
        var planted = PlantedValue.Unique();
        var environment = Model.Of("W.Id int NOT NULL", "W.Ref varchar(40) NULL Latin1_General_CI_AS",
            "W.Code varchar(40) NULL Latin1_General_CI_AS");
        var change = new Case(environment, environment.With("W.Code int NULL").Check("CK_W_Ref", "W", "[Ref]>(0)", ["Ref"]));
        using var copy = DisposableCopy.Create(root, await SqlServerFixture.ServerAsync());
        await SqlServerFixture.ExecuteAsync(copy.Copy.Connection, Tables(environment)
            + "INSERT dbo.W (Id, Ref, Code) VALUES (1, @name, @name), (2, '5', '7');", planted.Text);
        Assert.Contains(planted.Text, (await Assert.ThrowsAsync<SqlException>(() => SqlServerFixture.ScalarAsync(copy.Copy.Connection,
            "SELECT SUM(CASE WHEN NOT (Ref > 0) THEN 1 ELSE 0 END) FROM dbo.W;"))).Message, StringComparison.Ordinal);
        var uat = Resolved("uat", copy.Copy.Connection);
        var log = SqlServer.QueryLog.Start(root);
        var queries = change.Queries();

        var counts = Value(AggregateQueries.Count(uat, queries.Tables.Single(), log));

        Assert.Equal("rows 2, violating 1, not-convertible 1, empty-string 0", Line(counts, Kind(queries, "Fits", "Code")));
        var check = Assert.Single(counts.Unanswered);
        Assert.Equal((Kind(queries, "Check", "Ref"), "SQL Server raised Msg 245; message withheld"), (check.Precondition, check.Why));
        Assert.Contains("failed, Msg 245", File.ReadAllText(log.Path), StringComparison.Ordinal);
        planted.AbsentFrom(string.Join('\n', counts.Counted.Concat<object>(counts.Classes).Concat(counts.Unanswered)
            .Concat(queries.Notes.Select(n => n.Message))) + File.ReadAllText(log.Path));
    }

    /// <summary>Seven retypes on one table: a shorter string, a string to int and to decimal, a decimal to smallint, int to bit, a date and
    /// time to date, and a string to date.</summary>
    private static readonly Case Retypes = new(
        Model.Of("R.Id int NOT NULL", "R.Code nvarchar(50) NULL", "R.Qty nvarchar(10) NULL", "R.Price varchar(20) NULL Latin1_General_CI_AS",
            "R.Total decimal(18,2) NULL", "R.Flag int NULL", "R.PlacedOn datetime2(7) NULL", "R.Born nvarchar(10) NULL"),
        Model.Of("R.Id int NOT NULL", "R.Code nvarchar(10) NULL", "R.Qty int NULL", "R.Price decimal(9,2) NULL", "R.Total smallint NULL",
            "R.Flag bit NULL", "R.PlacedOn date NULL", "R.Born date NULL"));

    /// <summary>One row per value class of <see cref="Retypes"/>, and a row that fits beside each.</summary>
    private const string Misfits =
        "INSERT dbo.R (Id, Code) VALUES (1, N'abcdefghijklm'), (2, N'abc         '), (3, N'fits');"
        + "INSERT dbo.R (Id, Qty) VALUES (4, N'n/a'), (5, N''), (6, N'   '), (7, N'12');"
        + "INSERT dbo.R (Id, Price) VALUES (8, '12345678901.5'), (9, 'abc'), (10, ''), (11, '1.25');"
        + "INSERT dbo.R (Id, Total) VALUES (12, 40000.00), (13, 10.50);"
        + "INSERT dbo.R (Id, Flag) VALUES (14, 5), (15, 1);"
        + "INSERT dbo.R (Id, PlacedOn) VALUES (16, '2020-01-01T10:00:00'), (17, '2020-01-02T00:00:00');"
        + "INSERT dbo.R (Id, Born) VALUES (18, N'13/04/2001'), (19, N'02/01/2000'), (20, N'2000-02-01');";

    /// <summary>
    /// The change counted on a copy holding the environment's tables, planted by <paramref name="plant"/>: the queries the builders make,
    /// every table's counts together, and the run's log.
    /// </summary>
    private async Task<(AggregateQueries.Queries Queries, AggregateQueries.Counts Counts, string Log)> Counted(Case change, string plant,
        SqlServer.Session? session = null)
    {
        using var copy = DisposableCopy.Create(root, await SqlServerFixture.ServerAsync());
        await SqlServerFixture.ExecuteAsync(copy.Copy.Connection, Tables(change.Environment) + plant);
        var queries = change.Queries(session);
        var log = SqlServer.QueryLog.Start(root);
        var counts = queries.Tables.Select(t => Value(AggregateQueries.Count(copy.Copy, t, log))).ToList();
        Assert.Empty(queries.NotPredictable);
        return (queries, new AggregateQueries.Counts(SortedArray.Of(counts.SelectMany(c => c.Counted)),
            SortedArray.Of(counts.SelectMany(c => c.Classes)), SortedArray.Of(counts.SelectMany(c => c.Unanswered))),
            File.ReadAllText(log.Path));
    }

    /// <summary>A CREATE TABLE for each table of the model, each column as the model declares it.</summary>
    private static string Tables(Model model) => string.Concat(model.Columns.GroupBy(c => (c.Schema, c.Table)).Select(t =>
        "CREATE TABLE [" + t.Key.Schema + "].[" + t.Key.Table + "] (" + string.Join(", ", t.Select(c => "[" + c.Name + "] "
            + AggregateQueries.Written(c.Type) + (c.Type.Collation is { } collation ? " COLLATE " + collation : "")
            + (c.Nullable ? " NULL" : " NOT NULL"))) + ");\n"));

    /// <summary>The precondition of the kind given whose first column is the one named.</summary>
    private static Precondition Kind(AggregateQueries.Queries queries, string kind, string column) =>
        queries.Tables.SelectMany(t => t.Preconditions)
            .Single(p => p.ToString().StartsWith(kind + " ", StringComparison.Ordinal) && p.Columns.Count > 0 && p.Columns[0].Base == column);

    /// <summary>A precondition's count and its classes' counts in their order: "rows 4, violating 2, exact 1".</summary>
    private static string Line(AggregateQueries.Counts counts, Precondition precondition) =>
        counts.Counted.Single(c => c.Precondition == precondition) is var count
            ? "rows " + count.Rows + ", violating " + count.Violating + string.Concat(counts.Classes.Where(c => c.Precondition == precondition)
                .Select(c => ", " + AggregateQueries.Word(c.Class) + " " + c.Rows))
            : "";

    /// <summary>A login and its user, named after the copy, holding VIEW DEFINITION on it and nothing else; its password.</summary>
    private static async Task<string> ViewDefinitionOnly(string copy, string login)
    {
        var password = "Vd1!" + Guid.NewGuid().ToString("N");
        await SqlServerFixture.ExecuteAsync(copy,
            "DECLARE @sql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@name) + N' WITH PASSWORD = ' + QUOTENAME(@password, '''') "
            + "+ N', DEFAULT_DATABASE = ' + QUOTENAME(DB_NAME()) + N', CHECK_POLICY = OFF; CREATE USER ' + QUOTENAME(@name) + N' FOR LOGIN '"
            + " + QUOTENAME(@name) + N'; GRANT VIEW DEFINITION TO ' + QUOTENAME(@name) + N';'; EXEC (@sql);",
            login, ("@password", password));
        return password;
    }

    /// <summary>A named environment classified real, its connection the string given in a file outside git, resolved through
    /// dbchange/environments.json under the test's root.</summary>
    private SqlServer.Database Resolved(string name, string connection)
    {
        var file = folder.File(name + ".connection", connection);
        if (!OperatingSystem.IsWindows())
        {
            // io/SqlServer refuses a connection file others can read
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        EnvironmentsJson.Of(name, new("file:" + file.Replace('\\', '/'), "localhost", Classification: "real")).WriteTo(root);
        return Value(SqlServer.Resolve(Value(SqlServer.Target("env:" + name, "--target")), root));
    }

    /// <summary>queries.log as entries: the site and the outcome of each, as AggregateQueryTests reads it.</summary>
    private static (string Site, string Outcome)[] Entries(string log) =>
        [.. Regex.Matches(log, @"^-- \S+ \S+ (?<site>.+): (?<outcome>\d+ rows?|failed, Msg \d+)\n", RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .Select(m => (m.Groups["site"].Value, m.Groups["outcome"].Value))];
}
