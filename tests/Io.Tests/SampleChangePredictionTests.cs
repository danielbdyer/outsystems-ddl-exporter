using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// The corpus (V3_MILESTONES.md WP 2.6): each sample change of tests/Golden/changes/ predicted on three copies of the golden project's
/// base, as its predict.txt states. The preconditions come from the change between the base and the head as DacFx reads them, and the
/// BlockOnPossibleDataLoss checks of the head's plan against the base (io's own DacFx.Plan, package to package, under the pipeline
/// profile). The copy is the base published once to the fixture's SQL Server; before each sample change every row is deleted, which is
/// the empty copy; tests/Golden/copies/populated.sql then inserts one row into each table that breaks no rule any sample change adds,
/// which is the populated copy; and the change's violating.sql, where it has one, inserts the rows that break its rules, which is the
/// violating copy, the populated one where it has none. On each, every precondition is counted by io/AggregateQueries, or answered from
/// the change by the coordinate rules, and kernel Outcome.Predict gives the outcome, with the operation the change classifies as when it
/// is one. The INSERT scripts stand until WP 3.5 plants each copy from kernel Witness's plan of its existing data.
/// </summary>
public sealed class SampleChangePredictionTests(CorpusCopy copy) : IClassFixture<CorpusCopy>
{
    private static readonly string Golden = Path.Combine(Repository.Root, "tests", "Golden");

    public static TheoryData<string> Samples => new(GoldenProject.SampleChanges.Select(c => c.Name));

    /// <summary>
    /// predict.txt holds, one a line: each precondition of the change, as <c>precondition &lt;kind&gt; on &lt;table&gt; (&lt;columns&gt;)</c>;
    /// each precondition no read-only query counts, as <c>unanswered &lt;precondition&gt;: &lt;why&gt;</c>; then the outcome on the empty, the
    /// populated and the violating copy, as <c>empty: applies</c>, or <c>blocks by</c> the preconditions that stop it and how it ships, or
    /// <c>refused</c> and the error's code.
    /// </summary>
    [Theory]
    [Trait("Category", "fixture")]
    [MemberData(nameof(Samples))]
    public async Task Each_sample_change_predicts_on_an_empty_a_populated_and_a_violating_copy_what_its_predict_txt_states(string name)
    {
        var expected = Path.Combine(Golden, "changes", name, "predict.txt");
        var violating = Path.Combine(Golden, "changes", name, "violating.sql");
        var change = await Planned.Of(GoldenProject.Change(name));

        var lines = new List<string>();
        await copy.Emptied();
        var empty = change.Predicted(copy.Target, copy.Log);
        await copy.Run(File.ReadAllText(Path.Combine(Golden, "copies", "populated.sql")));
        var populated = change.Predicted(copy.Target, copy.Log);
        if (File.Exists(violating))
        {
            await copy.Run(File.ReadAllText(violating));
        }

        var broken = change.Predicted(copy.Target, copy.Log);
        lines.AddRange(change.Preconditions.Select(p => "precondition " + p));
        lines.AddRange(new[] { empty, populated, broken }.SelectMany(p => p.Unanswered).Distinct().Order(StringComparer.Ordinal)
            .Select(u => "unanswered " + u));
        lines.AddRange(["empty: " + empty.Outcome, "populated: " + populated.Outcome, "violating: " + broken.Outcome]);
        var actual = string.Concat(lines.Select(l => l + "\n"));

        Assert.True(File.Exists(expected), "tests/Golden/changes/" + name + "/ holds no predict.txt; the prediction is:\n" + actual);
        Assert.True(File.ReadAllText(expected) == actual, "tests/Golden/changes/" + name + "/predict.txt states otherwise; the prediction is:\n"
            + actual);
        Assert.True(File.Exists(violating) || populated.Outcome == broken.Outcome);
    }

    /// <summary>The populated copy's script and every violating.sql insert rows and nothing else, so each copy is the one before it with
    /// rows added; and a violating.sql lies beside the edits of a sample change.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Each_copy_script_inserts_rows_and_belongs_to_a_sample_change()
    {
        var scripts = Directory.GetFiles(Path.Combine(Golden, "changes"), "violating.sql", SearchOption.AllDirectories)
            .Append(Path.Combine(Golden, "copies", "populated.sql")).ToList();

        Assert.All(scripts, s => Assert.All(File.ReadAllLines(s).Where(l => l.Trim().Length > 0), l =>
            Assert.Matches(@"^(INSERT INTO |SET IDENTITY_INSERT |    VALUES )", l)));
        Assert.All(scripts.SkipLast(1), s => Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(s)!, "edits.txt")), s));
    }
}

