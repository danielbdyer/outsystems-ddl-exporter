using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Kernel.Tests;
using Xunit;
using static DbChange.Kernel.Tests.KernelProperties;

namespace DbChange.Io.Tests;

/// <summary>
/// The pure cases of the Facsimile's question catalog (archive/v2/ssdt-agent/facsimile/cases/10-questions.md) that read a plan's statements
/// (WP 2.1's second slice): each case's plan read by io/PlanStatements, its BlockOnPossibleDataLoss sites by io/BlockOnPossibleDataLoss, the
/// property read by Preconditions.Of, and the plan by the kernel's vocabulary, whose preconditions and unclassified statements are the
/// case's expectation. Every row of the vocabulary names a case that fails when the row is removed (the Facsimile's M1 and E1, QST-95):
/// here for a catalog case, and in BlockOnPossibleDataLossCheckTests for a row whose case is one of spike S2's sample changes.
/// </summary>
public sealed partial class VocabularyCases
{
    /// <summary>A case: the environment's model, the model the change brings, DacFx's plan, the preconditions the plan asserts, the lines of
    /// its unclassified statements, the deploy scripts of the change and of the model before it, and the refactorlog's renames.</summary>
    internal sealed record Case(Model Base, Model Head, string Plan, Precondition[] Expected, int[]? Unclassified = null,
        string? PreDeploy = null, string? PostDeploy = null, string? PreDeployBefore = null, string? PostDeployBefore = null,
        Rename[]? Renames = null);

    /// <summary>A program of releases: the plan of its final target against the environment's schema, the releases before it, each read
    /// with its own deploy scripts, and the preconditions the program asks.</summary>
    internal sealed record ProgramCase(Case Target, Case[] Releases, Precondition[] Expected);

    private static IReadOnlyDictionary<string, Case>? cases;

    private static IReadOnlyDictionary<string, ProgramCase>? programs;