/// <summary>A sample change planned against the base: the models, the change, the checks of its plan, its preconditions, its baseline
/// drift and the operation it classifies as, when it classifies as one.</summary>
internal sealed record Planned(Ssdt.ModelElements Base, Ssdt.ModelElements Head, Change Change, Collation Collation,
    SortedArray<BlockOnPossibleDataLossSite> Sites, SortedArray<Precondition> Preconditions, SortedArray<PlanOperation> Drift, string? Operation)
{
    public static async Task<Planned> Of(SampleChange sample)
    {
        var (basePackage, headPackage) = (await GoldenProject.Built(), await GoldenProject.Built(sample));
        var (before, after) = (await GoldenProject.Model(), await GoldenProject.Model(sample));
        var collation = Expect.Value(Ssdt.CollationOf(after.Elements));
        var change = Expect.Value(Change.Between(before.Elements, after.Elements, after.Renames, collation));
        using var source = Expect.Value(Ssdt.Open(headPackage));
        using var target = Expect.Value(Ssdt.Open(basePackage));
        var plan = Expect.Value(DacFx.Plan(source, target, "Corpus", Expect.Value(PublishProfiles.Load(GoldenProject.Profile)), []));
        var sites = BlockOnPossibleDataLoss.Sites(Expect.Value(BlockOnPossibleDataLoss.Read(plan.Script, [])));
        var classified = Classify.Of(before.Elements, after.Elements, change,
            Expect.Value(Patterns.Read(Path.Combine(Repository.Root, "tests", "Golden", "patterns"))),
            Classify.Inputs.None with { Collation = collation });
        return new(before, after, change, collation, sites, Expect.Value(Kernel.Preconditions.Of(before.Elements, after.Elements, change, sites)),
            Kernel.Preconditions.BaselineDrift(plan.Report, after.Elements, change, collation),
            classified.Operations.Select(o => o.Operation).Distinct().ToList() is [var one] ? one : null);
    }

    /// <summary>The prediction on the target: each precondition answered from the change or counted there, then the outcome, or the
    /// error that refuses it.</summary>
    public Prediction Predicted(SqlServer.Database target, SqlServer.QueryLog log)
    {
        var placed = Preconditions.Select(p => Expect.Value(Coordinates.Of(p, Change, Base.Elements, Collation))).ToList();
        var queries = Expect.Value(AggregateQueries.Of(placed.OfType<Coordinates.Counted>(), Head.Elements, Base.Elements, Collation,
            SqlServer.Session.Assumed));
        var counts = queries.Tables.Select(t => Expect.Value(AggregateQueries.Count(target, t, log))).ToList();
        var existing = ExistingData.Of(placed.OfType<Coordinates.Answered>().Select(a => a.Count).Concat(counts.SelectMany(c => c.Counted)));
        var unanswered = queries.NotPredictable.Concat(counts.SelectMany(c => c.Unanswered)).Select(u => u.Precondition + ": " + u.Why);
        var outcome = Outcome.Predict(existing, Sites, Preconditions, Drift, Operation).Match(
            o => o.Match(
                applies => "applies" + Drifted(applies.BaselineDrift),
                blocks => "blocks by " + string.Join(", ", blocks.Because.Select(s => s.Precondition.ToString())) + "; "
                    + blocks.Shipping.Match(() => "ships in two releases", operation => "ships as " + operation, () => "prove it")
                    + Drifted(blocks.BaselineDrift)),
            error => "refused " + error.Code);
        return new([.. unanswered], outcome);
    }

    private static string Drifted(SortedArray<PlanOperation> drift) =>
        drift.Count == 0 ? "" : "; baseline drift " + string.Join(", ", drift.Select(o => o.Kind + " " + o.Key));
}

/// <summary>A prediction on one copy: the preconditions no read-only query counted, with why, and the outcome as predict.txt writes
/// it.</summary>
internal sealed record Prediction(IReadOnlyList<string> Unanswered, string Outcome);

/// <summary>
/// The golden project's base published once, by DacFx 170.5.96 under the pipeline profile, to a database of the fixture's SQL Server,
/// reached as env:corpus through a connection file only the owner reads; and the query log the counts write.
/// </summary>
public sealed class CorpusCopy : IAsyncLifetime
{
    /// <summary>Every row of every user table deleted; the base holds no foreign key, so no order is needed.</summary>
    private const string Delete = "DECLARE @sql nvarchar(max) = N'';\n"
        + "SELECT @sql += N'DELETE FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N'; '\n"
        + "FROM sys.tables AS t JOIN sys.schemas AS s ON s.schema_id = t.schema_id WHERE t.is_ms_shipped = 0;\n"
        + "EXEC sys.sp_executesql @sql;";

    private readonly ScratchFolder folder = ScratchFolder.UnderRepository("corpus-copy");

    private RegisteredDatabase database = null!;

    internal SqlServer.Database Target { get; private set; } = null!;

    internal SqlServer.QueryLog Log { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        database = await SqlServerFixture.RegisterAsync();
        GoldenProject.Publish(await GoldenProject.Built(), database, GoldenProject.Pipeline());
        var file = folder.File("corpus.connection", database.ConnectionString);
        if (!OperatingSystem.IsWindows())
        {
            // io/SqlServer refuses a connection file others can read
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        EnvironmentsJson.Of("corpus", new("file:" + file.Replace('\\', '/'), "localhost", Classification: "real")).WriteTo(folder.Path);
        File.Copy(GoldenProject.Profile, Path.Combine(folder.Path, "dbchange", "profiles", "pipeline.publish.xml"), overwrite: true);
        Target = Expect.Value(SqlServer.Resolve(Expect.Value(SqlServer.Target("env:corpus", "--target")), folder.Path));
        Log = SqlServer.QueryLog.Start(folder.Path);
    }

    /// <summary>The copy emptied of rows.</summary>
    internal Task Emptied() => Run(Delete);

    /// <summary>A script run on the copy as the fixture's administrator, in one batch.</summary>
    internal Task Run(string script) => SqlServerFixture.ExecuteAsync(database.ConnectionString, script);

    public async Task DisposeAsync()
    {
        await database.DisposeAsync();
        folder.Dispose();
    }
}