    internal static IReadOnlyDictionary<string, Case> Cases =>
        cases ??= Framed().Concat(Scripted()).ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);

    /// <summary>The catalog's program: R1 is QST-82's two-release pre-deploy over an unchanged model, and R2, its final target, QST-16's
    /// ALTER … NOT NULL planned against the environment's schema; R2 has no pre-deploy script.</summary>
    internal static IReadOnlyDictionary<string, ProgramCase> Programs => programs ??= new Dictionary<string, ProgramCase>(StringComparer.Ordinal)
    {
        ["QST-83"] = new(Cases["QST-16"], [Cases["QST-82"]], [TableEmpty("Customer"), NotNull("Customer.Email"),
            Authored("Customer", ScriptPart.PreDeploy, 1, "Email"), Authored("Customer", ScriptPart.PreDeploy, 6, "Email")]),
    };

    /// <summary>The SQLCMD value a plan of the catalog's names, as DacFx's header gives it.</summary>
    private static readonly SqlCmdValue[] Values = [new(Ok(SqlCmdName.Of("case", "DatabaseName")), "bd_probe", false)];

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("QST-01")]
    [InlineData("QST-02")]
    [InlineData("QST-04")]
    [InlineData("QST-05")]
    [InlineData("QST-06")]
    [InlineData("QST-07")]
    [InlineData("QST-10")]
    [InlineData("QST-14")]
    [InlineData("QST-16")]
    [InlineData("QST-35")]
    [InlineData("QST-41")]
    [InlineData("QST-42")]
    [InlineData("QST-44")]
    [InlineData("QST-45")]
    [InlineData("QST-47")]
    [InlineData("QST-48")]
    [InlineData("QST-49")]
    [InlineData(UniqueOverTheLimit)]
    [InlineData(MaxBesideFixed)]
    [InlineData(RebuiltPrimaryKey)]
    [InlineData(RebuiltUnique)]
    [InlineData("QST-50")]
    [InlineData("QST-55")]
    [InlineData("QST-56")]
    [InlineData("QST-57")]
    [InlineData("QST-59")]
    [InlineData("QST-60")]
    [InlineData("QST-63")]
    [InlineData("QST-64")]
    [InlineData("QST-65")]
    [InlineData("QST-66")]
    [InlineData("QST-67")]
    [InlineData("QST-68")]
    [InlineData(IncludesAComputedColumn)]
    [InlineData(ComputedOnANewTable)]
    [InlineData("QST-69")]
    [InlineData("QST-70")]
    [InlineData("QST-71")]
    [InlineData("QST-72")]
    [InlineData("QST-73")]
    [InlineData("QST-74")]
    [InlineData("QST-76")]
    [InlineData("QST-77")]
    [InlineData("QST-78")]
    [InlineData("QST-79")]
    [InlineData("QST-80")]
    [InlineData("QST-81")]
    [InlineData("QST-82")]
    [InlineData(ExecutedByAScript)]
    [InlineData(FilledByAPostDeployScript)]
    [InlineData("QST-84")]
    [InlineData("QST-85")]
    [InlineData("QST-86")]
    [InlineData("QST-87")]
    [InlineData("QST-88")]
    [InlineData("QST-89")]
    [InlineData("QST-90")]
    [InlineData("QST-91")]
    [InlineData("QST-92")]
    [InlineData("QST-99")]
    [InlineData("QST-105 (a)")]
    [InlineData("QST-105 (b)")]
    [InlineData("QST-107")]
    public void A_pure_case_of_the_question_catalog_reads_its_preconditions_and_its_unclassified_statements_off_the_plan(string id)
    {
        var c = Cases[id];

        var reading = Read(c);

        Assert.Equal(Lines(c.Unclassified ?? []), Lines(reading.Unclassified.Select(s => s.Line)));
        Assert.Equal(SortedArray.Of(c.Expected), reading.Preconditions);
    }

    /// <summary>QST-94: a statement of DacFx's own that reads existing rows and that no row matches is unclassified, by its line,
    /// whatever its shape, and asks nothing the prediction could take as an answer; the reading's one finding is the warning that sends
    /// the change to proof, about that line.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("QST-94", "UPDATE [dbo].[Customer] SET [Email] = N'' WHERE [Email] IS NULL;")]
    [InlineData("QST-94", "DELETE FROM [dbo].[Customer] WHERE [Email] IS NULL;")]
    [InlineData("QST-94", "MERGE [dbo].[Status] AS target USING (VALUES (1, N'Pending')) AS source (Id, Code) ON target.Id = source.Id\n"
        + "WHEN NOT MATCHED THEN INSERT (Id, Code) VALUES (source.Id, source.Code);")]
    [InlineData("QST-94", "TRUNCATE TABLE [dbo].[OrderLine];")]
    [InlineData("QST-94", "SELECT * INTO [dbo].[CustomerCopy] FROM [dbo].[Customer];")]
    [InlineData("QST-94", "INSERT INTO [archive].[OrderArchive] ([Id], [CustomerId]) SELECT [Id], [CustomerId] FROM [dbo].[Order];")]
    [InlineData("QST-94", "ALTER TABLE [dbo].[Order] SWITCH TO [archive].[Order];")]
    [InlineData("QST-94", "DBCC CHECKIDENT ('dbo.Customer');")]
    [InlineData("QST-94", "EXECUTE [dbo].[pReconcileOrders];")]
    [InlineData("QST-94", "ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Status];")]
    public void A_statement_of_DacFx_s_own_that_reads_rows_and_matches_no_row_is_unclassified_by_its_line(string id, string statement)
    {
        var reading = Read(new Case(Pg, Pg, "PRINT N'" + id + "';\nGO\n" + statement + "\nGO\n", []));

        Assert.Equal("3", Lines(reading.Unclassified.Select(s => s.Line)));
        Assert.Empty(reading.Preconditions);
        Assert.Equal([(Severity.Warning, "plan.unclassified-statement", "Line 3 of the plan's script")],
            reading.Findings.Select(f => (f.Severity, f.Code, f.Subject)));
    }

    /// <summary>
    /// QST-83, the Facsimile's F8: a program of two releases asks what its final target's plan asks, QST-16's TableEmpty and NotNull, and
    /// the two Authored of its first release's pre-deploy script, QST-82's, whose ALTER … NOT NULL meets the NULLs the final target's
    /// NotNull counts, where the first release's plan alone asks only its two Authored.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("QST-83")]
    public void A_program_of_releases_asks_what_its_final_target_asks_and_the_authored_preconditions_of_every_release(string id)
    {
        var program = Programs[id];

        Assert.Equal(SortedArray.Of(program.Expected), Vocabulary.Program(Read(program.Target), program.Releases.Select(r => Read(r))));
    }

    /// <summary>QST-99: each statement of a plan is in one class, read by its line: a BlockOnPossibleDataLoss check and an ALTER COLUMN …
    /// NOT NULL are data-sensitive, a non-unique index a Scan, and the RAISERROR, a nullable column and a module's refresh read
    /// nothing.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Each_statement_of_QST_99_s_plan_is_in_one_class()
    {
        var reading = Read(Cases["QST-99"]);

        Assert.Equal(["1 DataSensitive", "2 ReadsNothing", "4 DataSensitive", "6 DataInsensitive", "9 ReadsNothing", "12 ReadsNothing"],
            reading.Statements.Select(s => s.Statement.Line + " " + s.Class));
    }

    /// <summary>
    /// The coordinate rules over the preconditions of two plans of a table the change creates, as WP 2.5 chains Coordinates.Of,
    /// ExistingData.Of and Outcome.Predict: an index over the new table's computed column computes it over no existing row, so the plan
    /// asks a Scan, which the change answers, and the change applies; and a changed post-deploy INSERT that fills the new table before
    /// DacFx validates its foreign key leaves an Authored, which is declared by a person and never answered as an empty table, so the
    /// prediction refuses it as plan.undeclared until its state is declared. Neither chain ends in an exception.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(ComputedOnANewTable, null)]
    [InlineData(FilledByAPostDeployScript, "plan.undeclared")]
    public void A_plan_over_a_table_the_change_creates_is_answered_from_the_change_or_declared_by_a_person(string id, string? refused)
    {
        var c = Cases[id];
        var change = Ok(Change.Between(c.Base.Elements, c.Head.Elements, SortedArray.Of(c.Renames ?? [])));
        var preconditions = Read(c).Preconditions;

        var located = preconditions.Select(p => Ok(Coordinates.Of(p, change, c.Base.Elements))).ToList();
        var predicted = Outcome.Predict(ExistingData.Of(located.OfType<Coordinates.Answered>().Select(a => a.Count)), [], preconditions, []);

        Assert.Equal(preconditions.Where(p => p.Kind.Declared), located.OfType<Coordinates.Declared>().Select(d => d.Precondition));
        Assert.Equal(preconditions.Where(p => !p.Kind.Declared), located.OfType<Coordinates.Answered>().Select(a => a.Count.Precondition));
        Assert.Equal(refused ?? "applies", predicted.Match(o => o is Outcome.Applies ? "applies" : "blocks", e => e.Code));
    }

    /// <summary>
    /// QST-95, the Facsimile's M1: each row of the vocabulary whose case is the catalog's makes its case fail when it is removed, the
    /// case then losing a precondition, asking another, or holding a statement no row matches; a row whose case is a sample change of
    /// spike S2 is held so by BlockOnPossibleDataLossCheckTests, against that change's plan.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void QST_95_removing_any_row_of_the_vocabulary_whose_case_is_the_catalog_s_makes_that_case_fail()
    {
        var rows = Vocabulary.Rows.Where(r => !r.Case.StartsWith("S2 ", StringComparison.Ordinal)).ToList();
        var unnamed = rows.Where(r => !Named(r).Any()).Select(r => r.Type + " for " + r.Case);
        var kept = rows.SelectMany(row => Named(row).Select(id => (Row: row, Id: id)))
            .Where(r => Same(Read(Cases[r.Id], [.. Vocabulary.Rows.Where(other => !ReferenceEquals(other, r.Row))]), Cases[r.Id]))
            .Select(r => r.Row.Type + " " + string.Join("|", r.Row.With) + " for " + r.Id);

        Assert.Empty(unnamed.Concat(kept));
    }

    /// <summary>Every case the kernel's QuestionCases leaves to this port, by its identifier, is a case here.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Every_case_the_kernel_s_question_cases_leave_to_the_vocabulary_is_a_case_here()
    {
        var left = Regex.Matches(Repository.Read("tests/Kernel.Tests/QuestionCases.cs"), @"\[""(?<id>QST-\d+)""\] = ""VocabularyCases:")
            .Select(m => m.Groups["id"].Value).ToList();

        Assert.NotEmpty(left);
        Assert.DoesNotContain(left, id => !Cases.Keys.Concat(Programs.Keys).Any(k => k == id || k.StartsWith(id + " (", StringComparison.Ordinal)));
    }

    /// <summary>The case's plan read by the vocabulary, with the rows given or the vocabulary's own.</summary>
    internal static Reading Read(Case c, IReadOnlyList<VocabularyRow>? rows = null)
    {
        var (before, after) = (c.Base.Elements, c.Head.Elements);
        var change = Ok(Change.Between(before, after, SortedArray.Of(c.Renames ?? [])));
        var listing = Ok(PlanStatements.Read(c.Plan, Values, new(c.PreDeploy, c.PostDeploy), new(c.PreDeployBefore, c.PostDeployBefore)));
        var sites = BlockOnPossibleDataLoss.Sites(Ok(BlockOnPossibleDataLoss.Read(c.Plan, Values)));
        return Vocabulary.Read(listing.Plan, listing.ScriptsBefore, Ok(Preconditions.Of(before, after, change, sites)), before, after, change,
            rows);
    }

    /// <summary>The cases a row names: the case of that identifier, or each of its variants, QST-105 (a) and (b).</summary>
    private static IEnumerable<string> Named(VocabularyRow row) =>
        Cases.Keys.Where(k => k == row.Case || k.StartsWith(row.Case + " (", StringComparison.Ordinal));

    private static bool Same(Reading reading, Case c) =>
        reading.Preconditions == SortedArray.Of(c.Expected) && Lines(reading.Unclassified.Select(s => s.Line)) == Lines(c.Unclassified ?? []);

    private static string Lines(IEnumerable<int> lines) => string.Join(",", lines);

    internal static Precondition TableEmpty(string table) => new(Model.TableName(table), [], new PreconditionKind.TableEmpty());

    internal static Precondition NotNull(string column) => On(column, new PreconditionKind.NotNull());

    internal static Precondition Fits(string column, string before, string after) =>
        On(column, new PreconditionKind.Fits(Model.Type(before), Model.Type(after)));

    internal static Precondition Unique(string table, params string[] columns) =>
        new(Model.TableName(table), Names(columns), new PreconditionKind.Unique(null, []));

    internal static Precondition ForeignKey(string table, string parent, params (string Column, string Referenced)[] pairs) =>
        new(Model.TableName(table), Names(pairs.Select(p => p.Column)), new PreconditionKind.ForeignKey(Model.TableName(parent),
            SortedArray.Of(pairs.Select(p => new PreconditionKind.ForeignKey.Pair(Ok(Name.Of(p.Column)), Ok(Name.Of(p.Referenced)))))));

    internal static Precondition Check(string table, string expression, params string[] reads) =>
        new(Model.TableName(table), Names(reads), new PreconditionKind.Check(expression));

    internal static Precondition KeyLength(string table, string index, bool clustered, params string[] columns) =>
        new(Model.TableName(table), Names(columns), new PreconditionKind.KeyLength(Ok(Name.Of(index)), clustered));

    internal static Precondition Rebuild(string table, params string[] identity) =>
        new(Model.TableName(table), Names(identity), new PreconditionKind.Rebuild());

    internal static Precondition Computed(string column, string expression) => On(column, new PreconditionKind.Computed(expression));

    internal static Precondition Temporal(string table, string start, string end, string? current) =>
        new(Model.TableName(table), Names([start, end]),
            new PreconditionKind.Temporal(Ok(Name.Of(start)), Ok(Name.Of(end)), current is null ? null : Model.TableName(current)));

    internal static Precondition Scan(string table) => new(Model.TableName(table), [], new PreconditionKind.Scan());

    internal static Precondition Authored(string table, ScriptPart part, int line, params string[] columns) =>
        new(Model.TableName(table), Names(columns), new PreconditionKind.Authored(part, line));

    internal static Precondition SeedDrift(string table, string[] key, string[] code, ScriptPart part, int line) =>
        new(Model.TableName(table), Names(key), new PreconditionKind.SeedDrift(Names(code), part, line));

    private static SortedArray<Name> Names(IEnumerable<string> names) => SortedArray.Of(names.Select(n => Ok(Name.Of(n))));

    private static Precondition On(string column, PreconditionKind kind)
    {
        var key = Model.ColumnKey(column);
        return new Precondition(key.Parent!.Name, [key.Name], kind);
    }
}
